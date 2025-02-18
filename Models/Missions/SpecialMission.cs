using OnlyWar.Models.Planets;
using System;

namespace OnlyWar.Models.Missions
{
    public class SpecialMission
    {
        public int Id { get; private set; }
        public MissionType MissionType { get; private set; }
        public Region Region { get; private set; }

        public SpecialMission(int id, MissionType missionType, Region region)
        {
            Id = id;
            MissionType = missionType;
            Region = region;
        }
    }
}
