using Godot;
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
    private Dictionary<ushort, List<Vector2I>> _subsectorPlanetMap;
	private Dictionary<ushort, Vector2I> _subsectorCenterMap;
	private Dictionary<ushort, int> _subsectorDiameterSquaredMap;
	private Dictionary<ushort, List<Vector2I>> _subsectorVertexListMap;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		HalfCellSize = CellSize / 2;
		SectorIds = new ushort[GridDimensions.X * GridDimensions.Y];
		HasPlanet = new bool[GridDimensions.X * GridDimensions.Y];
		PlacePlanets();
		PlaceFleets();
		GenerateSubssectors();
	}

	public override void _Input(InputEvent @event)
	{
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

    public bool IsWithinBounds(Vector2I cellCoordinates)
    {
        return (cellCoordinates.X >= 0 && cellCoordinates.X < GridDimensions.X && cellCoordinates.Y >= 0 && cellCoordinates.Y < GridDimensions.Y);
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
		_subsectorPlanetMap = [];

		var starTexture = (Texture2D)GD.Load("res://MainGameScreen/UICircle.png");
		Vector2 starTextureScale = new Vector2(0.05f, 0.05f);
		ushort currentSubsectorId = 1;
		Random rand = new Random();
		foreach(var kvp in GameDataSingleton.Instance.Sector.Planets)
		{
			Vector2I gridPosition = new(kvp.Value.Position.Item1, kvp.Value.Position.Item2);
			int index = GridPositionToIndex(gridPosition);
			HasPlanet[index] = true;
			DrawTexture(starTexture, starTextureScale, gridPosition);
			_subsectorPlanetMap[currentSubsectorId] =
                [
                    gridPosition
				];
			currentSubsectorId++;
		}
	}

	private void PlaceFleets()
	{
		var shipTexture = (Texture2D)GD.Load(("res://Assets/shipAtlastTexture.tres"));
		Vector2 shipTextureScale = new Vector2(0.05f, 0.05f);
		foreach(var taskForceKvp in GameDataSingleton.Instance.Sector.Fleets)
		{
            TaskForce taskForce = taskForceKvp.Value;

            // Determine the position for the fleet's sprite
            Vector2 fleetPosition;
            if (taskForce.Planet != null)
            {
                // Fleet is in orbit around a planet

                // Assuming you have a way to get the planet's position
                // You'll need to implement GetPlanetSpritePosition or similar
                Vector2I gridPosition = new(taskForce.Planet.Position.Item1, taskForce.Planet.Position.Item2);
                Vector2 planetSpritePosition = CalculateMapPosition(gridPosition);

                // Offset from the top-right of the planet
                fleetPosition = planetSpritePosition + HalfCellSize;
            }
            else
            {
                // Fleet is in space, use its map coordinates
                fleetPosition = CalculateMapPosition(new Vector2I(taskForce.Position.Item1, taskForce.Position.Item2));
            }

            // Create the sprite and add it to the scene
            Sprite2D fleetSprite = new Sprite2D();
            AddChild(fleetSprite);

            // Make sure you are the owner of the new node, or it will not save properly
            fleetSprite.Owner = this;
            fleetSprite.Texture = shipTexture;
            fleetSprite.Scale = shipTextureScale;
            fleetSprite.Position = fleetPosition;
            fleetSprite.ZIndex = 2;

            // You might want to store a reference to the sprite in the TaskForce 
            // or in a separate dictionary for later access/updates.
            // For example:
            // taskForce.Sprite = fleetSprite; 
        }
    }

	private void DrawTexture(Texture2D texture, Vector2 scale, Vector2I gridPosition)
	{
		Sprite2D newSprite = new Sprite2D();
		this.AddChild(newSprite);
        newSprite.Owner = this;
        newSprite.GlobalPosition = CalculateMapPosition(gridPosition);
        newSprite.Texture = texture;
        newSprite.Scale = scale;
	}

	private void GenerateSubssectors()
	{
		_subsectorDiameterSquaredMap = CombineSubsectors(_subsectorPlanetMap, GameDataSingleton.Instance.GameRulesData.MaxSubsectorCellDiameter);
		_subsectorCenterMap = CalculateSubSectorCenters(_subsectorPlanetMap);
		AssignGridSubsectors(_subsectorPlanetMap, _subsectorCenterMap, _subsectorDiameterSquaredMap, SectorIds, GameDataSingleton.Instance.GameRulesData.MaxSubsectorCellDiameter / 2);
		_subsectorVertexListMap = DetermineSubsectorBorderPoints(SectorIds);
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

	private Dictionary<ushort, int> CombineSubsectors(Dictionary<ushort, List<Vector2I>> subsectorPlanetMap, ushort subsectorMaxDiameter)
	{
		int maxDistanceSquared = subsectorMaxDiameter * subsectorMaxDiameter;
		Dictionary<Tuple<ushort, ushort>, int> subsectorPairDistanceMap = [];
		Dictionary<ushort, List<ushort>> subsectorPairMap = [];
		Dictionary<ushort, int> subsectorInternalDistance = [];

		// calculate the distance between each subsector
		foreach (var kvp in subsectorPlanetMap)
		{
			if (!subsectorInternalDistance.ContainsKey(kvp.Key))
			{
				subsectorInternalDistance[kvp.Key] = 0;
			}
			foreach (var kvp2 in subsectorPlanetMap)
			{
				if (kvp.Key >= kvp2.Key) continue;
				// find the maximum distance between subsectors
				int longestPlanetaryDistance = CalculateLongestPlanetaryDistanceSquared(kvp.Value, kvp2.Value);
				// only keep results that could potentially merge
				if (longestPlanetaryDistance < maxDistanceSquared)
				{
					Tuple<ushort, ushort> sectorPairId = new Tuple<ushort, ushort>(kvp.Key, kvp2.Key);
					subsectorPairDistanceMap[sectorPairId] = longestPlanetaryDistance;
					if (!subsectorPairMap.ContainsKey(kvp.Key))
					{
						subsectorPairMap[kvp.Key] = [];
					}
					if (!subsectorPairMap.ContainsKey(kvp2.Key))
					{
						subsectorPairMap[kvp2.Key] = [];
					}

					subsectorPairMap[kvp.Key].Add(kvp2.Key);
					subsectorPairMap[kvp2.Key].Add(kvp.Key);
				}
			}
		}
		while (subsectorPairDistanceMap.Count > 0)
		{
			var shortestDistance = subsectorPairDistanceMap.OrderBy(kvp => kvp.Value).First();

			// add the points from the second subsector to the first, and remove the second subsector
			subsectorPlanetMap[shortestDistance.Key.Item1].AddRange(subsectorPlanetMap[shortestDistance.Key.Item2]);
			subsectorInternalDistance[shortestDistance.Key.Item1] = shortestDistance.Value;
			subsectorPlanetMap.Remove(shortestDistance.Key.Item2);
			if (subsectorInternalDistance.ContainsKey(shortestDistance.Key.Item2))
			{
				subsectorInternalDistance.Remove(shortestDistance.Key.Item2);
			}
			// remove all kvps involving the second subsector
			foreach (ushort otherSubsector in subsectorPairMap[shortestDistance.Key.Item2])
			{
				// the smaller subsector id is always the first item
				if (otherSubsector < shortestDistance.Key.Item2)
				{

					subsectorPairDistanceMap.Remove(new Tuple<ushort, ushort>(otherSubsector, shortestDistance.Key.Item2));
				}
				else
				{
					subsectorPairDistanceMap.Remove(new Tuple<ushort, ushort>(shortestDistance.Key.Item2, otherSubsector));
				}
				subsectorPairMap[otherSubsector].Remove(shortestDistance.Key.Item2);
			}
			subsectorPairMap.Remove(shortestDistance.Key.Item2);

			// recalculate distances for the new combined subsector
			foreach (ushort otherSubsector in subsectorPairMap[shortestDistance.Key.Item1].ToList())
			{
				Tuple<ushort, ushort> pair;
				if (otherSubsector < shortestDistance.Key.Item1)
				{
					pair = new Tuple<ushort, ushort>(otherSubsector, shortestDistance.Key.Item1);
				}
				else if (otherSubsector > shortestDistance.Key.Item1)
				{
					pair = new Tuple<ushort, ushort>(shortestDistance.Key.Item1, otherSubsector);
				}
				else
				{
					continue;
				}

				int newDistanceSquared =
					CalculateLongestPlanetaryDistanceSquared(subsectorPlanetMap[pair.Item1], subsectorPlanetMap[pair.Item2]);

				if (newDistanceSquared > maxDistanceSquared)
				{
					// after combination, the other subsector is no longer in range
					if (subsectorPairDistanceMap.ContainsKey(pair))
					{
						subsectorPairDistanceMap.Remove(pair);
						subsectorPairMap[pair.Item1].Remove(pair.Item2);
						subsectorPairMap[pair.Item2].Remove(pair.Item1);
					}
				}
				else
				{
					subsectorPairDistanceMap[pair] = newDistanceSquared;
				}
			}
		}
		return subsectorInternalDistance;
	}

	private void AssignGridSubsectors(Dictionary<ushort, List<Vector2I>> subsectorPlanetMap, Dictionary<ushort, Vector2I> subsectorCenterMap, 
									  Dictionary<ushort, int> subsectorDiameterSquaredMap, ushort[] sectorIds, int minDiameter)
	{
		foreach(var kvp in subsectorPlanetMap)
		{
			foreach(Vector2I planetLocation in kvp.Value)
			{
				int index = GridPositionToIndex(planetLocation);
				sectorIds[index] = kvp.Key;
			}
		}

		int minDiameterSquared = minDiameter * minDiameter;
		for (int j = 0; j < GridDimensions.Y; j++)
		{
			for (int i = 0; i < GridDimensions.X; i++)
			{
				Vector2I gridPos = new Vector2I(i, j);
				int index = GridPositionToIndex(gridPos);
				if (sectorIds[index] != 0) continue;
				int currentDistanceSquared = minDiameterSquared * 10;
				foreach (var subsectorCenter in subsectorCenterMap)
				{
					int diameterSquared = subsectorDiameterSquaredMap[subsectorCenter.Key];
					if (diameterSquared < minDiameterSquared)
					{
						diameterSquared = minDiameterSquared;
					}
					int radiusSquared = diameterSquared / 4;
					int distanceSquared = CalculateDistanceSquared(gridPos, subsectorCenter.Value);
					if (distanceSquared < currentDistanceSquared && distanceSquared <= radiusSquared)
					{
						SectorIds[index] = subsectorCenter.Key;
						currentDistanceSquared = distanceSquared;
					}
				}
				if (currentDistanceSquared == minDiameterSquared * 10)
				{
					SectorIds[index] = 0;
				}
			}
		}
	}

	private Dictionary<ushort, List<Vector2I>> DetermineSubsectorBorderPoints(ushort[] sectorIds)
	{
		int i = 0;
		Dictionary<ushort, List<Vector2I>> subsectorVertexListMap = [];
		while(i < sectorIds.Length)
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

	private Dictionary<ushort, Vector2I> CalculateSubSectorCenters(Dictionary<ushort, List<Vector2I>> subsectorPlanetMap)
	{
		Dictionary<ushort, Vector2I> centers = [];
		foreach (var subsectorPlanetList in subsectorPlanetMap)
		{
			int x = subsectorPlanetList.Value.Sum(v => v.X) / subsectorPlanetList.Value.Count;
			int y = subsectorPlanetList.Value.Sum(v => v.Y) / subsectorPlanetList.Value.Count;
			centers[subsectorPlanetList.Key] = new Vector2I(x, y);
		}
		return centers;
	}

	private int CalculateLongestPlanetaryDistanceSquared(List<Vector2I> coordinates1, List<Vector2I> coordinates2)
	{
		int longestPlanetaryDistance = 0;
		foreach (var coordinate1 in coordinates1)
		{
			foreach (var coordinate2 in coordinates2)
			{
				int distance = CalculateDistanceSquared(coordinate1, coordinate2);
				if (distance > longestPlanetaryDistance)
				{
					longestPlanetaryDistance = distance;
				}
			}
		}

		return longestPlanetaryDistance;
	}

	private int CalculateDistanceSquared(Vector2I coordinate1, Vector2I coordinate2)
	{
		int xDiff = coordinate1.X - coordinate2.X;
		int yDiff = coordinate1.Y - coordinate2.Y;
		return (xDiff * xDiff) + (yDiff * yDiff);
	}
}
