using OnlyWar.Models;
using System;
using System.Data;

namespace OnlyWar.Helpers.Database.GameState
{
    // The single-row GlobalData table holds chapter-wide scalars that aren't owned by any
    // other aggregate: the current date, the Requisition pool (PRD 4.23), the gene-seed
    // stockpile count and aggregate purity (PRD 4.8), and the optional Opening Scenario
    // state (Design/OpeningScenario.md §7). Scenario is null for legacy saves and sandbox sectors.
    public sealed record GlobalState(Date Date, int Requisition, int GeneseedStockpile,
                                     float GeneseedPurity, CampaignScenario Scenario);

    public class GlobalDataAccess
    {
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

                    // Scenario columns are appended after GeneseedPurity. A legacy save that
                    // predates them has fewer columns, so guard on the column count and treat
                    // such saves (and any ScenarioType.None row) as having no scenario.
                    CampaignScenario scenario = null;
                    if (reader.FieldCount > 7)
                    {
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
                    }

                    state = new GlobalState(new Date(millenium, year, week), requisition,
                                            geneseedStockpile, geneseedPurity, scenario);
                }
            }
            return state;
        }

        public void SaveGlobalData(IDbTransaction transaction, Date currentDate, int requisition,
                                   int geneseedStockpile, float geneseedPurity, CampaignScenario scenario)
        {
            using (var command = transaction.Connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO GlobalData VALUES
                    (@millenium, @year, @week, 1, @requisition, @geneseedStockpile, @geneseedPurity,
                     @scenarioType, @scenarioPromisedPlanetId, @scenarioState,
                     @scenarioBriefingAcknowledged, @scenarioBriefingText,
                     @scenarioOriginalAuthorityCharacterId);";
                command.AddParam("@millenium", currentDate.Millenium);
                command.AddParam("@year", currentDate.Year);
                command.AddParam("@week", currentDate.Week);
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
                command.ExecuteNonQuery();
            }
        }
    }
}
