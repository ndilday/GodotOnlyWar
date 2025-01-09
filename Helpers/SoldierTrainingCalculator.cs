using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    public interface ISoldierTrainingService
    {
        public void UpdateRatings(Date date, PlayerSoldier soldier);
        public void EvaluateSoldier(PlayerSoldier soldier, Date trainingFinishedYear);
        public void AwardSoldier(PlayerSoldier soldier, Date awardDate, string awardName, string type, ushort level);
        public void ApplySoldierWorkExperience(ISoldier soldier, float points);
        public void TrainScouts(IEnumerable<Squad> scoutSquads, Dictionary<int, TrainingFocuses> squadFocusMap);
    }

    [Flags]
    public enum TrainingFocuses
    {
        None = 0,
        Physical = 0x1,
        Vehicles = 0x2,
        Melee = 0x4,
        Ranged = 0x8
    }

    public class SoldierTrainingCalculator : ISoldierTrainingService
    {
        private readonly IReadOnlyDictionary<string, BaseSkill> _skillsByName;

        public SoldierTrainingCalculator(IEnumerable<BaseSkill> baseSkills)
        {
            _skillsByName = baseSkills.ToDictionary(bs => bs.Name);
        }

        public void UpdateRatings(Date date, PlayerSoldier soldier)
        {
            // Melee score = (STR * Melee)
            // Expected score = 16 * 16 * 15.5/8 = 1000
            // low-end = 15 * 15 * 14/8 = 850
            // high-end = 17 * 17 * 16/8 = 578
            float meleeRating = 
                soldier.Strength * soldier.GetTotalSkillValue(_skillsByName["Sword"]) 
                / (float)(RNG.GetDoubleInRange(1.44f, 1.76f) * RNG.GetDoubleInRange(1.44f, 1.76f));
            // marksman, sharpshooter, sniper
            // Ranged Score = PER * Ranged
            Skill bestRanged = soldier.GetBestSkillInCategory(SkillCategory.Ranged);
            float rangedRating =
                    (soldier.Dexterity + bestRanged.SkillBonus)
                    / (float)RNG.GetDoubleInRange(0.144f, 0.176f); 
            // Leadership Score = CHA * Leadership * Tactics
            float leadershipRating = soldier.Ego
                * soldier.GetTotalSkillValue(_skillsByName["Leadership"])
                * soldier.GetTotalSkillValue(_skillsByName["Tactics"])
                / (float)(RNG.GetDoubleInRange(12.6f, 15.4f) * RNG.GetDoubleInRange(1.26f, 1.54f) * RNG.GetDoubleInRange(1.26f, 1.54f));
            // Ancient Score = EGO * BOD
            float ancientRating = soldier.Ego * soldier.Constitution
                / (float)(RNG.GetDoubleInRange(1.26f, 1.54f) * RNG.GetDoubleInRange(2.88f, 3.52f));
            // Medical Score = INT * Medicine
            float medicalRating = 
                soldier.GetTotalSkillValue(_skillsByName["Diagnosis"])
                * soldier.GetTotalSkillValue(_skillsByName["First Aid"])
                / (float)(RNG.GetDoubleInRange(0.99f, 1.21f) * RNG.GetDoubleInRange(1.17f, 1.43f));
            // Tech Score =  INT * TechRapair
            float techRating = 
                soldier.GetTotalSkillValue(_skillsByName["Armory (Small Arms)"])
                * soldier.GetTotalSkillValue(_skillsByName["Armory (Vehicle)"])
                / (float)(RNG.GetDoubleInRange(1.17f, 1.43f) * RNG.GetDoubleInRange(1.17f, 1.43f));
            // Piety Score = Piety * Ritual * Persuade
            float pietyRating = 
                soldier.GetTotalSkillValue(_skillsByName["Theology (Emperor of Man)"])
                / (float)RNG.GetDoubleInRange(0.108f, 0.132f);

            SoldierEvaluation eval = new(date, meleeRating, rangedRating, leadershipRating, ancientRating, medicalRating, techRating, pietyRating);
            soldier.AddEvaluation(eval);
        }

        public void EvaluateSoldier(PlayerSoldier soldier, Date trainingFinishedYear)
        {
            UpdateRatings(trainingFinishedYear, soldier);
            SoldierEvaluation eval = soldier.SoldierEvaluationHistory.Last();

            if (eval.MeleeRating > 115) AwardSoldier(soldier, trainingFinishedYear, "Adamantium Sword of the Emperor", "Sword", 4);
            else if (eval.MeleeRating > 105) AwardSoldier(soldier, trainingFinishedYear, "Gold Sword of the Emperor", "Sword", 3);
            else if (eval.MeleeRating > 99) AwardSoldier(soldier, trainingFinishedYear, "Silver Sword of the Emperor", "Sword", 2);
            else if (eval.MeleeRating > 90) AwardSoldier(soldier, trainingFinishedYear, "Bronze Sword of the Emperor", "Sword", 1);

            if (eval.RangedRating > 120) AwardSoldier(soldier, trainingFinishedYear, $"Adamantium {soldier.GetBestSkillInCategory(SkillCategory.Ranged).BaseSkill.Name} of the Emperor", "Gun", 4);
            else if (eval.RangedRating > 115) AwardSoldier(soldier, trainingFinishedYear, $"Gold {soldier.GetBestSkillInCategory(SkillCategory.Ranged).BaseSkill.Name} of the Emperor", "Gun", 3);
            else if (eval.RangedRating > 110) AwardSoldier(soldier, trainingFinishedYear, $"Silver {soldier.GetBestSkillInCategory(SkillCategory.Ranged).BaseSkill.Name} of the Emperor", "Gun", 2);
            else if (eval.RangedRating > 105) AwardSoldier(soldier, trainingFinishedYear, $"Bronze {soldier.GetBestSkillInCategory(SkillCategory.Ranged).BaseSkill.Name} of the Emperor", "Gun", 1);

            if(eval.LeadershipRating > 95) AwardSoldier(soldier, trainingFinishedYear, "Adamantium Voice of the Emperor", "Voice", 4);
            else if (eval.LeadershipRating > 65) AwardSoldier(soldier, trainingFinishedYear, "Gold Voice of the Emperor", "Voice", 3);
            else if (eval.LeadershipRating > 55) AwardSoldier(soldier, trainingFinishedYear, "Silver Voice of the Emperor", "Voice", 2);
            else if (eval.LeadershipRating > 50) AwardSoldier(soldier, trainingFinishedYear, "Bronze Voice of the Emperor", "Voice", 1);

            if(eval.AncientRating > 112) AwardSoldier(soldier, trainingFinishedYear, "Admantium Banner of the Emperor", "Banner", 4);
            if (eval.AncientRating > 100) AwardSoldier(soldier, trainingFinishedYear, "Gold Banner of the Emperor", "Banner", 3);
            else if (eval.AncientRating > 95) AwardSoldier(soldier, trainingFinishedYear, "Silver Banner of the Emperor", "Banner", 2);
            else if (eval.AncientRating > 85) AwardSoldier(soldier, trainingFinishedYear, "Bronze Banner of the Emperor", "Banner", 1);

            if (eval.MedicalRating > 115) soldier.AddEntryToHistory(trainingFinishedYear.ToString() + ": Flagged for potential training as Apothecary");

            if (eval.TechRating > 80) soldier.AddEntryToHistory(trainingFinishedYear.ToString() + ": Flagged for potential training as Techmarine");

            if (eval.PietyRating > 50) soldier.AddEntryToHistory(trainingFinishedYear.ToString() + ": Awarded Devout badge and declared a Novice");
        }

        public void AwardSoldier(PlayerSoldier soldier, Date awardDate, string awardName, string type, ushort level)
        {
            if(!soldier.SoldierAwards.Any(a => a.Type == type && a.Level >= level))
            {
                soldier.AddEntryToHistory(awardDate.ToString() + ": Awarded " + awardName);
                soldier.AddAward(new SoldierAward(awardDate, awardName, type, level));
            }
        }

        public void ApplySoldierWorkExperience(ISoldier soldier, float points)
        {
            float powerArmorSkill = soldier.GetTotalSkillValue(_skillsByName["Power Armor"]);
            // if any gunnery, ranged, melee, or vehicle skill is below the PA skill, focus on improving PA
            float gunnerySkill = soldier.GetTotalSkillValue(soldier.GetBestSkillInCategory(SkillCategory.Gunnery).BaseSkill);
            float meleeSkill = soldier.GetTotalSkillValue(soldier.GetBestSkillInCategory(SkillCategory.Melee).BaseSkill);
            float rangedSkill = soldier.GetTotalSkillValue(soldier.GetBestSkillInCategory(SkillCategory.Ranged).BaseSkill);
            float vehicleSkill = soldier.GetTotalSkillValue(soldier.GetBestSkillInCategory(SkillCategory.Vehicle).BaseSkill);
            float[] floatArray = { gunnerySkill, meleeSkill, rangedSkill, vehicleSkill };
            float totalMax = floatArray.Max();
            if (totalMax > powerArmorSkill)
            {
                soldier.AddSkillPoints(_skillsByName["Power Armor"], points);
            }
            else
            {
                ApplyMarineWorkExperienceByType(soldier, points);
            }
        }

        public void ApplyMarineWorkExperienceByType(ISoldier soldier, float points)
        {
            switch(soldier.Template.Name)
            {
                case "Chapter Master":
                case "Captain":
                case "Sergeant":
                    break;
                case "Master of the Apothecarion":
                case "Apothecary":
                    break;
                case "Master of the Forge":
                case "Master Techmarine":
                case "TechMarine":
                    break;
                case "Master of the Librarium":
                case "Epistolary":
                case "Codiciers":
                case "Lexicanium":
                    break;
                case "Master of Sanctity":
                case "Reclusiarch":
                case "Chaplain":
                    break;
                case "Ancient":
                    break;
                case "Champion":
                    break;
                case "Veteran":
                    ApplyVeteranWorkExperience(soldier, points);
                    break;
                case "Tactical Marine":
                    ApplyTacticalWorkExperience(soldier, points);
                    break;
                case "Assault Marine":
                    ApplyAssaultWorkExperience(soldier, points);
                    break;
                case "Devastator Marine":
                    ApplyDevastatorWorkExperience(soldier, points);
                    break;
                case "Scout Marine":
                    // scouts are handled via the recruitment controller
                    break;
            }
        }

        public void ApplyVeteranWorkExperience(ISoldier soldier, float points)
        {
            float pointShare = points / 7.0f;
            soldier.AddSkillPoints(_skillsByName["Marine"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Power Armor"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Armory (Small Arms)"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Drive (Bike)"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Jump Pack"], pointShare);
            if (soldier.Template.IsSquadLeader)
            {
                soldier.AddSkillPoints(_skillsByName["Tactics"], pointShare);
                soldier.AddSkillPoints(_skillsByName["Leadership"], pointShare);
            }
            else
            {
                soldier.AddSkillPoints(_skillsByName["Gun (Bolter)"], pointShare);
                soldier.AddSkillPoints(_skillsByName["Sword"], pointShare);
            }
        }

        public void ApplyTacticalWorkExperience(ISoldier soldier, float points)
        {
            float pointShare = points / 9.0f;
            soldier.AddSkillPoints(_skillsByName["Marine"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Power Armor"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Armory (Small Arms)"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Gun (Bolter)"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Sword"], pointShare);

            if (soldier.Template.IsSquadLeader)
            {
                soldier.AddSkillPoints(_skillsByName["Tactics"], pointShare * 2);
                soldier.AddSkillPoints(_skillsByName["Leadership"], pointShare * 2);
            }
            else
            {
                soldier.AddSkillPoints(_skillsByName["Gunnery (Rocket)"], pointShare);
                soldier.AddSkillPoints(_skillsByName["Gunnery (Bolter)"], pointShare);
                soldier.AddSkillPoints(_skillsByName["Gun (Plasma)"], pointShare);
                soldier.AddSkillPoints(_skillsByName["Gun (Flamer)"], pointShare);
            }
        }

        public void ApplyAssaultWorkExperience(ISoldier soldier, float points)
        {
            float pointShare = points / 9.0f;
            soldier.AddSkillPoints(_skillsByName["Marine"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Power Armor"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Armory (Small Arms)"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Drive (Bike)"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Jump Pack"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Gun (Bolter)"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Sword"], pointShare);

            if (soldier.Template.IsSquadLeader)
            {
                soldier.AddSkillPoints(_skillsByName["Tactics"], pointShare);
                soldier.AddSkillPoints(_skillsByName["Leadership"], pointShare);
            }
            else
            {
                soldier.AddSkillPoints(_skillsByName["Gun (Bolter)"], pointShare);
                soldier.AddSkillPoints(_skillsByName["Sword"], pointShare);
            }
        }

        public void ApplyDevastatorWorkExperience(ISoldier soldier, float points)
        {
            float pointShare = points / 9.0f;
            soldier.AddSkillPoints(_skillsByName["Marine"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Power Armor"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Armory (Small Arms)"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Gun (Bolter)"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Gunnery (Bolter)"], pointShare);

            if (soldier.Template.IsSquadLeader)
            {
                soldier.AddSkillPoints(_skillsByName["Tactics"], pointShare * 2);
                soldier.AddSkillPoints(_skillsByName["Leadership"], pointShare * 2);
            }
            else
            {
                soldier.AddSkillPoints(_skillsByName["Gun (Plasma)"], pointShare);
                soldier.AddSkillPoints(_skillsByName["Gun (Flamer)"], pointShare);
                soldier.AddSkillPoints(_skillsByName["Gunnery (Rocket)"], pointShare);
                soldier.AddSkillPoints(_skillsByName["Gunnery (Laser)"], pointShare);
            }
        }

        public void ApplyScoutWorkExperience(ISoldier soldier, float points)
        {
            // scouts in reserve get training, not work experience
            float pointShare = points / 9.0f;
            soldier.AddSkillPoints(_skillsByName["Marine"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Power Armor"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Armory (Small Arms)"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Gun (Bolter)"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Gunnery (Bolter)"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Stealth"], pointShare);

            if (soldier.Template.IsSquadLeader)
            {
                soldier.AddSkillPoints(_skillsByName["Tactics"], pointShare);
                soldier.AddSkillPoints(_skillsByName["Leadership"], pointShare);
                soldier.AddSkillPoints(_skillsByName["Teaching"], pointShare);
            }
            else
            {
                soldier.AddSkillPoints(_skillsByName["Gun (Sniper)"], pointShare);
                soldier.AddSkillPoints(_skillsByName["Gun (Shotgun)"], pointShare);
                soldier.AddSkillPoints(_skillsByName["Gunnery (Bolter)"], pointShare);
            }
        }

        public void TrainScouts(IEnumerable<Squad> scoutSquads, Dictionary<int, TrainingFocuses> squadFocusMap)
        {
            foreach (Squad squad in scoutSquads)
            {
                if (squad.Members.Count == 0) continue;
                // scout squads on active duty don't have time to train, they'll get battle experience
                if (squad.CurrentOrders == null || squad.CurrentOrders.MissionType == Models.Orders.MissionType.Train)
                {
                    bool goodTeacher = false;
                    TrainingFocuses focuses = squadFocusMap[squad.Id];
                    int numberOfAreas = 0;
                    if ((focuses & TrainingFocuses.Melee) != TrainingFocuses.None) numberOfAreas++;
                    if ((focuses & TrainingFocuses.Physical) != TrainingFocuses.None) numberOfAreas++;
                    if ((focuses & TrainingFocuses.Ranged) != TrainingFocuses.None) numberOfAreas++;
                    if ((focuses & TrainingFocuses.Vehicles) != TrainingFocuses.None) numberOfAreas++;
                    if (numberOfAreas == 0)
                    {
                        numberOfAreas = 4;
                        focuses = TrainingFocuses.Melee | TrainingFocuses.Physical | TrainingFocuses.Ranged | TrainingFocuses.Vehicles;
                    }
                    // 200 hours per point means about 5 weeks, so about 1/5 point per week
                    float baseLearning = 0.2f;
                    squad.SquadLeader.AddSkillPoints(_skillsByName["Teaching"], 0.05f);
                    if (squad.SquadLeader.GetTotalSkillValue(_skillsByName["Teaching"]) >= 12.0f)
                    {
                        goodTeacher = true;
                    }
                    if (!goodTeacher)
                    {
                        // with a sub-par teacher, learning is halfway between teaching and practicing
                        baseLearning *= 0.75f;
                    }
                    foreach (ISoldier soldier in squad.Members)
                    {
                        if ((focuses & TrainingFocuses.Melee) != TrainingFocuses.None)
                        {
                            TrainMelee(soldier, baseLearning / numberOfAreas);
                        }
                        if ((focuses & TrainingFocuses.Physical) != TrainingFocuses.None)
                        {
                            TrainPhysical(soldier, baseLearning / numberOfAreas);
                        }
                        if ((focuses & TrainingFocuses.Ranged) != TrainingFocuses.None)
                        {
                            TrainRanged(soldier, baseLearning / numberOfAreas);
                        }
                        if ((focuses & TrainingFocuses.Vehicles) != TrainingFocuses.None)
                        {
                            TrainVehicles(soldier, baseLearning / numberOfAreas);
                        }
                    }
                }
            }
        }

        private void TrainMelee(ISoldier soldier, float points)
        {
            float pointShare = points / 4;
            soldier.AddSkillPoints(_skillsByName["Sword"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Shield"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Axe"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Fist"], pointShare);
        }

        private void TrainPhysical(ISoldier soldier, float points)
        {
            // Traits are 10 for 11, 20 for 12, 40 for 13, 80 for 14, 160 for 15, 320 for 16
            // y = (2^(x-11))*10
            // y = 
            float pointShare = points / 3;
            soldier.AddAttributePoints(Models.Soldiers.Attribute.Strength, pointShare);
            soldier.AddAttributePoints(Models.Soldiers.Attribute.Dexterity, pointShare);
            soldier.AddAttributePoints(Models.Soldiers.Attribute.Constitution, pointShare);
        }

        private void TrainRanged(ISoldier soldier, float points)
        {
            float pointShare = points / 5;
            soldier.AddSkillPoints(_skillsByName["Gun (Bolter)"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Gunnery (Laser)"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Gun (Flamer)"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Gun (Sniper)"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Gun (Shotgun)"], pointShare);
        }

        private void TrainVehicles(ISoldier soldier, float points)
        {
            float pointShare = points / 4;
            soldier.AddSkillPoints(_skillsByName["Drive (Bike)"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Pilot (Land Speeder)"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Drive (Rhino)"], pointShare);
            soldier.AddSkillPoints(_skillsByName["Gunnery (Bolter)"], pointShare);
        }
    }
}
