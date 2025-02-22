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

            var bottomZone = PlaceBottomSquads(bottomSquads, new Tuple<ushort, ushort>(0, 0), result);
            PlaceTopSquads(topSquads, bottomZone, result);

            
            return result;
        }

        private Tuple<Tuple<int, int>, Tuple<int, int>> PlaceBottomSquads(IEnumerable<BattleSquad> squads, Tuple<ushort, ushort> startingPoint,
                                                              Dictionary<BattleSquad, Tuple<int, int>> squadPositionMap)
        {
            // assume a depth of four yards per squad
            // center them all on the midpoint, one behind the other
            ushort topLimit = startingPoint.Item2;
            ushort bottomLimit = startingPoint.Item2;
            ushort leftLimit = startingPoint.Item1;
            ushort rightLimit = startingPoint.Item1;
            foreach (BattleSquad squad in squads)
            {
                Tuple<ushort, ushort> squadSize = squad.GetSquadBoxSize();
                ushort left = (ushort)(startingPoint.Item1 - squadSize.Item1 / 2);
                ushort right = (ushort)(startingPoint.Item1 + squadSize.Item1 - squadSize.Item1 / 2);
                bottomLimit = (ushort)(startingPoint.Item2 - squadSize.Item2);
                squadPositionMap[squad] = new Tuple<int, int>(left, bottomLimit);
                BattleSquadPlacer.PlaceBattleSquad(_grid, squad, new Tuple<int, int>(left, bottomLimit), true);

                topLimit -= (ushort)(squadSize.Item2 + 1);

                if (left < leftLimit)
                {
                    leftLimit = left;
                }
                if (right > rightLimit)
                {
                    rightLimit = right;
                }
            }

            return new Tuple<Tuple<int, int>, Tuple<int, int>>(new Tuple<int, int>(leftLimit, topLimit), new Tuple<int, int>(rightLimit, bottomLimit));
        }

        private void PlaceTopSquads(IEnumerable<BattleSquad> squads,
                                       Tuple<Tuple<int, int>, Tuple<int, int>> bottomForceCorners,
                                       Dictionary<BattleSquad, Tuple<int, int>> squadPositionMap)
        {
            // assume a depth of four yards per squad
            // center them all on the midpoint, one behind the other
            int currentY = bottomForceCorners.Item1.Item2;
            int currentX = bottomForceCorners.Item1.Item1 - _range;
            int bottomLimit = bottomForceCorners.Item2.Item2 - _range;
            int rightLimit = bottomForceCorners.Item2.Item1;
            int iteration = 0;
            foreach (BattleSquad squad in squads)
            {
                Tuple<ushort, ushort> squadSize = squad.GetSquadBoxSize();
                // start at top left of killzone, fill right
                squadPositionMap[squad] = new Tuple<int, int>(currentX, currentY);
                BattleSquadPlacer.PlaceBattleSquad(_grid, squad, new Tuple<int, int>(currentX, currentY), true);
                currentX += squadSize.Item1;
                if (currentX >= rightLimit)
                {
                    iteration++;
                    currentX = bottomForceCorners.Item1.Item1 - _range - (iteration * 4);
                    currentY = bottomForceCorners.Item1.Item2;
                }
            }
        }
    }
}
