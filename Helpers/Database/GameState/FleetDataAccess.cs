using OnlyWar.Models;
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
            Dictionary<int, List<Ship>> fleetShipMap = [];
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
                        fleetShipMap[fleetId] = [];
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
            List<TaskForce> fleetList = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM Fleet";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    int factionId = reader.GetInt32(1);
                    float x = Convert.ToSingle(reader[2]);
                    float y = Convert.ToSingle(reader[3]);
                    int travelWeeksRemaining = reader.FieldCount > 5 && reader[5].GetType() != typeof(DBNull)
                        ? reader.GetInt32(5)
                        : 0;
                    Planet destination;
                    if (reader[4].GetType() != typeof(DBNull))
                    {
                        destination = planetList.First(p => p.Id == reader.GetInt32(4));
                    }
                    else
                    {
                        destination = null;
                    }

                    Planet origin = GetPlanetById(reader, 6, planetList);
                    FleetTravelPhase travelPhase = reader.FieldCount > 7 && reader[7].GetType() != typeof(DBNull)
                        ? (FleetTravelPhase)reader.GetInt32(7)
                        : destination == null ? FleetTravelPhase.InOrbit : FleetTravelPhase.InWarp;
                    int currentPhaseWeeksRemaining = reader.FieldCount > 8 && reader[8].GetType() != typeof(DBNull)
                        ? reader.GetInt32(8)
                        : travelWeeksRemaining;
                    double warpSubjectiveWeeks = reader.FieldCount > 9 && reader[9].GetType() != typeof(DBNull)
                        ? reader.GetDouble(9)
                        : 0;
                    double warpObjectiveWeeks = reader.FieldCount > 10 && reader[10].GetType() != typeof(DBNull)
                        ? reader.GetDouble(10)
                        : travelWeeksRemaining;
                    bool warpSubjectiveTrainingApplied = reader.FieldCount <= 11
                        || reader[11].GetType() == typeof(DBNull)
                        || reader.GetBoolean(11);

                    // see if the position is a planet
                    Coordinate location = new((ushort)x, (ushort)y);
                    bool isInTransit = destination != null && travelWeeksRemaining > 0;
                    Planet planet = isInTransit
                        ? null
                        : planetList.FirstOrDefault(p => p.Position.Equals(location));

                    TaskForce fleet = new TaskForce(id, factionMap[factionId], location, planet,
                                            destination, fleetShipMap[id], travelWeeksRemaining,
                                            origin, travelPhase, currentPhaseWeeksRemaining,
                                            warpSubjectiveWeeks, warpObjectiveWeeks,
                                            warpSubjectiveTrainingApplied);
                    fleetList.Add(fleet);
                }
            }
            return fleetList;
        }

        private static Planet GetPlanetById(IDataRecord reader, int ordinal, IReadOnlyList<Planet> planetList)
        {
            if (reader.FieldCount <= ordinal || reader[ordinal].GetType() == typeof(DBNull)) return null;
            return planetList.First(p => p.Id == reader.GetInt32(ordinal));
        }

        public void SaveFleet(IDbTransaction transaction, TaskForce fleet)
        {
            string destination = fleet.Destination == null ? "null" : fleet.Destination.Id.ToString();
            string origin = fleet.Origin == null ? "null" : fleet.Origin.Id.ToString();
            int warpSubjectiveTrainingApplied = fleet.WarpSubjectiveTrainingApplied ? 1 : 0;
            string insert = $@"INSERT INTO Fleet VALUES ({fleet.Id}, {fleet.Faction.Id}, 
                {fleet.Position.Value.X}, {fleet.Position.Value.Y}, {destination}, {fleet.TravelWeeksRemaining},
                {origin}, {(int)fleet.TravelPhase}, {fleet.CurrentPhaseWeeksRemaining},
                {fleet.WarpSubjectiveWeeks}, {fleet.WarpObjectiveWeeks}, {warpSubjectiveTrainingApplied});";
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
