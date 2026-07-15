using Godot;
using OnlyWar.Builders;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Scenes.MainGameScreen;
using System;
using System.Collections.Generic;
using System.Linq;

enum Facing { North, East, South, West}
struct BorderPoint
{
	public Vector2I gridPos;
	public Vector2I mapPoint;
	public Facing orientation;
}

public partial class SectorMap : Node2D
{
    private readonly struct EdgeKey : IEquatable<EdgeKey>
    {
        public readonly Vector2I A;
        public readonly Vector2I B;

        public EdgeKey(Vector2I a, Vector2I b)
        {
            if (ComparePoints(a, b) <= 0)
            {
                A = a;
                B = b;
            }
            else
            {
                A = b;
                B = a;
            }
        }

        public bool Equals(EdgeKey other)
        {
            return A == other.A && B == other.B;
        }

        public override bool Equals(object obj)
        {
            return obj is EdgeKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(A, B);
        }

        private static int ComparePoints(Vector2I left, Vector2I right)
        {
            int xCompare = left.X.CompareTo(right.X);
            return xCompare != 0 ? xCompare : left.Y.CompareTo(right.Y);
        }
    }

    private static readonly Color[] SubsectorPalette =
    [
        Color.Color8(118, 28, 46),
        Color.Color8(22, 102, 106),
        Color.Color8(86, 55, 132),
        Color.Color8(36, 68, 128),
        Color.Color8(78, 112, 62),
        Color.Color8(125, 88, 42)
    ];
    private static readonly Color SubsectorBorderColor = Color.Color8(176, 132, 66);
    private static readonly Color SubsectorBorderGlowColor = Color.Color8(218, 177, 94);
    private static readonly Color SubsectorBorderShadowColor = Color.Color8(7, 6, 5);
    private const float SubsectorGridFillAlpha = 0.08f;
    private const float SubsectorGlassFillAlpha = 0.24f;
    private const float SubsectorInnerStainAlpha = 0.08f;
    private const float PolygonSimplificationToleranceCellFraction = 1.1f;
    private const float PolygonSimplificationMaxBridgeCellFraction = 4.5f;
    private const int PolygonSmoothingPasses = 2;
    private const float SeamSimplificationToleranceCellFraction = 2.4f;
    private const float SeamSimplificationMaxBridgeCellFraction = 8.0f;
    private const int SeamCurveSamplesPerSegment = 10;

    // PROTOTYPE: when true, subsector regions are drawn from a constrained Voronoi
    // tessellation (VoronoiSubsectorMapper) instead of the grid-traced/smoothed polygons.
    private const bool UseVoronoiBorders = true;

    public event EventHandler<int> PlanetClicked;
    public event EventHandler<int> PlanetDoubleClicked;
    public event EventHandler<int> FleetClicked;
    public event EventHandler<int> FleetRightClicked;

    public Vector2I GridDimensions { get; private set; }
	public Vector2I CellSize { get; private set; }

    public Vector2I HalfCellSize { get; private set; }
    public ushort[] SectorIds { get; private set; }
    public bool[] HasPlanet { get; private set; }
    private Camera2D _camera;
    private Sprite2D _background;
    private readonly List<Node> _fleetSprites = [];
	
	private Dictionary<ushort, List<Vector2I>> _subsectorVertexListMap = [];
    private List<Vector2[]> _subsectorBoundaryPaths = [];
    private Dictionary<ushort, List<Vector2[]>> _voronoiSubsectorLoops = [];
	private Dictionary<ushort, HashSet<ushort>> _subsectorAdjacencyMap = [];
	private Dictionary<ushort, int> _subsectorColorIndexMap = [];
    private int? _selectedPlanetId;

    public override void _EnterTree()
    {
        EnsureMapMetricsInitialized();
    }

    public bool EnsureMapMetricsInitialized()
    {
        if (GridDimensions != Vector2I.Zero && CellSize != Vector2I.Zero) return true;
        if (!GameDataSingleton.Instance.IsInitialized) return false;

        GridDimensions = new(
            GameDataSingleton.Instance.GameRulesData.SectorSize.X,
            GameDataSingleton.Instance.GameRulesData.SectorSize.Y);
        CellSize = new(
            GameDataSingleton.Instance.GameRulesData.SectorCellSize.X,
            GameDataSingleton.Instance.GameRulesData.SectorCellSize.Y);
        HalfCellSize = CellSize / 2;
        return true;
    }

    // Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
        _camera = GetNode<Camera2D>("Camera2D");
        _background = GetNodeOrNull<Sprite2D>("Background");
        if (!EnsureMapMetricsInitialized())
        {
            GD.PushError("SectorMap requires initialized game data before the scene is readied.");
            return;
        }

        LayoutBackground();
		SectorIds = new ushort[GridDimensions.X * GridDimensions.Y];
		HasPlanet = new bool[GridDimensions.X * GridDimensions.Y];
		PlacePlanets();
		RefreshFleets();
		List<Subsector> subsectors = SubsectorBuilder.BuildSubsectors(GameDataSingleton.Instance.Sector.Planets.Values, GridDimensions);
        foreach(Subsector subsector in subsectors)
        {
            foreach (Vector2I cell in subsector.Cells)
            {
                SectorIds[GridPositionToIndex(cell)] = subsector.Id;
            }
        }
        _subsectorAdjacencyMap = DetermineSubsectorAdjacency(subsectors.Select(subsector => subsector.Id));
        _subsectorColorIndexMap = AssignSubsectorColorIndexes(_subsectorAdjacencyMap);
        ValidateSubsectorColoring(_subsectorAdjacencyMap, _subsectorColorIndexMap);
        _subsectorVertexListMap = DetermineSubsectorBorderPoints(subsectors);
        _subsectorBoundaryPaths = DetermineSubsectorBoundaryPaths();
        if (UseVoronoiBorders)
        {
            Dictionary<ushort, List<Planet>> subsectorPlanetMap =
                subsectors.ToDictionary(subsector => subsector.Id, subsector => subsector.Planets);
            var voronoiBorders = OnlyWar.Helpers.VoronoiSubsectorMapper.BuildSubsectorLoops(
                subsectorPlanetMap,
                GridDimensions,
                GameDataSingleton.Instance.GameRulesData.MaxSubsectorCellDiameter);
            _voronoiSubsectorLoops = voronoiBorders.Loops;

            // Recolor from the Voronoi adjacency (shared border edges) so that
            // neighboring subsectors never share a palette color.
            EnsureAdjacencyEntries(voronoiBorders.Adjacency, subsectors.Select(subsector => subsector.Id));
            _subsectorAdjacencyMap = voronoiBorders.Adjacency;
            _subsectorColorIndexMap = AssignSubsectorColorIndexes(_subsectorAdjacencyMap);
            ValidateSubsectorColoring(_subsectorAdjacencyMap, _subsectorColorIndexMap);
        }
        TaskForce centerFleet = GameDataSingleton.Instance.Sector.PlayerForce.Fleet.TaskForces.FirstOrDefault();
        Coordinate? centerPosition = centerFleet?.Planet?.Position ?? centerFleet?.Position;
        if (centerPosition == null)
        {
            centerPosition = GameDataSingleton.Instance.Sector.Planets.Values.First().Position;
        }

