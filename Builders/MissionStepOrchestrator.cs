﻿using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Missions.Ambush;
using OnlyWar.Helpers.Missions.Assassination;
using OnlyWar.Helpers.Missions.Recon;
using OnlyWar.Helpers.Missions.Sabotage;
using OnlyWar.Models.Missions;
using System.Linq;

namespace OnlyWar.Builders
{
    public static class MissionStepOrchestrator
    {
        public static IMissionStep GetStartingStep(MissionContext context)
        {
            if (context.Order.Mission.Region != context.PlayerSquads.First().Squad.CurrentRegion)
            {
                return new InfiltrateMissionStep();
            }
            return GetMainInitialStep(context);
        }

        public static IMissionStep GetMainInitialStep(MissionContext context)
        {
            switch (context.Order.Mission.MissionType)
            {
                case MissionType.Recon:
                    return new ReconStealthMissionStep();
                case MissionType.Sabotage:
                    return new SabotageStealthMissionStep();
                case MissionType.Assassination:
                    return new AssassinateStealthMissionStep();
                case MissionType.Ambush:
                    return new PositionAmbushMissionStep();
            }
            return null;
        }
    }
}
