using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Equippables;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

public class BattleValueCalculatorTests
{
    private static BattleValueCalculator.Input BaselineInput(MeleeWeaponTemplate meleeWeapon = null,
                                                             RangedWeaponTemplate rangedWeapon = null)
    {
        return new BattleValueCalculator.Input
        {
            Strength = 10,
            Constitution = 10,
            AttackSpeed = 10,
            Size = 1,
            MeleeSkill = 10,
            RangedSkill = 10,
            Armor = 5,
            MeleeWeapon = meleeWeapon,
            RangedWeapon = rangedWeapon
        };
    }

    [Fact]
    public void ReferenceProfile_IsStableAndPositive()
    {
        BattleValueCalculator.Result first = BattleValueCalculator.Calculate(
            BaselineInput(TestModelFactory.DefaultWeapons.PrimaryMeleeWeapon));
        BattleValueCalculator.Result second = BattleValueCalculator.Calculate(
            BaselineInput(TestModelFactory.DefaultWeapons.PrimaryMeleeWeapon));

        Assert.Equal(first.BattleValue, second.BattleValue);
        Assert.True(first.BattleValue > 0);
        Assert.True(first.NormalizedOffense > 0);
        Assert.True(first.NormalizedDurability > 0);
    }

    [Fact]
    public void FasterAndStrongerCombatant_IsValuedHigher()
    {
        BattleValueCalculator.Result baseline = BattleValueCalculator.Calculate(
            BaselineInput(TestModelFactory.DefaultWeapons.PrimaryMeleeWeapon));
        BattleValueCalculator.Result elite = BattleValueCalculator.Calculate(new BattleValueCalculator.Input
        {
            Strength = 20,
            Constitution = 20,
            AttackSpeed = 20,
            Size = 1.5f,
            MeleeSkill = 16,
            Armor = 10,
            MeleeWeapon = TestModelFactory.DefaultWeapons.PrimaryMeleeWeapon
        });

        Assert.True(elite.BattleValue > baseline.BattleValue);
    }

    [Fact]
    public void NullInput_ReturnsZeroProfile()
    {
        BattleValueCalculator.Result result = BattleValueCalculator.Calculate(null);

        Assert.Equal(0, result.BattleValue);
        Assert.Equal(0, result.Offense);
        Assert.Equal(0, result.Durability);
    }

    [Fact]
    public void HigherAttackSpeed_RaisesMeleeOffense()
    {
        BattleValueCalculator.Input slow = BaselineInput(TestModelFactory.DefaultWeapons.PrimaryMeleeWeapon);
        BattleValueCalculator.Result slowResult = BattleValueCalculator.Calculate(slow);
        BattleValueCalculator.Result fastResult = BattleValueCalculator.Calculate(new BattleValueCalculator.Input
        {
            Strength = slow.Strength,
            Constitution = slow.Constitution,
            AttackSpeed = 30,
            Size = slow.Size,
            MeleeSkill = slow.MeleeSkill,
            Armor = slow.Armor,
            MeleeWeapon = slow.MeleeWeapon
        });

        Assert.True(fastResult.Offense > slowResult.Offense);
        Assert.True(fastResult.BattleValue > slowResult.BattleValue);
    }

    [Fact]
    public void DualWielding_RaisesOffense()
    {
        MeleeWeaponTemplate weapon = TestModelFactory.DefaultWeapons.PrimaryMeleeWeapon;
        BattleValueCalculator.Result single = BattleValueCalculator.Calculate(BaselineInput(weapon));
        BattleValueCalculator.Input dualInput = BaselineInput(weapon);
        BattleValueCalculator.Result dual = BattleValueCalculator.Calculate(new BattleValueCalculator.Input
        {
            Strength = dualInput.Strength,
            Constitution = dualInput.Constitution,
            AttackSpeed = dualInput.AttackSpeed,
            Size = dualInput.Size,
            MeleeSkill = dualInput.MeleeSkill,
            Armor = dualInput.Armor,
            MeleeWeapon = weapon,
            SecondaryMeleeWeapon = weapon
        });

        Assert.True(dual.Offense > single.Offense);
    }

    [Fact]
    public void RangedVolley_OverkillIsCapped()
    {
        // A weapon that would statistically kill the softest panel member several times per
        // volley still only counts as one kill per volley (ShootAction targets one soldier),
        // so tripling its rate of fire must not triple offense.
        RangedWeaponTemplate baseline = TestModelFactory.DefaultWeapons.PrimaryRangedWeapon;
        RangedWeaponTemplate rapid = new(
            baseline.Id, baseline.Name, baseline.Location, baseline.RelatedSkill,
            baseline.Accuracy, baseline.ArmorMultiplier, baseline.WoundMultiplier,
            baseline.RequiredStrength, baseDamage: 30, maxDistance: baseline.MaximumRange,
            rof: 30, ammo: 300, recoil: 0, bulk: baseline.Bulk,
            doesDamageDegradeWithRange: false, reloadTime: baseline.ReloadTime);

        BattleValueCalculator.Result rapidResult = BattleValueCalculator.Calculate(
            BaselineInput(rangedWeapon: rapid));

        RangedWeaponTemplate overwhelming = new(
            baseline.Id, baseline.Name, baseline.Location, baseline.RelatedSkill,
            baseline.Accuracy, baseline.ArmorMultiplier, baseline.WoundMultiplier,
            baseline.RequiredStrength, baseDamage: 30, maxDistance: baseline.MaximumRange,
            rof: 90, ammo: 900, recoil: 0, bulk: baseline.Bulk,
            doesDamageDegradeWithRange: false, reloadTime: baseline.ReloadTime);

        BattleValueCalculator.Result overwhelmingResult = BattleValueCalculator.Calculate(
            BaselineInput(rangedWeapon: overwhelming));

        Assert.True(overwhelmingResult.Offense < rapidResult.Offense * 1.5f,
            $"Tripling RoF should not scale offense linearly: {rapidResult.Offense} -> {overwhelmingResult.Offense}");
    }

    [Fact]
    public void LargerCreature_IsEasierToHit_NotIntrinsicallyTankier()
    {
        // Same body, same armor, same constitution — the bigger silhouette should be worth
        // no more durability (in the engine, size only makes you easier to hit).
        MeleeWeaponTemplate weapon = TestModelFactory.DefaultWeapons.PrimaryMeleeWeapon;
        BattleValueCalculator.Input smallInput = BaselineInput(weapon);
        BattleValueCalculator.Result small = BattleValueCalculator.Calculate(smallInput);
        BattleValueCalculator.Result large = BattleValueCalculator.Calculate(new BattleValueCalculator.Input
        {
            Strength = smallInput.Strength,
            Constitution = smallInput.Constitution,
            AttackSpeed = smallInput.AttackSpeed,
            Size = 6,
            MeleeSkill = smallInput.MeleeSkill,
            Armor = smallInput.Armor,
            MeleeWeapon = weapon
        });

        Assert.True(large.Durability <= small.Durability,
            $"Size alone should not add durability: {small.Durability} -> {large.Durability}");
    }
}
