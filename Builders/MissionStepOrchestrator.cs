﻿using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Missions.Ambush;
using OnlyWar.Helpers.Missions.Assassinate;
using OnlyWar.Helpers.Missions.Assault;
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
            if (context.Order.Mission.RegionFaction.Region != context.MissionSquads.First().Squad.CurrentRegion)
            {
                return new InfiltrateMissionStep();
            }
            return GetMainInitialStep(context);
        }

        public static IMissionStep GetMainInitialStep(MissionContext context)
        {
            switch (context.Order.Mission.MissionType)
            {
                case MissionType.Advance:
                    return new PrepareAssaultMissionStep();
                case MissionType.Ambush:
                    return new PositionAmbushMissionStep();
                case MissionType.Assassination:
                    return new AssassinateStealthMissionStep();
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
