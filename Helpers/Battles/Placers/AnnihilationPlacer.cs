using System;
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

            PlaceSquads(bottomSquads, new Tuple<ushort, ushort>(0, 0), false, result);
            PlaceSquads(topSquads, new Tuple<ushort, ushort>(0, _range), true, result);

            
            return result;
        }

        private void PlaceSquads(IEnumerable<BattleSquad> squads, Tuple<ushort, ushort> startingPoint, bool isTop,
                                                              Dictionary<BattleSquad, Tuple<int, int>> squadPositionMap)
        {
            // start with the left, then place to whichever side is less far from the starting point
            ushort verticalLimit = startingPoint.Item2;
            ushort leftLimit = startingPoint.Item1;
            ushort rightLimit = startingPoint.Item1;
            
            foreach (BattleSquad squad in squads)
            {
                Tuple<ushort, ushort> squadSize = squad.GetSquadBoxSize();
                if (rightLimit - startingPoint.Item1 > startingPoint.Item1 - leftLimit)
                {
                    leftLimit -= squadSize.Item1;
                    leftLimit -= 1;
                    // if isTop, then we are placing the squad above the starting point
                    if (isTop)
                    {
                        BattleSquadPlacer.PlaceBattleSquad(_grid, squad, new Tuple<int, int>(leftLimit, verticalLimit), true);
                    }
                    else
                    {
                        ushort bottom = (ushort)(verticalLimit - squadSize.Item2);
                        BattleSquadPlacer.PlaceBattleSquad(_grid, squad, new Tuple<int, int>(leftLimit, bottom), true);

                    }
                }
                else
                {
                    rightLimit += squadSize.Item1;
                    rightLimit += 1;
                    if (isTop)
                    {
                        BattleSquadPlacer.PlaceBattleSquad(_grid, squad, new Tuple<int, int>(rightLimit, verticalLimit), true);
                    }
                    else
                    {
                        ushort bottom = (ushort)(verticalLimit - squadSize.Item2);
                        BattleSquadPlacer.PlaceBattleSquad(_grid, squad, new Tuple<int, int>(rightLimit, bottom), true);
                    }
                }
            }
        }
    }
}
