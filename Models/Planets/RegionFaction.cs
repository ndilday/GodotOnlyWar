using System.Collections.Generic;
using Godot;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Squads;

using System;

namespace OnlyWar.Models.Planets
{
    public class RegionFaction
    {
        public readonly PlanetFaction PlanetFaction;
        public readonly Region Region;
        public readonly List<Squad> LandedSquads;
        private long _population;
        private long _garrison;
        private long _armedCivilians;
        private long? _organizedMilitaryStrength;
        private int _legacyOrganization = 100;
        private float _contentment = 70f;

        // Total headcount for this faction in the region. The garrison is a subset of it (the portion
        // under arms), so lowering the population trims the garrison down with it — the invariant
        // Garrison <= Population is enforced here rather than relied on at every call site.
        public long Population
        {
            get => _population;
            set
            {
                long militaryBefore = MilitaryStrength;
                _population = value < 0 ? 0 : value;
                if (_garrison > _population) _garrison = _population;
                // Embedded garrison and armed civilian cadres are disjoint subsets of Population.
                // Preserve embedded personnel first when a population loss forces the pools down.
                long remaining = _population - _garrison;
                if (_armedCivilians > remaining) _armedCivilians = remaining;
                TrackMilitaryStrengthChange(militaryBefore);
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
            set
            {
                long militaryBefore = MilitaryStrength;
                long maximum = _population - _armedCivilians;
                _garrison = value < 0 ? 0 : (value > maximum ? maximum : value);
                TrackMilitaryStrengthChange(militaryBefore);
            }
        }

        // Armed civilian cadres are distinct from Garrison, which represents personnel embedded
        // in a host military/PDF. Keeping this pool separate prevents civilian insurgents from
        // inflating the nominal PDF strength shown to the player. The two armed pools are disjoint
        // subsets of Population and therefore may not exceed it in combination.
        public long ArmedCivilians
        {
            get => _armedCivilians;
            set
            {
                long militaryBefore = MilitaryStrength;
                long maximum = _population - _garrison;
                _armedCivilians = value < 0 ? 0 : (value > maximum ? maximum : value);
                TrackMilitaryStrengthChange(militaryBefore);
            }
        }

        // Internal civil-allegiance value. This is deliberately a simulation primitive rather than
        // player-visible intelligence, and is always constrained to the 0-100 rules scale.
        public float Contentment
        {
            get => _contentment;
            set => _contentment = float.IsNaN(value) ? 70f : Mathf.Clamp(value, 0f, 100f);
        }

        // This faction's fighting strength in the region: its whole population for a horde whose
        // numbers are its army (PopulationIsMilitary — Tyranids, cults);
        // for a civilian-base faction (the Imperium) only its garrison fields as troops. The
        // revolt/suppression checks read this rather than raw garrison, so a hidden cult is measured
        // by the members who would actually rise, not its vestigial armed cells (PRD §4.24).
        public long MilitaryStrength =>
            PlanetFaction.Faction.GrowthType == GrowthType.Unrest
                ? Garrison + ArmedCivilians
                : PlanetFaction.Faction.PopulationIsMilitary ? Population : Garrison;

        // Military strength is partitioned into forces that can currently deploy and forces that
        // still exist but have lost the command, supply, or formation needed to fight coherently.
        // The nullable backing field lets ordinary object initializers establish Population and
        // Garrison before the legacy Organization percentage is converted into a concrete BV pool.
        public long OrganizedMilitaryStrength
        {
            get
            {
                EnsureOrganizedMilitaryStrength();
                return _organizedMilitaryStrength.Value;
            }
        }

        public long DisorganizedMilitaryStrength =>
            Math.Max(0L, MilitaryStrength - OrganizedMilitaryStrength);
        public bool IsPublic { get; set; }
        // The first offensive after a hidden presence reveals is planned as an Ambush. Persisting
        // the marker prevents a save between revelation and planning from losing that advantage.
        public bool HasEmergenceAdvantage { get; set; }
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
        // Compatibility/display percentage. The persisted source of truth is organized BV; setting
        // this property remains useful for scenario builders and legacy saves, where it partitions
        // the current military pool once. Runtime reorganization should use ReorganizeMilitaryStrength.
        public int Organization
        {
            get
            {
                if (!_organizedMilitaryStrength.HasValue) return _legacyOrganization;
                long total = MilitaryStrength;
                if (total <= 0) return _legacyOrganization;
                return Math.Clamp((int)Math.Round(
                    OrganizedMilitaryStrength * 100.0 / total), 0, 100);
            }
            set
            {
                _legacyOrganization = Math.Clamp(value, 0, 100);
                _organizedMilitaryStrength = (long)(
                    MilitaryStrength * (_legacyOrganization / 100.0));
            }
        }

        // Multiplier (default 1.0) applied to this faction's organic population growth in the
        // turn loop. A general primitive, not scenario-specific: the Opening Scenario sets it
        // < 1.0 on stamped Tyranid regions to throttle them below the default curve, and the
        // post-0.7 Ork/revolt tuning will reuse the same lever (Design/Reference/OpeningScenario.md).
        public float GrowthMultiplier { get; set; } = 1.0f;

        // Transient, within-DAY state: search effort this faction has committed to looking at
        // something other than the ground it is supposed to be watching, in the difficulty-point
        // units MissionStealthDifficulty works in. Written by shaping steps (a diversion's feint)
        // during a day's Shaping pass, read by every stealth check during the same day's Acting pass,
        // and reset by MissionDayScheduler before the next day begins. Never persists across a day,
        // let alone a turn, so it is not saved/loaded.
        //
        // This replaces the old PerceivedThreatBonus/ProvocationLevel pair, which were transient
        // across a whole TURN: written by a pre-planning diversion pass and consumed by
        // FactionStrategyController in a later phase, with a manual clear in between. That made a
        // feint a purely strategic effect - it inflated the garrison the enemy planned to hold and did
        // nothing whatsoever to the search effort an infiltrator actually faced, so feinting against a
        // region and infiltrating it in the same week made the infiltration HARDER. The effect now
        // lands where a feint should land: on who is looking where, today.
        public float CommittedAttention { get; set; }

        public RegionFaction(PlanetFaction planetFaction, Region region)
        {
            LandedSquads = new List<Squad>();
            PlanetFaction = planetFaction;
            Region = region;
            IsPublic = planetFaction.IsPublic;
            // New presences default to fully organized. The nullable state defers materializing the
            // concrete pool until Population/Garrison has been initialized; legacy saves can then
            // partition it once through the old percentage column.
            _legacyOrganization = 100;
            _organizedMilitaryStrength = null;
        }

        // Adds/removes fighting strength (in battle-value points) from the pool that represents
        // this faction's army: Population for a population-is-military horde (Tyranids, cults),
        // Garrison for a faction with a separate civilian base (the Imperium, Tau) — PRD §4.24.
        // Amounts are battle value: forces are raised, lost, and returned in the same currency.
        public void AddMilitaryStrength(long battleValue)
        {
            if (battleValue <= 0) return;
            EnsureOrganizedMilitaryStrength();
            if (PlanetFaction.Faction.GrowthType == GrowthType.Unrest)
            {
                Population += battleValue;
                ArmedCivilians += battleValue;
            }
            else if (PlanetFaction.Faction.PopulationIsMilitary) Population += battleValue;
            else Garrison += battleValue;

            // The military-pool property setters add newly raised strength to the organized pool.
        }

        // Indexed access to the three buildable defense stats, so construction, sabotage, decay and
        // the revolt/insurgency splits stop each carrying their own copy of the same three-way
        // switch. Reorganization is deliberately absent: it transfers military BV rather than
        // creating a structure level, and nothing reading a DefenseType level wants that pool.
        public double GetDefense(DefenseType defenseType) => defenseType switch
        {
            DefenseType.Entrenchment => Entrenchment,
            DefenseType.ListeningPost => ListeningPost,
            DefenseType.AntiAir => AntiAir,
            _ => 0.0
        };

        public void SetDefense(DefenseType defenseType, double value)
        {
            value = Math.Max(0.0, value);
            switch (defenseType)
            {
                case DefenseType.Entrenchment:
                    Entrenchment = value;
                    break;
                case DefenseType.ListeningPost:
                    ListeningPost = value;
                    break;
                case DefenseType.AntiAir:
                    AntiAir = value;
                    break;
            }
        }

        public void AddDefense(DefenseType defenseType, double delta) =>
            SetDefense(defenseType, GetDefense(defenseType) + delta);

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
            EnsureOrganizedMilitaryStrength();
            long lost = Math.Min(MilitaryStrength, battleValue);
            long organizedAfter = Math.Max(0L, OrganizedMilitaryStrength - lost);
            RemoveRawMilitaryStrength(lost);
            _organizedMilitaryStrength = Math.Min(MilitaryStrength, organizedAfter);
        }

