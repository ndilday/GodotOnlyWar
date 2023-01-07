using Godot;
using OnlyWar.Models;
using System;
using System.Collections.Generic;
using System.Linq;

enum Facing { North, East, South, West}
struct BorderPoint
{
    public Vector2i gridPos;
    public Vector2i mapPoint;
    public Facing orientation;
}

public partial class SectorMap : Node2D
{
    public Vector2i GridDimensions =
        new(GameDataSingleton.Instance.GameRulesData.SectorSize.Item1,
            GameDataSingleton.Instance.GameRulesData.SectorSize.Item2);
    public Vector2i CellSize = 
        new(GameDataSingleton.Instance.GameRulesData.SectorCellSize.Item1,
            GameDataSingleton.Instance.GameRulesData.SectorCellSize.Item2);

    private Vector2i _halfCellSize;
    private ushort[] _sectorIds;
    private bool[] _hasPlanet;
    private Dictionary<ushort, List<Vector2i>> _subsectorPlanetMap;
    private Dictionary<ushort, Vector2i> _subsectorCenterMap;
    private Dictionary<ushort, int> _subsectorDiameterSquaredMap;
    private Dictionary<ushort, List<Vector2i>> _subsectorVertexListMap;
    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
	{
        _halfCellSize = CellSize / 2;
        _sectorIds = new ushort[GridDimensions.x * GridDimensions.y];
        _hasPlanet = new bool[GridDimensions.x * GridDimensions.y];
        PlacePlanets();
        GenerateSubssectors();
    }

    public override void _Input(InputEvent @event)
    {
        if(@event is InputEventMouseButton emb && emb.ButtonIndex == MouseButton.MaskLeft)
        {
            Vector2 gmpos = GetGlobalMousePosition();
            Vector2i mousePosition = new((int)(gmpos.x), (int)(gmpos.y));
            Vector2i gridPosition = CalculateGridCoordinates(mousePosition);
            int index = GridPositionToIndex(gridPosition);
            string text = $"({gridPosition.x},{gridPosition.y})\nPlanet: {_hasPlanet[index]}\nSubsector: {_sectorIds[index]}";
            GetNode<TopMenu>("CanvasLayer/TopMenu").SetDebugText(text);
        }
    }

    public override void _Draw()
    {
        base._Draw();
        float goldenRatioConjugate = 0.618f;
        float colorVal = (float)Random.Shared.NextDouble();
        foreach (var kvp in _subsectorVertexListMap)
        {
            DrawColoredPolygon(kvp.Value.Select(vector => new Vector2(vector.x, vector.y)).ToArray(), ConvertHsvToRgb(colorVal, 0.5f, 0.95f));
            colorVal += goldenRatioConjugate;
            colorVal %= 1;
            /*borders.Polygon = kvp.Value.Select(vector => new Vector2(vector.x, vector.y)).ToArray();
            this.AddChild(borders);
            borders.Owner = this;
            borders.Color = ConvertHsvToRgb(colorVal, 0.5f, 0.95f);
            */
        }
        /*Vector2i mapPos = CalculateMapPosition(gridPos);
        Label label = new Label();
        this.AddChild(label);
        label.Owner = this;
        label.Text = _sectorIds[index].ToString();
        label.Position = mapPos;*/
    }

    private void PlacePlanets()
    {
        _subsectorPlanetMap = new Dictionary<ushort, List<Vector2i>>();

        var starTexture = (Texture2D)GD.Load("res://MainGameScreen/UICircle.png");
        Vector2 starTextureScale = new Vector2(0.05f, 0.05f);
        ushort currentSubsectorId = 1;
        Random rand = new Random();
        foreach(var kvp in GameDataSingleton.Instance.Sector.Planets)
        {
            Vector2i gridPosition = new(kvp.Value.Position.Item1, kvp.Value.Position.Item2);
            int index = GridPositionToIndex(gridPosition);
            _hasPlanet[index] = true;
            DrawStar(starTexture, starTextureScale, gridPosition);
            _subsectorPlanetMap[currentSubsectorId] = new List<Vector2i>
                {
                    gridPosition
                };
            currentSubsectorId++;
        }
    }

