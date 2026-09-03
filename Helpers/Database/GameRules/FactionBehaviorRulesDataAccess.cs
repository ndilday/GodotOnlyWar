using OnlyWar.Models.FactionBehaviors;
using System;
using System.Collections.Generic;
using System.Data;

namespace OnlyWar.Helpers.Database.GameRules
{
    /// <summary>
    /// Loads capability-owned balance profiles. The legacy faction table is read only as a save/data
    /// compatibility path; new rules databases use the generic table and vocabulary.
    /// </summary>
    public sealed class FactionBehaviorRulesDataAccess
    {
        public IReadOnlyList<FactionBehaviorRulesProfile> GetProfiles(IDbConnection connection)
        {
            if (HasTable(connection, "FactionBehaviorRulesProfile"))
            {
                return ReadGenericProfiles(connection);
            }

            if (HasTable(connection, "OrkCampaignProfile"))
            {
                // Keep old shipped databases and old mod databases loadable while the rules
                // author migrates the table. The returned type is still the generic contract.
                return new LegacyFactionBehaviorRulesDataAccess().GetProfiles(connection);
            }

            return [];
        }

        private static IReadOnlyList<FactionBehaviorRulesProfile> ReadGenericProfiles(
            IDbConnection connection)
        {
            List<FactionBehaviorRulesProfile> profiles = [];
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = @"
                SELECT ProfileKey, GhostSourceChancePerEmptyTile, MinimumGhostSourcesPerSector,
                       WeeklyConsolidationSigmaDivisor, WeeklyConsolidationDrift,
                       MobilizationMedian, MobilizationSigma, MobilizationMinimum,
                       MobilizationMaximum, DefendedLandingRatio, UndefendedLandingBattleValue,
                       SuccessorGenerationMinimumBattleValue, SuccessorMergeLeaderLoss,
                       TravelMultiplier, GhostLogisticGrowthRate, OccupiedCivilianDeclineRate,
                       ExceptionalAssassinationMargin, PublicGrowthMultiplier,
                       DormantGrowthMultiplier, DormantEmergenceMinimumPopulation,
                       DormantEmergenceChance, DormantCullingPopulationReductionFraction,
                       DormantCullingConsolidationReductionFraction,
                       DormantCullingOutsideHelpEffectivePdfFloor,
                       DormantCullingFalsePositiveCapacityCost, MoraleNearbyMobSupport,
                       MoraleLivingLeaderSupport, MoraleCasualtyPenalty, MoraleRoutPenalty,
                       MoraleSeparatedPenalty, MoraleCommandLossPenalty, MoraleMaximumSupport,
                       DormantInitialBeliefChance, DormantInitialBeliefEvidence
                FROM FactionBehaviorRulesProfile
                ORDER BY ProfileKey";
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                profiles.Add(new FactionBehaviorRulesProfile(
                    reader.GetString(0),
                    Convert.ToDouble(reader.GetValue(1)), reader.GetInt32(2),
                    Convert.ToDouble(reader.GetValue(3)), Convert.ToDouble(reader.GetValue(4)),
                    Convert.ToDouble(reader.GetValue(5)), Convert.ToDouble(reader.GetValue(6)),
                    Convert.ToDouble(reader.GetValue(7)), Convert.ToDouble(reader.GetValue(8)),
                    Convert.ToDouble(reader.GetValue(9)), Convert.ToInt64(reader.GetValue(10)),
                    Convert.ToInt64(reader.GetValue(11)), Convert.ToDouble(reader.GetValue(12)),
                    Convert.ToDouble(reader.GetValue(13)), Convert.ToDouble(reader.GetValue(14)),
                    Convert.ToDouble(reader.GetValue(15)), Convert.ToDouble(reader.GetValue(16)),
                    Convert.ToDouble(reader.GetValue(17)), Convert.ToDouble(reader.GetValue(18)),
                    Convert.ToInt64(reader.GetValue(19)), Convert.ToDouble(reader.GetValue(20)),
                    Convert.ToDouble(reader.GetValue(21)), Convert.ToDouble(reader.GetValue(22)),
                    Convert.ToDouble(reader.GetValue(23)), Convert.ToDouble(reader.GetValue(24)),
                    Convert.ToDouble(reader.GetValue(25)), Convert.ToDouble(reader.GetValue(26)),
                    Convert.ToDouble(reader.GetValue(27)), Convert.ToDouble(reader.GetValue(28)),
                    Convert.ToDouble(reader.GetValue(29)), Convert.ToDouble(reader.GetValue(30)),
                    Convert.ToDouble(reader.GetValue(31)), Convert.ToDouble(reader.GetValue(32)),
                    Convert.ToDouble(reader.GetValue(33))));
            }
            return profiles;
        }

        private static bool HasTable(IDbConnection connection, string tableName)
        {
            using IDbCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name";
            IDbDataParameter parameter = command.CreateParameter();
            parameter.ParameterName = "@name";
            parameter.Value = tableName;
            command.Parameters.Add(parameter);
            return Convert.ToInt32(command.ExecuteScalar()) > 0;
        }
    }
}
