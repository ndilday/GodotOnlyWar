using OnlyWar.Builders;
using OnlyWar.Models.Equippables;
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
                                                               IReadOnlyDictionary<int, Region> regionMap,
                                                               IReadOnlyDictionary<int, Mission> missionMap)
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
                    SquadTemplate template = squadTemplateMap[squadTemplateId];

                    Squad squad = new Squad(id, name, null, template);
                    if (reader.FieldCount > 6 && reader[6].GetType() != typeof(DBNull))
                    {
                        squad.TrainingFocus = (TrainingFocuses)reader.GetInt32(6);
                    }
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
            var orderSquadMap = GetOrderSquadMapping(connection, squadByIdMap);
            PopulateOrdersBySquadId(connection, regionMap, squadByIdMap, orderSquadMap, missionMap);

            return squadMap;
        }

        private Dictionary<int, List<Squad>> GetOrderSquadMapping(IDbConnection connection,
            IReadOnlyDictionary<int, Squad> squadMap)
        {
            Dictionary<int, List<Squad>> orderSquadMap = [];

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM OrderSquad";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int orderId = reader.GetInt32(0);
                    int squadId = reader.GetInt32(1);
                    if (!orderSquadMap.ContainsKey(orderId))
                    {
                        orderSquadMap[orderId] = [];
                    }
                    orderSquadMap[orderId].Add(squadMap[squadId]);
                }
            }

            return orderSquadMap;
        }

        private void PopulateOrdersBySquadId(IDbConnection connection,
                                            IReadOnlyDictionary<int, Region> regionMap,
                                            IReadOnlyDictionary<int, Squad> squadMap,
                                            IReadOnlyDictionary<int, List<Squad>> orderSquadMap,
                                            IReadOnlyDictionary<int, Mission> missionMap)
        {
            using (var command = connection.CreateCommand())
            {
                int maxOrderId = 0;
                command.CommandText = "SELECT * FROM Assignment";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int orderId = reader.GetInt32(0);
                    int missionId = reader.GetInt32(1);
                    int disposition = reader.GetInt32(2);
                    bool isQuiet = reader.GetBoolean(3);
                    bool isActivelyEngaging = reader.GetBoolean(4);
                    int aggression = reader.GetInt32(5);
                    Disposition disp = (Disposition)disposition;
                    Aggression agg = (Aggression)aggression;
                    // The Order constructor reattaches the order to each of its squads via
                    // Squad.CurrentOrders, so the loaded order is restored onto its squads here.
                    Order order = new Order(orderId, orderSquadMap[orderId], disp, isQuiet, isActivelyEngaging, agg, missionMap[missionId]);
                    if(orderId > maxOrderId)
                    {
                        maxOrderId = orderId;
                    }
                }
                IdGenerator.SetNextOrderId(maxOrderId + 1);
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
            object parent = unit.ParentUnit == null ? null : (object)unit.ParentUnit.Id;
            using (var command = transaction.Connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO Unit VALUES
                    (@id, @factionId, @templateId, @parentId, @name);";
                command.AddParam("@id", unit.Id);
                command.AddParam("@factionId", unit.UnitTemplate.Faction.Id);
                command.AddParam("@templateId", unit.UnitTemplate.Id);
                command.AddParam("@parentId", parent);
                command.AddParam("@name", unit.Name);
                command.ExecuteNonQuery();
            }
        }

        public void SaveSquad(IDbTransaction transaction, Squad squad)
        {
            object ship = squad.BoardedLocation == null ? null : (object)squad.BoardedLocation.Id;
            object region = squad.CurrentRegion == null ? null : (object)squad.CurrentRegion.Id;
            using (var command = transaction.Connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO Squad VALUES
                    (@id, @templateId, @parentUnitId, @name, @ship, @region, @trainingFocus);";
                command.AddParam("@id", squad.Id);
                command.AddParam("@templateId", squad.SquadTemplate.Id);
                command.AddParam("@parentUnitId", squad.ParentUnit.Id);
                command.AddParam("@name", squad.Name);
                command.AddParam("@ship", ship);
                command.AddParam("@region", region);
                command.AddParam("@trainingFocus", (int)squad.TrainingFocus);
                command.ExecuteNonQuery();
            }

            if(squad.Loadout != null && squad.Loadout.Count > 0)
            {
                SaveSquadLoadout(transaction, squad);
            }
        }

        private void SaveSquadLoadout(IDbTransaction transaction, Squad squad)
        {
            foreach(WeaponSet weaponSet in squad.Loadout)
            {
                using (var command = transaction.Connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"INSERT INTO SquadWeaponSet VALUES
                        (@squadId, @weaponSetId);";
                    command.AddParam("@squadId", squad.Id);
                    command.AddParam("@weaponSetId", weaponSet.Id);
                    command.ExecuteNonQuery();
                }
            }
        }

        public void SaveOrder(IDbTransaction transaction, Order order)
        {
            // CREATE TABLE Assignment (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, MissionId INTEGER NOT NULL REFERENCES Mission (Id), Disposition INTEGER NOT NULL, IsQuiet BOOLEAN NOT NULL, IsActivelyEngaging BOOLEAN NOT NULL, Aggression INTEGER NOT NULL);
            using (var command = transaction.Connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO Assignment VALUES
                    (@id, @missionId, @disposition, @isQuiet, @isActivelyEngaging, @aggression);";
                command.AddParam("@id", order.Id);
                command.AddParam("@missionId", order.Mission.Id);
                command.AddParam("@disposition", (int)order.Disposition);
                command.AddParam("@isQuiet", order.IsQuiet ? 1 : 0);
                command.AddParam("@isActivelyEngaging", order.IsActivelyEngaging ? 1 : 0);
                command.AddParam("@aggression", (int)order.LevelOfAggression);
                command.ExecuteNonQuery();
            }
            foreach(Squad squad in order.AssignedSquads)
            {
                using (var command = transaction.Connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"INSERT INTO OrderSquad VALUES
                        (@orderId, @squadId);";
                    command.AddParam("@orderId", order.Id);
                    command.AddParam("@squadId", squad.Id);
                    command.ExecuteNonQuery();
                }
            }
        }
    }
}
