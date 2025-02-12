using OnlyWar.Helpers.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System.Collections.Generic;

namespace OnlyWar.Models.Missions
{
    public class MissionContext
    {
        public Region Region { get; }
        public MissionType MissionType { get; }
        public List<Squad> PlayerSquads { get; }
        public ushort DaysElapsed { get; set; }
        public List<Squad> OpposingForces { get; set; }

        public MissionContext(Region region, MissionType missionType, List<Squad> playerSquads, List<Squad> opposingForces)
        {
            Region = region;
            MissionType = missionType;
            PlayerSquads = playerSquads;
            OpposingForces = opposingForces;
            DaysElapsed = 0;
        }
    }
}
