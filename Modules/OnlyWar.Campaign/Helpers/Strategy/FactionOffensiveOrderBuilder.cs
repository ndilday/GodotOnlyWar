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
/// Target selection remains in <see cref="FactionOffensiveEvaluator"/>, and sizing and staging are
/// decided by the allocation auction. This owner is deliberately concerned only with the issuance
/// boundary: live-pool mutations, routing, and the tactical return paths.
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

    // Surprise opens the advance; it does not replace it. Selecting MissionType.Ambush instead would
    // swap in the whole ambush chain - including its MissionReturnPolicy.Return - so a force sized to
    // TAKE the region would strike once and withdraw from it. The mission stays an Advance and holds
    // what it takes; the first day is fought from ambush (MissionStepOrchestrator's opening-ambush
    // chain).
    //
    // Surprise belongs to a region that went public this week (FactionRevealService). It is not spent
    // by using it: FactionStrategyController clears every advantage at the end of the planning pass, so
    // it lasts exactly the one turn whether or not anything was launched with it.
    private static bool HasEmergenceAdvantage(Faction faction, PotentialOffensive offensive) =>
        offensive.AttackingRegions
            .Select(region => region.RegionFactionMap.TryGetValue(faction.Id, out RegionFaction rf) ? rf : null)
            .Any(rf => rf?.HasEmergenceAdvantage == true);

    /// <summary>
    /// Everything after the force has been committed: choose strategic or tactical resolution, build
    /// the mission, and record the staging region survivors withdraw to.
    /// </summary>
    /// <remarks>
    /// The allocation auction arrives here with its contributions already decided and its regional
    /// budgets already debited.
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
        // A faction that cannot field a tactical force has to resolve strategically: the tactical path
        // generates nothing, and FinishOffensive then abandons the offensive and hands the battle value
        // back, so the attack simply never happens.
        //
        // This used to be keyed on GrowthType.Unrest, on the grounds that secular insurgents were
        // abstract embedded-PDF and armed-civilian pools with no squad templates. The Insurrectionists
        // have templates now - a Mob and a Weapon Team, plus a Firebrand HQ - so the GROWTH TYPE is the
        // wrong test and they should size like anyone else. The CONDITION it stood for is still real,
        // and any faction with no usable template meets it, whatever its growth type.
        if (attacker.MinimumFullSquadRequest <= 0) return true;

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
