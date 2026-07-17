using System;
using System.Collections.Generic;
using System.Linq;

using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Models.Squads
{
    [Flags]
    public enum SquadTypes
    {
        None = 0x0,
        HQ = 0x1,
        Scout = 0x2,
        Elite = 0x4,
        Fast = 0x8,
        Heavy = 0x10,
        Bodyguard = 0x20
    }

    [Flags]
    public enum TrainingFocuses
    {
        None = 0,
        Physical = 0x1,
        Vehicles = 0x2,
        Melee = 0x4,
        Ranged = 0x8
    }

    public class SquadWeaponOption
    {
        public string Name { get; private set; }
        public int MaxNumber { get; private set; }
        public int MinNumber { get; private set; }
        public List<WeaponSet> Options { get; private set; }

        public SquadWeaponOption(string name, int min, int max, List<WeaponSet> options)
        {
            Name = name;
            MinNumber = min;
            MaxNumber = max;
            Options = options;
        }
    }

    public class SquadTemplate
    {
        public int Id { get; }
        public string Name { get; }
        public IReadOnlyCollection<SquadTemplateElement> Elements { get; }
        public IReadOnlyCollection<SquadWeaponOption> WeaponOptions { get; }
        public ArmorTemplate Armor { get; }
        public WeaponSet DefaultWeapons { get; }
        public SquadTypes SquadType { get; }
        // A squad's point value is the sum of its members' battle values at full strength (PRD
        // §4.24). Previously a stored column; now derived so it can never drift from the roster.
        public int BattleValue => Elements?.Sum(e => e.SoldierTemplate.BattleValue * e.MaximumNumber) ?? 0;
        // Derived, not stored (Design/Active/MoraleAndRout.md §4.1): true iff any element's
        // species carries SpeciesAbilities.Synapse. Adding a new synapse creature to the DB
        // works automatically.
        public bool ProvidesSynapse => Elements?.Any(
            e => e.SoldierTemplate.Species.Abilities.HasFlag(SpeciesAbilities.Synapse)) ?? false;
        // Squads are species-homogeneous (§3.1), so "squad Ego" is the shared Ego of every
        // member's species — read from the first element. Used by force generation's
        // coverage-needing gate and (Phase 4) the rear-guard predicate.
        public float SquadEgo => Elements != null && Elements.Count > 0
            ? Elements.First().SoldierTemplate.Species.Ego.BaseValue
            : 0f;
        public Faction Faction { get; set;  }
        public SquadTemplate BodyguardSquadTemplate { get; set; }
        // Work-experience training a squad leader develops toward while commanding this
        // squad type. This lets a single "Sergeant" rank train differently depending on
        // whether he leads a tactical, assault, or devastator squad. Null falls back to
        // the leader's own soldier-template profile (see SoldierTrainingCalculator).
        public TrainingProfile LeaderWorkExperienceProfile { get; set; }

        public SquadTemplate(int id, 
                             string name, 
                             WeaponSet defaultWeapons, 
                             List<SquadWeaponOption> weaponOptions, 
                             ArmorTemplate armor,
                             List<SquadTemplateElement> elements,
                             SquadTypes squadType)
        {
            Id = id;
            Name = name;
            Elements = elements.AsReadOnly();
            DefaultWeapons = defaultWeapons;
            WeaponOptions = weaponOptions?.AsReadOnly();
            Armor = armor;
            SquadType = squadType;
        }
    }
}
