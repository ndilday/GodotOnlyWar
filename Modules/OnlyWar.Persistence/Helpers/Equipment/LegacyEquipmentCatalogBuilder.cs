using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Soldiers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using OnlyWar.Domain.Equippables;
using static OnlyWar.Domain.Equippables.EquipmentRulesCatalog;
namespace OnlyWar.Persistence.Equipment
{
    public static class LegacyEquipmentCatalogBuilder
    {
        public static EquipmentRulesCatalog FromLegacyRules(
            IReadOnlyDictionary<int, RangedWeaponTemplate> rangedWeapons,
            IReadOnlyDictionary<int, MeleeWeaponTemplate> meleeWeapons,
            IReadOnlyDictionary<int, ArmorTemplate> armorTemplates,
            IReadOnlyDictionary<int, WeaponSet> weaponSets,
            IReadOnlyCollection<SquadTemplate> squadTemplates = null)
        {
            Dictionary<int, AmmunitionType> ammunitionTypes = [];
            // Rounds in one package of each caliber: the largest magazine that fires it, so a kit's
            // spare packages cover a full reload of any weapon sharing the caliber.
            Dictionary<int, ushort> roundsPerPackage = [];
            Dictionary<int, EquipmentTemplate> equipmentTemplates = [];

            foreach (RangedWeaponTemplate template in rangedWeapons?.Values ?? Array.Empty<RangedWeaponTemplate>())
            {
                int equipmentId = GetRangedEquipmentId(template.Id);
                AmmunitionType ammunitionType = null;
                if (template.AmmunitionBehavior is not AmmunitionBehavior.Unlimited
                    and not AmmunitionBehavior.SelfRegenerating
                    and not AmmunitionBehavior.ConsumableItem)
                {
                    // The rules database authors the caliber; hand-built fixture templates carry
                    // none, and get one private to the weapon as before.
                    ammunitionType = template.AmmunitionType ?? new AmmunitionType(
                        GetAmmunitionTypeId(template.Id),
                        $"{template.Name} ammunition");
                    ammunitionTypes.TryAdd(ammunitionType.Id, ammunitionType);
                    roundsPerPackage[ammunitionType.Id] = Math.Max(
                        roundsPerPackage.GetValueOrDefault(ammunitionType.Id),
                        template.AmmoCapacity);
                }

                AmmunitionBehavior behavior = template.AmmunitionBehavior;
                EquipmentTags tags = EquipmentTags.Weapon | EquipmentTags.Ranged;
                if (template.Location == EquipLocation.TwoHand) tags |= EquipmentTags.TwoHanded;
                if (template.TemplateType == 3)
                {
                    behavior = AmmunitionBehavior.ConsumableItem;
                    ammunitionType = null;
                    tags |= EquipmentTags.Consumable;
                }
                if (IsBiological(template.Name))
                {
                    behavior = AmmunitionBehavior.SelfRegenerating;
                    ammunitionType = null;
                    tags |= EquipmentTags.Biological;
                }

                RangedWeaponProfile profile = new(
                    template.RelatedSkill,
                    template.Accuracy,
                    template.ArmorMultiplier,
                    template.WoundMultiplier,
                    template.RequiredStrength,
                    template.DamageMultiplier,
                    template.MaximumRange,
                    template.RateOfFire,
                    template.AmmoCapacity,
                    template.Recoil,
                    template.Bulk,
                    template.DoesDamageDegradeWithRange,
                    template.Location,
                    ammunitionType,
                    behavior,
                    template.ConsumptionRule,
                    template.ReloadTime,
                    template.ReloadAmount,
                    template.RecoveryDuration,
                    template.RecoveryAmount,
                    template.TemplateType,
                    template.AreaRadius);
                equipmentTemplates[equipmentId] = new EquipmentTemplate(
                    equipmentId,
                    template.Name,
                    carryCost: template.Location == EquipLocation.TwoHand ? 2 : 1,
                    tags: tags,
                    rangedProfile: profile);
            }

            foreach (MeleeWeaponTemplate template in meleeWeapons?.Values ?? Array.Empty<MeleeWeaponTemplate>())
            {
                EquipmentTags tags = EquipmentTags.Weapon | EquipmentTags.Melee;
                if (template.Location == EquipLocation.TwoHand) tags |= EquipmentTags.TwoHanded;
                equipmentTemplates[GetMeleeEquipmentId(template.Id)] = new EquipmentTemplate(
                    GetMeleeEquipmentId(template.Id),
                    template.Name,
                    carryCost: template.Location == EquipLocation.TwoHand ? 2 : 1,
                    tags: tags,
                    meleeProfile: new MeleeWeaponProfile(
                        template.RelatedSkill,
                        template.Accuracy,
                        template.ArmorMultiplier,
                        template.WoundMultiplier,
                        template.RequiredStrength,
                        template.StrengthMultiplier,
                        template.ParryModifier,
                        template.AttackSpeedMultiplier,
                        template.Location));
            }

            foreach (ArmorTemplate template in armorTemplates?.Values ?? Array.Empty<ArmorTemplate>())
            {
                equipmentTemplates[GetArmorEquipmentId(template.Id)] = new EquipmentTemplate(
                    GetArmorEquipmentId(template.Id),
                    template.Name,
                    carryCost: 0,
                    tags: EquipmentTags.Armor,
                    armorProfile: new ArmorProfile(
                        template.ArmorProvided,
                        template.StealthModifier,
                        template.CapacityModifier,
                        template.PreventsRunning));
            }

            foreach (AmmunitionType ammunitionType in ammunitionTypes.Values)
            {
                equipmentTemplates[GetAmmunitionPackageId(ammunitionType.Id)] = new EquipmentTemplate(
                    GetAmmunitionPackageId(ammunitionType.Id),
                    $"{ammunitionType.Name} package",
                    carryCost: 0.25f,
                    tags: EquipmentTags.Ammunition | EquipmentTags.AmmunitionCarrier,
                    ammunitionProfile: new AmmunitionPackageProfile(
                        ammunitionType,
                        roundsPerPackage[ammunitionType.Id]));
            }

            Dictionary<int, EquipmentKitTemplate> kits = [];
            foreach (WeaponSet set in weaponSets?.Values ?? Array.Empty<WeaponSet>())
            {
                List<EquipmentKitEntry> entries = [];
                AddRanged(entries, set.PrimaryRangedWeapon, equipmentTemplates);
                AddRanged(entries, set.SecondaryRangedWeapon, equipmentTemplates);
                AddMelee(entries, set.PrimaryMeleeWeapon, equipmentTemplates);
                AddMelee(entries, set.SecondaryMeleeWeapon, equipmentTemplates);
                AddRanged(entries, set.GrenadeWeapon, equipmentTemplates, isGrenade: true);
                foreach (EquipmentKitEntry weaponEntry in entries.ToList())
                {
                    RangedWeaponProfile profile = weaponEntry.Equipment.RangedProfile;
                    if (profile?.AmmunitionType == null
                        || profile.AmmunitionBehavior is AmmunitionBehavior.SelfRegenerating
                            or AmmunitionBehavior.Unlimited
                            or AmmunitionBehavior.ConsumableItem)
                    {
                        continue;
                    }
                    EquipmentTemplate package = equipmentTemplates[GetAmmunitionPackageId(profile.AmmunitionType.Id)];
                    entries.Add(new EquipmentKitEntry(package, StandardSpareMagazines));
                }

                kits[GetKitId(set.Id)] = new EquipmentKitTemplate(
                    GetKitId(set.Id),
                    set.Name,
                    items: entries);
            }

            Dictionary<int, PersonalEquipmentRole> roles = [];
            foreach (SquadTemplateElement element in (squadTemplates ?? Array.Empty<SquadTemplate>())
                .SelectMany(template => template.Elements)
                .Where(element => element.PersonalEquipmentRole != null))
            {
                PersonalEquipmentRole role = element.PersonalEquipmentRole;
                if (!kits.ContainsKey(role.DefaultKitId))
                {
                    // Authored role ids are already rules-global; this branch only guards a
                    // partially migrated test fixture and keeps the catalog diagnostic useful.
                    continue;
                }
                roles[role.Id] = role;
            }
            return new EquipmentRulesCatalog(equipmentTemplates, ammunitionTypes, kits, roles);
        }

