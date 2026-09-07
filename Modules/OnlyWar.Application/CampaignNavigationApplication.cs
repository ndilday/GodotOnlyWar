using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Models;
using OnlyWar.Models.Command;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;

namespace OnlyWar.Application;

public sealed partial class CampaignApplication : ICampaignNavigationApplication
{
    public CampaignNavigationRoute ResolveNavigation(
        CampaignNavigationTargetKind kind, int? primaryId)
    {
        GameSession session = _activeSession;
        if (session == null) return CampaignNavigationRoute.None;

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
                    && session.Sector.Fleets.TryGetValue(primaryId.Value, out TaskForce fleet)
                    && fleet.Faction == session.Sector.PlayerForce.Faction
                        ? new CampaignNavigationRoute(CampaignNavigationRouteKind.Fleet)
                        : CampaignNavigationRoute.None;
            case CampaignNavigationTargetKind.Squad:
                return primaryId.HasValue
                    ? ResolveSquadLocation(primaryId.Value)
                    : CampaignNavigationRoute.None;
            case CampaignNavigationTargetKind.Soldier:
                return primaryId.HasValue && FindPlayerSoldier(session, primaryId.Value) != null
                    ? new CampaignNavigationRoute(
                        CampaignNavigationRouteKind.Soldier, SoldierId: primaryId)
                    : CampaignNavigationRoute.None;
            case CampaignNavigationTargetKind.Planet:
                return primaryId.HasValue
                    && session.Sector.Planets.ContainsKey(primaryId.Value)
                        ? new CampaignNavigationRoute(
                            CampaignNavigationRouteKind.PlanetOperations, PlanetId: primaryId)
                        : CampaignNavigationRoute.None;
            case CampaignNavigationTargetKind.Region:
                return primaryId.HasValue
                    ? RegionRoute(FindRegion(session, primaryId.Value))
                    : CampaignNavigationRoute.None;
            case CampaignNavigationTargetKind.Mission:
                Mission mission = primaryId.HasValue
                    ? session.Sector.Planets.Values
                        .SelectMany(planet => planet.Regions)
                        .SelectMany(region => region?.SpecialMissions ?? [])
                        .FirstOrDefault(candidate => candidate?.Id == primaryId.Value)
                    : null;
                return RegionRoute(mission?.RegionFaction?.Region);
            case CampaignNavigationTargetKind.Order:
                if (!primaryId.HasValue
                    || !session.Sector.Orders.TryGetValue(primaryId.Value, out Order order)
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

    public CampaignNavigationRoute ResolveSquadLocation(int squadId)
    {
        GameSession session = _activeSession;
        Squad squad = session?.Sector.PlayerForce?.Army?.OrderOfBattle?.GetAllSquads()
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

    public int? QueryRegionPlanet(int regionId) =>
        FindRegion(_activeSession, regionId)?.Planet?.Id;

    public string QueryPlanetName(int planetId) =>
        _activeSession != null
            && _activeSession.Sector.Planets.TryGetValue(planetId, out Planet planet)
                ? planet.Name
                : null;

    private static CampaignNavigationRoute RegionRoute(Region region) =>
        region?.Planet == null
            ? CampaignNavigationRoute.None
            : new CampaignNavigationRoute(
                CampaignNavigationRouteKind.RegionOperations,
                PlanetId: region.Planet.Id,
                RegionId: region.Id);

    private static Region FindRegion(GameSession session, int regionId) =>
        session?.Sector.Planets.Values
            .SelectMany(planet => planet.Regions)
            .FirstOrDefault(candidate => candidate?.Id == regionId);

    private static PlayerSoldier FindPlayerSoldier(GameSession session, int soldierId)
    {
        PlayerForce force = session.Sector.PlayerForce;
        return force.Army.PlayerSoldierMap.TryGetValue(soldierId, out PlayerSoldier soldier)
            ? soldier
            : force.Army.FallenBrothers.GetValueOrDefault(soldierId);
    }
}
