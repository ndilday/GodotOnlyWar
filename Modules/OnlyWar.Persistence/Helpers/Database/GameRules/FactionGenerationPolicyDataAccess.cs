using OnlyWar.Domain;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace OnlyWar.Persistence.Database.GameRules
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
            return GetScenarioProfiles(connection, GetScenarioInfiltratorOverrides(connection));
        }

        public IReadOnlyList<ScenarioProfile> GetScenarioProfiles(
            IDbConnection connection,
            IReadOnlyList<ScenarioInfiltratorOverride> infiltratorOverrides)
        {
            Dictionary<string, ScenarioInfiltratorOverride> overridesByProfile =
                (infiltratorOverrides ?? [])
                    .Where(infiltratorOverride => infiltratorOverride != null)
                    .ToDictionary(
                        infiltratorOverride => infiltratorOverride.ProfileKey,
                        infiltratorOverride => infiltratorOverride,
                        StringComparer.OrdinalIgnoreCase);

            List<ScenarioProfile> profiles = [];
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = @"
                SELECT ProfileKey, ScenarioKey, PrimaryFactionId, PrimarySelectionWeight,
                       MaxPromisedWorldPopulation, MinInvaderRegions,
                       MaxInvaderRegions, InvaderGarrisonStrengthMultiple,
                       PostLandingTurnsMean, SectorLordOpinionReward,
                       SectorLordOpinionPenalty
                FROM ScenarioProfile
                ORDER BY ScenarioKey, PrimaryFactionId, ProfileKey";
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string key = reader.IsDBNull(0) ? null : reader.GetString(0);
                profiles.Add(new ScenarioProfile(
                    key,
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetInt32(2),
                    Convert.ToDouble(reader.GetValue(3)),
                    reader.GetInt64(4),
                    reader.GetInt32(5),
                    reader.GetInt32(6),
                    Convert.ToSingle(reader.GetValue(7)),
                    Convert.ToDouble(reader.GetValue(8)),
                    Convert.ToSingle(reader.GetValue(9)),
                    Convert.ToSingle(reader.GetValue(10)),
                    key != null && overridesByProfile.TryGetValue(
                        key,
                        out ScenarioInfiltratorOverride infiltratorOverride)
                        ? infiltratorOverride
                        : null));
            }

            return profiles;
        }

        public IReadOnlyList<ScenarioInfiltratorOverride> GetScenarioInfiltratorOverrides(
            IDbConnection connection)
        {
            List<ScenarioInfiltratorOverride> overrides = [];
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = @"
                SELECT ProfileKey, FactionId, PreLandingTurns,
                       InitialPopulationShareMin, InitialPopulationShareMax,
                       InitialGarrisonPerPopulation, StrengthFraction, StartingIntel
                FROM ScenarioInfiltratorOverride
                ORDER BY ProfileKey";
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                overrides.Add(new ScenarioInfiltratorOverride(
                    reader.IsDBNull(0) ? null : reader.GetString(0),
                    reader.GetInt32(1),
                    reader.GetInt32(2),
                    Convert.ToSingle(reader.GetValue(3)),
                    Convert.ToSingle(reader.GetValue(4)),
                    Convert.ToSingle(reader.GetValue(5)),
                    Convert.ToSingle(reader.GetValue(6)),
                    Convert.ToSingle(reader.GetValue(7))));
            }
            return overrides;
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
