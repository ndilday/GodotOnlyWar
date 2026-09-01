using OnlyWar.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace OnlyWar.Helpers.Database.GameRules
{
    /// <summary>
    /// Reads the declarative faction, scenario-participant, and initial-presence policies. The
    /// tables contain only data inputs; generation and scenario sequencing remain in code.
    /// </summary>
    public sealed class FactionGenerationPolicyDataAccess
    {
        public IReadOnlyList<FactionRoleAssignment> GetFactionRoleAssignments(IDbConnection connection)
        {
            List<FactionRoleAssignment> assignments = [];
            using IDbCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT RoleKey, FactionId FROM FactionRoleAssignment ORDER BY RoleKey";
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                assignments.Add(new FactionRoleAssignment(
                    reader.IsDBNull(0) ? null : reader.GetString(0),
                    reader.GetInt32(1)));
            }
            return assignments;
        }

        public IReadOnlyList<ScenarioProfile> GetScenarioProfiles(IDbConnection connection)
        {
            return GetScenarioProfiles(connection, GetScenarioFactionOptions(connection));
        }

        public IReadOnlyList<ScenarioProfile> GetScenarioProfiles(
            IDbConnection connection,
            IReadOnlyList<ScenarioFactionOption> options)
        {
            Dictionary<string, List<ScenarioFactionOption>> optionsByScenario = (options ?? [])
                .GroupBy(option => option.ScenarioKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

            List<ScenarioProfile> profiles = [];
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = @"
                SELECT ScenarioKey, MaxPromisedWorldPopulation, MinInvaderRegions,
                       MaxInvaderRegions, InvaderGarrisonStrengthMultiple,
                       ImperialRemnantFraction, PreLandingTurns,
                       InitialInfiltratorPopulationShareMin,
                       InitialInfiltratorPopulationShareMax,
                       InitialInfiltratorGarrisonPerPopulation,
                       PromisedWorldInfiltratorStrengthFraction,
                       PromisedWorldInfiltratorStartingIntel, PostLandingTurnsMean,
                       SectorLordOpinionReward, SectorLordOpinionPenalty
                FROM ScenarioProfile
                ORDER BY ScenarioKey";
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string key = reader.IsDBNull(0) ? null : reader.GetString(0);
                profiles.Add(new ScenarioProfile(
                    key,
                    reader.GetInt64(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    Convert.ToSingle(reader.GetValue(4)),
                    Convert.ToSingle(reader.GetValue(5)),
                    reader.GetInt32(6),
                    Convert.ToSingle(reader.GetValue(7)),
                    Convert.ToSingle(reader.GetValue(8)),
                    Convert.ToSingle(reader.GetValue(9)),
                    Convert.ToSingle(reader.GetValue(10)),
                    Convert.ToSingle(reader.GetValue(11)),
                    Convert.ToDouble(reader.GetValue(12)),
                    Convert.ToSingle(reader.GetValue(13)),
                    Convert.ToSingle(reader.GetValue(14)),
                    key != null && optionsByScenario.TryGetValue(key, out List<ScenarioFactionOption> scenarioOptions)
                        ? scenarioOptions
                        : []));
            }

            return profiles;
        }

        public IReadOnlyList<ScenarioFactionOption> GetScenarioFactionOptions(IDbConnection connection)
        {
            List<ScenarioFactionOption> options = [];
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = @"
                SELECT ScenarioKey, SlotKey, FactionId, SelectionWeight, IsRequired
                FROM ScenarioFactionOption
                ORDER BY ScenarioKey, SlotKey, FactionId";
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                options.Add(new ScenarioFactionOption(
                    reader.IsDBNull(0) ? null : reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetInt32(2),
                    Convert.ToDouble(reader.GetValue(3)),
                    Convert.ToBoolean(reader.GetValue(4))));
            }
            return options;
        }

        public IReadOnlyList<FactionPlanetPresenceRule> GetFactionPlanetPresenceRules(
            IDbConnection connection)
        {
            List<FactionPlanetPresenceRule> rules = [];
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = @"
                SELECT ProfileKey, FactionId, PlanetTemplateId, PresenceMode,
                       SpawnChance, PopulationShareMin, PopulationShareMax,
                       GarrisonPerPopulation
                FROM FactionPlanetPresenceRule
                ORDER BY ProfileKey, PlanetTemplateId, FactionId";
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                rules.Add(new FactionPlanetPresenceRule(
                    reader.IsDBNull(0) ? null : reader.GetString(0),
                    reader.GetInt32(1),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    (FactionPresenceMode)reader.GetInt32(3),
                    Convert.ToDouble(reader.GetValue(4)),
                    Convert.ToDouble(reader.GetValue(5)),
                    Convert.ToDouble(reader.GetValue(6)),
                    Convert.ToDouble(reader.GetValue(7))));
            }
            return rules;
        }
    }
}
