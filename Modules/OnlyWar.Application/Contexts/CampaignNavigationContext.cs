using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;

namespace OnlyWar.Application;

/// <summary>
/// Resolves navigation targets from the campaign state without exposing that state to the host.
/// </summary>
internal sealed class CampaignNavigationContext
{
    private readonly Sector _sector;

    internal CampaignNavigationContext(Sector sector)
    {
        _sector = sector ?? throw new ArgumentNullException(nameof(sector));
    }

    internal CampaignNavigationRoute ResolveNavigation(
        CampaignNavigationTargetKind kind, int? primaryId)
    {
        switch (kind)
        {
            case CampaignNavigationTargetKind.LastTurnReport:
                return new CampaignNavigationRoute(
                    CampaignNavigationRouteKind.LastTurnReport);
            case CampaignNavigationTargetKind.Recruitment:
                return new CampaignNavigationRoute(CampaignNavigationRouteKind.Recruitment);
            case CampaignNavigationTargetKind.SectorMap:
                return new CampaignNavigationRoute(CampaignNavigationRouteKind.SectorMap);
            case CampaignNavigationTargetKind.Diplomacy:
                return new CampaignNavigationRoute(
                    CampaignNavigationRouteKind.Diplomacy, FocusId: primaryId);
            case CampaignNavigationTargetKind.Apothecarium:
                return new CampaignNavigationRoute(
                    CampaignNavigationRouteKind.Apothecarium, FocusId: primaryId);
            case CampaignNavigationTargetKind.Fleet:
                return primaryId.HasValue
                    && _sector.Fleets.TryGetValue(primaryId.Value, out TaskForce fleet)
                    && fleet.Faction == _sector.PlayerForce.Faction
                        ? new CampaignNavigationRoute(CampaignNavigationRouteKind.Fleet)
                        : CampaignNavigationRoute.None;
            case CampaignNavigationTargetKind.Squad:
                return primaryId.HasValue
                    ? ResolveSquadLocation(primaryId.Value)
                    : CampaignNavigationRoute.None;
            case CampaignNavigationTargetKind.Soldier:
                return primaryId.HasValue && FindPlayerSoldier(primaryId.Value) != null
                    ? new CampaignNavigationRoute(
                        CampaignNavigationRouteKind.Soldier, SoldierId: primaryId)
                    : CampaignNavigationRoute.None;
            case CampaignNavigationTargetKind.Planet:
                return primaryId.HasValue
                    && _sector.Planets.ContainsKey(primaryId.Value)
                        ? new CampaignNavigationRoute(
                            CampaignNavigationRouteKind.PlanetOperations, PlanetId: primaryId)
                        : CampaignNavigationRoute.None;
            case CampaignNavigationTargetKind.Region:
                return primaryId.HasValue
                    ? RegionRoute(FindRegion(primaryId.Value))
                    : CampaignNavigationRoute.None;
            case CampaignNavigationTargetKind.Mission:
                Mission mission = primaryId.HasValue
                    ? _sector.Planets.Values
                        .SelectMany(planet => planet.Regions)
                        .SelectMany(region => region?.SpecialMissions ?? [])
                        .FirstOrDefault(candidate => candidate?.Id == primaryId.Value)
                    : null;
                return RegionRoute(mission?.RegionFaction?.Region);
            case CampaignNavigationTargetKind.Order:
                if (!primaryId.HasValue
                    || !_sector.Orders.TryGetValue(primaryId.Value, out Order order)
                    || order == null)
                {
                    return CampaignNavigationRoute.None;
                }
                // An order fought on the ground opens its region; one with no ground - an
                // orbital or transit commitment - opens the squad carrying it out instead.
                if (order.Mission?.RegionFaction?.Region is Region orderRegion)
                {
                    return RegionRoute(orderRegion);
                }
                return order.AssignedSquads?.FirstOrDefault() is Squad orderedSquad
                    ? ResolveSquadLocation(orderedSquad.Id)
                    : CampaignNavigationRoute.None;
            default:
                return CampaignNavigationRoute.None;
        }
    }

    internal CampaignNavigationRoute ResolveSquadLocation(int squadId)
    {
        Squad squad = _sector.PlayerForce?.Army?.OrderOfBattle?.GetAllSquads()
            .FirstOrDefault(candidate => candidate.Id == squadId);
        if (squad == null) return CampaignNavigationRoute.None;

        SquadLocationNavigationTarget target = SquadLocationNavigation.Resolve(squad);
        if (target == null) return CampaignNavigationRoute.None;

        return target.Kind == SquadLocationNavigationKind.Region
            ? new CampaignNavigationRoute(
                CampaignNavigationRouteKind.SquadInRegion,
                PlanetId: target.Region?.Planet?.Id,
                RegionId: target.Region?.Id,
                SquadId: squadId)
            : new CampaignNavigationRoute(
                CampaignNavigationRouteKind.SquadOnShip, SquadId: squadId);
    }

    internal int? QueryRegionPlanet(int regionId) =>
        FindRegion(regionId)?.Planet?.Id;

    internal string QueryPlanetName(int planetId) =>
        _sector.Planets.TryGetValue(planetId, out Planet planet)
            ? planet.Name
            : null;

    private Region FindRegion(int regionId) =>
        _sector.Planets.Values
            .SelectMany(planet => planet.Regions)
            .FirstOrDefault(candidate => candidate?.Id == regionId);

    private PlayerSoldier FindPlayerSoldier(int soldierId)
    {
        PlayerForce force = _sector.PlayerForce;
        return force?.Army?.PlayerSoldierMap.TryGetValue(soldierId, out PlayerSoldier soldier) == true
            ? soldier
            : force?.Army?.FallenBrothers.GetValueOrDefault(soldierId);
    }

    private static CampaignNavigationRoute RegionRoute(Region region) =>
        region?.Planet == null
            ? CampaignNavigationRoute.None
            : new CampaignNavigationRoute(
                CampaignNavigationRouteKind.RegionOperations,
                PlanetId: region.Planet.Id,
                RegionId: region.Id);
}
