using Godot;
using OnlyWar.Helpers;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Missions
{
    public interface IMissionCheck
    {
        public BaseSkill SkillUsed { get; }
        // RunMissionTest returns the number of sigmas the squad succeeded or failed by
        public float RunMissionCheck(List<Squad> squads);
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

        public virtual float RunMissionCheck(List<Squad> squads)
        {
            // find soldier in squad with highest skill in SkillUsed
            ISoldier bestSoldier = squads.SelectMany(s => s.Members)
                .OrderByDescending(soldier => soldier.GetTotalSkillValue(SkillUsed))
                .FirstOrDefault();
            return RunCheckInternal(bestSoldier);
        }

        protected float RunCheckInternal(ISoldier soldier)
        {
            float zAdvantage = (soldier.GetTotalSkillValue(SkillUsed) - _difficulty) / 5.0f;
            return GaussianCalculator.DetermineMarginOfSuccessZvalue(zAdvantage);
        }
    }

    public class LeaderMissionTest : IndividualMissionTest
    {
        public LeaderMissionTest(BaseSkill skill, float difficulty) : base(skill, difficulty)
        {
        }

        public override float RunMissionCheck(List<Squad> squads)
        {
            if (!squads.Any(s => s.SquadLeader != null))
            {
                return base.RunMissionCheck(squads);
            }
            ISoldier bestLeader = squads.Select(s => s.SquadLeader)
                .OrderByDescending(soldier => soldier.GetTotalSkillValue(SkillUsed))
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
        public float RunMissionCheck(List<Squad> squads)
        {
            float totalSkill = squads.SelectMany(s => s.Members).Average(soldier => soldier.GetTotalSkillValue(SkillUsed));
            float zAdvantage = (totalSkill - _difficulty) / 5.0f;
            return GaussianCalculator.DetermineMarginOfSuccessZvalue(zAdvantage);
        }
    }
}
