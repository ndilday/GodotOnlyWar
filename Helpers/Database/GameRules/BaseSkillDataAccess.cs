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
                    BaseSkill baseSkill = new BaseSkill(id, category, name, attribute, difficulty);

                    baseSkillMap[id] = baseSkill;
                }
            }
            return baseSkillMap;
        }
    }
}
