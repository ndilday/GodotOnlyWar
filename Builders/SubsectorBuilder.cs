using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Godot;
using OnlyWar.Models;
using OnlyWar.Models.Planets;

namespace OnlyWar.Builders
{
    public static class SubsectorBuilder
    {
        public struct Circle
        {
            public Vector2 Center;
            public float RadiusSquared;
        }

        public static List<Subsector> BuildSubsectors(IEnumerable<Planet> planets, Vector2I gridDimensions)
        {
            Dictionary<ushort, List<Planet>> subsectorPlanetMap = [];
            Dictionary<ushort, Circle> subsectorCircleMap;
            Dictionary<ushort, List<Vector2I>> subsectorCellListMap = [];
            List<Subsector> subsectorList = [];
            // subsector 0 is reserved for empty space that is not part of a subsector
            ushort subsectorId = 1;
            foreach(Planet planet in planets)
            {
                // assign each planet its own subsector to start
                Vector2I gridPosition = new Vector2I((int)planet.Position.Item1, (int)planet.Position.Item2);
                subsectorPlanetMap[subsectorId] = [planet];
                subsectorId++;
            }

            ushort maxDiameter = GameDataSingleton.Instance.GameRulesData.MaxSubsectorCellDiameter;
            CombineSubsectors(subsectorPlanetMap, maxDiameter);
            subsectorCircleMap = CalculateSubsectorCircles(subsectorPlanetMap);
            //subsectorRadiusSquaredMap = CalculateSubsectorSquaredRadii(subsectorPlanetMap, subsectorCenterMap);
            subsectorCellListMap = AssignGridSubsectors(subsectorPlanetMap, subsectorCircleMap, gridDimensions, (ushort)(maxDiameter / 2));
            foreach(var kvp in subsectorCellListMap)
            {
                subsectorList.Add(new Subsector(kvp.Key.ToString(), kvp.Key, subsectorPlanetMap[kvp.Key], kvp.Value));
            }
            return subsectorList;
        }

