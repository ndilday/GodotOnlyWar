using System;
using System.Collections.Generic;
using System.Linq;

using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Models.Squads
{
    /// <summary>
    /// Describes whether a squad template is a manoeuvre formation, a seated formation whose
    /// members may deploy independently, or genuinely fixed in place.  This is rules data: a live
    /// squad cannot become administrative as a side effect of a campaign event.
    /// </summary>
    public enum FormationMobilityPolicy
    {
        WholeFormation = 0,
        MembersOnly = 1,
        Fixed = 2
    }

    [Flags]
    public enum SquadTypes
    {
        None = 0x0,
        HQ = 0x1,
        Scout = 0x2,
        Elite = 0x4,
        Fast = 0x8,
        Heavy = 0x10,
        Bodyguard = 0x20,
        // Retained as a compatibility bit for old hand-built fixtures and old rules databases.
        // New rules data expresses administration with SquadTemplate.IsAdministrative and
        // FormationMobilityPolicy.MembersOnly.
        Administrative = 0x40,
        // Retained only so format-13 compatibility callers continue to compile. It is no longer
        // consulted by new movement/order code; templates loaded from new rules data use the
        // explicit Administrative + MembersOnly pair above.
        PermitsIndividualDetachment = 0x80
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
        public bool IsAdministrative { get; }
        public FormationMobilityPolicy MobilityPolicy { get; }

        /// <summary>True when this formation's members can be deployed independently.</summary>
        public bool PermitsIndividualDeployment =>
            IsAdministrative && MobilityPolicy == FormationMobilityPolicy.MembersOnly;

        /// <summary>
        /// Compatibility projection for format-13 callers. New code must use
        /// <see cref="PermitsIndividualDeployment"/>.
        /// </summary>
        [Obsolete("Use PermitsIndividualDeployment.")]
        public bool PermitsIndividualDetachment =>
            PermitsIndividualDeployment
            || (SquadType & SquadTypes.PermitsIndividualDetachment) != 0;

        /// <summary>
        /// Compatibility name retained for older consumers. This is intentionally not used as
        /// the administration predicate by new code because medical staffing and manoeuvre
        /// eligibility are different questions.
        /// </summary>
        [Obsolete("Use CanMoveAsFormation, CanAcceptSquadOrder, or IsPresentOperationalForce.")]
        public bool IsOperational => true;

        public bool CanMoveAsFormation => MobilityPolicy == FormationMobilityPolicy.WholeFormation;

        public bool CanAcceptSquadOrder => CanMoveAsFormation;

        public bool IsPresentOperationalForce => CanAcceptSquadOrder;

        // Local medical/repair support is a role capability, not an administration test. Any
        // formation that can physically participate in the campaign may provide an eligible
        // specialist at its effective location; Fixed formations cannot.
        public bool MayProvideLocalSupport => MobilityPolicy != FormationMobilityPolicy.Fixed;
        // A squad's point value is the sum of its members' battle values (PRD §4.24). Previously a
        // stored column; now derived so it can never drift from the roster. Elements with a rolled
        // strength are priced at their average, since that is what generation actually fields over
        // many squads; for a fixed element the average IS the maximum, so every template authored
        // before variable strength existed prices exactly as it always did.
        public int BattleValue => IsPresentOperationalForce
            ? (int)Math.Round(
                Elements?.Sum(e => e.SoldierTemplate.BattleValue * e.ExpectedNumber) ?? 0f,
                MidpointRounding.AwayFromZero)
            : 0;
        // Derived, not stored (OnlyWar_TDD.md §6.6): true iff any element's
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
                             SquadTypes squadType,
                             FormationMobilityPolicy? mobilityPolicy = null)
        {
            Id = id;
            Name = name;
            Elements = elements.AsReadOnly();
            DefaultWeapons = defaultWeapons;
            WeaponOptions = weaponOptions?.AsReadOnly();
            Armor = armor;
            SquadType = squadType;
            IsAdministrative = (squadType & SquadTypes.Administrative) != 0;
            // A legacy detachment template was already a member-only pool. Mapping it here keeps
            // old fixtures usable while the shipped rules migrate to explicit policy data.
            MobilityPolicy = mobilityPolicy
                ?? (IsAdministrative
                    ? FormationMobilityPolicy.MembersOnly
                    : FormationMobilityPolicy.WholeFormation);
        }
    }
}
