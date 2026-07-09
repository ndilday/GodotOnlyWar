using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Models.Fleets;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace OnlyWar.Models
{
    public enum GrowthType
    {
        None = 0,
        Logistic = 1,
        Conversion = 2,
        // Consumption factions (Tyranids) have no organic birthrate: they grow only by eating
        // biomass — Predate (headcount) and Consume (carrying capacity). See PRD §4.24.
        Consumption = 3
    }

    public class Faction
    {
        public int Id { get; }
        public string Name { get; }
        public Color Color { get; }
        public bool IsPlayerFaction { get; }
        public bool IsDefaultFaction { get; }
        public bool CanInfiltrate { get; }
        public GrowthType GrowthType { get; }
        // Whether the faction's population IS its fighting force (Tyranids, Genestealer Cults, and
        // — once implemented — Orks and Necrons), so battle casualties come out of Population.
        // Factions with a separate civilian base (the Imperium, Tau, Votann, and for now
        // Chaos/Eldar/Drukhari) instead lose their military pool (Garrison). This is an interim
        // behavioral flag pending the data-driven FactionBehavior consolidation (PRD §4.21);
        // settable so a specific faction can override the default below.
        public bool PopulationIsMilitary { get; set; }
        // Whether a victorious offensive leaves its survivors behind to seize the ground (invade)
        // rather than returning them to their staging region (raid). A Tyranid tide invades; a
        // raider returns home. Interim default keyed off the consuming growth type (§4.24).
        public bool InvadesOnVictory { get; set; }
        public IReadOnlyDictionary<int, Species> Species { get; }
        public IReadOnlyDictionary<int, SoldierTemplate> SoldierTemplates { get; }
        public IReadOnlyDictionary<int, SquadTemplate> SquadTemplates { get; }
        public IReadOnlyDictionary<int, UnitTemplate> UnitTemplates { get; }
        public IReadOnlyDictionary<int, ShipTemplate> ShipTemplates { get; }
        public IReadOnlyDictionary<int, BoatTemplate> BoatTemplates { get; }
        public IReadOnlyDictionary<int, FleetTemplate> FleetTemplates { get; }

        public List<Unit> Units { get; set; }

        private long? _minimumForceRequest;
        // The battle value of the smallest full non-HQ squad this faction can field — the floor
        // for any force-generation budget. A request below this can be ungeneratable (the force
        // generator returns no squads when even a minimum partial squad exceeds the budget), so
        // order budgets sized off a near-dead defender must be clamped up to it or the target is
        // never attacked. Squad templates are fixed at load, so this is computed once.
        public long MinimumForceRequest =>
            _minimumForceRequest ??= SquadTemplates?.Values
                .Where(st => st.BattleValue > 0 && (st.SquadType & SquadTypes.HQ) == 0)
                .Select(st => (long)st.BattleValue)
                .DefaultIfEmpty(0)
                .Min() ?? 0;

        public Faction(int id, string name, Color color, bool isPlayerFaction, 
                       bool isDefaultFaction, bool canInfiltrate, GrowthType growthType,
                       IReadOnlyDictionary<int, Species> species,
                       IReadOnlyDictionary<int, SoldierTemplate> soldierTemplates,
                       IReadOnlyDictionary<int, SquadTemplate> squadTemplates,
                       IReadOnlyDictionary<int, UnitTemplate> unitTemplates,
                       IReadOnlyDictionary<int, BoatTemplate> boatTemplates,
                       IReadOnlyDictionary<int, ShipTemplate> shipTemplates,
                       IReadOnlyDictionary<int, FleetTemplate> fleetTemplates)
        {
            Id = id;
            Name = name;
            Color = color;
            IsPlayerFaction = isPlayerFaction;
            IsDefaultFaction = isDefaultFaction;
            CanInfiltrate = canInfiltrate;
            GrowthType = growthType;
            // Interim derivations (see property comments): every non-Imperial NPC faction that
            // currently exists is a population-is-military horde, and Tyranids (Consumption) are the
            // invaders. Both are overridable once FactionBehavior/rules data carry them explicitly.
            PopulationIsMilitary = !isPlayerFaction && !isDefaultFaction;
            InvadesOnVictory = growthType == GrowthType.Consumption;
            Species = species;
            SoldierTemplates = soldierTemplates;
            SquadTemplates = squadTemplates;
            UnitTemplates = unitTemplates;
            BoatTemplates = boatTemplates ?? new Dictionary<int, BoatTemplate>();
            ShipTemplates = shipTemplates ?? new Dictionary<int, ShipTemplate>();
            FleetTemplates = fleetTemplates ?? new Dictionary<int, FleetTemplate>();
            foreach(UnitTemplate template in UnitTemplates?.Values ?? Enumerable.Empty<UnitTemplate>())
            {
                template.Faction = this;
            }
            foreach(SquadTemplate template in SquadTemplates?.Values ?? Enumerable.Empty<SquadTemplate>())
            {
                template.Faction = this;
            }
            Units = [];
        }
    }
}
