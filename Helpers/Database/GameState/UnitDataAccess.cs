﻿using OnlyWar.Models.Equippables;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace OnlyWar.Helpers.Database.GameState
{
    public class UnitDataAccess
    {
        public Dictionary<int, List<Squad>> GetSquadsByUnitId(IDbConnection connection,
                                                               IReadOnlyDictionary<int, SquadTemplate> squadTemplateMap,
                                                               IReadOnlyDictionary<int, List<WeaponSet>> squadWeaponSetMap,
                                                               IReadOnlyDictionary<int, Ship> shipMap,
                                                               IReadOnlyDictionary<int, Region> regionMap)
        {
            Dictionary<int, List<Squad>> squadMap = [];
            Dictionary<int, Squad> squadByIdMap = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM Squad";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    int squadTemplateId = reader.GetInt32(1);
                    int parentUnitId = reader.GetInt32(2);
                    string name = reader[3].ToString();
                    bool isInReserve = reader.GetBoolean(6);

                    SquadTemplate template = squadTemplateMap[squadTemplateId];

                    Squad squad = new Squad(id, name, null, template);
                    squadByIdMap[id] = squad;


                    if (reader[4].GetType() != typeof(DBNull))
                    {
                        squad.BoardedLocation = shipMap[reader.GetInt32(4)];
                    }

                    if (reader[5].GetType() != typeof(DBNull))
                    {
                        squad.CurrentRegion = regionMap[reader.GetInt32(5)];
                    }

                    if (squadWeaponSetMap.ContainsKey(id))
                    {
                        squad.Loadout = squadWeaponSetMap[id];
                    }

                    if (!squadMap.ContainsKey(parentUnitId))
                    {
                        squadMap[parentUnitId] = [];
                    }

                    squadMap[parentUnitId].Add(squad);
                }
            }
            PopulateOrdersBySquadId(connection, regionMap, squadByIdMap);

            return squadMap;
        }

        private void PopulateOrdersBySquadId(IDbConnection connection,
                                            IReadOnlyDictionary<int, Region> regionMap,
                                            IReadOnlyDictionary<int, Squad> squadMap)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM SquadOrder";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int orderId = reader.GetInt32(0);
                    int squadId = reader.GetInt32(1);
                    int regionId = reader.GetInt32(2);
                    int disposition = reader.GetInt32(3);
                    bool isQuiet = reader.GetBoolean(4);
                    bool isActivelyEngaging = reader.GetBoolean(5);
                    int aggression = reader.GetInt32(6);
                    int missionType = reader.GetInt32(7);
                    Disposition disp = (Disposition)disposition;
                    Aggression agg = (Aggression)aggression;
                    MissionType mt = (MissionType)missionType;
                    Region region = regionMap[regionId];
                    Squad squad = squadMap[squadId];
                    squad.CurrentOrders = new Order(orderId, squad, region, disp, isQuiet, isActivelyEngaging, agg, mt);
                }
            }
        }

        public List<Unit> GetUnits(IDbConnection connection,
                                   IReadOnlyDictionary<int, UnitTemplate> unitTemplateMap,
                                   IReadOnlyDictionary<int, List<Squad>> unitSquadMap)
        {
            List<Unit> unitList = [];
            Dictionary<int, Unit> unitMap = [];
            Dictionary<int, List<Unit>> parentUnitMap = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM Unit";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    int unitTemplateId = reader.GetInt32(2);
                    string name = reader[4].ToString();

                    Squad hqSquad = null;
                    int parentUnitId;

                    List<Squad> squadList = null;
                    if (unitSquadMap.ContainsKey(id))
                    {
                        squadList = unitSquadMap[id];
                    }

                    Unit unit = new Unit(id, name, unitTemplateMap[unitTemplateId], squadList);
                    if (hqSquad != null)
                    {
                        hqSquad.ParentUnit = unit;
                    }
                    foreach (Squad squad in squadList)
                    {
                        squad.ParentUnit = unit;
                    }

                    unitMap[id] = unit;
                    unitList.Add(unit);

                    if (reader[3].GetType() != typeof(DBNull))
                    {
                        parentUnitId = reader.GetInt32(3);
                        if (!parentUnitMap.ContainsKey(parentUnitId))
                        {
                            parentUnitMap[parentUnitId] = [];
                        }
                        parentUnitMap[parentUnitId].Add(unit);
                    }
                }
            }

            foreach (KeyValuePair<int, List<Unit>> kvp in parentUnitMap)
            {
                unitMap[kvp.Key].ChildUnits = kvp.Value;
                foreach(Unit unit in kvp.Value)
                {
                    unit.ParentUnit = unitMap[kvp.Key];
                }
            }

            return unitList.Where(u => u.ParentUnit == null).ToList();
        }

        public Dictionary<int, List<WeaponSet>> GetSquadWeaponSets(IDbConnection connection, 
                                                                   IReadOnlyDictionary<int, WeaponSet> weaponSets)
        {
            Dictionary<int, List<WeaponSet>> squadWeaponSetMap = 
                [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM SquadWeaponSet";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int squadId = reader.GetInt32(0);
                    int weaponSetId = reader.GetInt32(1);

                    WeaponSet weaponSet = weaponSets[weaponSetId];

                    if (!squadWeaponSetMap.ContainsKey(squadId))
                    {
                        squadWeaponSetMap[squadId] = [];
                    }
                    squadWeaponSetMap[squadId].Add(weaponSet);
                }
            }
            return squadWeaponSetMap;
        }

        public void SaveUnit(IDbTransaction transaction, Unit unit)
        {
            string parent = unit.ParentUnit == null ? "null" : unit.ParentUnit.Id.ToString();
            string insert = $@"INSERT INTO Unit VALUES ({unit.Id}, {unit.UnitTemplate.Faction.Id}, 
                {unit.UnitTemplate.Id}, {parent}, '{unit.Name}');";
            using (var command = transaction.Connection.CreateCommand())
            {
                command.CommandText = insert;
                command.ExecuteNonQuery();
            }
        }

        public void SaveSquad(IDbTransaction transaction, Squad squad)
        {
            string safeName = squad.Name.Replace("\'", "\'\'");
            string ship = squad.BoardedLocation == null ? "null" : squad.BoardedLocation.Id.ToString();
            string region = squad.CurrentRegion == null ? "null" : squad.CurrentRegion.Id.ToString();
            string insert = $@"INSERT INTO Squad VALUES ({squad.Id}, {squad.SquadTemplate.Id}, 
                {squad.ParentUnit.Id}, '{safeName}', {ship}, {region});";
            using (var command = transaction.Connection.CreateCommand())
            {
                command.CommandText = insert;
                command.ExecuteNonQuery();
            }

            if(squad.Loadout != null && squad.Loadout.Count > 0)
            {
                SaveSquadLoadout(transaction, squad);
            }
            if(squad.CurrentOrders != null)
            {
                SaveSquadOrders(transaction, squad);
            }
        }

        private void SaveSquadLoadout(IDbTransaction transaction, Squad squad)
        {
            foreach(WeaponSet weaponSet in squad.Loadout)
            {
                string insert = $@"INSERT INTO SquadWeaponSet VALUES 
                    ({squad.Id}, {weaponSet.Id});";
                using (var command = transaction.Connection.CreateCommand())
                {
                    command.CommandText = insert;
                    command.ExecuteNonQuery();
                }
            }
        }

        private void SaveSquadOrders(IDbTransaction transaction, Squad squad)
        {
            if(squad.CurrentOrders != null)
            {
                Order order = squad.CurrentOrders;
                // CREATE TABLE SquadOrder (OrderId INTEGER PRIMARY KEY UNIQUE NOT NULL, SquadId INTEGER NOT NULL REFERENCES Squad (Id), RegionId INTEGER NOT NULL REFERENCES Region (Id), Disposition INTEGER NOT NULL, IsQuiet BOOLEAN NOT NULL, IsActivelyEngaging BOOLEAN NOT NULL, Aggression INTEGER NOT NULL, MissionType INTEGER NOT NULL);
                string insert = $@"INSERT INTO SquadOrder VALUES 
                    ({order.Id}, {squad.Id}, {order.TargetRegion.Id}, 
                    {order.Disposition}, {order.IsQuiet}, {order.IsActivelyEngaging}, 
                    {order.LevelOfAggression}, {order.MissionType});";
                using (var command = transaction.Connection.CreateCommand())
                {
                    command.CommandText = insert;
                    command.ExecuteNonQuery();
                }
            }
        }
    }
}
