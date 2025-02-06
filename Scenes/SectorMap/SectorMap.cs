using Godot;
using OnlyWar.Builders;
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
    public event EventHandler<int> PlanetClicked;
    public event EventHandler<int> FleetClicked;

    public Vector2I GridDimensions =
		new(GameDataSingleton.Instance.GameRulesData.SectorSize.Item1,
			GameDataSingleton.Instance.GameRulesData.SectorSize.Item2);
	public Vector2I CellSize = 
		new(GameDataSingleton.Instance.GameRulesData.SectorCellSize.Item1,
			GameDataSingleton.Instance.GameRulesData.SectorCellSize.Item2);

	public Vector2I HalfCellSize { get; private set; }
	public ushort[] SectorIds { get; private set; }
	public bool[] HasPlanet { get; private set; }
    private Camera2D _camera;
	
	private Dictionary<ushort, List<Vector2I>> _subsectorVertexListMap;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
        _camera = GetNode<Camera2D>("Camera2D");
		HalfCellSize = CellSize / 2;
		SectorIds = new ushort[GridDimensions.X * GridDimensions.Y];
		HasPlanet = new bool[GridDimensions.X * GridDimensions.Y];
		PlacePlanets();
		PlaceFleets();
		List<Subsector> subsectors = SubsectorBuilder.BuildSubsectors(GameDataSingleton.Instance.Sector.Planets.Values, GridDimensions);
        foreach(Subsector subsector in subsectors)
        {
            foreach (Vector2I cell in subsector.Cells)
            {
                SectorIds[GridPositionToIndex(cell)] = subsector.Id;
            }
        }
        _subsectorVertexListMap = DetermineSubsectorBorderPoints(subsectors);
        Planet centerPlanet = GameDataSingleton.Instance.Sector.PlayerForce.Fleet.TaskForces[0].Planet;
        Vector2I gridPosition = new Vector2I(centerPlanet.Position.Item1, centerPlanet.Position.Item2);
        Vector2I mapPosition = CalculateMapPosition(gridPosition);
        _camera.ZoomTo(1, mapPosition);
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
		Random rand = new Random();
		foreach(var kvp in GameDataSingleton.Instance.Sector.Planets)
		{
			Vector2I gridPosition = new(kvp.Value.Position.Item1, kvp.Value.Position.Item2);
			int index = GridPositionToIndex(gridPosition);
			HasPlanet[index] = true;
            var color = kvp.Value.ControllingFaction.Color;
            ClickableSprite2D planet = DrawTexture(starTexture, starTextureScale, gridPosition, new Color(color.R, color.G, color.B, color.A));
            planet.Pressed += (object sender, EventArgs e) => PlanetClicked.Invoke(planet, kvp.Key);
		}
	}

	private void PlaceFleets()
	{
		var shipTexture = GD.Load<AtlasTexture>(("res://Assets/shipAtlasTexture.tres"));
		Vector2 shipTextureScale = new Vector2(0.2f, 0.2f);
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
			ClickableSprite2D fleet = DrawTexture(shipTexture, shipTextureScale, gridPosition, Color.Color8(255, 255, 255), 2, true);
            fleet.Pressed += (object sender, EventArgs e) => FleetClicked.Invoke(fleet, taskForceKvp.Key);
        }
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
		ClickableSprite2D newSprite = new ClickableSprite2D();
		this.AddChild(newSprite);
        newSprite.Owner = this;
		Vector2I mapPosition = CalculateMapPosition(gridPosition);
		if(offset)
		{
			mapPosition += HalfCellSize * new Vector2I( 1,-1);
		}
        newSprite.GlobalPosition = mapPosition;
        newSprite.Texture = texture;
        newSprite.Modulate = color;
        newSprite.Scale = scale;
		newSprite.ZIndex = zIndex;
        return newSprite;
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
		return new Color(r, g, b, 0.25f);
	}
}
