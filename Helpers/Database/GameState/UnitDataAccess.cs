using OnlyWar.Builders;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Models;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Orders;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

namespace OnlyWar.Helpers.Database.GameState
{
    public class UnitDataAccess
    {
        public IReadOnlyDictionary<int, Order> LoadedOrders { get; private set; } =
            new Dictionary<int, Order>();

        public Dictionary<int, List<Squad>> GetSquadsByUnitId(IDbConnection connection,
                                                               IReadOnlyDictionary<int, SquadTemplate> squadTemplateMap,
                                                               IReadOnlyDictionary<int, List<WeaponSet>> squadWeaponSetMap,
                                                               IReadOnlyDictionary<int, Ship> shipMap,
                                                               IReadOnlyDictionary<int, Region> regionMap,
                                                               IReadOnlyDictionary<int, Mission> missionMap,
                                                               IReadOnlyDictionary<int, Faction> factionMap = null)
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
                    squad.UsesLoadoutDoctrine = reader.GetBoolean(7);
                    if (reader[8].GetType() != typeof(DBNull))
                    {
                        squad.FormationOrdinal = reader.GetInt32(8);
                    }
                    squad.HasBattleHistory = reader.GetBoolean(9);
                    squadByIdMap[id] = squad;


                    if (reader[4].GetType() != typeof(DBNull))
                    {
                        squad.BoardedLocation = shipMap[reader.GetInt32(4)];
                        squad.BoardedLocation.LoadSquad(squad);
                    }

                    if (reader[5].GetType() != typeof(DBNull))
                    {
                        squad.CurrentRegion = regionMap[reader.GetInt32(5)];
                        if (squad.CurrentRegion.RegionFactionMap.TryGetValue(squad.Faction.Id, out RegionFaction regionFaction)
                            && !regionFaction.LandedSquads.Contains(squad))
                        {
                            regionFaction.LandedSquads.Add(squad);
                        }
                    }

