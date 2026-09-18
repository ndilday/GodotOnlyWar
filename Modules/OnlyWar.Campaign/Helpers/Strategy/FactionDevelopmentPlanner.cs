using OnlyWar.Domain;
using OnlyWar.Domain.Extensions;
using OnlyWar.Domain.Fortifications;
using OnlyWar.Domain;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Squads;
using OnlyWar.Runtime.Allocators;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Campaign.Strategy;

/// <summary>
/// Allocates a region's remaining planning budget to construction policies.
/// </summary>
internal sealed class FactionDevelopmentPlanner
{
    private readonly IPersistentIdAllocator _identity;

    internal FactionDevelopmentPlanner(IPersistentIdAllocator identity = null)
    {
        _identity = identity ?? new PersistentIdAllocator();
    }

    internal const long MinimumDevelopmentSpendTroops = 100;

    private const long DefenseBaseBuildCost = 2;
    private const int DefenseCostCapLevel = 19;

    /// <summary>
    /// Every construction option open to a region, as raw (cost, benefit, amount) figures for the
    /// allocation auction to price.
    /// </summary>
    /// <remarks>
    /// The defence types are deliberately separate tasks rather than one "construct" task, so the
    /// auction can price them against each other and against everything else. Organization is not a
    /// fortification at all - it is whether the region can field its troops, bought in whole integer
    /// points at a hundred times the unit cost.
    ///
    /// ANTI-AIR IS DELIBERATELY ABSENT. Nothing in combat resolution reads RegionFaction.AntiAir: it
    /// reaches only the intelligence watch sums and the UI. Until a resolver consumes it, spending an
    /// army's battle value on it is a straight loss, and the AI should not do it. Restore the entry
    /// below when anti-air acquires an effect - the benefit expression it used was
    /// `0.25 + (localEnemy || adjacentEnemy ? 0.5 : 0.0)`, which will need revisiting against whatever
    /// that effect turns out to be.
    /// </remarks>
    internal static IEnumerable<(RegionForceState State, DefenseType Type, long Cost, double Benefit, double Amount)>
        EnumerateDevelopmentOptions(Faction faction, RegionForceState state)
    {
        RegionFaction rf = state.RegionFaction;
        var projected = (rf.Organization, (double)rf.ListeningPost, (double)rf.Entrenchment, (double)rf.AntiAir);
        bool localEnemy = FactionThreatAssessment.HasLocalEnemyMilitary(faction, rf.Region);
        bool adjacentEnemy = FactionThreatAssessment.VisibleAdjacentEnemyMilitary(faction, rf.Region) > 0;
        float ownIntel = rf.GetOwnRegionAwareness();

        long orgCost = projected.Organization < 100
            ? (long)(Math.Pow(2, projected.Organization / 10) * (rf.Population / 10000.0f)) + 1
            : long.MaxValue;
        if (orgCost > 0 && orgCost != long.MaxValue)
        {
            yield return (state, DefenseType.Organization, orgCost,
                (100 - projected.Organization) / 25.0 + (localEnemy ? 1.0 : 0.0), 1.0);
        }

        // Entrenchment is a MULTIPLIER on the garrison, not a good in its own right: every effect it
        // has - StrategicCombatRules.DefenderProtection, the aftermath casualty multiplier,
        // EntrenchmentMultiplier - scales the defenders standing in the region. Works protecting nobody
        // protect nothing, so the benefit is scaled by how far the region is toward being manned at all.
        //
        // Grist Nine, 2026-09-16: the Imperial PDF spent 1,101 battle value on works against 298 on
        // defending, and withdrew in the same week. Per point, a first level of entrenchment is a
        // genuinely excellent deal - the build economy is logarithmic, so the first level is cheap -
        // and priced alone it beat everything. It was multiplying a garrison that was not there.
        double manned = state.RequiredDefensiveBattleValue <= 0
            ? 1.0
            : Math.Clamp(
                rf.GetDeployedStrength() / (double)state.RequiredDefensiveBattleValue, 0.0, 1.0);

        foreach ((DefenseType type, double level, double benefit) in new[]
        {
            // A listening post is not a multiplier - it watches ground whether or not anyone is
            // holding it - so it keeps its standalone value.
            (DefenseType.ListeningPost, projected.Item2,
                1.0 + Math.Max(0, FactionThreatAssessment.GarrisonFullSightIntel - ownIntel)
                    + (adjacentEnemy ? 1.5 : 0.0)),
            (DefenseType.Entrenchment, projected.Item3,
                (0.5 + (localEnemy ? 4.0 : 0.0) + (adjacentEnemy ? 2.0 : 0.0)) * manned)
        })
        {
            long cost = DefenseBuildCost(CurrentLevelBand(level));
            if (cost <= 0 || cost == long.MaxValue) continue;
            yield return (state, type, cost,
                benefit * SharedEfficiency(rf, type, level),
                CurrentLevelBand(level) + 1.0 - level);
        }
    }

