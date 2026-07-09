using OnlyWar.Builders;
using OnlyWar.Models.Planets;
using System;

namespace OnlyWar.Models.Missions
{
    public enum DefenseType
    {
        Entrenchment = 0,
        // Sensor structure (formerly "Detection"). Persisted int value stays 1 for save compatibility.
        ListeningPost = 1,
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
        // Levels (possibly fractional) this order builds when it resolves without an assigned
        // squad (NPC faction development — TurnController.ProcessConstructionOrders). Squad-borne
        // construction ignores it and builds from the squad's engineering skill instead. The
        // int MissionSize on the base rounds it up, kept for mission persistence/display.
        public double BuildAmount { get; private set; }

        public ConstructionMission(int id, DefenseType defenseType, int size, RegionFaction regionFaction) : base(id, MissionType.Construction, regionFaction, size)
        {
            ConstructionType = defenseType;
            BuildAmount = size;
        }

        public ConstructionMission(DefenseType defenseType, int size, RegionFaction regionFaction) : base(MissionType.Construction, regionFaction, size)
        {
            ConstructionType = defenseType;
            BuildAmount = size;
        }

        public ConstructionMission(DefenseType defenseType, double buildAmount, RegionFaction regionFaction)
            : base(MissionType.Construction, regionFaction, (int)Math.Ceiling(buildAmount))
        {
            ConstructionType = defenseType;
            BuildAmount = buildAmount;
        }
    }
}