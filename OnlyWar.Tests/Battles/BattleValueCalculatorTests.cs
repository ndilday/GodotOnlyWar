using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Equippables;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

public class BattleValueCalculatorTests
{
    [Fact]
    public void ReferenceProfile_IsStableAndPositive()
    {
        BattleValueCalculator.Result first = BattleValueCalculator.Calculate(new BattleValueCalculator.Input
        {
            Strength = 10,
            Constitution = 10,
            AttackSpeed = 10,
            Size = 1,
            MeleeSkill = 10,
            Armor = 5,
            VitalLocationCount = 2,
            BodyLocationCount = 8,
            MeleeWeapon = TestModelFactory.DefaultWeapons.PrimaryMeleeWeapon
        });
        BattleValueCalculator.Result second = BattleValueCalculator.Calculate(new BattleValueCalculator.Input
        {
            Strength = 10,
            Constitution = 10,
            AttackSpeed = 10,
            Size = 1,
            MeleeSkill = 10,
            Armor = 5,
            VitalLocationCount = 2,
            BodyLocationCount = 8,
            MeleeWeapon = TestModelFactory.DefaultWeapons.PrimaryMeleeWeapon
        });

        Assert.Equal(first.BattleValue, second.BattleValue);
        Assert.True(first.BattleValue > 0);
        Assert.True(first.NormalizedOffense > 0);
        Assert.True(first.NormalizedDurability > 0);
    }

    [Fact]
    public void FasterAndStrongerCombatant_IsValuedHigher()
    {
        BattleValueCalculator.Result baseline = BattleValueCalculator.Calculate(new BattleValueCalculator.Input
        {
            Strength = 10,
            Constitution = 10,
            AttackSpeed = 10,
            Size = 1,
            MeleeSkill = 10,
            Armor = 5,
            MeleeWeapon = TestModelFactory.DefaultWeapons.PrimaryMeleeWeapon
        });
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
}
