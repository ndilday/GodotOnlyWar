using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Fleets
{
    public class FleetRouteCalculator
    {
        private const int SystemTransitWeeks = 4;

        public FleetRoute CalculateBestRoute(Planet origin,
                                             Planet destination,
                                             IEnumerable<WarpLane> warpLanes,
                                             FleetRouteScope scope)
        {
            return CalculateBestRoute(origin, destination, warpLanes, scope, RNG.NextRandomZValue(), RNG.NextRandomZValue());
        }

        public FleetRoute CalculateBestRoute(Planet origin,
                                             Planet destination,
                                             IEnumerable<WarpLane> warpLanes,
                                             FleetRouteScope scope,
                                             double subjectiveZ,
                                             double objectiveZ)
        {
            if (origin == null) throw new ArgumentNullException(nameof(origin));
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (origin == destination) throw new InvalidOperationException("Origin and destination must be different planets.");

            FleetRoute laneRoute = CreateLaneRoute(origin, destination, warpLanes ?? Enumerable.Empty<WarpLane>(), scope, subjectiveZ, objectiveZ);
            if (laneRoute != null) return laneRoute;

            return CreateDirectRoute(origin, destination, scope, subjectiveZ, objectiveZ);
        }

        public FleetRoute CreateDirectRoute(Planet origin,
                                            Planet destination,
                                            FleetRouteScope scope,
                                            double subjectiveZ,
                                            double objectiveZ)
        {
            double distance = CalculateDistance(origin, destination);
            return CreateRoute(FleetRouteType.Direct, scope, [origin, destination], distance, subjectiveZ, objectiveZ);
        }

        public FleetRoute CreateLaneRoute(Planet origin,
                                          Planet destination,
                                          IEnumerable<WarpLane> warpLanes,
                                          FleetRouteScope scope,
                                          double subjectiveZ,
                                          double objectiveZ)
        {
            List<Planet> hops = FindShortestLanePath(origin, destination, warpLanes);
            if (hops.Count == 0) return null;

            double totalDistance = 0;
            for (int i = 0; i < hops.Count - 1; i++)
            {
                totalDistance += CalculateDistance(hops[i], hops[i + 1]);
            }

            return CreateRoute(FleetRouteType.WarpLane, scope, hops, totalDistance, subjectiveZ, objectiveZ);
        }

        private static FleetRoute CreateRoute(FleetRouteType routeType,
                                              FleetRouteScope scope,
                                              IReadOnlyList<Planet> hops,
                                              double totalDistance,
                                              double subjectiveZ,
                                              double objectiveZ)
        {
            int baseWarpWeeks = CalculateBaseWarpWeeks(scope);
            double subjectiveWarpWeeks = baseWarpWeeks * CalculateSubjectiveWarpMultiplier(subjectiveZ);
            double objectiveWarpWeeks = subjectiveWarpWeeks * CalculateObjectiveWarpMultiplier(objectiveZ);
            double subjectiveTotalWeeks = SystemTransitWeeks + subjectiveWarpWeeks;
            double objectiveTotalWeeks = SystemTransitWeeks + objectiveWarpWeeks;
            int objectiveTurns = Math.Max(1, (int)Math.Ceiling(objectiveTotalWeeks));

            return new FleetRoute(
                routeType,
                scope,
                hops,
                totalDistance,
                baseWarpWeeks,
                subjectiveWarpWeeks,
                objectiveWarpWeeks,
                subjectiveTotalWeeks,
                objectiveTotalWeeks,
                objectiveTurns,
                Math.Max(1, (int)Math.Floor(objectiveTotalWeeks)),
                objectiveTurns);
        }

        private static List<Planet> FindShortestLanePath(Planet origin, Planet destination, IEnumerable<WarpLane> warpLanes)
        {
            Dictionary<Planet, List<(Planet Planet, double Distance)>> graph = [];
            foreach (WarpLane lane in warpLanes)
            {
                Planet first = lane.Path.Item1;
                Planet second = lane.Path.Item2;
                AddEdge(graph, first, second);
                AddEdge(graph, second, first);
            }

            if (!graph.ContainsKey(origin) || !graph.ContainsKey(destination)) return [];

            Dictionary<Planet, double> distances = graph.Keys.ToDictionary(p => p, _ => double.PositiveInfinity);
            Dictionary<Planet, Planet> previous = [];
            HashSet<Planet> unvisited = graph.Keys.ToHashSet();
            distances[origin] = 0;

            while (unvisited.Count > 0)
            {
                Planet current = unvisited.OrderBy(p => distances[p]).First();
                if (double.IsPositiveInfinity(distances[current])) break;
                if (current == destination) break;

                unvisited.Remove(current);
                foreach ((Planet neighbor, double distance) in graph[current])
                {
                    if (!unvisited.Contains(neighbor)) continue;

                    double candidateDistance = distances[current] + distance;
                    if (candidateDistance < distances[neighbor])
                    {
                        distances[neighbor] = candidateDistance;
                        previous[neighbor] = current;
                    }
                }
            }

            if (!previous.ContainsKey(destination)) return [];

            List<Planet> path = [destination];
            Planet pathNode = destination;
            while (pathNode != origin)
            {
                pathNode = previous[pathNode];
                path.Add(pathNode);
            }
            path.Reverse();
            return path;
        }

        private static void AddEdge(Dictionary<Planet, List<(Planet Planet, double Distance)>> graph, Planet from, Planet to)
        {
            if (!graph.ContainsKey(from))
            {
                graph[from] = [];
            }

            graph[from].Add((to, CalculateDistance(from, to)));
        }

        public static double CalculateDistance(Planet origin, Planet destination)
        {
            double x = origin.Position.Item1 - destination.Position.Item1;
            double y = origin.Position.Item2 - destination.Position.Item2;
            return Math.Sqrt((x * x) + (y * y));
        }

        public static FleetRouteScope DetermineScope(Planet origin, Planet destination, double maxSubsectorDiameter)
        {
            if (origin == null) throw new ArgumentNullException(nameof(origin));
            if (destination == null) throw new ArgumentNullException(nameof(destination));

            // Subsector wiring and warp-lane topology are not yet generated, so scope is
            // approximated from raw inter-planet distance against the maximum subsector
            // diameter. This is a placeholder for true subsector-relationship scoping,
            // which is a post-lane-generation refinement (PRD 6.11).
            double distance = CalculateDistance(origin, destination);
            if (distance <= maxSubsectorDiameter) return FleetRouteScope.SameSubsector;
            if (distance <= maxSubsectorDiameter * 2.5) return FleetRouteScope.AdjacentSubsector;
            return FleetRouteScope.DistantSubsector;
        }

        public static int CalculateBaseWarpWeeks(FleetRouteScope scope)
        {
            return scope switch
            {
                FleetRouteScope.SameSubsector => 1,
                FleetRouteScope.AdjacentSubsector => 3,
                FleetRouteScope.DistantSubsector => 7,
                _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null)
            };
        }

        public static double CalculateSubjectiveWarpMultiplier(double zValue)
        {
            return zValue >= 0
                ? 1.0 / (1.0 + (2.0 * zValue))
                : 1.0 - (2.0 * zValue);
        }

        public static double CalculateObjectiveWarpMultiplier(double zValue)
        {
            return Math.Pow(10, -zValue / 5.0);
        }
    }
}
