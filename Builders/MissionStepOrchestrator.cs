using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Missions.Ambush;
using OnlyWar.Helpers.Missions.Assassinate;
using OnlyWar.Helpers.Missions.Assault;
using OnlyWar.Helpers.Missions.Diversion;
using OnlyWar.Helpers.Missions.Raid;
using OnlyWar.Helpers.Missions.Recon;
using OnlyWar.Helpers.Missions.Sabotage;
using OnlyWar.Models.Missions;
using System.Linq;

namespace OnlyWar.Builders
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
            if (context.Order.Mission.RegionFaction.Region != context.MissionSquads.First().Squad.CurrentRegion)
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
                    return new PrepareAssaultMissionStep();
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
                case MissionType.Recon:
                    return new ReconStealthMissionStep();
                case MissionType.Sabotage:
                    return new SabotageStealthMissionStep();
            }
            return null;
        }
    }
}
