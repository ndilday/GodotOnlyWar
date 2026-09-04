using OnlyWar.Builders;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.StrategicCombat;
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
/// Plans reconnaissance and patrol taskings and owns their one-turn squad lifecycle.
/// </summary>
internal sealed class FactionReconPatrolPlanner
{
    internal const double PatrolForceFraction = 0.1;
    internal const double PolicingPatrolFraction = 0.05;
    internal const double WorthScreeningWorksLevel = 1.0;
    internal const float UnfamiliarGroundIntel = 1.0f;

    internal void PlanPatrolMissionsOnPlanet(
        Faction faction,
        Planet planet,
        List<RegionForceState> regionalForceStates,
        List<Order> allOrders,
        IRNG random)
    {
        foreach (RegionForceState state in regionalForceStates)
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
                Profile = ForceCompositionProfile.ScoutPatrol
            };

            List<Squad> patrolSquads = ForceGenerator.GenerateForce(request, random);
            if (patrolSquads.Count == 0) continue;

            // The patrol is a standing screen, not a sweep: its squads land in the faction's own
            // region and hold, joining the defence if the region is raided and intercepting enemy
            // recon that tries to scout it. These transient forces are cleared before the next pass.
            Mission mission = new Mission(MissionType.Patrol, state.RegionFaction, 0);
            Order order = new Order(patrolSquads, true, false, Aggression.Cautious, mission, faction);
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

    internal bool IssueReconMission(
        Faction faction,
        PotentialOffensive target,
        List<Order> allOrders,
        IRNG random)
    {
        return IssueReconMission(faction, target, null, allOrders, random);
    }

    internal bool IssueReconMission(
        Faction faction,
        PotentialOffensive target,
        List<RegionForceState> regionalForceStates,
        List<Order> allOrders,
        IRNG random)
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
        List<Squad> scouts = ForceGenerator.GenerateForce(request, random);
        if (scouts.Count == 0)
        {
            GameLog.Debug(() =>
                $"AI recon {faction.Name}: target={DescribeOffensive(target)}, requestedBV={request.TargetBattleValue}, "
                + "generated=0; no order created");
            return false;
        }

        Region stagingRegion = FactionStagingPlanner
            .ChooseStagingRegionsByOpportunityCost(target, regionalForceStates)
            .FirstOrDefault()
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
        Order order = new Order(scouts, true, false, reconAggression, mission, faction);
        allOrders.Add(order);
        GameLog.Debug(() =>
            $"AI recon {faction.Name}: target={DescribeOffensive(target)}, staging={stagingRegion.Name}, "
            + $"requestedBV={request.TargetBattleValue}, generatedSquads={scouts.Count}, "
            + $"generatedSoldiers={scouts.Sum(s => s.Members.Count)}, generatedBV={SquadBattleValue(scouts)}");
        return true;
    }

    /// <summary>How boldly this faction scouts a region based on its own existing awareness.</summary>
    internal static Aggression ChooseReconAggression(Faction faction, Region target)
    {
        float known = target.GetFactionRegionAwareness(faction);
        if (known < UnfamiliarGroundIntel) return Aggression.Cautious;
        if (known < FactionThreatAssessment.GarrisonFullSightIntel) return Aggression.Normal;
        return Aggression.Attritional;
    }

    /// <summary>Share of a region's spare troops posted as a standing patrol screen.</summary>
    internal double CalculatePatrolFraction(Faction faction, Planet planet, RegionForceState state)
    {
        // No declared enemy anywhere on the world: ordinary policing only. The works-based tier is
        // allowed to apply because a region worth infiltrating is worth watching whether or not an
        // enemy has declared itself.
        if (!FactionThreatAssessment.HasPublicEnemyOnPlanet(faction, planet))
        {
            return IsWorthScreening(state.RegionFaction)
                ? PatrolForceFraction
                : PolicingPatrolFraction;
        }

        bool localEnemy = FactionThreatAssessment.HasLocalEnemyMilitary(faction, state.RegionFaction.Region);
        bool adjacentEnemy = FactionThreatAssessment.VisibleAdjacentEnemyMilitary(faction, state.RegionFaction.Region) > 0;

        if (localEnemy) return 0.20;
        if (adjacentEnemy) return 0.10;
        if (IsWorthScreening(state.RegionFaction)) return PatrolForceFraction;
        return 0.0;
    }

    internal static bool IsTransientAiSquad(Squad squad)
    {
        MissionType? missionType = squad?.CurrentOrders?.Mission?.MissionType;
        return missionType is MissionType.Patrol or MissionType.Recon;
    }

    internal static void ClearStaleTransientSquads(Faction faction, Sector sector)
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

    private static bool IsWorthScreening(RegionFaction regionFaction)
    {
        double works = regionFaction.Entrenchment
            + regionFaction.ListeningPost
            + regionFaction.AntiAir;
        return works >= WorthScreeningWorksLevel;
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
}
