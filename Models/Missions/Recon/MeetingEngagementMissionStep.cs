using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnlyWar.Models.Missions.Recon
{
    public class MeetingEngagementMissionStep : IMissionStep
    {
        public string Description { get { return "Meeting Engagement"; } }

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            throw new NotImplementedException();
        }
    }
}
