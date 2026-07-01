using OnlyWar.Models;
﻿using System;
using System.Collections.Generic;

namespace OnlyWar.Helpers.Battles.Placers
{
    public static class BattleSquadPlacer
    {
        public delegate void SquadPlacedHandler(BattleSquad squad, Tuple<int, int> position);
        public static event SquadPlacedHandler OnSquadPlaced;

        public static void PlaceBattleSquad(BattleGridManager manager, BattleSquad squad, Tuple<int, int> bottomLeft, bool longHorizontal)
        {
            // if any squad member is already on the map, we have a problem
            //if (squad.Soldiers.Any(s => _soldierLocationsMap.ContainsKey(s.Soldier.Id))) throw new InvalidOperationException(squad.Name + " has soldiers already on BattleGrid");
            if (squad.AbleSoldiers.Count == 0) throw new InvalidOperationException("No soldiers in " + squad.Name + " to place");
            Coordinate squadBoxSize = squad.GetSquadBoxSize();
            Tuple<int, int> startingLocation;
            if (longHorizontal)
            {
                startingLocation = PlaceSquadHorizontally(manager, squad, bottomLeft, squadBoxSize);
            }
            else
            {
                startingLocation = PlaceSquadVertically(manager, squad, bottomLeft, squadBoxSize);
            }
            OnSquadPlaced?.Invoke(squad, startingLocation);
        }

        private static Tuple<int, int> PlaceSquadHorizontally(BattleGridManager manager, BattleSquad squad, Tuple<int, int> bottomLeft, Coordinate squadBoxSize)
        {
            Tuple<int, int> startingLocation = new Tuple<int, int>((short)(bottomLeft.Item1 + ((squadBoxSize.X - 1) / 2)),
                                                                           (short)(bottomLeft.Item2 + squadBoxSize.Y - 1));
            for (int i = 0; i < squad.AbleSoldiers.Count; i++)
            {
                ushort width = squad.AbleSoldiers[i].Soldier.Template.Species.Width;
                ushort depth = squad.AbleSoldiers[i].Soldier.Template.Species.Depth;
                // 0th soldier goes in the coordinate given, then alternate to each side up to membersPerRow, then repeat in additional rows as necessary
                int rowIndex = (i * width) / squadBoxSize.X;
                int yMod = rowIndex * depth * (squad.IsPlayerSquad ? -1 : 1);
                int xMod = (((i * width) % squadBoxSize.X) + 1) / 2 * (i % 2 == 0 ? -1 : 1);

                List<Tuple<int, int>> soldierLocations = [];
                for (int w = 0; w < width; w++)
                {
                    for (int d = 0; d < depth; d++)
                    {
                        Tuple<int, int> location = new Tuple<int, int>((short)(startingLocation.Item1 + xMod + w), (short)(startingLocation.Item2 + yMod + d));
                        soldierLocations.Add(location);
                    }
                }
                manager.PlaceSoldier(squad.AbleSoldiers[i], squad.IsPlayerSquad, soldierLocations);

                squad.AbleSoldiers[i].TopLeft = GetTopLeft(soldierLocations);
                squad.AbleSoldiers[i].Orientation = 0;
            }

            return startingLocation;
        }


        private static Tuple<int, int> PlaceSquadVertically(BattleGridManager manager, BattleSquad squad, Tuple<int, int> bottomLeft, Coordinate squadBoxSize)
        {
            Tuple<int, int> startingLocation = new Tuple<int, int>((short)(bottomLeft.Item1 + squadBoxSize.Y - 1),
                                                                   (short)(bottomLeft.Item2 + ((squadBoxSize.X - 1) / 2)));
            ushort width = squad.AbleSoldiers[0].Soldier.Template.Species.Width;
            ushort depth = squad.AbleSoldiers[0].Soldier.Template.Species.Depth;
            for (int i = 0; i < squad.AbleSoldiers.Count; i++)
            {
                // 0th soldier goes in the coordinate given, then alternate to each side up to membersPerRow, then repeat in additional rows as necessary
                int rowIndex = (i * width) / squadBoxSize.X;
                int xMod = rowIndex * depth * (squad.IsPlayerSquad ? -1 : 1);
                int yMod = (((i * width) % squadBoxSize.X) + 1) / 2 * (i % 2 == 0 ? -1 : 1);

                List<Tuple<int, int>> soldierLocations = [];
                for (int w = 0; w < depth; w++)
                {
                    for (int d = 0; d < width; d++)
                    {
                        Tuple<int, int> location = new Tuple<int, int>((short)(startingLocation.Item1 + xMod + w), (short)(startingLocation.Item2 + yMod + d));
                        soldierLocations.Add(location);
                    }
                }
                manager.PlaceSoldier(squad.AbleSoldiers[i], squad.IsPlayerSquad, soldierLocations);

                squad.AbleSoldiers[i].TopLeft = GetTopLeft(soldierLocations);
                squad.AbleSoldiers[i].Orientation = 1;
            }

            return startingLocation;
        }

        private static Tuple<int, int> GetTopLeft(List<Tuple<int, int>> tupleList)
        {
            Tuple<int, int> topLeft = null;
            foreach (Tuple<int, int> tuple in tupleList)
            {
                if (topLeft == null || (tuple.Item1 <= topLeft.Item1 && tuple.Item2 >= topLeft.Item2))
                {
                    topLeft = tuple;
                }
            }

            return topLeft;
        }
    }

}