        private static void CombineSubsectors(Dictionary<ushort, List<Planet>> subsectorPlanetMap, ushort subsectorMaxDiameter)
        {
            int maxDistanceSquared = subsectorMaxDiameter * subsectorMaxDiameter;
            Dictionary<Tuple<ushort, ushort>, int> subsectorPairDistanceSquaredMap = [];
            Dictionary<ushort, List<ushort>> subsectorPairMap = [];
            Dictionary<ushort, int> subsectorInternalDistanceSquaredMap = [];

            // calculate the distance between each subsector
            foreach (var kvp in subsectorPlanetMap)
            {
                if (!subsectorInternalDistanceSquaredMap.ContainsKey(kvp.Key))
                {
                    subsectorInternalDistanceSquaredMap[kvp.Key] = 0;
                }
                foreach (var kvp2 in subsectorPlanetMap)
                {
                    if (kvp.Key >= kvp2.Key) continue;
                    // find the maximum distance between subsectors
                    int longestSquaredDistance = CalculateLongestPlanetaryDistanceSquared(kvp.Value, kvp2.Value);
                    // only keep results that could potentially merge
                    if (longestSquaredDistance < maxDistanceSquared)
                    {
                        Tuple<ushort, ushort> sectorPairId = new Tuple<ushort, ushort>(kvp.Key, kvp2.Key);
                        // store the distance squared between these two subsectors
                        subsectorPairDistanceSquaredMap[sectorPairId] = longestSquaredDistance;
                        // associate each subsector with the other as a potential match
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
            while (subsectorPairDistanceSquaredMap.Count > 0)
            {
                // we have to reorder every time, as we'll be modifying subsectors as we go
                var shortestDistance = subsectorPairDistanceSquaredMap.OrderBy(kvp => kvp.Value).First();

                //merge the two closest subsectors
                // add the points from the second subsector to the first, and remove the second subsector
                subsectorPlanetMap[shortestDistance.Key.Item1].AddRange(subsectorPlanetMap[shortestDistance.Key.Item2]);
                subsectorInternalDistanceSquaredMap[shortestDistance.Key.Item1] = shortestDistance.Value;
                subsectorPlanetMap.Remove(shortestDistance.Key.Item2);
                if (subsectorInternalDistanceSquaredMap.ContainsKey(shortestDistance.Key.Item2))
                {
                    subsectorInternalDistanceSquaredMap.Remove(shortestDistance.Key.Item2);
                }
                // remove all kvps involving the second subsector
                foreach (ushort otherSubsector in subsectorPairMap[shortestDistance.Key.Item2])
                {
                    // the smaller subsector id is always the first item
                    if (otherSubsector < shortestDistance.Key.Item2)
                    {
                        subsectorPairDistanceSquaredMap.Remove(new Tuple<ushort, ushort>(otherSubsector, shortestDistance.Key.Item2));
                    }
                    else
                    {
                        subsectorPairDistanceSquaredMap.Remove(new Tuple<ushort, ushort>(shortestDistance.Key.Item2, otherSubsector));
                    }
                    subsectorPairMap[otherSubsector].Remove(shortestDistance.Key.Item2);
                }
                subsectorPairMap.Remove(shortestDistance.Key.Item2);

                // recalculate squared distances for the new combined subsector
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
                        if (subsectorPairDistanceSquaredMap.ContainsKey(pair))
                        {
                            subsectorPairDistanceSquaredMap.Remove(pair);
                            subsectorPairMap[pair.Item1].Remove(pair.Item2);
                            subsectorPairMap[pair.Item2].Remove(pair.Item1);
                        }
                    }
                    else
                    {
                        subsectorPairDistanceSquaredMap[pair] = newDistanceSquared;
                    }
                }
            }
        }

        private static Dictionary<ushort, List<Vector2I>> AssignGridSubsectors(Dictionary<ushort, List<Planet>> subsectorPlanetMap, Dictionary<ushort, Circle> subsectorCircleMap,
                                                                               Vector2I gridDimensions, ushort subsectorMaxRadius)
        {
            Dictionary<ushort, List<Vector2I>> subsectorCellListMap = [];
            // iterate through each cell in the grid
            for (int j = 0; j < gridDimensions.Y; j++)
            {
                for (int i = 0; i < gridDimensions.X; i++)
                {
                    Vector2I gridPos = new Vector2I(i, j);
                    float currentDistanceSquared = int.MaxValue;
                    ushort currentSubsectorId = 0;

                    // find the closest subsector center
                    foreach (var subsectorCircle in subsectorCircleMap)
                    {
                        float subsectorRadiusSquared = subsectorCircleMap[subsectorCircle.Key].RadiusSquared;
                        float subsectorMaxRadiusSquared = subsectorMaxRadius * subsectorMaxRadius;
                        float maxRadiusSquared = Math.Max(subsectorRadiusSquared, subsectorMaxRadiusSquared);
                        //float maxRadiusSquared = subsectorRadiusSquared;

                        float distanceSquared = CalculateDistanceSquared(gridPos, subsectorCircle.Value.Center);

                        if (distanceSquared < currentDistanceSquared && distanceSquared <= maxRadiusSquared)
                        {
                            currentSubsectorId = subsectorCircle.Key;
                            currentDistanceSquared = distanceSquared;
                        }
                    }
                    if (currentSubsectorId != 0)
                    {
                        if (subsectorCellListMap.ContainsKey(currentSubsectorId))
                        {
                            subsectorCellListMap[currentSubsectorId].Add(gridPos);
                        }
                        else
                        {
                            subsectorCellListMap[currentSubsectorId] = [gridPos];
                        }
                    }
                }
            }
            return subsectorCellListMap;
        }

        private static Dictionary<ushort, Circle> CalculateSubsectorCircles(Dictionary<ushort, List<Planet>> subsectorPlanetMap)
        {
            Dictionary<ushort, Circle> circles = [];
            foreach (var subsectorPlanetList in subsectorPlanetMap)
            {
                List<Vector2> points = subsectorPlanetList.Value.Select(p => new Vector2(p.Position.Item1, p.Position.Item2)).ToList();
                circles[subsectorPlanetList.Key] = FindMinimumEnclosingCircle(points, new List<Vector2>());
            }
            return circles;
        }

        private static Circle FindMinimumEnclosingCircle(List<Vector2> points, List<Vector2> boundary)
        {
            if (points.Count == 0 || boundary.Count == 3)
            {
                return CalculateCircle(boundary);
            }

            Vector2 point = points[points.Count - 1];
            points = points.GetRange(0, points.Count - 1);

            Circle circle = FindMinimumEnclosingCircle(points, boundary);
            
            if (!IsInsideCircle(circle, point))
            {
                boundary = boundary.GetRange(0, boundary.Count);
                boundary.Add(point);
                circle = FindMinimumEnclosingCircle(points, boundary);
            }

            return circle;
        }

        private static Circle CalculateCircle(List<Vector2> boundary)
        {
            if (boundary.Count == 0)
            {
                return new Circle { Center = new Vector2(0, 0), RadiusSquared = 0 };
            }
            else if (boundary.Count == 1)
            {
                return new Circle { Center = boundary[0], RadiusSquared = 0 };
            }
            else if (boundary.Count == 2)
            {
                Vector2 newCenter = new Vector2((boundary[0].X + boundary[1].X) / 2, (boundary[0].Y + boundary[1].Y) / 2);
                return new Circle
                {
                    Center = newCenter,
                    RadiusSquared = CalculateDistanceSquared(boundary[0], newCenter)
                };
            }
            else
            {
                // Check if the points are collinear or form an obtuse triangle
                // Find the midpoint of the segment formed by the two farthest points
                float dist12 = CalculateDistanceSquared(boundary[0], boundary[1]);
                float dist13 = CalculateDistanceSquared(boundary[0], boundary[2]);
                float dist23 = CalculateDistanceSquared(boundary[1], boundary[2]);

                if (dist12 > dist13 + dist23)
                {
                    Vector2 midpoint = new Vector2((boundary[0].X + boundary[1].X) / 2, (boundary[0].Y + boundary[1].Y) / 2);
                    return new Circle
                    {
                        Center = midpoint,
                        RadiusSquared = CalculateDistanceSquared(boundary[0], midpoint)
                    };
                }
                else if (dist13 > dist12 + dist23)
                {
                    Vector2 midpoint = new Vector2((boundary[0].X + boundary[2].X) / 2, (boundary[0].Y + boundary[2].Y) / 2);
                    return new Circle
                    {
                        Center = midpoint,
                        RadiusSquared = CalculateDistanceSquared(boundary[0], midpoint)
                    };
                }
                else if (dist23 > dist12 + dist13)
                {
                    Vector2 midpoint = new Vector2((boundary[1].X + boundary[2].X) / 2, (boundary[1].Y + boundary[2].Y) / 2);
                    return new Circle 
                    { 
                        Center = midpoint, 
                        RadiusSquared = CalculateDistanceSquared(boundary[1], midpoint) 
                    };
                }
                else
                {
                    float p0 = boundary[0].X * boundary[0].X + boundary[0].Y * boundary[0].Y;
                    float p1 = boundary[1].X * boundary[1].X + boundary[1].Y * boundary[1].Y;
                    float p2 = boundary[2].X * boundary[2].X + boundary[2].Y * boundary[2].Y;
                    Vector2 v21 = boundary[2] - boundary[1];
                    Vector2 v02 = boundary[0] - boundary[2];
                    Vector2 v10 = boundary[1] - boundary[0];
                    float centerX = (p0 * v21.Y + p1 * v02.Y + p2 * v10.Y) / (boundary[0].X * v21.Y + boundary[1].X * v02.Y + boundary[2].X * v10.Y) / 2;
                    float centerY = (p0 * v21.X + p1 * v02.X + p2 * v10.X) / (boundary[0].Y * v21.X + boundary[1].Y * v02.X + boundary[2].Y * v10.X) / 2;
                    Vector2 newCenter = new Vector2(centerX, centerY);
                    return new Circle
                    {
                        Center = newCenter,
                        RadiusSquared = CalculateDistanceSquared(boundary[0], newCenter)
                    };
                }
            }
        }

        private static bool IsInsideCircle(Circle circle, Vector2 point)
        {
            return CalculateDistanceSquared(circle.Center, point) <= circle.RadiusSquared;
        }

        private static int CalculateLongestPlanetaryDistanceSquared(List<Planet> planets1, List<Planet> planets2)
        {
            int longestPlanetaryDistance = 0;
            foreach (var planet1 in planets1)
            {
                foreach (var planet2 in planets2)
                {
                    int distance = (planet1.Position.Item1 - planet2.Position.Item1) * (planet1.Position.Item1 - planet2.Position.Item1) + 
                        (planet1.Position.Item2 - planet2.Position.Item2) * (planet1.Position.Item2 - planet2.Position.Item2);
                    if (distance > longestPlanetaryDistance)
                    {
                        longestPlanetaryDistance = distance;
                    }
                }
            }

            return longestPlanetaryDistance;
        }

        private static float CalculateDistanceSquared(Vector2 coordinate1, Vector2 coordinate2)
        {
            float xDiff = coordinate1.X - coordinate2.X;
            float yDiff = coordinate1.Y - coordinate2.Y;
            return (xDiff * xDiff) + (yDiff * yDiff);
        }
    }
}
