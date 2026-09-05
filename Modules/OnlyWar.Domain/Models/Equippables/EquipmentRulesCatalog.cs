using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace OnlyWar.Models.Equippables
{
    /// <summary>
    /// Runtime registry for the itemized rules vocabulary. The legacy loader uses this conversion
    /// while the shipped database is being rebuilt; all consumers of the new foundation receive
    /// globally unique equipment and kit ids from this registry.
    /// </summary>
    public sealed class EquipmentRulesCatalog
    {
        public IReadOnlyDictionary<int, EquipmentTemplate> EquipmentTemplates { get; }
        public IReadOnlyDictionary<int, AmmunitionType> AmmunitionTypes { get; }
        public IReadOnlyDictionary<int, EquipmentKitTemplate> EquipmentKits { get; }
        public IReadOnlyDictionary<int, PersonalEquipmentRole> PersonalEquipmentRoles { get; }

        public EquipmentRulesCatalog(
            IDictionary<int, EquipmentTemplate> equipmentTemplates,
            IDictionary<int, AmmunitionType> ammunitionTypes,
            IDictionary<int, EquipmentKitTemplate> equipmentKits,
            IDictionary<int, PersonalEquipmentRole> personalEquipmentRoles)
        {
            EquipmentTemplates = new ReadOnlyDictionary<int, EquipmentTemplate>(equipmentTemplates);
            AmmunitionTypes = new ReadOnlyDictionary<int, AmmunitionType>(ammunitionTypes);
            EquipmentKits = new ReadOnlyDictionary<int, EquipmentKitTemplate>(equipmentKits);
            PersonalEquipmentRoles = new ReadOnlyDictionary<int, PersonalEquipmentRole>(personalEquipmentRoles);
        }

        public static int GetRangedEquipmentId(int rangedTemplateId) => 1_000_000 + rangedTemplateId;
        public static int GetMeleeEquipmentId(int meleeTemplateId) => 2_000_000 + meleeTemplateId;
        public static int GetArmorEquipmentId(int armorTemplateId) => 3_000_000 + armorTemplateId;
        public static int GetAmmunitionTypeId(int rangedTemplateId) => 4_000_000 + rangedTemplateId;
        public static int GetAmmunitionPackageId(int ammunitionTypeId) => 5_000_000 + ammunitionTypeId;
        public static int GetKitId(int weaponSetId) => 6_000_000 + weaponSetId;

    }
}
