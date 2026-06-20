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
            var awardMap = GetAwardsBySoldierId(dbCon);
            var playerSoldiers = GetPlayerSoldiers(dbCon, soldierMap, factionCasualtyMap, rangedWeaponCasualtyMap, 
                                                   meleeWeaponCasualtyMap, historyMap, evaluationMap, awardMap);
            return playerSoldiers;
        }

        public void SavePlayerSoldier(IDbTransaction transaction, PlayerSoldier playerSoldier)
        {
            using (var command = transaction.Connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO PlayerSoldier VALUES
                    (@id, @millenium, @year, @week);";
                command.AddParam("@id", playerSoldier.Id);
                command.AddParam("@millenium", playerSoldier.ProgenoidImplantDate.Millenium);
                command.AddParam("@year", playerSoldier.ProgenoidImplantDate.Year);
                command.AddParam("@week", playerSoldier.ProgenoidImplantDate.Week);
                command.ExecuteNonQuery();
            }

            foreach (KeyValuePair<int, ushort> weaponCasualtyCount in playerSoldier.RangedWeaponCasualtyCountMap)
            {
                using (var command = transaction.Connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"INSERT INTO PlayerSoldierRangedWeaponCasualtyCount VALUES
                        (@soldierId, @weaponTemplateId, @count);";
                    command.AddParam("@soldierId", playerSoldier.Id);
                    command.AddParam("@weaponTemplateId", weaponCasualtyCount.Key);
                    command.AddParam("@count", weaponCasualtyCount.Value);
                    command.ExecuteNonQuery();
                }
            }

            foreach (KeyValuePair<int, ushort> weaponCasualtyCount in playerSoldier.MeleeWeaponCasualtyCountMap)
            {
                using (var command = transaction.Connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"INSERT INTO PlayerSoldierMeleeWeaponCasualtyCount VALUES
                        (@soldierId, @weaponTemplateId, @count);";
                    command.AddParam("@soldierId", playerSoldier.Id);
                    command.AddParam("@weaponTemplateId", weaponCasualtyCount.Key);
                    command.AddParam("@count", weaponCasualtyCount.Value);
                    command.ExecuteNonQuery();
                }
            }

            foreach (KeyValuePair<int, ushort> factionCasualtyCount in playerSoldier.FactionCasualtyCountMap)
            {
                using (var command = transaction.Connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"INSERT INTO PlayerSoldierFactionCasualtyCount VALUES
                        (@soldierId, @factionId, @count);";
                    command.AddParam("@soldierId", playerSoldier.Id);
                    command.AddParam("@factionId", factionCasualtyCount.Key);
                    command.AddParam("@count", factionCasualtyCount.Value);
                    command.ExecuteNonQuery();
                }
            }

            foreach (string entry in playerSoldier.SoldierHistory)
            {
                using (var command = transaction.Connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"INSERT INTO PlayerSoldierHistory VALUES
                        (@soldierId, @entry);";
                    command.AddParam("@soldierId", playerSoldier.Id);
                    command.AddParam("@entry", entry);
                    command.ExecuteNonQuery();
                }
            }

            foreach(SoldierEvaluation evaluation in playerSoldier.SoldierEvaluationHistory)
            {
                using (var command = transaction.Connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"INSERT INTO SoldierEvaluation VALUES
                        (@soldierId, @millenium, @year, @week);";
                    command.AddParam("@soldierId", playerSoldier.Id);
                    command.AddParam("@millenium", evaluation.EvaluationDate.Millenium);
                    command.AddParam("@year", evaluation.EvaluationDate.Year);
                    command.AddParam("@week", evaluation.EvaluationDate.Week);
                    command.ExecuteNonQuery();
                }

                foreach (KeyValuePair<string, float> rating in evaluation.Ratings)
                {
                    using var ratingCommand = transaction.Connection.CreateCommand();
                    ratingCommand.Transaction = transaction;
                    ratingCommand.CommandText = @"INSERT INTO SoldierEvaluationRating VALUES
                        (@soldierId, @millenium, @year, @week, @ratingKey, @value);";
                    ratingCommand.AddParam("@soldierId", playerSoldier.Id);
                    ratingCommand.AddParam("@millenium", evaluation.EvaluationDate.Millenium);
                    ratingCommand.AddParam("@year", evaluation.EvaluationDate.Year);
                    ratingCommand.AddParam("@week", evaluation.EvaluationDate.Week);
                    ratingCommand.AddParam("@ratingKey", rating.Key);
                    ratingCommand.AddParam("@value", rating.Value);
                    ratingCommand.ExecuteNonQuery();
                }
            }

            foreach (SoldierAward award in playerSoldier.SoldierAwards)
            {
                using (var command = transaction.Connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"INSERT INTO SoldierAward VALUES
                        (@soldierId, @millenium, @year, @week, @name, @type, @level);";
                    command.AddParam("@soldierId", playerSoldier.Id);
                    command.AddParam("@millenium", award.DateAwarded.Millenium);
                    command.AddParam("@year", award.DateAwarded.Year);
                    command.AddParam("@week", award.DateAwarded.Week);
                    command.AddParam("@name", award.Name);
                    command.AddParam("@type", award.Type);
                    command.AddParam("@level", award.Level);
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
            Dictionary<(int, int, int, int), Dictionary<string, float>> ratingsByEvaluation =
                GetEvaluationRatings(connection);

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
                    Dictionary<string, float> ratings =
                        ratingsByEvaluation.TryGetValue((soldierId, millenium, year, week), out var r)
                            ? r : [];

                    SoldierEvaluation entry = new SoldierEvaluation(date, ratings);

                    if (!soldierEvalListMap.ContainsKey(soldierId))
                    {
                        soldierEvalListMap[soldierId] = [];
                    }
                    soldierEvalListMap[soldierId].Add(entry);
                }
            }
            return soldierEvalListMap;
        }

        private Dictionary<(int, int, int, int), Dictionary<string, float>> GetEvaluationRatings(IDbConnection connection)
        {
            Dictionary<(int, int, int, int), Dictionary<string, float>> map = [];
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT SoldierId, Millenium, Year, Week, RatingKey, Value FROM SoldierEvaluationRating";
            var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var key = (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
                string ratingKey = reader.GetString(4);
                float value = System.Convert.ToSingle(reader[5]);
                if (!map.TryGetValue(key, out var ratings))
                {
                    ratings = [];
                    map[key] = ratings;
                }
                ratings[ratingKey] = value;
            }
            return map;
        }

        private Dictionary<int, List<SoldierAward>> GetAwardsBySoldierId(IDbConnection connection)
        {
            Dictionary<int, List<SoldierAward>> soldierAwardListMap = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM SoldierAward";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int soldierId = reader.GetInt32(0);
                    int millenium = reader.GetInt32(1);
                    int year = reader.GetInt32(2);
                    int week = reader.GetInt32(3);

                    Date date = new Date(millenium, year, week);
                    string name = reader.GetString(4);
                    string type = reader.GetString(5);
                    ushort level = (ushort)reader.GetInt16(6);

                    SoldierAward entry = new SoldierAward(date, name, type, level);

                    if (!soldierAwardListMap.ContainsKey(soldierId))
                    {
                        soldierAwardListMap[soldierId] = [];
                    }
                    soldierAwardListMap[soldierId].Add(entry);
                }
            }
            return soldierAwardListMap;
        }

        private Dictionary<int, PlayerSoldier> GetPlayerSoldiers(IDbConnection connection,
                                                                 IReadOnlyDictionary<int, Soldier> baseSoldierMap,
                                                                 IReadOnlyDictionary<int, Dictionary<int, ushort>> factionCasualtyMap,
                                                                 IReadOnlyDictionary<int, Dictionary<int, ushort>> rangedWeaponCasualtyMap,
                                                                 IReadOnlyDictionary<int, Dictionary<int, ushort>> meleeWeaponCasualtyMap,
                                                                 IReadOnlyDictionary<int, List<string>> historyMap,
                                                                 IReadOnlyDictionary<int, List<SoldierEvaluation>> evaluationMap,
                                                                 IReadOnlyDictionary<int, List<SoldierAward>> awardMap)
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

                    List<SoldierAward> awards;
                    if(awardMap.ContainsKey(soldierId))
                    {
                        awards = awardMap[soldierId];
                    }
                    else
                    {
                        awards = [];
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

                    PlayerSoldier playerSoldier = new PlayerSoldier(baseSoldierMap[soldierId], evals, awards,
                                                                    implantDate, history, rangedWeaponCasualties,
                                                                    meleeWeaponCasualties, factionCasualties);

                    playerSoldierMap[soldierId] = playerSoldier;

                }
            }
            return playerSoldierMap;
        }
    }
}
