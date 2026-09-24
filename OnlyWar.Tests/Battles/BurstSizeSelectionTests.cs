using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Battles;
using OnlyWar.Battles.Models;
using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// Burst length (RangedShotEvaluator.ChooseShotsToFire): the round count with the best expected
/// removal net of the rounds it spends, priced by ammunition scarcity. Free rounds fire the full
/// rate; scarce rounds shorten the burst, because the later rounds of a recoiling burst mostly buy
/// the rate-of-fire bonus rather than extra hits.
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public sealed class BurstSizeSelectionTests
{
    private const int BoltgunRateOfFire = 9;
    private const ushort BoltgunMagazine = 30;

    [Fact]
    public void PlentifulAmmunition_FiresTheFullRateOfFire()
    {
        Fixture fixture = CreateFixture(spareMagazines: RangedTargetSelector.PlentifulMagazines - 1);

        Assert.Equal(0f, RangedTargetSelector.AmmunitionScarcity(fixture.Weapon));
        Assert.Equal(BoltgunRateOfFire, fixture.ShootNow().ShotsToFire);
    }

    [Fact]
    public void UntypedReload_IsFreeAndFiresTheFullRateOfFire()
    {
        Fixture fixture = CreateFixture(spareMagazines: 0, typedAmmunition: false);

        Assert.Equal(BoltgunRateOfFire, fixture.ShootNow().ShotsToFire);
    }

    [Fact]
    public void StandardIssue_FiresAShorterBurstThanTheFullRate()
    {
        Fixture fixture = CreateFixture(spareMagazines: EquipmentRulesCatalog.StandardSpareMagazines);

        int shots = fixture.ShootNow().ShotsToFire;

        Assert.InRange(shots, 1, BoltgunRateOfFire - 1);
    }

    [Fact]
    public void EmptyingPouches_NeverLengthenTheBurst()
    {
        int previous = int.MaxValue;
        for (int spares = RangedTargetSelector.PlentifulMagazines - 1; spares >= 0; spares--)
        {
            int shots = CreateFixture(spareMagazines: spares).ShootNow().ShotsToFire;
            Assert.True(
                shots <= previous,
                $"{spares} spare magazines fired {shots}, more than the {previous} fired with one more");
            previous = shots;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void TheClimb_MatchesTheBruteForceBestNetValue(int spareMagazines)
    {
        // The climb stops at the first round that does not pay for itself, which is only the
        // global best when the score is concave. Check it against every burst length directly.
        Fixture fixture = CreateFixture(spareMagazines: spareMagazines);
        float scarcity = RangedTargetSelector.AmmunitionScarcity(fixture.Weapon);
        float removalPerHit = RangedShotEvaluator.CalculateRangedRemovalFraction(
            fixture.Target, fixture.Weapon, fixture.Range, armor: 0);
        float[] score = Enumerable.Range(1, BoltgunRateOfFire)
            .Select(shots => RemovalMath.ExpectedBurstRemovalFraction(
                RangedShotEvaluator.CalculateRangedPreRollHitTotal(
                    fixture.Shooter,
                    fixture.Target,
                    fixture.Weapon,
                    fixture.Range,
                    moveAndAimMod: 0,
                    shots,
                    firingIntoMelee: false),
                shots,
                fixture.Weapon.Template.Recoil,
                removalPerHit))
            .ToArray();
        float valuePerRound = score.Select((value, index) => value / (index + 1)).Max();
        int best = Enumerable.Range(1, BoltgunRateOfFire)
            .OrderByDescending(shots => score[shots - 1] - (scarcity * valuePerRound * shots))
            .ThenBy(shots => shots)
            .First();

        Assert.Equal(best, fixture.ShootNow().ShotsToFire);
    }

    [Fact]
    public void SingleShotWeapon_AlwaysFiresOnce()
    {
        Fixture fixture = CreateFixture(spareMagazines: 0, rateOfFire: 1);

        Assert.Equal(1, fixture.ShootNow().ShotsToFire);
    }

    [Fact]
    public void MachineGun_NeverFiresBelowAQuarterOfItsRate()
    {
        Fixture fixture = CreateFixture(spareMagazines: 0, rateOfFire: 16, magazine: 64);

        Assert.True(fixture.ShootNow().ShotsToFire >= 4);
    }

    private static Fixture CreateFixture(
        int spareMagazines,
        bool typedAmmunition = true,
        byte rateOfFire = BoltgunRateOfFire,
        ushort magazine = BoltgunMagazine)
    {
        const int idBase = 98_600;
        SoldierTemplate shooterTemplate = new(
            idBase + 1,
            TestModelFactory.HumanSpecies,
            "Burst Shooter",
            1,
            1,
            false,
            0,
            Array.Empty<ValueTuple<BaseSkill, float>>(),
            battleValue: 20);
        SoldierTemplate targetTemplate = new(
            idBase + 2,
            TestModelFactory.HumanSpecies,
            "Burst Target",
            1,
            1,
            false,
            0,
            Array.Empty<ValueTuple<BaseSkill, float>>(),
            battleValue: 20);
        Soldier shooterModel = TestModelFactory.CreateSoldier(
            shooterTemplate,
            "Burst Shooter",
            dexterity: 14,
            skills: new Skill(TestSkills.Ranged, 4));
        Soldier targetModel = TestModelFactory.CreateSoldier(targetTemplate, "Burst Target");
        shooterModel.Id = idBase + 101;
        targetModel.Id = idBase + 102;

        BattleSquad shooterSquad = new(
            false,
            TestModelFactory.CreateSquad("Burst Shooters", shooterModel));
        BattleSquad targetSquad = new(
            false,
            TestModelFactory.CreateSquad("Burst Targets", targetModel));
        BattleSoldier shooter = shooterSquad.Soldiers[0];
        BattleSoldier target = targetSquad.Soldiers[0];
        // Boltgun-shaped: recoil 2, so the fifth hit of a burst needs a margin above 7.
        RangedWeapon weapon = new(new RangedWeaponTemplate(
            idBase + 201,
            "Burst rifle",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: 3,
            armorMultiplier: 1,
            penetrationMultiplier: 2,
            requiredStrength: 0,
            baseDamage: 6,
            maxDistance: 1000,
            rof: rateOfFire,
            ammo: magazine,
            recoil: 2,
            bulk: 4,
            doesDamageDegradeWithRange: true,
            reloadTime: 3,
            ammunitionType: typedAmmunition
                ? new AmmunitionType(idBase + 301, "Burst rounds")
                : null));
        weapon.LoadedAmmo = magazine;
        weapon.ReserveAmmo = spareMagazines * magazine;
        shooter.RangedWeapons.Clear();
        shooter.ClearReadiedRangedWeapons();
        shooter.RangedWeapons.Add(weapon);
        shooter.ReadyWeapon(weapon);

        const int distance = 20;
        BattleGridManager grid = new();
        Place(grid, shooter, true, 0);
        Place(grid, target, false, distance);
        Dictionary<int, BattleSoldier> soldiers = new()
        {
            [shooter.Soldier.Id] = shooter,
            [target.Soldier.Id] = target
        };
        SquadPlanningServices services = new(
            grid,
            soldiers,
            new Dictionary<int, MeleeWeaponTemplate>(),
            log: null,
            new BattlePlanningContext());
        return new Fixture(
            shooter,
            target,
            weapon,
            distance,
            new RangedTargetSelector(new RangedTargetingServices(services)));
    }

    private static void Place(BattleGridManager grid, BattleSoldier soldier, bool firstSide, int x)
    {
        soldier.TopLeft = (x, 0);
        grid.PlaceSoldier(soldier, firstSide, [soldier.TopLeft.Value]);
    }

    private sealed record Fixture(
        BattleSoldier Shooter,
        BattleSoldier Target,
        RangedWeapon Weapon,
        float Range,
        RangedTargetSelector Selector)
    {
        internal RangedTargetEvaluation ShootNow() =>
            Selector.EvaluateRangedTarget(Shooter, Target, Weapon, Range, 0);
    }
}
