using OnlyWar.Models;
﻿using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models.Squads;

namespace OnlyWar.Helpers.Battles.Placers
{
    class AnnihilationPlacer
    {
        // How far behind its side's fighting line an HQ squad deploys. Modest on purpose:
        // deep enough that the enemy's first contact is with the line squads, shallow
        // enough that the HQ stays well inside its own command aura and weapon range.
        private const int HqRearOffset = 10;

        private readonly BattleGridManager _grid;
        private readonly ushort _range;

        public AnnihilationPlacer(BattleGridManager grid, ushort range)
        {
            _grid = grid;
            _range = range;
        }
        public Dictionary<BattleSquad, Tuple<int, int>> PlaceSquads(IEnumerable<BattleSquad> bottomSquads,
                                                            IEnumerable<BattleSquad> topSquads)
        {
            Dictionary<BattleSquad, Tuple<int, int>> result = [];
            (List<BattleSquad> bottomLine, List<BattleSquad> bottomHq) = SplitOutHqSquads(bottomSquads);
            (List<BattleSquad> topLine, List<BattleSquad> topHq) = SplitOutHqSquads(topSquads);
            int bottomDepth = bottomLine.Select(s => (int)s.GetSquadBoxSize().Y).DefaultIfEmpty(0).Max();
            int topDepth = topLine.Select(s => (int)s.GetSquadBoxSize().Y).DefaultIfEmpty(0).Max();
            ushort effectiveRange = (ushort)Math.Max(_range, bottomDepth + topDepth + 1);

            (int Left, int Right) bottomExtent = PlaceSquads(bottomLine, 0, false, true, result);
            (int Left, int Right) topExtent = PlaceSquads(topLine, effectiveRange, true, false, result);
            PlaceHqRank(bottomHq, bottomExtent, 0, false, true, result);
            PlaceHqRank(topHq, topExtent, effectiveRange, true, false, result);

            return result;
        }

        // An HQ deploys behind the fighting line, not in it. A force with no line squads at
        // all keeps its HQs in the line — someone has to stand somewhere.
        private static (List<BattleSquad> Line, List<BattleSquad> Hq) SplitOutHqSquads(
            IEnumerable<BattleSquad> squads)
        {
            List<BattleSquad> line = [];
            List<BattleSquad> hq = [];
            foreach (BattleSquad squad in squads)
            {
                if (squad.Squad?.SquadTemplate?.SquadType.HasFlag(SquadTypes.HQ) == true)
                {
                    hq.Add(squad);
                }
                else
                {
                    line.Add(squad);
                }
            }
            if (line.Count == 0)
            {
                return (hq, []);
            }
            return (line, hq);
        }

        private (int Left, int Right) PlaceSquads(IReadOnlyList<BattleSquad> squads, int facingEdge, bool isTop, bool tacticalSide,
                                                              Dictionary<BattleSquad, Tuple<int, int>> squadPositionMap)
        {
            const int squadSpacing = 2;
            int totalFrontage = squads.Sum(squad => (int)squad.GetSquadBoxSize().X)
                + Math.Max(0, squads.Count - 1) * squadSpacing;
            int left = -(totalFrontage / 2);
            int lineLeft = left;

            foreach (BattleSquad squad in squads)
            {
                Coordinate squadSize = squad.GetSquadBoxSize();
                int bottom = isTop ? facingEdge - squadSize.Y + 1 : facingEdge;
                Tuple<int, int> position = new(left, bottom);
                squadPositionMap[squad] = position;
                BattleSquadPlacer.PlaceBattleSquad(_grid, squad, position, true, tacticalSide, isTop);
                left += squadSize.X + squadSpacing;
            }
            return (lineLeft, Math.Max(lineLeft, left - squadSpacing));
        }

        // Distribute the HQ squads evenly along the line's frontage, one rear offset behind
        // the fighting line: a single HQ sits behind the line's center, two split it into
        // thirds, and so on — larger forces get command presence down the whole line rather
        // than one cluster in the middle.
        private void PlaceHqRank(IReadOnlyList<BattleSquad> hqSquads,
                                 (int Left, int Right) lineExtent, int facingEdge, bool isTop, bool tacticalSide,
                                 Dictionary<BattleSquad, Tuple<int, int>> squadPositionMap)
        {
            if (hqSquads.Count == 0)
            {
                return;
            }

            int span = Math.Max(0, lineExtent.Right - lineExtent.Left);
            for (int i = 0; i < hqSquads.Count; i++)
            {
                BattleSquad squad = hqSquads[i];
                Coordinate squadSize = squad.GetSquadBoxSize();
                int centerX = lineExtent.Left + span * (i + 1) / (hqSquads.Count + 1);
                int left = centerX - squadSize.X / 2;
                int bottom = isTop
                    ? facingEdge + HqRearOffset + 1
                    : facingEdge - HqRearOffset - squadSize.Y;
                Tuple<int, int> position = new(left, bottom);
                squadPositionMap[squad] = position;
                BattleSquadPlacer.PlaceBattleSquad(_grid, squad, position, true, tacticalSide, isTop);
            }
        }
    }
}
