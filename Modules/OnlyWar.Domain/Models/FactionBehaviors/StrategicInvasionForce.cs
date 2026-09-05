using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.FactionBehaviors
{
    /// <summary>
    /// Persistent strategic identity for one invasion force. The force lifecycle is capability
    /// owned and therefore does not encode a faction name or species in its type.
    /// </summary>
    public class StrategicInvasionForce
    {
        private readonly List<RegionFaction> _knownRegions = [];

        public long Id { get; set; }
        public Faction Faction { get; }
        public Squad CommandSquad { get; }
        public Squad StrategicCommanderSquad => CommandSquad;
        public Region CurrentRegion { get; set; }
        public Planet OriginPlanet { get; }
        public Planet DestinationPlanet { get; set; }
        public int TravelWeeksRemaining { get; set; }
        public long TransitBattleValue { get; set; }
        public bool IsActive { get; set; } = true;
        public IReadOnlyList<RegionFaction> KnownRegions => _knownRegions;
        public bool IsInTransit => DestinationPlanet != null && CurrentRegion == null;

        public ISoldier StrategicCommander => CommandSquad?.Members
            .FirstOrDefault(member => member.Template?.IsSquadLeader == true)
            ?? CommandSquad?.Members.FirstOrDefault();

        public long OrganizedBattleValue => _knownRegions
            .Where(regionFaction => regionFaction?.StrategicInvasionForceId == Id)
            .Sum(regionFaction => regionFaction.OrganizedMilitaryStrength)
            + TransitBattleValue
            + SquadBattleValue(CommandSquad);

        public long CurrentBattleValue => _knownRegions
            .Where(regionFaction => regionFaction?.StrategicInvasionForceId == Id)
            .Sum(regionFaction => regionFaction.MilitaryStrength)
            + TransitBattleValue
            + SquadBattleValue(CommandSquad);

        public StrategicInvasionForce(long id, Faction faction, Squad commandSquad,
            Region currentRegion, Planet originPlanet)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            Id = id;
            Faction = faction ?? throw new ArgumentNullException(nameof(faction));
            CommandSquad = commandSquad ?? throw new ArgumentNullException(nameof(commandSquad));
            CurrentRegion = currentRegion;
            OriginPlanet = originPlanet;
        }

        public void TrackRegion(RegionFaction regionFaction)
        {
            if (regionFaction != null && !_knownRegions.Contains(regionFaction)) _knownRegions.Add(regionFaction);
        }

        public void ForgetRegion(RegionFaction regionFaction) => _knownRegions.Remove(regionFaction);

        private static long SquadBattleValue(Squad squad) => squad?.Members
            .Sum(member => (long)(member.Template?.BattleValue ?? 0)) ?? 0L;
    }

    /// <summary>Persistence projection for a strategic invasion force.</summary>
    public class StrategicInvasionForceSaveData
    {
        public long Id { get; set; }
        public int FactionId { get; set; }
        public int CommandSquadId { get; set; }
        public int? CurrentRegionId { get; set; }
        public int? OriginPlanetId { get; set; }
        public int? DestinationPlanetId { get; set; }
        public int TravelWeeksRemaining { get; set; }
        public long TransitBattleValue { get; set; }
        public bool IsActive { get; set; }
    }
}
