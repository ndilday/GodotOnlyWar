﻿using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace OnlyWar.Helpers.Database.GameRules
{
    public class SquadTemplateDataBlob
    {
        public Dictionary<int, ArmorTemplate> ArmorTemplates { get; set; }
        public Dictionary<int, RangedWeaponTemplate> RangedWeaponTemplateMap { get; set; }
        public Dictionary<int, MeleeWeaponTemplate> MeleeWeaponTemplateMap { get; set; }
        public Dictionary<int, WeaponSet> WeaponSetMap { get; set; }
        public Dictionary<int, List<SoldierTemplate>> SoldierTemplatesByFactionId { get; set; }
        public Dictionary<int, List<Species>> SpeciesByFactionId { get; set; }
        public Dictionary<int, List<SquadTemplate>> SquadTemplatesByFactionId { get; set; }
        public Dictionary<int, SquadTemplate> SquadTemplatesById { get; set; }
    }

    public class SquadTemplateDataAccess
    {
        public SquadTemplateDataBlob GetSquadDataBlob(IDbConnection connection, 
                                              Dictionary<int, BaseSkill> baseSkillMap,
                                              Dictionary<int, List<HitLocationTemplate>> hitLocationMap)
        {
            var attributes = GetAttributeTemplates(connection);
            var soldierTemplateSkills = GetSoldierMosTrainingBySoldierTemplateId(connection, baseSkillMap);
            var species = GetSpeciesByFactionId(connection, attributes, hitLocationMap);
            var soldierTemplates = 
                GetSoldierTemplatesByFactionId(connection, soldierTemplateSkills, species);
            var armorTemplates = GetArmorTemplates(connection);
            var meleeWeapons = GetMeleeWeaponTemplates(connection, baseSkillMap);
            var rangedWeapons = GetRangedWeaponTemplates(connection, baseSkillMap);
            var weaponSets = GetWeaponSetMap(connection, meleeWeapons, rangedWeapons);
            var squadTemplateWeaponSetIds = 
                GetSquadTemplateWeaponSetIdsBySquadTemplateWeaponOptionId(connection);
            var squadWeaponOptions = 
                GetSquadWeaponOptionsBySquadTemplateId(connection, 
                                                       squadTemplateWeaponSetIds, 
                                                       weaponSets);
            var basicSoldierTemplateMap = soldierTemplates.Values
                                                          .SelectMany(st => st)
                                                          .ToDictionary(st => st.Id);
            var squadElements = GetSquadTemplateElementsBySquadId(connection, basicSoldierTemplateMap);
            var squadTemplates = GetSquadTemplatesById(connection, squadElements, weaponSets, 
                                                       squadWeaponOptions, armorTemplates);
            return new SquadTemplateDataBlob
            {
                ArmorTemplates = armorTemplates,
                MeleeWeaponTemplateMap= meleeWeapons,
                RangedWeaponTemplateMap = rangedWeapons,
                WeaponSetMap = weaponSets,
                SquadTemplatesByFactionId = squadTemplates.Item1,
                SquadTemplatesById = squadTemplates.Item2,
                SoldierTemplatesByFactionId = soldierTemplates,
                SpeciesByFactionId = species
            };
        }

        private Dictionary<int, List<Tuple<BaseSkill, float>>> GetSoldierMosTrainingBySoldierTemplateId(
            IDbConnection connection, Dictionary<int, BaseSkill> baseSkillMap)
        {
            Dictionary<int, List<Tuple<BaseSkill, float>>> soldierTemplateMosMap =
                [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM SoldierMosTraining";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int soldierTemplateId = reader.GetInt32(0);
                    int baseSkillId = reader.GetInt32(1);
                    float points = Convert.ToSingle(reader[2]);

                    BaseSkill baseSkill = baseSkillMap[baseSkillId];

                    Tuple<BaseSkill, float> training = new Tuple<BaseSkill, float>(baseSkill, points);

                    if (!soldierTemplateMosMap.ContainsKey(soldierTemplateId))
                    {
                        soldierTemplateMosMap[soldierTemplateId] = [];
                    }
                    soldierTemplateMosMap[soldierTemplateId].Add(training);
                }
            }
            return soldierTemplateMosMap;
        }


        private Dictionary<int, ArmorTemplate> GetArmorTemplates(IDbConnection connection)
        {
            Dictionary<int, ArmorTemplate> armorTemplateMap = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM ArmorTemplate";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    //int factionId = reader.GetInt32(1);
                    string name = reader[2].ToString();
                    //int location = reader.GetInt32(3);
                    int armorProvided = reader.GetInt32(4);
                    int stealthMod = reader.GetInt32(5);
                    ArmorTemplate armorTemplate = new ArmorTemplate(id, name, (byte)armorProvided, 
                                                                    (short)stealthMod);
                    armorTemplateMap[id] = armorTemplate;
                }
            }
            return armorTemplateMap;
        }

        private Dictionary<int, MeleeWeaponTemplate> GetMeleeWeaponTemplates(
            IDbConnection connection,
            Dictionary<int, BaseSkill> baseSkillMap)
        {
            Dictionary<int, MeleeWeaponTemplate> factionWeaponTemplateMap =
                [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM MeleeWeaponTemplate";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    string name = reader[1].ToString();
                    int location = reader.GetInt32(2);
                    int baseSkillId = reader.GetInt32(3);
                    float accuracy = Convert.ToSingle(reader[4]);
                    float armorMultiplier = Convert.ToSingle(reader[5]);
                    float woundMultiplier = Convert.ToSingle(reader[6]);
                    float requiredStrength = Convert.ToSingle(reader[7]);
                    float strengthMultiplier = Convert.ToSingle(reader[8]);
                    float extraDamage = Convert.ToSingle(reader[9]);
                    float parryMod = Convert.ToSingle(reader[10]);
                    float extraAttacks = Convert.ToSingle(reader[11]);

                    BaseSkill baseSkill = baseSkillMap[baseSkillId];

                    MeleeWeaponTemplate weaponTemplate =
                        new MeleeWeaponTemplate(id, name, (EquipLocation)location, baseSkill,
                                                accuracy, armorMultiplier, woundMultiplier,
                                                requiredStrength, strengthMultiplier, extraDamage,
                                                parryMod, extraAttacks);
                    factionWeaponTemplateMap[id] = weaponTemplate;
                }
            }
            return factionWeaponTemplateMap;
        }

        private Dictionary<int, RangedWeaponTemplate> GetRangedWeaponTemplates(
            IDbConnection connection,
            Dictionary<int, BaseSkill> baseSkillMap)
        {
            Dictionary<int, RangedWeaponTemplate> factionWeaponTemplateMap =
                [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM RangedWeaponTemplate";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    string name = reader[1].ToString();
                    int location = reader.GetInt32(2);
                    int baseSkillId = reader.GetInt32(3);
                    float accuracy = Convert.ToSingle(reader[4]);
                    float armorMultiplier = Convert.ToSingle(reader[5]);
                    float woundMultiplier = Convert.ToSingle(reader[6]);
                    float requiredStrength = Convert.ToSingle(reader[7]);
                    float damageMultiplier = Convert.ToSingle(reader[8]);
                    float maxRange = Convert.ToSingle(reader[9]);
                    byte rof = reader.GetByte(10);
                    ushort ammo = (ushort)reader.GetInt16(11);
                    ushort recoil = (ushort)reader.GetInt16(12);
                    ushort bulk = (ushort)reader.GetInt16(13);
                    bool doesDamageDegrade = Convert.ToBoolean(reader[14]);
                    ushort reloadTime = (ushort)reader.GetInt16(15);

                    BaseSkill baseSkill = baseSkillMap[baseSkillId];

                    RangedWeaponTemplate weaponTemplate =
                        new RangedWeaponTemplate(id, name, (EquipLocation)location, baseSkill,
                                                accuracy, armorMultiplier, woundMultiplier,
                                                requiredStrength, damageMultiplier, maxRange,
                                                rof, ammo, recoil, bulk, doesDamageDegrade, reloadTime);
                    factionWeaponTemplateMap[id] = weaponTemplate;
                }
            }
            return factionWeaponTemplateMap;
        }

        private Dictionary<int, WeaponSet> GetWeaponSetMap(
            IDbConnection connection,
            Dictionary<int, MeleeWeaponTemplate> meleeWeaponMap,
            Dictionary<int, RangedWeaponTemplate> rangedWeaponMap)
        {
            RangedWeaponTemplate primaryRanged, secondaryRanged;
            MeleeWeaponTemplate primaryMelee, secondaryMelee;
            Dictionary<int, WeaponSet> weaponSetMap = [];

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM WeaponSet";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    //int factionId = reader.GetInt32(1);
                    string name = reader[2].ToString();

                    if (reader[3].GetType() != typeof(DBNull))
                    {
                        primaryRanged = rangedWeaponMap[reader.GetInt32(3)];
                    }
                    else
                    {
                        primaryRanged = null;
                    }

                    if (reader[4].GetType() != typeof(DBNull))
                    {
                        secondaryRanged = rangedWeaponMap[reader.GetInt32(4)];
                    }
                    else
                    {
                        secondaryRanged = null;
                    }

                    if (reader[5].GetType() != typeof(DBNull))
                    {
                        primaryMelee = meleeWeaponMap[reader.GetInt32(5)];
                    }
                    else
                    {
                        primaryMelee = null;
                    }

                    if (reader[6].GetType() != typeof(DBNull))
                    {
                        secondaryMelee = meleeWeaponMap[reader.GetInt32(6)];
                    }
                    else
                    {
                        secondaryMelee = null;
                    }

                    WeaponSet weaponSet = new WeaponSet(id, name, primaryRanged, secondaryRanged,
                                                        primaryMelee, secondaryMelee);
                    weaponSetMap[id] = weaponSet;
                }
            }
            return weaponSetMap;
        }

        private Dictionary<int, List<int>> GetSquadTemplateWeaponSetIdsBySquadTemplateWeaponOptionId(IDbConnection connection)
        {
            Dictionary<int, List<int>> weaponOptionToWeaponSetMap = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM SquadTemplateWeaponOptionWeaponSet";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int weaponSetId = reader.GetInt32(1);
                    int squadTemplateWeaponOption = reader.GetInt32(2);
                    if (!weaponOptionToWeaponSetMap.ContainsKey(squadTemplateWeaponOption))
                    {
                        weaponOptionToWeaponSetMap[squadTemplateWeaponOption] = [];
                    }
                    weaponOptionToWeaponSetMap[squadTemplateWeaponOption].Add(weaponSetId);
                }
            }
            return weaponOptionToWeaponSetMap;
        }

        private Dictionary<int, List<SquadWeaponOption>> GetSquadWeaponOptionsBySquadTemplateId(
            IDbConnection connection,
            Dictionary<int, List<int>> weaponOptionWeaponSetMap,
            Dictionary<int, WeaponSet> weaponSetMap)
        {
            Dictionary<int, List<SquadWeaponOption>> squadWeaponOptionMap =
                [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM SquadTemplateWeaponOption";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    int squadTemplateId = reader.GetInt32(1);
                    string name = reader[2].ToString();
                    int min = reader.GetInt32(3);
                    int max = reader.GetInt32(4);

                    List<int> baseList = weaponOptionWeaponSetMap[id];
                    List<WeaponSet> weaponSetList = [];
                    foreach (int weaponSetId in baseList)
                    {
                        weaponSetList.Add(weaponSetMap[weaponSetId]);
                    }

                    SquadWeaponOption weaponOption = new SquadWeaponOption(name, min, max, weaponSetList);
                    if (!squadWeaponOptionMap.ContainsKey(squadTemplateId))
                    {
                        squadWeaponOptionMap[squadTemplateId] = [];
                    }
                    squadWeaponOptionMap[squadTemplateId].Add(weaponOption);
                }
            }
            return squadWeaponOptionMap;
        }

        private Dictionary<int, List<SquadTemplateElement>> GetSquadTemplateElementsBySquadId(IDbConnection connection,
                                                                                              Dictionary<int, SoldierTemplate> soldierTemplateMap)
        {
            Dictionary<int, List<SquadTemplateElement>> elementsMap =
                [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM SquadTemplateElement";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int squadTemplateId = reader.GetInt32(1);
                    int soldierTemplateId = reader.GetInt32(2);
                    int min = reader.GetInt32(3);
                    int max = reader.GetInt32(4);

                    SoldierTemplate template = soldierTemplateMap[soldierTemplateId];

                    if (!elementsMap.ContainsKey(squadTemplateId))
                    {
                        elementsMap[squadTemplateId] = [];
                    }
                    elementsMap[squadTemplateId].Add(new SquadTemplateElement(template, (byte)min, (byte)max));
                }
            }
            return elementsMap;
        }

        private Tuple<Dictionary<int, List<SquadTemplate>>, Dictionary<int, SquadTemplate>> GetSquadTemplatesById(
            IDbConnection connection,
            Dictionary<int, List<SquadTemplateElement>> elementMap,
            Dictionary<int, WeaponSet> weaponSetMap,
            Dictionary<int, List<SquadWeaponOption>> squadWeaponOptionMap,
            Dictionary<int, ArmorTemplate> armorTemplateMap)
        {
            Dictionary<int, SquadTemplate> squadTemplateMap = [];
            Dictionary<int, List<SquadTemplate>> squadTemplatesByFactionId = [];
            Dictionary<int, int> bodyguardMap = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM SquadTemplate";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    int factionId = reader.GetInt32(1);
                    string name = reader[2].ToString();
                    int defaultArmorId = reader.GetInt32(3);
                    int defaultWeaponSetId = reader.GetInt32(4);
                    int squadType = reader.GetInt32(5);
                    int battleValue = reader.GetInt32(6);
                    if (reader[7].GetType() != typeof(DBNull))
                    {
                        bodyguardMap[id] = reader.GetInt32(7);
                    }

                    ArmorTemplate defaultArmor = armorTemplateMap[defaultArmorId];
                    List<SquadWeaponOption> options = squadWeaponOptionMap.ContainsKey(id) ?
                        squadWeaponOptionMap[id] : null;
                    SquadTemplate squadTemplate = new SquadTemplate(id, 
                                                                    name, 
                                                                    weaponSetMap[defaultWeaponSetId],
                                                                    options, 
                                                                    defaultArmor,
                                                                    elementMap[id], 
                                                                    (SquadTypes)squadType,
                                                                    battleValue);
                    squadTemplateMap[id] = squadTemplate;
                    if (!squadTemplatesByFactionId.ContainsKey(factionId))
                    {
                        squadTemplatesByFactionId[factionId] = [];
                    }
                    squadTemplatesByFactionId[factionId].Add(squadTemplate);
                }
                foreach (KeyValuePair<int, int> kvp in bodyguardMap)
                {
                    squadTemplateMap[kvp.Key].BodyguardSquadTemplate = squadTemplateMap[kvp.Value];
                }
            }
            return new Tuple<Dictionary<int, List<SquadTemplate>>, Dictionary<int, SquadTemplate>>(squadTemplatesByFactionId, squadTemplateMap);
        }

        private Dictionary<int, NormalizedValueTemplate> GetAttributeTemplates(IDbConnection connection)
        {
            Dictionary<int, NormalizedValueTemplate> attributeTemplateMap =
                [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM AttributeTemplate";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    float baseValue = Convert.ToSingle(reader[1]);
                    float stdDev = Convert.ToSingle(reader[2]);
                    NormalizedValueTemplate attributeTemplate = new NormalizedValueTemplate
                    {
                        BaseValue = baseValue,
                        StandardDeviation = stdDev
                    };

                    attributeTemplateMap[id] = attributeTemplate;
                }
            }
            return attributeTemplateMap;
        }

        private Dictionary<int, List<Species>> GetSpeciesByFactionId(IDbConnection connection,
                                                                     Dictionary<int, NormalizedValueTemplate> attributeMap,
                                                                     Dictionary<int, List<HitLocationTemplate>> hitLocationTemplateMap)
        {
            Dictionary<int, List<Species>> speciesMap = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM Species";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    int factionId = reader.GetInt32(1);
                    int bodyId = reader.GetInt32(2);
                    string name = reader[3].ToString();
                    int strengthTemplateId = reader.GetInt32(4);
                    int dexterityTemplateId = reader.GetInt32(5);
                    int constitutionTemplateId = reader.GetInt32(6);
                    int intelligenceTemplateId = reader.GetInt32(7);
                    int perceptionTemplateId = reader.GetInt32(8);
                    int egoTemplateId = reader.GetInt32(9);
                    int charismaTemplateId = reader.GetInt32(10);
                    int psychicTemplateId = reader.GetInt32(11);
                    int attackSpeedTemplateId = reader.GetInt32(12);
                    int moveSpeedTemplateId = reader.GetInt32(13);
                    int sizeTemplateId = reader.GetInt32(14);
                    ushort width = (ushort)reader.GetInt16(15);
                    ushort depth = (ushort)reader.GetInt16(16);
                    Species species = new Species(id, name,
                                                  attributeMap[strengthTemplateId],
                                                  attributeMap[dexterityTemplateId],
                                                  attributeMap[constitutionTemplateId],
                                                  attributeMap[intelligenceTemplateId],
                                                  attributeMap[perceptionTemplateId],
                                                  attributeMap[egoTemplateId],
                                                  attributeMap[charismaTemplateId],
                                                  attributeMap[psychicTemplateId],
                                                  attributeMap[attackSpeedTemplateId],
                                                  attributeMap[moveSpeedTemplateId],
                                                  attributeMap[sizeTemplateId],
                                                  width,
                                                  depth,
                                                  new BodyTemplate(hitLocationTemplateMap[bodyId]));

                    if (!speciesMap.ContainsKey(factionId))
                    {
                        speciesMap[factionId] = [];
                    }
                    speciesMap[factionId].Add(species);
                }
            }
            return speciesMap;
        }

        private Dictionary<int, List<SoldierTemplate>> GetSoldierTemplatesByFactionId(
            IDbConnection connection,
            Dictionary<int, List<Tuple<BaseSkill, float>>> soldierTemplateTrainingMap,
            Dictionary<int, List<Species>> speciesMap)
        {
            Dictionary<int, List<SoldierTemplate>> soldierTemplatesByFactionId = 
                [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM SoldierTemplate";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    int factionId = reader.GetInt32(1);
                    int speciesId = reader.GetInt32(2);
                    string name = reader[3].ToString();
                    int rank = reader.GetInt32(4);
                    int subrank = reader.GetInt32(5);
                    bool isSquadLeader = Convert.ToBoolean(reader[6]);
                    int specialistType = reader.GetInt32(7);
                    List<Tuple<BaseSkill, float>> trainingList = null;
                    if (soldierTemplateTrainingMap.ContainsKey(id))
                    {
                        trainingList = soldierTemplateTrainingMap[id];
                    }
                    var species = speciesMap[factionId].First(s => s.Id == speciesId);
                    SoldierTemplate soldierTemplate =
                        new SoldierTemplate(id, species, name, (byte)rank, (byte)subrank,
                                            isSquadLeader, (byte)specialistType, trainingList);

                    if (!soldierTemplatesByFactionId.ContainsKey(factionId))
                    {
                        soldierTemplatesByFactionId[factionId] = [];
                    }
                    soldierTemplatesByFactionId[factionId].Add(soldierTemplate);
                }
            }
            return soldierTemplatesByFactionId;
        }

    }
}
