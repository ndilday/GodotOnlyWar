using System;
using System.Collections.Generic;

using OnlyWar.Domain.Equippables;

namespace OnlyWar.Domain.Soldiers
{
    public class SoldierTemplate
    {
        private static readonly IReadOnlyDictionary<string, IReadOnlyList<WeaponSet>> EmptyWeaponOptions =
            new Dictionary<string, IReadOnlyList<WeaponSet>>();
        private static readonly IReadOnlyList<WeaponSet> EmptyMenu = Array.Empty<WeaponSet>();
        private static readonly IReadOnlyDictionary<string, IReadOnlyList<EquipmentKitTemplate>> EmptyEquipmentOptions =
            new Dictionary<string, IReadOnlyList<EquipmentKitTemplate>>();

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
        // No StrategicValue here: one soldier template stands in squads of very different worth (an Ork
        // Nob in Nobz and in Meganobz), so the value lives on SquadTemplateElement.StrategicValuePerSoldier and
        // a soldier reads it through ISoldier.StrategicValue. No MeleeFraction either (removed
        // 2026-11-01): whether a soldier fights in melee depends on his armament and his target,
        // and the battle measures it (TacticalValueTable.MeleeShares).
        /// <summary>
        /// Weapon-set menus this role may carry, grouped by option group. A group name is scoped
        /// to whatever SquadTemplateElement quota references it — "Command Weapon" for the
        /// former "character" roles (command staff and specialists equipped individually rather
        /// than through a squad's pooled counts), or a squad-authored name for troopers with a
        /// squad-level special-weapon menu (e.g. a Tactical Marine's "Heavy Weapon" option).
        /// Empty for a role with no authored menu at all.
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyList<WeaponSet>> WeaponOptionsByGroup { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<EquipmentKitTemplate>> EquipmentKitOptionsByGroup { get; }

        public SoldierTemplate(int id, Species species, string name, byte rank, byte subrank,
                               bool isSquadLeader, byte specialistType,
                               IReadOnlyCollection<ValueTuple<BaseSkill, float>> mosTraining,
                               TrainingProfile workExperienceTrainingProfile = null,
                               IReadOnlyList<SoldierTemplateRequirement> promotionRequirements = null,
                               IReadOnlyDictionary<string, IReadOnlyList<WeaponSet>> weaponOptionsByGroup = null,
                               IReadOnlyDictionary<string, IReadOnlyList<EquipmentKitTemplate>> equipmentKitOptionsByGroup = null)
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
            PromotionRequirements = promotionRequirements ?? [];
            WeaponOptionsByGroup = weaponOptionsByGroup ?? EmptyWeaponOptions;
            EquipmentKitOptionsByGroup = equipmentKitOptionsByGroup ?? EmptyEquipmentOptions;
        }

        /// <summary>The menu for one option group, or empty if this role authors none.</summary>
        public IReadOnlyList<WeaponSet> GetWeaponOptions(string optionGroup) =>
            WeaponOptionsByGroup.TryGetValue(optionGroup, out IReadOnlyList<WeaponSet> options)
                ? options
                : EmptyMenu;
    }
}
