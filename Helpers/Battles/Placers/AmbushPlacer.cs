using System;
using System.Collections.Generic;


namespace OnlyWar.Helpers.Battles.Placers
{
    public class AmbushPlacer
    {
        private readonly BattleGrid _grid;

        public AmbushPlacer(BattleGrid grid)
        {
            _grid = grid;
        }

        public Dictionary<BattleSquad, Tuple<int, int>> PlaceSquads(IReadOnlyList<BattleSquad> ambushedSquads,
                                                            IReadOnlyList<BattleSquad> ambushingSquads)
        {
            Dictionary<BattleSquad, Tuple<int, int>> result = new Dictionary<BattleSquad, Tuple<int, int>>();
            // TODO: prevent crippled soldiers from being deployed
            var killZone = PlaceAmbushedSquads(ambushedSquads, 0, 0, result);
            PlaceAmbushingSquads(ambushingSquads, killZone, result);
            return result;
        }

        private Tuple<Tuple<int, int>, Tuple<int, int>> PlaceAmbushedSquads(IEnumerable<BattleSquad> squads, ushort xMid, ushort top,
                                                              Dictionary<BattleSquad, Tuple<int, int>> squadPositionMap)
        {
            // assume a depth of four yards per squad
            // center them all on the midpoint, one behind the other
            ushort topLimit = top;
            ushort bottomLimit = top;
            ushort leftLimit = xMid;
            ushort rightLimit = xMid;
            foreach (BattleSquad squad in squads)
            {
                Tuple<ushort, ushort> squadSize = squad.GetSquadBoxSize();
                ushort left = (ushort)(xMid - squadSize.Item1 / 2);
                ushort right = (ushort)(xMid + squadSize.Item1 - squadSize.Item1 / 2);
                bottomLimit = (ushort)(top - squadSize.Item2);
                squadPositionMap[squad] = new Tuple<int, int>(left, bottomLimit);
                _grid.PlaceBattleSquad(squad, new Tuple<int, int>(left, bottomLimit), true);

                top -= (ushort)(squadSize.Item2 + 1);

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
        private void PlaceAmbushingSquads(IReadOnlyList<BattleSquad> ambushingSquads, 
                                             Tuple<Tuple<int, int>, Tuple<int, int>> killZone, 
                                             Dictionary<BattleSquad, Tuple<int, int>> squadPositionMap)
        {
            // ambushing forces start 40 yards from the target to the top and left
            int currentY = killZone.Item1.Item2;
            int currentX = killZone.Item1.Item1 - 40;
            int bottomLimit = killZone.Item2.Item2 - 50;
            int rightLimit = killZone.Item2.Item1;
            bool onLeft = true;
            int iteration = 0;
            foreach (BattleSquad squad in ambushingSquads)
            {
                Tuple<ushort, ushort> squadSize = squad.GetSquadBoxSize();
                if (onLeft)
                {
                    // start at top left of killzone, fill downward
                    currentY -= squadSize.Item1;
                    int left = currentX - squadSize.Item2;
                    _grid.PlaceBattleSquad(squad, new Tuple<int, int>(left, currentY), false);
                    if(currentY <= bottomLimit)
                    {
                        onLeft = false;
                        currentY = killZone.Item1.Item2 + 40 + (iteration * 4);
                        currentX = killZone.Item1.Item1;
                    }
                }
                else
                {
                    // start at top left of killzone, fill right
                    squadPositionMap[squad] = new Tuple<int, int>(currentX, currentY);
                    _grid.PlaceBattleSquad(squad, new Tuple<int, int>(currentX, currentY), true);
                    currentX += squadSize.Item1;
                    if(currentX >= rightLimit)
                    {
                        onLeft = true;
                        iteration++;
                        currentX = killZone.Item1.Item1 - 40 - (iteration * 4);
                        currentY = killZone.Item1.Item2;
                    }
                }
            }
        }
    }
}
