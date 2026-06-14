using OnlyWar.Models;
using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;
using System.Data;

namespace OnlyWar.Helpers.Database.GameState
{
    public class RequestDataAccess
    {
        public List<IRequest> GetRequests(IDbConnection connection,
                                          IReadOnlyDictionary<int, Character> characterMap,
                                          IReadOnlyDictionary<int, Faction> factionMap,
                                          List<Planet> planetList)
        {
            List<IRequest> requests = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM Request";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    int characterId = reader.GetInt32(1);
                    int planetId = reader.GetInt32(2);
                    Faction threatFaction = reader[3].GetType() != typeof(DBNull) ?
                        factionMap[reader.GetInt32(3)] : null;
                    int requestDate = reader.GetInt32(4);
                    Date fulfillDate;
                    if (reader[5].GetType() != typeof(DBNull))
                    {
                        fulfillDate = new Date(reader.GetInt32(5));
                    }
                    else
                    {
                        fulfillDate = null;
                    }
                    PresenceRequest request =
                        new PresenceRequest(id, planetList[planetId], characterMap[characterId],
                                            threatFaction, new Date(requestDate), fulfillDate);
                    requests.Add(request);
                    if(request.DateRequestFulfilled == null)
                    {
                        characterMap[characterId].ActiveRequest = request;
                    }
                }
            }
            return requests;
        }

        public void SaveRequest(IDbTransaction transaction, IRequest request)
        {
            object fulfillDate = request.DateRequestFulfilled != null ?
                    (object)request.DateRequestFulfilled.GetTotalWeeks() :
                    "null";
            object threatFactionId = request.ThreatFaction != null ?
                    (object)request.ThreatFaction.Id :
                    "null";
            string insert = $@"INSERT INTO Request
                (Id, CharacterId, PlanetId, ThreatFactionId, RequestDate, FulfillmentDate) VALUES
                ({request.Id}, {request.Requester.Id}, {request.TargetPlanet.Id}, {threatFactionId},
                {request.DateRequestMade.GetTotalWeeks()}, {fulfillDate});";
            using (var command = transaction.Connection.CreateCommand())
            {
                command.CommandText = insert;
                command.ExecuteNonQuery();
            }
        }
    }
}
