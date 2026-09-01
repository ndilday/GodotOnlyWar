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
            Dictionary<int, string> squadTrainingOptionMap,
            float points = 0.2f,
            IReadOnlyDictionary<int, float> pointsBySquad = null);
    }

    public class SoldierTrainingCalculator : ISoldierTrainingService
    {
        private readonly IReadOnlyDictionary<string, BaseSkill> _skillsByKey;
        private readonly IReadOnlyDictionary<string, ScoutTrainingOption> _scoutTrainingOptionsByKey;
        private readonly RatingCalculator _ratingCalculator;
        private readonly NamedSkillRegistry _namedSkills;

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
                                         RatingCalculator ratingCalculator = null,
                                         IEnumerable<ScoutTrainingOption> scoutTrainingOptions = null)
            : this(baseSkills, trainingProfiles, ratingCalculator, null, scoutTrainingOptions)
        {
        }

        internal SoldierTrainingCalculator(IEnumerable<BaseSkill> baseSkills,
                                           IEnumerable<TrainingProfile> trainingProfiles,
                                           RatingCalculator ratingCalculator,
                                           NamedSkillRegistry namedSkills,
                                           IEnumerable<ScoutTrainingOption> scoutTrainingOptions = null)
        {
            _skillsByKey = (baseSkills ?? throw new ArgumentNullException(nameof(baseSkills)))
                .Where(bs => !string.IsNullOrWhiteSpace(bs.SkillKey))
                .ToDictionary(bs => bs.SkillKey, StringComparer.Ordinal);
            _scoutTrainingOptionsByKey = scoutTrainingOptions?.ToDictionary(
                option => option.Key,
                StringComparer.Ordinal)
                ?? new Dictionary<string, ScoutTrainingOption>(StringComparer.Ordinal);
            _ratingCalculator = ratingCalculator;
            _namedSkills = namedSkills;
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
            BaseSkill powerArmor = RequiredSkill(SkillRole.PowerArmor);
            float powerArmorSkill = soldier.GetTotalSkillValue(powerArmor);
            // if any gunnery, ranged, melee, or vehicle skill is below the PA skill, focus on improving PA
            float gunnerySkill = soldier.GetTotalSkillValue(soldier.GetBestSkillInCategory(SkillCategory.Gunnery).BaseSkill);
            float meleeSkill = soldier.GetTotalSkillValue(soldier.GetBestSkillInCategory(SkillCategory.Melee).BaseSkill);
            float rangedSkill = soldier.GetTotalSkillValue(soldier.GetBestSkillInCategory(SkillCategory.Ranged).BaseSkill);
            float vehicleSkill = soldier.GetTotalSkillValue(soldier.GetBestSkillInCategory(SkillCategory.Vehicle).BaseSkill);
            float[] floatArray = { gunnerySkill, meleeSkill, rangedSkill, vehicleSkill };
            float totalMax = floatArray.Max();
            if (totalMax > powerArmorSkill)
            {
                soldier.AddSkillPoints(powerArmor, points);
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
            Dictionary<int, string> squadTrainingOptionMap,
            float points = 0.2f,
            IReadOnlyDictionary<int, float> pointsBySquad = null)
        {
            if (squadTrainingOptionMap == null)
            {
                throw new ArgumentNullException(nameof(squadTrainingOptionMap));
            }

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

                if (!squadTrainingOptionMap.TryGetValue(squad.Id, out string optionKey))
                {
                    throw new InvalidOperationException(
                        $"Scout squad {squad.Id} has no training option selection.");
                }
                ScoutTrainingOption option = RequiredScoutTrainingOption(optionKey);
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
                    BaseSkill teaching = RequiredSkill(SkillRole.Teaching);
                    instructor.AddSkillPoints(teaching, squadPoints * InstructorTeachingXpShare);
                    baseLearning *=
                        instructor.GetTotalSkillValue(teaching) < GoodTeacherSkillThreshold
                            ? SubParInstructorLearningRate
                            : GoodInstructorLearningRate;
                }
                foreach (ISoldier soldier in squad.Members)
                {
                    ApplyTrainingProfile(soldier, option.Profile, baseLearning);
                }
            }
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

        private ScoutTrainingOption RequiredScoutTrainingOption(string optionKey)
        {
            if (!string.IsNullOrWhiteSpace(optionKey)
                && _scoutTrainingOptionsByKey.TryGetValue(optionKey, out ScoutTrainingOption option))
            {
                return option;
            }

            throw new InvalidOperationException(
                $"Scout training option '{optionKey}' is not available to the training calculator.");
        }

        private BaseSkill RequiredSkill(SkillRole role)
        {
            if (_namedSkills != null)
            {
                return _namedSkills[role];
            }

            string key = SkillRoleKeys.For(role);
            if (_skillsByKey.TryGetValue(key, out BaseSkill skill))
            {
                return skill;
            }

            throw new InvalidOperationException(
                $"Required skill role '{key}' ({SkillRoleKeys.DisplayName(role)}) "
                + "is not available to the training calculator.");
        }
    }
}
