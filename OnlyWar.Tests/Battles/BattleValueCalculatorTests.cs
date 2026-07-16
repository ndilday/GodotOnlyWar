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
    public void BlastWeapons_DoNotCrashAndAreValuedAtZeroPendingDesignDecision()
    {
        // Blast valuation is an explicitly deferred design decision: the template
        // branch's fuel duty cycle zeroes out any weapon with FuelPerBurst 0, so a
        // grenade (TemplateType 3) or grenade launcher (TemplateType 2) currently
        // contributes nothing to the bearer's battle value.
        MeleeWeaponTemplate knife = TestModelFactory.DefaultWeapons.PrimaryMeleeWeapon;
        RangedWeaponTemplate launcher = new(
            99, "Test Grenade Launcher", EquipLocation.TwoHand, TestSkills.Ranged,
            accuracy: 0, armorMultiplier: 1, penetrationMultiplier: 1,
            requiredStrength: 0, baseDamage: 6, maxDistance: 1_000, rof: 1, ammo: 12,
            recoil: 0, bulk: 2, doesDamageDegradeWithRange: false, reloadTime: 3,
            templateType: 2, areaRadius: 6, fuelPerBurst: 0);

        BattleValueCalculator.Result bare = BattleValueCalculator.Calculate(BaselineInput(knife));
        BattleValueCalculator.Result thrown = BattleValueCalculator.Calculate(
            BaselineInput(knife, TestModelFactory.FragGrenadeTemplate));
        BattleValueCalculator.Result launched = BattleValueCalculator.Calculate(
            BaselineInput(knife, launcher));

        Assert.True(bare.BattleValue > 0);
        Assert.Equal(bare.BattleValue, thrown.BattleValue);
        Assert.Equal(bare.BattleValue, launched.BattleValue);
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
    public void TemplateWeapon_AutoHitIgnoresSkillAndUsesFuelDutyCycle()
    {
        RangedWeaponTemplate baseline = TestModelFactory.DefaultWeapons.PrimaryRangedWeapon;
        RangedWeaponTemplate smallTank = new(
            baseline.Id, "Template Flamer", baseline.Location, baseline.RelatedSkill,
            accuracy: 0, armorMultiplier: baseline.ArmorMultiplier,
            penetrationMultiplier: baseline.WoundMultiplier,
            requiredStrength: baseline.RequiredStrength, baseDamage: 10, maxDistance: 30,
            rof: 1, ammo: 10, recoil: 100, bulk: baseline.Bulk,
            doesDamageDegradeWithRange: false, reloadTime: 10,
            templateType: 1, areaRadius: 3, fuelPerBurst: 10);
        RangedWeaponTemplate largeTank = new(
            baseline.Id, "Template Flamer", baseline.Location, baseline.RelatedSkill,
            accuracy: 0, armorMultiplier: baseline.ArmorMultiplier,
            penetrationMultiplier: baseline.WoundMultiplier,
            requiredStrength: baseline.RequiredStrength, baseDamage: 10, maxDistance: 30,
            rof: 1, ammo: 100, recoil: 100, bulk: baseline.Bulk,
            doesDamageDegradeWithRange: false, reloadTime: 10,
            templateType: 1, areaRadius: 3, fuelPerBurst: 10);

        static BattleValueCalculator.Input InputFor(
            RangedWeaponTemplate weapon,
            float rangedSkill) => new()
        {
            Strength = 10,
            Constitution = 10,
            AttackSpeed = 10,
            Size = 1,
            MeleeSkill = 10,
            RangedSkill = rangedSkill,
            Armor = 5,
            RangedWeapon = weapon
        };

        BattleValueCalculator.Result unskilled = BattleValueCalculator.Calculate(
            InputFor(smallTank, rangedSkill: -100));
        BattleValueCalculator.Result expert = BattleValueCalculator.Calculate(
            InputFor(smallTank, rangedSkill: 100));
        BattleValueCalculator.Result sustained = BattleValueCalculator.Calculate(
            InputFor(largeTank, rangedSkill: -100));

        Assert.Equal(unskilled.Offense, expert.Offense, precision: 6);
        Assert.True(sustained.Offense > unskilled.Offense);
    }

    [Fact]
    public void Burrower_PaysNoMeleeClosingTax()
    {
        MeleeWeaponTemplate weapon = TestModelFactory.DefaultWeapons.PrimaryMeleeWeapon;
        BattleValueCalculator.Input walker = BaselineInput(weapon);
        BattleValueCalculator.Result walkerResult = BattleValueCalculator.Calculate(walker);
        BattleValueCalculator.Result burrowerResult = BattleValueCalculator.Calculate(new BattleValueCalculator.Input
        {
            Strength = walker.Strength,
            Constitution = walker.Constitution,
            AttackSpeed = walker.AttackSpeed,
            Size = walker.Size,
            MeleeSkill = walker.MeleeSkill,
            Armor = walker.Armor,
            MeleeWeapon = weapon,
            CanBurrow = true
        });

        // Same body, same weapon: erupting adjacent (attack on arrival) beats walking in
        // under fire by exactly the forgone closing tax (walker at MoveSpeed 6 keeps
        // 0.75 * 6/8 of its melee output; the burrower keeps all of it).
        float walkerClosing = BattleValueCalculator.MeleeClosingFactor
            * (walker.MoveSpeed / BattleValueCalculator.MeleeClosingReferenceSpeed);
        Assert.True(burrowerResult.Offense > walkerResult.Offense);
        Assert.Equal(walkerResult.Offense / walkerClosing, burrowerResult.Offense, precision: 3);
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
