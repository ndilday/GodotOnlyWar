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
        Consumption = 3,
        // Civilian allegiance shifts into and out of an Unrest faction according to the host
        // region's Contentment. Unlike Conversion, this flow is reversible when conditions improve.
        Unrest = 4
    }

    public class Faction
    {
        public int Id { get; }
        public string Name { get; }
        public Color Color { get; }
        public bool IsPlayerFaction { get; }
        public bool IsDefaultFaction { get; }
        public FactionBehavior Behavior { get; }
        public GrowthType GrowthType { get; }
        // How strongly a squad of this faction distributes its fire across the enemy frontage rather
        // than piling every weapon onto the single most valuable target (Phase 3 fire distribution).
        // 1 = tight sector discipline; 0 = an undisciplined mob that dogpiles. Interim derivation
        // (see PopulationIsMilitary): the Imperium fights to Codex doctrine, synaptic Tyranid broods
        // (Consumption) coordinate through the hive mind, and everything else is a horde. Overridable
        // once rules data carry it explicitly, and refinable to live synapse coverage per squad.
        public float FireDiscipline { get; set; }
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
                .Where(st => st.IsPresentOperationalForce
                    && st.BattleValue > 0
                    && (st.SquadType & SquadTypes.HQ) == 0)
                .Select(st => (long)st.BattleValue)
                .DefaultIfEmpty(0)
                .Min() ?? 0;

        public Faction(int id, string name, Color color, bool isPlayerFaction,
                       bool isDefaultFaction, FactionBehavior behavior, GrowthType growthType,
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
            Behavior = behavior;
            GrowthType = growthType;
            FireDiscipline =
                isPlayerFaction || isDefaultFaction || growthType == GrowthType.Consumption
                    ? 1.0f
                    : 0.3f;
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

        public bool HasBehavior(FactionBehavior behavior) => (Behavior & behavior) == behavior;
    }
}
