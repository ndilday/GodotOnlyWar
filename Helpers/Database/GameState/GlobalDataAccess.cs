using OnlyWar.Models;
using System.Data;

namespace OnlyWar.Helpers.Database.GameState
{
    // The single-row GlobalData table holds chapter-wide scalars that aren't owned by any
    // other aggregate: the current date, the Requisition pool (PRD 4.23), and the gene-seed
    // stockpile count and aggregate purity (PRD 4.8).
    public sealed record GlobalState(Date Date, int Requisition, int GeneseedStockpile, float GeneseedPurity);

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

                    state = new GlobalState(new Date(millenium, year, week), requisition,
                                            geneseedStockpile, geneseedPurity);
                }
            }
            return state;
        }

        public void SaveGlobalData(IDbTransaction transaction, Date currentDate, int requisition,
                                   int geneseedStockpile, float geneseedPurity)
        {
            using (var command = transaction.Connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO GlobalData VALUES
                    (@millenium, @year, @week, 1, @requisition, @geneseedStockpile, @geneseedPurity);";
                command.AddParam("@millenium", currentDate.Millenium);
                command.AddParam("@year", currentDate.Year);
                command.AddParam("@week", currentDate.Week);
                command.AddParam("@requisition", requisition);
                command.AddParam("@geneseedStockpile", geneseedStockpile);
                command.AddParam("@geneseedPurity", geneseedPurity);
                command.ExecuteNonQuery();
            }
        }
    }
}
