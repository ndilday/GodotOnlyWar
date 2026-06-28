using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Builders
{
    public static class WarpLaneBuilder
    {
        // Lane quality is a placeholder hook for future navigator/transit-variance tuning.
        private const int IntraSubsectorQuality = 2;
        private const int InterSubsectorQuality = 1;

        /// <summary>
        /// Builds the basic warp-lane network: within each subsector every non-capital
        /// planet links to its subsector capital, and capitals link to the capitals of
        /// adjoining subsectors. Capitals are additionally connected by a minimum spanning
        /// tree so the whole sector is reachable even when subsectors are spread apart.
        /// </summary>
        public static List<WarpLane> BuildWarpLanes(IReadOnlyList<Subsector> subsectors, double adjacencyThreshold)
        {
            List<WarpLane> lanes = [];
            int nextLaneId = 0;
            List<Planet> capitals = [];

            foreach (Subsector subsector in subsectors)
            {
                if (subsector.Planets.Count == 0) continue;

                Planet capital = SelectCapital(subsector);
                capitals.Add(capital);

                foreach (Planet planet in subsector.Planets)
                {
                    if (planet == capital) continue;
                    lanes.Add(new WarpLane(nextLaneId++, IntraSubsectorQuality,
                        new Tuple<Planet, Planet>(capital, planet)));
                }
            }

            foreach (Tuple<Planet, Planet> connection in BuildCapitalConnections(capitals, adjacencyThreshold))
            {
                lanes.Add(new WarpLane(nextLaneId++, InterSubsectorQuality, connection));
            }

            return lanes;
        }

        private static Planet SelectCapital(Subsector subsector)
        {
            // PRD §4.1: for 0.7 the importance score driving capital selection is population.
            return subsector.Planets
                .OrderByDescending(planet => planet.Population)
                .ThenByDescending(planet => planet.Importance)
                .ThenBy(planet => planet.Id)
                .First();
        }

        private static IEnumerable<Tuple<Planet, Planet>> BuildCapitalConnections(
            IReadOnlyList<Planet> capitals, double adjacencyThreshold)
        {
            int count = capitals.Count;
            if (count < 2) yield break;

            HashSet<(int, int)> edges = [];

            // Direct lanes between capitals of adjoining (nearby) subsectors.
            double thresholdSquared = adjacencyThreshold * adjacencyThreshold;
            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    if (DistanceSquared(capitals[i], capitals[j]) <= thresholdSquared)
                    {
                        edges.Add((i, j));
                    }
                }
            }

            // Minimum spanning tree (Prim) guarantees the capital network is fully connected.
            bool[] inTree = new bool[count];
            inTree[0] = true;
            for (int added = 1; added < count; added++)
            {
                int bestFrom = -1;
                int bestTo = -1;
                double bestDistance = double.PositiveInfinity;
                for (int i = 0; i < count; i++)
                {
                    if (!inTree[i]) continue;
                    for (int j = 0; j < count; j++)
                    {
                        if (inTree[j]) continue;
                        double distance = DistanceSquared(capitals[i], capitals[j]);
                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            bestFrom = i;
                            bestTo = j;
                        }
                    }
                }
                if (bestTo == -1) break;
                inTree[bestTo] = true;
                edges.Add((Math.Min(bestFrom, bestTo), Math.Max(bestFrom, bestTo)));
            }

            foreach ((int i, int j) in edges)
            {
                yield return new Tuple<Planet, Planet>(capitals[i], capitals[j]);
            }
        }

        private static double DistanceSquared(Planet a, Planet b)
        {
            double x = a.Position.X - b.Position.X;
            double y = a.Position.Y - b.Position.Y;
            return (x * x) + (y * y);
        }
    }
}
