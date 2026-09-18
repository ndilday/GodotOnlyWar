using OnlyWar.Domain.Missions;
using OnlyWar.Operations.Missions.Ambush;
using OnlyWar.Operations.Missions.Assassinate;
using OnlyWar.Operations.Missions.Assault;
using OnlyWar.Operations.Missions.Diversion;
using OnlyWar.Operations.Missions.Raid;
using OnlyWar.Operations.Missions.Recon;
using OnlyWar.Operations.Missions.Sabotage;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Planets;
using System.Linq;

namespace OnlyWar.Operations.Missions
{
    public static class MissionStepOrchestrator
    {
        public static IMissionStep GetStartingStep(MissionExecutionContext execution)
        {
            MissionContext context = execution.State;
            // A diversion is overt and demonstrates from an adjacent region by design, so it
            // never infiltrates the target.
            if (context.Order.Mission.MissionType == MissionType.Diversion)
            {
                return GetMainInitialStep(execution);
            }
            Region currentRegion = context.MissionSquads
                .Select(squad => squad.CampaignCharacter?.EffectiveRegion
                    ?? squad.CampaignSquad?.CurrentRegion)
                .FirstOrDefault(region => region != null);
            if (context.Order.Mission.RegionFaction.Region != currentRegion)
            {
                return new InfiltrateMissionStep();
            }
            return GetMainInitialStep(execution);
        }

        public static IMissionStep GetMainInitialStep(MissionExecutionContext execution)
        {
            MissionContext context = execution.State;
            switch (context.Order.Mission.MissionType)
            {
                case MissionType.Advance:
                    return context.Order.OpensWithAmbush
                        ? BuildOpeningAmbushChain(context)
                        : new PrepareAssaultMissionStep();
                case MissionType.LightningRaid:
                    return new LightningRaidMissionStep();
                case MissionType.Ambush:
                    return new PositionAmbushMissionStep();
                case MissionType.Assassination:
                    return new AssassinateStealthMissionStep();
                case MissionType.Diversion:
                    return new DemonstrateForceMissionStep();
                case MissionType.Extermination:
                    return new PositionAmbushMissionStep();
                case MissionType.Patrol:
                    return new PatrolSweepMissionStep();
                case MissionType.Recon:
                    return new ReconStealthMissionStep();
                case MissionType.Sabotage:
                    return new SabotageStealthMissionStep();
            }
            return null;
        }

        /// <summary>
        /// An advance made by a force whose region revealed itself this week: the first blow is struck
        /// from ambush, and the rest of the week is an ordinary assault.
        /// </summary>
        /// <remarks>
        /// Composed from the existing steps rather than given a MissionType of its own. That keeps the
        /// mission an Advance - and so keeps MissionReturnPolicy.Hold, the point of the exercise - and it
        /// keeps the ambush steps ignorant of the assault: they are handed a continuation and a way to
        /// raise the defenders, and neither names the other.
        ///
        /// Both the ambush day and the assault days raise their defenders through the SAME
        /// AssembleDefendingForce call, so the garrison is one force across the week. The ambush's kills
        /// reach the following days through MissionContext.DefenderBattleValueDestroyed, which that
        /// method already deducts from the reserve it mobilises.
        ///
        /// The defenders keep their patrol-detection and preparation contests here, unchanged from an
        /// ordinary assault. The ambush's advantage is expressed where it is earned - in the engagement
        /// itself, which is entered as SecondSideAmbushed at the range the ambushers chose - rather than
        /// stacked a second and third time by suppressing the defenders' own checks.
        /// </remarks>
        private static IMissionStep BuildOpeningAmbushChain(MissionContext context)
        {
            PrepareAssaultMissionStep assault = new();
            return new PositionAmbushMissionStep(
                continuation: assault,
                opposingForce: (execution, margin) => assault.AssembleDefendingForce(
                    context.Order.Mission.RegionFaction,
                    margin,
                    execution.Random,
                    execution.EntityIds,
                    execution.Rules.Tactics,
                    context.DefenderBattleValueDestroyed,
                    context.CurrentMissionBattleValue,
                    execution.Campaign,
                    execution.EngagementElements));
        }
    }
}
