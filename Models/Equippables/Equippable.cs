using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;

namespace OnlyWar.Models.Equippables
{
    public enum EquipLocation
    {
        Body = 0,
        OneHand = 1,
        TwoHand = 2
    }

    public class EquippableTemplate
    {
        public int Id { get; }
        public string Name { get; }
        public EquipLocation Location { get; }
        public EquippableTemplate(int id, string name, EquipLocation location)
        {
            Id = id;
            Name = name;
            Location = location;
        }
    }

    public class ArmorTemplate : EquippableTemplate
    {
        public byte ArmorProvided { get; }
        public short StealthModifier { get; }
        public float CapacityModifier { get; }
        public bool PreventsRunning { get; }
        public ArmorTemplate(
            int id,
            string name,
            byte armorProvided,
            short stealthModifier,
            float capacityModifier = 0,
            bool preventsRunning = false) : base(id, name, EquipLocation.Body)
        {
            ArmorProvided = armorProvided;
            StealthModifier = stealthModifier;
            CapacityModifier = capacityModifier;
            PreventsRunning = preventsRunning;
        }
    }

    public class WeaponTemplate : EquippableTemplate
    {
        public BaseSkill RelatedSkill { get; }
        public float Accuracy { get; }
        public float ArmorMultiplier { get; }
        public float WoundMultiplier { get; }
        public float RequiredStrength { get; }
        public WeaponTemplate(int id, string name, EquipLocation location,
                              BaseSkill skill, float accuracy, 
                              float armorMultiplier, float penetrationMultiplier,
                              float requiredStrength) : base(id, name, location)
        {
            RelatedSkill = skill;
            Accuracy = accuracy;
            ArmorMultiplier = armorMultiplier;
            WoundMultiplier = penetrationMultiplier;
            RequiredStrength = requiredStrength;
        }
    }

    public class RangedWeaponTemplate: WeaponTemplate
    {
        public float DamageMultiplier { get; }
        public float MaximumRange { get; }
        public byte RateOfFire { get; }
        public ushort AmmoCapacity { get; }
        public ushort Recoil { get; }
        public ushort Bulk { get; }
        public bool DoesDamageDegradeWithRange { get; }
        public ushort ReloadTime { get; }
        public AmmunitionType AmmunitionType { get; }
        public AmmunitionBehavior AmmunitionBehavior { get; }
        public AmmunitionConsumptionRule ConsumptionRule { get; }
        public ushort ReloadAmount { get; }
        public ushort RecoveryDuration { get; }
        public ushort RecoveryAmount { get; }
        public byte TemplateType { get; }
        public float AreaRadius { get; }
        // TemplateType: 0 normal, 1 cone (flamer), 2 launched blast, 3 thrown blast
        public bool IsTemplateWeapon => TemplateType != 0;
        public bool IsConeWeapon => TemplateType == 1;
        public bool IsBlastWeapon => TemplateType == 2 || TemplateType == 3;
        public bool IsThrown => TemplateType == 3;

        public RangedWeaponTemplate(int id, string name, EquipLocation location,
                              BaseSkill skill, float accuracy,
                              float armorMultiplier, float penetrationMultiplier,
                              float requiredStrength, float baseDamage,
                              float maxDistance, byte rof, ushort ammo,
                              ushort recoil, ushort bulk, bool doesDamageDegradeWithRange, ushort reloadTime,
                              byte templateType = 0, float areaRadius = 0,
                              AmmunitionType ammunitionType = null,
                              AmmunitionBehavior ammunitionBehavior = AmmunitionBehavior.Magazine,
                              AmmunitionConsumptionRule consumptionRule = AmmunitionConsumptionRule.PerShot,
                              ushort reloadAmount = 0,
                              ushort recoveryDuration = 0,
                              ushort recoveryAmount = 0)
                              : base(id, name, location, skill, accuracy, armorMultiplier, 
                                     penetrationMultiplier, requiredStrength)
        {
            DamageMultiplier = baseDamage;
            MaximumRange = maxDistance;
            RateOfFire = rof;
            AmmoCapacity = ammo;
            Recoil = recoil;
            Bulk = bulk;
            DoesDamageDegradeWithRange = doesDamageDegradeWithRange;
            ReloadTime = reloadTime;
            AmmunitionType = ammunitionType;
            AmmunitionBehavior = ammunitionBehavior;
            ConsumptionRule = consumptionRule;
            ReloadAmount = reloadAmount == 0 ? ammo : reloadAmount;
            RecoveryDuration = recoveryDuration == 0 ? reloadTime : recoveryDuration;
            RecoveryAmount = recoveryAmount == 0 ? ammo : recoveryAmount;
            TemplateType = templateType;
            AreaRadius = areaRadius;
        }

