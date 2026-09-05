using OnlyWar.Models.Soldiers.Ratings;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;

namespace OnlyWar.Helpers.Database.GameRules
{
    public sealed class RatingDataAccess
    {
        public IReadOnlyList<RatingDefinition> GetRatingDefinitions(IDbConnection connection)
        {
            Dictionary<int, List<RatingComponent>> componentsByDefinition = GetComponentsByDefinitionId(connection);
            Dictionary<int, List<RatingNormalizationFactor>> factorsByDefinition = GetFactorsByDefinitionId(connection);

            List<RatingDefinition> definitions = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Id, RatingKey, DisplayName, Aggregation FROM RatingDefinition";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    string key = reader.GetString(1);
                    string displayName = reader.GetString(2);
                    RatingAggregation aggregation = (RatingAggregation)reader.GetInt32(3);

                    List<RatingComponent> components =
                        componentsByDefinition.TryGetValue(id, out var c) ? c : [];
                    List<RatingNormalizationFactor> factors =
                        factorsByDefinition.TryGetValue(id, out var f) ? f : [];

                    definitions.Add(new RatingDefinition(id, key, displayName, aggregation, components, factors));
                }
            }
            return definitions;
        }

        public IReadOnlyList<RatingAwardTier> GetRatingAwardTiers(IDbConnection connection)
        {
            // Award tiers reference ratings by key for stability across reloads.
            Dictionary<int, string> keyByDefinitionId = GetRatingKeysById(connection);

            List<RatingAwardTier> tiers = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT Id, RatingDefinitionId, Level, Threshold, EffectType, AwardType, NameTemplate FROM RatingAwardTier";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    int definitionId = reader.GetInt32(1);
                    int level = reader.GetInt32(2);
                    double threshold = Convert.ToDouble(reader[3]);
                    RatingAwardEffect effect = (RatingAwardEffect)reader.GetInt32(4);
                    string awardType = reader[5] is DBNull ? null : reader.GetString(5);
                    string nameTemplate = reader.GetString(6);

                    tiers.Add(new RatingAwardTier(
                                                  id,
                                                  RulesDatabaseLookup.Require(
                                                      keyByDefinitionId,
                                                      definitionId,
                                                      $"RatingAwardTier {id}.RatingDefinitionId"),
                                                  level,
                                                  threshold,
                                                  effect, awardType, nameTemplate));
                }
            }
            return tiers;
        }

        /// <summary>
        /// Loads the data-owned mapping from gameplay capabilities to open-ended
        /// rating keys. Older rules databases predate this table and receive the
        /// shipped default mapping in <see cref="Models.GameRulesData"/>.
        /// </summary>
        public IReadOnlyList<RatingConsumerAssignment> GetRatingConsumerAssignments(
            IDbConnection connection)
        {
            List<RatingConsumerAssignment> assignments = [];
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT RoleKey, RatingKey FROM RatingConsumerAssignment";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    assignments.Add(new RatingConsumerAssignment(
                        reader.GetString(0), reader.GetString(1)));
                }
                return assignments;
            }
            catch (DbException exception) when (IsMissingTable(exception, "RatingConsumerAssignment"))
            {
                return null;
            }
        }

        /// <summary>
        /// Loads optional award presentation metadata. A null result means the
        /// database is an older stock database; an empty result is an intentional
        /// catalogue with no declared families.
        /// </summary>
        public IReadOnlyList<AwardFamilyDefinition> GetAwardFamilies(IDbConnection connection)
        {
            List<AwardFamilyDefinition> families = [];
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"SELECT AwardFamilyKey, DisplayName, IconAssetKey,
                    SortOrder, SummaryGroup, StackingGroup FROM AwardFamily";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    families.Add(new AwardFamilyDefinition(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetInt32(3),
                        reader[4] is DBNull ? null : reader.GetString(4),
                        reader[5] is DBNull ? null : reader.GetString(5)));
                }
                return families;
            }
            catch (DbException exception) when (IsMissingTable(exception, "AwardFamily"))
            {
                return null;
            }
        }

        private static bool IsMissingTable(DbException exception, string tableName) =>
            exception.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase)
            && exception.Message.Contains(tableName, StringComparison.OrdinalIgnoreCase);

        private Dictionary<int, string> GetRatingKeysById(IDbConnection connection)
        {
            Dictionary<int, string> keys = [];
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, RatingKey FROM RatingDefinition";
            var reader = command.ExecuteReader();
            while (reader.Read())
            {
                keys[reader.GetInt32(0)] = reader.GetString(1);
            }
            return keys;
        }

        private Dictionary<int, List<RatingComponent>> GetComponentsByDefinitionId(IDbConnection connection)
        {
            Dictionary<int, List<RatingComponent>> map = [];
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT RatingDefinitionId, ComponentType, TargetId, Ordinal FROM RatingComponent";
            var reader = command.ExecuteReader();
            while (reader.Read())
            {
                int definitionId = reader.GetInt32(0);
                RatingComponent component = new(
                    (RatingComponentType)reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
                if (!map.TryGetValue(definitionId, out var list))
                {
                    list = [];
                    map[definitionId] = list;
                }
                list.Add(component);
            }
            return map;
        }

        private Dictionary<int, List<RatingNormalizationFactor>> GetFactorsByDefinitionId(IDbConnection connection)
        {
            Dictionary<int, List<RatingNormalizationFactor>> map = [];
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT RatingDefinitionId, Low, High, Ordinal FROM RatingNormalizationFactor";
            var reader = command.ExecuteReader();
            while (reader.Read())
            {
                int definitionId = reader.GetInt32(0);
                RatingNormalizationFactor factor = new(
                    Convert.ToDouble(reader[1]), Convert.ToDouble(reader[2]), reader.GetInt32(3));
                if (!map.TryGetValue(definitionId, out var list))
                {
                    list = [];
                    map[definitionId] = list;
                }
                list.Add(factor);
            }
            return map;
        }
    }
}
