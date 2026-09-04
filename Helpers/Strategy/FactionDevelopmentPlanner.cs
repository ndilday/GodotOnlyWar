using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Fortifications;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Strategy;

/// <summary>
/// Allocates a region's remaining planning budget to construction policies.
/// </summary>
internal sealed class FactionDevelopmentPlanner
{
    // Each pass either completes a whole level or drains a region below the minimum spend. The cap is
    // a backstop against degenerate floating-point behaviour.
    private const int MaxDevelopmentIterations = 256;
    internal const long MinimumDevelopmentSpendTroops = 100;

    private const long DefenseBaseBuildCost = 2;
    private const int DefenseCostCapLevel = 19;

    private sealed class DevelopmentOption
    {
        public DefenseType DefenseType { get; set; }
        public long Cost { get; set; }
        public double Score { get; set; }
    }

    internal void GenerateEfficientDevelopmentOrders(
        Faction faction,
        List<RegionForceState> regionalForceStates,
        List<Order> allOrders)
    {
        // Project each stat as we plan this turn's builds; the stats themselves only change at
        // resolution. Defense levels are fractional and can absorb the remaining budget.
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
                // affordable outright; fractional defenses can absorb any budget.
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
                // Buy up to the next whole level at this band's price, less if the budget runs out.
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

            allOrders.Add(new Order(new List<Squad>(), true, false, Aggression.Avoid, mission, faction));
            best.State.SpareTroops = Math.Max(0, best.State.SpareTroops - spend);
            projected[best.State] = (org, det, ent, aa);

            GameLog.Trace(() =>
                $"AI efficient construction {best.State.RegionFaction.PlanetFaction.Faction.Name}/"
                + $"{best.State.RegionFaction.Region.Planet.Name}/{best.State.RegionFaction.Region.Name}: "
                + $"{mission.ConstructionType}+{mission.BuildAmount:F2}, spend={spend}, score={best.Option.Score:F2}, "
                + $"spareRemaining={best.State.SpareTroops}");
        }
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

            allOrders.Add(new Order(new List<Squad>(), true, false, Aggression.Avoid,
                new ConstructionMission(DefenseType.ListeningPost, amount, state.RegionFaction), faction));
            state.SpareTroops = Math.Max(0, state.SpareTroops - spend);
            GameLog.Trace(() =>
                $"AI border listening post {faction.Name}/{state.RegionFaction.Region.Planet.Name}/"
                + $"{state.RegionFaction.Region.Name}: Detection+{amount:F2}, spend={spend}, "
                + $"spareRemaining={state.SpareTroops}");
        }
    }

    private static DevelopmentOption BestDevelopmentOption(
        Faction faction,
        RegionForceState state,
        (int Org, double Det, double Ent, double Aa) projected)
    {
        List<DevelopmentOption> options = new();
        RegionFaction rf = state.RegionFaction;
        bool localEnemy = FactionThreatAssessment.HasLocalEnemyMilitary(faction, rf.Region);
        bool adjacentEnemy = FactionThreatAssessment.VisibleAdjacentEnemyMilitary(faction, rf.Region) > 0;
        float ownIntel = rf.GetOwnRegionAwareness();

        long orgCost = projected.Org < 100
            ? (long)(Math.Pow(2, projected.Org / 10) * (rf.Population / 10000.0f)) + 1
            : long.MaxValue;
        AddDevelopmentOption(options, DefenseType.Organization, orgCost,
            (100 - projected.Org) / 25.0 + (localEnemy ? 1.0 : 0.0));

        AddDevelopmentOption(options, DefenseType.ListeningPost, DefenseBuildCost(CurrentLevelBand(projected.Det)),
            (1.0 + Math.Max(0, FactionThreatAssessment.GarrisonFullSightIntel - ownIntel)
                + (adjacentEnemy ? 1.5 : 0.0))
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

    // Exponential build cost for a defense stat: baseCost * 10^currentLevel. At or past the cap the
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
