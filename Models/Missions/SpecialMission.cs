using OnlyWar.Models.Planets;
using System;

namespace OnlyWar.Models.Missions
{
    public enum DefenseType
    {
        Entrenchment = 0,
        Detection = 1,
        AntiAir = 2
    }

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

    public class SabotageMission : SpecialMission
    {
        public DefenseType DefenseType { get; private set; }
        public int MissionSize { get; private set; }
        public SabotageMission(int id, DefenseType defenseType, int size, Region region) : base(id, MissionType.Sabotage, region)
        {
            DefenseType = defenseType;
            MissionSize = size;
        }
    }
}
