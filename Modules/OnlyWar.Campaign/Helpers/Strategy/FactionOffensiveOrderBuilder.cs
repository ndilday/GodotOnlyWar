using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Helpers.StrategicCombat;
using OnlyWar.Models;
using OnlyWar.Models.FactionBehaviors;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Strategy;

/// <summary>
/// Turns one evaluated offensive into either a strategic or tactical order.
/// </summary>
/// <remarks>
/// Target selection remains in <see cref="FactionOffensiveEvaluator"/> and the regional mission
/// loop remains in the facade. This owner is deliberately concerned only with the issuance boundary:
/// sizing, staging, live-pool/planning-budget mutations, routing, and the existing tactical return
/// paths.
/// </remarks>
internal sealed class FactionOffensiveOrderBuilder
{
    private readonly Func<ForceGenerationRequest, IRNG, List<Squad>> _forceGenerator;

    internal FactionOffensiveOrderBuilder(
        Func<ForceGenerationRequest, IRNG, List<Squad>> forceGenerator = null)
    {
        _forceGenerator = forceGenerator ?? ForceGenerator.GenerateForce;
    }

    internal bool IssueAssault(
        Faction faction,
        PotentialOffensive chosenOffensive,
        List<RegionForceState> regionalForceStates,
        List<Order> allOrders,
        IRNG random)
    {
        long intendedBattleValue = (long)(chosenOffensive.DefenderBattleValue * 2);
        List<RegionFaction> emergingSources = chosenOffensive.AttackingRegions
            .Select(region => region.RegionFactionMap.TryGetValue(faction.Id, out RegionFaction rf) ? rf : null)
            .Where(rf => rf?.HasEmergenceAdvantage == true)
            .ToList();
        MissionType missionType = emergingSources.Count > 0 ? MissionType.Ambush : MissionType.Advance;
        bool launched = IssueOffensive(
            faction,
            chosenOffensive,
            regionalForceStates,
            allOrders,
            intendedBattleValue,
            missionType,
            Aggression.Normal,
            random);
        if (launched)
        {
            foreach (RegionFaction source in emergingSources) source.HasEmergenceAdvantage = false;
        }
        return launched;
    }

    internal bool IssueLightningRaid(
        Faction faction,
        PotentialOffensive chosenOffensive,
        List<RegionForceState> regionalForceStates,
        List<Order> allOrders,
        IRNG random)
    {
        long minimum = Math.Max(
            FactionOffensiveEvaluator.MinimumRaidBattleValue,
            (long)Math.Ceiling(
                chosenOffensive.EstimatedDefenderBattleValue
                * FactionOffensiveEvaluator.RaidForceRatioThreshold));
        long intendedBattleValue = Math.Min(
            chosenOffensive.AvailableAttackingForce,
            Math.Max(
                minimum,
                (long)(chosenOffensive.AvailableAttackingForce
                    * FactionOffensiveEvaluator.RaidCommitFraction)));
        return IssueOffensive(
            faction,
            chosenOffensive,
            regionalForceStates,
            allOrders,
            intendedBattleValue,
            MissionType.LightningRaid,
            Aggression.Cautious,
            random);
    }

