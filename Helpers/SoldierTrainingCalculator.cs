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
        public void ApplySoldierWorkExperience(ISoldier soldier, Squad squad, float points);
        public void TrainScouts(IEnumerable<Squad> scoutSquads, Dictionary<int, TrainingFocuses> squadFocusMap, float points = 0.2f);
    }

    public class SoldierTrainingCalculator : ISoldierTrainingService
    {
        private readonly IReadOnlyDictionary<string, BaseSkill> _skillsByName;
        private readonly IReadOnlyDictionary<string, TrainingProfile> _trainingProfilesByName;
        private readonly RatingCalculator _ratingCalculator;

        // Base skills this calculator still references by name directly (work-experience
        // and scout training). Rating-formula skills are now validated through the
        // data-driven rating definitions instead (see Design/DataDrivenRatings.md).
        // Exposed so the rules-DB load step can fail fast if any is missing (TDD §8.3).
        public static readonly string[] RequiredSkillNames =
        [
            "Power Armor", "Teaching"
        ];

        public SoldierTrainingCalculator(IEnumerable<BaseSkill> baseSkills,
                                         IEnumerable<TrainingProfile> trainingProfiles = null,
                                         RatingCalculator ratingCalculator = null)
        {
            _skillsByName = baseSkills.ToDictionary(bs => bs.Name);
            _trainingProfilesByName = trainingProfiles?.ToDictionary(tp => tp.Name)
                ?? new Dictionary<string, TrainingProfile>();
            _ratingCalculator = ratingCalculator;
        }

        public void UpdateRatings(Date date, PlayerSoldier soldier)
        {
            RequireRatingCalculator();
            SoldierEvaluation eval = _ratingCalculator.Evaluate(soldier, date);
            soldier.AddEvaluation(eval);
        }

        public void EvaluateSoldier(PlayerSoldier soldier, Date trainingFinishedYear)
        {
            RequireRatingCalculator();
            UpdateRatings(trainingFinishedYear, soldier);
            SoldierEvaluation eval = soldier.SoldierEvaluationHistory.Last();
            _ratingCalculator.ApplyAwards(soldier, eval, trainingFinishedYear);
        }

        private void RequireRatingCalculator()
        {
            if (_ratingCalculator == null)
            {
                throw new InvalidOperationException(
                    "This SoldierTrainingCalculator was constructed without a RatingCalculator; "
                    + "rating evaluation and awards are unavailable.");
            }
        }

        public void ApplySoldierWorkExperience(ISoldier soldier, Squad squad, float points)
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
                ApplyTrainingProfile(soldier, ResolveWorkExperienceProfile(soldier, squad), points);
            }
        }

        // A squad leader develops toward the leadership/tactics-plus-combat profile of the
        // squad type he commands (an assault sergeant trains differently than a devastator
        // sergeant), so a single "Sergeant" rank grows into its role. Everyone else, and
        // leaders of squad types that define no leader profile, follow their own template.
        private static TrainingProfile ResolveWorkExperienceProfile(ISoldier soldier, Squad squad)
        {
            if (soldier.Template.IsSquadLeader && squad?.SquadTemplate?.LeaderWorkExperienceProfile != null)
            {
                return squad.SquadTemplate.LeaderWorkExperienceProfile;
            }
            return soldier.Template.WorkExperienceTrainingProfile;
        }

        public void ApplyMarineWorkExperienceByType(ISoldier soldier, float points)
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
