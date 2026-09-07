using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Data;

namespace OnlyWar.Helpers.Database.GameRules
{
    public class BaseSkillDataAccess
    {
        public Dictionary<int, BaseSkill> GetBaseSkills(IDbConnection connection)
        {
            Dictionary<int, BaseSkill> baseSkillMap = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM BaseSkill";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    string name = reader[1].ToString();
                    SkillCategory category = (SkillCategory)reader.GetInt32(2);
                    var attribute = (Models.Soldiers.Attribute)reader.GetInt32(3);
                    float difficulty = Convert.ToSingle(reader[4]);
                    // SkillKey was appended to preserve compatibility with focused legacy
                    // fixtures. Production rules data must provide it; GameRulesData validates
                    // that contract before exposing the runtime registry.
                    string skillKey = reader.FieldCount > 5 && !reader.IsDBNull(5)
                        ? reader[5].ToString()
                        : null;
                    BaseSkill baseSkill = new BaseSkill(
                        id, category, name, attribute, difficulty, skillKey);

                    baseSkillMap[id] = baseSkill;
                }
            }
            return baseSkillMap;
        }
    }
}
