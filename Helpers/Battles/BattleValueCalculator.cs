using System;
using OnlyWar.Helpers;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;
using SoldierAttribute = OnlyWar.Models.Soldiers.Attribute;

namespace OnlyWar.Helpers.Battles;

/// <summary>
/// Deterministic valuation of a representative soldier against a small reference
/// threat panel. This is intentionally an offline/data-calibration primitive; the
/// resulting value is persisted on SoldierTemplate and consumed by force generation.
/// </summary>
public static class BattleValueCalculator
{
    // BattleValue shares the strategic garrison unit scale. The engine-math score is normalized
    // against the reference profile, then compressed so legacy garrison growth and force pools
    // remain comparable after the melee rebalance.
    public const float CalibrationConstant = 10.0f / 14.0f;
    public const float ReferenceArmor = 5.0f;
    public const float ReferenceConstitution = 10.0f;
    public const float ReferenceSkill = 10.0f;
    public const float ReferenceTargetSize = 1.0f;
    public const float ReferenceRange = 2.0f;

    public sealed class Input
    {
        public float Strength { get; init; }
        public float Constitution { get; init; }
        public float AttackSpeed { get; init; }
        public float Size { get; init; }
        public float MeleeSkill { get; init; }
        public float RangedSkill { get; init; }
        public float MeleeEvasion { get; init; }
        public float RangedEvasion { get; init; }
        public float Armor { get; init; }
        public float VitalLocationCount { get; init; } = 1.0f;
        public float BodyLocationCount { get; init; } = 1.0f;
        public MeleeWeaponTemplate MeleeWeapon { get; init; }
        public RangedWeaponTemplate RangedWeapon { get; init; }
    }

    public readonly struct Result
    {
        public float Offense { get; }
        public float Durability { get; }
        public float NormalizedOffense { get; }
        public float NormalizedDurability { get; }
        public int BattleValue { get; }

        public Result(float offense, float durability, float normalizedOffense,
                      float normalizedDurability, int battleValue)
        {
            Offense = offense;
            Durability = durability;
            NormalizedOffense = normalizedOffense;
            NormalizedDurability = normalizedDurability;
            BattleValue = battleValue;
        }
    }

    private static readonly MeleeWeaponTemplate ReferenceMeleeWeapon = new(
        0,
        "Battle Value Reference Weapon",
        EquipLocation.OneHand,
        new BaseSkill(0, SkillCategory.Melee, "Battle Value Reference Skill", SoldierAttribute.Strength, 0),
        accuracy: 0,
        armorMultiplier: 1,
        penetrationMultiplier: 1,
        requiredStrength: 0,
        strengthMultiplier: 1,
        parryMod: 0,
        attackSpeedMultiplier: 1);

    private static readonly Input ReferenceInput = new()
    {
        Strength = 10,
        Constitution = ReferenceConstitution,
        AttackSpeed = 10,
        Size = ReferenceTargetSize,
        MeleeSkill = ReferenceSkill,
        RangedSkill = ReferenceSkill,
        MeleeEvasion = 0,
        RangedEvasion = 0,
        Armor = ReferenceArmor,
        VitalLocationCount = 2,
        BodyLocationCount = 8,
        MeleeWeapon = ReferenceMeleeWeapon,
        RangedWeapon = null
    };

    private static readonly float ReferenceOffense = CalculateRawOffense(ReferenceInput);
    private static readonly float ReferenceDurability = CalculateRawDurability(ReferenceInput);

    public static Result Calculate(Input input)
    {
        if (input == null)
        {
            return new Result(0, 0, 0, 0, 0);
        }

        float offense = CalculateRawOffense(input);
        float durability = CalculateRawDurability(input);
        float normalizedOffense = Math.Max(0.01f, offense / ReferenceOffense);
        float normalizedDurability = Math.Max(0.01f, durability / ReferenceDurability);
        float rawValue = CalibrationConstant * (float)Math.Sqrt(normalizedOffense * normalizedDurability);
        int battleValue = Math.Max(1, (int)Math.Round(rawValue, MidpointRounding.AwayFromZero));
        return new Result(offense, durability, normalizedOffense, normalizedDurability, battleValue);
    }

    private static float CalculateRawOffense(Input input)
    {
        float meleeScore = 0;
        if (input.MeleeWeapon != null)
        {
            float hitProbability = MeleeMath.CalculateContestedHitProbability(
                input.MeleeSkill,
                input.MeleeWeapon.Accuracy,
                didMove: false,
                ReferenceSkill,
                defenderEvasion: 0);
            float attacks = Math.Max(0.1f, input.AttackSpeed / 10.0f
                * input.MeleeWeapon.AttackSpeedMultiplier);
            float damageRatio = CalculateMeleeDamageRatio(input, input.MeleeWeapon);
            meleeScore = attacks * hitProbability * damageRatio;
        }

        float rangedScore = 0;
        if (input.RangedWeapon != null)
        {
            float range = Math.Clamp(ReferenceRange, 0.1f, Math.Max(0.1f, input.RangedWeapon.MaximumRange));
            float toHit = input.RangedSkill
                + input.RangedWeapon.Accuracy
                + BattleModifiersUtil.CalculateRangeModifier(range, 0)
                + BattleModifiersUtil.CalculateSizeModifier(ReferenceTargetSize)
                - 10.5f;
            float hitProbability = Math.Clamp(GaussianCalculator.ApproximateNormalCDF(toHit), 0, 1);
            float damage = BattleModifiersUtil.CalculateDamageAtRange(
                new RangedWeapon(input.RangedWeapon), range) * 3.5f;
            float penetratingDamage = Math.Max(0, damage
                - ReferenceArmor * input.RangedWeapon.ArmorMultiplier);
            float damageRatio = penetratingDamage * input.RangedWeapon.WoundMultiplier
                / ReferenceConstitution;
            rangedScore = Math.Max(1, (int)input.RangedWeapon.RateOfFire) * hitProbability
                * Math.Max(0, damageRatio);
        }

        return Math.Max(0.01f, Math.Max(meleeScore, rangedScore));
    }

    private static float CalculateMeleeDamageRatio(Input input, MeleeWeaponTemplate weapon)
    {
        float damage = input.Strength * weapon.StrengthMultiplier * 3.5f;
        float penetratingDamage = Math.Max(0, damage - ReferenceArmor * weapon.ArmorMultiplier);
        return Math.Max(0, penetratingDamage * weapon.WoundMultiplier / ReferenceConstitution);
    }

    private static float CalculateRawDurability(Input input)
    {
        float constitution = Math.Max(1, input.Constitution);
        float armorFactor = 1.0f + Math.Max(0, input.Armor) / ReferenceArmor;
        float bodyFactor = 1.0f
            + Math.Max(0, input.VitalLocationCount) * 0.15f
            + Math.Max(0, input.BodyLocationCount) * 0.02f;
        float evasionFactor = 1.0f
            + Math.Max(0, input.MeleeEvasion + input.RangedEvasion) / 8.0f;
        float sizeFactor = Math.Max(0.5f, input.Size);
        return constitution * armorFactor * bodyFactor * evasionFactor * sizeFactor;
    }
}
