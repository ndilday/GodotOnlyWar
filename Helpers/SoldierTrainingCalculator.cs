using OnlyWar.Models;
using OnlyWar.Models.Missions;
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
        public void TrainScouts(IEnumerable<Squad> scoutSquads, Dictionary<int, TrainingFocuses> squadFocusMap, float points = 0.2f);
    }

    public class SoldierTrainingCalculator : ISoldierTrainingService
    {
        private readonly IReadOnlyDictionary<string, BaseSkill> _skillsByName;
        private readonly IReadOnlyDictionary<string, TrainingProfile> _trainingProfilesByName;

        public SoldierTrainingCalculator(IEnumerable<BaseSkill> baseSkills,
                                         IEnumerable<TrainingProfile> trainingProfiles = null)
        {
            _skillsByName = baseSkills.ToDictionary(bs => bs.Name);
            _trainingProfilesByName = trainingProfiles?.ToDictionary(tp => tp.Name)
                ?? new Dictionary<string, TrainingProfile>();
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
            ApplyTrainingProfile(soldier, soldier.Template.WorkExperienceTrainingProfile, points);
        }

        public void ApplyVeteranWorkExperience(ISoldier soldier, float points)
        {
            ApplyTrainingProfile(soldier, soldier.Template.WorkExperienceTrainingProfile, points);
        }

        public void ApplyTacticalWorkExperience(ISoldier soldier, float points)
        {
            ApplyTrainingProfile(soldier, soldier.Template.WorkExperienceTrainingProfile, points);
        }

        public void ApplyAssaultWorkExperience(ISoldier soldier, float points)
        {
            ApplyTrainingProfile(soldier, soldier.Template.WorkExperienceTrainingProfile, points);
        }

        public void ApplyDevastatorWorkExperience(ISoldier soldier, float points)
        {
            ApplyTrainingProfile(soldier, soldier.Template.WorkExperienceTrainingProfile, points);
        }

        public void ApplyScoutWorkExperience(ISoldier soldier, float points)
        {
            ApplyTrainingProfile(soldier, soldier.Template.WorkExperienceTrainingProfile, points);
        }

        public void TrainScouts(IEnumerable<Squad> scoutSquads, Dictionary<int, TrainingFocuses> squadFocusMap, float points = 0.2f)
        {
            foreach (Squad squad in scoutSquads)
            {
                if (squad.Members.Count == 0) continue;
                // scout squads on active duty don't have time to train, they'll get battle experience
                if (squad.CurrentOrders == null || squad.CurrentOrders.Mission.MissionType == MissionType.Training)
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
                    float baseLearning = points;
                    squad.SquadLeader.AddSkillPoints(_skillsByName["Teaching"], points * 0.25f);
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
                            ApplyNamedTrainingProfile(soldier, "scout_focus_melee", baseLearning / numberOfAreas);
                        }
                        if ((focuses & TrainingFocuses.Physical) != TrainingFocuses.None)
                        {
                            ApplyNamedTrainingProfile(soldier, "scout_focus_physical", baseLearning / numberOfAreas);
                        }
                        if ((focuses & TrainingFocuses.Ranged) != TrainingFocuses.None)
                        {
                            ApplyNamedTrainingProfile(soldier, "scout_focus_ranged", baseLearning / numberOfAreas);
                        }
                        if ((focuses & TrainingFocuses.Vehicles) != TrainingFocuses.None)
                        {
                            ApplyNamedTrainingProfile(soldier, "scout_focus_vehicles", baseLearning / numberOfAreas);
                        }
                    }
                }
            }
        }

        private void TrainMelee(ISoldier soldier, float points)
        {
            ApplyNamedTrainingProfile(soldier, "scout_focus_melee", points);
        }

        private void TrainPhysical(ISoldier soldier, float points)
        {
            ApplyNamedTrainingProfile(soldier, "scout_focus_physical", points);
        }

        private void TrainRanged(ISoldier soldier, float points)
        {
            ApplyNamedTrainingProfile(soldier, "scout_focus_ranged", points);
        }

        private void TrainVehicles(ISoldier soldier, float points)
        {
            ApplyNamedTrainingProfile(soldier, "scout_focus_vehicles", points);
        }

        private void ApplyNamedTrainingProfile(ISoldier soldier, string profileName, float points)
        {
            if (!_trainingProfilesByName.ContainsKey(profileName)) return;
            ApplyTrainingProfile(soldier, _trainingProfilesByName[profileName], points);
        }

        private void ApplyTrainingProfile(ISoldier soldier, TrainingProfile trainingProfile, float points)
        {
            if (trainingProfile == null || trainingProfile.Entries.Count == 0 || points <= 0) return;

            float totalWeight = trainingProfile.Entries.Sum(entry => entry.Weight);
            if (totalWeight <= 0) return;

            foreach (TrainingProfileEntry entry in trainingProfile.Entries)
            {
                float awardedPoints = points * entry.Weight / totalWeight;
                if (entry.TargetType == TrainingTargetType.Skill)
                {
                    soldier.AddSkillPoints(entry.Skill, awardedPoints);
                }
                else if (entry.Attribute.HasValue)
                {
                    soldier.AddAttributePoints(entry.Attribute.Value, awardedPoints);
                }
            }
        }
    }
}
