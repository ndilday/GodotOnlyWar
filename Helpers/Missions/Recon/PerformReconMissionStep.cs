using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Soldiers;
using System.Linq;

namespace OnlyWar.Helpers.Missions.Recon
{
    public class PerformReconMissionStep : IMissionStep
    {
        // Base difficulty of turning a day's observation into usable intelligence, before
        // aggression. Unchanged from the literal 10.0f this step has always used; named so the
        // aggression modifier reads as a shift against a baseline rather than magic arithmetic.
        private const float ReconIntelligenceDifficulty = 10.0f;

        public string Description { get { return "Recon"; } }

        public PerformReconMissionStep()
        {
            
        }

        public MissionStepResult ExecuteMissionStep(MissionExecutionContext execution, float marginOfSuccess, IMissionStep resumeStep)
        {
            MissionContext context = execution.State;
            BaseSkill tactics = execution.Rules.Tactics;
            // The other half of aggression's exposure-for-effect trade (MissionAggressionModifiers).
            // ReconStealthMissionStep makes a cautious sweep harder to spot; here it learns less,
            // because a force unwilling to expose itself cannot get close enough to see much. A bold
            // sweep inverts both.
            float difficulty = ReconIntelligenceDifficulty
                + MissionAggressionModifiers.EffectDifficulty(context.Order.LevelOfAggression);
            LeaderMissionTest missionTest = new LeaderMissionTest(tactics, difficulty);
            // move the generation of new missions to the turn controller, rather than the individual mission steps
            context.AddLog($"Day {context.DaysElapsed}: Force performs reconnaissance in {context.Order.Mission.RegionFaction.Region.Name}");
            float margin = missionTest.RunMissionCheck(context.MissionSquads, execution.Random);
            // a particularly bad result means bad intel
            if(margin > 0 || margin < -0.5f)
            {
                context.Impact += margin;
            }

            if (context.OperatingDaysSpent)
            {
                // time to go home; otherwise we don't have to go anywhere, so just exit.
                return context.MustExfiltrate
                    ? MissionStepResult.Continue(new ExfiltrateMissionStep(), 0.0f, this)
                    : MissionStepResult.Complete;
            }

            return MissionStepResult.Continue(new ReconStealthMissionStep(), marginOfSuccess, this);
        }
    }
}
