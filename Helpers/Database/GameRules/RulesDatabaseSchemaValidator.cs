using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace OnlyWar.Helpers.Database.GameRules
{
    /// <summary>
    /// Defines the rules-database schema boundary. The tables listed here are the tables the
    /// normal rules loader requires in order to build a usable campaign profile. Tables not in
    /// this list are deliberately compatibility or extension tables and must have an explicit
    /// fallback in their loader rather than becoming accidentally required.
    /// </summary>
    internal static class RulesDatabaseSchemaValidator
    {
        private static readonly string[] RequiredTables =
        [
            "Faction",
            "Species",
            "AttributeTemplate",
            "BaseSkill",
            "SkillTemplate",
            "HitLocationTemplate",
            "HitLocationStanceSize",
            "SoldierTemplate",
            "SoldierMosTraining",
            "SoldierTemplateRequirement",
            "TrainingProfile",
            "TrainingProfileEntry",
            "ScoutTrainingOption",
            "MeleeWeaponTemplate",
            "RangedWeaponTemplate",
            "WeaponSet",
            "SoldierTemplateWeaponOption",
            "ArmorTemplate",
            "SquadTemplate",
            "SquadTemplateElement",
            "SquadTemplateElementQuota",
            "UnitTemplate",
            "UnitTemplateTree",
            "UnitTemplateSquadTemplate",
            "PlanetTemplate",
            "PlanetTemplateEligibility",
            "BoatTemplate",
            "ShipTemplate",
            "FleetTemplate",
            "FleetTemplateShipTemplate",
            "RatingDefinition",
            "RatingComponent",
            "RatingNormalizationFactor",
            "RatingAwardTier",
            "FactionRoleAssignment",
            "ScenarioProfile",
            "ScenarioFactionOption",
            "FactionPlanetPresenceRule",
            "ChapterGenerationProfile",
            "ChapterGenerationTemplateAssignment",
            "ChapterGenerationFormationAssignment",
            "ChapterGenerationUnitOrder"
        ];

        // Optional policy tables. Their absence is valid because the corresponding loaders
        // supply a documented compatibility behavior:
        //
        //   SkillRoleAssignment / RatingConsumerAssignment / AwardFamily: shipped defaults;
        //   PersonalEquipmentRole / SquadTemplateElementEquipmentRole: legacy role inference;
        //   itemized equipment tables: legacy WeaponSet-derived catalog when unavailable;
        //   AmmunitionType: legacy weapon templates without ammunition identities.
        //
        // Do not add a table to RequiredTables merely because a query can read it. First decide
        // whether an absent or empty table has an intentional result for every caller.

        public static void Validate(IDbConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            HashSet<string> presentTables = GetPresentTables(connection);
            List<string> missingTables = RequiredTables
                .Where(table => !presentTables.Contains(table))
                .ToList();

            if (missingTables.Count > 0)
            {
                throw new InvalidOperationException(
                    "Rules database is missing required tables: "
                    + string.Join(", ", missingTables) + ".");
            }
        }

        private static HashSet<string> GetPresentTables(IDbConnection connection)
        {
            HashSet<string> tables = new(StringComparer.OrdinalIgnoreCase);
            using IDbCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name IS NOT NULL";
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
            return tables;
        }
    }
}
