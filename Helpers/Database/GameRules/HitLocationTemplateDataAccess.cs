using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Data;

namespace OnlyWar.Helpers.Database.GameRules
{
    public class HitLocationTemplateDataAccess
    {
        public Dictionary<int, List<HitLocationTemplate>> GetHitLocationsByBodyId(IDbConnection connection)
        {
            Dictionary<int, List<HitLocationTemplate>> hitLocationTemplateMap =
                [];
            var stanceProbabilityMap = GetStanceHitProbabilitiesByHitLocationId(connection);
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM HitLocationTemplate";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = Convert.ToInt32(reader["Id"]);
                    int bodyId = Convert.ToInt32(reader["BodyId"]);
                    string name = reader["Name"].ToString();
                    float naturalArmor = Convert.ToSingle(reader["NaturalArmor"]);
                    float woundMultiplier = Convert.ToSingle(reader["WoundMultiplier"]);
                    int crippleLevel = Convert.ToInt32(reader["CrippleWoundLevel"]);
                    int severLevel = Convert.ToInt32(reader["SeverWoundLevel"]);
                    bool isMotive = Convert.ToBoolean(reader["IsMotive"]);
                    bool isVital = Convert.ToBoolean(reader["IsVital"]);
                    bool holdsProgenoid = Convert.ToBoolean(reader["HoldsProgenoid"]);
                    int? handGroupId = reader["HandGroupId"].GetType() == typeof(DBNull)
                        ? null
                        : Convert.ToInt32(reader["HandGroupId"]);
                    int[] hitProbabilityMap = stanceProbabilityMap[id];
                    HitLocationTemplate hitLocationTemplate =
                        new HitLocationTemplate
                        {
                            Id = id,
                            Name = name,
                            NaturalArmor = naturalArmor,
                            WoundMultiplier = woundMultiplier,
                            CrippleWound = (uint)crippleLevel,
                            SeverWound = (uint)severLevel,
                            IsMotive = isMotive,
                            HandGroupId = handGroupId,
                            IsVital = isVital,
                            HoldsProgenoid = holdsProgenoid,
                            HitProbabilityMap = hitProbabilityMap
                        };
                    if (!hitLocationTemplateMap.ContainsKey(bodyId))
                    {
                        hitLocationTemplateMap[bodyId] = [];
                    }
                    hitLocationTemplateMap[bodyId].Add(hitLocationTemplate);
                }
            }
            return hitLocationTemplateMap;
        }

        private Dictionary<int, int[]> GetStanceHitProbabilitiesByHitLocationId(IDbConnection connection)
        {
            Dictionary<int, int[]> hitProbabilityMap = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM HitLocationStanceSize";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int hitLocationId = reader.GetInt32(1);
                    int stance = reader.GetInt32(2);
                    int size = reader.GetInt32(3);

                    if (!hitProbabilityMap.ContainsKey(hitLocationId))
                    {
                        hitProbabilityMap[hitLocationId] = new int[3];
                    }
                    hitProbabilityMap[hitLocationId][stance] = size;
                }
            }
            return hitProbabilityMap;
        }
    }
}
