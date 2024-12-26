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
                    int id = reader.GetInt32(0);
                    int bodyId = reader.GetInt32(1);
                    string name = reader[2].ToString();
                    float naturalArmor = Convert.ToSingle(reader[3]);
                    float woundMultiplier = Convert.ToSingle(reader[4]);
                    int crippleLevel = Convert.ToInt32(reader[5]);
                    int severLevel = Convert.ToInt32(reader[6]);
                    bool isMotive = Convert.ToBoolean(reader[7]);
                    bool isRanged = Convert.ToBoolean(reader[8]);
                    bool isMelee = Convert.ToBoolean(reader[9]);
                    bool isVital = Convert.ToBoolean(reader[10]);
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
                            IsRangedWeaponHolder = isRanged,
                            IsMeleeWeaponHolder = isMelee,
                            IsVital = isVital,
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