        // Normal military casualties come from the force that actually deployed. They reduce both
        // total strength and organized strength, leaving the disorganized reserve untouched.
        public long RemoveOrganizedMilitaryStrength(long battleValue)
        {
            if (battleValue <= 0) return 0;
            EnsureOrganizedMilitaryStrength();
            long lost = Math.Min(OrganizedMilitaryStrength, battleValue);
            if (lost <= 0) return 0;
            long organizedAfter = OrganizedMilitaryStrength - lost;
            RemoveRawMilitaryStrength(lost);
            _organizedMilitaryStrength = Math.Min(MilitaryStrength, organizedAfter);
            return lost;
        }

        // Used when an undefended assault overruns troops that still exist but cannot form a defence.
        public long RemoveDisorganizedMilitaryStrength(long battleValue)
        {
            if (battleValue <= 0) return 0;
            EnsureOrganizedMilitaryStrength();
            long lost = Math.Min(DisorganizedMilitaryStrength, battleValue);
            if (lost <= 0) return 0;
            long organizedBefore = OrganizedMilitaryStrength;
            RemoveRawMilitaryStrength(lost);
            _organizedMilitaryStrength = Math.Min(MilitaryStrength, organizedBefore);
            return lost;
        }

        public long ReorganizeMilitaryStrength(long battleValue)
        {
            if (battleValue <= 0) return 0;
            EnsureOrganizedMilitaryStrength();
            long moved = Math.Min(DisorganizedMilitaryStrength, battleValue);
            _organizedMilitaryStrength += moved;
            return moved;
        }

