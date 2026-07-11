using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Missions
{
    public interface IMissionCheck
    {
        public BaseSkill SkillUsed { get; }
        // RunMissionTest returns the number of sigmas the squad succeeded or failed by
        public float RunMissionCheck(List<BattleSquad> squads);
    }

    // Central choke point for "learn by doing" field experience (PRD §4.12). Every mission
    // check, regardless of which IMissionCheck implementation ran it, funnels through here so
    // field XP is awarded consistently without touching every individual mission step. Awards
    // go to every able participating PlayerSoldier in the squads that attempted the check (the
    // whole squad exercises the skill, not just whichever soldier's roll was used to resolve
    // it), scaled by MissionExperienceCalculator's margin-inverse curve, and only to
    // PlayerSoldier instances (mirrors PlayerChapterBattleAftermathPolicy's battle XP, which
    // likewise skips non-player soldiers).
    internal static class MissionExperienceAwarder
    {
        public static void AwardFieldExperience(List<BattleSquad> squads, BaseSkill skillUsed, float margin)
        {
            if (squads == null || skillUsed == null)
            {
                return;
            }
            float points = MissionExperienceCalculator.CalculatePointsForMargin(margin);
            foreach (BattleSoldier soldier in squads.SelectMany(s => s.AbleSoldiers))
            {
                if (soldier?.Soldier is PlayerSoldier playerSoldier)
                {
                    playerSoldier.AddSkillPoints(skillUsed, points);
                }
            }
        }
    }

    // A force that has been emptied of able soldiers (combat can wipe or fully incapacitate an
    // order's squad mid-mission) cannot attempt a check; rather than averaging/min-ing over an
    // empty set (which throws), the attempt auto-fails by this many sigma. Modest magnitude so the
    // downstream margin handling (e.g. DetectedMissionStep's opposing-force sizing) stays in the
    // same range as an ordinary failed check.
    internal static class MissionCheckDefaults
    {
        public const float NoAbleSoldiersZDisadvantage = -5.0f;
    }

    public class IndividualMissionTest : IMissionCheck
    {
        public BaseSkill SkillUsed { get; }

        private float _difficulty;

        public IndividualMissionTest(BaseSkill skill, float difficulty)
        {
            SkillUsed = skill;
            _difficulty = difficulty;
        }

        public virtual float RunMissionCheck(List<BattleSquad> squads)
        {
            // find soldier in squad with highest skill in SkillUsed
            BattleSoldier bestSoldier = squads.SelectMany(s => s.AbleSoldiers)
                .OrderByDescending(soldier => soldier.Soldier.GetTotalSkillValue(SkillUsed))
                .FirstOrDefault();
            float margin = RunCheckInternal(bestSoldier);
            MissionExperienceAwarder.AwardFieldExperience(squads, SkillUsed, margin);
            return margin;
        }

        protected float RunCheckInternal(BattleSoldier soldier)
        {
            // No able soldier to make the attempt: auto-fail rather than dereferencing null.
            if (soldier == null)
            {
                return GaussianCalculator.DetermineMarginOfSuccessZvalue(
                    MissionCheckDefaults.NoAbleSoldiersZDisadvantage);
            }
            float zAdvantage = (soldier.Soldier.GetTotalSkillValue(SkillUsed) - _difficulty) / 5.0f;
            return GaussianCalculator.DetermineMarginOfSuccessZvalue(zAdvantage);
        }
    }

    public class LeaderMissionTest : IndividualMissionTest
    {
        public LeaderMissionTest(BaseSkill skill, float difficulty) : base(skill, difficulty)
        {
        }

        public override float RunMissionCheck(List<BattleSquad> squads)
        {
            if (!squads.Any(s => s.Squad.SquadLeader != null))
            {
                return base.RunMissionCheck(squads);
            }
            BattleSoldier bestLeader = squads.Select(s => s.SquadLeader)
                .OrderByDescending(soldier => soldier?.Soldier.GetTotalSkillValue(SkillUsed))
                .FirstOrDefault();
            float margin = RunCheckInternal(bestLeader);
            MissionExperienceAwarder.AwardFieldExperience(squads, SkillUsed, margin);
            return margin;
        }
    }

    public class SquadMissionTest : IMissionCheck
    {
        public BaseSkill SkillUsed { get; }
        private float _difficulty;
        public SquadMissionTest(BaseSkill skill, float difficulty)
        {
            SkillUsed = skill;
            _difficulty = difficulty;
        }
        public float RunMissionCheck(List<BattleSquad> squads)
        {
            List<BattleSoldier> ableSoldiers = squads.SelectMany(s => s.AbleSoldiers).ToList();
            // No able soldiers left to attempt the check: auto-fail rather than averaging over an
            // empty set (which throws InvalidOperationException).
            if (ableSoldiers.Count == 0)
            {
                return GaussianCalculator.DetermineMarginOfSuccessZvalue(
                    MissionCheckDefaults.NoAbleSoldiersZDisadvantage);
            }
            float totalSkill = ableSoldiers.Average(soldier => soldier.Soldier.GetTotalSkillValue(SkillUsed));
            float zAdvantage = (totalSkill - _difficulty) / 5.0f;
            float margin = GaussianCalculator.DetermineMarginOfSuccessZvalue(zAdvantage);
            MissionExperienceAwarder.AwardFieldExperience(squads, SkillUsed, margin);
            return margin;
        }
    }
}