        public RangedWeaponProfile ToProfile() => new(
            RelatedSkill,
            Accuracy,
            ArmorMultiplier,
            WoundMultiplier,
            RequiredStrength,
            DamageMultiplier,
            MaximumRange,
            RateOfFire,
            AmmoCapacity,
            Recoil,
            Bulk,
            DoesDamageDegradeWithRange,
            Location,
            AmmunitionType,
            AmmunitionBehavior,
            ConsumptionRule,
            ReloadTime,
            ReloadAmount,
            RecoveryDuration,
            RecoveryAmount,
            TemplateType,
            AreaRadius);
    }

    public class MeleeWeaponTemplate: WeaponTemplate
    {
        public const float DefaultAttackSpeedMultiplier = 1.0f;

        public float StrengthMultiplier { get; }
        public float ParryModifier { get; }
        public float AttackSpeedMultiplier { get; }
        //public float Reach;

        public MeleeWeaponTemplate(int id, string name, EquipLocation location,
                              BaseSkill skill, float accuracy,
                              float armorMultiplier, float penetrationMultiplier,
                              float requiredStrength, float strengthMultiplier,
                              float parryMod, float attackSpeedMultiplier)
                              : base(id, name, location, skill, accuracy, armorMultiplier, 
                                     penetrationMultiplier, requiredStrength)
        {
            StrengthMultiplier = strengthMultiplier;
            ParryModifier = parryMod;
            AttackSpeedMultiplier = attackSpeedMultiplier <= 0
                ? DefaultAttackSpeedMultiplier
                : attackSpeedMultiplier;
        }
    }

    public class Equippable
    {
        public EquippableTemplate Template { get; private set; }
        public Equippable(EquippableTemplate template) { Template = template; }
    }

    public class Armor
    {
        public ArmorTemplate Template { get; private set; }
        public Armor(ArmorTemplate template) { Template = template; }
    }

    /// <summary>
    /// Mission-lifetime reserve ammunition shared by every weapon in one itemized loadout.
    /// Legacy WeaponSet weapons keep their existing weapon-local reserve instead.
    /// </summary>
    public sealed class AmmunitionReservePool
    {
        private readonly Dictionary<int, int> _roundsByType;

        public AmmunitionReservePool(IReadOnlyDictionary<int, int> roundsByType = null)
        {
            _roundsByType = roundsByType == null
                ? []
                : new Dictionary<int, int>(roundsByType);
        }

        public int Get(AmmunitionType ammunitionType) =>
            ammunitionType == null ? 0 : _roundsByType.GetValueOrDefault(ammunitionType.Id);

        public void Set(AmmunitionType ammunitionType, int rounds)
        {
            if (ammunitionType == null) return;
            if (rounds <= 0) _roundsByType.Remove(ammunitionType.Id);
            else _roundsByType[ammunitionType.Id] = rounds;
        }

        public AmmunitionReservePool DeepCopy() => new(_roundsByType);
    }

    public class RangedWeapon
    {
        public RangedWeaponTemplate Template { get; private set; }
        internal AmmunitionReservePool ReservePool { get; }
        public int? InitialReadyOrder { get; }
        private ushort _loadedAmmo;
        private int _localReserveAmmo;
        public ushort LoadedAmmo
        {
            get => _loadedAmmo;
            set => _loadedAmmo = (ushort)Math.Min(value, Template?.AmmoCapacity ?? value);
        }
        public int ReserveAmmo
        {
            get => ReservePool == null
                ? _localReserveAmmo
                : ReservePool.Get(Template.AmmunitionType);
            set
            {
                int rounds = Math.Max(0, value);
                if (ReservePool == null) _localReserveAmmo = rounds;
                else ReservePool.Set(Template.AmmunitionType, rounds);
            }
        }
        public ushort ReloadProgress { get; set; }
        public ushort RecoveryProgress { get; set; }
        public int ConsumableQuantity { get; set; }
        public RangedWeapon(
            RangedWeaponTemplate template,
            AmmunitionReservePool reservePool = null,
            int? initialReadyOrder = null)
        { 
            Template = template ?? throw new ArgumentNullException(nameof(template));
            ReservePool = reservePool;
            InitialReadyOrder = initialReadyOrder;
            LoadedAmmo = template.AmmunitionBehavior is AmmunitionBehavior.Unlimited
                or AmmunitionBehavior.ConsumableItem
                ? (ushort)0
                : template.AmmoCapacity;
            ConsumableQuantity = template.AmmunitionBehavior == AmmunitionBehavior.ConsumableItem
                ? 1
                : 0;
        }

