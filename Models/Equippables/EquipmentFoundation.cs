using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;

namespace OnlyWar.Models.Equippables
{
    /// <summary>
    /// Broad equipment interactions. Rules that need an exact eligibility decision use
    /// <see cref="EquipmentRequirement"/> instead of growing this flag set into a scripting
    /// language.
    /// </summary>
    [Flags]
    public enum EquipmentTags
    {
        None = 0,
        Weapon = 1 << 0,
        Armor = 1 << 1,
        Ammunition = 1 << 2,
        Consumable = 1 << 3,
        TwoHanded = 1 << 4,
        Relic = 1 << 5,
        Biological = 1 << 6,
        AmmunitionCarrier = 1 << 7,
        Gear = 1 << 8,
        Ranged = 1 << 9,
        Melee = 1 << 10
    }

    public enum AmmunitionBehavior
    {
        Magazine = 0,
        Incremental = 1,
        SelfRegenerating = 2,
        Unlimited = 3,
        ConsumableItem = 4
    }

    public enum AmmunitionConsumptionRule
    {
        PerShot = 0,
        PerAttack = 1
    }

    public enum EquipmentRequirementKind
    {
        Faction = 0,
        Species = 1,
        PersonalEquipmentRole = 2,
        SoldierTemplate = 3,
        MinimumStrength = 4,
        RequiredSkill = 5,
        RequiredEquipmentTag = 6,
        ProhibitedEquipmentTag = 7,
        MaximumDuplicateCount = 8
    }

    /// <summary>
    /// A data-authored eligibility rule. A row may carry either an allow-list or a scalar value,
    /// depending on its kind. Empty allow-lists are intentionally unrestricted, which lets rules
    /// data omit dimensions it does not want to constrain.
    /// </summary>
    public sealed class EquipmentRequirement
    {
        public EquipmentRequirementKind Kind { get; }
        public IReadOnlyList<int> AllowedIds { get; }
        public float MinimumValue { get; }
        public string SkillName { get; }
        public EquipmentTags EquipmentTag { get; }
        public int MaximumDuplicates { get; }

        public EquipmentRequirement(
            EquipmentRequirementKind kind,
            IEnumerable<int> allowedIds = null,
            float minimumValue = 0,
            string skillName = null,
            EquipmentTags equipmentTag = EquipmentTags.None,
            int maximumDuplicates = 0)
        {
            Kind = kind;
            AllowedIds = new ReadOnlyCollection<int>((allowedIds ?? Array.Empty<int>()).Distinct().ToList());
            MinimumValue = minimumValue;
            SkillName = skillName;
            EquipmentTag = equipmentTag;
            MaximumDuplicates = maximumDuplicates;
        }

        public EquipmentRequirement(EquipmentRequirementKind kind, int value)
            : this(
                kind,
                kind is EquipmentRequirementKind.MinimumStrength
                    or EquipmentRequirementKind.MaximumDuplicateCount
                    ? null
                    : new[] { value },
                kind == EquipmentRequirementKind.MinimumStrength ? value : 0,
                maximumDuplicates: kind == EquipmentRequirementKind.MaximumDuplicateCount ? value : 0)
        {
        }

        public EquipmentRequirement(EquipmentRequirementKind kind, string value)
            : this(kind, skillName: value)
        {
        }

        public static EquipmentRequirement FactionAllowList(params int[] ids) =>
            new(EquipmentRequirementKind.Faction, ids);

        public static EquipmentRequirement SpeciesAllowList(params int[] ids) =>
            new(EquipmentRequirementKind.Species, ids);

        public static EquipmentRequirement RoleAllowList(params int[] ids) =>
            new(EquipmentRequirementKind.PersonalEquipmentRole, ids);

        public static EquipmentRequirement SoldierTemplateAllowList(params int[] ids) =>
            new(EquipmentRequirementKind.SoldierTemplate, ids);

        public static EquipmentRequirement MinimumStrength(float value) =>
            new(EquipmentRequirementKind.MinimumStrength, minimumValue: value);

        public static EquipmentRequirement RequiredSkill(string name) =>
            new(EquipmentRequirementKind.RequiredSkill, skillName: name);

        public static EquipmentRequirement RequiredTag(EquipmentTags tag) =>
            new(EquipmentRequirementKind.RequiredEquipmentTag, equipmentTag: tag);

