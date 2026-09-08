using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Geometry;
using OnlyWar.Models.Planets;

namespace OnlyWar.Application;

public sealed class SectorMapApplication : CampaignScreenApplication, ISectorMapApplication
{
    public SectorMapApplication(CampaignApplicationContext context) : base(context) { }

    public bool HasCampaign => ActiveSession != null;

    public bool TryQuerySectorGrid(out int width, out int height)
    {
        width = 0;
        height = 0;
        if (ActiveSession == null) return false;

        SectorGenerationProfile profile = ActiveSession.Rules.SectorGenerationProfile;
        width = profile.SectorWidth;
        height = profile.SectorHeight;
        return true;
    }

    public SectorMapGeometryView QuerySectorMapGeometry(bool useVoronoiBorders)
    {
        GameSession session = ActiveSession;
        if (session == null) return SectorMapGeometryView.Empty;

        SectorGenerationProfile profile = session.Rules.SectorGenerationProfile;
        GridCell grid = new(profile.SectorWidth, profile.SectorHeight);
        List<Subsector> subsectors = SubsectorBuilder.BuildSubsectors(
            session.Sector.Planets.Values, grid, profile.MaxSubsectorDiameter);

        // The generated partition is geometry only; the seat each subsector actually holds comes
        // from the campaign's own subsector records.
        IReadOnlyList<Subsector> sectorSubsectors = session.Sector.Subsectors;
        List<SectorMapSubsector> projected = subsectors
            .Select(subsector =>
            {
                Subsector source = sectorSubsectors.FirstOrDefault(
                    candidate => candidate.Id == subsector.Id);
                return new SectorMapSubsector(
                    subsector.Id,
                    source?.Name ?? subsector.Name,
                    subsector.Cells.Select(cell => new SectorMapCell(cell.X, cell.Y)).ToList(),
                    subsector.Planets
                        .Select(planet => new SectorMapCell(planet.Position.X, planet.Position.Y))
                        .ToList(),
                    source?.GovernanceSeat?.Id);
            })
            .ToList();

        List<SectorMapVoronoiLoop> loops = [];
        Dictionary<ushort, IReadOnlyList<ushort>> adjacency = new();
        if (useVoronoiBorders)
        {
            Dictionary<ushort, List<Planet>> subsectorPlanetMap = subsectors
                .ToDictionary(subsector => subsector.Id, subsector => subsector.Planets);
            VoronoiSubsectorMapper.SubsectorBorders borders =
                VoronoiSubsectorMapper.BuildSubsectorLoops(
                    subsectorPlanetMap, grid, profile.MaxSubsectorDiameter);
            loops = borders.Loops
                .SelectMany(pair => pair.Value.Select(loop => new SectorMapVoronoiLoop(
                    pair.Key,
                    loop.Select(point => new SectorMapPoint(point.X, point.Y)).ToList())))
                .ToList();
            foreach (ushort id in subsectors.Select(subsector => subsector.Id))
            {
                adjacency[id] = borders.Adjacency.TryGetValue(id, out HashSet<ushort> neighbors)
                    ? neighbors.ToList()
                    : [];
            }
            foreach (KeyValuePair<ushort, HashSet<ushort>> pair in borders.Adjacency)
            {
                adjacency[pair.Key] = pair.Value.ToList();
            }
        }

        // The map opens on the chapter's own fleet when it has one, and on any charted world
        // otherwise, so a campaign with its fleet in the warp still has somewhere to look.
        TaskForce centerFleet = session.Sector.PlayerForce?.Fleet?.TaskForces.FirstOrDefault();
        Coordinate? center = centerFleet?.Planet?.Position
            ?? centerFleet?.Position
            ?? session.Sector.Planets.Values.FirstOrDefault()?.Position;

        return new SectorMapGeometryView(
            true,
            profile.SectorWidth,
            profile.SectorHeight,
            projected,
            loops,
            adjacency,
            session.Sector.Planets.Values
                .Select(planet => new SectorMapPlanetMarker(
                    planet.Id,
                    planet.Position.X,
                    planet.Position.Y,
                    ToArgb(planet.GetControllingFaction()?.Color ?? System.Drawing.Color.Gray)))
                .ToList(),
            center.HasValue ? new SectorMapCell(center.Value.X, center.Value.Y) : null);
    }

