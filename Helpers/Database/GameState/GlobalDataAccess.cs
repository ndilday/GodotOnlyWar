using OnlyWar.Models;
using OnlyWar.Models.Events;
using System;
using System.Data;
using System.IO;

namespace OnlyWar.Helpers.Database.GameState
{
    // The single-row GlobalData table holds chapter-wide scalars that aren't owned by any
    // other aggregate: the current date, the Requisition pool (PRD 4.23), the gene-seed
    // stockpile count and aggregate purity (PRD 4.8), and the optional Opening Scenario
    // state (Design/Reference/OpeningScenario.md). Scenario is null for sandbox sectors. The
    // nullable Home World id is set when the Promised World is secured.
    public sealed record GlobalState(Date Date, int Requisition, int GeneseedStockpile,
                                     float GeneseedPurity, CampaignScenario Scenario,
                                     int? HomeWorldPlanetId, CampaignIdentity CampaignIdentity = null);

    public class GlobalDataAccess
    {
        public int GetSaveVersion(IDbConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT SaveVersion FROM GlobalData LIMIT 1";
            object result = command.ExecuteScalar();
            if (result == null || result is DBNull)
            {
                throw new InvalidDataException("The save contains no GlobalData row.");
            }

            return Convert.ToInt32(result);
        }

        public int EnsureCompatibleSaveVersion(IDbConnection connection)
        {
            int version = GetSaveVersion(connection);
            if (version < SaveFormat.MinimumSupportedVersion || version > SaveFormat.CurrentVersion)
            {
                throw new InvalidDataException(
                    $"Save version {version} is not supported by this build "
                    + $"(supported {SaveFormat.MinimumSupportedVersion}-{SaveFormat.CurrentVersion}).");
            }
            return version;
        }

        public GlobalState GetGlobalData(IDbConnection connection)
        {
            GlobalState state = null;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM GlobalData";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int millenium = reader.GetInt32(0);
                    int year = reader.GetInt32(1);
                    int week = reader.GetInt32(2);
                    // index 3 is SaveVersion
                    int requisition = reader.GetInt32(4);
                    int geneseedStockpile = reader.GetInt32(5);
                    float geneseedPurity = (float)reader.GetDouble(6);

                    CampaignScenario scenario = null;
                    ScenarioType type = (ScenarioType)reader.GetInt32(7);
                    if (type != ScenarioType.None)
                    {
                        int promisedPlanetId = reader.GetInt32(8);
                        ObjectiveState scenarioState = (ObjectiveState)reader.GetInt32(9);
                        bool briefingAcknowledged = reader.GetBoolean(10);
                        string briefingText = reader[11] is DBNull ? null : reader.GetString(11);
                        int authorityId = reader.GetInt32(12);
                        scenario = new CampaignScenario(type, promisedPlanetId, briefingText,
                            authorityId, scenarioState, briefingAcknowledged);
                    }

                    int? homeWorldPlanetId = reader[13] is DBNull ? null : reader.GetInt32(13);
                    CampaignIdentity campaignIdentity = ReadCampaignIdentity(reader);
                    state = new GlobalState(new Date(millenium, year, week), requisition,
                                            geneseedStockpile, geneseedPurity, scenario,
                                            homeWorldPlanetId, campaignIdentity);
                }
            }
            return state;
        }

        public void SaveGlobalData(IDbTransaction transaction, Date currentDate, int requisition,
                                   int geneseedStockpile, float geneseedPurity,
                                   CampaignScenario scenario, int? homeWorldPlanetId,
                                   CampaignIdentity campaignIdentity = null)
        {
            using (var command = transaction.Connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO GlobalData
                    (Millenium, Year, Week, SaveVersion, Requisition, GeneseedStockpile,
                     GeneseedPurity, ScenarioType, ScenarioPromisedPlanetId, ScenarioState,
                     ScenarioBriefingAcknowledged, ScenarioBriefingText,
                     ScenarioOriginalAuthorityCharacterId, HomeWorldPlanetId,
                     CampaignId, CampaignSeed, RandomAlgorithmVersion)
                    VALUES
                    (@millenium, @year, @week, @saveVersion, @requisition, @geneseedStockpile,
                     @geneseedPurity, @scenarioType, @scenarioPromisedPlanetId, @scenarioState,
                     @scenarioBriefingAcknowledged, @scenarioBriefingText,
                     @scenarioOriginalAuthorityCharacterId, @homeWorldPlanetId,
                     @campaignId, @campaignSeed, @randomAlgorithmVersion);";
                command.AddParam("@millenium", currentDate.Millenium);
                command.AddParam("@year", currentDate.Year);
                command.AddParam("@week", currentDate.Week);
                command.AddParam("@saveVersion", SaveFormat.CurrentVersion);
                command.AddParam("@requisition", requisition);
                command.AddParam("@geneseedStockpile", geneseedStockpile);
                command.AddParam("@geneseedPurity", geneseedPurity);
                command.AddParam("@scenarioType", (int)(scenario?.Type ?? ScenarioType.None));
                command.AddParam("@scenarioPromisedPlanetId", scenario?.PromisedPlanetId ?? 0);
                command.AddParam("@scenarioState", (int)(scenario?.State ?? ObjectiveState.Pending));
                command.AddParam("@scenarioBriefingAcknowledged",
                    (scenario?.BriefingAcknowledged ?? false) ? 1 : 0);
                command.AddParam("@scenarioBriefingText", scenario?.BriefingText);
                command.AddParam("@scenarioOriginalAuthorityCharacterId",
                    scenario?.OriginalAuthorityCharacterId ?? 0);
                command.AddParam("@homeWorldPlanetId", homeWorldPlanetId);
                CampaignIdentity identity = campaignIdentity ?? CampaignIdentity.Empty;
                command.AddParam("@campaignId", identity.CampaignId.ToString("D"));
                command.AddParam("@campaignSeed", identity.CampaignSeed);
                command.AddParam("@randomAlgorithmVersion", identity.RandomAlgorithmVersion);
                command.ExecuteNonQuery();
            }
        }

        private static CampaignIdentity ReadCampaignIdentity(IDataRecord reader)
        {
            int campaignIdOrdinal = TryGetOrdinal(reader, "CampaignId");
            int seedOrdinal = TryGetOrdinal(reader, "CampaignSeed");
            int versionOrdinal = TryGetOrdinal(reader, "RandomAlgorithmVersion");
            if (campaignIdOrdinal < 0 || seedOrdinal < 0 || versionOrdinal < 0
                || reader.IsDBNull(campaignIdOrdinal) || reader.IsDBNull(seedOrdinal))
            {
                return null;
            }

            string campaignIdText = reader.GetString(campaignIdOrdinal);
            if (!Guid.TryParse(campaignIdText, out Guid campaignId))
                throw new InvalidDataException($"GlobalData contains invalid CampaignId '{campaignIdText}'.");
            long seed = Convert.ToInt64(reader.GetValue(seedOrdinal));
            int version = reader.IsDBNull(versionOrdinal) ? 1 : Convert.ToInt32(reader.GetValue(versionOrdinal));
            return new CampaignIdentity(campaignId, seed, version);
        }

        private static int TryGetOrdinal(IDataRecord reader, string name)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (string.Equals(reader.GetName(i), name, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }
    }
}
