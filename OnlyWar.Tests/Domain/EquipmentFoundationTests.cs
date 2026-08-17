using OnlyWar.Models.Equippables;
using System.Linq;
using Xunit;

namespace OnlyWar.Tests.Domain;

public sealed class EquipmentFoundationTests
{
    [Fact]
    public void EquipmentSignature_IsStableWhenItemRowsAreReordered()
    {
        EquipmentTemplate pistol = new(101, "Pistol", carryCost: 1);
        EquipmentTemplate blade = new(102, "Blade", carryCost: 1);

        EquipmentLoadout first = new(null,
            new EquipmentLoadoutEntry(blade, 1, 1),
            new EquipmentLoadoutEntry(pistol, 1, 0));
        EquipmentLoadout second = new(null,
            new EquipmentLoadoutEntry(pistol, 1, 0),
            new EquipmentLoadoutEntry(blade, 1, 1));

        Assert.Equal(first.Signature, second.Signature);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Validator_UsesWornArmorAndGearBonusesWithoutChargingArmorCarryCost()
    {
        AmmunitionType ammunition = new(401, "Test rounds");
        EquipmentTemplate armor = new(
            201,
            "Carapace",
            carryCost: 0,
            armorProfile: new ArmorProfile(3, capacityModifier: 1));
        EquipmentTemplate gear = new(
            202,
            "Harness",
            carryCost: 1,
            gearProfile: new GearProfile(capacityBonus: 2));
        EquipmentTemplate weapon = new(
            203,
            "Rifle",
            carryCost: 3,
            requirements: [EquipmentRequirement.RequiredTag(EquipmentTags.Gear)],
            rangedProfile: new RangedWeaponProfile(
                null,
                ammunitionType: ammunition,
                loadedCapacity: 4));
        EquipmentLoadout loadout = new(
            armor,
            new EquipmentLoadoutEntry(gear, 1),
            new EquipmentLoadoutEntry(weapon, 1));

        EquipmentValidationResult result = EquipmentLoadoutValidator.Validate(
            loadout,
            new EquipmentValidationContext { BaseCapacity = 2 });

        Assert.True(result.IsValid, string.Join("; ", result.Issues.Select(issue => issue.Message)));
        Assert.Equal(4, EquipmentLoadoutValidator.GetUsedCapacity(loadout));
        Assert.Equal(5, EquipmentLoadoutValidator.GetAvailableCapacity(
            loadout,
            new EquipmentValidationContext { BaseCapacity = 2 }));
    }

    [Fact]
    public void ItemizedWeapons_ShareMissionReserveAcrossReloadAndCopyIndependently()
    {
        AmmunitionType ammunition = new(501, "Magazine rounds");
        RangedWeaponTemplate rifle = new(
            301,
            "Mission rifle",
            EquipLocation.TwoHand,
            null,
            0, 1, 1, 0, 1, 10, 1, 5, 0, 0, false, 2,
            ammunitionType: ammunition);
        AmmunitionReservePool pool = new(
            new System.Collections.Generic.Dictionary<int, int> { [ammunition.Id] = 5 });
        RangedWeapon first = new(rifle, pool);
        RangedWeapon second = new(rifle, pool);

        first.LoadedAmmo = 2;
        first.AdvanceReload();
        Assert.Equal(1, first.ReloadProgress);
        first.AdvanceReload();

        Assert.Equal(5, first.LoadedAmmo);
        Assert.Equal(2, first.ReserveAmmo);
        Assert.Equal(2, second.ReserveAmmo);

        RangedWeapon noPackage = new(rifle, new AmmunitionReservePool()) { LoadedAmmo = 0 };
        Assert.Equal(0, noPackage.ReserveAmmo);
        Assert.False(noPackage.CanReload);

        RangedWeapon copy = first.DeepCopy();
        copy.ReserveAmmo = 0;
        Assert.Equal(2, first.ReserveAmmo);
    }

    [Fact]
    public void BattleSnapshotWeapons_ShareOneCopiedReserveWithoutAliasingMissionState()
    {
        AmmunitionType ammunition = new(502, "Snapshot rounds");
        RangedWeaponTemplate rifle = new(
            302,
            "Snapshot rifle",
            EquipLocation.TwoHand,
            null,
            0, 1, 1, 0, 1, 10, 1, 5, 0, 0, false, 1,
            ammunitionType: ammunition);
        AmmunitionReservePool missionPool = new(
            new System.Collections.Generic.Dictionary<int, int> { [ammunition.Id] = 8 });
        RangedWeapon missionFirst = new(rifle, missionPool);
        RangedWeapon missionSecond = new(rifle, missionPool);

        AmmunitionReservePool snapshotPool = missionPool.DeepCopy();
        RangedWeapon snapshotFirst = missionFirst.DeepCopy(snapshotPool);
        RangedWeapon snapshotSecond = missionSecond.DeepCopy(snapshotPool);
        snapshotFirst.LoadedAmmo = 0;
        snapshotFirst.AdvanceReload();

        Assert.Equal(3, snapshotFirst.ReserveAmmo);
        Assert.Equal(3, snapshotSecond.ReserveAmmo);
        Assert.Equal(8, missionFirst.ReserveAmmo);
        Assert.Equal(8, missionSecond.ReserveAmmo);
    }

    [Fact]
    public void AmmunitionBehaviors_RespectIncrementalRecoveryConsumableAndUnlimitedRules()
    {
        AmmunitionType ammunition = new(601, "Rounds");
        RangedWeaponTemplate incremental = new(
            401,
            "Autoloader",
            EquipLocation.TwoHand,
            null,
            0, 1, 1, 0, 1, 10, 1, 5, 0, 0, false, 1,
            ammunitionType: ammunition,
            ammunitionBehavior: AmmunitionBehavior.Incremental,
            reloadAmount: 2);
        AmmunitionReservePool incrementalPool = new(
            new System.Collections.Generic.Dictionary<int, int> { [ammunition.Id] = 3 });
        RangedWeapon incrementalWeapon = new(incremental, incrementalPool) { LoadedAmmo = 0 };
        incrementalWeapon.AdvanceReload();
        Assert.Equal(2, incrementalWeapon.LoadedAmmo);
        Assert.Equal(1, incrementalWeapon.ReserveAmmo);

        RangedWeaponTemplate regenerating = new(
            402,
            "Bio weapon",
            EquipLocation.TwoHand,
            null,
            0, 1, 1, 0, 1, 10, 1, 3, 0, 0, false, 1,
            ammunitionBehavior: AmmunitionBehavior.SelfRegenerating,
            recoveryDuration: 2,
            recoveryAmount: 1);
        RangedWeapon regeneratingWeapon = new(regenerating) { LoadedAmmo = 0 };
        regeneratingWeapon.AdvanceRecovery();
        regeneratingWeapon.AdvanceRecovery();
        Assert.Equal(1, regeneratingWeapon.LoadedAmmo);

        RangedWeaponTemplate consumable = new(
            403,
            "Grenade",
            EquipLocation.OneHand,
            null,
            0, 1, 1, 0, 1, 10, 1, 1, 0, 0, false, 1,
            ammunitionBehavior: AmmunitionBehavior.ConsumableItem);
        RangedWeapon consumableWeapon = new(consumable) { ConsumableQuantity = 2 };
        Assert.True(consumableWeapon.TryConsume(1));
        Assert.False(consumableWeapon.TryConsume(2));

        RangedWeaponTemplate unlimited = new(
            404,
            "Psychic weapon",
            EquipLocation.OneHand,
            null,
            0, 1, 1, 0, 1, 10, 1, 0, 0, 0, false, 1,
            ammunitionBehavior: AmmunitionBehavior.Unlimited);
        Assert.True(new RangedWeapon(unlimited).TryConsume(100));
    }
}
