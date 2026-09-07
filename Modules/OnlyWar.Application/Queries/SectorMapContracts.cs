using System;
using System.Collections.Generic;
using OnlyWar.Models;

namespace OnlyWar.Application;

/// <summary>A grid cell or a planet position, in sector-map grid coordinates.</summary>
public readonly record struct SectorMapCell(int X, int Y);

/// <summary>A point on a subsector boundary loop, in grid coordinates.</summary>
public readonly record struct SectorMapPoint(float X, float Y);

/// <summary>
/// One subsector's geometry as the map draws it: its cells, the planets that anchor it, and the
/// world that holds its governance seat. No live planet or subsector crosses this boundary.
/// </summary>
public sealed record SectorMapSubsector(
    ushort Id,
    string Name,
    IReadOnlyList<SectorMapCell> Cells,
    IReadOnlyList<SectorMapCell> PlanetPositions,
    int? GovernanceSeatPlanetId);

public sealed record SectorMapVoronoiLoop(
    ushort SubsectorId, IReadOnlyList<SectorMapPoint> Points);

/// <summary>A star marker: where it sits and the ARGB of whoever controls it.</summary>
public sealed record SectorMapPlanetMarker(int PlanetId, int X, int Y, int ControllerColorArgb);

/// <summary>
/// A label's text, anchor and colour plus the campaign facts that rank it: outstanding work on
/// the world, the loudest open petition, whether it seats a governor, and its importance.
/// </summary>
public sealed record SectorMapPlanetLabelFacts(
    int PlanetId,
    string Name,
    int X, int Y,
    bool HasController,
    int ControllerColorArgb,
    bool HasActiveWork,
    RequestSeverity RequestSeverity,
    bool IsGovernanceSeat,
    int Importance);

public sealed record SectorMapFleetMarker(int FleetId, int X, int Y, bool IsRealspaceTransit);

/// <summary>
/// The map's slow-changing geometry. Subsector partitioning and the Voronoi border tessellation
/// are campaign geometry, computed here; palette, smoothing and drawing stay in the host.
/// </summary>
public sealed record SectorMapGeometryView(
    bool HasCampaign,
    int GridWidth,
    int GridHeight,
    IReadOnlyList<SectorMapSubsector> Subsectors,
    IReadOnlyList<SectorMapVoronoiLoop> VoronoiLoops,
    IReadOnlyDictionary<ushort, IReadOnlyList<ushort>> SubsectorAdjacency,
    IReadOnlyList<SectorMapPlanetMarker> Planets,
    SectorMapCell? OpeningCenter)
{
    public static readonly SectorMapGeometryView Empty =
        new(false, 0, 0, [], [], new Dictionary<ushort, IReadOnlyList<ushort>>(), [], null);
}

public sealed record SectorMapOrbitingFleet(int FleetId, bool IsPlayerFleet);

public sealed record SectorMapSelectionView(
    bool Exists, int X, int Y, IReadOnlyList<SectorMapOrbitingFleet> OrbitingFleets)
{
    public static readonly SectorMapSelectionView Missing = new(false, 0, 0, []);
}

public interface ISectorMapApplication
{
    Guid SessionToken { get; }
    event EventHandler SessionChanged;

    bool HasCampaign { get; }

    /// <summary>Grid dimensions alone, for the map metrics set before the campaign is drawn.</summary>
    bool TryQuerySectorGrid(out int width, out int height);

    SectorMapGeometryView QuerySectorMapGeometry(bool useVoronoiBorders);

    IReadOnlyList<SectorMapFleetMarker> QuerySectorMapFleets();

    /// <summary>The turn-varying label facts. Geometry is left untouched.</summary>
    IReadOnlyList<SectorMapPlanetLabelFacts> QuerySectorMapPlanetLabels();

    SectorMapSelectionView QuerySectorMapSelection(int planetId);
}
