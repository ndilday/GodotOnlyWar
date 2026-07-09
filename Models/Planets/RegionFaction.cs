using System.Collections.Generic;
using Godot;
using OnlyWar.Models.Squads;

namespace OnlyWar.Models.Planets
{
    public class RegionFaction
    {
        public readonly PlanetFaction PlanetFaction;
        public readonly Region Region;
        public readonly List<Squad> LandedSquads;
        private long _population;
        private long _garrison;

        // Total headcount for this faction in the region. The garrison is a subset of it (the portion
        // under arms), so lowering the population trims the garrison down with it — the invariant
        // Garrison <= Population is enforced here rather than relied on at every call site.
        public long Population
        {
            get => _population;
            set
            {
                _population = value < 0 ? 0 : value;
                if (_garrison > _population) _garrison = _population;
            }
        }

        // Garrison represents PDF forces for the default faction, and forces actively defending for
        // other non-player factions. It is a sub-value of Population (the members currently under
        // arms), so it is clamped to [0, Population] and can never exceed it. NOTE: the DB load and
        // object initializers must set Population before Garrison so this clamp does not eat a loaded
        // garrison value.
        public long Garrison
        {
            get => _garrison;
            set => _garrison = value < 0 ? 0 : (value > _population ? _population : value);
        }

        // This faction's fighting strength in the region: its whole population for a horde whose
        // numbers are its army (PopulationIsMilitary — Tyranids, cults);
        // for a civilian-base faction (the Imperium) only its garrison fields as troops. The
        // revolt/suppression checks read this rather than raw garrison, so a hidden cult is measured
        // by the members who would actually rise, not its vestigial armed cells (PRD §4.24).
        public long MilitaryStrength =>
            PlanetFaction.Faction.PopulationIsMilitary ? Population : Garrison;
        public bool IsPublic { get; set; }
        // Entrenchment provides bonsues against attacks. Defense stats are doubles so that build
        // progress (a fortifying squad's weekly engineering output) and demolition (occupation
        // decay, sabotage) accrue fractionally instead of rounding to whole levels.
        public double Entrenchment { get; set; }
        // ListeningPost is a buildable/sabotageable sensor structure (like Entrenchment/AntiAir). It
        // no longer directly provides an awareness bonus; instead it passively feeds this faction's
        // situational awareness of the region each turn (PlanetFaction.RegionIntel). Its awareness
        // role moved to the unified per-(faction, region) intel value.
        public double ListeningPost { get; set; }
        // AntiAir provides bonuses against air atacks and air assaults
        public double AntiAir { get; set; }
        // Organization determins how much of the enemy force can be effectively deployed
        public int Organization { get; set; }

        // Multiplier (default 1.0) applied to this faction's organic population growth in the
        // turn loop. A general primitive, not scenario-specific: the Opening Scenario sets it
        // < 1.0 on stamped Tyranid regions to throttle them below the default curve, and the
        // post-0.7 Ork/revolt tuning will reuse the same lever (Design/OpeningScenario.md §2.2).
        public float GrowthMultiplier { get; set; } = 1.0f;

        // Transient, within-turn diversion state. Set by a diversion mission's pre-planning
        // resolution and consumed by FactionStrategyController when it generates orders that
        // same turn, then cleared. Never persists across a turn, so it is not saved/loaded.
        //
        // PerceivedThreatBonus: extra apparent enemy threat (in troop-equivalents) that a feint
        // projects onto this region, inflating the garrison its controller feels it must hold.
        public float PerceivedThreatBonus { get; set; }
        // ProvocationLevel: how strongly enemies are baited into attacking the force standing in
        // this region (i.e. the feinting force's own region), drawing a counterattack.
        public float ProvocationLevel { get; set; }

        public RegionFaction(PlanetFaction planetFaction, Region region)
        {
            LandedSquads = new List<Squad>();
            PlanetFaction = planetFaction;
            Region = region;
            IsPublic = planetFaction.IsPublic;
            // Organization is a 0-100 percentage: it is the share of this faction's population that
            // can be fielded as effective troops. A newly generated region faction defaults to fully
            // organized (100%); factions build/lose it from there. (This was previously 1, written
            // under the mistaken belief that 1 meant "100%"; at the true scale that left every
            // generated faction fielding only 1% of its population — see the org=100 test fixtures.)
            Organization = 100;
        }

        // Adds/removes fighting strength (in battle-value points) from the pool that represents
        // this faction's army: Population for a population-is-military horde (Tyranids, cults),
        // Garrison for a faction with a separate civilian base (the Imperium, Tau) — PRD §4.24.
        // Amounts are battle value: forces are raised, lost, and returned in the same currency.
        public void AddMilitaryStrength(long battleValue)
        {
            if (battleValue <= 0) return;
            if (PlanetFaction.Faction.PopulationIsMilitary) Population += battleValue;
            else Garrison += battleValue;
        }

        // A faction beaten into hiding abandons its defensive works to the occupier: half of each
        // structure is wrecked in the collapse or captured. The remainder then rots away each turn
        // it stays unmanned under enemy control (TurnController.DecayUnmannedDefenses), so a remnant
        // that resurfaces after a long occupation does not get its old bastion back for free.
        public void HalveDefensesOnGoingToGround()
        {
            Entrenchment *= 0.5;
            ListeningPost *= 0.5;
            AntiAir *= 0.5;
        }

        public void RemoveMilitaryStrength(long battleValue)
        {
            if (battleValue <= 0) return;
            if (PlanetFaction.Faction.PopulationIsMilitary)
            {
                // A horde's numbers are its army: losses come straight out of population (the setter
                // trims any vestigial garrison that would now exceed it).
                Population = Population > battleValue ? Population - battleValue : 0;
            }
            else
            {
                // A civilian-base faction fights with its garrison, but a fallen defender is also a
                // lost member of the population the garrison is drawn from, so the same casualty count
                // leaves both pools (the civilian remainder, Population - Garrison, is unchanged).
                long lost = Garrison < battleValue ? Garrison : battleValue;
                Garrison -= lost;
                Population -= lost;
            }
        }
    }
}