        public static EquipmentRequirement ProhibitedTag(EquipmentTags tag) =>
            new(EquipmentRequirementKind.ProhibitedEquipmentTag, equipmentTag: tag);

        public static EquipmentRequirement MaxDuplicates(int count) =>
            new(EquipmentRequirementKind.MaximumDuplicateCount, maximumDuplicates: count);
    }

    public sealed class AmmunitionType : IEquatable<AmmunitionType>
    {
        public int Id { get; }
        public string Name { get; }

        public AmmunitionType(int id, string name)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            Id = id;
            Name = string.IsNullOrWhiteSpace(name) ? $"Ammunition {id}" : name;
        }

        public bool Equals(AmmunitionType other) => other != null && Id == other.Id;
        public override bool Equals(object obj) => Equals(obj as AmmunitionType);
        public override int GetHashCode() => Id;
        public override string ToString() => Name;
    }

    public sealed class RangedWeaponProfile
    {
        public BaseSkill RelatedSkill { get; }
        public float Accuracy { get; }
        public float ArmorMultiplier { get; }
        public float WoundMultiplier { get; }
        public float RequiredStrength { get; }
        public float DamageMultiplier { get; }
        public float MaximumRange { get; }
        public byte RateOfFire { get; }
        public ushort LoadedCapacity { get; }
        public ushort Recoil { get; }
        public ushort Bulk { get; }
        public bool DoesDamageDegradeWithRange { get; }
        public byte TemplateType { get; }
        public float AreaRadius { get; }
        public EquipLocation Location { get; }
        public AmmunitionType AmmunitionType { get; }
        public AmmunitionBehavior AmmunitionBehavior { get; }
        public AmmunitionConsumptionRule ConsumptionRule { get; }
        public ushort ReloadDuration { get; }
        public ushort ReloadAmount { get; }
        public ushort RecoveryDuration { get; }
        public ushort RecoveryAmount { get; }

        public bool IsTemplateWeapon => TemplateType != 0;
        public bool IsConeWeapon => TemplateType == 1;
        public bool IsBlastWeapon => TemplateType is 2 or 3;
        public bool IsThrown => TemplateType == 3;
        public int HandGroupsRequired => TagsRequireTwoHands() ? 2 : 1;

        public RangedWeaponProfile(
            BaseSkill relatedSkill,
            float accuracy = 0,
            float armorMultiplier = 1,
            float woundMultiplier = 1,
            float requiredStrength = 0,
            float damageMultiplier = 1,
            float maximumRange = 1,
            byte rateOfFire = 1,
            ushort loadedCapacity = 1,
            ushort recoil = 0,
            ushort bulk = 0,
            bool doesDamageDegradeWithRange = false,
            EquipLocation location = EquipLocation.OneHand,
            AmmunitionType ammunitionType = null,
            AmmunitionBehavior ammunitionBehavior = AmmunitionBehavior.Magazine,
            AmmunitionConsumptionRule consumptionRule = AmmunitionConsumptionRule.PerShot,
            ushort reloadDuration = 1,
            ushort reloadAmount = 0,
            ushort recoveryDuration = 0,
            ushort recoveryAmount = 0,
            byte templateType = 0,
            float areaRadius = 0)
        {
            RelatedSkill = relatedSkill;
            Accuracy = accuracy;
            ArmorMultiplier = armorMultiplier;
            WoundMultiplier = woundMultiplier;
            RequiredStrength = requiredStrength;
            DamageMultiplier = damageMultiplier;
            MaximumRange = maximumRange;
            RateOfFire = rateOfFire;
            LoadedCapacity = loadedCapacity;
            Recoil = recoil;
            Bulk = bulk;
            DoesDamageDegradeWithRange = doesDamageDegradeWithRange;
            Location = location;
            AmmunitionType = ammunitionType;
            AmmunitionBehavior = ammunitionBehavior;
            ConsumptionRule = consumptionRule;
            ReloadDuration = reloadDuration;
            ReloadAmount = reloadAmount == 0 ? loadedCapacity : reloadAmount;
            RecoveryDuration = recoveryDuration == 0 ? reloadDuration : recoveryDuration;
            RecoveryAmount = recoveryAmount == 0 ? loadedCapacity : recoveryAmount;
            TemplateType = templateType;
            AreaRadius = areaRadius;
        }

        private bool TagsRequireTwoHands() => Location == EquipLocation.TwoHand;
    }

    public sealed class MeleeWeaponProfile
    {
        public BaseSkill RelatedSkill { get; }
        public float Accuracy { get; }
        public float ArmorMultiplier { get; }
        public float WoundMultiplier { get; }
        public float RequiredStrength { get; }
        public float StrengthMultiplier { get; }
        public float ParryModifier { get; }
        public float AttackSpeedMultiplier { get; }
        public EquipLocation Location { get; }
        public int HandGroupsRequired => Location == EquipLocation.TwoHand ? 2 : 1;

        public MeleeWeaponProfile(
            BaseSkill relatedSkill,
            float accuracy = 0,
            float armorMultiplier = 1,
            float woundMultiplier = 1,
            float requiredStrength = 0,
            float strengthMultiplier = 1,
            float parryModifier = 0,
            float attackSpeedMultiplier = 1,
            EquipLocation location = EquipLocation.OneHand)
        {
            RelatedSkill = relatedSkill;
            Accuracy = accuracy;
            ArmorMultiplier = armorMultiplier;
            WoundMultiplier = woundMultiplier;
            RequiredStrength = requiredStrength;
            StrengthMultiplier = strengthMultiplier;
            ParryModifier = parryModifier;
            AttackSpeedMultiplier = attackSpeedMultiplier <= 0 ? 1 : attackSpeedMultiplier;
            Location = location;
        }
    }

    public sealed class ArmorProfile
    {
        public byte ArmorProvided { get; }
        public short StealthModifier { get; }
        public float CapacityModifier { get; }

        public ArmorProfile(byte armorProvided, short stealthModifier = 0, float capacityModifier = 0)
        {
            ArmorProvided = armorProvided;
            StealthModifier = stealthModifier;
            CapacityModifier = capacityModifier;
        }
    }

    public sealed class AmmunitionPackageProfile
    {
        public AmmunitionType AmmunitionType { get; }
        public int RoundsPerPackage { get; }

        public AmmunitionPackageProfile(AmmunitionType ammunitionType, int roundsPerPackage)
        {
            AmmunitionType = ammunitionType ?? throw new ArgumentNullException(nameof(ammunitionType));
            if (roundsPerPackage <= 0) throw new ArgumentOutOfRangeException(nameof(roundsPerPackage));
            RoundsPerPackage = roundsPerPackage;
        }
    }

    public sealed class GearProfile
    {
        public float CapacityBonus { get; }

        public GearProfile(float capacityBonus = 0)
        {
            if (capacityBonus < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacityBonus), "Gear capacity bonuses cannot be negative.");
            }
            CapacityBonus = capacityBonus;
        }
    }

    /// <summary>
    /// The globally identified, compositional rules identity for one physical equipment pattern.
    /// A template can carry several optional profiles, but runtime consumers should only execute
    /// the explicitly supported profile properties above.
    /// </summary>
    public sealed class EquipmentTemplate
    {
        public int Id { get; }
        public string Name { get; }
        public float CarryCost { get; }
        public int MaximumQuantity { get; }
        public EquipmentTags Tags { get; }
        public RangedWeaponProfile RangedProfile { get; }
        public MeleeWeaponProfile MeleeProfile { get; }
        public ArmorProfile ArmorProfile { get; }
        public AmmunitionPackageProfile AmmunitionProfile { get; }
        public GearProfile GearProfile { get; }
        public IReadOnlyList<EquipmentRequirement> Requirements { get; }

        public bool IsWeapon => RangedProfile != null || MeleeProfile != null || Tags.HasFlag(EquipmentTags.Weapon);
        public int HandGroupsRequired => Math.Max(
            RangedProfile?.HandGroupsRequired ?? 0,
            MeleeProfile?.HandGroupsRequired ?? 0);

        public EquipmentTemplate(
            int id,
            string name,
            float carryCost = 0,
            int maximumQuantity = int.MaxValue,
            EquipmentTags tags = EquipmentTags.None,
            RangedWeaponProfile rangedProfile = null,
            MeleeWeaponProfile meleeProfile = null,
            ArmorProfile armorProfile = null,
            AmmunitionPackageProfile ammunitionProfile = null,
            GearProfile gearProfile = null,
            IEnumerable<EquipmentRequirement> requirements = null)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Equipment needs a name.", nameof(name));
            if (carryCost < 0) throw new ArgumentOutOfRangeException(nameof(carryCost));
            if (maximumQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(maximumQuantity));
            if (gearProfile?.CapacityBonus < 0)
            {
                throw new ArgumentException("Gear capacity bonuses cannot be negative.", nameof(gearProfile));
            }

            Id = id;
            Name = name;
            CarryCost = carryCost;
            MaximumQuantity = maximumQuantity;
            RangedProfile = rangedProfile;
            MeleeProfile = meleeProfile;
            ArmorProfile = armorProfile;
            AmmunitionProfile = ammunitionProfile;
            GearProfile = gearProfile;
            Requirements = new ReadOnlyCollection<EquipmentRequirement>(
                (requirements ?? Array.Empty<EquipmentRequirement>()).Where(r => r != null).ToList());

            EquipmentTags inferred = tags;
            if (rangedProfile != null) inferred |= EquipmentTags.Weapon | EquipmentTags.Ranged;
            if (meleeProfile != null) inferred |= EquipmentTags.Weapon | EquipmentTags.Melee;
            if (armorProfile != null) inferred |= EquipmentTags.Armor;
            if (ammunitionProfile != null) inferred |= EquipmentTags.Ammunition | EquipmentTags.AmmunitionCarrier;
            if (gearProfile != null) inferred |= EquipmentTags.Gear;
            if (rangedProfile?.Location == EquipLocation.TwoHand || meleeProfile?.Location == EquipLocation.TwoHand)
            {
                inferred |= EquipmentTags.TwoHanded;
            }
            Tags = inferred;
        }

        public override string ToString() => Name;
    }

    public sealed record EquipmentKitEntry
    {
        public EquipmentTemplate Equipment { get; }
        public int Quantity { get; }
        public int? InitialReadyOrder { get; }

        public EquipmentKitEntry(
            EquipmentTemplate equipment,
            int quantity,
            int? initialReadyOrder = null)
        {
            if (equipment == null) throw new ArgumentNullException(nameof(equipment));
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            if (quantity > equipment.MaximumQuantity)
            {
                throw new ArgumentOutOfRangeException(nameof(quantity),
                    $"{equipment.Name} permits at most {equipment.MaximumQuantity} copies.");
            }
            Equipment = equipment;
            Quantity = quantity;
            InitialReadyOrder = initialReadyOrder;
        }
    }

    public sealed class EquipmentKitTemplate
    {
        public int Id { get; }
        public string Name { get; }
        public EquipmentTemplate Armor { get; }
        public IReadOnlyList<EquipmentKitEntry> Items { get; }

        public EquipmentKitTemplate(
            int id,
            string name,
            EquipmentTemplate armor = null,
            IEnumerable<EquipmentKitEntry> items = null)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Kit needs a name.", nameof(name));
            Id = id;
            Name = name;
            Armor = armor;
            Items = new ReadOnlyCollection<EquipmentKitEntry>(
                (items ?? Array.Empty<EquipmentKitEntry>()).Where(item => item != null).ToList());
        }

        public EquipmentLoadout ToLoadout() => new(Armor, Items.Select(item =>
            new EquipmentLoadoutEntry(item.Equipment, item.Quantity, item.InitialReadyOrder)));

        public override string ToString() => Name;
    }

    public sealed record EquipmentLoadoutEntry
    {
        public EquipmentTemplate Equipment { get; }
        public int Quantity { get; }
        public int? InitialReadyOrder { get; }

        public EquipmentLoadoutEntry(
            EquipmentTemplate equipment,
            int quantity,
            int? initialReadyOrder = null)
        {
            if (equipment == null) throw new ArgumentNullException(nameof(equipment));
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            if (quantity > equipment.MaximumQuantity)
            {
                throw new ArgumentOutOfRangeException(nameof(quantity),
                    $"{equipment.Name} permits at most {equipment.MaximumQuantity} copies.");
            }
            Equipment = equipment;
            Quantity = quantity;
            InitialReadyOrder = initialReadyOrder;
        }
    }

    public sealed class EquipmentLoadout : IEquatable<EquipmentLoadout>
    {
        public EquipmentTemplate Armor { get; }
        public IReadOnlyList<EquipmentLoadoutEntry> Items { get; }
        public EquipmentSignature Signature { get; }

        public EquipmentLoadout(
            EquipmentTemplate armor,
            IEnumerable<EquipmentLoadoutEntry> items)
        {
            Armor = armor;
            Items = new ReadOnlyCollection<EquipmentLoadoutEntry>(
                (items ?? Array.Empty<EquipmentLoadoutEntry>()).Where(item => item != null).ToList());
            Signature = EquipmentSignature.Create(this);
        }

        public EquipmentLoadout(EquipmentTemplate armor = null, params EquipmentLoadoutEntry[] items)
            : this(armor, (IEnumerable<EquipmentLoadoutEntry>)items)
        {
        }

        public bool Equals(EquipmentLoadout other) => other != null && Signature.Equals(other.Signature);
        public override bool Equals(object obj) => Equals(obj as EquipmentLoadout);
        public override int GetHashCode() => Signature.GetHashCode();
        public override string ToString() => Signature.ToString();
    }

    /// <summary>
    /// Stable value used for tactical Battle Value caching. It intentionally excludes loaded rounds,
    /// reserve quantities, wounds, aim, and other live mission state.
    /// </summary>
    public sealed class EquipmentSignature : IEquatable<EquipmentSignature>
    {
        private readonly string _value;
        public int? ArmorId { get; }
        public IReadOnlyList<(int EquipmentId, int Quantity, int? InitialReadyOrder)> Entries { get; }

        private EquipmentSignature(
            int? armorId,
            IReadOnlyList<(int EquipmentId, int Quantity, int? InitialReadyOrder)> entries)
        {
            ArmorId = armorId;
            Entries = entries;
            StringBuilder builder = new();
            builder.Append(armorId?.ToString(CultureInfo.InvariantCulture) ?? "-");
            foreach ((int equipmentId, int quantity, int? readyOrder) in entries)
            {
                builder.Append('|').Append(equipmentId.ToString(CultureInfo.InvariantCulture));
                builder.Append(':').Append(quantity.ToString(CultureInfo.InvariantCulture));
                builder.Append(':').Append(readyOrder?.ToString(CultureInfo.InvariantCulture) ?? "-");
            }
            _value = builder.ToString();
        }

        public static EquipmentSignature Create(EquipmentLoadout loadout)
        {
            if (loadout == null) throw new ArgumentNullException(nameof(loadout));
            IReadOnlyList<(int EquipmentId, int Quantity, int? InitialReadyOrder)> entries = loadout.Items
                .OrderBy(item => item.Equipment.Id)
                .ThenBy(item => item.Quantity)
                .ThenBy(item => item.InitialReadyOrder ?? int.MaxValue)
                .Select(item => (item.Equipment.Id, item.Quantity, item.InitialReadyOrder))
                .ToList();
            return new EquipmentSignature(loadout.Armor?.Id, entries);
        }

        public bool Equals(EquipmentSignature other) => other != null && _value == other._value;
        public override bool Equals(object obj) => Equals(obj as EquipmentSignature);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_value);
        public override string ToString() => _value;
    }

    public sealed class PersonalEquipmentRole
    {
        public int Id { get; }
        public string Name { get; }
        public int DefaultKitId { get; }
        public float CapacityModifier { get; }

        public PersonalEquipmentRole(int id, string name, int defaultKitId, float capacityModifier = 0)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (defaultKitId <= 0) throw new ArgumentOutOfRangeException(nameof(defaultKitId));
            Id = id;
            Name = string.IsNullOrWhiteSpace(name) ? $"Role {id}" : name;
            DefaultKitId = defaultKitId;
            CapacityModifier = capacityModifier;
        }
    }

    public sealed class EquipmentValidationContext
    {
        public int? FactionId { get; init; }
        public int? SpeciesId { get; init; }
        public int? SoldierTemplateId { get; init; }
        public PersonalEquipmentRole PersonalEquipmentRole { get; init; }
        public float Strength { get; init; }
        public IReadOnlySet<string> Skills { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public int HandGroups { get; init; } = 2;
        public float BaseCapacity { get; init; } = 16;
        public EquipmentTags CarriedTags { get; init; }
    }

    public sealed record EquipmentValidationIssue(string Code, string Message);

    public sealed class EquipmentValidationResult
    {
        public IReadOnlyList<EquipmentValidationIssue> Issues { get; }
        public bool IsValid => Issues.Count == 0;

        internal EquipmentValidationResult(IEnumerable<EquipmentValidationIssue> issues)
        {
            Issues = new ReadOnlyCollection<EquipmentValidationIssue>((issues ?? Array.Empty<EquipmentValidationIssue>()).ToList());
        }
    }

    /// <summary>
    /// The one eligibility/capacity implementation used by rule validation, deployment, and UI
    /// callers. The validator is deliberately deterministic and side-effect free.
    /// </summary>
    public static class EquipmentLoadoutValidator
    {
        public static EquipmentValidationResult Validate(
            EquipmentLoadout loadout,
            EquipmentValidationContext context = null)
        {
            List<EquipmentValidationIssue> issues = [];
            if (loadout == null)
            {
                issues.Add(new("loadout.missing", "A loadout is required."));
                return new EquipmentValidationResult(issues);
            }

            context ??= new EquipmentValidationContext();
            EquipmentTags loadoutTags = context.CarriedTags | (loadout.Armor?.Tags ?? EquipmentTags.None);
            foreach (EquipmentLoadoutEntry item in loadout.Items)
            {
                loadoutTags |= item?.Equipment?.Tags ?? EquipmentTags.None;
            }
            if (loadout.Armor != null)
            {
                ValidateEquipment(loadout.Armor, context, issues, "armor", isWornArmor: true, loadoutTags: loadoutTags);
            }

            Dictionary<int, int> quantities = [];
            HashSet<int> readyOrders = [];
            float usedCapacity = 0;
            float capacityBonus = loadout.Armor?.ArmorProfile?.CapacityModifier ?? 0;
            foreach (EquipmentLoadoutEntry entry in loadout.Items)
            {
                if (entry == null || entry.Equipment == null)
                {
                    issues.Add(new("item.missing", "Every loadout item must reference equipment."));
                    continue;
                }

                EquipmentTemplate equipment = entry.Equipment;
                ValidateEquipment(equipment, context, issues, "item", isWornArmor: false, loadoutTags: loadoutTags);
                if (equipment.ArmorProfile != null)
                {
                    issues.Add(new("item.armor", $"{equipment.Name} is armor and must occupy the armor slot."));
                }

                quantities[equipment.Id] = quantities.GetValueOrDefault(equipment.Id) + entry.Quantity;
                if (quantities[equipment.Id] > equipment.MaximumQuantity)
                {
                    issues.Add(new("item.maximum_quantity",
                        $"{equipment.Name} exceeds its maximum quantity of {equipment.MaximumQuantity}."));
                }

                usedCapacity += equipment.CarryCost * entry.Quantity;
                capacityBonus += equipment.GearProfile?.CapacityBonus ?? 0;

                if (entry.InitialReadyOrder is int readyOrder)
                {
                    if (readyOrder < 0 || !readyOrders.Add(readyOrder))
                    {
                        issues.Add(new("ready_order.duplicate", $"Ready order {readyOrder} is not unique."));
                    }
                    if (!equipment.IsWeapon)
                    {
                        issues.Add(new("ready_order.non_weapon", $"{equipment.Name} is not a weapon."));
                    }
                    if (equipment.HandGroupsRequired > context.HandGroups)
                    {
                        issues.Add(new("ready_order.hands",
                            $"{equipment.Name} needs {equipment.HandGroupsRequired} hand groups "
                            + $"but the soldier has {context.HandGroups}."));
                    }
                }
            }

            foreach (EquipmentLoadoutEntry entry in loadout.Items)
            {
                if (entry?.Equipment == null) continue;
                foreach (EquipmentRequirement requirement in entry.Equipment.Requirements)
                {
                    if (requirement.Kind == EquipmentRequirementKind.MaximumDuplicateCount
                        && quantities.GetValueOrDefault(entry.Equipment.Id) > requirement.MaximumDuplicates)
                    {
                        issues.Add(new("requirement.maximum_duplicates",
                            $"{entry.Equipment.Name} allows at most {requirement.MaximumDuplicates} copies."));
                    }
                }
            }

            float availableCapacity = context.BaseCapacity
                + (context.PersonalEquipmentRole?.CapacityModifier ?? 0)
                + capacityBonus;
            if (availableCapacity < 0)
            {
                issues.Add(new("capacity.negative", "The resolved loadout has negative carrying capacity."));
            }
            if (usedCapacity > availableCapacity + 0.0001f)
            {
                issues.Add(new("capacity.exceeded",
                    $"Load uses {usedCapacity:0.##} capacity but only {availableCapacity:0.##} is available."));
            }
            return new EquipmentValidationResult(issues);
        }

        public static EquipmentValidationResult Validate(
            EquipmentKitTemplate kit,
            EquipmentValidationContext context = null) =>
            Validate(kit?.ToLoadout(), context);

        public static float GetUsedCapacity(EquipmentLoadout loadout) =>
            loadout?.Items.Sum(item => item.Equipment.CarryCost * item.Quantity) ?? 0;

        public static float GetAvailableCapacity(
            EquipmentLoadout loadout,
            EquipmentValidationContext context = null)
        {
            context ??= new EquipmentValidationContext();
            return context.BaseCapacity
                + (context.PersonalEquipmentRole?.CapacityModifier ?? 0)
                + (loadout?.Armor?.ArmorProfile?.CapacityModifier ?? 0)
                + (loadout?.Items.Sum(item => item.Equipment.GearProfile?.CapacityBonus ?? 0) ?? 0);
        }

        private static void ValidateEquipment(
            EquipmentTemplate equipment,
            EquipmentValidationContext context,
            ICollection<EquipmentValidationIssue> issues,
            string scope,
            bool isWornArmor,
            EquipmentTags loadoutTags)
        {
            if (equipment == null)
            {
                issues.Add(new($"{scope}.missing", "Equipment is required."));
                return;
            }
            if (equipment.CarryCost < 0)
            {
                issues.Add(new("equipment.negative_cost", $"{equipment.Name} has a negative carry cost."));
            }
            if (equipment.GearProfile?.CapacityBonus < 0)
            {
                issues.Add(new("equipment.negative_capacity_bonus", $"{equipment.Name} has a negative capacity bonus."));
            }
            foreach (EquipmentRequirement requirement in equipment.Requirements)
            {
                bool satisfied = requirement.Kind switch
                {
                    EquipmentRequirementKind.Faction => IsAllowed(requirement.AllowedIds, context.FactionId),
                    EquipmentRequirementKind.Species => IsAllowed(requirement.AllowedIds, context.SpeciesId),
                    EquipmentRequirementKind.PersonalEquipmentRole =>
                        IsAllowed(requirement.AllowedIds, context.PersonalEquipmentRole?.Id),
                    EquipmentRequirementKind.SoldierTemplate => IsAllowed(requirement.AllowedIds, context.SoldierTemplateId),
                    EquipmentRequirementKind.MinimumStrength => context.Strength >= requirement.MinimumValue,
                    EquipmentRequirementKind.RequiredSkill => string.IsNullOrWhiteSpace(requirement.SkillName)
                        || context.Skills.Contains(requirement.SkillName),
                    EquipmentRequirementKind.RequiredEquipmentTag =>
                        (loadoutTags & requirement.EquipmentTag) == requirement.EquipmentTag,
                    EquipmentRequirementKind.ProhibitedEquipmentTag =>
                        (loadoutTags & requirement.EquipmentTag) == 0,
                    EquipmentRequirementKind.MaximumDuplicateCount => requirement.MaximumDuplicates > 0,
                    _ => false
                };
                if (!satisfied)
                {
                    issues.Add(new("requirement.unsatisfied",
                        $"{equipment.Name} fails its {requirement.Kind} requirement."));
                }
            }

            if (isWornArmor && equipment.ArmorProfile == null)
            {
                issues.Add(new("armor.profile", $"{equipment.Name} has no armor profile."));
            }
        }

        private static bool IsAllowed(IReadOnlyList<int> allowed, int? value) =>
            allowed.Count == 0 || value.HasValue && allowed.Contains(value.Value);
    }
}
