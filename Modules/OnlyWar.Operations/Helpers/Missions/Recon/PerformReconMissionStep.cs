using OnlyWar.Domain;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Soldiers;
using System.Linq;

namespace OnlyWar.Operations.Missions.Recon
{
    public class PerformReconMissionStep : IMissionStep
    {
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
            // More eyes cover more ground: the force-size term lowers the difficulty for a squad
            // above ten able members and raises it below that (ReconIntelligenceRules.SizeModifier).
            int observerCount = context.MissionSquads.Sum(squad => squad.AbleMembers.Count);
            float difficulty = ReconIntelligenceRules.ObservationDifficulty
                + MissionAggressionModifiers.EffectDifficulty(context.Order.LevelOfAggression)
                - ReconIntelligenceRules.SizeModifier(observerCount);
            LeaderMissionTest missionTest = new LeaderMissionTest(tactics, difficulty);
            // move the generation of new missions to the turn controller, rather than the individual mission steps
            context.AddLog($"Day {context.DaysElapsed}: Force performs reconnaissance in {context.Order.Mission.RegionFaction.Region.Name}");
            float margin = missionTest.RunMissionCheck(context.MissionSquads, execution.Random);
            // Every day's margin counts, good or bad. The old gate dropped small failures, which
            // biased the walk upward for no stated reason; the curve that turns this running total
            // into awareness (ReconIntelligenceRules.AwarenessDelta) now bounds the downside
            // instead, and the signed total still reaches the belief as evidence.
            context.Impact += margin;

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