        Vector2I gridPosition = new Vector2I(centerPosition.Value.X, centerPosition.Value.Y);
        Vector2I mapPosition = CalculateMapPosition(gridPosition);
        _camera.ZoomTo(1, mapPosition);
    }

    private void LayoutBackground()
    {
        if (_background?.Texture == null) return;

        Vector2 mapSize = new(GridDimensions.X * CellSize.X, GridDimensions.Y * CellSize.Y);
        Vector2 backgroundSize = mapSize + Vector2.One * (2 * _camera.MapBorderPixels);

        _background.Position = mapSize / 2.0f;
        _background.Scale = backgroundSize / _background.Texture.GetSize();
    }

	public override void _Draw()
	{
		base._Draw();
        if (!GameDataSingleton.Instance.IsInitialized) return;

        if (UseVoronoiBorders && _voronoiSubsectorLoops.Count > 0)
        {
            DrawVoronoiSubsectors();
            DrawSelectedSystemOverlay();
            return;
        }

        if (_subsectorVertexListMap.Count == 0) return;

		foreach (var kvp in _subsectorVertexListMap.OrderBy(kvp => kvp.Key))
		{
            Vector2[] polygonPoints = kvp.Value.Select(vector => new Vector2(vector.X, vector.Y)).ToArray();
            Vector2[] smoothedPolygonPoints = BuildSmoothedPolygon(polygonPoints);
            Color baseColor = GetSubsectorColor(kvp.Key);

            DrawSubsectorFill(kvp.Key, polygonPoints, smoothedPolygonPoints, baseColor);
		}
        DrawSubsectorBoundaries();
        DrawSelectedSystemOverlay();
	}

    public void SetSelectedPlanet(int? planetId)
    {
        _selectedPlanetId = planetId;
        QueueRedraw();
    }

    public void ZoomIn()
    {
        _camera.ZoomIn(null);
    }

    public void ZoomOut()
    {
        _camera.ZoomOut(null);
    }

    public void CenterOnSelectedPlanet()
    {
        if (!_selectedPlanetId.HasValue) return;
        if (!GameDataSingleton.Instance.Sector.Planets.TryGetValue(_selectedPlanetId.Value, out Planet planet)) return;

        Vector2I gridPosition = new(planet.Position.X, planet.Position.Y);
        _camera.ZoomTo(_camera.Zoom.X, CalculateMapPosition(gridPosition));
    }

    private Dictionary<ushort, HashSet<ushort>> DetermineSubsectorAdjacency(IEnumerable<ushort> subsectorIds)
    {
        Dictionary<ushort, HashSet<ushort>> adjacencyMap = subsectorIds
            .Distinct()
            .ToDictionary(id => id, _ => new HashSet<ushort>());

        for (int y = 0; y < GridDimensions.Y; y++)
        {
            for (int x = 0; x < GridDimensions.X; x++)
            {
                Vector2I cell = new(x, y);
                ushort currentId = SectorIds[GridPositionToIndex(cell)];
                if (currentId == 0) continue;

                AddSubsectorAdjacency(adjacencyMap, currentId, cell + Vector2I.Right);
                AddSubsectorAdjacency(adjacencyMap, currentId, cell + Vector2I.Down);
            }
        }

        return adjacencyMap;
    }

    private void AddSubsectorAdjacency(Dictionary<ushort, HashSet<ushort>> adjacencyMap, ushort currentId, Vector2I neighborCell)
    {
        if (!IsWithinBounds(neighborCell)) return;

        ushort neighborId = SectorIds[GridPositionToIndex(neighborCell)];
        if (neighborId == 0 || neighborId == currentId) return;

        adjacencyMap[currentId].Add(neighborId);
        adjacencyMap[neighborId].Add(currentId);
    }

    private Dictionary<ushort, int> AssignSubsectorColorIndexes(Dictionary<ushort, HashSet<ushort>> adjacencyMap)
    {
        Dictionary<ushort, int> colorIndexMap = [];

        if (TryAssignSubsectorColorIndexes(adjacencyMap, colorIndexMap))
        {
            return colorIndexMap;
        }

        GD.PushWarning("Subsector graph coloring failed; falling back to deterministic id-based colors.");
        foreach (ushort subsectorId in adjacencyMap.Keys.OrderBy(id => id))
        {
            colorIndexMap[subsectorId] = subsectorId % SubsectorPalette.Length;
        }
        return colorIndexMap;
    }

    private bool TryAssignSubsectorColorIndexes(Dictionary<ushort, HashSet<ushort>> adjacencyMap, Dictionary<ushort, int> colorIndexMap)
    {
        if (colorIndexMap.Count == adjacencyMap.Count) return true;

        ushort subsectorId = SelectNextSubsectorToColor(adjacencyMap, colorIndexMap);
        HashSet<int> usedNeighborColors = adjacencyMap[subsectorId]
            .Where(colorIndexMap.ContainsKey)
            .Select(neighborId => colorIndexMap[neighborId])
            .ToHashSet();

        for (int i = 0; i < SubsectorPalette.Length; i++)
        {
            if (usedNeighborColors.Contains(i)) continue;

            colorIndexMap[subsectorId] = i;
            if (TryAssignSubsectorColorIndexes(adjacencyMap, colorIndexMap))
            {
                return true;
            }
            colorIndexMap.Remove(subsectorId);
        }

        return false;
    }

    private ushort SelectNextSubsectorToColor(Dictionary<ushort, HashSet<ushort>> adjacencyMap, Dictionary<ushort, int> colorIndexMap)
    {
        return adjacencyMap.Keys
            .Where(id => !colorIndexMap.ContainsKey(id))
            .OrderByDescending(id => adjacencyMap[id]
                .Where(colorIndexMap.ContainsKey)
                .Select(neighborId => colorIndexMap[neighborId])
                .Distinct()
                .Count())
            .ThenByDescending(id => adjacencyMap[id].Count)
            .ThenBy(id => id)
            .First();
    }

    private void ValidateSubsectorColoring(Dictionary<ushort, HashSet<ushort>> adjacencyMap, Dictionary<ushort, int> colorIndexMap)
    {
        foreach (var kvp in adjacencyMap)
        {
            foreach (ushort neighborId in kvp.Value)
            {
                if (kvp.Key >= neighborId) continue;
                if (colorIndexMap[kvp.Key] != colorIndexMap[neighborId]) continue;

                GD.PushWarning($"Adjacent subsectors {kvp.Key} and {neighborId} share a color.");
            }
        }
    }

    private static void EnsureAdjacencyEntries(Dictionary<ushort, HashSet<ushort>> adjacencyMap, IEnumerable<ushort> subsectorIds)
    {
        foreach (ushort subsectorId in subsectorIds)
        {
            if (!adjacencyMap.ContainsKey(subsectorId))
            {
                adjacencyMap[subsectorId] = [];
            }
        }
    }

    private Color GetSubsectorColor(ushort subsectorId)
    {
        if (!_subsectorColorIndexMap.TryGetValue(subsectorId, out int colorIndex))
        {
            colorIndex = subsectorId % SubsectorPalette.Length;
        }

        return SubsectorPalette[colorIndex];
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        return new Color(color.R, color.G, color.B, alpha);
    }

    private void DrawSubsectorFill(ushort subsectorId, Vector2[] polygonPoints, Vector2[] smoothedPolygonPoints, Color baseColor)
    {
        DrawColoredPolygon(polygonPoints, WithAlpha(baseColor, SubsectorGridFillAlpha));
        DrawColoredPolygon(smoothedPolygonPoints, WithAlpha(baseColor, SubsectorGlassFillAlpha));

        Vector2 centroid = CalculateCentroid(smoothedPolygonPoints);
        float inset = 0.78f + 0.08f * Noise01(subsectorId, 17);
        Vector2[] innerStainPoints = ScalePolygon(smoothedPolygonPoints, centroid, inset);
        Color stainColor = TintSubsectorColor(baseColor, 1.18f);
        DrawColoredPolygon(innerStainPoints, WithAlpha(stainColor, SubsectorInnerStainAlpha));

        float secondInset = 0.48f + 0.08f * Noise01(subsectorId, 29);
        Vector2 offset = new(
            (Noise01(subsectorId, 31) - 0.5f) * CellSize.X * 1.6f,
            (Noise01(subsectorId, 37) - 0.5f) * CellSize.Y * 1.6f);
        Vector2[] shadowStainPoints = ScalePolygon(smoothedPolygonPoints, centroid + offset, secondInset);
        Color shadowColor = TintSubsectorColor(baseColor, 0.72f);
        DrawColoredPolygon(shadowStainPoints, WithAlpha(shadowColor, SubsectorInnerStainAlpha * 0.8f));
    }

    private Vector2 GridToPixel(Vector2 gridPoint)
    {
        return new Vector2(
            gridPoint.X * CellSize.X + HalfCellSize.X,
            gridPoint.Y * CellSize.Y + HalfCellSize.Y);
    }

    private void DrawVoronoiSubsectors()
    {
        // Fills first, so the border strokes sit cleanly on top of every region.
        foreach (var kvp in _voronoiSubsectorLoops.OrderBy(entry => entry.Key))
        {
            Color baseColor = GetSubsectorColor(kvp.Key);
            foreach (Vector2[] loop in kvp.Value)
            {
                if (loop.Length < 3) continue;
                Vector2[] pixelLoop = loop.Select(GridToPixel).ToArray();
                DrawColoredPolygon(pixelLoop, WithAlpha(baseColor, SubsectorGlassFillAlpha));
            }
        }

        foreach (var kvp in _voronoiSubsectorLoops.OrderBy(entry => entry.Key))
        {
            foreach (Vector2[] loop in kvp.Value)
            {
                if (loop.Length < 2) continue;
                Vector2[] pixelLoop = loop.Select(GridToPixel).ToArray();
                Vector2[] closedLoop = ClosePolygon(pixelLoop);
                DrawPolyline(closedLoop, WithAlpha(SubsectorBorderShadowColor, 0.82f), 4.2f, true);
                DrawPolyline(closedLoop, WithAlpha(SubsectorBorderGlowColor, 0.20f), 2.8f, true);
                DrawPolyline(closedLoop, WithAlpha(SubsectorBorderColor, 0.88f), 1.35f, true);
            }
        }
    }

    private void DrawSubsectorBoundaries()
    {
        foreach (Vector2[] boundaryPath in _subsectorBoundaryPaths)
        {
            if (boundaryPath.Length < 2) continue;

            DrawPolyline(boundaryPath, WithAlpha(SubsectorBorderShadowColor, 0.82f), 4.2f, true);
            DrawPolyline(boundaryPath, WithAlpha(SubsectorBorderGlowColor, 0.20f), 2.8f, true);
            DrawPolyline(boundaryPath, WithAlpha(SubsectorBorderColor, 0.88f), 1.35f, true);
        }
    }

    private static Color TintSubsectorColor(Color color, float multiplier)
    {
        return new Color(
            Mathf.Clamp(color.R * multiplier, 0.0f, 1.0f),
            Mathf.Clamp(color.G * multiplier, 0.0f, 1.0f),
            Mathf.Clamp(color.B * multiplier, 0.0f, 1.0f),
            color.A);
    }

    private static Vector2 CalculateCentroid(Vector2[] polygonPoints)
    {
        if (polygonPoints.Length == 0) return Vector2.Zero;

        Vector2 sum = Vector2.Zero;
        foreach (Vector2 point in polygonPoints)
        {
            sum += point;
        }

        return sum / polygonPoints.Length;
    }

    private static Vector2[] ScalePolygon(Vector2[] polygonPoints, Vector2 center, float scale)
    {
        Vector2[] scaledPoints = new Vector2[polygonPoints.Length];
        for (int i = 0; i < polygonPoints.Length; i++)
        {
            scaledPoints[i] = center + (polygonPoints[i] - center) * scale;
        }

        return scaledPoints;
    }

    private static Vector2[] ClosePolygon(Vector2[] polygonPoints)
    {
        if (polygonPoints.Length == 0) return polygonPoints;

        Vector2[] closedPolygonPoints = new Vector2[polygonPoints.Length + 1];
        polygonPoints.CopyTo(closedPolygonPoints, 0);
        closedPolygonPoints[^1] = polygonPoints[0];
        return closedPolygonPoints;
    }

    private Vector2[] BuildSmoothedPolygon(Vector2[] polygonPoints)
    {
        if (polygonPoints.Length < 3) return polygonPoints;

        Vector2[] simplifiedPoints = SimplifyClosedPolygon(
            polygonPoints,
            PolygonSimplificationToleranceCellFraction,
            PolygonSimplificationMaxBridgeCellFraction);
        if (simplifiedPoints.Length < 3) return polygonPoints;

        return SmoothClosedPolygon(simplifiedPoints, PolygonSmoothingPasses);
    }

    private Vector2[] SimplifyClosedPolygon(Vector2[] polygonPoints, float toleranceCellFraction, float maxBridgeCellFraction)
    {
        List<Vector2> simplifiedPoints = polygonPoints.ToList();
        float cellSize = Mathf.Min(CellSize.X, CellSize.Y);
        float tolerance = cellSize * toleranceCellFraction;
        float maxBridgeLengthSquared = Mathf.Pow(cellSize * maxBridgeCellFraction, 2);

        for (int pass = 0; pass < 4 && simplifiedPoints.Count > 3; pass++)
        {
            bool removedAny = false;

            for (int i = 0; i < simplifiedPoints.Count && simplifiedPoints.Count > 3; i++)
            {
                Vector2 previous = simplifiedPoints[(i - 1 + simplifiedPoints.Count) % simplifiedPoints.Count];
                Vector2 current = simplifiedPoints[i];
                Vector2 next = simplifiedPoints[(i + 1) % simplifiedPoints.Count];
                float bridgeLengthSquared = previous.DistanceSquaredTo(next);
                float distanceFromBridge = DistanceToSegment(current, previous, next);

                if (distanceFromBridge <= 0.1f || (distanceFromBridge <= tolerance && bridgeLengthSquared <= maxBridgeLengthSquared))
                {
                    simplifiedPoints.RemoveAt(i);
                    removedAny = true;
                    i--;
                }
            }

            if (!removedAny) break;
        }

        return simplifiedPoints.ToArray();
    }

    private static Vector2[] SmoothClosedPolygon(Vector2[] polygonPoints, int passes)
    {
        Vector2[] smoothedPoints = polygonPoints;

        for (int pass = 0; pass < passes && smoothedPoints.Length >= 3; pass++)
        {
            Vector2[] nextPoints = new Vector2[smoothedPoints.Length * 2];

            for (int i = 0; i < smoothedPoints.Length; i++)
            {
                Vector2 current = smoothedPoints[i];
                Vector2 next = smoothedPoints[(i + 1) % smoothedPoints.Length];
                nextPoints[i * 2] = current.Lerp(next, 0.25f);
                nextPoints[i * 2 + 1] = current.Lerp(next, 0.75f);
            }

            smoothedPoints = nextPoints;
        }

        return smoothedPoints;
    }

    private Vector2[] BuildSmoothedBoundaryPath(Vector2[] boundaryPoints)
    {
        if (boundaryPoints.Length < 3) return boundaryPoints;

        bool isClosed = boundaryPoints[0] == boundaryPoints[^1];
        Vector2[] points = isClosed ? boundaryPoints.Take(boundaryPoints.Length - 1).ToArray() : boundaryPoints;
        Vector2[] simplifiedPoints = isClosed
            ? SimplifyClosedPolygon(points, SeamSimplificationToleranceCellFraction, SeamSimplificationMaxBridgeCellFraction)
            : SimplifyOpenPolyline(points, SeamSimplificationToleranceCellFraction, SeamSimplificationMaxBridgeCellFraction);

        if (simplifiedPoints.Length < 2) return boundaryPoints;

        Vector2[] smoothedPoints = isClosed
            ? BuildClosedCatmullRomCurve(simplifiedPoints, SeamCurveSamplesPerSegment)
            : BuildOpenCatmullRomCurve(simplifiedPoints, SeamCurveSamplesPerSegment);

        return isClosed ? ClosePolygon(smoothedPoints) : smoothedPoints;
    }

    private Vector2[] SimplifyOpenPolyline(Vector2[] polylinePoints, float toleranceCellFraction, float maxBridgeCellFraction)
    {
        if (polylinePoints.Length < 3) return polylinePoints;

        float cellSize = Mathf.Min(CellSize.X, CellSize.Y);
        float tolerance = cellSize * toleranceCellFraction;
        Vector2[] simplifiedPoints = SimplifyPolylineDouglasPeucker(polylinePoints, tolerance);
        if (simplifiedPoints.Length < 3) return simplifiedPoints;

        List<Vector2> locallySmoothedPoints = simplifiedPoints.ToList();
        float maxBridgeLengthSquared = Mathf.Pow(cellSize * maxBridgeCellFraction, 2);

        for (int pass = 0; pass < 2 && locallySmoothedPoints.Count > 2; pass++)
        {
            bool removedAny = false;
            for (int i = 1; i < locallySmoothedPoints.Count - 1 && locallySmoothedPoints.Count > 2; i++)
            {
                Vector2 previous = locallySmoothedPoints[i - 1];
                Vector2 current = locallySmoothedPoints[i];
                Vector2 next = locallySmoothedPoints[i + 1];
                float bridgeLengthSquared = previous.DistanceSquaredTo(next);
                float distanceFromBridge = DistanceToSegment(current, previous, next);

                if (distanceFromBridge <= 0.1f || (distanceFromBridge <= tolerance && bridgeLengthSquared <= maxBridgeLengthSquared))
                {
                    locallySmoothedPoints.RemoveAt(i);
                    removedAny = true;
                    i--;
                }
            }

            if (!removedAny) break;
        }

        return locallySmoothedPoints.ToArray();
    }

    private static Vector2[] SimplifyPolylineDouglasPeucker(Vector2[] points, float tolerance)
    {
        if (points.Length < 3) return points;

        bool[] keep = new bool[points.Length];
        keep[0] = true;
        keep[^1] = true;
        MarkDouglasPeuckerPoints(points, 0, points.Length - 1, tolerance, keep);

        List<Vector2> simplifiedPoints = [];
        for (int i = 0; i < points.Length; i++)
        {
            if (keep[i])
            {
                simplifiedPoints.Add(points[i]);
            }
        }

        return simplifiedPoints.ToArray();
    }

    private static void MarkDouglasPeuckerPoints(Vector2[] points, int startIndex, int endIndex, float tolerance, bool[] keep)
    {
        if (endIndex <= startIndex + 1) return;

        float maxDistance = 0.0f;
        int farthestIndex = -1;
        Vector2 start = points[startIndex];
        Vector2 end = points[endIndex];

        for (int i = startIndex + 1; i < endIndex; i++)
        {
            float distance = DistanceToSegment(points[i], start, end);
            if (distance > maxDistance)
            {
                maxDistance = distance;
                farthestIndex = i;
            }
        }

        if (farthestIndex == -1 || maxDistance <= tolerance) return;

        keep[farthestIndex] = true;
        MarkDouglasPeuckerPoints(points, startIndex, farthestIndex, tolerance, keep);
        MarkDouglasPeuckerPoints(points, farthestIndex, endIndex, tolerance, keep);
    }

    private static Vector2[] BuildOpenCatmullRomCurve(Vector2[] points, int samplesPerSegment)
    {
        if (points.Length < 3) return points;

        List<Vector2> curvePoints = [points[0]];
        for (int i = 0; i < points.Length - 1; i++)
        {
            Vector2 p0 = points[Math.Max(i - 1, 0)];
            Vector2 p1 = points[i];
            Vector2 p2 = points[i + 1];
            Vector2 p3 = points[Math.Min(i + 2, points.Length - 1)];
            for (int sample = 1; sample <= samplesPerSegment; sample++)
            {
                float t = sample / (float)samplesPerSegment;
                curvePoints.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }

        return curvePoints.ToArray();
    }

    private static Vector2[] BuildClosedCatmullRomCurve(Vector2[] points, int samplesPerSegment)
    {
        if (points.Length < 3) return points;

        List<Vector2> curvePoints = [];
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 p0 = points[(i - 1 + points.Length) % points.Length];
            Vector2 p1 = points[i];
            Vector2 p2 = points[(i + 1) % points.Length];
            Vector2 p3 = points[(i + 2) % points.Length];
            for (int sample = 0; sample < samplesPerSegment; sample++)
            {
                float t = sample / (float)samplesPerSegment;
                curvePoints.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }

        return curvePoints.ToArray();
    }

    private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (
            (2.0f * p1)
            + (-p0 + p2) * t
            + (2.0f * p0 - 5.0f * p1 + 4.0f * p2 - p3) * t2
            + (-p0 + 3.0f * p1 - 3.0f * p2 + p3) * t3);
    }

    private static float Noise01(ushort seed, int salt)
    {
        uint value = (uint)seed * 73856093u
            ^ (uint)(salt + 1) * 19349663u;

        value ^= value >> 16;
        value *= 2246822519u;
        value ^= value >> 13;
        value *= 3266489917u;
        value ^= value >> 16;

        return (value & 0x00FFFFFF) / 16777215.0f;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        Vector2 segment = end - start;
        float segmentLengthSquared = segment.LengthSquared();
        if (segmentLengthSquared <= 0.001f) return point.DistanceTo(start);

        float t = Mathf.Clamp((point - start).Dot(segment) / segmentLengthSquared, 0.0f, 1.0f);
        return point.DistanceTo(start + segment * t);
    }

    private void DrawSelectedSystemOverlay()
    {
        if (!_selectedPlanetId.HasValue) return;
        if (!GameDataSingleton.Instance.Sector.Planets.TryGetValue(_selectedPlanetId.Value, out Planet planet)) return;

        Vector2 center = CalculateMapPosition(new Vector2I(planet.Position.X, planet.Position.Y));
        float baseRadius = Mathf.Min(CellSize.X, CellSize.Y) * 0.42f;
        Color ringColor = Color.Color8(99, 199, 215);
        DrawArc(center, baseRadius, 0, Mathf.Tau, 96, WithAlpha(ringColor, 0.72f), 2.0f, true);
        DrawArc(center, baseRadius * 1.45f, 0, Mathf.Tau, 96, WithAlpha(ringColor, 0.28f), 1.2f, true);
        DrawArc(center, baseRadius * 1.9f, 0, Mathf.Tau, 96, WithAlpha(ringColor, 0.16f), 1.0f, true);

        List<TaskForce> orbitingFleets = GameDataSingleton.Instance.Sector.Fleets.Values
            .Where(fleet => fleet.Planet == planet && fleet.TravelPhase == FleetTravelPhase.InOrbit)
            .OrderBy(fleet => fleet.Id)
            .ToList();

        // The fleet's ship sprite is placed up-and-to-the-right of the planet by
        // half a cell (see PlaceFleets), so anchor the highlight there rather than
        // on an orbit angle, keeping the marker on the actual fleet icon.
        Vector2 fleetAnchor = center + new Vector2(HalfCellSize.X, -HalfCellSize.Y);

        for (int i = 0; i < orbitingFleets.Count; i++)
        {
            // Fan multiple orbiting fleets horizontally so their markers don't fully overlap.
            float fanOffset = (i - (orbitingFleets.Count - 1) / 2.0f) * 7.0f;
            Vector2 fleetPosition = fleetAnchor + new Vector2(fanOffset, 0.0f);
            bool isPlayerFleet = orbitingFleets[i].Faction == GameDataSingleton.Instance.Sector.PlayerForce.Faction;
            Color fleetColor = isPlayerFleet ? Color.Color8(99, 199, 215) : Color.Color8(204, 83, 71);
            DrawCircle(fleetPosition, 5.0f, WithAlpha(fleetColor, 0.85f), true, -1.0f, true);
            DrawArc(fleetPosition, 8.0f, 0, Mathf.Tau, 24, WithAlpha(fleetColor, 0.55f), 1.0f, true);
        }
    }

    public new void SetProcessInput(bool enable)
    {
        base.SetProcessInput(enable);
        _camera.SetProcessInput(enable);
    }

    public Vector2I CalculateMapPosition(Vector2I gridPosition)
    {
        return gridPosition * CellSize + HalfCellSize;
    }

    public Vector2I CalculateGridCoordinates(Vector2I mapPosition)
    {
        return (mapPosition / CellSize);
    }

    public int GridPositionToIndex(Vector2I cell)
    {
        return (GridDimensions.X * cell.Y + cell.X);
    }

    public Vector2I IndexToGridPosition(int index)
    {
        int x = index % GridDimensions.X;
        int y = index / GridDimensions.X;
        return new Vector2I(x, y);
    }

    private void PlacePlanets()
	{
		var starTexture = (Texture2D)GD.Load("res://Assets/UICircle.png");
		Vector2 starTextureScale = new Vector2(0.05f, 0.05f);
		foreach(var kvp in GameDataSingleton.Instance.Sector.Planets)
		{
			Vector2I gridPosition = new(kvp.Value.Position.X, kvp.Value.Position.Y);
			int index = GridPositionToIndex(gridPosition);
			HasPlanet[index] = true;
            Faction controller = kvp.Value.GetControllingFaction();
            var color = controller?.Color ?? System.Drawing.Color.Gray;
            ClickableSprite2D planet = DrawTexture(starTexture, starTextureScale, gridPosition, new Color(color.R, color.G, color.B, color.A));
            planet.Pressed += (object sender, EventArgs e) => PlanetClicked?.Invoke(planet, kvp.Key);
            planet.DoublePressed += (object sender, EventArgs e) => PlanetDoubleClicked?.Invoke(planet, kvp.Key);
		}
	}

	public void RefreshFleets()
	{
		foreach (Node fleetSprite in _fleetSprites)
		{
			RemoveChild(fleetSprite);
			fleetSprite.QueueFree();
		}
		_fleetSprites.Clear();
		PlaceFleets();
	}

	private void PlaceFleets()
	{
		var shipTexture = GD.Load<AtlasTexture>(("res://Assets/shipAtlasTexture.tres"));
		Vector2 shipTextureScale = new Vector2(0.2f, 0.2f);
		foreach(var taskForceKvp in GameDataSingleton.Instance.Sector.Fleets)
		{
            TaskForce taskForce = taskForceKvp.Value;
			if (!IsFleetVisibleOnMap(taskForce)) continue;

            // Determine the position for the fleet's sprite
            Vector2I gridPosition;
            bool isRealspaceTransit = taskForce.TravelPhase == FleetTravelPhase.OutboundSystemTransit
                || taskForce.TravelPhase == FleetTravelPhase.InboundSystemTransit;
            if (taskForce.Planet != null)
            {
                // Fleet is in orbit around a planet

                // Assuming you have a way to get the planet's position
                // You'll need to implement GetPlanetSpritePosition or similar
                gridPosition = new(taskForce.Planet.Position.X, taskForce.Planet.Position.Y);
            }
            else if (isRealspaceTransit && GetTransitAnchorPosition(taskForce) is Coordinate transitPosition)
            {
                gridPosition = new Vector2I(transitPosition.X, transitPosition.Y);
            }
            else
            {
                // Fleet is in space, use its map coordinates
                gridPosition = new Vector2I(taskForce.Position.Value.X, taskForce.Position.Value.Y);
            }

			// Make sure you are the owner of the new node, or it will not save properly
			Vector2I fleetOffset = isRealspaceTransit
				? new Vector2I(1, 1)
				: new Vector2I(1, -1);
			ClickableSprite2D fleet = DrawTexture(shipTexture, shipTextureScale, gridPosition, Color.Color8(255, 255, 255), 2, fleetOffset);
            fleet.Pressed += (object sender, EventArgs e) => FleetClicked?.Invoke(fleet, taskForceKvp.Key);
            fleet.RightPressed += (object sender, EventArgs e) => FleetRightClicked?.Invoke(fleet, taskForceKvp.Key);
			_fleetSprites.Add(fleet);
        }
    }

	private static Coordinate? GetTransitAnchorPosition(TaskForce taskForce)
	{
		return taskForce.TravelPhase switch
		{
			FleetTravelPhase.OutboundSystemTransit => taskForce.Origin?.Position ?? taskForce.Position,
			FleetTravelPhase.InboundSystemTransit => taskForce.Destination?.Position ?? taskForce.Position,
			_ => taskForce.Position
		};
	}

	private static bool IsFleetVisibleOnMap(TaskForce taskForce)
	{
		return taskForce.TravelPhase != FleetTravelPhase.InWarp;
	}

    private void Fleet_Pressed(object sender, EventArgs e)
    {
        throw new NotImplementedException();
    }

    private List<Vector2[]> DetermineSubsectorBoundaryPaths()
    {
        Dictionary<(ushort, ushort), HashSet<EdgeKey>> edgeSetsBySubsectorPair = [];

        for (int y = 0; y < GridDimensions.Y; y++)
        {
            for (int x = 0; x < GridDimensions.X; x++)
            {
                Vector2I cell = new(x, y);
                ushort currentId = SectorIds[GridPositionToIndex(cell)];
                if (currentId == 0) continue;

                AddBoundaryEdgeIfNeeded(edgeSetsBySubsectorPair, cell, Vector2I.Up);
                AddBoundaryEdgeIfNeeded(edgeSetsBySubsectorPair, cell, Vector2I.Right);
                AddBoundaryEdgeIfNeeded(edgeSetsBySubsectorPair, cell, Vector2I.Down);
                AddBoundaryEdgeIfNeeded(edgeSetsBySubsectorPair, cell, Vector2I.Left);
            }
        }

        List<Vector2[]> boundaryPaths = [];
        foreach (var kvp in edgeSetsBySubsectorPair.OrderBy(kvp => kvp.Key.Item1).ThenBy(kvp => kvp.Key.Item2))
        {
            Dictionary<Vector2I, HashSet<Vector2I>> adjacencyMap = BuildBoundaryAdjacencyMap(kvp.Value);
            boundaryPaths.AddRange(ChainBoundaryEdges(adjacencyMap, kvp.Value)
                .Select(BuildSmoothedBoundaryPath)
                .Where(path => path.Length >= 2));
        }

        return boundaryPaths;
    }

    private void AddBoundaryEdgeIfNeeded(
        Dictionary<(ushort, ushort), HashSet<EdgeKey>> edgeSetsBySubsectorPair,
        Vector2I cell,
        Vector2I direction)
    {
        ushort currentId = SectorIds[GridPositionToIndex(cell)];
        Vector2I neighborCell = cell + direction;
        ushort neighborId = IsWithinBounds(neighborCell) ? SectorIds[GridPositionToIndex(neighborCell)] : (ushort)0;
        if (neighborId == currentId) return;

        (Vector2I start, Vector2I end) = GetCellEdgeMapPoints(cell, direction);
        EdgeKey edgeKey = new(start, end);
        (ushort, ushort) subsectorPair = currentId < neighborId
            ? (currentId, neighborId)
            : (neighborId, currentId);

        if (!edgeSetsBySubsectorPair.TryGetValue(subsectorPair, out HashSet<EdgeKey> edgeSet))
        {
            edgeSet = [];
            edgeSetsBySubsectorPair[subsectorPair] = edgeSet;
        }

        edgeSet.Add(edgeKey);
    }

    private static Dictionary<Vector2I, HashSet<Vector2I>> BuildBoundaryAdjacencyMap(HashSet<EdgeKey> edgeSet)
    {
        Dictionary<Vector2I, HashSet<Vector2I>> adjacencyMap = [];
        foreach (EdgeKey edgeKey in edgeSet)
        {
            AddBoundaryAdjacency(adjacencyMap, edgeKey);
        }

        return adjacencyMap;
    }

    private static void AddBoundaryAdjacency(Dictionary<Vector2I, HashSet<Vector2I>> adjacencyMap, EdgeKey edgeKey)
    {
        if (!adjacencyMap.TryGetValue(edgeKey.A, out HashSet<Vector2I> aNeighbors))
        {
            aNeighbors = [];
            adjacencyMap[edgeKey.A] = aNeighbors;
        }
        if (!adjacencyMap.TryGetValue(edgeKey.B, out HashSet<Vector2I> bNeighbors))
        {
            bNeighbors = [];
            adjacencyMap[edgeKey.B] = bNeighbors;
        }

        aNeighbors.Add(edgeKey.B);
        bNeighbors.Add(edgeKey.A);
    }

    private (Vector2I Start, Vector2I End) GetCellEdgeMapPoints(Vector2I cell, Vector2I direction)
    {
        Vector2I cellCenterPosition = CalculateMapPosition(cell);
        Vector2I topLeft = cellCenterPosition - HalfCellSize;
        Vector2I topRight = topLeft + new Vector2I(CellSize.X, 0);
        Vector2I bottomRight = topLeft + CellSize;
        Vector2I bottomLeft = topLeft + new Vector2I(0, CellSize.Y);

        if (direction == Vector2I.Up) return (topLeft, topRight);
        if (direction == Vector2I.Right) return (topRight, bottomRight);
        if (direction == Vector2I.Down) return (bottomLeft, bottomRight);
        return (topLeft, bottomLeft);
    }

    private static List<Vector2[]> ChainBoundaryEdges(
        Dictionary<Vector2I, HashSet<Vector2I>> adjacencyMap,
        HashSet<EdgeKey> edgeSet)
    {
        List<Vector2[]> paths = [];
        HashSet<EdgeKey> visitedEdges = [];

        foreach (EdgeKey edge in edgeSet.OrderBy(edge => edge.A.X).ThenBy(edge => edge.A.Y).ThenBy(edge => edge.B.X).ThenBy(edge => edge.B.Y))
        {
            if (visitedEdges.Contains(edge)) continue;

            Vector2I start = adjacencyMap[edge.A].Count != 2 ? edge.A : edge.B;
            Vector2I current = start;
            Vector2I next = start == edge.A ? edge.B : edge.A;
            List<Vector2I> path = [start];

            while (true)
            {
                EdgeKey currentEdge = new(current, next);
                if (!visitedEdges.Add(currentEdge)) break;

                path.Add(next);
                if (next == start) break;
                if (adjacencyMap[next].Count != 2) break;

                Vector2I previous = current;
                current = next;
                Vector2I? nextCandidate = null;
                foreach (Vector2I candidate in adjacencyMap[current])
                {
                    if (candidate == previous) continue;
                    if (visitedEdges.Contains(new EdgeKey(current, candidate))) continue;

                    nextCandidate = candidate;
                    break;
                }

                if (!nextCandidate.HasValue) break;
                next = nextCandidate.Value;
            }

            if (path.Count >= 2)
            {
                paths.Add(path.Select(point => new Vector2(point.X, point.Y)).ToArray());
            }
        }

        return paths;
    }

    private Dictionary<ushort, List<Vector2I>> DetermineSubsectorBorderPoints(IEnumerable<Subsector> subsectors)
    {
        Dictionary<ushort, List<Vector2I>> subsectorVertexListMap = [];
        foreach(Subsector subsector in subsectors)
        {
            List<Vector2I> vertexList = [];
            subsectorVertexListMap[subsector.Id] = vertexList;
            // the first cell should be the top left of the subsector
            Vector2I gridPosition = subsector.Cells[0];
            Vector2I cellCenterPosition = CalculateMapPosition(gridPosition);
            Vector2I topLeft = cellCenterPosition - HalfCellSize;
            Vector2I topRight = new Vector2I(topLeft.X + CellSize.X, topLeft.Y);
            BorderPoint currentPoint = new BorderPoint
            {
                gridPos = gridPosition,
                mapPoint = topRight,
                orientation = Facing.East
            };
            vertexList.Add(topRight);
            while (currentPoint.mapPoint != topLeft)
            {
                currentPoint = GetNextPoint(currentPoint, subsector.Id);
                vertexList.Add(currentPoint.mapPoint);
            }
        }

        return subsectorVertexListMap;
    }

    private BorderPoint GetNextPoint(BorderPoint currentPoint, ushort subsectorId)
    {
        BorderPoint left, straight, right;
        switch (currentPoint.orientation)
        {
            case Facing.North:
                // at a top-left
                // left is (-1, -1)
                // straight is (0, -1)
                // right is (c, 0)
                left = new BorderPoint
                {
                    gridPos = currentPoint.gridPos + new Vector2I(-1, -1),
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2I.Left,
                    orientation = Facing.West
                };
                straight = new BorderPoint
                {
                    gridPos = currentPoint.gridPos + new Vector2I(0, -1),
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2I.Up,
                    orientation = Facing.North
                };
                right = new BorderPoint
                {
                    gridPos = currentPoint.gridPos,
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2I.Right,
                    orientation = Facing.East
                };
                break;
            case Facing.East:
                // at a top-right
                // left is (1, -1)
                // straight is (1, 0)
                // right is (0, c)
                left = new BorderPoint
                {
                    gridPos = currentPoint.gridPos + new Vector2I(1, -1),
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2I.Up,
                    orientation = Facing.North
                };
                straight = new BorderPoint
                {
                    gridPos = currentPoint.gridPos + new Vector2I(1, 0),
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2I.Right,
                    orientation = Facing.East
                };
                right = new BorderPoint
                {
                    gridPos = currentPoint.gridPos,
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2I.Down,
                    orientation = Facing.South
                };
                break;
            case Facing.South:
                // at a bottom-right
                // left is (1, 1)
                // straight is (0, 1)
                // right is (-c, 0)
                left = new BorderPoint
                {
                    gridPos = currentPoint.gridPos + new Vector2I(1, 1),
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2I.Right,
                    orientation = Facing.East
                };
                straight = new BorderPoint
                {
                    gridPos = currentPoint.gridPos + new Vector2I(0, 1),
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2I.Down,
                    orientation = Facing.South
                };
                right = new BorderPoint
                {
                    gridPos = currentPoint.gridPos,
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2I.Left,
                    orientation = Facing.West
                };
                break;
            default:
            case Facing.West:
                // at a bottom-left
                // left is (-1, 1)
                // straight is (-1, 0)
                // right is (0, -c)
                left = new BorderPoint
                {
                    gridPos = currentPoint.gridPos + new Vector2I(-1, 1),
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2I.Down,
                    orientation = Facing.South
                };
                straight = new BorderPoint
                {
                    gridPos = currentPoint.gridPos + new Vector2I(-1, 0),
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2I.Left,
                    orientation = Facing.West
                };
                right = new BorderPoint
                {
                    gridPos = currentPoint.gridPos,
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2I.Up,
                    orientation = Facing.North
                };
                break;
        }
        // if left is in bounds and part of this sector, it's next
        if (IsWithinBounds(left.gridPos) && SectorIds[GridPositionToIndex(left.gridPos)] == subsectorId)
        {
            return left;
        }
        else if (IsWithinBounds(straight.gridPos) && SectorIds[GridPositionToIndex(straight.gridPos)] == subsectorId)
        {
            return straight;
        }
        else
        {
            return right;
        }
    }

    private bool IsWithinBounds(Vector2I cellCoordinates)
    {
        return (cellCoordinates.X >= 0 && cellCoordinates.X < GridDimensions.X && cellCoordinates.Y >= 0 && cellCoordinates.Y < GridDimensions.Y);
    }

    private ClickableSprite2D DrawTexture(Texture2D texture, Vector2 scale, Vector2I gridPosition, Color color, int zIndex = 1, bool offset=false)
	{
		Vector2I spriteOffset = offset ? new Vector2I(1, -1) : Vector2I.Zero;
		return DrawTexture(texture, scale, gridPosition, color, zIndex, spriteOffset);
	}

    private ClickableSprite2D DrawTexture(Texture2D texture, Vector2 scale, Vector2I gridPosition, Color color, int zIndex, Vector2I offset)
	{
		ClickableSprite2D newSprite = new ClickableSprite2D();
		this.AddChild(newSprite);
        newSprite.Owner = this;
		Vector2I mapPosition = CalculateMapPosition(gridPosition);
		if(offset != Vector2I.Zero)
		{
			mapPosition += HalfCellSize * offset;
		}
        newSprite.GlobalPosition = mapPosition;
        newSprite.Texture = texture;
        newSprite.Modulate = color;
        newSprite.Scale = scale;
		newSprite.ZIndex = zIndex;
        return newSprite;
	}

}
