using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using System.Collections.Generic;
using System.Data;

namespace OnlyWar.Helpers.Database.GameState
{
    class PlayerSoldierDataAccess
    {
        public Dictionary<int, PlayerSoldier> GetData(IDbConnection dbCon, 
                                                      Dictionary<int, Soldier> soldierMap)
        {
            var factionCasualtyMap = GetFactionCasualtiesBySoldierId(dbCon);
            var rangedWeaponCasualtyMap = GetRangedWeaponCasualtiesBySoldierId(dbCon);
            var meleeWeaponCasualtyMap = GetMeleeWeaponCasualtiesBySoldierId(dbCon);
            var historyMap = GetHistoryBySoldierId(dbCon);
            var evaluationMap = GetEvaluationsBySoldierId(dbCon);
            var playerSoldiers = GetPlayerSoldiers(dbCon, soldierMap, factionCasualtyMap, rangedWeaponCasualtyMap, 
                                                   meleeWeaponCasualtyMap, historyMap, evaluationMap);
            return playerSoldiers;
        }

        public void SavePlayerSoldier(IDbTransaction transaction, PlayerSoldier playerSoldier)
        {
            string insert = $@"INSERT INTO PlayerSoldier VALUES ({playerSoldier.Id}, 
                {playerSoldier.ProgenoidImplantDate.Millenium},
                {playerSoldier.ProgenoidImplantDate.Year},{playerSoldier.ProgenoidImplantDate.Week});";
            using (var command = transaction.Connection.CreateCommand())
            {
                command.CommandText = insert;
                command.ExecuteNonQuery();
            }

            foreach (KeyValuePair<int, ushort> weaponCasualtyCount in playerSoldier.RangedWeaponCasualtyCountMap)
            {
                insert = $@"INSERT INTO PlayerSoldierRamgedWeaponCasualtyCount VALUES ({playerSoldier.Id}, 
                    {weaponCasualtyCount.Key}, {weaponCasualtyCount.Value});";
                using (var command = transaction.Connection.CreateCommand())
                {
                    command.CommandText = insert;
                    command.ExecuteNonQuery();
                }
            }

