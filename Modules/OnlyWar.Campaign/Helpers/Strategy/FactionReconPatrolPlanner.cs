using OnlyWar.Generation.World;
using OnlyWar.Domain.Extensions;
using OnlyWar.Operations.StrategicCombat;
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
/// Plans reconnaissance and patrol taskings and owns their one-turn squad lifecycle.
/// </summary>
internal sealed class FactionReconPatrolPlanner
{
    private readonly IPersistentIdAllocator _identity;

    internal FactionReconPatrolPlanner(IPersistentIdAllocator identity = null)
    {
        _identity = identity ?? new PersistentIdAllocator();
    }

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

            List<Squad> patrolSquads = ForceGenerator.GenerateForce(request, random, _identity);
            if (patrolSquads.Count == 0) continue;

            // The patrol is a standing screen, not a sweep: its squads land in the faction's own
            // region and hold, joining the defence if the region is raided and intercepting enemy
            // recon that tries to scout it. These transient forces are cleared before the next pass.
            Mission mission = new Mission(
                _identity.GetNextMissionId(), MissionType.Patrol, state.RegionFaction, 0);
            Order order = new Order(
                _identity.GetNextOrderId(),
                patrolSquads,
                true,
                false,
                Aggression.Cautious,
                mission,
                faction);
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
        // A reconnaissance sweep is a scout's job, so it fields ONE scout squad. This used to ask for
        // an AssaultForce, which let the generator spend the budget on whatever was affordable -
        // including formations with no squad leader at all (the mob roster's Flash Gitz and Lootas
        // are a single Ork Boy element). PerformReconMissionStep resolves its observation through
        // LeaderMissionTest, which then fell back to the best Tactics present: an untrained Ork Boy
        // at attribute-minus-four, or 4.0 against a difficulty of 9.5. Those squads returned about
        // -7.7 a week each and swamped the one competent squad in the same tasking, so a faction's
        // awareness of its neighbours could never leave zero no matter how long it scouted.
        long cheapestScoutBattleValue = CheapestScoutSquadBattleValue(faction);
        if (cheapestScoutBattleValue <= 0 || target.AvailableAttackingForce <= 0)
        {
            GameLog.Debug(() =>
                $"AI recon {faction.Name}: target={DescribeOffensive(target)}, "
                + $"available={target.AvailableAttackingForce}, cheapestScout={cheapestScoutBattleValue}; "
                + "no order created");
            return false;
        }

        var request = new ForceGenerationRequest
        {
            Faction = faction,
            // Tier caps the probe at one squad, so this budget is a ceiling rather than a target:
            // it buys one full scout squad when the region can afford one, and falls back to a
            // single understrength party when it cannot. Passing the cheapest full squad's price
            // instead would deny a thin region any reconnaissance at all - a PDF infantry squad is
            // 100 at full strength and 25 at its minimum, so a region holding 91 scouted nothing.
            TargetBattleValue = target.AvailableAttackingForce,
            Tier = 1,
            Profile = ForceCompositionProfile.ScoutPatrol
        };
        List<Squad> scouts = ForceGenerator.GenerateForce(request, random, _identity);
        if (scouts.Count == 0)
        {
            GameLog.Debug(() =>
                $"AI recon {faction.Name}: target={DescribeOffensive(target)}, "
                + $"cheapestScout={cheapestScoutBattleValue}, generated=0; no order created");
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

        Mission mission = new Mission(
            _identity.GetNextMissionId(), MissionType.Recon, target.TargetFaction, 0);
        Aggression reconAggression = ChooseReconAggression(faction, target.TargetRegion);
        Order order = new Order(
            _identity.GetNextOrderId(),
            scouts,
            true,
            false,
            reconAggression,
            mission,
            faction);
        allOrders.Add(order);
        GameLog.Debug(() =>
            $"AI recon {faction.Name}: target={DescribeOffensive(target)}, staging={stagingRegion.Name}, "
            + $"cheapestScout={cheapestScoutBattleValue}, generatedSquads={scouts.Count}, "
            + $"generatedSoldiers={scouts.Sum(s => s.Members.Count)}, generatedBV={SquadBattleValue(scouts)}, "
            + $"observer={DescribeObserver(scouts)}");
        return true;
    }

    /// <summary>
    /// What a single scout squad costs this faction, or zero when it has no scout formation.
    /// </summary>
    /// <remarks>
    /// ScoutPatrol builds full squads, so the template's own battle value is the price. Gating on
    /// this rather than on Faction.MinimumForceRequest matters: the latter is the cheapest squad of
    /// ANY kind, which for the mob roster is a 30-point Nobz remnant, while its only scout formation
    /// costs a good deal more.
    /// </remarks>
    internal static long CheapestScoutSquadBattleValue(Faction faction) =>
        faction?.SquadTemplates?.Values
            .Where(st => st.IsPresentOperationalForce
                && (st.SquadType & SquadTypes.Scout) != 0
                && st.BattleValue > 0)
            .Select(st => (long)st.BattleValue)
            .DefaultIfEmpty(0L)
            .Min() ?? 0L;

    private static string DescribeObserver(IEnumerable<Squad> scouts)
    {
        Squad squad = scouts.FirstOrDefault();
        return squad?.SquadLeader?.Template?.Name ?? "no squad leader";
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
