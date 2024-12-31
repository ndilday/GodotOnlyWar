using Godot;
using OnlyWar.Builders;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
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
	public Vector2I GridDimensions =
		new(GameDataSingleton.Instance.GameRulesData.SectorSize.Item1,
			GameDataSingleton.Instance.GameRulesData.SectorSize.Item2);
	public Vector2I CellSize = 
		new(GameDataSingleton.Instance.GameRulesData.SectorCellSize.Item1,
			GameDataSingleton.Instance.GameRulesData.SectorCellSize.Item2);

	public Vector2I HalfCellSize { get; private set; }
	public ushort[] SectorIds { get; private set; }
	public bool[] HasPlanet { get; private set; }
	
	private Dictionary<ushort, List<Vector2I>> _subsectorVertexListMap;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		HalfCellSize = CellSize / 2;
		SectorIds = new ushort[GridDimensions.X * GridDimensions.Y];
		HasPlanet = new bool[GridDimensions.X * GridDimensions.Y];
		PlacePlanets();
		PlaceFleets();
		SubsectorBuilder.BuildSubsectors(GameDataSingleton.Instance.Sector.Planets.Values, GridDimensions);
	}

	public override void _Draw()
	{
		base._Draw();
		float goldenRatioConjugate = 0.618f;
		float colorVal = (float)Random.Shared.NextDouble();
		foreach (var kvp in _subsectorVertexListMap)
		{
			DrawColoredPolygon(kvp.Value.Select(vector => new Vector2(vector.X, vector.Y)).ToArray(), ConvertHsvToRgb(colorVal, 0.5f, 0.95f));
			colorVal += goldenRatioConjugate;
			colorVal %= 1;
		}
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
		var starTexture = (Texture2D)GD.Load("res://MainGameScreen/UICircle.png");
		Vector2 starTextureScale = new Vector2(0.05f, 0.05f);
		Random rand = new Random();
		foreach(var kvp in GameDataSingleton.Instance.Sector.Planets)
		{
			Vector2I gridPosition = new(kvp.Value.Position.Item1, kvp.Value.Position.Item2);
			int index = GridPositionToIndex(gridPosition);
			HasPlanet[index] = true;
			DrawTexture(starTexture, starTextureScale, gridPosition);

		}
	}

	private void PlaceFleets()
	{
		var shipTexture = GD.Load<AtlasTexture>(("res://Assets/shipAtlasTexture.tres"));
		Vector2 shipTextureScale = new Vector2(0.1f, 0.1f);
		foreach(var taskForceKvp in GameDataSingleton.Instance.Sector.Fleets)
		{
            TaskForce taskForce = taskForceKvp.Value;

            // Determine the position for the fleet's sprite
            Vector2I gridPosition;
            if (taskForce.Planet != null)
            {
                // Fleet is in orbit around a planet

                // Assuming you have a way to get the planet's position
                // You'll need to implement GetPlanetSpritePosition or similar
                gridPosition = new(taskForce.Planet.Position.Item1, taskForce.Planet.Position.Item2);
            }
            else
            {
                // Fleet is in space, use its map coordinates
                gridPosition = new Vector2I(taskForce.Position.Item1, taskForce.Position.Item2);
            }

			// Make sure you are the owner of the new node, or it will not save properly
			DrawTexture(shipTexture, shipTextureScale, gridPosition, 2, true);

            // You might want to store a reference to the sprite in the TaskForce 
            // or in a separate dictionary for later access/updates.
            // For example:
            // taskForce.Sprite = fleetSprite; 
        }
    }

    private Dictionary<ushort, List<Vector2I>> DetermineSubsectorBorderPoints(ushort[] sectorIds)
    {
        int i = 0;
        Dictionary<ushort, List<Vector2I>> subsectorVertexListMap = [];
        while (i < sectorIds.Length)
        {
            ushort subsectorId = sectorIds[i];
            if (subsectorId == 0 || subsectorVertexListMap.ContainsKey(subsectorId))
            {
                i++;
            }
            else
            {
                List<Vector2I> vertexList = [];
                subsectorVertexListMap[subsectorId] = vertexList;
                // because we're processing to the right and down, the top edge is a border
                Vector2I gridPosition = IndexToGridPosition(i);
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
                    currentPoint = GetNextPoint(currentPoint, subsectorId);
                    vertexList.Add(currentPoint.mapPoint);
                }
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

    private void DrawTexture(Texture2D texture, Vector2 scale, Vector2I gridPosition, int zIndex = 1, bool offset=false)
	{
		Sprite2D newSprite = new Sprite2D();
		this.AddChild(newSprite);
        newSprite.Owner = this;
		Vector2I mapPosition = CalculateMapPosition(gridPosition);
		if(offset)
		{
			mapPosition += HalfCellSize * new Vector2I( 1,-1);
		}
        newSprite.GlobalPosition = mapPosition;
        newSprite.Texture = texture;
        newSprite.Scale = scale;
		newSprite.ZIndex = zIndex;
	}

	private Color ConvertHsvToRgb(float h, float s, float v)
	{
		int h_i = (int)(h * 6);
		float f = h * 6 - h_i;
		float p = v * (1 - s);
		float q = v * (1 - f * s);
		float t = v * (1 - (1 - f) * s);
		float r, g, b;
		switch (h_i)
		{
			case 0:
				r = v;
				g = t;
				b = p;
				break;
			case 1:
				r = q;
				g = v;
				b = p;
				break;
			case 2:
				r = p;
				g = v;
				b = t;
				break;
			case 3:
				r = p;
				g = q;
				b = v;
				break;
			case 4:
				r = t;
				g = p;
				b = v;
				break;
			default:
			case 5:
				r = v;
				g = p;
				b = q;
				break;
		}
		return new Color(r, g, b);
	}
}
