using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OnlyWar.Models;
using OnlyWar.Models.Planets;

namespace OnlyWar.Builders
{
    public static class SubsectorBuilder
    {
        public static List<Subsector> BuildSubsectors(IEnumerable<Planet> planets, Vector2I gridDimensions)
        {
            Dictionary<ushort, List<Planet>> subsectorPlanetMap = [];
            Dictionary<ushort, Vector2I> subsectorCenterMap;
            Dictionary<ushort, int> subsectorDiameterSquaredMap;
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
            
            subsectorDiameterSquaredMap = CombineSubsectors(subsectorPlanetMap, GameDataSingleton.Instance.GameRulesData.MaxSubsectorCellDiameter);
            subsectorCenterMap = CalculateSubsectorCenters(subsectorPlanetMap);
            subsectorCellListMap = AssignGridSubsectors(subsectorPlanetMap, subsectorCenterMap, subsectorDiameterSquaredMap, gridDimensions);
            foreach(var kvp in subsectorCellListMap)
            {
                subsectorList.Add(new Subsector(kvp.Key.ToString(), subsectorPlanetMap[kvp.Key], kvp.Value));
            }
            return subsectorList;
        }

        private static Dictionary<ushort, int> CombineSubsectors(Dictionary<ushort, List<Planet>> subsectorPlanetMap, ushort subsectorMaxDiameter)
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
            return subsectorInternalDistanceSquaredMap;
        }

        private static Dictionary<ushort, List<Vector2I>> AssignGridSubsectors(Dictionary<ushort, List<Planet>> subsectorPlanetMap, Dictionary<ushort, Vector2I> subsectorCenterMap,
                                                                               Dictionary<ushort, int> subsectorDiameterSquaredMap, Vector2I gridDimensions)
        {
            Dictionary<ushort, List<Vector2I>> subsectorCellListMap = [];
            // iterate through each cell in the grid
            for (int j = 0; j < gridDimensions.Y; j++)
            {
                for (int i = 0; i < gridDimensions.X; i++)
                {
                    Vector2I gridPos = new Vector2I(i, j);
                    int currentDistanceSquared = int.MaxValue;
                    ushort currentSubsectorId = 0;

                    // find the closest subsector center
                    foreach (var subsectorCenter in subsectorCenterMap)
                    {
                        int diameterSquared = subsectorDiameterSquaredMap[subsectorCenter.Key];

                        int radiusSquared = diameterSquared / 4;
                        int distanceSquared = CalculateDistanceSquared(gridPos, subsectorCenter.Value);
                        if (distanceSquared < currentDistanceSquared && distanceSquared <= radiusSquared)
                        {
                            currentSubsectorId = subsectorCenter.Key;
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

        private static Dictionary<ushort, Vector2I> CalculateSubsectorCenters(Dictionary<ushort, List<Planet>> subsectorPlanetMap)
        {
            Dictionary<ushort, Vector2I> centers = [];
            foreach (var subsectorPlanetList in subsectorPlanetMap)
            {
                int x = subsectorPlanetList.Value.Sum(v => v.Position.Item1) / subsectorPlanetList.Value.Count;
                int y = subsectorPlanetList.Value.Sum(v => v.Position.Item2) / subsectorPlanetList.Value.Count;
                centers[subsectorPlanetList.Key] = new Vector2I(x, y);
            }
            return centers;
        }

        private static int CalculateLongestPlanetaryDistanceSquared(List<Planet> planets1, List<Planet> planets2)
        {
            int longestPlanetaryDistance = 0;
            foreach (var planet1 in planets1)
            {
                Vector2I coordinate1 = new Vector2I(planet1.Position.Item1, planet1.Position.Item2);
                foreach (var planet2 in planets2)
                {
                    Vector2I coordinate2 = new Vector2I(planet2.Position.Item1, planet2.Position.Item2);
                    int distance = CalculateDistanceSquared(coordinate1, coordinate2);
                    if (distance > longestPlanetaryDistance)
                    {
                        longestPlanetaryDistance = distance;
                    }
                }
            }

            return longestPlanetaryDistance;
        }

        private static int CalculateDistanceSquared(Vector2I coordinate1, Vector2I coordinate2)
        {
            int xDiff = coordinate1.X - coordinate2.X;
            int yDiff = coordinate1.Y - coordinate2.Y;
            return (xDiff * xDiff) + (yDiff * yDiff);
        }
    }
}