    private void DrawStar(Texture2D starTexture, Vector2 starTextureScale, Vector2i gridPosition)
    {
        Sprite2D newStarSprite = new Sprite2D();
        this.AddChild(newStarSprite);
        newStarSprite.Owner = this;
        newStarSprite.GlobalPosition = CalculateMapPosition(gridPosition);
        newStarSprite.Texture = starTexture;
        newStarSprite.Scale = starTextureScale;
    }

    public Vector2i CalculateMapPosition(Vector2i gridPosition)
    {
        return gridPosition * CellSize + _halfCellSize;
    }

    public Vector2i CalculateGridCoordinates(Vector2i mapPosition)
    {
        return (mapPosition / CellSize);
    }

    public bool IsWithinBounds(Vector2i cellCoordinates)
    {
        return (cellCoordinates.x >= 0 && cellCoordinates.x < GridDimensions.x && cellCoordinates.y >= 0 && cellCoordinates.y < GridDimensions.y);
    }

    public int GridPositionToIndex(Vector2i cell)
    {
        return (GridDimensions.x * cell.y + cell.x);
    }

    public Vector2i IndexToGridPosition(int index)
    {
        int x = index % GridDimensions.x;
        int y = index / GridDimensions.x;
        return new Vector2i(x, y);
    }

    private void GenerateSubssectors()
    {
        _subsectorDiameterSquaredMap = CombineSubsectors(_subsectorPlanetMap, GameDataSingleton.Instance.GameRulesData.MaxSubsectorCellDiameter);
        _subsectorCenterMap = CalculateSubSectorCenters(_subsectorPlanetMap);
        AssignGridSubsectors(_subsectorPlanetMap, _subsectorCenterMap, _subsectorDiameterSquaredMap, _sectorIds, GameDataSingleton.Instance.GameRulesData.MaxSubsectorCellDiameter / 2);
        _subsectorVertexListMap = DetermineSubsectorBorderPoints(_sectorIds);
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

    private Dictionary<ushort, int> CombineSubsectors(Dictionary<ushort, List<Vector2i>> subsectorPlanetMap, ushort subsectorMaxDiameter)
    {
        int maxDistanceSquared = subsectorMaxDiameter * subsectorMaxDiameter;
        Dictionary<Tuple<ushort, ushort>, int> subsectorPairDistanceMap = new Dictionary<Tuple<ushort, ushort>, int>();
        Dictionary<ushort, List<ushort>> subsectorPairMap = new Dictionary<ushort, List<ushort>>();
        Dictionary<ushort, int> subsectorInternalDistance = new Dictionary<ushort, int>();

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
                        subsectorPairMap[kvp.Key] = new List<ushort>();
                    }
                    if (!subsectorPairMap.ContainsKey(kvp2.Key))
                    {
                        subsectorPairMap[kvp2.Key] = new List<ushort>();
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

    private void AssignGridSubsectors(Dictionary<ushort, List<Vector2i>> subsectorPlanetMap, Dictionary<ushort, Vector2i> subsectorCenterMap, 
                                      Dictionary<ushort, int> subsectorDiameterSquaredMap, ushort[] sectorIds, int minDiameter)
    {
        foreach(var kvp in subsectorPlanetMap)
        {
            foreach(Vector2i planetLocation in kvp.Value)
            {
                int index = GridPositionToIndex(planetLocation);
                sectorIds[index] = kvp.Key;
            }
        }

        int minDiameterSquared = minDiameter * minDiameter;
        for (int j = 0; j < GridDimensions.y; j++)
        {
            for (int i = 0; i < GridDimensions.x; i++)
            {
                Vector2i gridPos = new Vector2i(i, j);
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
                        _sectorIds[index] = subsectorCenter.Key;
                        currentDistanceSquared = distanceSquared;
                    }
                }
                if (currentDistanceSquared == minDiameterSquared * 10)
                {
                    _sectorIds[index] = 0;
                }
            }
        }
    }

