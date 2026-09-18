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

    /// <summary>
    /// Posts a standing screen on a battle-value budget the allocation auction decided.
    /// </summary>
    /// <remarks>
    /// This used to take a fraction of whatever survived the defensive reserve, which is why a region
    /// facing several neighbours screened nothing: the fraction was applied to zero. The budget now
    /// arrives from the auction, where a patrol's first points of force competed against the
    /// garrison's last ones on marginal value.
    /// </remarks>
    internal void IssueAllocatedPatrol(
        Faction faction,
        Planet planet,
        RegionFaction regionFaction,
        long budget,
        List<Order> allOrders,
        IRNG random)
    {
        {
            if (regionFaction == null || budget <= 0) return;

            long forceBattleValue = budget;

            // A patrol screen is still an order: its budget can be no smaller than the faction's
            // smallest full squad. A region too thin to field even that posts no screen.
            if (forceBattleValue < faction.MinimumForceRequest) return;

            var request = new ForceGenerationRequest
            {
                Faction = faction,
                TargetBattleValue = forceBattleValue,
                Profile = ForceCompositionProfile.ScoutPatrol
            };

            List<Squad> patrolSquads = ForceGenerator.GenerateForce(request, random, _identity);
            if (patrolSquads.Count == 0) return;

            // The patrol is a standing screen, not a sweep: its squads land in the faction's own
            // region and hold, joining the defence if the region is raided and intercepting enemy
            // recon that tries to scout it. These transient forces are cleared before the next pass.
            Mission mission = new Mission(
                _identity.GetNextMissionId(), MissionType.Patrol, regionFaction, 0);
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
                squad.CurrentRegion = regionFaction.Region;
                squad.CurrentOrders = order;
                regionFaction.LandedSquads.Add(squad);
            }
            allOrders.Add(order);
            GameLog.Debug(() =>
                $"AI patrol {faction.Name}/{planet.Name}/{regionFaction.Region.Name}: "
                + $"targetBV={forceBattleValue}, squads={patrolSquads.Count}, "
                + $"soldiers={patrolSquads.Sum(s => s.Members.Count)}, battleValue={SquadBattleValue(patrolSquads)}");
        }
    }

    /// <summary>
    /// Sends a reconnaissance probe on a battle-value budget the allocation auction decided, staged
    /// from the region that contributed most of it.
    /// </summary>
    internal bool IssueAllocatedRecon(
        Faction faction,
        PotentialOffensive target,
        RegionFaction staging,
        long budget,
        Aggression reconAggression,
        List<Order> allOrders,
        IRNG random)
    {
        // A sweep is a scout's job, and the PROFILE is what enforces that. This used to ask for an
        // AssaultForce, which let the generator spend the budget on whatever was affordable -
        // including formations with no squad leader at all, such as the mob roster's single-element
        // Flash Gitz and Lootas. Those returned around -7.7 a week each, and because the weekly
        // margins of every participating squad are POOLED as a signed sum, they cancelled out the
        // competent squads in the same tasking; a faction's awareness could never leave zero however
        // long it scouted.
        long cheapestScoutBattleValue = CheapestScoutSquadBattleValue(faction);
        if (cheapestScoutBattleValue <= 0 || budget <= 0)
        {
            GameLog.Debug(() =>
                $"AI recon {faction.Name}: target={DescribeOffensive(target)}, "
                + $"budget={budget}, cheapestScout={cheapestScoutBattleValue}; "
                + "no order created");
            return false;
        }

        var request = new ForceGenerationRequest
        {
            Faction = faction,
            // The budget is a ceiling rather than a target: it buys full scout squads while it can
            // afford them and falls back to a single understrength party when it cannot. Passing the
            // cheapest full squad's price instead would deny a thin region any reconnaissance at all -
            // a PDF infantry squad is 100 at full strength and 25 at its minimum, so a region holding
            // 91 scouted nothing.
            //
            // Tier is no longer pinned to one squad. Recon is the one mission type that fans out into
            // independent per-squad rolls (MissionForcePolicy.IndependentSquads), and the pooled
            // margin grows as the square root of the squads committed, so a three-squad sweep really
            // does learn more than a one-squad probe. The auction decides how many via
            // ReconSaturationSquads.
            //
            // The leaderless-squad hazard the old cap guarded against is handled by the PROFILE: the
            // original failure came from requesting an AssaultForce, which let the generator spend the
            // budget on formations with no squad leader at all. ScoutPatrol builds scout formations,
            // and CheapestScoutSquadBattleValue below refuses the tasking outright for a faction that
            // has none.
            TargetBattleValue = budget,
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

        Region stagingRegion = staging?.Region
            ?? target.AttackingRegions.FirstOrDefault()
            ?? target.TargetRegion;
        foreach (Squad squad in scouts)
        {
            squad.CurrentRegion = stagingRegion;
        }

        Mission mission = new Mission(
            _identity.GetNextMissionId(), MissionType.Recon, target.TargetFaction, 0);
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

    /// <summary>
    /// How much of a region's strength is worth posting as a standing screen. Read by the task builder
    /// as the patrol task's saturation and its importance.
    /// </summary>
    internal static double CalculatePatrolFraction(Faction faction, Planet planet, RegionForceState state)
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
            + $"defenderBV={offensive.DefenderBattleValue}, "
            + $"estimatedDefenderBV={offensive.EstimatedDefenderBattleValue}";
    }
}
