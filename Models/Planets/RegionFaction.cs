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
        // numbers are its army (PopulationIsMilitary — Tyranids, cults), plus any standing garrison;
        // for a civilian-base faction (the Imperium) only its garrison fields as troops. The
        // revolt/suppression checks read this rather than raw garrison, so a hidden cult is measured
        // by the members who would actually rise, not its vestigial armed cells (PRD §4.24).
        public long MilitaryStrength =>
            PlanetFaction.Faction.PopulationIsMilitary ? Population + Garrison : Garrison;
        public bool IsPublic { get; set; }
        // Entrenchment provides bonsues against attacks
        public int Entrenchment { get; set; }
        // Detection provides bonuses to detecting enemy forces in the region
        public int Detection { get; set; }
        // AntiAir provides bonuses against air atacks and air assaults
        public int AntiAir { get; set; }
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

        // Per-observer intelligence belief: how well another faction (keyed by its faction id)
        // believes it knows THIS region faction's fighting strength. Raised by that faction's
        // recon missions and consumed by its offensive targeting to shrink the noise on its
        // strength estimate (PRD §4.24 recon; an early, per-region slice of the intelligence-as-
        // belief model, §4.21). Persisted per (region, observed faction, observer faction).
        public readonly Dictionary<int, float> ObserverIntel;

        public RegionFaction(PlanetFaction planetFaction, Region region)
        {
            LandedSquads = new List<Squad>();
            ObserverIntel = new Dictionary<int, float>();
            PlanetFaction = planetFaction;
            Region = region;
            IsPublic = planetFaction.IsPublic;
            Organization = -1;
        }

        // How well observerFactionId believes it knows this region faction's strength (0 = no intel).
        public float GetObserverIntel(int observerFactionId) =>
            ObserverIntel.TryGetValue(observerFactionId, out float level) ? level : 0f;

        // Raises an observer's belief about this region faction (a recon result). Ignores
        // non-positive amounts so a failed recon never erodes prior knowledge here.
        public void AddObserverIntel(int observerFactionId, float amount)
        {
            if (amount <= 0) return;
            ObserverIntel[observerFactionId] = GetObserverIntel(observerFactionId) + amount;
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
