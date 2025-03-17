using OnlyWar.Builders;
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

    public class Mission
    {
        public int Id { get; private set; }
        public MissionType MissionType { get; private set; }
        public Region Region { get; private set; }
        public int MissionSize { get; private set; }

        public Mission(int id, MissionType missionType, Region region, int missionSize)
        {
            Id = id;
            MissionType = missionType;
            Region = region;
            MissionSize = missionSize;
        }

        public Mission(MissionType missionType, Region region, int missionSize) : this(IdGenerator.GetNextMissionId(), missionType, region, missionSize) { }
    }

    public class SabotageMission : Mission
    {
        public DefenseType DefenseType { get; private set; }

        public SabotageMission(int id, DefenseType defenseType, int size, Region region) : base(id, MissionType.Sabotage, region, size)
        {
            DefenseType = defenseType;
        }
    }
}
