using System;
using System.Collections.Generic;

using OnlyWar.Models.Equippables;

namespace OnlyWar.Models.Soldiers
{
    public class SoldierTemplate
    {
        private static readonly IReadOnlyDictionary<string, IReadOnlyList<WeaponSet>> EmptyWeaponOptions =
            new Dictionary<string, IReadOnlyList<WeaponSet>>();
        private static readonly IReadOnlyList<WeaponSet> EmptyMenu = Array.Empty<WeaponSet>();

        public int Id { get; }
        public string Name { get; }
        public bool IsSquadLeader { get; }
        public byte SpecialistType { get; }
        public byte Rank { get; }
        public byte Subrank { get; }
        public Species Species { get; }
        public IReadOnlyCollection<ValueTuple<BaseSkill, float>> MosTraining { get; }
        public TrainingProfile WorkExperienceTrainingProfile { get; }
        public IReadOnlyList<SoldierTemplateRequirement> PromotionRequirements { get; }
        // The soldier's point value — its weight in force generation and in casualty/survivor
        // accounting against the strategic pools. A squad's battle value is the sum of its members'
        // (PRD §4.24). Optional/defaulted for templates that predate populated point values.
        public int BattleValue { get; }
        /// <summary>
        /// Doctrinal share of canonical offensive output supplied by melee. Runtime battle roles
        /// additionally weight this by the able roster, usable grips, ammunition and readiness.
        /// </summary>
        public float MeleeFraction { get; }
        public bool HasAuthoredMeleeFraction { get; }
        /// <summary>
        /// Weapon-set menus this role may carry, grouped by option group. A group name is scoped
        /// to whatever SquadTemplateElement quota references it — "Command Weapon" for the
        /// former "character" roles (command staff and specialists equipped individually rather
        /// than through a squad's pooled counts), or a squad-authored name for troopers with a
        /// squad-level special-weapon menu (e.g. a Tactical Marine's "Heavy Weapon" option).
        /// Empty for a role with no authored menu at all.
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyList<WeaponSet>> WeaponOptionsByGroup { get; }

        public SoldierTemplate(int id, Species species, string name, byte rank, byte subrank,
                               bool isSquadLeader, byte specialistType,
                               IReadOnlyCollection<ValueTuple<BaseSkill, float>> mosTraining,
                               TrainingProfile workExperienceTrainingProfile = null,
                               int battleValue = 0,
                               IReadOnlyList<SoldierTemplateRequirement> promotionRequirements = null,
                               float? meleeFraction = null,
                               IReadOnlyDictionary<string, IReadOnlyList<WeaponSet>> weaponOptionsByGroup = null)
        {
            Id = id;
            Species = species;
            Name = name;
            IsSquadLeader = isSquadLeader;
            SpecialistType = specialistType;
            Rank = rank;
            Subrank = subrank;
            MosTraining = mosTraining;
            WorkExperienceTrainingProfile = workExperienceTrainingProfile;
            BattleValue = battleValue;
            HasAuthoredMeleeFraction = meleeFraction.HasValue;
            MeleeFraction = Math.Clamp(meleeFraction ?? 0, 0, 1);
            PromotionRequirements = promotionRequirements ?? [];
            WeaponOptionsByGroup = weaponOptionsByGroup ?? EmptyWeaponOptions;
        }

        /// <summary>The menu for one option group, or empty if this role authors none.</summary>
        public IReadOnlyList<WeaponSet> GetWeaponOptions(string optionGroup) =>
            WeaponOptionsByGroup.TryGetValue(optionGroup, out IReadOnlyList<WeaponSet> options)
                ? options
                : EmptyMenu;
    }
}
