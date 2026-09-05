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
        // How far behind a firing leg an ambushing HQ squad deploys (away from the kill
        // zone). Modest: enough that the counter-fire finds the line first, small enough
        // to keep the HQ inside its command aura and weapon reach.
        private const int HqRearOffset = 10;

        private readonly BattleGridManager _grid;
        private readonly ushort _engagementRange;
        // Set from the ambushed force at placement time; see MinimumStandoff.
        private int _minimumStandoff = 1;

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

        public Dictionary<BattleSquad, ValueTuple<int, int>> PlaceSquads(IReadOnlyList<BattleSquad> ambushedSquads,
                                                                    IReadOnlyList<BattleSquad> ambushingSquads)
        {
            Dictionary<BattleSquad, ValueTuple<int, int>> result = [];
            _minimumStandoff = MinimumStandoff(ambushedSquads);
            KillZone killZone = PlaceAmbushedSquads(OrderColumnHqMid(ambushedSquads), 0, 0, result);
            PlaceAmbushingSquads(ambushingSquads, killZone, result);
            return result;
        }

        /// <summary>
        /// No ambush is ever set inside one turn of the ambushed force's own movement, whatever
        /// range the mission derived.
        ///
        /// <para>A standoff shorter than that hands the column a free charge on the turn the ambush
        /// is sprung: the ambushers get one volley and then are fighting the melee they set the
        /// ambush to avoid. Xibarrus Zeta (2026-08-04) is the case -- a derived standoff of 1 yard
        /// put three marine squads in contact with a Broodlord and a Carnifex on turn 1 and cost 29
        /// of 30 marines. The derivation that produced that 1 is fixed in
        /// <c>BattleEngagementFrameBuilder.CalculatePreferredOpeningRange</c>; this is the floor that
        /// keeps a future one from being catastrophic rather than merely wrong, and it is expressed
        /// in the quantity that actually matters -- how far the enemy moves in a turn -- rather than
        /// as another round number.</para>
        /// </summary>
        private static int MinimumStandoff(IReadOnlyList<BattleSquad> ambushedSquads)
        {
            float fastest = ambushedSquads?
                .Where(squad => squad != null && squad.AbleSoldiers.Count > 0)
                .Select(squad => squad.GetSquadMove())
                .DefaultIfEmpty(0f)
                .Max() ?? 0f;
            return Math.Max(1, (int)Math.Ceiling(fastest));
        }

        private static bool IsHqSquad(BattleSquad squad) =>
            squad?.Traits.IsHeadquarters == true
            || squad?.Squad?.SquadTemplate?.SquadType.HasFlag(Models.Squads.SquadTypes.HQ) == true;

        // A marching column keeps its command element mid-column, not at the head where the
        // ambush's short leg fires first: line squads front and rear, HQs between them.
        private static IReadOnlyList<BattleSquad> OrderColumnHqMid(IReadOnlyList<BattleSquad> squads)
        {
            List<BattleSquad> line = squads.Where(s => !IsHqSquad(s)).ToList();
            List<BattleSquad> hq = squads.Where(IsHqSquad).ToList();
            if (hq.Count == 0 || line.Count == 0)
            {
                return squads;
            }
            List<BattleSquad> ordered = [];
            int lead = (line.Count + 1) / 2;
            ordered.AddRange(line.Take(lead));
            ordered.AddRange(hq);
            ordered.AddRange(line.Skip(lead));
            return ordered;
        }

        private KillZone PlaceAmbushedSquads(IReadOnlyList<BattleSquad> squads, int xMid, int top,
                                             Dictionary<BattleSquad, ValueTuple<int, int>> squadPositionMap)
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
                squadPositionMap[squad] = new ValueTuple<int, int>(left, bottom);
                BattleSquadPlacer.PlaceBattleSquad(_grid, squad, new ValueTuple<int, int>(left, bottom), true, true, true);

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
                                          Dictionary<BattleSquad, ValueTuple<int, int>> squadPositionMap)
        {
            if (ambushingSquads.Count == 0)
            {
                return;
            }

            // HQ squads deploy behind the firing legs, not on them; if the ambush force is
            // nothing but HQs, they take the line themselves.
            List<BattleSquad> hqSquads = ambushingSquads.Where(IsHqSquad).ToList();
            List<BattleSquad> lineSquads = ambushingSquads.Where(s => !IsHqSquad(s)).ToList();
            if (lineSquads.Count == 0)
            {
                lineSquads = hqSquads;
                hqSquads = [];
            }

            // Anchor the biggest squads on the main firing line first.
            List<BattleSquad> sorted = lineSquads
                .OrderByDescending(s => s.AbleSoldiers.Count)
                .ToList();

            // A squad's "frontage" is how many soldiers stand across its firing line -- the
            // width of its loose formation -- regardless of which leg it lands on.
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
            PlaceHqSquads(hqSquads, longLeg, shortLeg, killZone, squadPositionMap);
        }

        // Each HQ deploys one rear offset behind a firing leg, on the side away from the
        // kill zone, spread evenly along the leg's span. HQs are split between the legs in
        // proportion to how much line each leg holds (a large two-leg ambush gets command
        // presence behind both faces; a one-leg ambush keeps them all behind that leg).
        private void PlaceHqSquads(IReadOnlyList<BattleSquad> hqSquads,
                                   IReadOnlyList<BattleSquad> longLeg,
                                   IReadOnlyList<BattleSquad> shortLeg,
                                   KillZone killZone,
                                   Dictionary<BattleSquad, ValueTuple<int, int>> squadPositionMap)
        {
            if (hqSquads.Count == 0)
            {
                return;
            }

            int shortLegShare = shortLeg.Count == 0
                ? 0
                : hqSquads.Count * shortLeg.Count / (longLeg.Count + shortLeg.Count);
            List<BattleSquad> behindLong = hqSquads.Take(hqSquads.Count - shortLegShare).ToList();
            List<BattleSquad> behindShort = hqSquads.Skip(hqSquads.Count - shortLegShare).ToList();
            int standoff = FormationStandoff();

            // Behind the long (west) leg: further west. The leg's thickest squad sets the
            // west face; HQs spread down the leg's length (the kill zone's Y span).
            if (behindLong.Count > 0)
            {
                int legThickness = longLeg.Max(s => (int)s.GetSquadBoxSize().Y);
                int westFace = killZone.MinX - standoff - legThickness;
                int ySpan = Math.Max(0, killZone.MaxY - killZone.MinY);
                for (int i = 0; i < behindLong.Count; i++)
                {
                    BattleSquad squad = behindLong[i];
                    Coordinate squadSize = squad.GetSquadBoxSize();
                    int left = westFace - HqRearOffset - squadSize.Y;
                    int centerY = killZone.MaxY - ySpan * (i + 1) / (behindLong.Count + 1);
                    int bottom = centerY - squadSize.X / 2;
                    ValueTuple<int, int> position = new(left, bottom);
                    squadPositionMap[squad] = position;
                    BattleSquadPlacer.PlaceBattleSquad(_grid, squad, position, false, false, false);
                }
            }

            // Behind the short (north) leg: further north, spread across the leg's width.
            if (behindShort.Count > 0)
            {
                int legThickness = shortLeg.Max(s => (int)s.GetSquadBoxSize().Y);
                int northFace = killZone.MaxY + standoff + legThickness;
                int legLength = shortLeg.Sum(s => s.GetSquadBoxSize().X)
                    + Math.Max(0, shortLeg.Count - 1);
                int legWest = Math.Max(killZone.MinX, killZone.CenterX - legLength / 2);
                for (int i = 0; i < behindShort.Count; i++)
                {
                    BattleSquad squad = behindShort[i];
                    Coordinate squadSize = squad.GetSquadBoxSize();
                    int centerX = legWest + legLength * (i + 1) / (behindShort.Count + 1);
                    int left = centerX - squadSize.X / 2;
                    int bottom = northFace + HqRearOffset;
                    ValueTuple<int, int> position = new(left, bottom);
                    squadPositionMap[squad] = position;
                    BattleSquadPlacer.PlaceBattleSquad(_grid, squad, position, true, false, false);
                }
            }
        }

        private void PlaceLongLeg(IReadOnlyList<BattleSquad> squads, KillZone killZone,
                                  Dictionary<BattleSquad, ValueTuple<int, int>> squadPositionMap)
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
                squadPositionMap[squad] = new ValueTuple<int, int>(left, bottom);
                BattleSquadPlacer.PlaceBattleSquad(_grid, squad, new ValueTuple<int, int>(left, bottom), false, false, false);
                y -= squadSize.X + 1;
            }
        }

        private void PlaceShortLeg(IReadOnlyList<BattleSquad> squads, KillZone killZone,
                                   Dictionary<BattleSquad, ValueTuple<int, int>> squadPositionMap)
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
                squadPositionMap[squad] = new ValueTuple<int, int>(x, bottom);
                BattleSquadPlacer.PlaceBattleSquad(_grid, squad, new ValueTuple<int, int>(x, bottom), true, false, false);
                x += squadSize.X + 1;
            }
        }

        /// <summary>
        /// How far the firing legs stand off from the kill zone: the engagement range the mission
        /// chose, floored by <see cref="MinimumStandoff"/> so the ambushers never share cells with
        /// the column and never spring the ambush inside a single enemy bound.
        ///
        /// <para>PHASE 7 (Design/Reference/BattleLogic.md) REMOVED THE UPPER CLAMP. It
        /// was <c>Math.Clamp((int)_engagementRange, 1, 200)</c>, added 2026-07-08 in commit 4d6182c
        /// inside a change about PDF drafting and garrison rates, unmentioned in that commit's
        /// message and accompanied only by a test asserting that coordinates do not wrap. It was an
        /// overflow guard whose 200 was a round number, and it has acted as an unreviewed balance
        /// constant ever since -- capping every ambush at 200 yards regardless of what either force
        /// was carrying, which is exactly the authored quantity this overhaul exists to derive.</para>
        ///
        /// <para>THERE IS NO UPPER BOUND AT ALL NOW, because the wrap it was guarding has been
        /// fixed at its source. Phase 7 initially kept a ceiling of <c>short.MaxValue / 2</c>: the
        /// wrap was real and reproducible, but it lived in
        /// <c>BattleSquadPlacer.PlaceSquadHorizontally/Vertically</c>, which narrowed every cell
        /// coordinate through <c>(short)</c> before widening it straight back into the
        /// <c>ValueTuple&lt;int, int&gt;</c> the grid actually stores. A 65,535-yard standoff landed
        /// the west leg near x = -65,543, truncated it to a cell beside the origin, and threw on the
        /// collision with the column it was standing off from. Those casts are gone;
        /// <c>BattleGridManager</c> keys cells with a sparse
        /// <c>Dictionary&lt;(int X, int Y), int&gt;</c> and has no bounded extent, so any
        /// <see cref="ushort"/> standoff now places correctly.</para>
        ///
        /// <para>Fixing the placer rather than bounding its callers is what let this revert to the
        /// pre-2026-07-08 form. <see cref="AnnihilationPlacer"/> shared the same exposure with no
        /// guard of its own and is fixed by the same change.
        /// <c>PlaceSquads_HugeEngagementRange_DoesNotWrapCoordinates</c> still pins the behaviour at
        /// <see cref="ushort.MaxValue"/>, and now exercises the real placement path rather than a
        /// ceiling that kept it away from the bug.</para>
        /// </summary>
        private int FormationStandoff() =>
            Math.Max((int)_engagementRange, _minimumStandoff);
    }
}
