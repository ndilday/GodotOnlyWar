using OnlyWar.Models.Missions;
using System.Linq;

namespace OnlyWar.Helpers.Missions
{
    /// <summary>
    /// Withdraws the force if it is still able to move and is standing on ground it does not hold.
    /// </summary>
    /// <remarks>
    /// This is the post-engagement withdrawal that LightningRaidMissionStep and
    /// PerformAssassinationMissionStep both performed inline, with byte-identical logic, after their
    /// battle returned. Both ran it regardless of how the battle went, which is why it is reached as
    /// a mandatory follow-up (<see cref="MissionStepResult.Then"/>) rather than as the engagement's
    /// resume target: an engagement that leaves the force spent deliberately declines to resume, and
    /// routing the withdrawal through Resume would silently strand a raid that withdrew under fire
    /// but was still able to walk home.
    /// </remarks>
    public class WithdrawIfAbleMissionStep : IMissionStep
    {
        public string Description => "Withdraw";

        public MissionStepResult ExecuteMissionStep(
            MissionExecutionContext execution,
            float marginOfSuccess,
            IMissionStep resumeStep)
        {
            MissionContext context = execution.State;
            if (!context.MissionSquads.Any(squad => squad.ShouldContinueMission()))
            {
                return MissionStepResult.Complete;
            }
            if (context.Order.Mission.RegionFaction.Region
                == context.MissionSquads.First().Squad.CurrentRegion)
            {
                return MissionStepResult.Complete;
            }
            return MissionStepResult.Continue(new ExfiltrateMissionStep());
        }
    }
}
