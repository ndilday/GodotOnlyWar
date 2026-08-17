using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Fortifications;
using OnlyWar.Helpers.StrategicCombat;
using OnlyWar.Helpers.Turns;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System.Collections.Generic;
using System.Linq;
using System;

public class FactionStrategyController
{
    // Reward/risk offensive-targeting tunables (PRD §4.24). Kept here rather than in TurnController
    // because they govern strategic AI planning, not the biomass turn resolution.
    // Each point of enemy Entrenchment multiplies the effective cost of assaulting the region.
    internal const double EntrenchmentRiskFactor = 0.5;
    // Force-ratio edge the attacker insists on over its (estimated) defender before committing.
    internal const double OffensiveForceRatioThreshold = 1.5;
    // 1-sigma error on the attacker's estimate of defender strength with zero intelligence on the
    // target. It shrinks as the attacker's per-target intelligence (belief, raised by recon) rises
    // — an interim stand-in for the full per-faction intelligence-as-belief model (PRD §4.21), so
    // the AI is not omniscient.
    internal const double BaseDefenderIntelNoise = 0.5;
    // Intelligence a faction must hold on an adjacent enemy before it will commit to an assault
    // rather than reconnoitre it first. Below this the estimate is too noisy to stake force on.
    internal const float ReconIntelThreshold = 1.0f;
    // Caution under uncertainty: rather than betting the go/no-go on a single noisy draw, the AI
    // plans against a pessimistic (upper-confidence) estimate of defender strength — expected value
    // inflated by this many sigma of remaining uncertainty. When it knows little (large sigma) it
    // assumes the defender could be much stronger and demands a bigger margin or scouts first; as its
    // intel sharpens (sigma -> 0) the estimate converges on the truth. ~1 sigma ≈ 84th percentile.
    internal const double DefenderEstimateCautionZ = 1.0;
    // Defender awareness of an adjacent enemy region at which it perceives that region's full threat
    // when sizing its garrison. Below it, a blind defender under-garrisons — until an attack (which
    // grants it intel of the attacker's staging regions) or a deliberate recon opens its eyes.
    internal const float GarrisonFullSightIntel = 2.0f;
    // Below this much own-intel about a region, a faction is scouting ground it does not know and its
    // recon goes in carefully (ChooseReconAggression). Set at half of GarrisonFullSightIntel so the
    // three aggression bands land on the same scale the garrison-sizing threshold already uses,
    // rather than introducing a second, unrelated intel scale.
    internal const float UnfamiliarGroundIntel = 1.0f;
    // Share of a region's deployable strength that is never available for anything else, however quiet
    // the region looks. This is what stops CalculateRequiredDefensiveBattleValue's purely reactive
    // threat term from leaving interior or blind regions with literally no defence. It also costs every
    // faction a fifth of its offensive tempo, which is the intended trade: holding ground has a price.
    internal const double MinimumDefensiveReserveFraction = 0.20;
    // Fraction of a region's leftover spare troops committed as a standing patrol screen (the rest
    // stays available). Pools are 1:1 with battle value now, so this is a direct share, not a ×N.
    internal const double PatrolForceFraction = 0.1;
    // Share of spare troops kept on ordinary policing when there is no declared enemy on the world at
    // all. A faction with a garrison is checking papers and walking beats whether or not it knows it
    // has an enemy, and that alone should make covert ground non-free to cross.
    //
    // This closes the hole where a Chapter operating before it reveals itself met literally no
    // opposition anywhere on a planet (OnlyWar_TDD.md §6.4): the patrol
    // fraction returned a hard zero without a public enemy, and interception now requires a screen at
    // parity, so every intrusion went uncontested. Deliberately far below the 0.10 threatened-border
    // tier - this is policing, not screening - and it needs no new state and no signal the faction has
    // not earned, which is what made it the piece to build first.
    //
    // PlanPatrolMissionsOnPlanet still refuses to post a screen below faction.MinimumForceRequest, so a
    // thin region posts nothing regardless; the floor only buys a patrol where there are troops to spare.
    internal const double PolicingPatrolFraction = 0.05;
    // Combined defense levels (entrenchment + listening post + anti-air) at which a region is worth
    // screening on its own account, with no enemy visible next door. One whole level anywhere means the
    // region has works a saboteur would come for; below that there is nothing on it to wreck.
    internal const double WorthScreeningWorksLevel = 1.0;
    internal const double RaidForceRatioThreshold = 0.25;
    private const double RaidCommitFraction = 0.35;
    private const long MinimumRaidBattleValue = 100;
    private const int MaxMissionPlanningIterations = 24;

    internal class PotentialOffensive
    {
        public Region TargetRegion { get; set; }
        public RegionFaction TargetFaction { get; set; }
        public List<Region> AttackingRegions { get; set; } = new List<Region>();
        // The attacker's committable force, in battle-value points (spare organized troops drawn
        // from the staging regions' pools, 1:1 with headcount — §4.24).
        public long AvailableAttackingForce { get; set; }
        // The biomass the attacker stands to gain from taking the region (target population, plus
        // the land's carrying capacity for a Consumption swarm that eats the ground itself).
        public double Reward { get; set; }
        // True defender strength in battle value (garrison pool + landed squads weighted by their
        // soldiers' battle value, not raw headcount).
        public long DefenderBattleValue { get; set; }
        // What the attacker *believes* the defender is worth, after intel noise; all targeting
        // decisions run off this rather than the true value.
        public long EstimatedDefenderBattleValue { get; set; }
    }

    internal class RegionForceState
    {
        public RegionFaction RegionFaction { get; }
        // The reserve this region holds back to defend itself, in battle value. Named for battle value
        // rather than "garrison" because it is faction-agnostic - see
        // CalculateRequiredDefensiveBattleValue.
        public long RequiredDefensiveBattleValue { get; }
        // What this region can actually field against that requirement: the reserve clamped to the
        // organized troops that exist. RequiredDefensiveBattleValue is a want and may exceed the whole
        // army; this is the commitment, and it is the figure mirrored onto
        // RegionFaction.AssignedDefensiveBattleValue so the tactical assault path materialises the
        // defence that was assigned instead of re-deriving the unbounded want.
        public long AssignedDefensiveBattleValue { get; }
        public long SpareTroops { get; set; }
        // How far this region's organized troops fall short of the reserve it wants (0 when the
        // minimum is met). Tracked alongside SpareTroops so reinforcement can whittle the deficit down
        // as neighbours relocate troops in, rather than recomputing it each transfer.
        public long DefensiveShortfall { get; set; }

        public RegionForceState(
            RegionFaction factionInfo,
            long requiredDefensiveBattleValue,
            long assignedDefensiveBattleValue,
            long spareTroops,
            long defensiveShortfall)
        {
            RegionFaction = factionInfo;
            RequiredDefensiveBattleValue = requiredDefensiveBattleValue;
            AssignedDefensiveBattleValue = assignedDefensiveBattleValue;
            SpareTroops = spareTroops;
            DefensiveShortfall = defensiveShortfall;
        }
    }

    // When defensiveOnly is set (the Imperial PDF / default faction — PRD §4.24), the faction plans
    // only to dig in: it raises fortifications and listening posts to hold regions under assault,
    // but launches no offensives and runs no patrols. Massed counterattack is reserved for the
    // stronger Imperial Guard (§6.4); a bare PDF holds the line and buys time.
    //
    // When onlyPlanet is supplied the faction plans for that single world only (the opening-scenario
    // stamp's planet-scoped simulation — Design/Reference/OpeningScenario.md); otherwise it plans across
    // every world in the sector as it does each turn.
    public List<Order> GenerateFactionOrders(Faction faction, Sector sector, Planet onlyPlanet = null, bool defensiveOnly = false)
    {
        var allNewOrders = new List<Order>();

        // Discard last turn's transient screens and recon parties before planning this turn's (they
        // are not persisted roster squads, so they would otherwise pile up in the regions'
        // LandedSquads).
        ClearStaleTransientSquads(faction, sector);

        if (onlyPlanet != null)
        {
            if (onlyPlanet.RelationshipLedger != null)
            {
                FactionIntelligenceService.ObservePublicActivity(onlyPlanet, 0);
            }
            GeneratePlanetOrders(faction, onlyPlanet, defensiveOnly, allNewOrders);
        }
        else
        {
            foreach (var planet in sector.Planets.Values)
            {
                if (planet.RelationshipLedger != null)
                {
                    FactionIntelligenceService.ObservePublicActivity(planet, 0);
                }
                GeneratePlanetOrders(faction, planet, defensiveOnly, allNewOrders);
            }
        }

        return allNewOrders;
    }

