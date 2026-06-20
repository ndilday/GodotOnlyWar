using System;
using System.Data;

namespace OnlyWar.Helpers.Database
{
    internal static class DbCommandExtensions
    {
        /// <summary>
        /// Adds a named parameter to the command, mapping a null CLR value to
        /// <see cref="DBNull.Value"/> so nullable columns are written correctly.
        /// </summary>
        public static void AddParam(this IDbCommand command, string name, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
    }
}
