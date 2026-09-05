using System.Collections.Generic;

namespace OnlyWar.Models.Equippables
{
    public class WeaponSet
    {
        public int Id { get; }
        public string Name { get; }
        public RangedWeaponTemplate PrimaryRangedWeapon { get; }
        public RangedWeaponTemplate SecondaryRangedWeapon { get; }
        public MeleeWeaponTemplate PrimaryMeleeWeapon { get; }
        public MeleeWeaponTemplate SecondaryMeleeWeapon { get; }
        public RangedWeaponTemplate GrenadeWeapon { get; }
        public IReadOnlyCollection<RangedWeapon> GetRangedWeapons()
        {
            if (PrimaryRangedWeapon == null && GrenadeWeapon == null) return null;
            List<RangedWeapon> list = [];
            if (PrimaryRangedWeapon != null)
            {
                list.Add(new RangedWeapon(PrimaryRangedWeapon));
            }
            if (SecondaryRangedWeapon != null)
            {
                list.Add(new RangedWeapon(SecondaryRangedWeapon));
            }
            if (GrenadeWeapon != null)
            {
                list.Add(new RangedWeapon(GrenadeWeapon));
            }
            return list;
        }
        public IReadOnlyCollection<MeleeWeapon> GetMeleeWeapons()
        {
            if (PrimaryMeleeWeapon == null) return null;
            List<MeleeWeapon> list =
            [
                new MeleeWeapon(PrimaryMeleeWeapon)
            ];
            if (SecondaryMeleeWeapon != null)
            {
                list.Add(new MeleeWeapon(SecondaryMeleeWeapon));
            }
            return list;
        }
        public WeaponSet(int id, string name,
                         RangedWeaponTemplate primaryRanged = null, RangedWeaponTemplate secondaryRanged = null,
                         MeleeWeaponTemplate primaryMelee = null, MeleeWeaponTemplate secondaryMelee = null,
                         RangedWeaponTemplate grenadeWeapon = null)
        {
            Id = id;
            Name = name;
            PrimaryRangedWeapon = primaryRanged;
            SecondaryRangedWeapon = secondaryRanged;
            PrimaryMeleeWeapon = primaryMelee;
            SecondaryMeleeWeapon = secondaryMelee;
            GrenadeWeapon = grenadeWeapon;
        }
    }
}
