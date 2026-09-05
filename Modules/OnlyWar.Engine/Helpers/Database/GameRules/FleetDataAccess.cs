using OnlyWar.Models.Fleets;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace OnlyWar.Helpers.Database.GameRules
{
    public class FleetDataBlob
    {
        public Dictionary<int, List<BoatTemplate>> BoatTemplates { get; set; }
        public Dictionary<int, List<ShipTemplate>> ShipTemplates { get; set; }
        public Dictionary<int, List<FleetTemplate>> FleetTemplates { get; set; }
    }

    public class FleetDataAccess
    {
        public FleetDataBlob GetFleetData(IDbConnection connection)
        {
            FleetDataBlob dataBlob = new FleetDataBlob();
            dataBlob.BoatTemplates = GetBoatTemplatesByFactionId(connection);
            dataBlob.ShipTemplates = GetShipTemplatesByFactionId(connection);
            var fleetShipMap = GetFleetShipTemplateLists(connection);
            dataBlob.FleetTemplates = GetFleetTemplatesByFactionId(connection, 
                                                                   dataBlob.ShipTemplates, 
                                                                   fleetShipMap);
            return dataBlob;
        }

        private Dictionary<int, List<BoatTemplate>> GetBoatTemplatesByFactionId(IDbConnection connection)
        {
            Dictionary<int, List<BoatTemplate>> factionTemplateMap =
                [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM BoatTemplate";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    int factionId = reader.GetInt32(1);
                    string name = reader[2].ToString();
                    ushort soldierCap = (ushort)reader.GetInt16(3);
                    BoatTemplate boatTemplate = new BoatTemplate(id, name, soldierCap);
                    if (!factionTemplateMap.ContainsKey(factionId))
                    {
                        factionTemplateMap[factionId] = [];
                    }
                    factionTemplateMap[factionId].Add(boatTemplate);
                }
            }
            return factionTemplateMap;
        }

        private Dictionary<int, List<ShipTemplate>> GetShipTemplatesByFactionId(IDbConnection connection)
        {
            Dictionary<int, List<ShipTemplate>> factionTemplateMap =
                [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM ShipTemplate";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    int factionId = reader.GetInt32(1);
                    string name = reader[2].ToString();
                    ushort soldierCap = (ushort)reader.GetInt16(3);
                    ushort boatCap = (ushort)reader.GetInt16(4);
                    ushort landerCap = (ushort)reader.GetInt16(5);
                    int flagshipPrecedence = reader.FieldCount > 6 && !reader.IsDBNull(6)
                        ? reader.GetInt32(6)
                        : 0;
                    int hullSize = reader.FieldCount > 7 && !reader.IsDBNull(7)
                        ? reader.GetInt32(7)
                        : 0;
                    ShipTemplate boatTemplate = new ShipTemplate(
                        id, name, soldierCap, boatCap, landerCap,
                        flagshipPrecedence, hullSize);
                    if (!factionTemplateMap.ContainsKey(factionId))
                    {
                        factionTemplateMap[factionId] = [];
                    }
                    factionTemplateMap[factionId].Add(boatTemplate);
                }
            }
            return factionTemplateMap;
        }

        private Dictionary<int, List<int>> GetFleetShipTemplateLists(IDbConnection connection)
        {
            Dictionary<int, List<int>> fleetToShipMap = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM FleetTemplateShipTemplate";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int fleetId = reader.GetInt32(1);
                    int shipId = reader.GetInt32(2);
                    if (!fleetToShipMap.ContainsKey(fleetId))
                    {
                        fleetToShipMap[fleetId] = [];
                    }
                    fleetToShipMap[fleetId].Add(shipId);
                }
            }
            return fleetToShipMap;
        }

        private Dictionary<int, List<FleetTemplate>> GetFleetTemplatesByFactionId(IDbConnection connection,
                                                                                  Dictionary<int, List<ShipTemplate>> factionShipMap,
                                                                                  Dictionary<int, List<int>> fleetShipMap)
        {
            Dictionary<int, List<FleetTemplate>> factionTemplateMap =
                [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM FleetTemplate";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    int factionId = reader.GetInt32(1);
                    string name = reader[2].ToString();

                    List<ShipTemplate> baseList = RulesDatabaseLookup.Require(
                        factionShipMap,
                        factionId,
                        $"FleetTemplate {id}.FactionId");
                    List<ShipTemplate> fleetShipTemplateList = [];
                    List<int> shipTemplateIds = RulesDatabaseLookup.Require(
                        fleetShipMap,
                        id,
                        $"FleetTemplate {id}.ShipTemplates");
                    foreach (int shipTemplateId in shipTemplateIds)
                    {
                        ShipTemplate ship = baseList.FirstOrDefault(st => st.Id == shipTemplateId);
                        if (ship == null)
                        {
                            throw new InvalidOperationException(
                                $"Rules database FleetTemplate {id} references missing "
                                + $"ShipTemplate {shipTemplateId} for faction {factionId}.");
                        }
                        fleetShipTemplateList.Add(ship);
                    }

                    FleetTemplate fleetTemplate = new FleetTemplate(id, name, fleetShipTemplateList);
                    if (!factionTemplateMap.ContainsKey(factionId))
                    {
                        factionTemplateMap[factionId] = [];
                    }
                    factionTemplateMap[factionId].Add(fleetTemplate);
                }
            }
            return factionTemplateMap;
        }
    }
}
