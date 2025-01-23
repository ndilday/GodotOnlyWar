using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnlyWar.Models.Missions.Recon
{
    internal class AmbushedMissionStep : IMissionStep
    {
        public string Description { get { return "Ambushed"; } }

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            throw new NotImplementedException();
        }
    }
}
