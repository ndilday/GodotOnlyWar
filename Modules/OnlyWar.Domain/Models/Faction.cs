using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;
using OnlyWar.Domain.Fleets;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace OnlyWar.Domain
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
        private readonly Dictionary<int, UnitTemplate> _unitTemplates;
        public IReadOnlyDictionary<int, UnitTemplate> UnitTemplates => _unitTemplates;
        public IReadOnlyDictionary<int, ShipTemplate> ShipTemplates { get; }
        public IReadOnlyDictionary<int, BoatTemplate> BoatTemplates { get; }
        public IReadOnlyDictionary<int, FleetTemplate> FleetTemplates { get; }

        public List<Unit> Units { get; set; }

        private long? _minimumForceRequest;
        // The smallest force-generation budget that can produce anything for this faction: the
        // cheapest non-HQ squad it can field, with every element at its MINIMUM strength. A request
        // below this is ungeneratable — SquadFactory.GenerateSquadWithinBudget fills each element to
        // its minimum and returns nothing when even that will not fit — so order budgets sized off a
        // near-dead defender must be clamped up to it, or the target is never attacked.
        //
        // This prices the minimum squad, not SquadTemplate.BattleValue, which prices the expected
        // (for a fixed element, the maximum) one. The two are identical for a fixed-size template
        // and diverge as soon as an element allows a range: a PDF infantry squad of one sergeant
        // plus four-to-nineteen troopers is worth 100 at full strength but can be fielded for 25.
        // Using the larger figure made this floor refuse orders the generator could have filled,
        // and it grew whenever a template's maximum was widened - so making squads more flexible
        // made the faction less able to act.
        //
        // Squad templates are fixed at load, so this is computed once.
        public long MinimumForceRequest =>
            _minimumForceRequest ??= SquadTemplates?.Values
                .Where(st => st.IsPresentOperationalForce
                    && st.MinimumBattleValue > 0
                    && (st.SquadType & SquadTypes.HQ) == 0)
                .Select(st => (long)st.MinimumBattleValue)
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
            _unitTemplates = (unitTemplates ?? new Dictionary<int, UnitTemplate>())
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            BoatTemplates = boatTemplates ?? new Dictionary<int, BoatTemplate>();
            ShipTemplates = shipTemplates ?? new Dictionary<int, ShipTemplate>();
            FleetTemplates = fleetTemplates ?? new Dictionary<int, FleetTemplate>();
            foreach(UnitTemplate template in _unitTemplates.Values)
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

        /// <summary>
        /// Registers a code-owned unit template that is part of the faction's runtime model but
        /// was not authored in the rules database. This is used only for persistent campaign
        /// identities, such as a strategic invasion force command unit, so the normal Unit/Squad save shape can
        /// still be reused when a faction has only tactical squad data.
        /// </summary>
        internal void AddRuntimeUnitTemplate(UnitTemplate template)
        {
            if (template == null) throw new System.ArgumentNullException(nameof(template));
            if (_unitTemplates.ContainsKey(template.Id)) return;
            template.Faction = this;
            _unitTemplates.Add(template.Id, template);
        }
    }
}
