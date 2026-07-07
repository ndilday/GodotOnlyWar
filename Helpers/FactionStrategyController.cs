using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.StrategicCombat;
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

    private class RegionForceState
    {
        public RegionFaction RegionFaction { get; }
        public long RequiredGarrison { get; }
        public long SpareTroops { get; set; }

        public RegionForceState(RegionFaction factionInfo, long requiredGarrison, long spareTroops)
        {
            RegionFaction = factionInfo;
            RequiredGarrison = requiredGarrison;
            SpareTroops = spareTroops;
        }
    }

    // When defensiveOnly is set (the Imperial PDF / default faction — PRD §4.24), the faction plans
    // only to dig in: it raises fortifications and listening posts to hold regions under assault,
    // but launches no offensives and runs no patrols. Massed counterattack is reserved for the
    // stronger Imperial Guard (§6.4); a bare PDF holds the line and buys time.
    //
    // When onlyPlanet is supplied the faction plans for that single world only (the opening-scenario
    // stamp's planet-scoped simulation — Design/OpeningScenario.md §4.24); otherwise it plans across
    // every world in the sector as it does each turn.
    public List<Order> GenerateFactionOrders(Faction faction, Sector sector, Planet onlyPlanet = null, bool defensiveOnly = false)
    {
        var allNewOrders = new List<Order>();

        // Discard last turn's transient patrol screens before planning this turn's (they are not
        // persisted roster squads, so they would otherwise pile up in the regions' LandedSquads).
        ClearStalePatrolSquads(faction, sector);

        if (onlyPlanet != null)
        {
            GeneratePlanetOrders(faction, onlyPlanet, defensiveOnly, allNewOrders);
        }
        else
        {
            foreach (var planet in sector.Planets.Values)
            {
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
            long requiredGarrison = CalculateRequiredGarrison(regionFaction.Region);
            long organizedTroops = (long)(regionFaction.Population * regionFaction.Organization / 100.0f);
            long spareTroops = Math.Max(0, organizedTroops - requiredGarrison);
            regionalForceStates.Add(new RegionForceState(regionFaction, requiredGarrison, spareTroops));
        }

        long organizedTotal = factionRegionsOnPlanet
            .Sum(regionFaction => (long)(regionFaction.Population * regionFaction.Organization / 100.0f));
        GameLog.Debug(() =>
            $"AI plan {faction.Name}/{planet.Name}: posture={(defensiveOnly ? "defensive" : "offensive")}, "
            + $"regions={factionRegionsOnPlanet.Count}, organized={organizedTotal}, "
            + $"requiredGarrison={regionalForceStates.Sum(s => s.RequiredGarrison)}, spare={regionalForceStates.Sum(s => s.SpareTroops)}");
        GameLog.Trace(() =>
            $"AI plan {faction.Name}/{planet.Name}: force states "
            + string.Join("; ", regionalForceStates.Select(s =>
                $"{s.RegionFaction.Region.Name}:pop={s.RegionFaction.Population},mil={s.RegionFaction.MilitaryStrength},"
                + $"org={s.RegionFaction.Organization},required={s.RequiredGarrison},spare={s.SpareTroops}")));

        if (defensiveOnly)
        {
            bool underAssault = planet.IsUnderAssault();
            int beforeOrders = allNewOrders.Count;
            if (planet.IsUnderAssault())
            {
                // Under assault: dig in fully — fortifications, listening posts, organization.
                GenerateDevelopmentOrders(regionalForceStates, allNewOrders);
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

        // PRIORITY 2: PLAN MAJOR OFFENSIVE
        PlanMajorOffensiveOnPlanet(faction, planet, regionalForceStates, allNewOrders);
        GameLog.Trace(() => $"    plan {faction.Name}/{planet.Name}: offensive done ({allNewOrders.Count} orders)");

        // PRIORITY 3: PLAN DEVELOPMENT
        GenerateDevelopmentOrders(regionalForceStates, allNewOrders);
        GameLog.Trace(() => $"    plan {faction.Name}/{planet.Name}: development done ({allNewOrders.Count} orders)");

        // PRIORITY 4: PLAN RECON MISSIONS
        PlanPatrolMissionsOnPlanet(faction, planet, regionalForceStates, allNewOrders);
        GameLog.Trace(() => $"    plan {faction.Name}/{planet.Name}: patrols done ({allNewOrders.Count} orders)");
    }

    internal enum OffensivePlan { None, Assault, Recon }

    private void PlanMajorOffensiveOnPlanet(Faction faction, Planet planet, List<RegionForceState> regionalForceStates, List<Order> allOrders)
    {
        var potentialOffensives = IdentifyPotentialOffensivesOnPlanet(faction, planet, regionalForceStates);
        LogPotentialOffensives(faction, planet, potentialOffensives);
        (OffensivePlan plan, PotentialOffensive target) = DecideOffensivePlan(potentialOffensives, faction.Id);

        GameLog.Debug(() =>
            $"AI plan {faction.Name}/{planet.Name}: offensive choice={plan}"
            + (target == null ? "" : $", target={DescribeOffensive(target)}, score={RewardRiskScore(target):F2}, "
                + $"wellKnown={IsWellReconnoitred(target, faction.Id)}, winnable={IsWinnable(target)}"));

        switch (plan)
        {
            case OffensivePlan.Assault:
                LaunchAssault(faction, target, regionalForceStates, allOrders);
                break;
            case OffensivePlan.Recon:
                IssueReconMission(faction, target, allOrders);
                break;
        }
    }

    // The AI commits only to targets it understands: it assaults the best winnable reward-to-risk
    // objective among the well-reconnoitred targets; failing that, it scouts the most promising
    // under-known target so a later turn can decide from knowledge rather than a blind guess; if it
    // neither understands a winnable target nor has anything worth scouting, it holds (PRD §4.24).
    internal static (OffensivePlan Plan, PotentialOffensive Target) DecideOffensivePlan(
        List<PotentialOffensive> offensives, int attackerFactionId)
    {
        if (offensives == null || offensives.Count == 0) return (OffensivePlan.None, null);

        var wellKnown = offensives.Where(o => IsWellReconnoitred(o, attackerFactionId)).ToList();
        PotentialOffensive assault = ChooseBestOffensive(wellKnown);
        if (assault != null) return (OffensivePlan.Assault, assault);

        PotentialOffensive recon = ChooseReconTarget(
            offensives.Where(o => !IsWellReconnoitred(o, attackerFactionId)).ToList());
        if (recon != null) return (OffensivePlan.Recon, recon);

        return (OffensivePlan.None, null);
    }

    // A target is well-reconnoitred once the attacker's intelligence on it clears the threshold;
    // below that the strength estimate is too noisy to stake an assault on.
    internal static bool IsWellReconnoitred(PotentialOffensive offensive, int attackerFactionId) =>
        offensive.TargetFaction.GetObserverIntel(attackerFactionId) >= ReconIntelThreshold;

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
        var request = new ForceGenerationRequest
        {
            Faction = faction,
            TargetBattleValue = Math.Min(target.AvailableAttackingForce, StrategicCombatRules.NpcReconBattleValueCap),
            Profile = ForceCompositionProfile.AssaultForce
        };
        List<Squad> scouts = ForceGenerator.GenerateForce(request);
        if (scouts.Count == 0)
        {
            GameLog.Debug(() =>
                $"AI recon {faction.Name}: target={DescribeOffensive(target)}, requestedBV={request.TargetBattleValue}, "
                + "generated=0; no order created");
            return;
        }

        Region stagingRegion = target.AttackingRegions.First();
        foreach (Squad squad in scouts)
        {
            squad.CurrentRegion = stagingRegion;
        }

        Mission mission = new Mission(MissionType.Recon, target.TargetFaction, 0);
        Order order = new Order(scouts, Disposition.Mobile, true, false, Aggression.Cautious, mission);
        allOrders.Add(order);
        GameLog.Debug(() =>
            $"AI recon {faction.Name}: target={DescribeOffensive(target)}, staging={stagingRegion.Name}, "
            + $"requestedBV={request.TargetBattleValue}, generatedSquads={scouts.Count}, "
            + $"generatedSoldiers={scouts.Sum(s => s.Members.Count)}, generatedBV={SquadBattleValue(scouts)}");
    }

    private void LaunchAssault(Faction faction, PotentialOffensive chosenOffensive, List<RegionForceState> regionalForceStates, List<Order> allOrders)
    {
        long intendedBattleValue = (long)(chosenOffensive.AvailableAttackingForce * (0.5 + (RNG.GetLinearDouble() * 0.25)));
        long totalAvailableForAttack = chosenOffensive.AvailableAttackingForce;
        if (intendedBattleValue <= 0 || totalAvailableForAttack <= 0)
        {
            GameLog.Debug(() =>
                $"AI assault {faction.Name}: target={DescribeOffensive(chosenOffensive)}, "
                + $"available={totalAvailableForAttack}, intended={intendedBattleValue}; no order created");
            return;
        }

        // Commit the force and draw it from each staging region's military pool (Population for a
        // horde, Garrison otherwise), split in proportion to what each region contributed.
        List<StrategicCombatContribution> contributions = CommitAttackingForce(
            chosenOffensive, regionalForceStates, intendedBattleValue);
        long committedBattleValue = contributions.Sum(c => c.BattleValue);
        if (committedBattleValue <= 0)
        {
            GameLog.Debug(() =>
                $"AI assault {faction.Name}: target={DescribeOffensive(chosenOffensive)}, "
                + $"available={totalAvailableForAttack}, intended={intendedBattleValue}; no force could be committed");
            return;
        }

        bool useStrategicCombat = ShouldUseStrategicCombat(faction, chosenOffensive, committedBattleValue);
        GameLog.Debug(() =>
            $"AI assault {faction.Name}: target={DescribeOffensive(chosenOffensive)}, "
            + $"available={totalAvailableForAttack}, intended={intendedBattleValue}, committed={committedBattleValue}, "
            + $"mode={(useStrategicCombat ? "strategic" : "tactical")}, contributions={DescribeContributions(contributions)}");

        if (useStrategicCombat)
        {
            StrategicCombatMission strategicMission = new(
                chosenOffensive.TargetFaction,
                faction,
                committedBattleValue,
                contributions,
                Aggression.Normal,
                faction.InvadesOnVictory);
            allOrders.Add(new Order(new List<Squad>(), Disposition.Mobile, false, true, Aggression.Normal, strategicMission));
            return;
        }

        var request = new ForceGenerationRequest { Faction = faction, TargetBattleValue = committedBattleValue, Profile = ForceCompositionProfile.AssaultForce };
        List<Squad> generatedSquads = ForceGenerator.GenerateForce(request);
        if (generatedSquads.Count == 0)
        {
            ReturnCommittedForce(contributions);
            GameLog.Debug(() =>
                $"AI assault {faction.Name}: target={DescribeOffensive(chosenOffensive)}, tactical generation failed; "
                + $"returnedCommitted={committedBattleValue}");
            return;
        }

        long generatedBattleValue = generatedSquads.Sum(s => s.Members.Sum(m => m.Template.BattleValue));
        if (generatedBattleValue < committedBattleValue)
        {
            ReturnCommittedForceExcess(contributions, committedBattleValue - generatedBattleValue);
            GameLog.Debug(() =>
                $"AI assault {faction.Name}: target={DescribeOffensive(chosenOffensive)}, tactical generation shortfall="
                + $"{committedBattleValue - generatedBattleValue}; generatedBV={generatedBattleValue}");
            committedBattleValue = generatedBattleValue;
        }

        // Record the staging region on the assault force so its survivors know where to withdraw to
        // (raid) — see TurnController.ResolveOffensiveSurvivors. The primary contributing region
        // stands in for the whole staging effort.
        Region stagingRegion = chosenOffensive.AttackingRegions.First();
        foreach (Squad squad in generatedSquads)
        {
            squad.CurrentRegion = stagingRegion;
        }

        Mission newMission = new Mission(MissionType.Advance, chosenOffensive.TargetFaction, 0);
        Order newOrder = new Order(generatedSquads, Disposition.Mobile, false, true, Aggression.Normal, newMission);
        allOrders.Add(newOrder);
        GameLog.Debug(() =>
            $"AI assault {faction.Name}: tactical order created target={DescribeOffensive(chosenOffensive)}, "
            + $"staging={stagingRegion.Name}, squads={generatedSquads.Count}, soldiers={generatedSquads.Sum(s => s.Members.Count)}, "
            + $"battleValue={generatedBattleValue}");
    }

    internal static bool ShouldUseStrategicCombat(Faction attacker, PotentialOffensive offensive, long committedBattleValue)
    {
        if (attacker == null || offensive?.TargetFaction == null) return false;
        if (attacker.IsPlayerFaction || offensive.TargetFaction.PlanetFaction.Faction.IsPlayerFaction) return false;
        if (offensive.TargetFaction.LandedSquads.Any(s => s.Faction?.IsPlayerFaction == true)) return false;

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
        long totalAvailableForAttack = Math.Max(1, chosenOffensive.AvailableAttackingForce);
        long remaining = committedBattleValue;
        List<RegionForceState> contributingStates = chosenOffensive.AttackingRegions
            .Select(region => regionalForceStates.FirstOrDefault(s => s.RegionFaction.Region == region))
            .Where(state => state != null && state.SpareTroops > 0)
            .ToList();

        for (int i = 0; i < contributingStates.Count && remaining > 0; i++)
        {
            RegionForceState state = contributingStates[i];
            long contribution = i == contributingStates.Count - 1
                ? remaining
                : (long)Math.Round(committedBattleValue * (state.SpareTroops / (double)totalAvailableForAttack));
            contribution = Math.Min(contribution, Math.Min(state.SpareTroops, remaining));
            if (contribution <= 0) continue;

            state.SpareTroops -= contribution;
            state.RegionFaction.RemoveMilitaryStrength(contribution);
            contributions.Add(new StrategicCombatContribution(state.RegionFaction, contribution));
            remaining -= contribution;
        }

        return contributions;
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
                + $"intel={offensive.TargetFaction.GetObserverIntel(faction.Id):F2}/{ReconIntelThreshold:F2}, "
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
            .Select(g => $"{g.Key}+{g.Sum(m => m.MissionSize)} ({g.Count()} orders)"));
    }

    private void GenerateDevelopmentOrders(List<RegionForceState> regionalForceStates, List<Order> allOrders)
    {
        foreach (var state in regionalForceStates)
        {
            if (state.SpareTroops <= 0) continue;

            long buildPointsAvailable = state.SpareTroops / 100;

            // Project each defense's level as we plan this turn's builds. The stats themselves are
            // only applied later (ProcessConstructionOrders), so without projecting here the per-level
            // cost stayed constant and the loop poured a region's whole budget into a single defense —
            // and once a level passed ~30, the old (int)2^(level+1) cost overflowed to a NEGATIVE
            // value, so `buildPointsAvailable -= minCost` grew the budget and the loop never
            // terminated (a hang first seen when the opening-scenario sims handed a faction a large
            // spare pool). Projecting the level makes the exponential cost rise as we plan, which
            // self-limits how far a region's defenses can climb in one turn; DefenseBuildCost computes
            // in long and caps out so it can never wrap.
            int org = state.RegionFaction.Organization;
            int det = state.RegionFaction.Detection;
            int ent = state.RegionFaction.Entrenchment;
            int aa = state.RegionFaction.AntiAir;

            while (buildPointsAvailable > 0)
            {
                long orgCost = org < 100
                    ? (long)(Math.Pow(2, org / 10) * (state.RegionFaction.Population / 10000.0f)) + 1
                    : long.MaxValue;
                long detCost = DefenseBuildCost(det);
                long entCost = DefenseBuildCost(ent);
                long aaCost = DefenseBuildCost(aa);

                long minCost = Math.Min(orgCost, Math.Min(detCost, Math.Min(entCost, aaCost)));

                if (minCost > buildPointsAvailable) break;

                ConstructionMission mission;
                if (minCost == orgCost) { mission = new ConstructionMission(DefenseType.Organization, 1, state.RegionFaction); org++; }
                else if (minCost == entCost) { mission = new ConstructionMission(DefenseType.Entrenchment, 1, state.RegionFaction); ent++; }
                else if (minCost == detCost) { mission = new ConstructionMission(DefenseType.Detection, 1, state.RegionFaction); det++; }
                else { mission = new ConstructionMission(DefenseType.AntiAir, 1, state.RegionFaction); aa++; }

                Order devOrder = new Order(new List<Squad>(), Disposition.DugIn, true, false, Aggression.Avoid, mission);
                allOrders.Add(devOrder);

                buildPointsAvailable -= minCost;
                // Deduct the "manpower cost" of this construction from the available pool for this region
                state.SpareTroops -= minCost * 100;
                GameLog.Trace(() =>
                    $"AI construction plan {state.RegionFaction.PlanetFaction.Faction.Name}/"
                    + $"{state.RegionFaction.Region.Planet.Name}/{state.RegionFaction.Region.Name}: "
                    + $"{mission.ConstructionType}+1, cost={minCost}, spareRemaining={state.SpareTroops}");
            }
        }
    }

    // Exponential build cost 2^(level+1) for a defense stat, computed in long and capped so it can
    // never overflow: at or past DefenseCostCapLevel the cost is treated as effectively infinite
    // (unaffordable), which plateaus a defense rather than wrapping to a negative cost and spinning
    // the development planner forever.
    private const int DefenseCostCapLevel = 30;
    private static long DefenseBuildCost(int level)
    {
        return level >= DefenseCostCapLevel ? long.MaxValue : 1L << (level + 1);
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
            if (state.SpareTroops <= 0) continue;

            bool bordersPublicEnemy = state.RegionFaction.Region.GetAdjacentRegions()
                .Any(r => r.RegionFactionMap.Values
                           .Any(rf => rf.IsPublic && AreFactionsEnemies(faction, rf.PlanetFaction.Faction)));
            if (!bordersPublicEnemy) continue;

            long detCost = DefenseBuildCost(state.RegionFaction.Detection);
            if (detCost == long.MaxValue || detCost * 100L > state.SpareTroops) continue;

            allOrders.Add(new Order(new List<Squad>(), Disposition.DugIn, true, false, Aggression.Avoid,
                new ConstructionMission(DefenseType.Detection, 1, state.RegionFaction)));
            state.SpareTroops -= detCost * 100L;
            GameLog.Trace(() =>
                $"AI border listening post {faction.Name}/{state.RegionFaction.Region.Planet.Name}/"
                + $"{state.RegionFaction.Region.Name}: Detection+1, cost={detCost}, spareRemaining={state.SpareTroops}");
        }
    }

    private void PlanPatrolMissionsOnPlanet(Faction faction, Planet planet, List<RegionForceState> regionalForceStates, List<Order> allOrders)
    {
        // This is a new method that runs last, using only the final remaining spare troops.
        foreach (var state in regionalForceStates)
        {
            if (state.SpareTroops <= 0) continue;

            // TODO: do we want to only patrol if adjacent to an enemy force?
            // Check if this region borders an enemy. If not, no need to recon from here.
            /*var enemyNeighbors = state.FactionInfo.Region.GetAdjacentRegions()
                                    .Where(r => r.RegionFactionMap.Values.Any(rf => AreFactionsEnemies(faction, rf.PlanetFaction.Faction) && rf.IsPublic))
                                    .ToList();

            if (!enemyNeighbors.Any()) continue;*/

            // Use the remaining spare troops to form a small patrol force.
            // BattleValue is roughly 10 per troop.
            int forceBattleValue = (int)state.SpareTroops * 10;
            if (forceBattleValue <= 0) continue;

            var request = new ForceGenerationRequest
            {
                Faction = faction,
                TargetBattleValue = forceBattleValue,
                Profile = ForceCompositionProfile.ScoutPatrol // Use a more appropriate profile
            };

            List<Squad> patrolSquads = ForceGenerator.GenerateForce(request);
            if (patrolSquads.Count == 0) continue;

            // The patrol is a standing screen, not a sweep: its squads land in the faction's own
            // region and hold, joining the defence if the region is raided (AssembleDefendingForce)
            // and intercepting enemy recon that tries to scout it (TurnController). The order carries
            // the squads but launches no mission of its own (TurnController skips Patrol orders).
            // These squads are transient AI forces, cleared at the start of the next planning pass.
            Mission mission = new Mission(MissionType.Patrol, state.RegionFaction, 0);
            Order order = new Order(patrolSquads, Disposition.DugIn, true, false, Aggression.Cautious, mission);
            foreach (Squad squad in patrolSquads)
            {
                squad.CurrentRegion = state.RegionFaction.Region;
                squad.CurrentOrders = order;
                state.RegionFaction.LandedSquads.Add(squad);
            }
            allOrders.Add(order);
            GameLog.Debug(() =>
                $"AI patrol {faction.Name}/{planet.Name}/{state.RegionFaction.Region.Name}: "
                + $"targetBV={forceBattleValue}, squads={patrolSquads.Count}, "
                + $"soldiers={patrolSquads.Sum(s => s.Members.Count)}, battleValue={SquadBattleValue(patrolSquads)}");
        }
    }

    // Removes the transient patrol squads this faction landed on a previous turn before it plans
    // afresh. Patrol forces are AI-generated screens (not persisted roster squads), so they must be
    // cleared each turn rather than accumulating in the region's LandedSquads.
    private void ClearStalePatrolSquads(Faction faction, Sector sector)
    {
        foreach (var planet in sector.Planets.Values)
        {
            foreach (var region in planet.Regions)
            {
                if (region.RegionFactionMap.TryGetValue(faction.Id, out RegionFaction regionFaction))
                {
                    regionFaction.LandedSquads.RemoveAll(
                        s => s.CurrentOrders?.Mission.MissionType == MissionType.Patrol);
                }
            }
        }
    }

    private List<PotentialOffensive> IdentifyPotentialOffensivesOnPlanet(Faction attackingFaction, Planet planet, List<RegionForceState> regionalForceStates)
    {
        var potentialOffensives = new List<PotentialOffensive>();
        var allEnemyRegionFactions = planet.Regions.SelectMany(r => r.RegionFactionMap.Values)
                                           .Where(rf => AreFactionsEnemies(attackingFaction, rf.PlanetFaction.Faction) && rf.IsPublic).ToList();

        foreach (var targetFaction in allEnemyRegionFactions)
        {
            var adjacentAttackingRegions = targetFaction.Region.GetAdjacentRegions()
                                                       .Where(r => r.RegionFactionMap.ContainsKey(attackingFaction.Id)).ToList();

            if (adjacentAttackingRegions.Any())
            {
                long availableForce = adjacentAttackingRegions
                    .Select(r => regionalForceStates.FirstOrDefault(s => s.RegionFaction.Region == r)?.SpareTroops ?? 0)
                    .Sum();

                if (availableForce > 0)
                {
                    long defenderBattleValue = CalculateDefenderBattleValue(targetFaction);
                    // The attacker's estimate sharpens with its intelligence on THIS target —
                    // belief it has built by reconnoitring the region (see PlanMajorOffensiveOnPlanet),
                    // not blanket sensor coverage.
                    float intel = targetFaction.GetObserverIntel(attackingFaction.Id);

                    potentialOffensives.Add(new PotentialOffensive
                    {
                        TargetRegion = targetFaction.Region,
                        TargetFaction = targetFaction,
                        AttackingRegions = adjacentAttackingRegions,
                        AvailableAttackingForce = availableForce,
                        Reward = CalculateOffensiveReward(targetFaction, attackingFaction),
                        DefenderBattleValue = defenderBattleValue,
                        EstimatedDefenderBattleValue =
                            ApplyIntelNoise(defenderBattleValue, intel, RNG.NextRandomZValue())
                    });
                }
            }
        }
        return potentialOffensives;
    }

    private long CalculateRequiredGarrison(Region region)
    {
        long highestThreat = 0;
        foreach (var adjacentRegion in region.GetAdjacentRegions())
        {
            var controllingFaction = region.ControllingFaction;
            if (controllingFaction == null) continue; // No one controls this region, no garrison needed against ghosts.

            long adjacentThreat = adjacentRegion.RegionFactionMap.Values
                .Where(rf => AreFactionsEnemies(controllingFaction.PlanetFaction.Faction, rf.PlanetFaction.Faction))
                .Sum(rf => rf.Garrison + rf.LandedSquads.Sum(s => s.Members.Count));

            if (adjacentThreat > highestThreat) highestThreat = adjacentThreat;
        }

        // A diversion feinting against this region inflates the threat its defender believes it
        // faces, causing it to hold a larger garrison than the real enemy force would warrant.
        var defender = region.ControllingFaction;
        if (defender != null && defender.PerceivedThreatBonus > 0)
        {
            highestThreat += (long)defender.PerceivedThreatBonus;
        }

        return highestThreat;
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
    // ratio. A successful diversion baits the commander into accepting worse odds, shaving the
    // edge it insists on down toward parity.
    internal static bool IsWinnable(PotentialOffensive offensive)
    {
        double ratioThreshold = OffensiveForceRatioThreshold;
        if (offensive.TargetFaction.ProvocationLevel > 0)
        {
            ratioThreshold = Math.Max(1.0, OffensiveForceRatioThreshold - offensive.TargetFaction.ProvocationLevel * 0.1);
        }
        return offensive.AvailableAttackingForce > offensive.EstimatedDefenderBattleValue * ratioThreshold;
    }

    internal static double RewardRiskScore(PotentialOffensive offensive)
    {
        // Risk scales with the estimated defender strength and how dug-in it is: a fortified
        // objective is disproportionately costly to take.
        double risk = offensive.EstimatedDefenderBattleValue
                      * (1.0 + offensive.TargetFaction.Entrenchment * EntrenchmentRiskFactor);
        double score = offensive.Reward / Math.Max(risk, 1.0);
        // Provocation from a diversion makes the feinting region a more tempting target.
        return score * (1.0 + offensive.TargetFaction.ProvocationLevel * 0.1);
    }

    // Battle-value strength of a defending region faction: its strategic military pool
    // (Population for population-is-military hordes, Garrison for civilian-base factions) plus
    // landed squads weighted by their soldiers' battle value. A landed marine squad is worth far
    // more than its headcount, so counting bodies here would badly understate an elite garrison.
    internal static long CalculateDefenderBattleValue(RegionFaction defender)
    {
        return defender.MilitaryStrength
             + defender.LandedSquads.Sum(s => s.Members.Sum(m => (long)m.Template.BattleValue));
    }

    // The biomass the attacker gains by taking the region: the target population (headcount to
    // kill, convert, or seize) plus — only for a Consumption swarm that devours the land itself —
    // the region's carrying capacity (PRD §4.24).
    internal static double CalculateOffensiveReward(RegionFaction targetFaction, Faction attackingFaction)
    {
        double reward = targetFaction.Population;
        if (attackingFaction.GrowthType == GrowthType.Consumption)
        {
            reward += targetFaction.Region.CarryingCapacity;
        }
        return reward;
    }

    // Fuzz the true defender strength into what the attacker believes, with a 1-sigma error that
    // shrinks as its intelligence on the target improves. The z-value is supplied by the caller so
    // the noise is unit-testable; production passes a standard-normal draw. Interim stand-in for the
    // per-faction intelligence-as-belief model (§4.21).
    internal static long ApplyIntelNoise(long trueBattleValue, float intelLevel, double zValue)
    {
        double sigma = BaseDefenderIntelNoise / (1.0 + intelLevel);
        double multiplier = Math.Max(0.1, 1.0 + zValue * sigma); // never estimate a ~zero/negative force
        return (long)Math.Round(trueBattleValue * multiplier);
    }

    private bool AreFactionsEnemies(Faction f1, Faction f2)
    {
        if (f1 == null || f2 == null) return false;
        bool f1IsImperial = f1.IsPlayerFaction || f1.IsDefaultFaction;
        bool f2IsImperial = f2.IsPlayerFaction || f2.IsDefaultFaction;
        return f1IsImperial != f2IsImperial;
    }
}
