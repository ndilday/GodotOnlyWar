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

    // The recon-first / assault-or-raid candidate machinery that used to live here is gone. It chose
    // between plans from the force that happened to be spare beside a target (IsWinnable, IsRaidViable,
    // RaidUtility all read PotentialOffensive.AvailableAttackingForce), which is exactly the coupling
    // marginal allocation has to remove: a target's worth must be a property of the TARGET, and all
    // force-dependence must live in the value curve or the auction counts it twice.
    //
    // Assault and Raid are now offered together as separate tasks and priced against each other by
    // ForceTaskBuilder - so "cannot take it but can still hurt it" became a real option instead of a
    // branch that a frozen region never reached.

    internal static PotentialOffensive ChooseReconTarget(IEnumerable<PotentialOffensive> underKnown) =>
        underKnown.OrderByDescending(o => o.Reward).FirstOrDefault();

    /// <summary>
    /// Whether this much force would carry the region. Force is now a PARAMETER rather than a property
    /// of the target: under marginal allocation the question "is it winnable" is answered by what the
    /// auction is willing to spend, not by what happened to be spare beside it.
    /// </summary>
    internal static bool IsWinnable(PotentialOffensive offensive, long attackingForce)
    {
        return attackingForce
            > offensive.EstimatedDefenderBattleValue * OffensiveForceRatioThreshold;
    }

    internal static bool IsWellReconnoitred(PotentialOffensive offensive, int attackerFactionId) =>
        offensive.TargetRegion.GetFactionRegionAwareness(attackerFactionId)
            >= FactionStrategyPlanningConstants.ReconIntelThreshold;

    internal static double RewardRiskScore(PotentialOffensive offensive)
    {
        // Risk scales with the estimated defender strength and how dug-in it is: a fortified
        // objective is disproportionately costly to take.
        double risk = offensive.EstimatedDefenderBattleValue
                      * (1.0 + RegionDefenses.GetShared(offensive.TargetFaction, DefenseType.Entrenchment)
                          * EntrenchmentRiskFactor);
        return offensive.Reward / Math.Max(risk, 1.0);
    }

    /// <summary>
    /// What taking this region is worth, as a property of the TARGET alone.
    /// </summary>
    /// <remarks>
    /// The trailing <c>* availableAttackingForce / defenderForce</c> is gone. It made a target score
    /// higher because of an accident of who was standing next to it, so the planner's ranking moved
    /// whenever troops moved. Under marginal allocation it is worse than untidy: force-dependence belongs in the value
    /// curve, and leaving it in the importance too makes the auction count it twice.
    /// </remarks>
    internal static double CalculateOffensiveReward(
        RegionFaction targetFaction,
        Faction attackingFaction)
    {
        double reward = targetFaction.Population;
        if (attackingFaction.GrowthType == GrowthType.Consumption)
        {
            reward += targetFaction.Region.CarryingCapacity;
        }
        return reward;
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
                + $"wellKnown={IsWellReconnoitred(offensive, faction.Id)}, "
                + $"staging={string.Join(",", offensive.AttackingRegions.Select(r => r.Name))}");
        }
    }

    /// <summary>
    /// What a raid of this size is worth against this target.
    /// </summary>
    /// <remarks>
    /// The expected-damage term is where a raid's saturation comes from: above
    /// <c>estimatedDefender * 0.5 / RaidCommitFraction</c> - about 1.43 times the defender - the first
    /// half of the min() stops binding and more raiders add nothing. That figure was always implicit
    /// here; the task builder now reads it rather than authoring a number of its own.
    /// </remarks>
    internal static double RaidUtilityAt(PotentialOffensive offensive, long attackingForce)
    {
        double expectedDamage = Math.Min(
            attackingForce * RaidCommitFraction,
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

        // A target is no longer dropped because the regions beside it happen to hold nothing. Under
        // the old ladder that test removed a region as an attacker AND as a staging region the moment
        // its defensive reserve ate its strength, which is a large part of why forces froze. Whether
        // an offensive is affordable is now the auction's question, not the evaluator's.
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
        // The awareness actually earned by scouting, kept separate from the floor applied below so the
        // floor cannot silently buy precision it did not pay for.
        float observedAwareness = intel;
        // Ground the attacker is already standing on needs no scouting - it can see whoever shares
        // the region with it, so a local target counts as well known; without this a faction would
        // refuse to engage an enemy in its own streets
        // because it assumed the neighbours might be armed.
        if (targetFaction.Region.RegionFactionMap.ContainsKey(attackingFaction.Id))
        {
            // Two different things, and conflating them was the bug. The intel floor decides WHICH
            // MODEL to use - sharing a region means the population-anchored prior no longer applies,
            // because you are not guessing whether the neighbours might be armed. It says nothing about
            // how precisely you know their strength.
            intel = Math.Max(intel, FactionStrategyPlanningConstants.ReconIntelThreshold);

            // Precision stays an ESTIMATE, and it is bought with reconnaissance like every other
            // estimate. What sharing the ground gives you is that the estimate is CURRENT: you notice a
            // garrison marching out even if you cannot count what is left. Coarsened at the observer's
            // real awareness, so a faction that never scouts the region it occupies knows only the
            // order of magnitude - at awareness zero an actual 18 reads as 100 - and scouting its own
            // streets sharpens that, which is the reason to do it.
            //
            // Grist Nine, 2026-09-16: the Imperials withdrew from Grist Nine Nu leaving 18 battle value,
            // while the Orks sharing the region still believed 505 from weeks earlier. They sized the
            // assault off that and committed 753 to kill 18.
            estimatedDefenderBattleValue =
                FactionIntelligenceRules.CoarsenEstimate(defenderBattleValue, observedAwareness)
                ?? defenderBattleValue;
            estimatedPopulation =
                FactionIntelligenceRules.CoarsenEstimate(targetFaction.Population, observedAwareness)
                ?? targetFaction.Population;
        }

        potentialOffensives.Add(new PotentialOffensive
        {
            TargetRegion = targetFaction.Region,
            TargetFaction = targetFaction,
            AttackingRegions = attackingRegions,
            Reward = CalculateOffensiveReward(targetFaction, attackingFaction),
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
            + $"defenderBV={offensive.DefenderBattleValue}, "
            + $"estimatedDefenderBV={offensive.EstimatedDefenderBattleValue}";
    }
}
