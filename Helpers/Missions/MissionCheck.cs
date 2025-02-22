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
            return RunCheckInternal(bestSoldier);
        }

        protected float RunCheckInternal(BattleSoldier soldier)
        {
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
            return RunCheckInternal(bestLeader);
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
            float totalSkill = squads.SelectMany(s => s.AbleSoldiers).Average(soldier => soldier.Soldier.GetTotalSkillValue(SkillUsed));
            float zAdvantage = (totalSkill - _difficulty) / 5.0f;
            return GaussianCalculator.DetermineMarginOfSuccessZvalue(zAdvantage);
        }
    }
}