        public bool IsUnlimited => Template.AmmunitionBehavior == AmmunitionBehavior.Unlimited;
        public bool IsConsumableItem => Template.AmmunitionBehavior == AmmunitionBehavior.ConsumableItem;
        public bool IsSelfRegenerating => Template.AmmunitionBehavior == AmmunitionBehavior.SelfRegenerating;
        public bool CanFire => IsUnlimited
            || IsConsumableItem && ConsumableQuantity > 0
            || !IsConsumableItem && LoadedAmmo > 0;
        public bool HasSoldierReload => Template.AmmunitionBehavior is AmmunitionBehavior.Magazine
            or AmmunitionBehavior.Incremental;

        public bool TryConsume(int units)
        {
            if (units <= 0 || IsUnlimited) return true;
            if (IsConsumableItem)
            {
                if (ConsumableQuantity < units) return false;
                ConsumableQuantity -= units;
                return true;
            }
            if (LoadedAmmo < units) return false;
            LoadedAmmo = (ushort)(LoadedAmmo - units);
            return true;
        }

        public int GetAmmunitionUnitsForAttack(int authoredShots) => authoredShots <= 0
            ? 0
            : Template.ConsumptionRule == AmmunitionConsumptionRule.PerAttack
                ? 1
                : authoredShots;

        public bool CanReload => HasSoldierReload
            && LoadedAmmo < Template.AmmoCapacity
            && (Template.AmmunitionType == null || ReserveAmmo > 0);

        public void AdvanceReload()
        {
            if (!CanReload) return;

            ReloadProgress++;
            int reloadTime = Math.Max(1, (int)Template.ReloadTime);
            if (ReloadProgress < reloadTime) return;

            if (Template.AmmunitionType == null)
            {
                LoadedAmmo = Template.AmmoCapacity;
            }
            else
            {
                int amount = Template.AmmunitionBehavior == AmmunitionBehavior.Incremental
                    ? Math.Max(1, (int)Template.ReloadAmount)
                    : Template.AmmoCapacity - LoadedAmmo;
                amount = Math.Min(amount, ReserveAmmo);
                amount = Math.Min(amount, Template.AmmoCapacity - LoadedAmmo);
                if (amount > 0)
                {
                    LoadedAmmo = (ushort)(LoadedAmmo + amount);
                    ReserveAmmo -= amount;
                }
            }

            ReloadProgress = 0;
        }

        public void AdvanceRecovery()
        {
            if (!IsSelfRegenerating || Template.RecoveryDuration == 0 || LoadedAmmo >= Template.AmmoCapacity)
            {
                return;
            }
            RecoveryProgress++;
            while (RecoveryProgress >= Template.RecoveryDuration && LoadedAmmo < Template.AmmoCapacity)
            {
                LoadedAmmo = (ushort)Math.Min(
                    Template.AmmoCapacity,
                    LoadedAmmo + Math.Max(1, (int)Template.RecoveryAmount));
                RecoveryProgress -= Template.RecoveryDuration;
            }
        }

        public RangedWeapon DeepCopy() =>
            DeepCopy(ReservePool?.DeepCopy());

        internal RangedWeapon DeepCopy(AmmunitionReservePool reservePool)
        {
            return new RangedWeapon(Template, reservePool, InitialReadyOrder)
            {
                LoadedAmmo = LoadedAmmo,
                ReserveAmmo = ReserveAmmo,
                ReloadProgress = ReloadProgress,
                RecoveryProgress = RecoveryProgress,
                ConsumableQuantity = ConsumableQuantity
            };
        }

        internal void CopyLiveStateFrom(RangedWeapon source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.Template.Id != Template.Id)
            {
                throw new ArgumentException("Cannot copy state between different weapon templates.", nameof(source));
            }
            LoadedAmmo = source.LoadedAmmo;
            ReserveAmmo = source.ReserveAmmo;
            ReloadProgress = source.ReloadProgress;
            RecoveryProgress = source.RecoveryProgress;
            ConsumableQuantity = source.ConsumableQuantity;
        }

        public override string ToString()
        {
            return Template.Name;
        }
    }

    public class MeleeWeapon
    {
        public MeleeWeaponTemplate Template { get; private set; }
        public int? InitialReadyOrder { get; }
        internal bool IsItemized { get; }
        public MeleeWeapon(
            MeleeWeaponTemplate template,
            int? initialReadyOrder = null,
            bool isItemized = false)
        {
            Template = template;
            InitialReadyOrder = initialReadyOrder;
            IsItemized = isItemized;
        }
        public override string ToString()
        {
            return Template.Name;
        }
    }
}
