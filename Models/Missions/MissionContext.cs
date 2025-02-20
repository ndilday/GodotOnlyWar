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
        public Aggression Aggression { get; }
        public List<Squad> PlayerSquads { get; }
        public ushort DaysElapsed { get; set; }
        public List<Squad> OpposingForces { get; set; }
        public List<string> Log { get; private set; }

        public List<SpecialMission> MissionsToAdd { get; }
        public List<SpecialMission> MissionsToRemove { get; }

        public MissionContext(Region region, MissionType missionType, Aggression aggression, List<Squad> playerSquads, List<Squad> opposingForces)
        {
            Region = region;
            MissionType = missionType;
            Aggression = aggression;
            PlayerSquads = playerSquads;
            OpposingForces = opposingForces;
            DaysElapsed = 0;
            MissionsToAdd = new List<SpecialMission>();
            MissionsToRemove = new List<SpecialMission>();
            Log = new List<string>();
        }
    }
}
