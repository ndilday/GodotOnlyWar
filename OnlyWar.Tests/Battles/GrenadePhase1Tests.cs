using System.Linq;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

// Phase 1 (data layer) coverage for grenades: the WeaponSet grenade slot and the
// Strength-scaled effective-range helper. Blast geometry, actions, and planner
// behavior are covered by the later phases.
public class GrenadePhase1Tests
{
    [Fact]
    public void GetRangedWeapons_IncludesTheGrenadeSlot()
    {
        var weapons = TestModelFactory.GrenadierWeapons.GetRangedWeapons();

        Assert.Equal(2, weapons.Count);
        Assert.Contains(weapons, weapon => weapon.Template == TestModelFactory.FragGrenadeTemplate);
        // The single grenade arrives loaded.
        Assert.Equal(1, weapons.Single(weapon => weapon.Template.IsThrown).LoadedAmmo);
    }

    [Fact]
    public void GetRangedWeapons_ReturnsTheGrenadeWhenThereIsNoPrimaryRangedWeapon()
    {
        // Mirrors the melee-only marine sets (e.g. Eviscerator), which still carry a grenade.
        WeaponSet meleeOnlySet = new(3, "Test Melee + Grenade",
            primaryMelee: TestModelFactory.DefaultWeapons.PrimaryMeleeWeapon,
            grenadeWeapon: TestModelFactory.FragGrenadeTemplate);

        var weapons = meleeOnlySet.GetRangedWeapons();

        Assert.NotNull(weapons);
        RangedWeapon grenade = Assert.Single(weapons);
        Assert.Same(TestModelFactory.FragGrenadeTemplate, grenade.Template);
    }

    [Fact]
    public void DeepCopy_CarriesTheGrenadeSlot()
    {
        WeaponSet copy = TestModelFactory.GrenadierWeapons.DeepCopy();

        Assert.Same(TestModelFactory.FragGrenadeTemplate, copy.GrenadeWeapon);
    }

    [Fact]
    public void AddWeapons_GrenadeNeverOccupiesAHand()
    {
        // "Bolt Pistol + Chainsword"-style loadout: without the thrown-weapon exclusion,
        // the grenade filled the second hand and blocked the melee weapon from equipping.
        RangedWeaponTemplate pistol = new(4, "Test Pistol", EquipLocation.OneHand,
            TestSkills.Ranged, 0, 1, 1, 0, 5, 50, 1, 10, 0, 1, true, 1, 0, 0, 0);
        BattleSoldier soldier = new(TestModelFactory.CreateSoldier(), null);

        soldier.AddWeapons(
            [new RangedWeapon(pistol), new RangedWeapon(TestModelFactory.FragGrenadeTemplate)],
            [new MeleeWeapon(TestModelFactory.DefaultWeapons.PrimaryMeleeWeapon)]);

        RangedWeapon equippedRanged = Assert.Single(soldier.EquippedRangedWeapons);
        Assert.Same(pistol, equippedRanged.Template);
        Assert.Single(soldier.EquippedMeleeWeapons);
        // The grenade stays on the belt, still reachable by the throw path.
        Assert.Contains(soldier.RangedWeapons, weapon => weapon.Template.IsThrown);
    }

    [Fact]
    public void AddWeapons_GrenadeDoesNotBlockATwoHandedMeleeWeapon()
    {
        // Eviscerator-style loadout: the grenade must not register as an equipped
        // ranged weapon, which would leave the two-handed melee weapon sheathed.
        MeleeWeaponTemplate eviscerator = new(5, "Test Eviscerator", EquipLocation.TwoHand,
            TestSkills.Melee, 0, 1, 1, 0, 2, 0, 1);
        BattleSoldier soldier = new(TestModelFactory.CreateSoldier(), null);

        soldier.AddWeapons(
            [new RangedWeapon(TestModelFactory.FragGrenadeTemplate)],
            [new MeleeWeapon(eviscerator)]);

        Assert.Empty(soldier.EquippedRangedWeapons);
        MeleeWeapon equippedMelee = Assert.Single(soldier.EquippedMeleeWeapons);
        Assert.Same(eviscerator, equippedMelee.Template);
    }

    [Fact]
    public void GetEffectiveMaxRange_ScalesThrownRangeWithStrength()
    {
        Soldier trooper = TestModelFactory.CreateSoldier(strength: 10);
        Soldier marine = TestModelFactory.CreateSoldier(strength: 15);
        RangedWeaponTemplate grenade = TestModelFactory.FragGrenadeTemplate;

        Assert.Equal(30.0f, BattleModifiersUtil.GetEffectiveMaxRange(trooper, grenade));
        Assert.Equal(45.0f, BattleModifiersUtil.GetEffectiveMaxRange(marine, grenade));
    }

    [Fact]
    public void GetEffectiveMaxRange_UsesRawMaximumRangeForNonThrownWeapons()
    {
        Soldier marine = TestModelFactory.CreateSoldier(strength: 15);
        RangedWeaponTemplate rifle = TestModelFactory.DefaultWeapons.PrimaryRangedWeapon;

        Assert.Equal(rifle.MaximumRange, BattleModifiersUtil.GetEffectiveMaxRange(marine, rifle));
    }
}