    private void GeneratePlanetOrders(Faction faction, Planet planet, bool defensiveOnly, List<Order> allNewOrders)
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
            long requiredDefensiveBattleValue = CalculateRequiredDefensiveBattleValue(regionFaction);
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
                // then post standing patrols (which also sweep their own ground for awareness) and
                // scout the enemy regions pressing the border so the PDF fights informed rather than
                // blind. A defensive posture still never launches an assault of its own. Development
                // uses the same benefit-per-cost allocator as an offensive faction's; only the
                // surrounding posture (no assaults, no offensive recon) differs.
                GenerateEfficientDevelopmentOrders(faction, regionalForceStates, allNewOrders);
                PlanDefensiveReconOnPlanet(faction, planet, regionalForceStates, allNewOrders);
                PlanPatrolMissionsOnPlanet(faction, planet, regionalForceStates, allNewOrders);
            }
            else
            {
                // Not yet formally under assault, but a PDF facing an enemy massing across a
                // border raises listening posts there so it is not blind when the assault lands.
                // Sensors only, and only on threatened borders — no fortifying quiet worlds, no
                // maneuver (PRD §4.24).
                GenerateBorderListeningPosts(faction, regionalForceStates, allNewOrders);
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
        PlanRegionalMissionsOnPlanet(faction, planet, regionalForceStates, allNewOrders);
        GameLog.Trace(() => $"    plan {faction.Name}/{planet.Name}: regional missions done ({allNewOrders.Count} orders)");

        // PRIORITY 3: PLAN DEVELOPMENT
        if (HasPublicEnemyOnPlanet(faction, planet))
        {
            GenerateEfficientDevelopmentOrders(faction, regionalForceStates, allNewOrders);
            GameLog.Trace(() => $"    plan {faction.Name}/{planet.Name}: development done ({allNewOrders.Count} orders)");
        }
        else
        {
            GameLog.Debug(() =>
                $"AI plan {faction.Name}/{planet.Name}: development skipped; no public enemy threat on planet");
        }

        // PRIORITY 4: PLAN RECON MISSIONS
        PlanPatrolMissionsOnPlanet(faction, planet, regionalForceStates, allNewOrders);
        GameLog.Trace(() => $"    plan {faction.Name}/{planet.Name}: patrols done ({allNewOrders.Count} orders)");

        // PRIORITY 5/6: PLAN SWARM OPERATIONS
        // A Consumption faction spends what is left on spreading and then feeding. They come last so
        // both receive the true residual - what survives the defensive reserve, offensives,
        // development and the patrol screen - and spreading precedes feeding because a swarm on the
        // move is not grazing.
        if (faction.GrowthType == GrowthType.Consumption)
        {
            PlanSwarmExpansionOnPlanet(faction, planet, regionalForceStates);
            PlanFeedMissionsOnPlanet(faction, planet, regionalForceStates, allNewOrders);
            GameLog.Trace(() => $"    plan {faction.Name}/{planet.Name}: swarm operations done ({allNewOrders.Count} orders)");
        }
    }

    /// <summary>
    /// Pushes a share of each region's spare troops onto richer adjacent ground.
    /// </summary>
    /// <remarks>
    /// Expansion used to be a planet-update side effect that helped itself to the swarm's whole
    /// deployed strength, blind to everything the planner had already committed that force to - the
    /// same double-count feeding had, one function along. Applied directly here rather than issued as
    /// an order for the same reason garrison and front reinforcement are: it relocates strength
    /// between regions, and there is no tactical mission to run.
    ///
    /// It is deliberately NOT folded into the ordinary offensive path. Expansion's target is chosen
    /// by biomass, and the richest neighbour is frequently empty ground with a high carrying capacity
    /// and no enemy region-faction in it at all - nothing the offensive machinery could take as a
    /// target. Sharing the budget is what the double-count needed; sharing the code path would have
    /// cost the swarm the moves that matter most.
    /// </remarks>
    private void PlanSwarmExpansionOnPlanet(Faction faction, Planet planet, List<RegionForceState> states)
    {
        foreach (RegionForceState state in states)
        {
            if (state.SpareTroops <= 0) continue;

            (Region destination, long movers) =
                PlanetTurnProcessor.PlanSwarmExpansion(state.RegionFaction, state.SpareTroops);
            if (destination == null || movers <= 0) continue;

            PlanetTurnProcessor.ApplySwarmExpansion(state.RegionFaction, destination, movers);
            state.SpareTroops = Math.Max(0, state.SpareTroops - movers);

            GameLog.Debug(() =>
                $"AI swarm spread {faction.Name}/{planet.Name}: "
                + $"{state.RegionFaction.Region.Name}->{destination.Name}, "
                + $"movers={movers}, sourceSpare={state.SpareTroops}");
        }
    }

    /// <summary>
    /// Commits whatever spare troops a swarm has left to feeding.
    /// </summary>
    /// <remarks>
    /// No <see cref="ForceGenerator"/> call: feeding is squad-less. Materializing squads for a
    /// million-strong swarm would be absurd, and unlike a patrol screen there is nothing for them to
    /// do tactically. The order carries the committed battle value and resolves instantly in the
    /// mission phase.
    /// </remarks>
    private void PlanFeedMissionsOnPlanet(
        Faction faction,
        Planet planet,
        List<RegionForceState> states,
        List<Order> allOrders)
    {
        foreach (RegionForceState state in states)
        {
            if (state.SpareTroops <= 0) continue;

            long committed = state.SpareTroops;
            FeedMission mission = new FeedMission(committed, state.RegionFaction);
            allOrders.Add(new Order(new List<Squad>(), true, false, Aggression.Cautious, mission));
            state.SpareTroops = 0;

            GameLog.Debug(() =>
                $"AI feed {faction.Name}/{planet.Name}/{state.RegionFaction.Region.Name}: "
                + $"committedBV={committed}, deployed={state.RegionFaction.GetDeployedStrength()}, "
                + $"defensiveReserve={state.AssignedDefensiveBattleValue}");
        }
    }

    // Defensive reconnaissance: a purely-defensive faction (the PDF under assault) never assaults,
    // but it does scout the enemy regions massing on its borders — the recon-only slice of the same
    // targeting machinery. The intel it gains sharpens its garrison sizing against those neighbours
    // (CalculateRequiredDefensiveBattleValue) and denies attackers the from-within surprise edge.
    private void PlanDefensiveReconOnPlanet(Faction faction, Planet planet, List<RegionForceState> states, List<Order> allOrders)
    {
        List<PotentialOffensive> potentialTargets = IdentifyPotentialOffensivesOnPlanet(faction, planet, states);
        PotentialOffensive reconTarget = ChooseReconTarget(
            potentialTargets.Where(o => !IsWellReconnoitred(o, faction.Id)).ToList());
        if (reconTarget != null)
        {
            IssueReconMission(faction, reconTarget, allOrders);
        }
    }

    internal enum OffensivePlan { None, Assault, Recon, Raid }

    private void PlanRegionalMissionsOnPlanet(
        Faction faction,
        Planet planet,
        List<RegionForceState> regionalForceStates,
        List<Order> allOrders)
    {
        PlanGarrisonReinforcement(faction, planet, regionalForceStates);
        PlanFrontReinforcement(faction, planet, regionalForceStates);
        HashSet<string> plannedTargets = new();

        for (int i = 0; i < MaxMissionPlanningIterations; i++)
        {
            List<PotentialOffensive> potentialOffensives =
                IdentifyPotentialOffensivesOnPlanet(faction, planet, regionalForceStates);
            LogPotentialOffensives(faction, planet, potentialOffensives);

            MissionCandidate candidate = ChooseBestMissionCandidate(faction, potentialOffensives, plannedTargets);
            if (candidate == null) break;

            bool issued = candidate.Plan switch
            {
                OffensivePlan.Assault => LaunchAssault(faction, candidate.Offensive, regionalForceStates, allOrders),
                OffensivePlan.Raid => LaunchLightningRaid(faction, candidate.Offensive, regionalForceStates, allOrders),
                OffensivePlan.Recon => IssueReconMission(faction, candidate.Offensive, regionalForceStates, allOrders),
                _ => false
            };

            if (!issued) break;
            plannedTargets.Add(MissionTargetKey(candidate.Offensive));
        }
    }

    private class MissionCandidate
    {
        public OffensivePlan Plan { get; set; }
        public PotentialOffensive Offensive { get; set; }
        public double Score { get; set; }
    }

    private MissionCandidate ChooseBestMissionCandidate(
        Faction faction,
        List<PotentialOffensive> offensives,
        HashSet<string> plannedTargets)
    {
        return offensives
            .SelectMany(offensive => BuildMissionCandidates(faction, offensive))
            .Where(candidate => !plannedTargets.Contains(MissionTargetKey(candidate.Offensive)))
            .OrderByDescending(candidate => candidate.Score)
            .FirstOrDefault();
    }

    // Dedup key is the target alone, deliberately not the plan: once any mission (assault, raid, or
    // recon) commits against a region this planning pass, that region is off the table. Keying on the
    // plan too would let an already-assaulted target draw a follow-up raid once its shrinking
    // AvailableAttackingForce dropped below winnable but stayed raid-viable — a duplicate commitment.
    private static string MissionTargetKey(PotentialOffensive offensive) =>
        $"{offensive.TargetRegion.Id}:{offensive.TargetFaction.PlanetFaction.Faction.Id}";

    private IEnumerable<MissionCandidate> BuildMissionCandidates(Faction faction, PotentialOffensive offensive)
    {
        if (offensive.AvailableAttackingForce <= 0) yield break;

        bool wellKnown = IsWellReconnoitred(offensive, faction.Id) || IsLocalOffensive(faction, offensive);
        if (!wellKnown)
        {
            yield return new MissionCandidate
            {
                Plan = OffensivePlan.Recon,
                Offensive = offensive,
                Score = ReconUtility(faction, offensive)
            };
            yield break;
        }

        if (IsWinnable(offensive))
        {
            yield return new MissionCandidate
            {
                Plan = OffensivePlan.Assault,
                Offensive = offensive,
                Score = RewardRiskScore(offensive) * 10.0
            };
        }
        else if (IsRaidViable(offensive))
        {
            yield return new MissionCandidate
            {
                Plan = OffensivePlan.Raid,
                Offensive = offensive,
                Score = RaidUtility(offensive)
            };
        }
    }

    private static bool IsLocalOffensive(Faction faction, PotentialOffensive offensive) =>
        offensive.TargetRegion.RegionFactionMap.ContainsKey(faction.Id);

    private static double ReconUtility(Faction faction, PotentialOffensive offensive)
    {
        double intelGap = Math.Max(0.25, ReconIntelThreshold
            - offensive.TargetRegion.GetFactionRegionAwareness(faction.Id));
        return offensive.Reward * intelGap / Math.Max(offensive.AvailableAttackingForce, 1);
    }

    internal static bool IsRaidViable(PotentialOffensive offensive)
    {
        if (offensive.DefenderBattleValue <= 0) return false;
        long minimum = Math.Max(MinimumRaidBattleValue,
            (long)Math.Ceiling(offensive.EstimatedDefenderBattleValue * RaidForceRatioThreshold));
        return offensive.AvailableAttackingForce >= minimum;
    }

    private static double RaidUtility(PotentialOffensive offensive)
    {
        double expectedDamage = Math.Min(
            offensive.AvailableAttackingForce * RaidCommitFraction,
            Math.Max(1, offensive.EstimatedDefenderBattleValue) * 0.5);
        double risk = Math.Max(1.0, offensive.EstimatedDefenderBattleValue
            * (1.0 + RegionDefenses.GetShared(offensive.TargetFaction, DefenseType.Entrenchment) * EntrenchmentRiskFactor));
        return (offensive.Reward * 0.25 + expectedDamage) / risk;
    }

    // A target is well-reconnoitred once the attacker's intelligence on it clears the threshold;
    // below that the strength estimate is too noisy to stake an assault on.
    internal static bool IsWellReconnoitred(PotentialOffensive offensive, int attackerFactionId) =>
        offensive.TargetRegion.GetFactionRegionAwareness(attackerFactionId) >= ReconIntelThreshold;

    // Among under-known targets, scout the richest first — the objective most worth understanding
    // before committing force.
    internal static PotentialOffensive ChooseReconTarget(List<PotentialOffensive> underKnown) =>
        underKnown.OrderByDescending(o => o.Reward).FirstOrDefault();

    // Sends a reconnaissance force to assess an under-known adjacent enemy region; the mission's
    // result raises this faction's belief about it (TurnController.ApplyMissionResults). The force
    // is drawn but not debited from the pool (a within-turn scouting sortie, as patrols are), and
    // recon is mutually exclusive with an assault on the same planet this turn, so the pool is not
    // double-spent.
    private void IssueReconMission(Faction faction, PotentialOffensive target, List<Order> allOrders)
    {
        IssueReconMission(faction, target, null, allOrders);
    }

    private bool IssueReconMission(
        Faction faction,
        PotentialOffensive target,
        List<RegionForceState> regionalForceStates,
        List<Order> allOrders)
    {
        long requestedBattleValue = Math.Min(target.AvailableAttackingForce, StrategicCombatRules.NpcReconBattleValueCap);
        if (requestedBattleValue <= 0 || target.AvailableAttackingForce < faction.MinimumForceRequest) return false;

        // The recon budget, like any order budget, can be no smaller than the faction's smallest
        // full squad, or the force generator may be unable to produce anything for it.
        requestedBattleValue = Math.Max(requestedBattleValue, faction.MinimumForceRequest);

        var request = new ForceGenerationRequest
        {
            Faction = faction,
            TargetBattleValue = requestedBattleValue,
            Profile = ForceCompositionProfile.AssaultForce
        };
        List<Squad> scouts = ForceGenerator.GenerateForce(request, StaticRNG.Instance);
        if (scouts.Count == 0)
        {
            GameLog.Debug(() =>
                $"AI recon {faction.Name}: target={DescribeOffensive(target)}, requestedBV={request.TargetBattleValue}, "
                + "generated=0; no order created");
            return false;
        }

        Region stagingRegion = ChooseStagingRegionsByOpportunityCost(target, regionalForceStates).FirstOrDefault()
            ?? target.AttackingRegions.First();
        foreach (Squad squad in scouts)
        {
            squad.CurrentRegion = stagingRegion;
        }

        if (regionalForceStates != null)
        {
            RegionForceState state = regionalForceStates.FirstOrDefault(s => s.RegionFaction.Region == stagingRegion);
            if (state != null)
            {
                state.SpareTroops = Math.Max(0, state.SpareTroops - SquadBattleValue(scouts));
            }
        }

        Mission mission = new Mission(MissionType.Recon, target.TargetFaction, 0);
        Aggression reconAggression = ChooseReconAggression(faction, target.TargetRegion);
        Order order = new Order(scouts, true, false, reconAggression, mission);
        allOrders.Add(order);
        GameLog.Debug(() =>
            $"AI recon {faction.Name}: target={DescribeOffensive(target)}, staging={stagingRegion.Name}, "
            + $"requestedBV={request.TargetBattleValue}, generatedSquads={scouts.Count}, "
            + $"generatedSoldiers={scouts.Sum(s => s.Members.Count)}, generatedBV={SquadBattleValue(scouts)}");
        return true;
    }

    /// <summary>
    /// How boldly this faction scouts a region, chosen from how well it already knows the ground.
    /// </summary>
    /// <remarks>
    /// Aggression trades exposure for effect (MissionAggressionModifiers): a Cautious sweep is harder
    /// to spot but learns less, a bold one presses close, learns more, and gets seen. Defaulting it
    /// to a constant meant the AI never made that trade - and defaulting it to Cautious, as this did
    /// originally, had it quietly handicap the one thing it runs recon for, since the intel gathered
    /// is what sharpens its own garrison sizing (GarrisonFullSightIntel).
    ///
    /// The signal is <see cref="RegionExtensions.GetFactionRegionAwareness"/> - what this faction has
    /// already learned about that region - and NOT the region's true watch score. That distinction is
    /// the same one Q4 turns on (OnlyWar_TDD.md §6.4): reading
    /// MissionStealthDifficulty here would hand the planner exact knowledge of an enemy's sensors and
    /// patrol dispositions, which it has no way to hold. Own intel is unambiguously earned, and it is
    /// also the exact term the stealth model already credits the intruder with (its IntelMod), so the
    /// AI is reasoning about an advantage it genuinely has rather than about the answer sheet.
    ///
    /// The resulting progression is the intended one: the first look at unfamiliar ground is careful
    /// and gains a little, and once the faction knows the region its scouts press in close. That also
    /// climbs to GarrisonFullSightIntel faster than a flat Cautious ever would.
    /// </remarks>
    internal static Aggression ChooseReconAggression(Faction faction, Region target)
    {
        float known = target.GetFactionRegionAwareness(faction);
        if (known < UnfamiliarGroundIntel) return Aggression.Cautious;
        if (known < GarrisonFullSightIntel) return Aggression.Normal;
        return Aggression.Attritional;
    }

    private bool LaunchAssault(Faction faction, PotentialOffensive chosenOffensive, List<RegionForceState> regionalForceStates, List<Order> allOrders)
    {
        long intendedBattleValue = (long)(chosenOffensive.DefenderBattleValue * 2);
        List<RegionFaction> emergingSources = chosenOffensive.AttackingRegions
            .Select(region => region.RegionFactionMap.TryGetValue(faction.Id, out RegionFaction rf) ? rf : null)
            .Where(rf => rf?.HasEmergenceAdvantage == true)
            .ToList();
        MissionType missionType = emergingSources.Count > 0 ? MissionType.Ambush : MissionType.Advance;
        bool launched = LaunchOffensive(faction, chosenOffensive, regionalForceStates, allOrders,
            intendedBattleValue, missionType, Aggression.Normal);
        if (launched)
        {
            foreach (RegionFaction source in emergingSources) source.HasEmergenceAdvantage = false;
        }
        return launched;
    }

    private bool LaunchLightningRaid(Faction faction, PotentialOffensive chosenOffensive, List<RegionForceState> regionalForceStates, List<Order> allOrders)
    {
        long minimum = Math.Max(MinimumRaidBattleValue,
            (long)Math.Ceiling(chosenOffensive.EstimatedDefenderBattleValue * RaidForceRatioThreshold));
        long intendedBattleValue = Math.Min(
            chosenOffensive.AvailableAttackingForce,
            Math.Max(minimum, (long)(chosenOffensive.AvailableAttackingForce * RaidCommitFraction)));
        return LaunchOffensive(faction, chosenOffensive, regionalForceStates, allOrders,
            intendedBattleValue, MissionType.LightningRaid, Aggression.Cautious);
    }

    private bool LaunchOffensive(
        Faction faction,
        PotentialOffensive chosenOffensive,
        List<RegionForceState> regionalForceStates,
        List<Order> allOrders,
        long intendedBattleValue,
        MissionType missionType,
        Aggression aggression)
    {
        long totalAvailableForAttack = chosenOffensive.AvailableAttackingForce;
        if (intendedBattleValue <= 0 || totalAvailableForAttack <= 0
            || totalAvailableForAttack < faction.MinimumForceRequest)
        {
            GameLog.Debug(() =>
                $"AI {missionType} {faction.Name}: target={DescribeOffensive(chosenOffensive)}, "
                + $"available={totalAvailableForAttack}, intended={intendedBattleValue}, "
                + $"minimum={faction.MinimumForceRequest}; no order created");
            return false;
        }

        // Never budget less than the faction's smallest full squad: the force generator cannot
        // honor a smaller request, so an offensive sized off a near-dead defender (2x a tiny
        // garrison) would silently produce no force and the target would never be attacked.
        intendedBattleValue = Math.Max(intendedBattleValue, faction.MinimumForceRequest);

        // Commit the force and draw it from each staging region's military pool (Population for a
        // horde, Garrison otherwise), split in proportion to what each region contributed.
        List<StrategicCombatContribution> contributions = CommitAttackingForce(
            chosenOffensive, regionalForceStates, intendedBattleValue);
        long committedBattleValue = contributions.Sum(c => c.BattleValue);
        if (committedBattleValue <= 0)
        {
            GameLog.Debug(() =>
                $"AI {missionType} {faction.Name}: target={DescribeOffensive(chosenOffensive)}, "
                + $"available={totalAvailableForAttack}, intended={intendedBattleValue}; no force could be committed");
            return false;
        }

        bool useStrategicCombat = ShouldUseStrategicCombat(faction, chosenOffensive, committedBattleValue);
        GameLog.Debug(() =>
            $"AI {missionType} {faction.Name}: target={DescribeOffensive(chosenOffensive)}, "
            + $"available={totalAvailableForAttack}, intended={intendedBattleValue}, committed={committedBattleValue}, "
            + $"mode={(useStrategicCombat ? "strategic" : "tactical")}, contributions={DescribeContributions(contributions)}");

        if (useStrategicCombat)
        {
            StrategicCombatMission strategicMission = new(
                chosenOffensive.TargetFaction,
                faction,
                committedBattleValue,
                contributions,
                aggression,
                faction.HasBehavior(FactionBehavior.InvadesOnVictory),
                missionType);
            allOrders.Add(new Order(new List<Squad>(), false, true, aggression, strategicMission));
            return true;
        }

        var request = new ForceGenerationRequest { Faction = faction, TargetBattleValue = committedBattleValue, Profile = ForceCompositionProfile.AssaultForce };
        List<Squad> generatedSquads = ForceGenerator.GenerateForce(request, StaticRNG.Instance);
        if (generatedSquads.Count == 0)
        {
            ReturnCommittedForce(contributions);
            GameLog.Debug(() =>
                $"AI {missionType} {faction.Name}: target={DescribeOffensive(chosenOffensive)}, tactical generation failed; "
                + $"returnedCommitted={committedBattleValue}");
            return false;
        }

        long generatedBattleValue = generatedSquads.Sum(s => s.Members.Sum(m => m.Template.BattleValue));
        if (generatedBattleValue < committedBattleValue)
        {
            ReturnCommittedForceExcess(contributions, committedBattleValue - generatedBattleValue);
            GameLog.Debug(() =>
                $"AI {missionType} {faction.Name}: target={DescribeOffensive(chosenOffensive)}, tactical generation shortfall="
                + $"{committedBattleValue - generatedBattleValue}; generatedBV={generatedBattleValue}");
            committedBattleValue = generatedBattleValue;
        }

        // Record the staging region on the assault force so its survivors know where to withdraw to
        // (raid) — see MissionAftermathProcessor.ResolveOffensiveSurvivors. The primary contributing region
        // stands in for the whole staging effort.
        Region stagingRegion = contributions.OrderByDescending(c => c.BattleValue)
            .Select(c => c.StagingFaction?.Region)
            .FirstOrDefault(r => r != null)
            ?? chosenOffensive.AttackingRegions.First();
        foreach (Squad squad in generatedSquads)
        {
            squad.CurrentRegion = stagingRegion;
        }

        Mission newMission = new Mission(missionType, chosenOffensive.TargetFaction, 0);
        Order newOrder = new Order(generatedSquads, missionType == MissionType.LightningRaid, true, aggression, newMission);
        allOrders.Add(newOrder);
        GameLog.Debug(() =>
            $"AI {missionType} {faction.Name}: tactical order created target={DescribeOffensive(chosenOffensive)}, "
            + $"staging={stagingRegion.Name}, squads={generatedSquads.Count}, soldiers={generatedSquads.Sum(s => s.Members.Count)}, "
            + $"battleValue={generatedBattleValue}");
        return true;
    }

    internal static bool ShouldUseStrategicCombat(Faction attacker, PotentialOffensive offensive, long committedBattleValue)
    {
        if (attacker == null || offensive?.TargetFaction == null) return false;
        if (attacker.IsPlayerFaction || offensive.TargetFaction.PlanetFaction.Faction.IsPlayerFaction) return false;
        if (offensive.TargetFaction.LandedSquads.Any(s => s.Faction?.IsPlayerFaction == true)) return false;
        // Secular insurgents are represented by abstract embedded-PDF and armed-civilian pools;
        // they deliberately have no squad templates to generate for tactical combat.
        if (attacker.GrowthType == GrowthType.Unrest) return true;

        long defenderBattleValue = offensive.DefenderBattleValue > 0
            ? offensive.DefenderBattleValue
            : CalculateDefenderBattleValue(offensive.TargetFaction);

        if (committedBattleValue + defenderBattleValue >= StrategicCombatRules.MassCombatBattleValueFloor)
        {
            return true;
        }

        int estimatedAttackerSquads = EstimateGeneratedSquadCount(attacker, committedBattleValue);
        int estimatedActors = EstimateGeneratedActorCount(attacker, committedBattleValue);
        return estimatedAttackerSquads > StrategicCombatRules.MaxGeneratedSquads
            || estimatedActors > StrategicCombatRules.MaxTacticalActors;
    }

    private static List<StrategicCombatContribution> CommitAttackingForce(
        PotentialOffensive chosenOffensive,
        List<RegionForceState> regionalForceStates,
        long committedBattleValue)
    {
        var contributions = new List<StrategicCombatContribution>();
        long remaining = committedBattleValue;
        List<RegionForceState> contributingStates = ChooseStagingRegionsByOpportunityCost(chosenOffensive, regionalForceStates)
            .Select(region => regionalForceStates.FirstOrDefault(s => s.RegionFaction.Region == region))
            .Where(state => state != null && state.SpareTroops > 0)
            .ToList();

        for (int i = 0; i < contributingStates.Count && remaining > 0; i++)
        {
            RegionForceState state = contributingStates[i];
            long contribution = Math.Min(state.SpareTroops, remaining);
            if (contribution <= 0) continue;

            state.SpareTroops -= contribution;
            state.RegionFaction.RemoveMilitaryStrength(contribution);
            contributions.Add(new StrategicCombatContribution(state.RegionFaction, contribution));
            remaining -= contribution;
        }

        return contributions;
    }

    private static List<Region> ChooseStagingRegionsByOpportunityCost(
        PotentialOffensive offensive,
        List<RegionForceState> regionalForceStates)
    {
        if (regionalForceStates == null) return offensive.AttackingRegions.ToList();

        Faction attacker = regionalForceStates
            .FirstOrDefault(s => offensive.AttackingRegions.Contains(s.RegionFaction.Region))
            ?.RegionFaction.PlanetFaction.Faction;
        if (attacker == null) return offensive.AttackingRegions.ToList();

        return offensive.AttackingRegions
            .Select(region => regionalForceStates.FirstOrDefault(s => s.RegionFaction.Region == region))
            .Where(state => state != null && state.SpareTroops > 0)
            .OrderBy(state => CountReachableEnemyTargets(attacker, state.RegionFaction.Region, offensive.TargetRegion))
            .ThenBy(state => HasLocalEnemyMilitary(attacker, state.RegionFaction.Region) ? 1 : 0)
            .ThenByDescending(state => state.SpareTroops)
            .Select(state => state.RegionFaction.Region)
            .ToList();
    }

    private static int CountReachableEnemyTargets(Faction faction, Region sourceRegion, Region currentTarget)
    {
        return sourceRegion.GetAdjacentRegions()
            .Where(region => region != currentTarget)
            .Count(region => region.RegionFactionMap.Values.Any(rf =>
                rf.IsPublic && FactionRelationshipService.AreHostile(
                    faction, rf.PlanetFaction.Faction, region.Planet)));
    }

    private static void ReturnCommittedForce(IEnumerable<StrategicCombatContribution> contributions)
    {
        foreach (StrategicCombatContribution contribution in contributions)
        {
            contribution.StagingFaction?.AddMilitaryStrength(contribution.BattleValue);
        }
    }

    private static void ReturnCommittedForceExcess(IEnumerable<StrategicCombatContribution> contributions, long excess)
    {
        if (excess <= 0) return;
        StrategicCombatContribution largest = contributions
            .OrderByDescending(c => c.BattleValue)
            .FirstOrDefault();
        largest?.StagingFaction?.AddMilitaryStrength(excess);
    }

    private static int EstimateGeneratedSquadCount(Faction faction, long targetBattleValue)
    {
        int highestTemplateValue = faction.SquadTemplates.Values
            .Where(t => (t.SquadType & SquadTypes.HQ) == 0)
            .Select(t => t.BattleValue)
            .DefaultIfEmpty(0)
            .Max();
        if (highestTemplateValue <= 0) return 0;
        return (int)Math.Ceiling(targetBattleValue / (double)highestTemplateValue);
    }

    private static int EstimateGeneratedActorCount(Faction faction, long targetBattleValue)
    {
        var template = faction.SquadTemplates.Values
            .Where(t => (t.SquadType & SquadTypes.HQ) == 0)
            .OrderByDescending(t => t.BattleValue)
            .FirstOrDefault();
        if (template == null || template.BattleValue <= 0) return 0;
        int squadCount = (int)Math.Ceiling(targetBattleValue / (double)template.BattleValue);
        int actorsPerSquad = template.Elements.Sum(e => e.MaximumNumber);
        return squadCount * actorsPerSquad;
    }

    private static void LogPotentialOffensives(Faction faction, Planet planet, List<PotentialOffensive> offensives)
    {
        GameLog.Debug(() =>
            $"AI plan {faction.Name}/{planet.Name}: offensive candidates={offensives.Count}");
        foreach (PotentialOffensive offensive in offensives)
        {
            GameLog.Trace(() =>
                $"AI candidate {faction.Name}/{planet.Name}: {DescribeOffensive(offensive)}, "
                + $"reward={offensive.Reward:F0}, score={RewardRiskScore(offensive):F2}, "
                + $"intel={offensive.TargetRegion.GetFactionRegionAwareness(faction.Id):F2}/{ReconIntelThreshold:F2}, "
                + $"wellKnown={IsWellReconnoitred(offensive, faction.Id)}, winnable={IsWinnable(offensive)}, "
                + $"staging={string.Join(",", offensive.AttackingRegions.Select(r => r.Name))}");
        }
    }

    private static string DescribeOffensive(PotentialOffensive offensive)
    {
        if (offensive == null) return "none";
        return $"{offensive.TargetRegion.Planet.Name}/{offensive.TargetRegion.Name}/"
            + $"{offensive.TargetFaction.PlanetFaction.Faction.Name} "
            + $"available={offensive.AvailableAttackingForce}, defenderBV={offensive.DefenderBattleValue}, "
            + $"estimatedDefenderBV={offensive.EstimatedDefenderBattleValue}";
    }

    private static string DescribeContributions(IEnumerable<StrategicCombatContribution> contributions)
    {
        var parts = contributions
            .Where(c => c.BattleValue > 0)
            .Select(c => $"{c.StagingFaction?.Region.Name ?? "unknown"}:{c.BattleValue}")
            .ToList();
        return parts.Count == 0 ? "none" : string.Join(",", parts);
    }

    private static long SquadBattleValue(IEnumerable<Squad> squads) =>
        squads.Sum(squad => squad.Members.Sum(member => (long)member.Template.BattleValue));

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

    private class DevelopmentOption
    {
        public DefenseType DefenseType { get; set; }
        public long Cost { get; set; }
        public double Score { get; set; }
    }

    // Iteration guard for the development loop. Each pass either completes a whole level (finitely
    // many bands before the cost cap) or drains a region's budget below the minimum spend, so the
    // loop terminates on its own; the cap is a backstop against degenerate float behavior.
    private const int MaxDevelopmentIterations = 256;
    // Smallest spend (in battle-value troops) worth cutting a development order for; below this a
    // region's leftover trickle stays in its pool rather than becoming dust orders.
    private const long MinimumDevelopmentSpendTroops = 100;

    // The band whose build cost applies while a fractional stat completes its next whole level
    // (the epsilon keeps a level sitting exactly on a boundary from re-pricing into itself).
    private static int CurrentLevelBand(double level) => (int)Math.Floor(Math.Max(0.0, level) + 1e-9);

    private void GenerateEfficientDevelopmentOrders(Faction faction, List<RegionForceState> regionalForceStates, List<Order> allOrders)
    {
        // Project each stat as we plan this turn's builds (the stats themselves only change at
        // resolution — ProcessConstructionOrders). Defense levels are fractional: each pass puts
        // spare force wherever marginal benefit per cost is highest, buying however much of the
        // current level the budget covers rather than being constrained to whole levels.
        Dictionary<RegionForceState, (int Org, double Det, double Ent, double Aa)> projected = regionalForceStates
            .ToDictionary(
                state => state,
                state => (state.RegionFaction.Organization,
                          state.RegionFaction.ListeningPost,
                          state.RegionFaction.Entrenchment,
                          state.RegionFaction.AntiAir));

        for (int i = 0; i < MaxDevelopmentIterations; i++)
        {
            var best = regionalForceStates
                .Where(state => state.SpareTroops >= MinimumDevelopmentSpendTroops)
                .Select(state => (State: state, Option: BestDevelopmentOption(faction, state, projected[state])))
                // Organization is an integer percentage bought in whole points, so it must be
                // affordable outright; the fractional defense stats can always absorb any budget.
                .Where(choice => choice.Option != null
                    && (choice.Option.DefenseType != DefenseType.Organization
                        || choice.Option.Cost * 100L <= choice.State.SpareTroops))
                .OrderByDescending(choice => choice.Option.Score)
                .FirstOrDefault();

            if (best.State == null || best.Option == null) break;

            (int org, double det, double ent, double aa) = projected[best.State];
            ConstructionMission mission;
            long spend;
            if (best.Option.DefenseType == DefenseType.Organization)
            {
                mission = new ConstructionMission(DefenseType.Organization, 1, best.State.RegionFaction);
                spend = best.Option.Cost * 100L;
                org++;
            }
            else
            {
                double level = best.Option.DefenseType switch
                {
                    DefenseType.ListeningPost => det,
                    DefenseType.Entrenchment => ent,
                    _ => aa
                };
                // Buy up to the next whole level at this band's price — less if the budget runs
                // out first; on completing a band the loop re-prices the next one 10x higher.
                double toNextLevel = CurrentLevelBand(level) + 1.0 - level;
                long costPerLevel = best.Option.Cost * 100L;
                double amount = Math.Min(toNextLevel, (double)best.State.SpareTroops / costPerLevel);
                spend = (long)Math.Ceiling(amount * costPerLevel);
                mission = new ConstructionMission(best.Option.DefenseType, amount, best.State.RegionFaction);
                switch (best.Option.DefenseType)
                {
                    case DefenseType.ListeningPost:
                        det += amount;
                        break;
                    case DefenseType.Entrenchment:
                        ent += amount;
                        break;
                    case DefenseType.AntiAir:
                        aa += amount;
                        break;
                }
            }

            allOrders.Add(new Order(new List<Squad>(), true, false, Aggression.Avoid, mission));
            best.State.SpareTroops = Math.Max(0, best.State.SpareTroops - spend);
            projected[best.State] = (org, det, ent, aa);

            GameLog.Trace(() =>
                $"AI efficient construction {best.State.RegionFaction.PlanetFaction.Faction.Name}/"
                + $"{best.State.RegionFaction.Region.Planet.Name}/{best.State.RegionFaction.Region.Name}: "
                + $"{mission.ConstructionType}+{mission.BuildAmount:F2}, spend={spend}, score={best.Option.Score:F2}, "
                + $"spareRemaining={best.State.SpareTroops}");
        }
    }

    private DevelopmentOption BestDevelopmentOption(
        Faction faction,
        RegionForceState state,
        (int Org, double Det, double Ent, double Aa) projected)
    {
        List<DevelopmentOption> options = new();
        RegionFaction rf = state.RegionFaction;
        bool localEnemy = HasLocalEnemyMilitary(faction, rf.Region);
        bool adjacentEnemy = VisibleAdjacentEnemyMilitary(faction, rf.Region) > 0;
        float ownIntel = rf.GetOwnRegionAwareness();

        long orgCost = projected.Org < 100
            ? (long)(Math.Pow(2, projected.Org / 10) * (rf.Population / 10000.0f)) + 1
            : long.MaxValue;
        AddDevelopmentOption(options, DefenseType.Organization, orgCost,
            (100 - projected.Org) / 25.0 + (localEnemy ? 1.0 : 0.0));

        // Cost stays priced off this faction's OWN level - every faction buys construction points
        // at the same rate, so that is the honest price. Benefit is what has to account for allies:
        // points added beside works an ally has already built high move the shared position very
        // little, and without the discount the planner would keep buying cheap levels that change
        // nothing (FortificationMath.SharedContributionEfficiency).
        AddDevelopmentOption(options, DefenseType.ListeningPost, DefenseBuildCost(CurrentLevelBand(projected.Det)),
            (1.0 + Math.Max(0, GarrisonFullSightIntel - ownIntel) + (adjacentEnemy ? 1.5 : 0.0))
                * SharedEfficiency(rf, DefenseType.ListeningPost, projected.Det));

        AddDevelopmentOption(options, DefenseType.Entrenchment, DefenseBuildCost(CurrentLevelBand(projected.Ent)),
            (0.5 + (localEnemy ? 4.0 : 0.0) + (adjacentEnemy ? 2.0 : 0.0))
                * SharedEfficiency(rf, DefenseType.Entrenchment, projected.Ent));

        AddDevelopmentOption(options, DefenseType.AntiAir, DefenseBuildCost(CurrentLevelBand(projected.Aa)),
            (0.25 + (localEnemy || adjacentEnemy ? 0.5 : 0.0))
                * SharedEfficiency(rf, DefenseType.AntiAir, projected.Aa));

        return options
            .Where(option => option.Cost != long.MaxValue)
            .OrderByDescending(option => option.Score)
            .FirstOrDefault();
    }

    // 1.0 whenever this faction stands alone in the region, so a faction with no allied works
    // beside it plans exactly as it did before fortifications became shared.
    private static double SharedEfficiency(
        RegionFaction regionFaction,
        DefenseType defenseType,
        double projectedOwnLevel)
    {
        double alliedPoints = RegionDefenses.GetAlliedPoints(regionFaction, defenseType);
        if (alliedPoints <= 0.0) return 1.0;

        double shared = FortificationMath.PointsToLevel(
            FortificationMath.LevelToPoints(projectedOwnLevel) + alliedPoints);
        return FortificationMath.SharedContributionEfficiency(projectedOwnLevel, shared);
    }

    private static void AddDevelopmentOption(
        List<DevelopmentOption> options,
        DefenseType defenseType,
        long cost,
        double benefit)
    {
        if (cost <= 0 || cost == long.MaxValue) return;
        options.Add(new DevelopmentOption
        {
            DefenseType = defenseType,
            Cost = cost,
            Score = benefit / cost
        });
    }

    // Exponential build cost for a defense stat: baseCost * 10^currentLevel. Computed in long and
    // capped so it can never overflow; at or past DefenseCostCapLevel the cost is effectively
    // infinite, plateauing a defense rather than wrapping negative and spinning the planner.
    private const long DefenseBaseBuildCost = 2;
    private const int DefenseCostCapLevel = 19;
    private static long DefenseBuildCost(int level)
    {
        if (level < 0) level = 0;
        if (level >= DefenseCostCapLevel) return long.MaxValue;

        long cost = DefenseBaseBuildCost;
        for (int i = 0; i < level; i++)
        {
            if (cost > long.MaxValue / 10) return long.MaxValue;
            cost *= 10;
        }

        return cost;
    }

    // Peacetime PDF posture (PRD §4.24): before a world is formally under assault, a PDF that faces
    // a public enemy across a border quietly raises listening posts (Detection) so it is not blind
    // when the assault lands. Sensors only — no fortifications, no maneuver — and only in regions
    // that actually border an enemy, one level per turn; the exponential build cost then self-limits
    // how deep peacetime coverage gets.
    private void GenerateBorderListeningPosts(Faction faction, List<RegionForceState> states, List<Order> allOrders)
    {
        foreach (var state in states)
        {
            if (state.SpareTroops < MinimumDevelopmentSpendTroops) continue;

            bool bordersPublicEnemy = GetBelievedTargets(faction, state.RegionFaction.Region.Planet)
                .Any(target => target.CurrentPresence?.IsPublic == true
                    && state.RegionFaction.Region.GetAdjacentRegions().Contains(target.Region));
            if (!bordersPublicEnemy) continue;

            double level = state.RegionFaction.ListeningPost;
            long detCost = DefenseBuildCost(CurrentLevelBand(level));
            if (detCost == long.MaxValue) continue;

            // Still at most one level per turn, but a region too thin to afford the whole level
            // now builds whatever fraction its spare force covers instead of staying blind.
            long costPerLevel = detCost * 100L;
            double amount = Math.Min(CurrentLevelBand(level) + 1.0 - level,
                (double)state.SpareTroops / costPerLevel);
            long spend = (long)Math.Ceiling(amount * costPerLevel);

            allOrders.Add(new Order(new List<Squad>(), true, false, Aggression.Avoid,
                new ConstructionMission(DefenseType.ListeningPost, amount, state.RegionFaction)));
            state.SpareTroops = Math.Max(0, state.SpareTroops - spend);
            GameLog.Trace(() =>
                $"AI border listening post {faction.Name}/{state.RegionFaction.Region.Planet.Name}/"
                + $"{state.RegionFaction.Region.Name}: Detection+{amount:F2}, spend={spend}, "
                + $"spareRemaining={state.SpareTroops}");
        }
    }

    private bool HasPublicEnemyOnPlanet(Faction faction, Planet planet)
    {
        if (planet?.RelationshipLedger != null)
        {
            return GetBelievedTargets(faction, planet)
                .Any(target => target.CurrentPresence?.IsPublic == true);
        }

        return planet.Regions
            .SelectMany(region => region.RegionFactionMap.Values)
            .Any(regionFaction => regionFaction.IsPublic
                                  && FactionRelationshipService.AreHostile(
                                      faction, regionFaction.PlanetFaction.Faction, planet));
    }

    // Before spare troops are spent on offensives, shore up the line: a region with spare force
    // looks to adjacent friendly regions that cannot meet their own required garrison and relocates
    // troops to cover the shortfall. A region that can't self-garrison is a breach an attacker walks
    // through, so plugging it takes priority over opportunistic attacks (PRD §4.24). A donor only
    // ever gives away what it holds above its own minimum, so it never opens a breach to fill one.
    private void PlanGarrisonReinforcement(Faction faction, Planet planet, List<RegionForceState> states)
    {
        foreach (RegionForceState source in states)
        {
            if (source.SpareTroops <= 0) continue;

            // Adjacent friendly regions still short of their garrison minimum, neediest first.
            List<RegionForceState> needy = source.RegionFaction.Region.GetAdjacentRegions()
                .Select(region => states.FirstOrDefault(s => s.RegionFaction.Region == region))
                .Where(state => state != null && state.DefensiveShortfall > 0)
                .OrderByDescending(state => state.DefensiveShortfall)
                .ToList();

            foreach (RegionForceState destination in needy)
            {
                if (source.SpareTroops <= 0) break;

                long transfer = Math.Min(source.SpareTroops, destination.DefensiveShortfall);
                if (transfer <= 0) continue;

                source.RegionFaction.RemoveMilitaryStrength(transfer);
                destination.RegionFaction.AddMilitaryStrength(transfer);
                source.SpareTroops -= transfer;
                destination.DefensiveShortfall -= transfer;

                GameLog.Debug(() =>
                    $"AI garrison reinforce {faction.Name}/{planet.Name}: "
                    + $"{source.RegionFaction.Region.Name}->{destination.RegionFaction.Region.Name}, "
                    + $"transfer={transfer}, sourceSpare={source.SpareTroops}, "
                    + $"destShortfall={destination.DefensiveShortfall}");
            }
        }
    }

    private void PlanFrontReinforcement(Faction faction, Planet planet, List<RegionForceState> states)
    {
        foreach (RegionForceState source in states.ToList())
        {
            if (source.SpareTroops <= 0) continue;
            if (!HasLocalEnemyCiviliansButNoMilitary(faction, source.RegionFaction.Region)) continue;

            RegionForceState destination = ChooseFrontReinforcementDestination(faction, source, states);
            if (destination == null || destination == source) continue;

            long reserve = Math.Max(source.RequiredDefensiveBattleValue, (long)(source.SpareTroops * 0.30));
            long transfer = Math.Max(0, source.SpareTroops - reserve);
            if (transfer <= 0) continue;

            source.RegionFaction.RemoveMilitaryStrength(transfer);
            destination.RegionFaction.AddMilitaryStrength(transfer);
            source.SpareTroops -= transfer;
            destination.SpareTroops += transfer;

            GameLog.Debug(() =>
                $"AI reinforce {faction.Name}/{planet.Name}: "
                + $"{source.RegionFaction.Region.Name}->{destination.RegionFaction.Region.Name}, "
                + $"transfer={transfer}, sourceSpare={source.SpareTroops}, destSpare={destination.SpareTroops}");
        }
    }

    private RegionForceState ChooseFrontReinforcementDestination(
        Faction faction,
        RegionForceState source,
        List<RegionForceState> states)
    {
        List<RegionForceState> adjacentFriendly = source.RegionFaction.Region.GetAdjacentRegions()
            .Select(region => states.FirstOrDefault(s => s.RegionFaction.Region == region))
            .Where(state => state != null)
            .ToList();
        if (adjacentFriendly.Count == 0) return null;

        return adjacentFriendly
            .OrderByDescending(state => VisibleAdjacentEnemyMilitary(faction, state.RegionFaction.Region))
            .ThenByDescending(state => state.SpareTroops)
            .FirstOrDefault(state => VisibleAdjacentEnemyMilitary(faction, state.RegionFaction.Region) > 0);
    }

    private static bool HasLocalEnemyCiviliansButNoMilitary(Faction faction, Region region)
    {
        if (region?.Planet?.RelationshipLedger != null)
        {
            List<StrategicTarget> believedTargets = GetBelievedTargets(faction, region.Planet)
                .Where(target => target.Region == region && target.CurrentPresence?.IsPublic == true)
                .ToList();
            return believedTargets.Any(target =>
                target.Belief?.EstimatedPopulation > 0
                && (target.Belief?.EstimatedMilitaryStrength ?? 0) <= 0);
        }

        List<RegionFaction> enemies = region.RegionFactionMap.Values
            .Where(rf => rf.IsPublic && FactionRelationshipService.AreHostile(
                faction, rf.PlanetFaction.Faction, region.Planet))
            .ToList();
        return enemies.Any(rf => rf.Population > 0)
               && enemies.All(rf => CalculateDefenderBattleValue(rf) <= 0);
    }

    private static bool HasLocalEnemyMilitary(Faction faction, Region region)
    {
        if (region?.Planet?.RelationshipLedger != null)
        {
            return GetBelievedTargets(faction, region.Planet)
                .Any(target => target.Region == region
                    && target.CurrentPresence?.IsPublic == true
                    && target.Belief?.EstimatedMilitaryStrength > 0);
        }

        return region.RegionFactionMap.Values.Any(rf =>
            rf.IsPublic
            && FactionRelationshipService.AreHostile(faction, rf.PlanetFaction.Faction, region.Planet)
            && CalculateDefenderBattleValue(rf) > 0);
    }

    private static long VisibleAdjacentEnemyMilitary(Faction faction, Region region)
    {
        if (region?.Planet?.RelationshipLedger != null)
        {
            return GetBelievedTargets(faction, region.Planet)
                .Where(target => target.CurrentPresence?.IsPublic == true
                    && target.Region.GetAdjacentRegions().Contains(region))
                .Sum(target => target.Belief?.EstimatedMilitaryStrength ?? 0);
        }

        return region.GetAdjacentRegions()
            .SelectMany(adjacent => adjacent.RegionFactionMap.Values)
            .Where(rf => rf.IsPublic && FactionRelationshipService.AreHostile(
                faction, rf.PlanetFaction.Faction, region.Planet))
            .Sum(CalculateDefenderBattleValue);
    }

    private static IReadOnlyList<StrategicTarget> GetBelievedTargets(
        Faction observerFaction,
        Planet planet,
        IntelLevel minimumLevel = IntelLevel.Confirmed)
    {
        if (planet?.RelationshipLedger == null || observerFaction == null)
        {
            return Array.Empty<StrategicTarget>();
        }

        PlanetFaction observer = planet.PlanetFactionMap.GetValueOrDefault(observerFaction.Id);
        return observer == null
            ? Array.Empty<StrategicTarget>()
            : IntelligenceTargetService.GetTargets(observer, minimumLevel);
    }

    private void PlanPatrolMissionsOnPlanet(Faction faction, Planet planet, List<RegionForceState> regionalForceStates, List<Order> allOrders)
    {
        foreach (var state in regionalForceStates)
        {
            if (state.SpareTroops <= 0) continue;

            double patrolFraction = CalculatePatrolFraction(faction, planet, state);
            long forceBattleValue = (long)(state.SpareTroops * patrolFraction);
            if (forceBattleValue <= 0) continue;

            // A patrol screen is still an order: its budget can be no smaller than the faction's
            // smallest full squad. A region too thin to field even that posts no screen.
            forceBattleValue = Math.Max(forceBattleValue, faction.MinimumForceRequest);
            if (forceBattleValue > state.SpareTroops) continue;

            var request = new ForceGenerationRequest
            {
                Faction = faction,
                TargetBattleValue = forceBattleValue,
                Profile = ForceCompositionProfile.ScoutPatrol // Use a more appropriate profile
            };

            List<Squad> patrolSquads = ForceGenerator.GenerateForce(request, StaticRNG.Instance);
            if (patrolSquads.Count == 0) continue;

            // The patrol is a standing screen, not a sweep: its squads land in the faction's own
            // region and hold, joining the defence if the region is raided (AssembleDefendingForce)
            // and intercepting enemy recon that tries to scout it (TurnController). The order carries
            // the squads but launches no mission of its own (TurnController skips Patrol orders).
            // These squads are transient AI forces, cleared at the start of the next planning pass.
            Mission mission = new Mission(MissionType.Patrol, state.RegionFaction, 0);
            Order order = new Order(patrolSquads, true, false, Aggression.Cautious, mission);
            foreach (Squad squad in patrolSquads)
            {
                squad.CurrentRegion = state.RegionFaction.Region;
                squad.CurrentOrders = order;
                state.RegionFaction.LandedSquads.Add(squad);
            }
            state.SpareTroops = Math.Max(0, state.SpareTroops - SquadBattleValue(patrolSquads));
            allOrders.Add(order);
            GameLog.Debug(() =>
                $"AI patrol {faction.Name}/{planet.Name}/{state.RegionFaction.Region.Name}: "
                + $"targetBV={forceBattleValue}, squads={patrolSquads.Count}, "
                + $"soldiers={patrolSquads.Sum(s => s.Members.Count)}, battleValue={SquadBattleValue(patrolSquads)}");
        }
    }

    /// <summary>
    /// Share of a region's spare troops posted as a standing patrol screen.
    /// </summary>
    /// <remarks>
    /// The two low-own-intel tiers this used to carry are gone. They existed because patrolling raised a
    /// region's own situational awareness, and it no longer does - patrol grants no intelligence at all
    /// (OnlyWar_TDD.md §6.4), so the AI was spending troops on a reward it could
    /// not collect. That motivation now lives where it can actually be paid: the listening-post benefit in
    /// BestDevelopmentOption already scales with the gap below GarrisonFullSightIntel, and recon covers the
    /// rest.
    ///
    /// What patrol is now FOR is counter-infiltration and interception, which is why the last tier exists:
    /// a region with no visible enemy next door can still be worth screening if it is the sort of place
    /// somebody would infiltrate.
    /// </remarks>
    internal double CalculatePatrolFraction(Faction faction, Planet planet, RegionForceState state)
    {
        // No declared enemy anywhere on the world: ordinary policing only. Not zero - see
        // PolicingPatrolFraction. The threat-based tiers below all read visible enemies, so they would
        // return zero here anyway; the works-based tier is allowed to apply, because a region worth
        // infiltrating is worth watching whether or not anyone has declared themselves yet.
        if (!HasPublicEnemyOnPlanet(faction, planet))
        {
            return IsWorthScreening(state.RegionFaction)
                ? PatrolForceFraction
                : PolicingPatrolFraction;
        }

        bool localEnemy = HasLocalEnemyMilitary(faction, state.RegionFaction.Region);
        bool adjacentEnemy = VisibleAdjacentEnemyMilitary(faction, state.RegionFaction.Region) > 0;

        if (localEnemy) return 0.20;
        if (adjacentEnemy) return 0.10;
        if (IsWorthScreening(state.RegionFaction)) return PatrolForceFraction;
        return 0.0;
    }

    /// <summary>
    /// Whether a region has anything on it worth an intruder's trouble, and so is worth screening even
    /// with no enemy massing next door.
    /// </summary>
    /// <remarks>
    /// Reads the faction's own works, which is information it plainly has about itself - deliberately NOT
    /// Region.SpecialMissions, which is the set of opportunities the PLAYER can see and would be an
    /// unearned signal. Works are also scale-free: defense levels are small integers, where a population
    /// threshold would need calibrating against whatever regional populations turn out to be.
    ///
    /// Sabotage opportunities are generated from a region's defenses and assassinations from its
    /// population (PlanetTurnProcessor), so a population term belongs here too eventually; it is left out
    /// until there is a principled threshold to use rather than a guessed one.
    /// </remarks>
    private static bool IsWorthScreening(RegionFaction regionFaction)
    {
        double works = regionFaction.Entrenchment
            + regionFaction.ListeningPost
            + regionFaction.AntiAir;
        return works >= WorthScreeningWorksLevel;
    }

    // Removes the transient patrol squads this faction landed on a previous turn before it plans
    // afresh. Patrol forces are AI-generated screens (not persisted roster squads), so they must be
    // cleared each turn rather than accumulating in the region's LandedSquads.
    /// <summary>
    /// Whether a landed squad is a transient force this controller conjured for one turn's tasking.
    /// </summary>
    /// <remarks>
    /// An NPC faction has no roster: its strength is a battle-value pool on the RegionFaction, and
    /// the squads it fields are generated from nothing each planning pass and thrown away at the
    /// start of the next one. Patrol screens were always cleared this way. Recon parties were not,
    /// and they land: the force is created with only a CurrentRegion, but a party that survives its
    /// week is put into its home RegionFaction.LandedSquads by ExfiltrateMissionStep, where nothing
    /// removed it - so every NPC recon that came home left a permanent ghost squad standing in the
    /// region, inflating its search difficulty (MissionStealthDifficulty counts Patrol and Recon
    /// squads as coverage) for the rest of the campaign.
    ///
    /// Only ever applied to a non-player faction's own RegionFaction, so a player squad on recon -
    /// which IS a roster squad and belongs where it landed - is never touched.
    /// </remarks>
    internal static bool IsTransientAiSquad(Squad squad)
    {
        MissionType? missionType = squad?.CurrentOrders?.Mission?.MissionType;
        return missionType is MissionType.Patrol or MissionType.Recon;
    }

    private void ClearStaleTransientSquads(Faction faction, Sector sector)
    {
        foreach (var planet in sector.Planets.Values)
        {
            foreach (var region in planet.Regions)
            {
                if (region.RegionFactionMap.TryGetValue(faction.Id, out RegionFaction regionFaction))
                {
                    regionFaction.LandedSquads.RemoveAll(IsTransientAiSquad);
                }
            }
        }
    }

    private List<PotentialOffensive> IdentifyPotentialOffensivesOnPlanet(Faction attackingFaction, Planet planet, List<RegionForceState> regionalForceStates)
    {
        PlanetFaction observer = planet.PlanetFactionMap.GetValueOrDefault(attackingFaction.Id);
        if (observer == null) return [];

        List<RegionFaction> allEnemyRegionFactions = IntelligenceTargetService
            .GetTargets(observer, IntelLevel.Confirmed)
            .Select(target => target.CurrentPresence)
            .Where(regionFaction => regionFaction != null && regionFaction.IsPublic)
            .Distinct()
            .ToList();

        var localOffensives = new List<PotentialOffensive>();
        foreach (var targetFaction in allEnemyRegionFactions)
        {
            if (targetFaction.Region.RegionFactionMap.ContainsKey(attackingFaction.Id))
            {
                AddPotentialOffensive(attackingFaction, targetFaction, [targetFaction.Region], regionalForceStates, localOffensives);
            }
        }

        var potentialOffensives = new List<PotentialOffensive>(localOffensives);
        foreach (var targetFaction in allEnemyRegionFactions)
        {
            if (targetFaction.Region.RegionFactionMap.ContainsKey(attackingFaction.Id)) continue;

            var adjacentAttackingRegions = targetFaction.Region.GetAdjacentRegions()
                                                       .Where(r => r.RegionFactionMap.TryGetValue(attackingFaction.Id, out RegionFaction rf)
                                                                   && rf.IsPublic).ToList();
            AddPotentialOffensive(attackingFaction, targetFaction, adjacentAttackingRegions, regionalForceStates, potentialOffensives);
        }
        return potentialOffensives;
    }

    private static void AddPotentialOffensive(
        Faction attackingFaction,
        RegionFaction targetFaction,
        List<Region> attackingRegions,
        List<RegionForceState> regionalForceStates,
        List<PotentialOffensive> potentialOffensives)
    {
        if (!attackingRegions.Any()) return;

        long availableForce = attackingRegions
            .Select(r => regionalForceStates.FirstOrDefault(s => s.RegionFaction.Region == r)?.SpareTroops ?? 0)
            .Sum();

        if (availableForce <= 0) return;

        long defenderBattleValue = StrategicCombatResolver.CalculateDefenderBattleValueAgainst(
            targetFaction, attackingFaction);
        PlanetFaction observer = targetFaction.Region.Planet.PlanetFactionMap
            .GetValueOrDefault(attackingFaction.Id);
        FactionIntelBelief belief = observer?.GetTargetBelief(
            targetFaction.Region,
            targetFaction.PlanetFaction.Faction);
        long estimatedDefenderBattleValue = belief?.EstimatedMilitaryStrength
            ?? defenderBattleValue;
        // Regional awareness remains the planner's recon/readiness signal. The strength estimate
        // itself comes from the stored target belief; it is never rounded from the live target here.
        float intel = belief?.Evidence
            ?? targetFaction.Region.GetFactionRegionAwareness(attackingFaction.Id);

        potentialOffensives.Add(new PotentialOffensive
        {
            TargetRegion = targetFaction.Region,
            TargetFaction = targetFaction,
            AttackingRegions = attackingRegions,
            AvailableAttackingForce = availableForce,
            Reward = CalculateOffensiveReward(targetFaction, attackingFaction, availableForce, defenderBattleValue),
            DefenderBattleValue = defenderBattleValue,
            EstimatedDefenderBattleValue =
                CautiousDefenderEstimate(estimatedDefenderBattleValue, intel)
        });
    }

    /// <summary>
    /// The battle value a region's controller holds back to defend it — the reserve, as an amount
    /// rather than a set of squads. This is the single definition of "how strong is this region's
    /// defence", read both by the planner (to work out what is spare) and by
    /// PrepareAssaultMissionStep (to materialise the defence when the region is actually attacked).
    /// </summary>
    /// <remarks>
    /// Named for battle value, not "garrison", deliberately: <see cref="RegionFaction.Garrison"/> is an
    /// Imperial-specific field that <see cref="Helpers.Simulation.FactionRevealService"/> zeroes when a
    /// cult or revolt reveals, whereas this is faction-agnostic and denominated in the same units as
    /// <see cref="RegionFactionExtensions.GetDeployedStrength"/>. The old name is what led the tactical
    /// assault path to materialise defenders from raw Garrison, so every revealed non-Imperial region
    /// fielded no defence at all.
    /// </remarks>
    internal static long CalculateRequiredDefensiveBattleValue(RegionFaction defender)
    {
        Faction defenderFaction = defender.PlanetFaction.Faction;
        Region region = defender.Region;

        if (region.Planet.RelationshipLedger != null)
        {
            FactionIntelligenceService.ObservePublicActivity(region.Planet, 0);
        }

        long highestThreat = 0;
        foreach (Region adjacentRegion in region.GetAdjacentRegions())
        {
            // The defender's awareness of the adjacent enemy region gates how much of that region's
            // threat it perceives: a blind defender under-reserves because it cannot see what is
            // massing next door. Awareness is opened either by a deliberate recon or — the reactive
            // path — by being attacked from there, which grants intel of the attacker's staging
            // regions (StrategicCombatResolver), so a region that got hit last turn plans around
            // the threat instead of instantly raising a larger combat garrison.
            float sight = Math.Min(1.0f,
                adjacentRegion.GetFactionRegionAwareness(defenderFaction.Id) / GarrisonFullSightIntel);
            if (sight <= 0f) continue;

            long adjacentThreat;
            if (region.Planet.RelationshipLedger != null)
            {
                adjacentThreat = GetBelievedTargets(defenderFaction, region.Planet, IntelLevel.Suspected)
                    .Where(target => target.Region == adjacentRegion
                        && target.CurrentPresence?.IsPublic == true)
                    .Sum(target => (long)((target.Belief?.EstimatedMilitaryStrength ?? 0) * sight));
            }
            else
            {
                // Detached domain fixtures without a sector ledger have no observation boundary;
                // retain their direct combat calculation so the helper remains useful in isolation.
                adjacentThreat = adjacentRegion.RegionFactionMap.Values
                    .Where(rf => FactionRelationshipService.AreHostile(
                        defenderFaction, rf.PlanetFaction.Faction, region.Planet))
                    .Sum(rf => (long)(CalculateDefenderBattleValue(rf) * sight));
            }

            if (adjacentThreat > highestThreat) highestThreat = adjacentThreat;
        }

        // A diversion used to inflate the threat a defender believed it faced here, via
        // RegionFaction.PerceivedThreatBonus. That garrison-inflation channel was dropped along with
        // the pre-planning shaping phase: it required the feint to have already resolved before the
        // enemy planned, which is only possible if diversions run outside the day scheduler. A feint's
        // effect is now tactical and same-day (RegionFaction.CommittedAttention).

        // Floor: nobody leaves ground completely unheld. The threat term above is purely reactive - it
        // is the highest adjacent enemy battle value scaled by how well this defender SEES that
        // neighbour - so it returns zero for an interior region with no armed neighbours, and also for
        // a defender that is simply blind to what is massing next door. Without a floor those regions
        // materialise no defence whatsoever when attacked, and a player learns to walk into quiet
        // interior hexes unopposed.
        long floor = (long)(defender.GetDeployedStrength() * MinimumDefensiveReserveFraction);
        return Math.Max(highestThreat, floor);
    }

    // Pick the best objective by reward-to-risk among the targets the attacker believes it can
    // win, rather than the old pure-ease ranking that would pick a single "easiest" target and
    // then let a downstream strength check veto the whole turn if that one happened to be too
    // strong — ignoring other winnable targets (PRD §4.24).
    internal static PotentialOffensive ChooseBestOffensive(List<PotentialOffensive> offensives)
    {
        return offensives
            .Where(IsWinnable)
            .OrderByDescending(RewardRiskScore)
            .FirstOrDefault();
    }

    // Winnable when the attacker's force exceeds its *estimated* defender strength by the required
    // ratio.
    //
    // A diversion used to bait the commander into accepting worse odds here, shaving this threshold
    // toward parity via RegionFaction.ProvocationLevel. That bait now lives in the diversion itself as
    // Roll B (DemonstrateForceMissionStep): a feint that draws attention risks drawing a real
    // counterattack the same day, resolved from the feinting player's own dice. That is strictly better
    // than nudging a planning threshold - the response lands mid-week instead of a turn later, and the
    // enemy AI makes no decision at all, which is what keeps "plans weekly, resolves daily" intact.
    internal static bool IsWinnable(PotentialOffensive offensive)
    {
        return offensive.AvailableAttackingForce
            > offensive.EstimatedDefenderBattleValue * OffensiveForceRatioThreshold;
    }

    internal static double RewardRiskScore(PotentialOffensive offensive)
    {
        // Risk scales with the estimated defender strength and how dug-in it is: a fortified
        // objective is disproportionately costly to take.
        double risk = offensive.EstimatedDefenderBattleValue
                      * (1.0 + RegionDefenses.GetShared(offensive.TargetFaction, DefenseType.Entrenchment) * EntrenchmentRiskFactor);
        // Provocation from a diversion used to multiply this score, making a feinting region a more
        // tempting target. See IsWinnable for why that channel was replaced by the diversion's own
        // same-day response roll.
        return offensive.Reward / Math.Max(risk, 1.0);
    }

    // Battle-value strength of a defending region faction: its strategic military pool
    // (Population for population-is-military hordes, Garrison for civilian-base factions) plus
    // landed squads weighted by their soldiers' battle value. A landed marine squad is worth far
    // more than its headcount, so counting bodies here would badly understate an elite garrison.
    /// <remarks>
    /// Raw MilitaryStrength, deliberately, and NOT GetDeployedStrength - do not "unify" this with
    /// StrategicCombatResolver.CalculateDefenderBattleValue, which shares its name and its shape but
    /// answers a different question.
    ///
    /// This one is what an ATTACKER ESTIMATES about a defender when deciding whether to attack, and an
    /// attacker does not know how organised its target is or how much of that strength the defender will
    /// actually commit. Pricing the estimate off Organization would hand the planner knowledge it has no
    /// way to hold. The resolver's version is what actually FIGHTS once the assault lands, and that is
    /// correctly limited to what can be fielded.
    ///
    /// Estimation versus resolution. The two are supposed to disagree.
    /// </remarks>
    internal static long CalculateDefenderBattleValue(RegionFaction defender)
    {
        return defender.MilitaryStrength
             + defender.LandedSquads.Sum(s => s.Members.Sum(m => (long)m.Template.BattleValue));
    }

    // The biomass the attacker gains by taking the region: the target population (headcount to
    // kill, convert, or seize) plus — only for a Consumption swarm that devours the land itself —
    // the region's carrying capacity (PRD §4.24).
    internal static double CalculateOffensiveReward(RegionFaction targetFaction, Faction attackingFaction, long availableAttackingForce, long defenderForce)
    {
        double reward = targetFaction.Population;
        if (attackingFaction.GrowthType == GrowthType.Consumption)
        {
            reward += targetFaction.Region.CarryingCapacity;
        }
        return reward * availableAttackingForce / defenderForce;
    }

    // The defender strength the attacker plans against: not a single noisy draw, but a pessimistic
    // upper-confidence estimate. Expected value is the truth; the AI inflates it by DefenderEstimateCautionZ
    // sigma of the remaining uncertainty, where sigma shrinks as its intel on the target rises. So a
    // blind attacker assumes the worst (and either demands a big margin or scouts first), while a
    // well-informed one trusts the number. Deterministic — the go/no-go no longer hinges on one roll.
    internal static long CautiousDefenderEstimate(long trueBattleValue, float intelLevel)
    {
        double sigma = BaseDefenderIntelNoise / (1.0 + intelLevel);
        double multiplier = 1.0 + DefenderEstimateCautionZ * sigma;
        return (long)Math.Round(trueBattleValue * multiplier);
    }

}
