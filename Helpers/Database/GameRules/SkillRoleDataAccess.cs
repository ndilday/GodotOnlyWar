using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;

namespace OnlyWar.Helpers.Database.GameRules
{
    public sealed class SkillRoleDataAccess
    {
        public IReadOnlyList<SkillRoleAssignment> GetSkillRoleAssignments(IDbConnection connection)
        {
            List<SkillRoleAssignment> assignments = [];
            try
            {
                using IDbCommand command = connection.CreateCommand();
                command.CommandText =
                    "SELECT RoleKey, SkillKey FROM SkillRoleAssignment ORDER BY RoleKey";
                using IDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    assignments.Add(new SkillRoleAssignment(
                        reader.IsDBNull(0) ? null : reader.GetString(0),
                        reader.IsDBNull(1) ? null : reader.GetString(1)));
                }
                return assignments;
            }
            catch (DbException exception) when (
                exception.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
            {
                // The assignment table is an opt-in extension. A database without it uses the
                // built-in role-to-same-key mapping, while a present table is validated strictly.
                return null;
            }
        }
    }
}
