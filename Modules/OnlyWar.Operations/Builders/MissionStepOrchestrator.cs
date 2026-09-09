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
                case MissionType.Patrol:
                    return new PatrolSweepMissionStep();
                case MissionType.Recon:
                    return new ReconStealthMissionStep();
                case MissionType.Sabotage:
                    return new SabotageStealthMissionStep();
            }
            return null;
        }
    }
}
