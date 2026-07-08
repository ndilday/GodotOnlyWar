using OnlyWar.Models;
﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Battles.Placers
{
    public static class BattleSquadPlacer
    {
        public delegate void SquadPlacedHandler(BattleSquad squad, Tuple<int, int> position);
        public static event SquadPlacedHandler OnSquadPlaced;

        public static void PlaceBattleSquad(BattleGridManager manager, BattleSquad squad, Tuple<int, int> bottomLeft, bool longHorizontal)
        {
            PlaceBattleSquad(manager, squad, bottomLeft, longHorizontal, squad.IsPlayerSquad);
        }

        public static void PlaceBattleSquad(BattleGridManager manager, BattleSquad squad, Tuple<int, int> bottomLeft,
                                           bool longHorizontal, bool tacticalSide)
        {
            PlaceBattleSquad(manager, squad, bottomLeft, longHorizontal, tacticalSide, squad.IsPlayerSquad);
        }

        public static void PlaceBattleSquad(BattleGridManager manager, BattleSquad squad, Tuple<int, int> bottomLeft,
                                           bool longHorizontal, bool tacticalSide, bool formationSide)
        {
            // if any squad member is already on the map, we have a problem
            //if (squad.Soldiers.Any(s => _soldierLocationsMap.ContainsKey(s.Soldier.Id))) throw new InvalidOperationException(squad.Name + " has soldiers already on BattleGrid");
            if (squad.AbleSoldiers.Count == 0) throw new InvalidOperationException("No soldiers in " + squad.Name + " to place");
            Coordinate squadBoxSize = squad.GetSquadBoxSize();
            Tuple<int, int> startingLocation;
            if (longHorizontal)
            {
                startingLocation = PlaceSquadHorizontally(manager, squad, bottomLeft, squadBoxSize, formationSide, tacticalSide);
            }
            else
            {
                startingLocation = PlaceSquadVertically(manager, squad, bottomLeft, squadBoxSize, formationSide, tacticalSide);
            }
            OnSquadPlaced?.Invoke(squad, startingLocation);
        }

        private static Tuple<int, int> PlaceSquadHorizontally(BattleGridManager manager, BattleSquad squad,
                                                              Tuple<int, int> bottomLeft, Coordinate squadBoxSize,
                                                              bool formationSide, bool tacticalSide)
        {
            Tuple<int, int> startingLocation = new Tuple<int, int>((short)(bottomLeft.Item1 + ((squadBoxSize.X - 1) / 2)),
                                                                           (short)(bottomLeft.Item2 + squadBoxSize.Y - 1));
            int cellWidth = squad.AbleSoldiers.Max(s => s.Soldier.Template.Species.Width);
            int cellDepth = squad.AbleSoldiers.Max(s => s.Soldier.Template.Species.Depth);
            for (int i = 0; i < squad.AbleSoldiers.Count; i++)
            {
                ushort width = squad.AbleSoldiers[i].Soldier.Template.Species.Width;
                ushort depth = squad.AbleSoldiers[i].Soldier.Template.Species.Depth;
                int membersPerRow = Math.Max(1, squadBoxSize.X / cellWidth);
                int rowIndex = i / membersPerRow;
                int columnIndex = i % membersPerRow;
                int yMod = rowIndex * cellDepth * (formationSide ? -1 : 1);
                int xMod = (columnIndex * cellWidth) - ((squadBoxSize.X - cellWidth) / 2)
                           + ((cellWidth - width) / 2);

                List<Tuple<int, int>> soldierLocations = [];
                for (int w = 0; w < width; w++)
                {
                    for (int d = 0; d < depth; d++)
                    {
                        Tuple<int, int> location = new Tuple<int, int>((short)(startingLocation.Item1 + xMod + w), (short)(startingLocation.Item2 + yMod + d));
                        soldierLocations.Add(location);
                    }
                }
                manager.PlaceSoldier(squad.AbleSoldiers[i], tacticalSide, soldierLocations);

                squad.AbleSoldiers[i].TopLeft = GetTopLeft(soldierLocations);
                squad.AbleSoldiers[i].Orientation = 0;
            }

            return startingLocation;
        }


        private static Tuple<int, int> PlaceSquadVertically(BattleGridManager manager, BattleSquad squad,
                                                            Tuple<int, int> bottomLeft, Coordinate squadBoxSize,
                                                            bool formationSide, bool tacticalSide)
        {
            Tuple<int, int> startingLocation = new Tuple<int, int>((short)(bottomLeft.Item1 + squadBoxSize.Y - 1),
                                                                   (short)(bottomLeft.Item2 + ((squadBoxSize.X - 1) / 2)));
            int cellWidth = squad.AbleSoldiers.Max(s => s.Soldier.Template.Species.Depth);
            int cellDepth = squad.AbleSoldiers.Max(s => s.Soldier.Template.Species.Width);
            for (int i = 0; i < squad.AbleSoldiers.Count; i++)
            {
                ushort width = squad.AbleSoldiers[i].Soldier.Template.Species.Width;
                ushort depth = squad.AbleSoldiers[i].Soldier.Template.Species.Depth;
                int membersPerColumn = Math.Max(1, squadBoxSize.X / cellDepth);
                int rowIndex = i / membersPerColumn;
                int columnIndex = i % membersPerColumn;
                int xMod = rowIndex * cellWidth * (formationSide ? -1 : 1);
                int yMod = (columnIndex * cellDepth) - ((squadBoxSize.X - cellDepth) / 2)
                           + ((cellDepth - width) / 2);

                List<Tuple<int, int>> soldierLocations = [];
                for (int w = 0; w < depth; w++)
                {
                    for (int d = 0; d < width; d++)
                    {
                        Tuple<int, int> location = new Tuple<int, int>((short)(startingLocation.Item1 + xMod + w), (short)(startingLocation.Item2 + yMod + d));
                        soldierLocations.Add(location);
                    }
                }
                manager.PlaceSoldier(squad.AbleSoldiers[i], tacticalSide, soldierLocations);

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
