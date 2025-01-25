using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System.Collections.Generic;

namespace OnlyWar.Models.Missions
{
    public class MissionContext
    {
        public Region Region { get; }
        public List<Squad> PlayerSquads { get; }
        public ushort DaysElapsed { get; set; }
        public List<Squad> OpposingForces { get; set; }
    }
}
