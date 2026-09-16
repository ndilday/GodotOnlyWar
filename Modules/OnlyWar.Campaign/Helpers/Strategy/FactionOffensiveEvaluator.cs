using OnlyWar.Domain.Extensions;
using OnlyWar.Domain.Fortifications;
using OnlyWar.Operations.StrategicCombat;
using OnlyWar.Domain;
using OnlyWar.Domain.FactionBehaviors;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Planets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Campaign.Strategy;

/// <summary>
/// Discovers and scores the offensive opportunities available to a faction on one planet.
/// </summary>
/// <remarks>
/// This component evaluates the current world only. It never issues an order or mutates planning
/// state, so the controller can rebuild its candidates after each issued mission.
/// </remarks>
internal sealed class FactionOffensiveEvaluator
{
    // Each point of enemy Entrenchment multiplies the effective cost of assaulting the region.
    internal const double EntrenchmentRiskFactor = 0.5;
    // Force-ratio edge the attacker insists on over its estimated defender before committing.
    internal const double OffensiveForceRatioThreshold = 1.5;
    // Share of a believed population an attacker assumes is under arms on ground it has not
    // scouted. A PDF actually fields about 3%, so this errs against the attacker by design.
    internal const double PessimisticMobilizationFraction = 0.05;
    internal const double RaidForceRatioThreshold = 0.25;
    internal const double RaidCommitFraction = 0.35;
    internal const long MinimumRaidBattleValue = 100;

    private readonly FactionBehaviorRulesProfile _behaviorRules;
    // The persistent invasion forces of the campaign being planned for. Supplied explicitly so the
    // planner prices a defender the same way strategic combat will, without reading a current session.
    private readonly IReadOnlyList<StrategicInvasionForce> _strategicInvasionForces;

    internal FactionOffensiveEvaluator(
        FactionBehaviorRulesProfile behaviorRules,
        IReadOnlyList<StrategicInvasionForce> strategicInvasionForces = null)
    {
        _behaviorRules = behaviorRules;
        _strategicInvasionForces = strategicInvasionForces;
    }

    internal List<PotentialOffensive> IdentifyPotentialOffensivesOnPlanet(
        Faction attackingFaction,
        Planet planet,
        List<RegionForceState> regionalForceStates)
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
        foreach (RegionFaction targetFaction in allEnemyRegionFactions)
        {
            if (targetFaction.Region.RegionFactionMap.ContainsKey(attackingFaction.Id))
            {
                AddPotentialOffensive(
                    attackingFaction,
                    targetFaction,
                    [targetFaction.Region],
                    regionalForceStates,
                    localOffensives);
            }
        }

