using OnlyWar.Models.Supply;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;

namespace OnlyWar.Helpers.Database.GameRules
{
    internal sealed class SupplyRulesDataAccess
    {
        internal SupplyEconomyRules GetData(IDbConnection connection)
        {
            Dictionary<string, decimal> values = new(StringComparer.OrdinalIgnoreCase);
            using (IDbCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT RuleKey, RuleValue FROM SupplyRule";
                using IDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    values[reader.GetString(0)] = Convert.ToDecimal(reader.GetValue(1), CultureInfo.InvariantCulture);
                }
            }

            List<ThroughputPremiumBand> bands = [];
            using (IDbCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT MaximumBattleValuePerWeek, Multiplier FROM SupplyThroughputPremium ORDER BY MaximumBattleValuePerWeek";
                using IDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    bands.Add(new ThroughputPremiumBand(
                        reader.GetInt64(0),
                        Convert.ToDecimal(reader.GetValue(1), CultureInfo.InvariantCulture)));
                }
            }

            List<QualificationPremium> qualificationPremiums = [];
            using (IDbCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT GroupKey, RequirementKey, Multiplier FROM SupplyQualificationPremium";
                using IDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    qualificationPremiums.Add(new QualificationPremium(
                        reader.GetString(0), reader.GetString(1),
                        Convert.ToDecimal(reader.GetValue(2), CultureInfo.InvariantCulture)));
                }
            }

            Dictionary<string, decimal> LoadMultipliers(string tableName)
            {
                Dictionary<string, decimal> result = new(StringComparer.OrdinalIgnoreCase);
                using IDbCommand command = connection.CreateCommand();
                command.CommandText = $"SELECT MultiplierKey, Multiplier FROM {tableName}";
                using IDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    result[reader.GetString(0)] = Convert.ToDecimal(
                        reader.GetValue(1), CultureInfo.InvariantCulture);
                }
                return result;
            }

            Dictionary<int, decimal> worldMultipliers = [];
            using (IDbCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT PlanetTemplateId, Multiplier FROM SupplyWorldRequisitionPremium";
                using IDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    worldMultipliers[reader.GetInt32(0)] = Convert.ToDecimal(
                        reader.GetValue(1), CultureInfo.InvariantCulture);
                }
            }

            decimal Value(string key)
            {
                if (!values.TryGetValue(key, out decimal value))
                    throw new InvalidOperationException($"Rules database is missing supply rule '{key}'.");
                return value;
            }

            RequestValuationRules valuation = new(
                Value("RequisitionPerBvt"), bands,
                decimal.ToInt32(Value("MinimumRequestValue")),
                decimal.ToInt32(Value("MaximumRequestValue")),
                Value("MaximumCombinedPremium"));
            GovernorOfferRules offers = new(
                decimal.ToInt32(Value("MinimumOffer")),
                decimal.ToInt32(Value("MaximumOffer")),
                Value("MinimumWillingnessMultiplier"),
                Value("MaximumWillingnessMultiplier"));
            return new SupplyEconomyRules(
                valuation,
                offers,
                decimal.ToInt32(Value("DefaultServiceWeeks")),
                decimal.ToInt32(Value("DefaultDeadlineWeeks")),
                decimal.ToInt32(Value("DefaultDeliveryWeeks")),
                decimal.ToInt32(Value("StandingCadenceWeeks")),
                Value("StandingDeliveryFraction"),
                decimal.ToInt32(Value("StandingMinimumOffer")),
                decimal.ToInt32(Value("RequestCooldownWeeks")),
                qualificationPremiums,
                LoadMultipliers("SupplyHazardPremium"),
                LoadMultipliers("SupplyAuthorityPremium"),
                LoadMultipliers("SupplyDesperationPremium"),
                worldMultipliers,
                Value("RelationshipBaseMultiplier"),
                Value("RelationshipOpinionScale"));
        }
    }
}
