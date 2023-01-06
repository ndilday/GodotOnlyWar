﻿using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace OnlyWar.Helpers.Database.GameState
{
    public class FleetDataAccess
    {
        public Dictionary<int, List<Ship>> GetShipsByFleetId(IDbConnection connection,
                                                             IReadOnlyDictionary<int, ShipTemplate> shipTemplateMap)
        {
            Dictionary<int, List<Ship>> fleetShipMap = new Dictionary<int, List<Ship>>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM Ship";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    int shipTemplateId = reader.GetInt32(1);
                    int fleetId = reader.GetInt32(2);
                    string name = reader[3].ToString();

                    Ship ship = new Ship(id, name, shipTemplateMap[shipTemplateId]);

                    if (!fleetShipMap.ContainsKey(fleetId))
                    {
                        fleetShipMap[fleetId] = new List<Ship>();
                    }
                    fleetShipMap[fleetId].Add(ship);
                }
            }
            return fleetShipMap;
        }

        public List<TaskForce> GetFleetsByFactionId(IDbConnection connection,
                                                IReadOnlyDictionary<int, List<Ship>> fleetShipMap,
                                                IReadOnlyDictionary<int, Faction> factionMap,
                                                IReadOnlyList<Planet> planetList)
        {
            List<TaskForce> fleetList = new List<TaskForce>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM Fleet";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    int factionId = reader.GetInt32(1);
                    float x = (float)reader[2];
                    float y = (float)reader[3];

                    Planet destination;
                    if (reader[4].GetType() != typeof(DBNull))
                    {
                        destination = planetList.First(p => p.Id == reader.GetInt32(4));
                    }
                    else
                    {
                        destination = null;
                    }

                    // see if the position is a planet
                    Tuple<ushort, ushort> location = new((ushort)x, (ushort)y);
                    Planet planet = planetList.FirstOrDefault(p => p.Position == location);

                    TaskForce fleet = new TaskForce(id, factionMap[factionId], location, planet,
                                            destination, fleetShipMap[id]);
                    fleetList.Add(fleet);
                }
            }
            return fleetList;
        }

        public void SaveFleet(IDbTransaction transaction, TaskForce fleet)
        {
            string destination = fleet.Destination == null ? "null" : fleet.Destination.Id.ToString();
            string insert = $@"INSERT INTO Fleet VALUES ({fleet.Id}, {fleet.Faction.Id}, 
                {fleet.Position.Item1}, {fleet.Position.Item2}, {destination});";
            using (var command = transaction.Connection.CreateCommand())
            {
                command.CommandText = insert;
                command.ExecuteNonQuery();
            }
        }

        public void SaveShip(IDbTransaction transaction, Ship ship)
        {
            string insert = $@"INSERT INTO Ship VALUES ({ship.Id}, {ship.Template.Id}, 
                {ship.Fleet.Id}, '{ship.Name}');";
            using (var command = transaction.Connection.CreateCommand())
            {
                command.CommandText = insert;
                command.ExecuteNonQuery();
            }
        }
    }
}