        public long DisorganizeMilitaryStrength(long battleValue)
        {
            if (battleValue <= 0) return 0;
            EnsureOrganizedMilitaryStrength();
            long moved = Math.Min(OrganizedMilitaryStrength, battleValue);
            _organizedMilitaryStrength -= moved;
            return moved;
        }

        // Save loading sets the concrete pool after loading the legacy percentage. Kept public so
        // the data layer does not need reflection or a persistence-only constructor.
        public void SetOrganizedMilitaryStrength(long battleValue)
        {
            _organizedMilitaryStrength = Math.Clamp(battleValue, 0L, MilitaryStrength);
            _legacyOrganization = Organization;
        }

        private void EnsureOrganizedMilitaryStrength()
        {
            if (_organizedMilitaryStrength.HasValue)
            {
                if (_organizedMilitaryStrength.Value > MilitaryStrength)
                    _organizedMilitaryStrength = MilitaryStrength;
                return;
            }

            _organizedMilitaryStrength = (long)(
                MilitaryStrength * (_legacyOrganization / 100.0));
        }

        private void TrackMilitaryStrengthChange(long before)
        {
            if (!_organizedMilitaryStrength.HasValue) return;
            long after = MilitaryStrength;
            if (after > before)
            {
                _organizedMilitaryStrength = Math.Min(
                    after,
                    _organizedMilitaryStrength.Value + (after - before));
            }
            else if (_organizedMilitaryStrength.Value > after)
            {
                _organizedMilitaryStrength = after;
            }
        }

        private void RemoveRawMilitaryStrength(long battleValue)
        {
            if (battleValue <= 0) return;
            if (PlanetFaction.Faction.GrowthType == GrowthType.Unrest)
            {
                long available = Garrison + ArmedCivilians;
                long lost = Math.Min(available, battleValue);
                if (lost <= 0) return;
                long garrisonLost = available <= 0
                    ? 0
                    : Math.Min(Garrison, (long)Math.Round(lost * (Garrison / (double)available)));
                long civiliansLost = lost - garrisonLost;
                if (civiliansLost > ArmedCivilians)
                {
                    garrisonLost += civiliansLost - ArmedCivilians;
                    civiliansLost = ArmedCivilians;
                }
                Garrison -= garrisonLost;
                ArmedCivilians -= civiliansLost;
                Population -= lost;
            }
            else if (PlanetFaction.Faction.PopulationIsMilitary)
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
