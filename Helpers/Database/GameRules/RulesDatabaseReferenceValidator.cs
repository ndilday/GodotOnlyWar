using System;
using System.Collections.Generic;
using System.Data;

namespace OnlyWar.Helpers.Database.GameRules
{
    /// <summary>
    /// Checks the relational references that otherwise can be lost while the object graph is
    /// hydrated. Loader-side lookup guards still provide context for polymorphic/positional
    /// relationships, while these checks catch orphan rows that a loader would simply ignore.
    /// </summary>
    internal static class RulesDatabaseReferenceValidator
    {
        public static void Validate(IDbConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            List<string> errors = [];

            RequireReferences(
                connection, "SkillTemplate", "BaseSkillId", "BaseSkill", "Id", errors);
            RequireReferences(
                connection, "SoldierMosTraining", "BaseSkillId", "BaseSkill", "Id", errors);
            RequireReferences(
                connection, "SoldierMosTraining", "SoldierTemplateId", "SoldierTemplate", "Id", errors);
            RequireReferences(
                connection, "TrainingProfileEntry", "TrainingProfileId", "TrainingProfile", "Id", errors);
            RequireReferences(
                connection, "ScoutTrainingOption", "TrainingProfileId", "TrainingProfile", "Id", errors);
            RequireReferences(
                connection, "MeleeWeaponTemplate", "RelatedSkillId", "BaseSkill", "Id", errors);
            RequireReferences(
                connection, "RangedWeaponTemplate", "RelatedSkillId", "BaseSkill", "Id", errors);
            RequireReferences(
                connection, "SquadTemplateElement", "SoldierTemplateId", "SoldierTemplate", "Id", errors);
            RequireReferences(
                connection, "SquadTemplateElement", "SquadTemplateId", "SquadTemplate", "Id", errors);
            RequireReferences(
                connection, "SquadTemplateElementQuota", "SquadTemplateElementId",
                "SquadTemplateElement", "Id", errors);
            RequireReferences(
                connection, "UnitTemplateSquadTemplate", "UnitTemplateId", "UnitTemplate", "Id", errors);
            RequireReferences(
                connection, "UnitTemplateSquadTemplate", "SquadTemplateId", "SquadTemplate", "Id", errors);
            RequireReferences(
                connection, "UnitTemplateTree", "ParentUnitTemplateId", "UnitTemplate", "Id", errors);
            RequireReferences(
                connection, "UnitTemplateTree", "ChildUnitTemplateId", "UnitTemplate", "Id", errors);
            RequireReferences(
                connection, "FleetTemplateShipTemplate", "FleetTemplateId", "FleetTemplate", "Id", errors);
            RequireReferences(
                connection, "FleetTemplateShipTemplate", "ShipTemplateId", "ShipTemplate", "Id", errors);
            RequireReferences(
                connection, "RatingComponent", "RatingDefinitionId", "RatingDefinition", "Id", errors);
            RequireReferences(
                connection, "RatingNormalizationFactor", "RatingDefinitionId", "RatingDefinition", "Id", errors);
            RequireReferences(
                connection, "RatingAwardTier", "RatingDefinitionId", "RatingDefinition", "Id", errors);
            RequireReferences(
                connection, "PlanetTemplateEligibility", "PlanetTemplateId",
                "PlanetTemplate", "Id", errors);

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    "Rules database reference validation failed:\n - "
                    + string.Join("\n - ", errors));
            }
        }

        private static void RequireReferences(
            IDbConnection connection,
            string sourceTable,
            string sourceColumn,
            string targetTable,
            string targetColumn,
            ICollection<string> errors)
        {
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = $@"
                SELECT [{sourceColumn}]
                FROM [{sourceTable}]
                WHERE [{sourceColumn}] IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM [{targetTable}] target
                      WHERE target.[{targetColumn}] = [{sourceTable}].[{sourceColumn}])
                LIMIT 1";

            object value = command.ExecuteScalar();
            if (value != null && value != DBNull.Value)
            {
                errors.Add(
                    $"Reference '{sourceTable}.{sourceColumn}' points to missing "
                    + $"id '{value}' in '{targetTable}.{targetColumn}'.");
            }
        }
    }
}