    public IReadOnlyList<SectorMapFleetMarker> QuerySectorMapFleets()
    {
        GameSession session = ActiveSession;
        if (session == null) return [];

        List<SectorMapFleetMarker> markers = [];
        foreach (KeyValuePair<int, TaskForce> pair in session.Sector.Fleets)
        {
            TaskForce taskForce = pair.Value;
            // A task force in the warp is out of contact and is not drawn at all.
            if (taskForce.TravelPhase == FleetTravelPhase.InWarp) continue;

            bool isRealspaceTransit =
                taskForce.TravelPhase == FleetTravelPhase.OutboundSystemTransit
                || taskForce.TravelPhase == FleetTravelPhase.InboundSystemTransit;
            Coordinate? position = taskForce.Planet != null
                ? taskForce.Planet.Position
                : isRealspaceTransit
                    ? TransitAnchorPosition(taskForce)
                    : taskForce.Position;
            if (position == null) continue;

            markers.Add(new SectorMapFleetMarker(
                pair.Key, position.Value.X, position.Value.Y, isRealspaceTransit));
        }
        return markers;
    }

    public IReadOnlyList<SectorMapPlanetLabelFacts> QuerySectorMapPlanetLabels()
    {
        GameSession session = ActiveSession;
        if (session == null) return [];

        HashSet<int> governanceSeats = session.Sector.Subsectors
            .Where(subsector => subsector.GovernanceSeat != null)
            .Select(subsector => subsector.GovernanceSeat.Id)
            .ToHashSet();

        return session.Sector.Planets.Values
            .Where(planet => !string.IsNullOrWhiteSpace(planet.Name))
            .OrderBy(planet => planet.Id)
            .Select(planet =>
            {
                bool hasActiveRequest = false;
                RequestSeverity severity = RequestSeverity.Concerned;
                foreach (IRequest request in session.Sector.PlayerForce?.Requests ?? [])
                {
                    if (request.TargetPlanet != planet
                        || request.Status is not (RequestStatus.Open or RequestStatus.InProgress))
                    {
                        continue;
                    }

                    hasActiveRequest = true;
                    severity = (RequestSeverity)Math.Max((int)severity, (int)request.Severity);
                }

                bool hasActiveMission = planet.Regions
                    .Where(region => region != null)
                    .Any(region => region.SpecialMissions.Count > 0);
                bool hasActiveOrder = session.Sector.Orders.Values
                    .Any(order => order.Mission?.RegionFaction?.Region?.Planet == planet);
                Faction controller = planet.GetControllingFaction();

                return new SectorMapPlanetLabelFacts(
                    planet.Id,
                    planet.Name,
                    planet.Position.X,
                    planet.Position.Y,
                    controller != null,
                    controller == null ? 0 : ToArgb(controller.Color),
                    hasActiveRequest || hasActiveMission || hasActiveOrder,
                    severity,
                    governanceSeats.Contains(planet.Id),
                    planet.Importance);
            })
            .ToList();
    }

    public SectorMapSelectionView QuerySectorMapSelection(int planetId)
    {
        GameSession session = ActiveSession;
        if (session == null || !session.Sector.Planets.TryGetValue(planetId, out Planet planet))
        {
            return SectorMapSelectionView.Missing;
        }

        Faction playerFaction = session.Sector.PlayerForce?.Faction;
        return new SectorMapSelectionView(
            true,
            planet.Position.X,
            planet.Position.Y,
            session.Sector.Fleets.Values
                .Where(fleet => fleet.Planet == planet
                    && fleet.TravelPhase == FleetTravelPhase.InOrbit)
                .OrderBy(fleet => fleet.Id)
                .Select(fleet => new SectorMapOrbitingFleet(
                    fleet.Id, fleet.Faction == playerFaction))
                .ToList());
    }

    private static Coordinate? TransitAnchorPosition(TaskForce taskForce) =>
        taskForce.TravelPhase switch
        {
            FleetTravelPhase.OutboundSystemTransit =>
                taskForce.Origin?.Position ?? taskForce.Position,
            FleetTravelPhase.InboundSystemTransit =>
                taskForce.Destination?.Position ?? taskForce.Position,
            _ => taskForce.Position
        };

    private static int ToArgb(System.Drawing.Color color) => color.ToArgb();
}
