using OnlyWar.Domain;
using OnlyWar.Domain.Extensions;
using OnlyWar.Campaign.Turns;
using OnlyWar.Domain;
using OnlyWar.Domain.FactionBehaviors;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Planets;
using System.Collections.Generic;
using System.Linq;
using System;
using OnlyWar.Campaign.Strategy;
using OnlyWar.Campaign.Strategy.Allocation;
using StrategyPotentialOffensive = OnlyWar.Campaign.Strategy.PotentialOffensive;
using OnlyWar.Runtime.Allocators;

public class FactionStrategyController
{
    private readonly IRNG _random;
    private readonly FactionBehaviorRulesProfile _behaviorRules;
    private readonly IReadOnlyDictionary<int, ForceDoctrineWeights> _factionDoctrines;
    private readonly FactionDevelopmentPlanner _developmentPlanner;
    private readonly FactionConsumptionPlanner _consumptionPlanner;
    private readonly FactionReconPatrolPlanner _reconPatrolPlanner;
    private readonly FactionOffensiveOrderBuilder _offensiveOrderBuilder;
    private readonly ForceTaskCommitter _committer;

    /// <summary>
    /// Explicit planning dependencies used by session-owned production callers and isolated tests.
    /// A missing behavior profile is intentionally allowed; the existing ratio fallback remains in
    /// the capability-specific decision below.
    /// </summary>
    public FactionStrategyController(
        IRNG random,
        FactionBehaviorRulesProfile behaviorRules = null,
        IPersistentIdAllocator identity = null,
        IReadOnlyDictionary<int, ForceDoctrineWeights> factionDoctrines = null)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _behaviorRules = behaviorRules;
        // Absent doctrine is intentionally allowed, exactly as a missing behavior profile is: detached
        // fixtures and any faction whose row has not been authored fall back to the balanced defaults.
        _factionDoctrines = factionDoctrines;
        identity ??= new PersistentIdAllocator();
        _developmentPlanner = new FactionDevelopmentPlanner(identity);
        _consumptionPlanner = new FactionConsumptionPlanner(identity);
        _reconPatrolPlanner = new FactionReconPatrolPlanner(identity);
        _offensiveOrderBuilder = new FactionOffensiveOrderBuilder(identity: identity);
        _committer = new ForceTaskCommitter(
            _developmentPlanner, _consumptionPlanner, _reconPatrolPlanner, _offensiveOrderBuilder);
    }

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
        IRNG random = _random;
        FactionBehaviorRulesProfile behaviorRules = _behaviorRules;

        // Discard last turn's transient screens and recon parties before planning this turn's (they
        // are not persisted roster squads, so they would otherwise pile up in the regions'
        // LandedSquads).
        FactionReconPatrolPlanner.ClearStaleTransientSquads(faction, sector);
        FactionOffensiveEvaluator offensiveEvaluator = new(behaviorRules, sector?.StrategicInvasionForces);

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

        // Surprise lasts the one planning pass that follows the reveal, spent or not. It is cleared here
        // rather than where it is consumed because an advantage that only expired on USE was an
        // un-decaying bank: a region that revealed and was then frozen out of acting by its own
        // defensive reserve kept the drop on its neighbours indefinitely, and would have cashed it many
        // weeks later the moment it could afford an attack.
        ClearEmergenceAdvantages(faction, sector);
        return allNewOrders;
    }

    private ForceDoctrineWeights DoctrineFor(Faction faction) =>
        _factionDoctrines != null
        && faction != null
        && _factionDoctrines.TryGetValue(faction.Id, out ForceDoctrineWeights doctrine)
            ? doctrine
            : ForceDoctrineWeights.Balanced;

    private static void ClearEmergenceAdvantages(Faction faction, Sector sector)
    {
        foreach (Planet planet in sector.Planets.Values)
        {
            foreach (Region region in planet.Regions)
            {
                if (region.RegionFactionMap.TryGetValue(faction.Id, out RegionFaction regionFaction))
                {
                    regionFaction.HasEmergenceAdvantage = false;
                }
            }
        }
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

        // ASSESS FORCES
        //
        // The defensive reserve is NO LONGER SUBTRACTED HERE. It used to be taken off the top -
        // `spare = organized - required`, with `required` an unbounded want summed over every believed
        // neighbour - and every later policy read the residual. A region facing three enemies therefore
        // reserved its whole strength and then looked, to the offensive evaluator, the patrol planner,
        // the development planner and the staging planner alike, like a region with no troops at all.
        // That is the freeze. Defence is now a task that bids for this budget like any other.
        var regionalForceStates = new List<RegionForceState>();
        foreach (var regionFaction in factionRegionsOnPlanet)
        {
            long requiredDefensiveBattleValue =
                FactionThreatAssessment.CalculateRequiredDefensiveBattleValue(regionFaction);
            long organizedTroops = regionFaction.GetDeployedStrength();
            long defensiveShortfall = Math.Max(0, requiredDefensiveBattleValue - organizedTroops);
            // Cleared before the auction runs, and set again by ForceTaskCommitter from what the
            // region actually committed. It stays persisted, and stays clamped to the troops that
            // exist: the tactical assault path materialises the defence days after this pass, and
            // reading an unbounded want there let a region field several times its entire organized
            // strength in soldiers generated from nothing.
            regionFaction.AssignedDefensiveBattleValue = 0;
            regionalForceStates.Add(new RegionForceState(
                regionFaction, requiredDefensiveBattleValue, 0,
                organizedTroops, defensiveShortfall));
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

        // A defensive faction that is not yet under assault still raises listening posts on threatened
        // borders so it is not blind when the assault lands. Sensors only, and only there - no
        // fortifying quiet worlds and no maneuver (PRD §4.24). This one case stays outside the auction
        // because it is a posture restriction, not an allocation decision.
        if (defensiveOnly && !planet.IsUnderAssault())
        {
            int beforeBorderOrders = allNewOrders.Count;
            // This path never reaches the auction, so it has no Defend task bidding against the build.
            // Hand it the old net-of-reserve figure rather than the region's whole strength, or a world
            // that is merely watched digs in with everything it has.
            foreach (RegionForceState state in regionalForceStates)
            {
                state.SpareTroops = Math.Max(
                    0, state.SpareTroops - state.RequiredDefensiveBattleValue);
            }
            _developmentPlanner.GenerateBorderListeningPosts(
                faction, regionalForceStates, allNewOrders);
            GameLog.Debug(() =>
                $"AI plan {faction.Name}/{planet.Name}: defensive choice=border listening posts, "
                + $"ordersAdded={allNewOrders.Count - beforeBorderOrders}, "
                + $"construction={SummarizeConstructionOrders(allNewOrders.Skip(beforeBorderOrders))}");
            return;
        }

        // ONE AUCTION FOR THE WHOLE PLANET
        //
        // Tasks are keyed by objective rather than by region, which is what lets several regions feed
        // one assault and what lets two regions garrisoning against the same neighbour answer a single
        // shared threat. Bids are ranked by marginal value PER BATTLE VALUE, so a task stops attracting
        // force when it saturates rather than when a fixed priority says the next policy may begin.
        List<StrategyPotentialOffensive> offensives =
            offensiveEvaluator.IdentifyPotentialOffensivesOnPlanet(faction, planet, regionalForceStates);
        offensiveEvaluator.LogPotentialOffensives(faction, planet, offensives);

        SharedThreatLedger threats = new();
        List<ForceTask> tasks = new ForceTaskBuilder(_behaviorRules, DoctrineFor(faction)).Build(
            faction, planet, regionalForceStates, offensives, threats, defensiveOnly);
        List<ForceTaskAward> awards = new ForceAllocationAuction(threats).Run(
            faction, planet, tasks, regionalForceStates);

        _committer.Commit(
            faction, planet, awards, regionalForceStates, allNewOrders, random, DoctrineFor(faction));

        GameLog.Debug(() =>
            $"AI plan {faction.Name}/{planet.Name}: tasks={tasks.Count}, awards={awards.Count}, "
            + $"allocated={awards.Sum(a => a.BattleValue)}, unallocated={regionalForceStates.Sum(s => s.SpareTroops)}, "
            + $"orders={allNewOrders.Count}, spend=[" + SummarizeAwards(awards) + "]");
    }

    // Where the planet's battle value actually went, by task family. Without this the plan line says
    // only that everything was allocated, which is true of a faction that spent its whole army on
    // patrols and of one that stormed three regions alike.
    private static string SummarizeAwards(IEnumerable<ForceTaskAward> awards)
    {
        return string.Join(", ", awards
            .GroupBy(award => award.Task.Kind)
            .OrderByDescending(group => group.Sum(award => award.BattleValue))
            .Select(group => $"{group.Key}={group.Sum(award => award.BattleValue)}"));
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
