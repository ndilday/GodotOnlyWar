﻿using System;
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
            // build OpFor
            // set up Ambush battle with OpFor attacker and context.Squad defender

            returnStep.ExecuteMissionStep(context, marginOfSuccess, returnStep);
        }
    }
}
