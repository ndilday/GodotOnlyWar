using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// Tactical value derived from an immutable equipment signature. The template's BattleValue
    /// remains the intrinsic strategic value; live ammunition and wounds are deliberately absent
    /// from this cache key.
    /// </summary>
    public static class EffectiveBattleValueCalculator
    {
        private static readonly ConcurrentDictionary<(int TemplateId, string Signature), int> Cache = [];

        public static int Calculate(SoldierTemplate template, EquipmentLoadout loadout)
        {
            if (template == null) return 0;
            loadout ??= new EquipmentLoadout();
            string signature = loadout.Signature.ToString();
            return Cache.GetOrAdd((template.Id, signature), _ => CalculateUncached(template, loadout));
        }

        public static int CalculateRuntime(
            ISoldier soldier,
            Armor armor,
            IEnumerable<RangedWeapon> rangedWeapons,
            IEnumerable<MeleeWeapon> meleeWeapons)
        {
            if (soldier?.Template == null) return 0;
            int value = Math.Max(1, soldier.Template.BattleValue);
            value += armor?.Template == null ? 0 : Math.Max(0, armor.Template.ArmorProvided / 2);
            value += (rangedWeapons ?? Array.Empty<RangedWeapon>()).Sum(WeaponContribution);
            value += (meleeWeapons ?? Array.Empty<MeleeWeapon>()).Sum(WeaponContribution);
            return Math.Max(1, value);
        }

        public static void ClearCache() => Cache.Clear();

        private static int CalculateUncached(SoldierTemplate template, EquipmentLoadout loadout)
        {
            int value = Math.Max(1, template.BattleValue);
            if (loadout.Armor?.ArmorProfile != null)
            {
                value += Math.Max(0, loadout.Armor.ArmorProfile.ArmorProvided / 2);
            }

            foreach (EquipmentLoadoutEntry entry in loadout.Items)
            {
                int contribution = entry.Equipment.RangedProfile != null
                    ? RangedContribution(entry.Equipment.RangedProfile)
                    : entry.Equipment.MeleeProfile != null
                        ? MeleeContribution(entry.Equipment.MeleeProfile)
                        : Math.Max(0, (int)Math.Round(entry.Equipment.GearProfile?.CapacityBonus ?? 0));
                value += contribution * entry.Quantity;
            }
            return Math.Max(1, value);
        }

        private static int WeaponContribution(RangedWeapon weapon) => weapon?.Template == null
            ? 0
            : Math.Max(0, (int)Math.Round(
                weapon.Template.DamageMultiplier * Math.Max(1, (int)weapon.Template.RateOfFire) / 10f
                + weapon.Template.Accuracy / 2f));

        private static int WeaponContribution(MeleeWeapon weapon) => weapon?.Template == null
            ? 0
            : Math.Max(0, (int)Math.Round(
                weapon.Template.StrengthMultiplier * Math.Max(1, weapon.Template.AttackSpeedMultiplier) * 2f
                + weapon.Template.Accuracy / 2f));

        private static int RangedContribution(RangedWeaponProfile profile) => Math.Max(0,
            (int)Math.Round(profile.DamageMultiplier * Math.Max(1, (int)profile.RateOfFire) / 10f
                + profile.Accuracy / 2f
                + profile.LoadedCapacity / 10f));

        private static int MeleeContribution(MeleeWeaponProfile profile) => Math.Max(0,
            (int)Math.Round(profile.StrengthMultiplier * Math.Max(1, profile.AttackSpeedMultiplier) * 2f
                + profile.Accuracy / 2f));
    }
}
