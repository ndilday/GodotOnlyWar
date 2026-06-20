using Godot;
using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    /// <summary>
    /// PROTOTYPE: derives smooth subsector boundary polygons from a constrained
    /// Voronoi tessellation of planet "sites" plus "empty space" phantom sites.
    ///
    /// Rationale: instead of rasterizing subsectors into grid cells and then trying to
    /// trace and smooth a staircase, we treat each planet as a Voronoi site tagged with
    /// its subsector id and surround the populated region with phantom (empty-space)
    /// sites tagged subsector 0. The boundary between two real subsectors, or between a
    /// real subsector and empty space, is the set of Voronoi edges whose dual Delaunay
    /// edge joins two sites of different subsectors. Those edges already sit on the
    /// perpendicular bisector between sites (so they keep clear of the planets), and
    /// there are only a handful of them per subsector, so a light Chaikin pass reads as
    /// smooth rather than bumpy.
    ///
    /// All geometry is in GRID coordinates (1 unit == 1 cell == 1 light year), matching
    /// Planet.Position, so the caller converts to pixels exactly as it would a planet.
    /// </summary>
    public static class VoronoiSubsectorMapper
    {
        // ----- Tuning knobs (fractions of maxDiameter, in grid cells) -----------------

        // Spacing of the phantom lattice that fills empty space. Smaller => subsectors
        // are pinned tighter (less expansive), larger => they can breathe more.
        private const float PhantomLatticeSpacingFraction = 0.5f;

        // Phantom sites are rejected if they land within this distance of any planet, so
        // a real/empty border can never crowd a planet. The border sits at the midpoint
        // between the planet and the nearest phantom, i.e. >= half this clearance away.
        private const float PhantomClearanceFraction = 0.45f;

        // How far the phantom lattice extends beyond the planet bounding box. Must exceed
        // the clearance so the populated region is fully enclosed by phantom sites
        // (this guarantees every real site is interior and therefore has a bounded cell).
        private const float PhantomRingMarginFraction = 1.0f;

        // Chaikin corner-cutting passes applied to each boundary loop.
        private const int ChaikinPasses = 3;

        private const ushort EmptySpaceId = 0;

        /// <summary>
        /// Smoothed boundary loops per subsector, plus the subsector-to-subsector
        /// adjacency graph (two subsectors are adjacent iff they share a Voronoi border
        /// edge). The adjacency can be fed straight into a graph-coloring pass so that
        /// neighboring subsectors never share a color.
        /// </summary>
        public sealed class SubsectorBorders
        {
            public Dictionary<ushort, List<Vector2[]>> Loops { get; } = [];
            public Dictionary<ushort, HashSet<ushort>> Adjacency { get; } = [];
        }

        public static SubsectorBorders BuildSubsectorLoops(
            Dictionary<ushort, List<Planet>> subsectorPlanetMap,
            Vector2I gridDimensions,
            int maxDiameter)
        {
            SubsectorBorders result = new();
            if (subsectorPlanetMap.Count == 0) return result;

            // 1. Collect real sites (planets) tagged with their subsector id.
            List<double> siteX = [];
            List<double> siteY = [];
            List<ushort> siteId = [];
            foreach (var kvp in subsectorPlanetMap)
            {
                foreach (Planet planet in kvp.Value)
                {
                    siteX.Add(planet.Position.X);
                    siteY.Add(planet.Position.Y);
                    siteId.Add(kvp.Key);
                }
            }
            int realSiteCount = siteX.Count;
            if (realSiteCount == 0) return result;

            // 2. Add phantom "empty space" sites surrounding and infilling the region.
            AddPhantomSites(siteX, siteY, siteId, realSiteCount, maxDiameter);

            // 3. Delaunay triangulate every site, then read the Voronoi structure off it.
            List<Triangle> triangles = Triangulate(siteX, siteY);
            if (triangles.Count == 0) return result;

            // 4. Group boundary Voronoi edges per subsector and chain them into loops,
            //    capturing which subsectors border each other along the way.
            Dictionary<ushort, Dictionary<int, List<int>>> regionAdjacency =
                BuildRegionAdjacency(triangles, siteId, result.Adjacency);

            foreach (var kvp in regionAdjacency)
            {
                ushort subsectorId = kvp.Key;
                List<Vector2[]> loops = ChainLoops(kvp.Value, triangles);
                if (loops.Count > 0)
                {
                    result.Loops[subsectorId] = loops;
                }
            }

            return result;
        }

        // -----------------------------------------------------------------------------
        // Phantom sites
        // -----------------------------------------------------------------------------

        private static void AddPhantomSites(List<double> siteX, List<double> siteY,
                                            List<ushort> siteId, int realSiteCount, int maxDiameter)
        {
            double spacing = Math.Max(1.0, maxDiameter * PhantomLatticeSpacingFraction);
            double clearance = maxDiameter * PhantomClearanceFraction;
            double clearanceSquared = clearance * clearance;
            double margin = maxDiameter * PhantomRingMarginFraction;

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            for (int i = 0; i < realSiteCount; i++)
            {
                minX = Math.Min(minX, siteX[i]);
                minY = Math.Min(minY, siteY[i]);
                maxX = Math.Max(maxX, siteX[i]);
                maxY = Math.Max(maxY, siteY[i]);
            }

            double startX = minX - margin;
            double startY = minY - margin;
            double endX = maxX + margin;
            double endY = maxY + margin;

            for (double y = startY; y <= endY + 0.5; y += spacing)
            {
                for (double x = startX; x <= endX + 0.5; x += spacing)
                {
                    if (IsWithinClearanceOfAnyPlanet(siteX, siteY, realSiteCount, x, y, clearanceSquared))
                    {
                        continue;
                    }
                    siteX.Add(x);
                    siteY.Add(y);
                    siteId.Add(EmptySpaceId);
                }
            }
        }

        private static bool IsWithinClearanceOfAnyPlanet(List<double> siteX, List<double> siteY,
                                                         int realSiteCount, double x, double y, double clearanceSquared)
        {
            for (int i = 0; i < realSiteCount; i++)
            {
                double dx = siteX[i] - x;
                double dy = siteY[i] - y;
                if (dx * dx + dy * dy < clearanceSquared)
                {
                    return true;
                }
            }
            return false;
        }

        // -----------------------------------------------------------------------------
        // Region boundary extraction
        // -----------------------------------------------------------------------------

        private static Dictionary<ushort, Dictionary<int, List<int>>> BuildRegionAdjacency(
            List<Triangle> triangles, List<ushort> siteId,
            Dictionary<ushort, HashSet<ushort>> subsectorAdjacency)
        {
            // Map each Delaunay edge (unordered site pair) to the triangles that share it.
            Dictionary<long, List<int>> edgeToTriangles = [];
            for (int t = 0; t < triangles.Count; t++)
            {
                Triangle tri = triangles[t];
                RegisterEdge(edgeToTriangles, tri.A, tri.B, t);
                RegisterEdge(edgeToTriangles, tri.B, tri.C, t);
                RegisterEdge(edgeToTriangles, tri.C, tri.A, t);
            }

            // For every interior Delaunay edge that joins two different subsectors, the dual
            // Voronoi edge (between the two triangles' circumcenters) is a boundary segment
            // for each non-empty subsector it touches.
            Dictionary<ushort, Dictionary<int, List<int>>> regionAdjacency = [];
            foreach (var kvp in edgeToTriangles)
            {
                if (kvp.Value.Count != 2) continue;

                DecodeEdge(kvp.Key, out int siteA, out int siteB);
                ushort idA = siteId[siteA];
                ushort idB = siteId[siteB];
                if (idA == idB) continue;

                int triangle0 = kvp.Value[0];
                int triangle1 = kvp.Value[1];

                if (idA != EmptySpaceId) AddBoundarySegment(regionAdjacency, idA, triangle0, triangle1);
                if (idB != EmptySpaceId) AddBoundarySegment(regionAdjacency, idB, triangle0, triangle1);

                if (idA != EmptySpaceId && idB != EmptySpaceId)
                {
                    LinkSubsectors(subsectorAdjacency, idA, idB);
                }
            }

            return regionAdjacency;
        }

        private static void LinkSubsectors(Dictionary<ushort, HashSet<ushort>> subsectorAdjacency, ushort idA, ushort idB)
        {
            if (!subsectorAdjacency.TryGetValue(idA, out var neighborsA))
            {
                neighborsA = [];
                subsectorAdjacency[idA] = neighborsA;
            }
            if (!subsectorAdjacency.TryGetValue(idB, out var neighborsB))
            {
                neighborsB = [];
                subsectorAdjacency[idB] = neighborsB;
            }
            neighborsA.Add(idB);
            neighborsB.Add(idA);
        }

        private static void AddBoundarySegment(Dictionary<ushort, Dictionary<int, List<int>>> regionAdjacency,
                                               ushort subsectorId, int triangle0, int triangle1)
        {
            if (!regionAdjacency.TryGetValue(subsectorId, out var adjacency))
            {
                adjacency = [];
                regionAdjacency[subsectorId] = adjacency;
            }
            AddAdjacency(adjacency, triangle0, triangle1);
            AddAdjacency(adjacency, triangle1, triangle0);
        }

        private static void AddAdjacency(Dictionary<int, List<int>> adjacency, int from, int to)
        {
            if (!adjacency.TryGetValue(from, out var neighbors))
            {
                neighbors = [];
                adjacency[from] = neighbors;
            }
            neighbors.Add(to);
        }

        private static List<Vector2[]> ChainLoops(Dictionary<int, List<int>> adjacency, List<Triangle> triangles)
        {
            List<Vector2[]> loops = [];
            HashSet<long> visitedEdges = [];

            foreach (int startNode in adjacency.Keys)
            {
                foreach (int firstNeighbor in adjacency[startNode])
                {
                    long startEdge = EdgeKey(startNode, firstNeighbor);
                    if (!visitedEdges.Add(startEdge)) continue;

                    List<int> loopNodes = [startNode];
                    int previous = startNode;
                    int current = firstNeighbor;
                    int guard = 0;
                    int maxSteps = adjacency.Count * 2 + 4;

                    while (current != startNode && guard++ < maxSteps)
                    {
                        loopNodes.Add(current);
                        if (!adjacency.TryGetValue(current, out var neighbors) || neighbors.Count < 2)
                        {
                            break;
                        }
                        int next = neighbors[0] == previous ? neighbors[1] : neighbors[0];
                        visitedEdges.Add(EdgeKey(current, next));
                        previous = current;
                        current = next;
                    }

                    if (current == startNode && loopNodes.Count >= 3)
                    {
                        loops.Add(BuildSmoothedLoop(loopNodes, triangles));
                    }
                }
            }

            return loops;
        }

        private static Vector2[] BuildSmoothedLoop(List<int> loopNodes, List<Triangle> triangles)
        {
            Vector2[] rawPoints = new Vector2[loopNodes.Count];
            for (int i = 0; i < loopNodes.Count; i++)
            {
                Triangle tri = triangles[loopNodes[i]];
                rawPoints[i] = new Vector2((float)tri.CircumX, (float)tri.CircumY);
            }
            return ChaikinClosed(rawPoints, ChaikinPasses);
        }

        private static Vector2[] ChaikinClosed(Vector2[] points, int passes)
        {
            Vector2[] current = points;
            for (int pass = 0; pass < passes && current.Length >= 3; pass++)
            {
                Vector2[] next = new Vector2[current.Length * 2];
                for (int i = 0; i < current.Length; i++)
                {
                    Vector2 a = current[i];
                    Vector2 b = current[(i + 1) % current.Length];
                    next[i * 2] = a.Lerp(b, 0.25f);
                    next[i * 2 + 1] = a.Lerp(b, 0.75f);
                }
                current = next;
            }
            return current;
        }

        // -----------------------------------------------------------------------------
        // Edge key helpers
        // -----------------------------------------------------------------------------

        private static void RegisterEdge(Dictionary<long, List<int>> edgeToTriangles, int siteU, int siteV, int triangle)
        {
            long key = EdgeKey(siteU, siteV);
            if (!edgeToTriangles.TryGetValue(key, out var list))
            {
                list = [];
                edgeToTriangles[key] = list;
            }
            list.Add(triangle);
        }

        private static long EdgeKey(int a, int b)
        {
            int lo = Math.Min(a, b);
            int hi = Math.Max(a, b);
            return ((long)lo << 32) | (uint)hi;
        }

        private static void DecodeEdge(long key, out int a, out int b)
        {
            a = (int)(key >> 32);
            b = (int)(key & 0xFFFFFFFF);
        }

        // -----------------------------------------------------------------------------
        // Delaunay triangulation (Bowyer-Watson, double precision)
        // -----------------------------------------------------------------------------

        private struct Triangle
        {
            public int A;
            public int B;
            public int C;
            public double CircumX;
            public double CircumY;
            public double CircumRadiusSquared;
        }

        private static List<Triangle> Triangulate(List<double> siteX, List<double> siteY)
        {
            int n = siteX.Count;
            if (n < 3) return [];

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                minX = Math.Min(minX, siteX[i]);
                minY = Math.Min(minY, siteY[i]);
                maxX = Math.Max(maxX, siteX[i]);
                maxY = Math.Max(maxY, siteY[i]);
            }

            double width = Math.Max(maxX - minX, 1.0);
            double height = Math.Max(maxY - minY, 1.0);
            double delta = Math.Max(width, height) * 10.0;
            double midX = (minX + maxX) / 2.0;
            double midY = (minY + maxY) / 2.0;

            // Three super-triangle vertices appended after the real sites.
            int s0 = n, s1 = n + 1, s2 = n + 2;
            siteX.Add(midX - delta); siteY.Add(midY - delta);
            siteX.Add(midX); siteY.Add(midY + delta);
            siteX.Add(midX + delta); siteY.Add(midY - delta);

            List<Triangle> triangles = [CreateTriangle(s0, s1, s2, siteX, siteY)];

            for (int i = 0; i < n; i++)
            {
                double px = siteX[i];
                double py = siteY[i];

                // Find triangles whose circumcircle contains the point and collect the
                // boundary of the resulting hole.
                Dictionary<long, int> edgeCount = [];
                Dictionary<long, (int, int)> edgeVerts = [];
                for (int t = triangles.Count - 1; t >= 0; t--)
                {
                    Triangle tri = triangles[t];
                    double dx = px - tri.CircumX;
                    double dy = py - tri.CircumY;
                    if (dx * dx + dy * dy <= tri.CircumRadiusSquared + 1e-9)
                    {
                        AccumulateEdge(edgeCount, edgeVerts, tri.A, tri.B);
                        AccumulateEdge(edgeCount, edgeVerts, tri.B, tri.C);
                        AccumulateEdge(edgeCount, edgeVerts, tri.C, tri.A);
                        triangles.RemoveAt(t);
                    }
                }

                foreach (var edge in edgeCount)
                {
                    if (edge.Value != 1) continue;
                    (int u, int v) = edgeVerts[edge.Key];
                    triangles.Add(CreateTriangle(u, v, i, siteX, siteY));
                }
            }

            // Drop any triangle that still references a super-triangle vertex.
            triangles.RemoveAll(tri => tri.A >= n || tri.B >= n || tri.C >= n);
            return triangles;
        }

        private static void AccumulateEdge(Dictionary<long, int> edgeCount,
                                           Dictionary<long, (int, int)> edgeVerts, int u, int v)
        {
            long key = EdgeKey(u, v);
            if (edgeCount.TryGetValue(key, out int count))
            {
                edgeCount[key] = count + 1;
            }
            else
            {
                edgeCount[key] = 1;
                edgeVerts[key] = (u, v);
            }
        }

        private static Triangle CreateTriangle(int a, int b, int c, List<double> siteX, List<double> siteY)
        {
            double ax = siteX[a], ay = siteY[a];
            double bx = siteX[b], by = siteY[b];
            double cx = siteX[c], cy = siteY[c];

            double d = 2.0 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
            double circumX, circumY;
            if (Math.Abs(d) < 1e-12)
            {
                // Degenerate (collinear) — fall back to centroid; this triangle will be
                // effectively inert.
                circumX = (ax + bx + cx) / 3.0;
                circumY = (ay + by + cy) / 3.0;
            }
            else
            {
                double a2 = ax * ax + ay * ay;
                double b2 = bx * bx + by * by;
                double c2 = cx * cx + cy * cy;
                circumX = (a2 * (by - cy) + b2 * (cy - ay) + c2 * (ay - by)) / d;
                circumY = (a2 * (cx - bx) + b2 * (ax - cx) + c2 * (bx - ax)) / d;
            }

            double radiusSquared = (ax - circumX) * (ax - circumX) + (ay - circumY) * (ay - circumY);
            return new Triangle
            {
                A = a,
                B = b,
                C = c,
                CircumX = circumX,
                CircumY = circumY,
                CircumRadiusSquared = radiusSquared
            };
        }
    }
}
