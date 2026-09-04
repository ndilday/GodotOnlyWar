using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Turns;
using OnlyWar.Models;
using OnlyWar.Models.FactionBehaviors;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using System.Collections.Generic;
using System.Linq;
using System;
using OnlyWar.Helpers.Strategy;
using StrategyPotentialOffensive = OnlyWar.Helpers.Strategy.PotentialOffensive;

public class FactionStrategyController
{
    private readonly IRNG _random;
    private readonly FactionBehaviorRulesProfile _behaviorRules;
    private readonly bool _hasExplicitDependencies;
    private readonly FactionReinforcementPlanner _reinforcementPlanner = new();
    private readonly FactionDevelopmentPlanner _developmentPlanner = new();
    private readonly FactionConsumptionPlanner _consumptionPlanner = new();
    private readonly FactionReconPatrolPlanner _reconPatrolPlanner = new();
    private readonly FactionOffensiveOrderBuilder _offensiveOrderBuilder = new();

    /// <summary>
    /// Legacy adapter. Defaults are resolved when planning starts so the global campaign data is not
    /// captured by constructing the facade early.
    /// </summary>
    public FactionStrategyController()
    {
    }

    /// <summary>
    /// Explicit planning dependencies used by session-owned production callers and isolated tests.
    /// A missing behavior profile is intentionally allowed; the existing ratio fallback remains in
    /// the capability-specific decision below.
    /// </summary>
    internal FactionStrategyController(
        IRNG random,
        FactionBehaviorRulesProfile behaviorRules)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _behaviorRules = behaviorRules;
        _hasExplicitDependencies = true;
    }

    private const int MaxMissionPlanningIterations = 24;

    // When defensiveOnly is set (the Imperial PDF / default faction — PRD §4.24), the faction plans
    // only to hold: it raises fortifications and listening posts, and under assault may run defensive
    // recon and standing patrols, but it launches no offensive missions. Massed counterattack is
    // reserved for the stronger Imperial Guard (§6.4); a bare PDF holds the line and buys time.
    //
    // When onlyPlanet is supplied the faction plans for that single world only (the opening-scenario
    // stamp's planet-scoped simulation — Design/Reference/OpeningScenario.md); otherwise it plans across
    // every world in the sector as it does each turn.
    public List<Order> GenerateFactionOrders(Faction faction, Sector sector, Planet onlyPlanet = null, bool defensiveOnly = false)
    {
        var allNewOrders = new List<Order>();
        IRNG random = _hasExplicitDependencies ? _random : StaticRNG.Instance;
        FactionBehaviorRulesProfile behaviorRules = _hasExplicitDependencies
            ? _behaviorRules
            : GameDataSingleton.Instance?.GameRulesData?.FactionBehaviorRules;

        // Discard last turn's transient screens and recon parties before planning this turn's (they
        // are not persisted roster squads, so they would otherwise pile up in the regions'
        // LandedSquads).
        FactionReconPatrolPlanner.ClearStaleTransientSquads(faction, sector);
        FactionOffensiveEvaluator offensiveEvaluator = new(behaviorRules);

        if (onlyPlanet != null)
        {
            if (onlyPlanet.RelationshipLedger != null)
            {
                FactionIntelligenceService.ObservePublicActivity(onlyPlanet, 0);
            }
            GeneratePlanetOrders(
                faction, onlyPlanet, defensiveOnly, allNewOrders, random, offensiveEvaluator);
        }
        else
        {
            foreach (var planet in sector.Planets.Values)
            {
                if (planet.RelationshipLedger != null)
                {
                    FactionIntelligenceService.ObservePublicActivity(planet, 0);
                }
                GeneratePlanetOrders(
                    faction, planet, defensiveOnly, allNewOrders, random, offensiveEvaluator);
            }
        }

        return allNewOrders;
    }

    private void GeneratePlanetOrders(
        Faction faction,
        Planet planet,
        bool defensiveOnly,
        List<Order> allNewOrders,
        IRNG random,
        FactionOffensiveEvaluator offensiveEvaluator)
    {
        var factionRegionsOnPlanet = planet.Regions
                                           .SelectMany(r => r.RegionFactionMap.Values)
                                           .Where(rf => rf.PlanetFaction.Faction == faction && rf.IsPublic)
                                           .ToList();

        if (!factionRegionsOnPlanet.Any()) return;

        // PRIORITY 1: ASSESS FORCES AND GARRISON NEEDS
        var regionalForceStates = new List<RegionForceState>();
        foreach (var regionFaction in factionRegionsOnPlanet)
        {
            long requiredDefensiveBattleValue =
                FactionThreatAssessment.CalculateRequiredDefensiveBattleValue(regionFaction);
            long organizedTroops = regionFaction.GetDeployedStrength();
            long spareTroops = Math.Max(0, organizedTroops - requiredDefensiveBattleValue);
            long defensiveShortfall = Math.Max(0, requiredDefensiveBattleValue - organizedTroops);
            // The requirement is a want and is deliberately unbounded (it is derived from the enemy
            // strength next door, not from this region's own army), so what the region actually
            // commits is the want clamped to the troops that exist. Persisting it on the region
            // faction is the point of the clamp: the tactical assault path materialises the defence
            // days after this planning pass, and reading the raw want there let a region field several
            // times its entire organized strength in soldiers generated from nothing.
            long assignedDefensiveBattleValue = Math.Min(organizedTroops, requiredDefensiveBattleValue);
            regionFaction.AssignedDefensiveBattleValue = assignedDefensiveBattleValue;
            regionalForceStates.Add(new RegionForceState(
                regionFaction, requiredDefensiveBattleValue, assignedDefensiveBattleValue,
                spareTroops, defensiveShortfall));
        }

        long organizedTotal = factionRegionsOnPlanet
            .Sum(regionFaction => regionFaction.GetDeployedStrength());
        GameLog.Debug(() =>
            $"AI plan {faction.Name}/{planet.Name}: posture={(defensiveOnly ? "defensive" : "offensive")}, "
            + $"regions={factionRegionsOnPlanet.Count}, organized={organizedTotal}, "
            + $"requiredDefensiveBv={regionalForceStates.Sum(s => s.RequiredDefensiveBattleValue)}, spare={regionalForceStates.Sum(s => s.SpareTroops)}");
        GameLog.Trace(() =>
            $"AI plan {faction.Name}/{planet.Name}: force states "
            + string.Join("; ", regionalForceStates.Select(s =>
                $"{s.RegionFaction.Region.Name}:pop={s.RegionFaction.Population},mil={s.RegionFaction.MilitaryStrength},"
                + $"org={s.RegionFaction.Organization},required={s.RequiredDefensiveBattleValue},spare={s.SpareTroops}")));

        if (defensiveOnly)
        {
            bool underAssault = planet.IsUnderAssault();
            int beforeOrders = allNewOrders.Count;
            if (planet.IsUnderAssault())
            {
                // Under assault: dig in fully — fortifications, listening posts, organization —
                // then post a standing patrol screen and scout the enemy regions pressing the border
                // so the PDF fights informed rather than blind. A defensive posture still never
                // launches an assault of its own. Development uses the same benefit-per-cost
                // allocator as an offensive faction's; only the surrounding posture differs.
                _developmentPlanner.GenerateEfficientDevelopmentOrders(
                    faction, regionalForceStates, allNewOrders);
                PlanDefensiveReconOnPlanet(
                    faction, planet, regionalForceStates, allNewOrders, random, offensiveEvaluator);
                _reconPatrolPlanner.PlanPatrolMissionsOnPlanet(
                    faction, planet, regionalForceStates, allNewOrders, random);
            }
            else
            {
                // Not yet formally under assault, but a PDF facing an enemy massing across a
                // border raises listening posts there so it is not blind when the assault lands.
                // Sensors only, and only on threatened borders — no fortifying quiet worlds, no
                // maneuver (PRD §4.24).
                _developmentPlanner.GenerateBorderListeningPosts(
                    faction, regionalForceStates, allNewOrders);
            }
            GameLog.Debug(() =>
                $"AI plan {faction.Name}/{planet.Name}: defensive choice="
                + $"{(underAssault ? "under assault; full development" : "border listening posts")}, "
                + $"ordersAdded={allNewOrders.Count - beforeOrders}, "
                + $"construction={SummarizeConstructionOrders(allNewOrders.Skip(beforeOrders))}");
            return;
        }

        GameLog.Trace(() => $"    plan {faction.Name}/{planet.Name}: {factionRegionsOnPlanet.Count} regions, "
            + $"spareTroops={regionalForceStates.Sum(s => s.SpareTroops)}");

        // PRIORITY 2: PLAN REGIONAL MISSIONS
        PlanRegionalMissionsOnPlanet(
            faction, planet, regionalForceStates, allNewOrders, random, offensiveEvaluator);
        GameLog.Trace(() => $"    plan {faction.Name}/{planet.Name}: regional missions done ({allNewOrders.Count} orders)");

        // PRIORITY 3: PLAN DEVELOPMENT
        if (FactionThreatAssessment.HasPublicEnemyOnPlanet(faction, planet))
        {
            _developmentPlanner.GenerateEfficientDevelopmentOrders(
                faction, regionalForceStates, allNewOrders);
            GameLog.Trace(() => $"    plan {faction.Name}/{planet.Name}: development done ({allNewOrders.Count} orders)");
        }
        else
        {
            GameLog.Debug(() =>
                $"AI plan {faction.Name}/{planet.Name}: development skipped; no public enemy threat on planet");
        }

        // PRIORITY 4: PLAN PATROL MISSIONS
        _reconPatrolPlanner.PlanPatrolMissionsOnPlanet(
            faction, planet, regionalForceStates, allNewOrders, random);
        GameLog.Trace(() => $"    plan {faction.Name}/{planet.Name}: patrols done ({allNewOrders.Count} orders)");

        // PRIORITY 5/6: PLAN SWARM OPERATIONS
        // A Consumption faction spends what is left on spreading and then feeding. They come last so
        // both receive the true residual - what survives the defensive reserve, offensives,
        // development and the patrol screen - and spreading precedes feeding because a consumer on the
        // move is not grazing.
        if (faction.GrowthType == GrowthType.Consumption)
        {
            _consumptionPlanner.PlanConsumptionExpansionOnPlanet(faction, planet, regionalForceStates);
            _consumptionPlanner.PlanFeedMissionsOnPlanet(
                faction, planet, regionalForceStates, allNewOrders);
            GameLog.Trace(() => $"    plan {faction.Name}/{planet.Name}: consumption operations done ({allNewOrders.Count} orders)");
        }
    }

    // Defensive reconnaissance: a purely-defensive faction (the PDF under assault) never assaults,
    // but it does scout the enemy regions massing on its borders — the recon-only slice of the same
    // targeting machinery. The intel it gains sharpens its garrison sizing against those neighbours
    // (CalculateRequiredDefensiveBattleValue) and denies attackers the from-within surprise edge.
    private void PlanDefensiveReconOnPlanet(
        Faction faction,
        Planet planet,
        List<RegionForceState> states,
        List<Order> allOrders,
        IRNG random,
        FactionOffensiveEvaluator offensiveEvaluator)
    {
        List<StrategyPotentialOffensive> potentialTargets = offensiveEvaluator
            .IdentifyPotentialOffensivesOnPlanet(faction, planet, states);
        StrategyPotentialOffensive reconTarget = FactionOffensiveEvaluator.ChooseReconTarget(
            potentialTargets.Where(o => !FactionOffensiveEvaluator.IsWellReconnoitred(o, faction.Id)).ToList());
        if (reconTarget != null)
        {
            _reconPatrolPlanner.IssueReconMission(faction, reconTarget, allOrders, random);
        }
    }

    private void PlanRegionalMissionsOnPlanet(
        Faction faction,
        Planet planet,
        List<RegionForceState> regionalForceStates,
        List<Order> allOrders,
        IRNG random,
        FactionOffensiveEvaluator offensiveEvaluator)
    {
        _reinforcementPlanner.PlanGarrisonReinforcement(faction, planet, regionalForceStates);
        _reinforcementPlanner.PlanFrontReinforcement(faction, planet, regionalForceStates);
        HashSet<string> plannedTargets = new();

        for (int i = 0; i < MaxMissionPlanningIterations; i++)
        {
            List<StrategyPotentialOffensive> potentialOffensives =
                offensiveEvaluator.IdentifyPotentialOffensivesOnPlanet(
                    faction, planet, regionalForceStates);
            offensiveEvaluator.LogPotentialOffensives(faction, planet, potentialOffensives);

            MissionCandidate candidate = offensiveEvaluator.ChooseBestMissionCandidate(
                faction, potentialOffensives, plannedTargets);
            if (candidate == null) break;

            bool issued = candidate.Plan switch
            {
                OffensivePlan.Assault => _offensiveOrderBuilder.IssueAssault(
                    faction, candidate.Offensive, regionalForceStates, allOrders, random),
                OffensivePlan.Raid => _offensiveOrderBuilder.IssueLightningRaid(
                    faction, candidate.Offensive, regionalForceStates, allOrders, random),
                OffensivePlan.Recon => _reconPatrolPlanner.IssueReconMission(
                    faction, candidate.Offensive, regionalForceStates, allOrders, random),
                _ => false
            };

            if (!issued) break;
            plannedTargets.Add(FactionOffensiveEvaluator.MissionTargetKey(candidate.Offensive));
        }
    }

    private static string SummarizeConstructionOrders(IEnumerable<Order> orders)
    {
        List<ConstructionMission> missions = orders
            .Select(o => o.Mission)
            .OfType<ConstructionMission>()
            .ToList();
        if (missions.Count == 0) return "none";

        return string.Join(", ", missions
            .GroupBy(m => m.ConstructionType)
            .Select(g => $"{g.Key}+{g.Sum(m => m.BuildAmount):F2} ({g.Count()} orders)"));
    }

}