                    if (reader.FieldCount > 10)
                    {
                        Ship dutyShip = reader[10].GetType() == typeof(DBNull)
                            ? null : shipMap.GetValueOrDefault(reader.GetInt32(10));
                        Region dutyRegion = reader[11].GetType() == typeof(DBNull)
                            ? null : regionMap.GetValueOrDefault(reader.GetInt32(11));
                        if ((dutyShip == null) == (dutyRegion == null)
                            && (dutyShip != null || dutyRegion != null))
                        {
                            throw new InvalidDataException(
                                $"Administrative squad {id} has an invalid duty station.");
                        }
                        if (dutyShip != null || dutyRegion != null)
                        {
                            if (!squad.PermitsIndividualDeployment || squad.CurrentRegion != null
                                || squad.BoardedLocation != null)
                            {
                                throw new InvalidDataException(
                                    $"Squad {id} has a duty station but is not a seated administrative formation.");
                            }
                            squad.DutyStation = dutyShip != null
                                ? CampaignLocation.Aboard(dutyShip)
                                : CampaignLocation.Landed(dutyRegion);
                            if (dutyShip != null)
                            {
                                dutyShip.StationAdministrativeFormation(squad);
                            }
                        }
                        else if (squad.PermitsIndividualDeployment)
                        {
                            throw new InvalidDataException(
                                $"Administrative squad {id} has no duty station.");
                        }
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
            PopulateOrdersBySquadId(connection, regionMap, squadByIdMap, orderSquadMap, missionMap, factionMap);

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
                                            IReadOnlyDictionary<int, Mission> missionMap,
                                            IReadOnlyDictionary<int, Faction> factionMap)
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
                    bool isQuiet = reader.GetBoolean(2);
                    bool isActivelyEngaging = reader.GetBoolean(3);
                    int aggression = reader.GetInt32(4);
                    Aggression agg = (Aggression)aggression;
                    // The Order constructor reattaches the order to each of its squads via
                    // Squad.CurrentOrders, so the loaded order is restored onto its squads here.
                    Faction ownerFaction = reader.FieldCount > 5 && !reader.IsDBNull(5)
                        ? factionMap?.GetValueOrDefault(reader.GetInt32(5))
                        : null;
                    if (reader.FieldCount > 5 && ownerFaction == null)
                    {
                        throw new InvalidDataException(
                            $"Order {orderId} references a missing owner faction.");
                    }
                    Order order = new Order(orderId,
                        orderSquadMap.GetValueOrDefault(orderId) ?? [],
                        isQuiet, isActivelyEngaging, agg, missionMap[missionId], ownerFaction);
                    LoadedOrders = LoadedOrders
                        .Append(new KeyValuePair<int, Order>(orderId, order))
                        .ToDictionary(pair => pair.Key, pair => pair.Value);
                    if(orderId > maxOrderId)
                    {
                        maxOrderId = orderId;
                    }
                }
                IdGenerator.SetNextOrderId(maxOrderId + 1);
            }
        }

        public void PopulateOrderCharacters(
            IDbConnection connection,
            IReadOnlyDictionary<int, PlayerSoldier> soldiers)
        {
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT OrderId, SoldierId FROM OrderCharacter";
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                int orderId = reader.GetInt32(0);
                int soldierId = reader.GetInt32(1);
                if (!LoadedOrders.TryGetValue(orderId, out Order order)
                    || !soldiers.TryGetValue(soldierId, out PlayerSoldier soldier))
                {
                    throw new InvalidDataException(
                        $"OrderCharacter ({orderId}, {soldierId}) references a missing order or player soldier.");
                }
                if (soldier.CurrentOrder != null && !ReferenceEquals(soldier.CurrentOrder, order))
                {
                    throw new InvalidDataException(
                        $"Player soldier {soldierId} is assigned to multiple orders.");
                }
                OrderForceService.BindLoadedCharacter(order, soldier);
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
                command.CommandText = @"INSERT INTO Squad
                    (Id, SquadTemplateId, ParentUnitId, Name, LoadedShipId, LandedRegionId,
                     TrainingFocus, UsesLoadoutDoctrine, FormationOrdinal, HasBattleHistory,
                     DutyStationShipId, DutyStationRegionId) VALUES
                    (@id, @templateId, @parentUnitId, @name, @ship, @region, @trainingFocus,
                     @usesLoadoutDoctrine, @formationOrdinal, @hasBattleHistory,
                     @dutyStationShip, @dutyStationRegion);";
                command.AddParam("@id", squad.Id);
                command.AddParam("@templateId", squad.SquadTemplate.Id);
                command.AddParam("@parentUnitId", squad.ParentUnit.Id);
                command.AddParam("@name", squad.Name);
                command.AddParam("@ship", ship);
                command.AddParam("@region", region);
                command.AddParam("@trainingFocus", (int)squad.TrainingFocus);
                command.AddParam("@usesLoadoutDoctrine", squad.UsesLoadoutDoctrine ? 1 : 0);
                command.AddParam("@formationOrdinal", squad.FormationOrdinal.HasValue
                    ? squad.FormationOrdinal.Value
                    : null);
                command.AddParam("@hasBattleHistory", squad.HasBattleHistory ? 1 : 0);
                command.AddParam("@dutyStationShip", squad.DutyStation?.Ship?.Id);
                command.AddParam("@dutyStationRegion", squad.DutyStation?.Region?.Id);
                command.ExecuteNonQuery();
            }

            if(!squad.UsesLoadoutDoctrine && squad.Loadout != null && squad.Loadout.Count > 0)
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
            if (order?.Mission == null || order.OwnerFaction == null)
            {
                throw new InvalidDataException(
                    $"Order {order?.Id.ToString() ?? "<null>"} has no mission or owner faction.");
            }
            using (var command = transaction.Connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO Assignment
                    (Id, MissionId, IsQuiet, IsActivelyEngaging, Aggression, OwnerFactionId) VALUES
                    (@id, @missionId, @isQuiet, @isActivelyEngaging, @aggression, @ownerFactionId);";
                command.AddParam("@id", order.Id);
                command.AddParam("@missionId", order.Mission.Id);
                command.AddParam("@isQuiet", order.IsQuiet ? 1 : 0);
                command.AddParam("@isActivelyEngaging", order.IsActivelyEngaging ? 1 : 0);
                command.AddParam("@aggression", (int)order.LevelOfAggression);
                command.AddParam("@ownerFactionId", order.OwnerFaction?.Id);
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
            foreach (PlayerSoldier character in order.AssignedCharacters)
            {
                using var command = transaction.Connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO OrderCharacter
                    (OrderId, SoldierId) VALUES (@orderId, @soldierId);";
                command.AddParam("@orderId", order.Id);
                command.AddParam("@soldierId", character.Id);
                command.ExecuteNonQuery();
            }
        }

    }
}