        var potentialOffensives = new List<PotentialOffensive>(localOffensives);
        foreach (RegionFaction targetFaction in allEnemyRegionFactions)
        {
            if (targetFaction.Region.RegionFactionMap.ContainsKey(attackingFaction.Id)) continue;

            List<Region> adjacentAttackingRegions = targetFaction.Region.GetAdjacentRegions()
                .Where(r => r.RegionFactionMap.TryGetValue(attackingFaction.Id, out RegionFaction rf)
                            && rf.IsPublic)
                .ToList();
            AddPotentialOffensive(
                attackingFaction,
                targetFaction,
                adjacentAttackingRegions,
                regionalForceStates,
                potentialOffensives);
        }
        return potentialOffensives;
    }

    internal MissionCandidate ChooseBestMissionCandidate(
        Faction faction,
        List<PotentialOffensive> offensives,
        HashSet<string> plannedTargets)
    {
        return offensives
            .SelectMany(BuildMissionCandidatesForOffensive)
            .Where(candidate => !plannedTargets.Contains(MissionTargetKey(candidate.Offensive)))
            .OrderByDescending(candidate => candidate.Score)
            .FirstOrDefault();

        IEnumerable<MissionCandidate> BuildMissionCandidatesForOffensive(PotentialOffensive offensive) =>
            BuildMissionCandidates(faction, offensive);
    }

    /// <summary>
    /// Builds the one or more executable plans for an evaluated target, retaining the runtime's
    /// recon-first and capability-specific assault/raid rules.
    /// </summary>
    internal IEnumerable<MissionCandidate> BuildMissionCandidates(
        Faction faction,
        PotentialOffensive offensive)
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

        if (IsWinnableForFaction(faction, offensive))
        {
            yield return new MissionCandidate
            {
                Plan = OffensivePlan.Assault,
                Offensive = offensive,
                Score = FactionCapabilities.GeneratesInvasions(faction)
                    ? offensive.DefenderBattleValue
                    : RewardRiskScore(offensive) * 10.0
            };
        }
        else if (!FactionCapabilities.GeneratesInvasions(faction) && IsRaidViable(offensive))
        {
            yield return new MissionCandidate
            {
                Plan = OffensivePlan.Raid,
                Offensive = offensive,
                Score = RaidUtility(offensive)
            };
        }
    }

    internal static string MissionTargetKey(PotentialOffensive offensive) =>
        $"{offensive.TargetRegion.Id}:{offensive.TargetFaction.PlanetFaction.Faction.Id}";

    internal static PotentialOffensive ChooseBestOffensive(IEnumerable<PotentialOffensive> offensives)
    {
        return offensives
            .Where(IsWinnable)
            .OrderByDescending(RewardRiskScore)
            .FirstOrDefault();
    }

    internal static PotentialOffensive ChooseReconTarget(IEnumerable<PotentialOffensive> underKnown) =>
        underKnown.OrderByDescending(o => o.Reward).FirstOrDefault();

    internal static bool IsWinnable(PotentialOffensive offensive)
    {
        return offensive.AvailableAttackingForce
            > offensive.EstimatedDefenderBattleValue * OffensiveForceRatioThreshold;
    }

    internal static bool IsWellReconnoitred(PotentialOffensive offensive, int attackerFactionId) =>
        offensive.TargetRegion.GetFactionRegionAwareness(attackerFactionId)
            >= FactionStrategyPlanningConstants.ReconIntelThreshold;

    internal static bool IsRaidViable(PotentialOffensive offensive)
    {
        if (offensive.DefenderBattleValue <= 0) return false;
        long minimum = Math.Max(MinimumRaidBattleValue,
            (long)Math.Ceiling(offensive.EstimatedDefenderBattleValue * RaidForceRatioThreshold));
        return offensive.AvailableAttackingForce >= minimum;
    }

    internal static double RewardRiskScore(PotentialOffensive offensive)
    {
        // Risk scales with the estimated defender strength and how dug-in it is: a fortified
        // objective is disproportionately costly to take.
        double risk = offensive.EstimatedDefenderBattleValue
                      * (1.0 + RegionDefenses.GetShared(offensive.TargetFaction, DefenseType.Entrenchment)
                          * EntrenchmentRiskFactor);
        return offensive.Reward / Math.Max(risk, 1.0);
    }

    internal static double CalculateOffensiveReward(
        RegionFaction targetFaction,
        Faction attackingFaction,
        long availableAttackingForce,
        long defenderForce)
    {
        double reward = targetFaction.Population;
        if (attackingFaction.GrowthType == GrowthType.Consumption)
        {
            reward += targetFaction.Region.CarryingCapacity;
        }
        return reward * availableAttackingForce / defenderForce;
    }

    /// <summary>
    /// What an attacker should PLAN against: a pessimistic prior on ground it has not scouted,
    /// giving way to the observed estimate as reconnaissance earns the right to it.
    /// </summary>
    /// <remarks>
    /// Unscouted ground used to look empty, so attacking was always the cheapest option available
    /// and reconnaissance had to compete with targets that appeared free. Assuming the worst inverts
    /// that: a faction must scout a region to discover it is weak enough to attack, which is what
    /// makes reconnaissance worth doing at all. The asymmetry is deliberate - an attacker who
    /// guesses low dies, a defender who guesses high only wastes troops.
    ///
    /// Confidence comes from REGION AWARENESS rather than belief evidence, and that matters:
    /// ObservePublicActivity refreshes every public belief to Confirmed for free each turn, so an
    /// evidence-based confidence would be total everywhere and the prior would never apply.
    /// Awareness is bought only by scouting, which is precisely the thing being rewarded.
    ///
    /// There is no uncertainty hedge here any more. The belief is already an upper bound - awareness
    /// decides how many significant figures it carries and FactionIntelligenceRules.CoarsenEstimate
    /// rounds up - so multiplying it again was caution counted twice, on a curve unrelated to the
    /// measurement it claimed to be correcting. It also quietly turned the 1.5:1 doctrine in
    /// OffensiveForceRatioThreshold into 1.875:1. What this returns is now what the attacker
    /// genuinely expects to face, so a doctrine multiplier applied to it means what it says.
    ///
    /// This is applied ONLY here, on the attacker's side. The defensive reserve reads the raw belief
    /// (FactionThreatAssessment.CalculateRequiredDefensiveBattleValue), and inflating that would
    /// make every region garrison against phantoms on all its borders and leave nothing spare for
    /// anything - the paralysis this whole area was fixed to escape, arrived at from the far side.
    /// </remarks>
    internal static long CautiousDefenderEstimate(
        long believedMilitaryStrength,
        long believedPopulation,
        Faction targetFaction,
        float regionAwareness)
    {
        // What the region could field if it turned out to be as dangerous as it might be. For a
        // horde whose numbers ARE its army, that is everyone; for a civilian-based faction it is a
        // mobilized fraction, set above the ~3% a PDF actually fields so that guessing costs the
        // attacker caution rather than its force.
        double pessimisticPrior = targetFaction?.HasBehavior(FactionBehavior.PopulationIsMilitary) == true
            ? believedPopulation
            : believedPopulation * PessimisticMobilizationFraction;

        double confidence = Math.Clamp(
            regionAwareness / FactionStrategyPlanningConstants.ReconIntelThreshold, 0.0, 1.0);
        double blended = pessimisticPrior
            + (believedMilitaryStrength - pessimisticPrior) * confidence;

        // Both ends of that blend sit at or above the truth - the prior by construction, the belief
        // because it was rounded up - so anything between them does too.
        return (long)Math.Round(Math.Max(0.0, blended));
    }

    internal void LogPotentialOffensives(
        Faction faction,
        Planet planet,
        List<PotentialOffensive> offensives)
    {
        GameLog.Debug(() =>
            $"AI plan {faction.Name}/{planet.Name}: offensive candidates={offensives.Count}");
        foreach (PotentialOffensive offensive in offensives)
        {
            GameLog.Trace(() =>
                $"AI candidate {faction.Name}/{planet.Name}: {DescribeOffensive(offensive)}, "
                + $"reward={offensive.Reward:F0}, score={RewardRiskScore(offensive):F2}, "
                + $"intel={offensive.TargetRegion.GetFactionRegionAwareness(faction.Id):F2}/"
                + $"{FactionStrategyPlanningConstants.ReconIntelThreshold:F2}, "
                + $"wellKnown={IsWellReconnoitred(offensive, faction.Id)}, winnable={IsWinnable(offensive)}, "
                + $"staging={string.Join(",", offensive.AttackingRegions.Select(r => r.Name))}");
        }
    }

    private static bool IsLocalOffensive(Faction faction, PotentialOffensive offensive) =>
        offensive.TargetRegion.RegionFactionMap.ContainsKey(faction.Id);

    // Scouting a region is worth what it would unlock, scaled by how much is still unknown about
    // it. It is deliberately NOT divided by the force available to stage it: that made a candidate
    // look better the less able the faction was to act on it, so the planner consistently ranked
    // the offensives it could not afford above the ones it could.
    private static double ReconUtility(Faction faction, PotentialOffensive offensive)
    {
        double intelGap = Math.Max(0.25,
            FactionStrategyPlanningConstants.ReconIntelThreshold
            - offensive.TargetRegion.GetFactionRegionAwareness(faction.Id));
        return offensive.Reward * intelGap;
    }

    private bool IsWinnableForFaction(Faction faction, PotentialOffensive offensive)
    {
        if (FactionCapabilities.GeneratesInvasions(faction))
        {
            return offensive.AvailableAttackingForce
                >= (long)Math.Ceiling(offensive.EstimatedDefenderBattleValue
                    * (_behaviorRules?.DefendedLandingRatio ?? 2.0));
        }
        return IsWinnable(offensive);
    }

    private static double RaidUtility(PotentialOffensive offensive)
    {
        double expectedDamage = Math.Min(
            offensive.AvailableAttackingForce * RaidCommitFraction,
            Math.Max(1, offensive.EstimatedDefenderBattleValue) * 0.5);
        double risk = Math.Max(1.0, offensive.EstimatedDefenderBattleValue
            * (1.0 + RegionDefenses.GetShared(offensive.TargetFaction, DefenseType.Entrenchment)
                * EntrenchmentRiskFactor));
        return (offensive.Reward * 0.25 + expectedDamage) / risk;
    }

    private void AddPotentialOffensive(
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
            targetFaction, attackingFaction, _strategicInvasionForces);
        PlanetFaction observer = targetFaction.Region.Planet.PlanetFactionMap
            .GetValueOrDefault(attackingFaction.Id);
        FactionIntelBelief belief = observer?.GetTargetBelief(
            targetFaction.Region,
            targetFaction.PlanetFaction.Faction);
        long estimatedDefenderBattleValue = belief?.EstimatedMilitaryStrength
            ?? defenderBattleValue;
        // The believed headcount is what the pessimistic prior is anchored on: a population is
        // visible from outside in a way a garrison is not.
        long estimatedPopulation = belief?.EstimatedPopulation ?? targetFaction.Population;
        // Regional awareness is the planner's recon signal, and it is what the prior gives way to.
        // Belief evidence is deliberately NOT used: public activity refreshes it for free.
        float intel = targetFaction.Region.GetFactionRegionAwareness(attackingFaction.Id);
        // Ground the attacker is already standing on needs no scouting - it can see whoever shares
        // the region with it. This mirrors IsLocalOffensive, which likewise treats a local target as
        // well known; without it a faction would refuse to engage an enemy in its own streets
        // because it assumed the neighbours might be armed.
        if (targetFaction.Region.RegionFactionMap.ContainsKey(attackingFaction.Id))
        {
            intel = Math.Max(intel, FactionStrategyPlanningConstants.ReconIntelThreshold);
        }

        potentialOffensives.Add(new PotentialOffensive
        {
            TargetRegion = targetFaction.Region,
            TargetFaction = targetFaction,
            AttackingRegions = attackingRegions,
            AvailableAttackingForce = availableForce,
            Reward = CalculateOffensiveReward(targetFaction, attackingFaction, availableForce, defenderBattleValue),
            DefenderBattleValue = defenderBattleValue,
            EstimatedDefenderBattleValue = CautiousDefenderEstimate(
                estimatedDefenderBattleValue,
                estimatedPopulation,
                targetFaction.PlanetFaction.Faction,
                intel)
        });
    }

    private static string DescribeOffensive(PotentialOffensive offensive)
    {
        if (offensive == null) return "none";
        return $"{offensive.TargetRegion.Planet.Name}/{offensive.TargetRegion.Name}/"
            + $"{offensive.TargetFaction.PlanetFaction.Faction.Name} "
            + $"available={offensive.AvailableAttackingForce}, defenderBV={offensive.DefenderBattleValue}, "
            + $"estimatedDefenderBV={offensive.EstimatedDefenderBattleValue}";
    }
}