    private Dictionary<ushort, List<Vector2i>> DetermineSubsectorBorderPoints(ushort[] sectorIds)
    {
        int i = 0;
        Dictionary<ushort, List<Vector2i>> subsectorVertexListMap = new Dictionary<ushort, List<Vector2i>>();
        while(i < sectorIds.Length)
        {
            ushort subsectorId = sectorIds[i];
            if (subsectorId == 0 || subsectorVertexListMap.ContainsKey(subsectorId))
            {
                i++;
            }
            else
            {
                List<Vector2i> vertexList = new List<Vector2i>();
                subsectorVertexListMap[subsectorId] = vertexList;
                // because we're processing to the right and down, the top edge is a border
                Vector2i gridPosition = IndexToGridPosition(i);
                Vector2i cellCenterPosition = CalculateMapPosition(gridPosition);
                Vector2i topLeft = cellCenterPosition - _halfCellSize;
                Vector2i topRight = new Vector2i(topLeft.x + CellSize.x, topLeft.y);
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
                    gridPos = currentPoint.gridPos + new Vector2i(-1, -1),
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2i.Left,
                    orientation = Facing.West
                };
                straight = new BorderPoint
                {
                    gridPos = currentPoint.gridPos + new Vector2i(0, -1),
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2i.Up,
                    orientation = Facing.North
                };
                right = new BorderPoint
                {
                    gridPos = currentPoint.gridPos,
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2i.Right,
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
                    gridPos = currentPoint.gridPos + new Vector2i(1, -1),
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2i.Up,
                    orientation = Facing.North
                };
                straight = new BorderPoint
                {
                    gridPos = currentPoint.gridPos + new Vector2i(1, 0),
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2i.Right,
                    orientation = Facing.East
                };
                right = new BorderPoint
                {
                    gridPos = currentPoint.gridPos,
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2i.Down,
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
                    gridPos = currentPoint.gridPos + new Vector2i(1, 1),
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2i.Right,
                    orientation = Facing.East
                };
                straight = new BorderPoint
                {
                    gridPos = currentPoint.gridPos + new Vector2i(0, 1),
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2i.Down,
                    orientation = Facing.South
                };
                right = new BorderPoint
                {
                    gridPos = currentPoint.gridPos,
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2i.Left,
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
                    gridPos = currentPoint.gridPos + new Vector2i(-1, 1),
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2i.Down,
                    orientation = Facing.South
                };
                straight = new BorderPoint
                {
                    gridPos = currentPoint.gridPos + new Vector2i(-1, 0),
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2i.Left,
                    orientation = Facing.West
                };
                right = new BorderPoint
                {
                    gridPos = currentPoint.gridPos,
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2i.Up,
                    orientation = Facing.North
                };
                break;
        }
        // if left is in bounds and part of this sector, it's next
        if (IsWithinBounds(left.gridPos) && _sectorIds[GridPositionToIndex(left.gridPos)] == subsectorId)
        {
            return left;
        }
        else if (IsWithinBounds(straight.gridPos) && _sectorIds[GridPositionToIndex(straight.gridPos)] == subsectorId)
        {
            return straight;
        }
        else
        {
            return right;
        }
    }

    private Dictionary<ushort, Vector2i> CalculateSubSectorCenters(Dictionary<ushort, List<Vector2i>> subsectorPlanetMap)
    {
        Dictionary<ushort, Vector2i> centers = new Dictionary<ushort, Vector2i>();
        foreach (var subsectorPlanetList in subsectorPlanetMap)
        {
            int x = subsectorPlanetList.Value.Sum(v => v.x) / subsectorPlanetList.Value.Count;
            int y = subsectorPlanetList.Value.Sum(v => v.y) / subsectorPlanetList.Value.Count;
            centers[subsectorPlanetList.Key] = new Vector2i(x, y);
        }
        return centers;
    }

    private int CalculateLongestPlanetaryDistanceSquared(List<Vector2i> coordinates1, List<Vector2i> coordinates2)
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

    private int CalculateDistanceSquared(Vector2i coordinate1, Vector2i coordinate2)
    {
        int xDiff = coordinate1.x - coordinate2.x;
        int yDiff = coordinate1.y - coordinate2.y;
        return (xDiff * xDiff) + (yDiff * yDiff);
    }
}
