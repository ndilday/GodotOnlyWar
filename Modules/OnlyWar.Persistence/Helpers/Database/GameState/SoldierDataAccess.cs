using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Data;

namespace OnlyWar.Helpers.Database.GameState
{
    public class SoldierDataAccess
    {
        public Dictionary<int, Soldier> GetData(IDbConnection dbCon, 
                                                IReadOnlyDictionary<int, HitLocationTemplate> hitLocationTemplateMap,
                                                IReadOnlyDictionary<int, BaseSkill> baseSkillMap, 
                                                IReadOnlyDictionary<int, SoldierTemplate> soldierTemplateMap,
                                                IReadOnlyDictionary<int, Squad> squadMap)
        {
            var hitLocationMap = GetHitLocationsBySoldierId(dbCon, hitLocationTemplateMap);
            var soldierSkillMap = GetSkillsBySoldierId(dbCon, baseSkillMap);
            var soldierMap = GetSoldiers(dbCon, soldierTemplateMap, squadMap, soldierSkillMap, hitLocationMap);

            return soldierMap;
        }

        public void SaveSoldier(IDbTransaction transaction, ISoldier soldier)
        {
            using (var command = transaction.Connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO Soldier VALUES
                    (@id, @templateId, @squadId, @name,
                     @strength, @dexterity, @constitution, @intelligence, @perception,
                     @ego, @charisma, @psychicPower, @attackSpeed, @size, @moveSpeed);";
                command.AddParam("@id", soldier.Id);
                command.AddParam("@templateId", soldier.Template.Id);
                // Fallen brothers belong to no squad; persist a null SquadId for them.
                command.AddParam("@squadId", (object)soldier.AssignedSquad?.Id ?? DBNull.Value);
                command.AddParam("@name", soldier.Name);
                command.AddParam("@strength", soldier.Strength);
                command.AddParam("@dexterity", soldier.Dexterity);
                command.AddParam("@constitution", soldier.Constitution);
                command.AddParam("@intelligence", soldier.Intelligence);
                command.AddParam("@perception", soldier.Perception);
                command.AddParam("@ego", soldier.Ego);
                command.AddParam("@charisma", soldier.Charisma);
                command.AddParam("@psychicPower", soldier.PsychicPower);
                command.AddParam("@attackSpeed", soldier.AttackSpeed);
                command.AddParam("@size", soldier.Size);
                command.AddParam("@moveSpeed", soldier.MoveSpeed);
                command.ExecuteNonQuery();
            }

            foreach (Skill skill in soldier.Skills)
            {
                using (var command = transaction.Connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"INSERT INTO SoldierSkill VALUES
                        (@soldierId, @baseSkillId, @pointsInvested);";
                    command.AddParam("@soldierId", soldier.Id);
                    command.AddParam("@baseSkillId", skill.BaseSkill.Id);
                    command.AddParam("@pointsInvested", skill.PointsInvested);
                    command.ExecuteNonQuery();
                }
            }

            foreach (HitLocation hitLocation in soldier.Body.HitLocations)
            {
                using (var command = transaction.Connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"INSERT INTO HitLocation VALUES
                        (@soldierId, @templateId, @isCybernetic, @armor, @woundTotal, @weeksOfHealing);";
                    command.AddParam("@soldierId", soldier.Id);
                    command.AddParam("@templateId", hitLocation.Template.Id);
                    command.AddParam("@isCybernetic", hitLocation.IsCybernetic);
                    command.AddParam("@armor", hitLocation.Armor);
                    command.AddParam("@woundTotal", hitLocation.Wounds.WoundTotal);
                    command.AddParam("@weeksOfHealing", hitLocation.Wounds.WeeksOfHealing);
                    command.ExecuteNonQuery();
                }
            }
        }

