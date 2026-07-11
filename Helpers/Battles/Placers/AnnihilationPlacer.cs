using OnlyWar.Models;
﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Battles.Placers
{
    class AnnihilationPlacer
    {
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
            List<BattleSquad> bottom = bottomSquads.ToList();
            List<BattleSquad> top = topSquads.ToList();
            int bottomDepth = bottom.Select(s => (int)s.GetSquadBoxSize().Y).DefaultIfEmpty(0).Max();
            int topDepth = top.Select(s => (int)s.GetSquadBoxSize().Y).DefaultIfEmpty(0).Max();
            ushort effectiveRange = (ushort)Math.Max(_range, bottomDepth + topDepth + 1);

            PlaceSquads(bottom, 0, false, true, result);
            PlaceSquads(top, effectiveRange, true, false, result);

            
            return result;
        }

        private void PlaceSquads(IReadOnlyList<BattleSquad> squads, int facingEdge, bool isTop, bool tacticalSide,
                                                              Dictionary<BattleSquad, Tuple<int, int>> squadPositionMap)
        {
            const int squadSpacing = 2;
            int totalFrontage = squads.Sum(squad => (int)squad.GetSquadBoxSize().X)
                + Math.Max(0, squads.Count - 1) * squadSpacing;
            int left = -(totalFrontage / 2);

            foreach (BattleSquad squad in squads)
            {
                Coordinate squadSize = squad.GetSquadBoxSize();
                int bottom = isTop ? facingEdge - squadSize.Y + 1 : facingEdge;
                Tuple<int, int> position = new(left, bottom);
                squadPositionMap[squad] = position;
                BattleSquadPlacer.PlaceBattleSquad(_grid, squad, position, true, tacticalSide, isTop);
                left += squadSize.X + squadSpacing;
            }
        }
    }
}
