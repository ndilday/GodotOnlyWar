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
        public void TrainScouts(
            IEnumerable<Squad> scoutSquads,
            Dictionary<int, TrainingFocuses> squadFocusMap,
            float points = 0.2f,
            IReadOnlyDictionary<int, float> pointsBySquad = null);
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

        // Instruction-quality tiers for scout drills. A capable sergeant is worth a bonus over
        // the baseline; a sub-par one still runs drills at the ordinary rate; a squad that has
        // lost its sergeant has no instructor at all and falls back to self-directed drill,
        // which costs it a quarter of the week's value until a replacement is assigned.
        private const float GoodTeacherSkillThreshold = 12.0f;
        private const float GoodInstructorLearningRate = 1.1f;
        private const float SubParInstructorLearningRate = 1.0f;
        private const float NoInstructorLearningRate = 0.75f;
        private const float InstructorTeachingXpShare = 0.25f;

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

        public void TrainScouts(
            IEnumerable<Squad> scoutSquads,
            Dictionary<int, TrainingFocuses> squadFocusMap,
            float points = 0.2f,
            IReadOnlyDictionary<int, float> pointsBySquad = null)
        {
            foreach (Squad squad in scoutSquads)
            {
                if (squad.Members.Count == 0) continue;
                // A squad named in pointsBySquad has already had its eligibility and its share of the
                // week decided by the caller - a mission that finished early leaves drill time behind,
                // and scouts are the squads most often out on one. Squads absent from the map keep the
                // original rule: on active duty they have no time to train and take their growth from the
                // field instead.
                bool hasExplicitShare = pointsBySquad != null
                    && pointsBySquad.TryGetValue(squad.Id, out float explicitPoints);
                float squadPoints = points;
                if (hasExplicitShare)
                {
                    pointsBySquad.TryGetValue(squad.Id, out squadPoints);
                    if (squadPoints <= 0f) continue;
                }
                else if (squad.CurrentOrders != null
                    && squad.CurrentOrders.Mission.MissionType != MissionType.Training)
                {
                    continue;
                }

                {
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
                    float baseLearning = squadPoints;
                    ISoldier instructor = squad.SquadLeader;
                    if (instructor == null)
                    {
                        // The sergeant is dead or transferred out and no replacement has been
                        // assigned. The scouts still drill, but with nobody running the training
                        // they lose part of its value.
                        baseLearning *= NoInstructorLearningRate;
                    }
                    else
                    {
                        instructor.AddSkillPoints(_skillsByName["Teaching"], squadPoints * InstructorTeachingXpShare);
                        baseLearning *=
                            instructor.GetTotalSkillValue(_skillsByName["Teaching"]) < GoodTeacherSkillThreshold
                                ? SubParInstructorLearningRate
                                : GoodInstructorLearningRate;
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