    internal bool IssueOffensive(
        Faction faction,
        PotentialOffensive chosenOffensive,
        List<RegionForceState> regionalForceStates,
        List<Order> allOrders,
        long intendedBattleValue,
        MissionType missionType,
        Aggression aggression,
        IRNG random)
    {
        long totalAvailableForAttack = chosenOffensive.AvailableAttackingForce;
        if (intendedBattleValue <= 0
            || totalAvailableForAttack <= 0
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
        // horde, Garrison otherwise), split in the existing opportunity-cost order.
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

        bool useStrategicCombat = ShouldUseStrategicCombat(
            faction, chosenOffensive, committedBattleValue);
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
            allOrders.Add(new Order(new List<Squad>(), false, true, aggression, strategicMission, faction));
            return true;
        }

        ForceGenerationRequest request = new()
        {
            Faction = faction,
            TargetBattleValue = committedBattleValue,
            Profile = ForceCompositionProfile.AssaultForce
        };
        List<Squad> generatedSquads = _forceGenerator(request, random) ?? new List<Squad>();
        if (generatedSquads.Count == 0)
        {
            ReturnCommittedForce(contributions);
            GameLog.Debug(() =>
                $"AI {missionType} {faction.Name}: target={DescribeOffensive(chosenOffensive)}, tactical generation failed; "
                + $"returnedCommitted={committedBattleValue}");
            return false;
        }

        long generatedBattleValue = SquadBattleValue(generatedSquads);
        if (generatedBattleValue < committedBattleValue)
        {
            ReturnCommittedForceExcess(contributions, committedBattleValue - generatedBattleValue);
            GameLog.Debug(() =>
                $"AI {missionType} {faction.Name}: target={DescribeOffensive(chosenOffensive)}, tactical generation shortfall="
                + $"{committedBattleValue - generatedBattleValue}; generatedBV={generatedBattleValue}");
            committedBattleValue = generatedBattleValue;
        }

        // Record the staging region on the assault force so its survivors know where to withdraw to
        // (raid) — see MissionAftermathProcessor.ResolveOffensiveSurvivors. The primary contributing
        // region stands in for the whole staging effort.
        Region stagingRegion = contributions
            .OrderByDescending(c => c.BattleValue)
            .Select(c => c.StagingFaction?.Region)
            .FirstOrDefault(region => region != null)
            ?? chosenOffensive.AttackingRegions.First();
        foreach (Squad squad in generatedSquads)
        {
            squad.CurrentRegion = stagingRegion;
        }

        Mission newMission = new(missionType, chosenOffensive.TargetFaction, 0);
        Order newOrder = new(
            generatedSquads,
            missionType == MissionType.LightningRaid,
            true,
            aggression,
            newMission,
            faction);
        allOrders.Add(newOrder);
        GameLog.Debug(() =>
            $"AI {missionType} {faction.Name}: tactical order created target={DescribeOffensive(chosenOffensive)}, "
            + $"staging={stagingRegion.Name}, squads={generatedSquads.Count}, soldiers={generatedSquads.Sum(s => s.Members.Count)}, "
            + $"battleValue={generatedBattleValue}");
        return true;
    }

    internal static bool ShouldUseStrategicCombat(
        Faction attacker,
        PotentialOffensive offensive,
        long committedBattleValue)
    {
        if (attacker == null || offensive?.TargetFaction == null) return false;
        if (attacker.IsPlayerFaction || offensive.TargetFaction.PlanetFaction.Faction.IsPlayerFaction) return false;
        if (offensive.TargetFaction.LandedSquads.Any(s => s.Faction?.IsPlayerFaction == true)) return false;
        // Secular insurgents are represented by abstract embedded-PDF and armed-civilian pools;
        // they deliberately have no squad templates to generate for tactical combat.
        if (attacker.GrowthType == GrowthType.Unrest) return true;

        long defenderBattleValue = offensive.DefenderBattleValue > 0
            ? offensive.DefenderBattleValue
            : FactionThreatAssessment.CalculateDefenderBattleValue(offensive.TargetFaction);

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
        List<StrategicCombatContribution> contributions = new();
        long remaining = committedBattleValue;
        List<RegionForceState> contributingStates = FactionStagingPlanner
            .ChooseStagingRegionsByOpportunityCost(chosenOffensive, regionalForceStates)
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

    private static void ReturnCommittedForce(IEnumerable<StrategicCombatContribution> contributions)
    {
        foreach (StrategicCombatContribution contribution in contributions)
        {
            contribution.StagingFaction?.AddMilitaryStrength(contribution.BattleValue);
        }
    }

    private static void ReturnCommittedForceExcess(
        IEnumerable<StrategicCombatContribution> contributions,
        long excess)
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
        SquadTemplate template = faction.SquadTemplates.Values
            .Where(t => (t.SquadType & SquadTypes.HQ) == 0)
            .OrderByDescending(t => t.BattleValue)
            .FirstOrDefault();
        if (template == null || template.BattleValue <= 0) return 0;
        int squadCount = (int)Math.Ceiling(targetBattleValue / (double)template.BattleValue);
        int actorsPerSquad = template.Elements.Sum(e => e.MaximumNumber);
        return squadCount * actorsPerSquad;
    }

    private static long SquadBattleValue(IEnumerable<Squad> squads) =>
        squads.Sum(squad => squad.Members.Sum(member => (long)member.Template.BattleValue));

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
        List<string> parts = contributions
            .Where(c => c.BattleValue > 0)
            .Select(c => $"{c.StagingFaction?.Region.Name ?? "unknown"}:{c.BattleValue}")
            .ToList();
        return parts.Count == 0 ? "none" : string.Join(",", parts);
    }
}
