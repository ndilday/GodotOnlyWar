using OnlyWar.Builders;
using OnlyWar.Models.Planets;
using System;

namespace OnlyWar.Models.Missions
{
    public enum DefenseType
    {
        Entrenchment = 0,
        Detection = 1,
        AntiAir = 2,
        Organization = 3
    }

    public class Mission
    {
        public int Id { get; private set; }
        public MissionType MissionType { get; private set; }
        public RegionFaction RegionFaction { get; private set; }
        public int MissionSize { get; private set; }

        public Mission(int id, MissionType missionType, RegionFaction regionFaction, int missionSize)
        {
            Id = id;
            MissionType = missionType;
            RegionFaction = regionFaction;
            MissionSize = missionSize;
        }

        public Mission(MissionType missionType, RegionFaction regionFaction, int missionSize) : this(IdGenerator.GetNextMissionId(), missionType, regionFaction, missionSize) { }
    }

    public class SabotageMission : Mission
    {
        public DefenseType DefenseType { get; private set; }

        public SabotageMission(int id, DefenseType defenseType, int size, RegionFaction regionFaction) : base(id, MissionType.Sabotage, regionFaction, size)
        {
            DefenseType = defenseType;
        }

        public SabotageMission(DefenseType defenseType, int size, RegionFaction regionFaction) : base(MissionType.Sabotage, regionFaction, size)
        {
            DefenseType = defenseType;
        }
    }

    public class ConstructionMission : Mission
    {
        public DefenseType ConstructionType { get; private set; }

        public ConstructionMission(int id, DefenseType defenseType, int size, RegionFaction regionFaction) : base(id, MissionType.Construction, regionFaction, size)
        {
            ConstructionType = defenseType;
        }

        public ConstructionMission(DefenseType defenseType, int size, RegionFaction regionFaction) : base(MissionType.Construction, regionFaction, size)
        {
            ConstructionType = defenseType;
        }
    }
}