using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnlyWar.Models.Missions
{
    public class MeetingEngagementMissionStep : IMissionStep
    {
        public string Description { get { return "Meeting Engagement"; } }

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            // build OpFor
            // set up meeting engagement with OpFor and context.Squad

            returnStep.ExecuteMissionStep(context, marginOfSuccess, returnStep);
        }
    }
}