            foreach (KeyValuePair<int, ushort> weaponCasualtyCount in playerSoldier.MeleeWeaponCasualtyCountMap)
            {
                insert = $@"INSERT INTO PlayerSoldierMeleeWeaponCasualtyCount VALUES ({playerSoldier.Id}, 
                    {weaponCasualtyCount.Key}, {weaponCasualtyCount.Value});";
                using (var command = transaction.Connection.CreateCommand())
                {
                    command.CommandText = insert;
                    command.ExecuteNonQuery();
                }
            }

            foreach (KeyValuePair<int, ushort> factionCasualtyCount in playerSoldier.FactionCasualtyCountMap)
            {
                insert = $@"INSERT INTO PlayerSoldierFactionCasualtyCount VALUES ({playerSoldier.Id}, 
                    {factionCasualtyCount.Key}, {factionCasualtyCount.Value});";
                using (var command = transaction.Connection.CreateCommand())
                {
                    command.CommandText = insert;
                    command.ExecuteNonQuery();
                }
            }

            foreach (string entry in playerSoldier.SoldierHistory)
            {
                string safeEntry = entry.Replace("\'", "\'\'");
                insert = $@"INSERT INTO PlayerSoldierHistory VALUES ({playerSoldier.Id}, '{safeEntry}');";
                using (var command = transaction.Connection.CreateCommand())
                {
                    command.CommandText = insert;
                    command.ExecuteNonQuery();
                }
            }

            foreach(SoldierEvaluation evaluation in playerSoldier.SoldierEvaluationHistory)
            {
                insert = $@"INSERT INTO SoldierEvaluation VALUES ({playerSoldier.Id}, 
                {evaluation.EvaluationDate.Millenium}, {evaluation.EvaluationDate.Year}, {evaluation.EvaluationDate.Week}
                {evaluation.MeleeRating}, {evaluation.RangedRating}, {evaluation.LeadershipRating},
                {evaluation.MedicalRating}, {evaluation.TechRating}, {evaluation.PietyRating}, {evaluation.AncientRating})";
                using (var command = transaction.Connection.CreateCommand())
                {
                    command.CommandText = insert;
                    command.ExecuteNonQuery();
                }
            }
        }

        private Dictionary<int, Dictionary<int, ushort>> GetFactionCasualtiesBySoldierId(IDbConnection connection)
        {
            Dictionary<int, Dictionary<int, ushort>> soldierFactionCasualtyMap = 
                [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM PlayerSoldierFactionCasualtyCount";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int soldierId = reader.GetInt32(0);
                    int factionId = reader.GetInt32(1);
                    ushort count = (ushort)reader.GetInt32(2);

                    if (!soldierFactionCasualtyMap.ContainsKey(soldierId))
                    {
                        soldierFactionCasualtyMap[soldierId] = [];
                    }
                    soldierFactionCasualtyMap[soldierId][factionId] = count;

                }
            }
            return soldierFactionCasualtyMap;
        }

        private Dictionary<int, Dictionary<int, ushort>> GetRangedWeaponCasualtiesBySoldierId(IDbConnection connection)
        {
            Dictionary<int, Dictionary<int, ushort>> soldierWeaponCasualtyMap =
                [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM PlayerSoldierRangedWeaponCasualtyCount";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int soldierId = reader.GetInt32(0);
                    int weaponTemplateId = reader.GetInt32(1);
                    ushort count = (ushort)reader.GetInt32(2);

                    if (!soldierWeaponCasualtyMap.ContainsKey(soldierId))
                    {
                        soldierWeaponCasualtyMap[soldierId] = [];
                    }
                    soldierWeaponCasualtyMap[soldierId][weaponTemplateId] = count;

                }
            }
            return soldierWeaponCasualtyMap;
        }

        private Dictionary<int, Dictionary<int, ushort>> GetMeleeWeaponCasualtiesBySoldierId(IDbConnection connection)
        {
            Dictionary<int, Dictionary<int, ushort>> soldierWeaponCasualtyMap =
                [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM PlayerSoldierMeleeWeaponCasualtyCount";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int soldierId = reader.GetInt32(0);
                    int weaponTemplateId = reader.GetInt32(1);
                    ushort count = (ushort)reader.GetInt32(2);

                    if (!soldierWeaponCasualtyMap.ContainsKey(soldierId))
                    {
                        soldierWeaponCasualtyMap[soldierId] = [];
                    }
                    soldierWeaponCasualtyMap[soldierId][weaponTemplateId] = count;

                }
            }
            return soldierWeaponCasualtyMap;
        }

        private Dictionary<int, List<string>> GetHistoryBySoldierId(IDbConnection connection)
        {
            Dictionary<int, List<string>> soldierEntryListMap = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM PlayerSoldierHistory";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int soldierId = reader.GetInt32(0);
                    string entry = reader[1].ToString();

                    if (!soldierEntryListMap.ContainsKey(soldierId))
                    {
                        soldierEntryListMap[soldierId] = [];
                    }
                    soldierEntryListMap[soldierId].Add(entry);

                }
            }
            return soldierEntryListMap;
        }

        private Dictionary<int, List<SoldierEvaluation>> GetEvaluationsBySoldierId(IDbConnection connection)
        {
            Dictionary<int, List<SoldierEvaluation>> soldierEvalListMap = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM SoldierEvaluation";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int soldierId = reader.GetInt32(0);
                    int millenium = reader.GetInt32(1);
                    int year = reader.GetInt32(2);
                    int week = reader.GetInt32(3);

                    Date date = new Date(millenium, year, week);
                    float melee = reader.GetFloat(4);
                    float ranged = reader.GetFloat(5);
                    float leadership = reader.GetFloat(6);
                    float medical = reader.GetFloat(7);
                    float tech = reader.GetFloat(8);
                    float piety = reader.GetFloat(9);
                    float ancient = reader.GetFloat(10);

                    SoldierEvaluation entry = new SoldierEvaluation(date, melee, ranged, leadership, medical, tech, piety, ancient);

                    if (!soldierEvalListMap.ContainsKey(soldierId))
                    {
                        soldierEvalListMap[soldierId] = [];
                    }
                    soldierEvalListMap[soldierId].Add(entry);
                }
            }
            return soldierEvalListMap;
        }

        private Dictionary<int, PlayerSoldier> GetPlayerSoldiers(IDbConnection connection,
                                                                 IReadOnlyDictionary<int, Soldier> baseSoldierMap,
                                                                 IReadOnlyDictionary<int, Dictionary<int, ushort>> factionCasualtyMap,
                                                                 IReadOnlyDictionary<int, Dictionary<int, ushort>> rangedWeaponCasualtyMap,
                                                                 IReadOnlyDictionary<int, Dictionary<int, ushort>> meleeWeaponCasualtyMap,
                                                                 IReadOnlyDictionary<int, List<string>> historyMap,
                                                                 IReadOnlyDictionary<int, List<SoldierEvaluation>> evaluationMap)
        {
            Dictionary<int, PlayerSoldier> playerSoldierMap = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM PlayerSoldier";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int soldierId = reader.GetInt32(0);
                    int implantMillenium = reader.GetInt32(1);
                    int implantYear = reader.GetInt32(2);
                    int implantWeek = reader.GetInt32(3);

                    Date implantDate = new Date(implantMillenium, implantYear, implantWeek);

                    List<string> history;
                    if (historyMap.ContainsKey(soldierId))
                    {
                        history = historyMap[soldierId];
                    }
                    else
                    {
                        history = [];
                    }

                    List<SoldierEvaluation> evals;
                    if(evaluationMap.ContainsKey(soldierId))
                    {
                        evals = evaluationMap[soldierId];
                    }
                    else
                    {
                        evals = [];
                    }

                    Dictionary<int, ushort> rangedWeaponCasualties;
                    if (rangedWeaponCasualtyMap.ContainsKey(soldierId))
                    {
                        rangedWeaponCasualties = rangedWeaponCasualtyMap[soldierId];
                    }
                    else
                    {
                        rangedWeaponCasualties = [];
                    }

                    Dictionary<int, ushort> meleeWeaponCasualties;
                    if (meleeWeaponCasualtyMap.ContainsKey(soldierId))
                    {
                        meleeWeaponCasualties = meleeWeaponCasualtyMap[soldierId];
                    }
                    else
                    {
                        meleeWeaponCasualties = [];
                    }

                    Dictionary<int, ushort> factionCasualties;
                    if (factionCasualtyMap.ContainsKey(soldierId))
                    {
                        factionCasualties = factionCasualtyMap[soldierId];
                    }
                    else
                    {
                        factionCasualties = [];
                    }

                    PlayerSoldier playerSoldier = new PlayerSoldier(baseSoldierMap[soldierId], evals,
                                                                    implantDate, history, rangedWeaponCasualties,
                                                                    meleeWeaponCasualties, factionCasualties);

                    playerSoldierMap[soldierId] = playerSoldier;

                }
            }
            return playerSoldierMap;
        }
    }
}
