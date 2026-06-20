using OnlyWar.Models;
using System.Data;

namespace OnlyWar.Helpers.Database.GameState
{
    public class GlobalDataAccess
    {
        public Date GetGlobalData(IDbConnection connection)
        {
            Date date = null;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM GlobalData";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int millenium = reader.GetInt32(0);
                    int year = reader.GetInt32(1);
                    int week = reader.GetInt32(2);

                    date = new Date(millenium, year, week);
                }
            }
            return date;
        }

        public void SaveDate(IDbTransaction transaction, Date currentDate)
        {
            using (var command = transaction.Connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO GlobalData VALUES
                    (@millenium, @year, @week, 1);";
                command.AddParam("@millenium", currentDate.Millenium);
                command.AddParam("@year", currentDate.Year);
                command.AddParam("@week", currentDate.Week);
                command.ExecuteNonQuery();
            }
        }
    }
}
