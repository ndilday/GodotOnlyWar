using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Data;

namespace OnlyWar.Helpers.Database.GameRules
{
    public sealed class ScoutTrainingOptionDataAccess
    {
        public ScoutTrainingOptionCatalog GetCatalog(
            IDbConnection connection,
            IReadOnlyDictionary<int, TrainingProfile> profiles)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (profiles == null) throw new ArgumentNullException(nameof(profiles));

            List<ScoutTrainingOption> options = [];
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = @"
                SELECT OptionKey, DisplayName, TrainingProfileId, SortOrder
                FROM ScoutTrainingOption
                ORDER BY SortOrder, OptionKey";
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string key = reader.IsDBNull(0) ? null : reader.GetString(0);
                string displayName = reader.IsDBNull(1) ? null : reader.GetString(1);
                int profileId = reader.GetInt32(2);
                int sortOrder = reader.GetInt32(3);
                TrainingProfile profile = RulesDatabaseLookup.Require(
                    profiles,
                    profileId,
                    $"ScoutTrainingOption '{key}'.TrainingProfileId");
                options.Add(new ScoutTrainingOption(key, displayName, profile, sortOrder));
            }

            return new ScoutTrainingOptionCatalog(options);
        }
    }
}
