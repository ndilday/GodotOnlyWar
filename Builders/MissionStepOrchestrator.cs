using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Missions.Recon;
using OnlyWar.Models.Missions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnlyWar.Builders
{
    public static class MissionStepOrchestrator
    {
        public static IMissionStep GetStartingStep(MissionContext context)
        {
            if (context.Region != context.PlayerSquads.First().CurrentRegion)
            {
                return new InfiltrateMissionStep();
            }
            return GetMainInitialStep(context);
        }

        public static IMissionStep GetMainInitialStep(MissionContext context)
        {
            switch (context.MissionType)
            {
                case MissionType.Recon:
                    return new ReconStealthMissionStep();
            }
            return null;
        }
    }
}
