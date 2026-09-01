using OnlyWar.Models;
using System;
using System.Collections.Generic;
using System.Data;

namespace OnlyWar.Helpers.Database.GameRules
{
    internal sealed class SectorGenerationProfileDataAccess
    {
        public IReadOnlyList<SectorGenerationProfile> GetProfiles(IDbConnection connection)
        {
            List<SectorGenerationProfile> profiles = [];
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = @"
                SELECT ProfileKey, SectorWidth, SectorHeight, PlanetSpawnProbability,
                       MaxSubsectorDiameter, IsDefault
                FROM SectorGenerationProfile
                ORDER BY ProfileKey";

            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string key = reader.IsDBNull(0) ? null : reader.GetString(0);
                profiles.Add(new SectorGenerationProfile(
                    key,
                    Convert.ToInt32(reader.GetValue(1)),
                    Convert.ToInt32(reader.GetValue(2)),
                    Convert.ToDouble(reader.GetValue(3)),
                    Convert.ToInt32(reader.GetValue(4)),
                    Convert.ToBoolean(reader.GetValue(5))));
            }

            return profiles;
        }
    }
}
