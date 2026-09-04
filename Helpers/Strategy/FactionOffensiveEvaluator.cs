using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Fortifications;
using OnlyWar.Helpers.StrategicCombat;
using OnlyWar.Models;
using OnlyWar.Models.FactionBehaviors;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Strategy;

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
    // 1-sigma error on the attacker's estimate at zero intelligence.
    internal const double BaseDefenderIntelNoise = 0.5;
    // Caution under uncertainty: the planning estimate is one sigma above the expected value.
    internal const double DefenderEstimateCautionZ = 1.0;
    internal const double RaidForceRatioThreshold = 0.25;
    internal const double RaidCommitFraction = 0.35;
    internal const long MinimumRaidBattleValue = 100;

    private readonly FactionBehaviorRulesProfile _behaviorRules;

    internal FactionOffensiveEvaluator(FactionBehaviorRulesProfile behaviorRules)
    {
        _behaviorRules = behaviorRules;
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

    internal static long CautiousDefenderEstimate(long trueBattleValue, float intelLevel)
    {
        double sigma = BaseDefenderIntelNoise / (1.0 + intelLevel);
        double multiplier = 1.0 + DefenderEstimateCautionZ * sigma;
        return (long)Math.Round(trueBattleValue * multiplier);
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

    private static double ReconUtility(Faction faction, PotentialOffensive offensive)
    {
        double intelGap = Math.Max(0.25,
            FactionStrategyPlanningConstants.ReconIntelThreshold
            - offensive.TargetRegion.GetFactionRegionAwareness(faction.Id));
        return offensive.Reward * intelGap / Math.Max(offensive.AvailableAttackingForce, 1);
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
            targetFaction, attackingFaction);
        PlanetFaction observer = targetFaction.Region.Planet.PlanetFactionMap
            .GetValueOrDefault(attackingFaction.Id);
        FactionIntelBelief belief = observer?.GetTargetBelief(
            targetFaction.Region,
            targetFaction.PlanetFaction.Faction);
        long estimatedDefenderBattleValue = belief?.EstimatedMilitaryStrength
            ?? defenderBattleValue;
        // Regional awareness remains the planner's recon/readiness signal. The strength estimate
        // itself comes from the stored target belief; it is never rounded from live target truth here.
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
            EstimatedDefenderBattleValue = CautiousDefenderEstimate(estimatedDefenderBattleValue, intel)
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