        private static void AddRanged(
            ICollection<EquipmentKitEntry> entries,
            RangedWeaponTemplate template,
            IReadOnlyDictionary<int, EquipmentTemplate> equipmentTemplates,
            bool isGrenade = false)
        {
            if (template == null) return;
            EquipmentTemplate equipment = equipmentTemplates[GetRangedEquipmentId(template.Id)];
            entries.Add(new EquipmentKitEntry(
                equipment,
                isGrenade && template.AmmunitionBehavior == AmmunitionBehavior.ConsumableItem ? 1 : 1,
                isGrenade ? null : entries.Count));
        }

        private static void AddMelee(
            ICollection<EquipmentKitEntry> entries,
            MeleeWeaponTemplate template,
            IReadOnlyDictionary<int, EquipmentTemplate> equipmentTemplates)
        {
            if (template == null) return;
            entries.Add(new EquipmentKitEntry(
                equipmentTemplates[GetMeleeEquipmentId(template.Id)],
                1,
                entries.Count));
        }

        private static bool IsBiological(string name)
        {
            string value = name?.ToLowerInvariant() ?? string.Empty;
            return value.Contains("devourer")
                || value.Contains("deathspitter")
                || value.Contains("spinefist")
                || value.Contains("fleshborer")
                || value.Contains("symbiote")
                || value.Contains("bio-");
        }
    }
}
