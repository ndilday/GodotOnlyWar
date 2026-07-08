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

            PlaceSquads(bottom, new Coordinate(0, 0), false, true, result);
            PlaceSquads(top, new Coordinate(0, effectiveRange), true, false, result);

            
            return result;
        }

        private void PlaceSquads(IEnumerable<BattleSquad> squads, Coordinate startingPoint, bool isTop, bool tacticalSide,
                                                              Dictionary<BattleSquad, Tuple<int, int>> squadPositionMap)
        {
            // start with the left, then place to whichever side is less far from the starting point
            ushort verticalLimit = startingPoint.Y;
            ushort leftLimit = startingPoint.X;
            ushort rightLimit = startingPoint.X;
            
            foreach (BattleSquad squad in squads)
            {
                Coordinate squadSize = squad.GetSquadBoxSize();
                if (rightLimit - startingPoint.X > startingPoint.X - leftLimit)
                {
                    leftLimit -= squadSize.X;
                    leftLimit -= 1;
                    // if isTop, then we are placing the squad above the starting point
                    if (isTop)
                    {
                        int bottom = verticalLimit - squadSize.Y + 1;
                        BattleSquadPlacer.PlaceBattleSquad(_grid, squad, new Tuple<int, int>(leftLimit, bottom), true, tacticalSide, true);
                    }
                    else
                    {
                        BattleSquadPlacer.PlaceBattleSquad(_grid, squad, new Tuple<int, int>(leftLimit, verticalLimit), true, tacticalSide, false);

                    }
                }
                else
                {
                    rightLimit += squadSize.X;
                    rightLimit += 1;
                    if (isTop)
                    {
                        int bottom = verticalLimit - squadSize.Y + 1;
                        BattleSquadPlacer.PlaceBattleSquad(_grid, squad, new Tuple<int, int>(rightLimit, bottom), true, tacticalSide, true);
                    }
                    else
                    {
                        BattleSquadPlacer.PlaceBattleSquad(_grid, squad, new Tuple<int, int>(rightLimit, verticalLimit), true, tacticalSide, false);
                    }
                }
            }
        }
    }
}
