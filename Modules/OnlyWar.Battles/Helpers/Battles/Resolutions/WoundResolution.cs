using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Soldiers;

namespace OnlyWar.Battles.Resolutions
{
    public class WoundResolution
    {
        public BattleSoldier Inflicter { get; }
        public WeaponTemplate Weapon { get; }
        public BattleSoldier Suffererer { get; }
        public float Damage { get; }
        public HitLocation HitLocation { get; }
        public string Description { get; set; }

        public WoundResolution(BattleSoldier inflicter, WeaponTemplate weapon, BattleSoldier sufferer, float damage, HitLocation hitLocation)
        {
            Inflicter = inflicter;
            Weapon = weapon;
            Suffererer = sufferer;
            Damage = damage;
            HitLocation = hitLocation;
            Description = null;
        }
    }
}