        private Dictionary<int, List<HitLocation>> GetHitLocationsBySoldierId(IDbConnection connection,
                                                                              IReadOnlyDictionary<int, HitLocationTemplate> hitLocationTemplateMap)
        {
            Dictionary<int, List<HitLocation>> hitLocationMap = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM HitLocation";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int soldierId = reader.GetInt32(0);
                    int hitLocationTemplateId = reader.GetInt32(1);
                    bool isCybernetic = reader.GetBoolean(2);
                    float armor = Convert.ToSingle(reader[3]);
                    int woundTotal = reader.GetInt32(4);
                    int weeksOfHealing = reader.GetInt32(5);
                    HitLocation hitLocation =
                        new HitLocation(hitLocationTemplateMap[hitLocationTemplateId],
                                        isCybernetic, armor, (uint)woundTotal, (uint)weeksOfHealing);

                    if (!hitLocationMap.ContainsKey(soldierId))
                    {
                        hitLocationMap[soldierId] = [];
                    }
                    hitLocationMap[soldierId].Add(hitLocation);
                }
            }
            return hitLocationMap;
        }

        private Dictionary<int, List<Skill>> GetSkillsBySoldierId(IDbConnection connection,
                                                                  IReadOnlyDictionary<int, BaseSkill> baseSkillMap)
        {
            Dictionary<int, List<Skill>> skillMap = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM SoldierSkill";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int soldierId = reader.GetInt32(0);
                    int baseSkillId = reader.GetInt32(1);
                    float points = Convert.ToSingle(reader[2]);
                    BaseSkill baseSkill = baseSkillMap[baseSkillId];

                    Skill skill = new Skill(baseSkill, points);

                    if (!skillMap.ContainsKey(soldierId))
                    {
                        skillMap[soldierId] = [];
                    }
                    skillMap[soldierId].Add(skill);
                }
            }
            return skillMap;
        }

        private Dictionary<int, Soldier> GetSoldiers(IDbConnection connection, 
                                                     IReadOnlyDictionary<int, SoldierTemplate> soldierTemplateMap,
                                                     IReadOnlyDictionary<int, Squad> squadMap,
                                                     IReadOnlyDictionary<int, List<Skill>> skillMap,
                                                     IReadOnlyDictionary<int, List<HitLocation>> hitLocationMap)
        {
            Dictionary<int, Soldier> soldiers = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM Soldier";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    int soldierTemplateId = reader.GetInt32(1);
                    int? squadId = reader.IsDBNull(2) ? null : reader.GetInt32(2);
                    string name = reader[3].ToString();
                    float strength = Convert.ToSingle(reader[4]);
                    float dexterity = Convert.ToSingle(reader[5]);
                    float constitution = Convert.ToSingle(reader[6]);
                    float intelligence = Convert.ToSingle(reader[7]);
                    float perception = Convert.ToSingle(reader[8]);
                    float ego = Convert.ToSingle(reader[9]);
                    float charisma = Convert.ToSingle(reader[10]);
                    float psychic = Convert.ToSingle(reader[11]);
                    float attack = Convert.ToSingle(reader[12]);
                    float size = Convert.ToSingle(reader[13]);
                    float move = Convert.ToSingle(reader[14]);


                    Soldier soldier = new Soldier(hitLocationMap[id], skillMap[id])
                    {
                        Strength = strength,
                        Dexterity = dexterity,
                        Constitution = constitution,
                        Intelligence = intelligence,
                        Perception = perception,
                        Ego = ego,
                        Charisma = charisma,
                        PsychicPower = psychic,
                        AttackSpeed = attack,
                        Size = size,
                        MoveSpeed = move,
                        Id = id,
                        Name = name,
                        Template = soldierTemplateMap[soldierTemplateId]
                    };

                    // due to how we handle decorating with PlayerSoldier, we may need to adjust this.
                    // Fallen brothers have no squad; they stay squad-less and are routed into
                    // Army.FallenBrothers after decoration.
                    if (squadId.HasValue)
                    {
                        squadMap[squadId.Value].AddSquadMember(soldier);
                        soldier.AssignedSquad = squadMap[squadId.Value];
                    }
                    soldiers[id] = soldier;
                }
            }
            return soldiers;
        }
    }
}
