using OnlyWar.Domain;
using OnlyWar.Operations.StrategicCombat;
using OnlyWar.Domain;
using OnlyWar.Domain.FactionBehaviors;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Squads;
using OnlyWar.Runtime.Allocators;
using OnlyWar.Runtime.Factories;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Campaign.Strategy;

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
    private readonly IPersistentIdAllocator _identity;
    private readonly Func<ForceGenerationRequest, IRNG, List<Squad>> _forceGenerator;

    internal FactionOffensiveOrderBuilder(
        Func<ForceGenerationRequest, IRNG, List<Squad>> forceGenerator = null,
        IPersistentIdAllocator identity = null)
    {
        _identity = identity ?? new PersistentIdAllocator();
        _forceGenerator = forceGenerator
            ?? ((request, random) => ForceGenerator.GenerateForce(request, random, _identity));
    }

    /// <summary>
    /// Launches an offensive on the battle value the allocation auction awarded it, from the regions
    /// that actually bid into it.
    /// </summary>
    /// <remarks>
    /// The auction has already decided both the size and the staging split, and has already debited
    /// each region's planning budget, so this neither re-sizes the force nor re-chooses staging by
    /// opportunity cost. It only converts the award into a mission - which is what makes a
    /// multi-region assault ONE order rather than several.
    /// </remarks>
    internal bool IssueAllocatedOffensive(
        Faction faction,
        PotentialOffensive chosenOffensive,
        IReadOnlyList<(RegionFaction Staging, long BattleValue)> allocations,
        long totalBattleValue,
        MissionType missionType,
        Aggression aggression,
        List<Order> allOrders,
        IRNG random)
    {
        if (chosenOffensive == null || allocations.Count == 0 || totalBattleValue <= 0) return false;
        if (totalBattleValue < faction.MinimumForceRequest)
        {
            GameLog.Debug(() =>
                $"AI {missionType} {faction.Name}: target={DescribeOffensive(chosenOffensive)}, "
                + $"awarded={totalBattleValue}, minimum={faction.MinimumForceRequest}; no order created");
            return false;
        }

        List<StrategicCombatContribution> contributions = new();
        foreach ((RegionFaction staging, long battleValue) in allocations)
        {
            long drawn = Math.Min(battleValue, staging.MilitaryStrength);
            if (drawn <= 0) continue;
            staging.RemoveMilitaryStrength(drawn);
            contributions.Add(new StrategicCombatContribution(staging, drawn));
        }
        long committedBattleValue = contributions.Sum(c => c.BattleValue);
        if (committedBattleValue <= 0) return false;

        bool opensWithAmbush = HasEmergenceAdvantage(faction, chosenOffensive);
        return FinishOffensive(
            faction, chosenOffensive, contributions, committedBattleValue,
            missionType, aggression, allOrders, random, opensWithAmbush);
    }

    internal bool IssueAssault(
        Faction faction,
        PotentialOffensive chosenOffensive,
        List<RegionForceState> regionalForceStates,
        List<Order> allOrders,
        IRNG random)
    {
        // Sized off what the attacker BELIEVES it faces, not the truth. The two used to disagree:
        // the force was sized from the real defender while the decision to attack at all was taken
        // from the estimate, so a faction committed as though it knew the ground exactly and chose
        // as though it did not. Now a blind attack is overwhelming, because the attacker fears the
        // worst, and a scouted one is efficient.
        long intendedBattleValue = (long)(chosenOffensive.EstimatedDefenderBattleValue * 2);
        // Surprise opens the advance; it no longer replaces it. This used to select
        // MissionType.Ambush, which swapped in the whole ambush chain - including its
        // MissionReturnPolicy.Return - so a force sized at twice the defender expressly to TAKE the
        // region struck once and withdrew from it. The mission stays an Advance and holds what it
        // takes; the first day is fought from ambush (MissionStepOrchestrator's opening-ambush chain).
        bool opensWithAmbush = HasEmergenceAdvantage(faction, chosenOffensive);
        return IssueOffensive(
            faction,
            chosenOffensive,
            regionalForceStates,
            allOrders,
            intendedBattleValue,
            MissionType.Advance,
            Aggression.Normal,
            random,
            opensWithAmbush);
    }

    // Surprise belongs to a region that went public this week (FactionRevealService). It is not spent
    // by using it: FactionStrategyController clears every advantage at the end of the planning pass, so
    // it lasts exactly the one turn whether or not anything was launched with it.
    private static bool HasEmergenceAdvantage(Faction faction, PotentialOffensive offensive) =>
        offensive.AttackingRegions
            .Select(region => region.RegionFactionMap.TryGetValue(faction.Id, out RegionFaction rf) ? rf : null)
            .Any(rf => rf?.HasEmergenceAdvantage == true);

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
        long budget = StagingBudget(chosenOffensive, regionalForceStates);
        long intendedBattleValue = Math.Min(
            budget,
            Math.Max(minimum, (long)(budget * FactionOffensiveEvaluator.RaidCommitFraction)));
        // A raid already exists to strike and withdraw, so surprise needs no special chain here - the
        // standalone ambush IS a raid made from concealment, and it carries the same Return policy.
        MissionType missionType = HasEmergenceAdvantage(faction, chosenOffensive)
            ? MissionType.Ambush
            : MissionType.LightningRaid;
        return IssueOffensive(
            faction,
            chosenOffensive,
            regionalForceStates,
            allOrders,
            intendedBattleValue,
            missionType,
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
        IRNG random,
        bool opensWithAmbush = false)
    {
        long totalAvailableForAttack = StagingBudget(chosenOffensive, regionalForceStates);
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

        return FinishOffensive(
            faction, chosenOffensive, contributions, committedBattleValue,
            missionType, aggression, allOrders, random, opensWithAmbush);
    }

    /// <summary>
    /// Everything after the force has been committed: choose strategic or tactical resolution, build
    /// the mission, and record the staging region survivors withdraw to.
    /// </summary>
    /// <remarks>
    /// Shared by the legacy sizing path and by the allocation auction, which arrives here with its
    /// contributions already decided and its regional budgets already debited.
    /// </remarks>
    private bool FinishOffensive(
        Faction faction,
        PotentialOffensive chosenOffensive,
        List<StrategicCombatContribution> contributions,
        long committedBattleValue,
        MissionType missionType,
        Aggression aggression,
        List<Order> allOrders,
        IRNG random,
        bool opensWithAmbush)
    {
        bool useStrategicCombat = ShouldUseStrategicCombat(
            faction, chosenOffensive, committedBattleValue);
        GameLog.Debug(() =>
            $"AI {missionType} {faction.Name}: target={DescribeOffensive(chosenOffensive)}, "
            + $"committed={committedBattleValue}, "
            + $"mode={(useStrategicCombat ? "strategic" : "tactical")}, contributions={DescribeContributions(contributions)}");

        if (useStrategicCombat)
        {
            StrategicCombatMission strategicMission = new(
                _identity.GetNextMissionId(),
                chosenOffensive.TargetFaction,
                faction,
                committedBattleValue,
                contributions,
                aggression,
                faction.HasBehavior(FactionBehavior.InvadesOnVictory),
                missionType);
            allOrders.Add(new Order(
                _identity.GetNextOrderId(),
                new List<Squad>(),
                false,
                true,
                aggression,
                strategicMission,
                faction)
            {
                OpensWithAmbush = opensWithAmbush
            });
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

        Mission newMission = new(
            _identity.GetNextMissionId(), missionType, chosenOffensive.TargetFaction, 0);
        Order newOrder = new(
            _identity.GetNextOrderId(),
            generatedSquads,
            missionType is MissionType.LightningRaid or MissionType.Ambush,
            true,
            aggression,
            newMission,
            faction)
        {
            OpensWithAmbush = opensWithAmbush
        };
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

    /// <summary>Planning budget the staging regions have between them.</summary>
    private static long StagingBudget(
        PotentialOffensive offensive,
        List<RegionForceState> regionalForceStates)
    {
        if (regionalForceStates == null) return 0L;
        return offensive.AttackingRegions
            .Select(region => regionalForceStates
                .FirstOrDefault(state => state.RegionFaction.Region == region)?.SpareTroops ?? 0L)
            .Sum();
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
            + $"defenderBV={offensive.DefenderBattleValue}, "
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
