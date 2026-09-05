using OnlyWar.Models.Planets;
using System.Collections.Generic;
using System.Data;

namespace OnlyWar.Helpers.Database.GameRules
{
    /// <summary>
    /// Reads data-owned planet-template eligibility assignments. Generation contexts remain
    /// code-owned; this table supplies the content membership for each context.
    /// </summary>
    public sealed class PlanetTemplateEligibilityDataAccess
    {
        public IReadOnlyList<PlanetTemplateEligibilityAssignment> GetData(
            IDbConnection connection)
        {
            List<PlanetTemplateEligibilityAssignment> assignments = [];
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = @"
                SELECT ContextKey, PlanetTemplateId
                FROM PlanetTemplateEligibility
                ORDER BY ContextKey, PlanetTemplateId";
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                assignments.Add(new PlanetTemplateEligibilityAssignment(
                    reader.IsDBNull(0) ? null : reader.GetString(0),
                    reader.GetInt32(1)));
            }
            return assignments;
        }
    }
}
