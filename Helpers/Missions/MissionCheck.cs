using OnlyWar.Helpers;
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
        public float RunMissionCheck(List<BattleSquad> squads, IRNG random);
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
        // NPC missions still use the same mission-check classes for their tactical rolls, but
        // field XP is a player-career system. Keep those checks out of the XP path entirely rather
        // than calculating an award and discovering afterward that there are no recipients.
        public static bool ShouldAwardFieldExperience(List<BattleSquad> squads)
        {
            return squads?.SelectMany(squad => squad?.AbleSoldiers ?? Enumerable.Empty<BattleSoldier>())
                .Any(soldier => soldier?.Soldier is PlayerSoldier) == true;
        }

        public static void AwardFieldExperience(List<BattleSquad> squads, BaseSkill skillUsed, float margin)
        {
            if (skillUsed == null || !ShouldAwardFieldExperience(squads))
            {
                return;
            }
            float points = MissionExperienceCalculator.CalculatePointsForMargin(margin);
            int recipients = 0;
            foreach (BattleSoldier soldier in squads.SelectMany(s => s.AbleSoldiers))
            {
                if (soldier?.Soldier is PlayerSoldier playerSoldier)
                {
                    playerSoldier.AddSkillPoints(skillUsed, points);
                    GameLog.Trace(() =>
                        $"Field XP: {playerSoldier.Name} +{points:F4} {skillUsed.Name} "
                        + $"(margin={margin:F2})");
                    recipients++;
                }
            }
            int awardedCount = recipients;
            GameLog.Trace(() =>
                $"Field XP awarded: {skillUsed.Name} margin={margin:F2} -> "
                + $"{points:F4} pts to {awardedCount} soldier(s)");
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

        public virtual float RunMissionCheck(List<BattleSquad> squads, IRNG random)
        {
            // find soldier in squad with highest skill in SkillUsed
            BattleSoldier bestSoldier = squads.SelectMany(s => s.AbleSoldiers)
                .OrderByDescending(soldier => soldier.Soldier.GetTotalSkillValue(SkillUsed))
                .FirstOrDefault();
            float margin = RunCheckInternal(bestSoldier, random);
            if (MissionExperienceAwarder.ShouldAwardFieldExperience(squads))
            {
                MissionExperienceAwarder.AwardFieldExperience(squads, SkillUsed, margin);
            }
            return margin;
        }

        protected float RunCheckInternal(BattleSoldier soldier, IRNG random)
        {
            // No able soldier to make the attempt: auto-fail rather than dereferencing null.
            if (soldier == null)
            {
                return GaussianCalculator.DetermineMarginOfSuccessZvalue(
                    MissionCheckDefaults.NoAbleSoldiersZDisadvantage,
                    random);
            }
            float zAdvantage = (soldier.Soldier.GetTotalSkillValue(SkillUsed) - _difficulty) / 5.0f;
            return GaussianCalculator.DetermineMarginOfSuccessZvalue(zAdvantage, random);
        }
    }

    public class LeaderMissionTest : IndividualMissionTest
    {
        public LeaderMissionTest(BaseSkill skill, float difficulty) : base(skill, difficulty)
        {
        }

        public override float RunMissionCheck(List<BattleSquad> squads, IRNG random)
        {
            // The senior officer present calls the shots, not the most talented one: a mediocre
            // Captain still commands over a gifted Sergeant, and the force lives with his judgment.
            // Skill only breaks ties between equals in rank, subrank, and time in rank; soldier id
            // is the final tiebreak so the choice is deterministic under a fixed seed.
            //
            // Candidates come from BattleSquad.SquadLeader, which is already restricted to able
            // soldiers. Filtering nulls out is load-bearing: the old guard tested Squad.SquadLeader
            // (the roster) while the selection below read the able-only property, so a force whose
            // sergeants were all down passed the guard and then ran the check on a null leader —
            // an automatic -5 sigma failure instead of falling back on the best brother standing.
            List<BattleSoldier> leaders = squads
                .Select(s => s.SquadLeader)
                .Where(leader => leader != null)
                .ToList();
            if (leaders.Count == 0)
            {
                return base.RunMissionCheck(squads, random);
            }
            BattleSoldier commander = SoldierSeniority
                .OrderBySeniority(leaders, leader => leader.Soldier)
                .ThenByDescending(leader => leader.Soldier.GetTotalSkillValue(SkillUsed))
                .ThenBy(leader => leader.Soldier.Id)
                .First();
            float margin = RunCheckInternal(commander, random);
            if (MissionExperienceAwarder.ShouldAwardFieldExperience(squads))
            {
                MissionExperienceAwarder.AwardFieldExperience(squads, SkillUsed, margin);
            }
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
        public float RunMissionCheck(List<BattleSquad> squads, IRNG random)
        {
            List<BattleSoldier> ableSoldiers = squads.SelectMany(s => s.AbleSoldiers).ToList();
            // No able soldiers left to attempt the check: auto-fail rather than averaging over an
            // empty set (which throws InvalidOperationException).
            if (ableSoldiers.Count == 0)
            {
                return GaussianCalculator.DetermineMarginOfSuccessZvalue(
                    MissionCheckDefaults.NoAbleSoldiersZDisadvantage,
                    random);
            }
            float totalSkill = ableSoldiers.Average(soldier => soldier.Soldier.GetTotalSkillValue(SkillUsed));
            float zAdvantage = (totalSkill - _difficulty) / 5.0f;
            float margin = GaussianCalculator.DetermineMarginOfSuccessZvalue(zAdvantage, random);
            if (MissionExperienceAwarder.ShouldAwardFieldExperience(squads))
            {
                MissionExperienceAwarder.AwardFieldExperience(squads, SkillUsed, margin);
            }
            return margin;
        }
    }
}
