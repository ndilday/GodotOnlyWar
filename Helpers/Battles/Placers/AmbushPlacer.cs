using OnlyWar.Models;
﻿using System;
using System.Collections.Generic;
using System.Linq;


namespace OnlyWar.Helpers.Battles.Placers
{
    public class AmbushPlacer
    {
        // The ambushed force is laid out as a marching column running along the Y axis.
        // Ambushers form an L: a long leg parallel to that column (the west face, firing
        // east) plus a short leg capping the north end (firing south). The two legs are
        // perpendicular and the far (north-west) corner is left open, so no element ever
        // fires across the kill zone into another -- a third side would create that
        // friendly-fire risk, so we deliberately use at most two adjacent sides.

        // Fraction of ambusher frontage assigned to the long (main) leg; the remainder
        // caps one end as the short leg.
        private const double LongLegFrontageShare = 0.6;
        private const int MaxFormationStandoff = 200;

        private readonly BattleGridManager _grid;
        private readonly ushort _engagementRange;

        private readonly struct KillZone
        {
            public int MinX { get; }
            public int MaxX { get; }
            public int MinY { get; }
            public int MaxY { get; }
            public int CenterX => (MinX + MaxX) / 2;
            public int CenterY => (MinY + MaxY) / 2;

            public KillZone(int minX, int maxX, int minY, int maxY)
            {
                MinX = minX;
                MaxX = maxX;
                MinY = minY;
                MaxY = maxY;
            }
        }

        public AmbushPlacer(BattleGridManager grid, ushort engagementRange)
        {
            _grid = grid;
            _engagementRange = engagementRange;
        }

        public Dictionary<BattleSquad, Tuple<int, int>> PlaceSquads(IReadOnlyList<BattleSquad> ambushedSquads,
                                                                    IReadOnlyList<BattleSquad> ambushingSquads)
        {
            Dictionary<BattleSquad, Tuple<int, int>> result = [];
            KillZone killZone = PlaceAmbushedSquads(ambushedSquads, 0, 0, result);
            PlaceAmbushingSquads(ambushingSquads, killZone, result);
            return result;
        }

        private KillZone PlaceAmbushedSquads(IReadOnlyList<BattleSquad> squads, int xMid, int top,
                                             Dictionary<BattleSquad, Tuple<int, int>> squadPositionMap)
        {
            // Center each squad on the midpoint, stacked one behind the other down the Y axis.
            int topLimit = top;
            int minX = int.MaxValue, maxX = int.MinValue;
            int minY = int.MaxValue, maxY = int.MinValue;
            foreach (BattleSquad squad in squads)
            {
                Coordinate squadSize = squad.GetSquadBoxSize();
                int left = xMid - squadSize.X / 2;
                int bottom = topLimit - squadSize.Y;
                squadPositionMap[squad] = new Tuple<int, int>(left, bottom);
                BattleSquadPlacer.PlaceBattleSquad(_grid, squad, new Tuple<int, int>(left, bottom), true, true);

                // the squad occupies X in [left, left + width) and Y in [bottom, topLimit]
                minX = Math.Min(minX, left);
                maxX = Math.Max(maxX, left + squadSize.X);
                minY = Math.Min(minY, bottom);
                maxY = Math.Max(maxY, topLimit);

                topLimit -= squadSize.Y + 1;
            }

            return new KillZone(minX, maxX, minY, maxY);
        }

        private void PlaceAmbushingSquads(IReadOnlyList<BattleSquad> ambushingSquads,
                                          KillZone killZone,
                                          Dictionary<BattleSquad, Tuple<int, int>> squadPositionMap)
        {
            if (ambushingSquads.Count == 0)
            {
                return;
            }

            // Anchor the biggest squads on the main firing line first.
            List<BattleSquad> sorted = ambushingSquads
                .OrderByDescending(s => s.AbleSoldiers.Count)
                .ToList();

            // A squad's "frontage" is how many soldiers stand across its firing line -- the
            // width of its box (GetSquadBoxSize().X) -- regardless of which leg it lands on.
            int totalFrontage = sorted.Sum(s => s.GetSquadBoxSize().X);
            List<BattleSquad> longLeg = [];
            List<BattleSquad> shortLeg = [];
            int longLegFrontage = 0;
            foreach (BattleSquad squad in sorted)
            {
                // Always seed the long leg with at least one squad; fill it to its frontage
                // share, then send the remainder to the short leg. A single squad therefore
                // degrades cleanly to a linear (one-side) ambush.
                if (longLeg.Count == 0 || longLegFrontage < LongLegFrontageShare * totalFrontage)
                {
                    longLeg.Add(squad);
                    longLegFrontage += squad.GetSquadBoxSize().X;
                }
                else
                {
                    shortLeg.Add(squad);
                }
            }

            PlaceLongLeg(longLeg, killZone, squadPositionMap);
            PlaceShortLeg(shortLeg, killZone, squadPositionMap);
        }

        private void PlaceLongLeg(IReadOnlyList<BattleSquad> squads, KillZone killZone,
                                  Dictionary<BattleSquad, Tuple<int, int>> squadPositionMap)
        {
            // West face: squads placed vertically, running along Y, firing east across the
            // column. Anchored at the kill zone's north edge and filled southward, so the leg
            // never rises into the short leg's latitude -- that shared north-west corner is the
            // junction, and keeping the long leg south of it stops the two legs raking each
            // other. An overlong leg simply overruns south, covering the column's length.
            int standoff = FormationStandoff();
            int y = killZone.MaxY;
            foreach (BattleSquad squad in squads)
            {
                Coordinate squadSize = squad.GetSquadBoxSize();
                // A vertical squad is squadSize.Y thick in X and squadSize.X long in Y.
                // Put its east edge one standoff west of the kill zone.
                int left = killZone.MinX - standoff - squadSize.Y;
                int bottom = y - squadSize.X;
                squadPositionMap[squad] = new Tuple<int, int>(left, bottom);
                BattleSquadPlacer.PlaceBattleSquad(_grid, squad, new Tuple<int, int>(left, bottom), false, false);
                y -= squadSize.X + 1;
            }
        }

        private void PlaceShortLeg(IReadOnlyList<BattleSquad> squads, KillZone killZone,
                                   Dictionary<BattleSquad, Tuple<int, int>> squadPositionMap)
        {
            if (squads.Count == 0)
            {
                return;
            }

            // North face: squads placed horizontally, running along X, firing south down the
            // column. Centered over the kill zone but never extended west of it, so it can
            // never reach back under the long leg (keeping the north-west corner open).
            int standoff = FormationStandoff();
            int legLength = squads.Sum(s => s.GetSquadBoxSize().X) + Math.Max(0, squads.Count - 1);
            int x = Math.Max(killZone.MinX, killZone.CenterX - legLength / 2);
            int bottom = killZone.MaxY + standoff;
            foreach (BattleSquad squad in squads)
            {
                Coordinate squadSize = squad.GetSquadBoxSize();
                squadPositionMap[squad] = new Tuple<int, int>(x, bottom);
                BattleSquadPlacer.PlaceBattleSquad(_grid, squad, new Tuple<int, int>(x, bottom), true, false);
                x += squadSize.X + 1;
            }
        }

        private int FormationStandoff() =>
            Math.Clamp((int)_engagementRange, 1, MaxFormationStandoff);
    }
}