    /// <summary>Issues one construction order for the battle value the auction awarded it.</summary>
    internal void IssueAllocatedConstruction(
        Faction faction,
        RegionFaction regionFaction,
        DefenseType defenseType,
        long budget,
        List<Order> allOrders)
    {
        if (regionFaction == null || budget <= 0) return;

        double amount;
        if (defenseType == DefenseType.Organization)
        {
            long orgCost = regionFaction.Organization < 100
                ? (long)(Math.Pow(2, regionFaction.Organization / 10) * (regionFaction.Population / 10000.0f)) + 1
                : long.MaxValue;
            // Organization is an integer percentage bought in whole points, so it must be affordable
            // outright; a partial payment buys nothing at all.
            if (orgCost == long.MaxValue || orgCost * 100L > budget) return;
            amount = 1.0;
        }
        else
        {
            double level = defenseType switch
            {
                DefenseType.ListeningPost => regionFaction.ListeningPost,
                DefenseType.Entrenchment => regionFaction.Entrenchment,
                _ => regionFaction.AntiAir
            };
            long cost = DefenseBuildCost(CurrentLevelBand(level));
            if (cost <= 0 || cost == long.MaxValue) return;
            long costPerLevel = cost * 100L;
            // Fractional defences absorb any budget, so a thin region builds what it can afford rather
            // than staying blind.
            amount = Math.Min(CurrentLevelBand(level) + 1.0 - level, budget / (double)costPerLevel);
            if (amount <= 0.0) return;
        }

        allOrders.Add(new Order(
            _identity.GetNextOrderId(),
            new List<Squad>(),
            true,
            false,
            Aggression.Avoid,
            new ConstructionMission(
                _identity.GetNextMissionId(), defenseType, amount, regionFaction),
            faction));

        GameLog.Trace(() =>
            $"AI allocated construction {faction.Name}/{regionFaction.Region.Planet.Name}/"
            + $"{regionFaction.Region.Name}: {defenseType}+{amount:F2}, budget={budget}");
    }

    internal void GenerateBorderListeningPosts(
        Faction faction,
        List<RegionForceState> states,
        List<Order> allOrders)
    {
        foreach (RegionForceState state in states)
        {
            if (state.SpareTroops < MinimumDevelopmentSpendTroops) continue;

            bool bordersPublicEnemy = FactionThreatAssessment.GetBelievedTargets(
                    faction, state.RegionFaction.Region.Planet)
                .Any(target => target.CurrentPresence?.IsPublic == true
                    && state.RegionFaction.Region.GetAdjacentRegions().Contains(target.Region));
            if (!bordersPublicEnemy) continue;

            double level = state.RegionFaction.ListeningPost;
            long detCost = DefenseBuildCost(CurrentLevelBand(level));
            if (detCost == long.MaxValue) continue;

            // Still at most one level per turn, but a thin region can build the fractional amount its
            // spare force covers instead of staying blind.
            long costPerLevel = detCost * 100L;
            double amount = Math.Min(CurrentLevelBand(level) + 1.0 - level,
                (double)state.SpareTroops / costPerLevel);
            long spend = (long)Math.Ceiling(amount * costPerLevel);

            allOrders.Add(new Order(
                _identity.GetNextOrderId(),
                new List<Squad>(),
                true,
                false,
                Aggression.Avoid,
                new ConstructionMission(
                    _identity.GetNextMissionId(),
                    DefenseType.ListeningPost,
                    amount,
                    state.RegionFaction),
                faction));
            state.SpareTroops = Math.Max(0, state.SpareTroops - spend);
            GameLog.Trace(() =>
                $"AI border listening post {faction.Name}/{state.RegionFaction.Region.Planet.Name}/"
                + $"{state.RegionFaction.Region.Name}: Detection+{amount:F2}, spend={spend}, "
                + $"spareRemaining={state.SpareTroops}");
        }
    }

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

    // cost is effectively infinite, plateauing a defense rather than overflowing.
    internal static long DefenseBuildCost(int level)
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

    internal static int CurrentLevelBand(double level) =>
        (int)Math.Floor(Math.Max(0.0, level) + 1e-9);
}
