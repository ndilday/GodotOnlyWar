using OnlyWar.Models;
﻿using System;
using System.Collections.Generic;

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

            PlaceSquads(bottomSquads, new Coordinate(0, 0), false, result);
            PlaceSquads(topSquads, new Coordinate(0, _range), true, result);

            
            return result;
        }

        private void PlaceSquads(IEnumerable<BattleSquad> squads, Coordinate startingPoint, bool isTop,
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
                        BattleSquadPlacer.PlaceBattleSquad(_grid, squad, new Tuple<int, int>(leftLimit, verticalLimit), true);
                    }
                    else
                    {
                        ushort bottom = (ushort)(verticalLimit - squadSize.Y);
                        BattleSquadPlacer.PlaceBattleSquad(_grid, squad, new Tuple<int, int>(leftLimit, bottom), true);

                    }
                }
                else
                {
                    rightLimit += squadSize.X;
                    rightLimit += 1;
                    if (isTop)
                    {
                        BattleSquadPlacer.PlaceBattleSquad(_grid, squad, new Tuple<int, int>(rightLimit, verticalLimit), true);
                    }
                    else
                    {
                        ushort bottom = (ushort)(verticalLimit - squadSize.Y);
                        BattleSquadPlacer.PlaceBattleSquad(_grid, squad, new Tuple<int, int>(rightLimit, bottom), true);
                    }
                }
            }
        }
    }
}
