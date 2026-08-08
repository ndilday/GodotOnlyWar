using OnlyWar.Models;
﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Battles.Placers
{
    public static class BattleSquadPlacer
    {
        public delegate void SquadPlacedHandler(BattleSquad squad, ValueTuple<int, int> position);
        public static event SquadPlacedHandler OnSquadPlaced;

        public static void PlaceBattleSquad(BattleGridManager manager, BattleSquad squad, ValueTuple<int, int> bottomLeft,
                                           bool longHorizontal, bool tacticalSide, bool formationSide)
        {
            // if any squad member is already on the map, we have a problem
            //if (squad.Soldiers.Any(s => _soldierLocationsMap.ContainsKey(s.Soldier.Id))) throw new InvalidOperationException(squad.Name + " has soldiers already on BattleGrid");
            SquadFormationGeometry geometry = SquadFormationGeometry.For(squad);
            ValueTuple<int, int> startingLocation;
            if (longHorizontal)
            {
                startingLocation = PlaceSquadHorizontally(manager, squad, bottomLeft, geometry, formationSide, tacticalSide);
            }
            else
            {
                startingLocation = PlaceSquadVertically(manager, squad, bottomLeft, geometry, formationSide, tacticalSide);
            }
            OnSquadPlaced?.Invoke(squad, startingLocation);
        }

        private static ValueTuple<int, int> PlaceSquadHorizontally(BattleGridManager manager, BattleSquad squad,
                                                              ValueTuple<int, int> bottomLeft, SquadFormationGeometry geometry,
                                                              bool formationSide, bool tacticalSide)
        {
            Coordinate squadBoxSize = geometry.Bounds;
            // Cell coordinates are int, all the way down: BattleGridManager keys its cells with a
            // sparse Dictionary<(int X, int Y), int> and stores every position as ValueTuple<int, int>.
            // These expressions used to be narrowed through (short) before being widened straight back
            // into that int tuple, which did nothing except silently wrap any layout wider than 32,767
            // -- a large standoff would land a leg at x = -65,543, truncate to a cell near the origin,
            // and throw on the collision with the force it was meant to be standing off from. See
            // Design/Reference/EngagementScoringOverhaul.md; AmbushPlacer carried a ceiling to dodge this
            // and no longer needs one.
            ValueTuple<int, int> startingLocation = new ValueTuple<int, int>(bottomLeft.Item1 + ((squadBoxSize.X - 1) / 2),
                                                                           bottomLeft.Item2 + squadBoxSize.Y - 1);
            int emptyRearRankSlots = geometry.RankCount * geometry.MembersPerRank
                - squad.AbleSoldiers.Count;
            for (int i = 0; i < squad.AbleSoldiers.Count; i++)
            {
                ushort width = squad.AbleSoldiers[i].Soldier.Template.Species.Width;
                ushort depth = squad.AbleSoldiers[i].Soldier.Template.Species.Depth;
                int formationIndex = i + emptyRearRankSlots;
                int rowIndex = formationIndex / geometry.MembersPerRank;
                int columnIndex = formationIndex % geometry.MembersPerRank;
                int yMod = rowIndex * geometry.CellDepth * (formationSide ? -1 : 1);
                int xMod = (columnIndex * geometry.LateralPitch)
                           + (rowIndex % 2)
                           + ((geometry.CellWidth - width) / 2)
                           - ((squadBoxSize.X - 1) / 2);

                List<ValueTuple<int, int>> soldierLocations = [];
                for (int w = 0; w < width; w++)
                {
                    for (int d = 0; d < depth; d++)
                    {
                        ValueTuple<int, int> location = new ValueTuple<int, int>(startingLocation.Item1 + xMod + w, startingLocation.Item2 + yMod + d);
                        soldierLocations.Add(location);
                    }
                }
                manager.PlaceSoldier(squad.AbleSoldiers[i], tacticalSide, soldierLocations);

                squad.AbleSoldiers[i].TopLeft = GetTopLeft(soldierLocations);
                squad.AbleSoldiers[i].Orientation = 0;
            }

            return startingLocation;
        }


        private static ValueTuple<int, int> PlaceSquadVertically(BattleGridManager manager, BattleSquad squad,
                                                            ValueTuple<int, int> bottomLeft, SquadFormationGeometry geometry,
                                                            bool formationSide, bool tacticalSide)
        {
            Coordinate squadBoxSize = geometry.Bounds;
            // int throughout -- see PlaceSquadHorizontally for why there is no narrowing here.
            ValueTuple<int, int> startingLocation = new ValueTuple<int, int>(bottomLeft.Item1 + squadBoxSize.Y - 1,
                                                                   bottomLeft.Item2 + ((squadBoxSize.X - 1) / 2));
            int emptyRearRankSlots = geometry.RankCount * geometry.MembersPerRank
                - squad.AbleSoldiers.Count;
            for (int i = 0; i < squad.AbleSoldiers.Count; i++)
            {
                ushort width = squad.AbleSoldiers[i].Soldier.Template.Species.Width;
                ushort depth = squad.AbleSoldiers[i].Soldier.Template.Species.Depth;
                int formationIndex = i + emptyRearRankSlots;
                int rowIndex = formationIndex / geometry.MembersPerRank;
                int columnIndex = formationIndex % geometry.MembersPerRank;
                int xMod = rowIndex * geometry.CellDepth * (formationSide ? -1 : 1);
                int yMod = (columnIndex * geometry.LateralPitch)
                           + (rowIndex % 2)
                           + ((geometry.CellWidth - width) / 2)
                           - ((squadBoxSize.X - 1) / 2);

                List<ValueTuple<int, int>> soldierLocations = [];
                for (int w = 0; w < depth; w++)
                {
                    for (int d = 0; d < width; d++)
                    {
                        ValueTuple<int, int> location = new ValueTuple<int, int>(startingLocation.Item1 + xMod + w, startingLocation.Item2 + yMod + d);
                        soldierLocations.Add(location);
                    }
                }
                manager.PlaceSoldier(squad.AbleSoldiers[i], tacticalSide, soldierLocations);

                squad.AbleSoldiers[i].TopLeft = GetTopLeft(soldierLocations);
                squad.AbleSoldiers[i].Orientation = 2;
            }

            return startingLocation;
        }

        private static ValueTuple<int, int> GetTopLeft(List<ValueTuple<int, int>> tupleList)
        {
            ValueTuple<int, int>? topLeft = tupleList[0];
            foreach (ValueTuple<int, int> tuple in tupleList)
            {
                if (tuple.Item1 <= topLeft?.Item1 && tuple.Item2 >= topLeft?.Item2)
                {
                    topLeft = tuple;
                }
            }

            return topLeft.Value;
        }
    }

}
