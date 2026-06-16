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
    private static readonly Color[] SubsectorPalette =
    [
        Color.Color8(84, 190, 214),
        Color.Color8(222, 174, 80),
        Color.Color8(113, 187, 101),
        Color.Color8(204, 83, 71),
        Color.Color8(111, 142, 218),
        Color.Color8(200, 132, 62)
    ];

    public event EventHandler<int> PlanetClicked;
    public event EventHandler<int> PlanetDoubleClicked;
    public event EventHandler<int> FleetClicked;
    public event EventHandler<int> FleetRightClicked;

    public Vector2I GridDimensions =
		new(GameDataSingleton.Instance.GameRulesData.SectorSize.X,
			GameDataSingleton.Instance.GameRulesData.SectorSize.Y);
	public Vector2I CellSize =
		new(GameDataSingleton.Instance.GameRulesData.SectorCellSize.X,
			GameDataSingleton.Instance.GameRulesData.SectorCellSize.Y);

	public Vector2I HalfCellSize { get; private set; }
    public ushort[] SectorIds { get; private set; }
    public bool[] HasPlanet { get; private set; }
    private Camera2D _camera;
    private readonly List<Node> _fleetSprites = [];
	
	private Dictionary<ushort, List<Vector2I>> _subsectorVertexListMap;
	private Dictionary<ushort, HashSet<ushort>> _subsectorAdjacencyMap;
	private Dictionary<ushort, int> _subsectorColorIndexMap;
    private int? _selectedPlanetId;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
        _camera = GetNode<Camera2D>("Camera2D");
		HalfCellSize = CellSize / 2;
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

	public override void _Draw()
	{
		base._Draw();
		foreach (var kvp in _subsectorVertexListMap.OrderBy(kvp => kvp.Key))
		{
            Vector2[] polygonPoints = kvp.Value.Select(vector => new Vector2(vector.X, vector.Y)).ToArray();
            Vector2[] closedPolygonPoints = ClosePolygon(polygonPoints);
            Vector2[] visualBoundaryPoints = BuildJitteredBoundary(kvp.Key, closedPolygonPoints);
            Color baseColor = GetSubsectorColor(kvp.Key);
            DrawColoredPolygon(polygonPoints, WithAlpha(baseColor, 0.12f));
            DrawPolyline(visualBoundaryPoints, WithAlpha(baseColor, 0.12f), 8.0f, true);
            DrawDistressedBoundary(visualBoundaryPoints, baseColor, kvp.Key);
            DrawPolyline(visualBoundaryPoints, WithAlpha(baseColor, 0.42f), 1.4f, true);
		}
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

    private static Vector2[] ClosePolygon(Vector2[] polygonPoints)
    {
        if (polygonPoints.Length == 0) return polygonPoints;

        Vector2[] closedPolygonPoints = new Vector2[polygonPoints.Length + 1];
        polygonPoints.CopyTo(closedPolygonPoints, 0);
        closedPolygonPoints[^1] = polygonPoints[0];
        return closedPolygonPoints;
    }

    private Vector2[] BuildJitteredBoundary(ushort subsectorId, Vector2[] closedPolygonPoints)
    {
        if (closedPolygonPoints.Length == 0) return closedPolygonPoints;

        Vector2[] visualBoundaryPoints = new Vector2[closedPolygonPoints.Length];
        for (int i = 0; i < closedPolygonPoints.Length - 1; i++)
        {
            visualBoundaryPoints[i] = JitterBoundaryPoint(closedPolygonPoints[i], subsectorId, i);
        }

        visualBoundaryPoints[^1] = visualBoundaryPoints[0];
        return visualBoundaryPoints;
    }

    private Vector2 JitterBoundaryPoint(Vector2 point, ushort subsectorId, int index)
    {
        float maxJitter = Mathf.Min(CellSize.X, CellSize.Y) * 0.12f;
        float x = (Noise01(subsectorId, index, 11) - 0.5f) * maxJitter;
        float y = (Noise01(subsectorId, index, 23) - 0.5f) * maxJitter;
        return point + new Vector2(x, y);
    }

    private void DrawDistressedBoundary(Vector2[] boundaryPoints, Color baseColor, ushort subsectorId)
    {
        if (boundaryPoints.Length < 2) return;

        for (int i = 0; i < boundaryPoints.Length - 1; i++)
        {
            Vector2 start = boundaryPoints[i];
            Vector2 end = boundaryPoints[i + 1];
            float segmentAlpha = 0.26f + 0.26f * Noise01(subsectorId, i, 37);
            float segmentWidth = 1.0f + 1.2f * Noise01(subsectorId, i, 41);

            DrawBrokenSegment(start, end, WithAlpha(baseColor, segmentAlpha), segmentWidth, subsectorId, i);

            if (Noise01(subsectorId, i, 53) > 0.72f)
            {
                DrawBoundaryTick(start, end, baseColor, subsectorId, i);
            }
        }
    }

    private void DrawBrokenSegment(Vector2 start, Vector2 end, Color color, float width, ushort subsectorId, int segmentIndex)
    {
        float length = start.DistanceTo(end);
        if (length <= 0.001f) return;

        int pieces = Mathf.Max(2, (int)Mathf.Ceil(length / 18.0f));
        for (int i = 0; i < pieces; i++)
        {
            if (Noise01(subsectorId, segmentIndex * 31 + i, 67) < 0.18f) continue;

            float t0 = i / (float)pieces;
            float t1 = (i + 0.72f + 0.18f * Noise01(subsectorId, segmentIndex * 31 + i, 71)) / pieces;
            t1 = Mathf.Min(t1, 1.0f);
            Vector2 pieceStart = start.Lerp(end, t0);
            Vector2 pieceEnd = start.Lerp(end, t1);
            DrawLine(pieceStart, pieceEnd, color, width, true);
        }
    }

    private void DrawBoundaryTick(Vector2 start, Vector2 end, Color baseColor, ushort subsectorId, int segmentIndex)
    {
        Vector2 segment = end - start;
        if (segment.LengthSquared() <= 0.001f) return;

        Vector2 midpoint = start.Lerp(end, 0.35f + 0.3f * Noise01(subsectorId, segmentIndex, 79));
        Vector2 normal = new Vector2(-segment.Y, segment.X).Normalized();
        float tickLength = Mathf.Min(CellSize.X, CellSize.Y) * (0.08f + 0.08f * Noise01(subsectorId, segmentIndex, 83));
        if (Noise01(subsectorId, segmentIndex, 89) > 0.5f)
        {
            normal = -normal;
        }

        DrawLine(midpoint - normal * tickLength * 0.5f,
            midpoint + normal * tickLength * 0.5f,
            WithAlpha(baseColor, 0.24f),
            1.0f,
            true);
    }

    private static float Noise01(ushort subsectorId, int index, uint salt)
    {
        uint value = (uint)subsectorId * 73856093u
            ^ (uint)(index + 1) * 19349663u
            ^ salt * 83492791u;

        value ^= value >> 16;
        value *= 2246822519u;
        value ^= value >> 13;
        value *= 3266489917u;
        value ^= value >> 16;

        return (value & 0x00FFFFFF) / 16777215.0f;
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

        for (int i = 0; i < orbitingFleets.Count; i++)
        {
            float angle = -Mathf.Pi / 2.0f + Mathf.Tau * i / Mathf.Max(orbitingFleets.Count, 1);
            float radius = baseRadius * (1.35f + 0.2f * (i % 2));
            Vector2 fleetPosition = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
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
            var color = kvp.Value.GetControllingFaction().Color;
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
