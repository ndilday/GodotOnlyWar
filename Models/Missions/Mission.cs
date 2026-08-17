using OnlyWar.Builders;
using OnlyWar.Models.Planets;
using OnlyWar.Models;
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
        private Region TargetRegion { get; }
        private Faction TargetFactionWithoutPresence { get; }
        public Region Region => RegionFaction?.Region ?? TargetRegion;
        public Faction TargetFaction => RegionFaction?.PlanetFaction?.Faction ?? TargetFactionWithoutPresence;
        public StrategicTarget Target => Region == null || TargetFaction == null
            ? null
            : new StrategicTarget(Region, TargetFaction, RegionFaction);
        public int MissionSize { get; private set; }
        // Ambush opportunities roll their concrete opposing-force budget when intelligence
        // discovers them, so the player can make an informed commitment and execution can use
        // the same force strength. Other mission types leave this null.
        public long? TargetBattleValue { get; private set; }

        public Mission(
            int id,
            MissionType missionType,
            RegionFaction regionFaction,
            int missionSize,
            long? targetBattleValue = null)
        {
            Id = id;
            MissionType = missionType;
            RegionFaction = regionFaction;
            TargetRegion = regionFaction?.Region;
            TargetFactionWithoutPresence = regionFaction?.PlanetFaction?.Faction;
            MissionSize = missionSize;
            TargetBattleValue = targetBattleValue;
        }

        /// <summary>
        /// Creates an intelligence-led mission whose target may not currently occupy the region.
        /// Execution resolves the optional current presence through <see cref="Target"/>.
        /// </summary>
        public Mission(
            int id,
            MissionType missionType,
            Region region,
            Faction targetFaction,
            int missionSize,
            long? targetBattleValue = null)
        {
            Id = id;
            MissionType = missionType;
            RegionFaction = null;
            TargetRegion = region ?? throw new ArgumentNullException(nameof(region));
            TargetFactionWithoutPresence = targetFaction
                ?? throw new ArgumentNullException(nameof(targetFaction));
            MissionSize = missionSize;
            TargetBattleValue = targetBattleValue;
        }

        public Mission(
            MissionType missionType,
            RegionFaction regionFaction,
            int missionSize,
            long? targetBattleValue = null)
            : this(
                IdGenerator.GetNextMissionId(),
                missionType,
                regionFaction,
                missionSize,
                targetBattleValue)
        { }

        public Mission(
            MissionType missionType,
            Region region,
            Faction targetFaction,
            int missionSize,
            long? targetBattleValue = null)
            : this(
                IdGenerator.GetNextMissionId(),
                missionType,
                region,
                targetFaction,
                missionSize,
                targetBattleValue)
        { }
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
        // squad (NPC faction development — MissionTurnProcessor.ProcessConstructionOrders). Squad-borne
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

    /// <summary>
    /// A Consumption faction's biomass feeding for one turn, in the region it feeds.
    /// </summary>
    /// <remarks>
    /// Feeding is a tasking like any other: the strategy controller allocates it out of the same
    /// per-region force budget defence, offensives, development and patrols draw on, and what it
    /// commits is carried here. It used to be a planet-update side effect that recomputed the swarm's
    /// whole deployed strength from scratch, so the same troops fed, defended, patrolled and attacked
    /// in the same week (Design/Reference/TyranidFeedingAsMission.md).
    ///
    /// Squad-less on the <see cref="ConstructionMission"/> precedent - materializing squads for a
    /// million-strong swarm would be absurd, and unlike a patrol screen there is nothing for them to
    /// do tactically. The order carries no squads and resolves instantly in the mission phase
    /// (MissionTurnProcessor.ProcessFeedOrders).
    /// </remarks>
    public class FeedMission : Mission
    {
        // Battle value committed to feeding this turn. For a PopulationIsMilitary swarm the BV pool
        // and the headcount are the same number (RegionFaction.MilitaryStrength), so this drops
        // straight into the biomass allocator's "troops" term with no conversion.
        public long CommittedBattleValue { get; private set; }

        public FeedMission(long committedBattleValue, RegionFaction regionFaction)
            : base(
                MissionType.Feed,
                regionFaction,
                (int)Math.Clamp(committedBattleValue, 0L, int.MaxValue))
        {
            CommittedBattleValue = Math.Max(0L, committedBattleValue);
        }
    }
}
