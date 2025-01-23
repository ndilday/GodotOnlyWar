using Godot;
using OnlyWar.Helpers;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnlyWar.Models.Missions
{
    public interface IMissionTest
    {
        public BaseSkill SkillUsed { get; }
        public float RunMissionTest(Squad squad);
    }

    public static class GaussianMissionTestCalculator
    {

        public static float DetermineMarginOfSuccess(float skillValue, float difficulty)
        {
            float advantage = (skillValue - difficulty) / 5.0f;
            double roll = RNG.NextGaussianDouble();
            return (float)(roll - ApproximateNormalCDF(advantage));
        }

        private static double ApproximateNormalCDF(double zScore)
        {
            // Abramowitz and Stegun approximation constants
            const double a1 = 0.319381530;
            const double a2 = -0.356563782;
            const double a3 = 1.781477937;
            const double a4 = -1.821255978;
            const double a5 = 1.330274429;
            const double k = 0.2316419;

            double x = Math.Abs(zScore);
            double t = 1.0 / (1.0 + k * x);

            double poly = t * (a1 + t * (a2 + t * (a3 + t * (a4 + t * a5))));
            double prob = 1.0 - (1.0 / Math.Sqrt(2 * Math.PI)) * Math.Exp(-x * x / 2.0) * poly;

            if (zScore < 0)
                prob = 1.0 - prob;

            return prob;
        }
    }

    public class IndividualMissionTest : IMissionTest
    {
        public BaseSkill SkillUsed { get; }

        private float _difficulty;

        public IndividualMissionTest(BaseSkill skill, float difficulty)
        {
            SkillUsed = skill;
            _difficulty = difficulty;
        }

        public virtual float RunMissionTest(Squad squad)
        {
            // find soldier in squad with highest skill in SkillUsed
            ISoldier bestSoldier = squad.Members
                .OrderByDescending(soldier => soldier.GetTotalSkillValue(SkillUsed))
                .FirstOrDefault();
            return RunTestInternal(bestSoldier);
        }

        protected float RunTestInternal(ISoldier soldier)
        {
            return GaussianMissionTestCalculator.DetermineMarginOfSuccess(soldier.GetTotalSkillValue(SkillUsed), _difficulty);
        }
    }

    public class LeaderTest : IndividualMissionTest
    {
        public LeaderTest(BaseSkill skill, float difficulty) : base(skill, difficulty)
        {
        }

        public override float RunMissionTest(Squad squad)
        {
            if (squad.SquadLeader == null)
            {
                ISoldier bestSoldier = squad.Members
                .OrderByDescending(soldier => soldier.GetTotalSkillValue(SkillUsed))
                .FirstOrDefault();
                return RunTestInternal(bestSoldier);
            }
            return RunTestInternal(squad.SquadLeader);
        }
    }

    public class SquadTest : IMissionTest
    {
        public BaseSkill SkillUsed { get; }
        private float _difficulty;
        public SquadTest(BaseSkill skill, float difficulty)
        {
            SkillUsed = skill;
            _difficulty = difficulty;
        }
        public float RunMissionTest(Squad squad)
        {
            float totalSkill = squad.Members.Average(soldier => soldier.GetTotalSkillValue(SkillUsed));
            return GaussianMissionTestCalculator.DetermineMarginOfSuccess(totalSkill, _difficulty);
        }
    }
}
