using System;
using System.Collections.Generic;
using OnlyWar.Battles;
using OnlyWar.Battles.Models;
using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// The aim-versus-shoot rate (RangedTargetSelector.EvaluateFireTiming) and the range projection it
/// rests on. The planner fires when a shot now is worth at least the best per-turn value of
/// aiming first, and prices a planned or stored aim at that same rate.
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public sealed class PreparedShotRateTests
{
    // Enough enemies that the shooter's share of the work outlasts his rounds, so they are
    // scarce (RangedTargetSelector.Scarcity). The work is counted in battle value, and so is what
    // a round removes, so it is the NUMBER of enemies that sets the rounds needed, not their worth.
    private const int Horde = 400;

    [Fact]
    public void RangeProjection_FleeingMovesAwayAdvancingClosesHoldingStays()
    {
        Fixture fixture = CreateFixture(distance: 20, targetSpeed: 5);

        fixture.Target.BattleSquad.WithdrawalRole = WithdrawalRole.Bound;
        float fleeing = RangedTargetSelector.ProjectedRangeChangePerTurn(fixture.Target);
        fixture.Target.BattleSquad.WithdrawalRole = WithdrawalRole.None;
        fixture.Target.BattleSquad.LastEngagementOptionKind = EngagementOptionKind.RunToward;
        float advancing = RangedTargetSelector.ProjectedRangeChangePerTurn(fixture.Target);
        fixture.Target.BattleSquad.LastEngagementOptionKind = EngagementOptionKind.Hold;
        float holding = RangedTargetSelector.ProjectedRangeChangePerTurn(fixture.Target);

        Assert.Equal(5f, fleeing);
        Assert.Equal(-5f, advancing);
        Assert.Equal(0f, holding);
    }

    [Fact]
    public void AimingIsWorthMoreAgainstATargetClosingInThanOneRunningAway()
    {
        // Same range, same speed, opposite direction. Every turn spent aiming gives a runner back
        // range and brings a closing target nearer, so the value of waiting must split.
        Fixture closing = CreateFixture(distance: 20, targetSpeed: 5);
        closing.Target.BattleSquad.LastEngagementOptionKind = EngagementOptionKind.RunToward;
        Fixture fleeing = CreateFixture(distance: 20, targetSpeed: 5);
        fleeing.Target.BattleSquad.WithdrawalRole = WithdrawalRole.Bound;

        float closingRate = closing.Rate(currentAimBonus: null);
        float fleeingRate = fleeing.Rate(currentAimBonus: null);

        Assert.True(closingRate > 0, $"expected a worthwhile prepared shot, got {closingRate}");
        Assert.True(
            closingRate > fleeingRate,
            $"closing target rate {closingRate} should exceed fleeing target rate {fleeingRate}");
    }

    [Fact]
    public void FullAimLeavesNothingToWaitFor()
    {
        Fixture fixture = CreateFixture(distance: 20, targetSpeed: 0);

        float rate = fixture.Rate(currentAimBonus: RangedTargetSelector.FullAimBonusTurns);

        Assert.Equal(float.MinValue, rate);
        Assert.Equal(0f, fixture.Timing(currentAimBonus: RangedTargetSelector.FullAimBonusTurns)
            .Readiness);
    }

    [Fact]
    public void RoundsShortOfTheWorkChargeAShotForItsRounds()
    {
        // Same shot, same aimed alternatives, same enemy; only the pouches differ. When the rounds
        // carried outlast the shooter's share of the work they are free and firing now is worth its
        // full score. When they fall short every round must earn what the best round on offer
        // would, so the same shot is worth less.
        Fixture plentiful = CreateFixture(
            distance: 60, targetSpeed: 0, typedAmmunition: true, hordeSize: Horde);
        plentiful.Weapon.ReserveAmmo = 50_000;
        Fixture scarce = CreateFixture(
            distance: 60, targetSpeed: 0, typedAmmunition: true, hordeSize: Horde);
        scarce.Weapon.ReserveAmmo = 0;

        RangedTargetEvaluation plentifulShot = plentiful.ShootNow();
        RangedTargetSelector.FireTiming plentifulTiming = plentiful.Timing(null, plentifulShot);
        RangedTargetSelector.FireTiming scarceTiming = scarce.Timing(null, scarce.ShootNow());

        Assert.Equal(0f, plentiful.StandingScarcity());
        Assert.True(scarce.StandingScarcity() > 0.5f);
        Assert.Equal(plentifulShot.Score, plentifulTiming.ShootNowValue, 4);
        Assert.True(
            scarceTiming.ShootNowValue < plentifulTiming.ShootNowValue,
            $"scarce {scarceTiming.ShootNowValue} should be below plentiful "
                + $"{plentifulTiming.ShootNowValue}");
    }

    /// <summary>
    /// The product owner's rule (2026-10-11): rounds are scarce only when the shooter's share of the
    /// enemy battle value would outlast them at his fastest pace. The same ten rounds are plenty
    /// against one man and short against a host, and only the host makes the shooter thrifty.
    /// </summary>
    [Fact]
    public void TheSameRoundsAreScarceOnlyWhenTheWorkOutlastsThem()
    {
        Fixture oneMan = CreateFixture(distance: 60, targetSpeed: 0, typedAmmunition: true);
        oneMan.Weapon.ReserveAmmo = 0;
        Fixture host = CreateFixture(
            distance: 60, targetSpeed: 0, typedAmmunition: true, hordeSize: Horde);
        host.Weapon.ReserveAmmo = 0;

        Assert.Equal(0f, oneMan.StandingScarcity());
        Assert.True(host.StandingScarcity() > 0.5f);
        Assert.Equal(1f, RangedTargetSelector.Scarcity(0, 100, 1));
        Assert.Equal(0f, RangedTargetSelector.Scarcity(10, 100, 10));
        Assert.Equal(0.5f, RangedTargetSelector.Scarcity(10, 100, 5), 4);
    }

    /// <summary>
    /// The round charge decides how the soldier fires; it does not decide what the aim is worth.
    /// Readiness is compared in the engagement potential with the forecast's standing rate, which
    /// is gross removal, so readiness is the gross removal per turn of the plan the charge picks.
    /// Until 2026-10-09 it was the net value, about half, and every step toward the enemy looked
    /// cheaper than a standing turn the forecast itself valued twice as high.
    /// </summary>
    [Fact]
    public void ReadinessIsTheGrossRemovalOfThePlanTheRoundChargePicks()
    {
        Fixture scarce = CreateFixture(
            distance: 60, targetSpeed: 0, typedAmmunition: true, hordeSize: Horde);
        scarce.Weapon.ReserveAmmo = 0;

        RangedTargetSelector.FireTiming timing = scarce.Timing(null, scarce.ShootNow());

        Assert.True(scarce.StandingScarcity() > 0.5f);
        Assert.False(timing.ShootNow, "the scarce shooter should prefer to aim at 60 yards");
        Assert.True(
            timing.Readiness > timing.PreparedRate,
            $"readiness {timing.Readiness} should be the gross rate, above the net {timing.PreparedRate}");
        // The gross rate of the prepared shot with the best NET rate, not the best gross rate.
        float bestNet = float.MinValue;
        float grossOfBestNet = 0;
        float roundPrice = scarce.Selector.StandingRoundPrice(
            scarce.Shooter, scarce.Target, scarce.Weapon, scarce.Range, 0, null, out _);
        List<(RangedTargetEvaluation Shot, int Turns)> prepared = [];
        for (int bonus = 0; bonus <= RangedTargetSelector.FullAimBonusTurns; bonus++)
        {
            RangedTargetEvaluation shot = scarce.Selector.EvaluateRangedTarget(
                scarce.Shooter, scarce.Target, scarce.Weapon, scarce.Range,
                RangedTargetSelector.AimAccuracy(scarce.Shooter, scarce.Weapon.Template) + bonus + 1,
                roundPrice: roundPrice);
            prepared.Add((shot, bonus + 2));
        }
        foreach ((RangedTargetEvaluation shot, int turns) in prepared)
        {
            float net = (shot.Score - roundPrice * System.Math.Max(1, shot.ShotsToFire))
                / turns;
            if (net > bestNet)
            {
                bestNet = net;
                grossOfBestNet = shot.Score / turns;
            }
        }
        Assert.Equal(grossOfBestNet, timing.Readiness, 4);
    }

    /// <summary>
    /// The exchange forecast's standing rate and the Hold option's readiness price the same turn.
    /// They must agree: the removal-rate table picks the soldier's firing plan with the shot
    /// planner's own rule and credits its gross removal, exactly as readiness does. When they
    /// disagreed (2026-10-09: one aim turn in the table, net value in readiness) Neophyte Hybrids
    /// crept toward Shoota Boyz under fire without shooting.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheTablesStandingRateIsTheReadinessOfTheShootersOwnPlan(bool scarceAmmunition)
    {
        Fixture fixture = CreateFixture(
            distance: 60, targetSpeed: 0, typedAmmunition: true, hordeSize: Horde);
        fixture.Weapon.ReserveAmmo = scarceAmmunition ? 0 : 50_000;
        PairRemovalRateTable table = new(fixture.Services, fixture.Selector);

        PairRemovalTerm term = Assert.Single(
            Assert.Single(table.GetPairRemovalRates(fixture.Shooter.BattleSquad)).Value.Terms);
        RangedTargetSelector.FireTiming timing = fixture.Timing(null, fixture.ShootNow());
        float planned = timing.ShootNow ? timing.ShootNowGross : timing.Readiness;

        Assert.True(planned > 0);
        Assert.Equal(planned, term.SustainedRateAt(term.ReferenceRange), 3);
    }

    /// <summary>
    /// Every plan in the comparison picks its burst at the round price the comparison charges it.
    /// Until 2026-10-11 each shot priced its own rounds at its own best value per round: an
    /// un-aimed shot, whose rounds are worth little, found them cheap and chose a long burst, and
    /// the comparison then charged that burst at the aimed shot's value per round (a marine at 196
    /// yards: nine rounds, net -7.4, against -0.8 for the one round it should have fired).
    /// </summary>
    // Settings where rounds are scarce for the decision and the un-aimed shot, priced alone, would
    // have bought a longer burst.
    [Theory]
    [InlineData(150, 10)]
    [InlineData(100, 3)]
    public void FiringNowUsesTheBurstPricedAtTheDecisionsRoundValue(int distance, int hordeSize)
    {
        Fixture fixture = CreateFixture(
            distance: distance, targetSpeed: 0, typedAmmunition: true, rateOfFire: 9, recoil: 2,
            hordeSize: hordeSize);
        fixture.Weapon.ReserveAmmo = 0;
        RangedTargetEvaluation ownPrice = fixture.ShootNow();

        RangedTargetSelector.FireTiming timing = fixture.Timing(null, ownPrice);

        float roundPrice = fixture.Selector.StandingRoundPrice(
            fixture.Shooter, fixture.Target, fixture.Weapon, fixture.Range, 0, null,
            out float scarcity);
        RangedTargetEvaluation fired = Assert.IsType<RangedTargetEvaluation>(timing.ShootNowShot);
        float NetAtCommonPrice(RangedTargetEvaluation shot) =>
            shot.Score - (roundPrice * shot.ShotsToFire);

        Assert.True(scarcity > 0f, "the decision must find rounds scarce for its price to matter");
        // The case this guards: at its own lower price the un-aimed shot buys a longer burst.
        Assert.True(
            fired.ShotsToFire < ownPrice.ShotsToFire,
            $"fired {fired.ShotsToFire} rounds; at its own price the shot chose {ownPrice.ShotsToFire}");
        Assert.Equal(NetAtCommonPrice(fired), timing.ShootNowValue, 4);
        Assert.True(
            timing.ShootNowValue >= NetAtCommonPrice(ownPrice) - 1e-4f,
            $"the shot fired ({fired.ShotsToFire} rounds, net {timing.ShootNowValue}) must be worth "
                + $"at least the own-price burst ({ownPrice.ShotsToFire} rounds, net "
                + $"{NetAtCommonPrice(ownPrice)}) at the price the decision charges");
    }

    [Fact]
    public void UntypedReloadIsNeverScarce()
    {
        Fixture fixture = CreateFixture(distance: 20, targetSpeed: 0);
        fixture.Weapon.LoadedAmmo = 1;

        Assert.True(RangedTargetSelector.IsNeverScarce(fixture.Weapon));
    }

    [Fact]
    public void ARecedingTargetThatLeavesRangeStopsTheAimProjection()
    {
        // Two turns of flight take the target past the weapon's reach, so only the one-turn aim
        // can still be cashed and the rate is that shot over two turns.
        Fixture fixture = CreateFixture(distance: 25, targetSpeed: 5, maximumRange: 32);
        fixture.Target.BattleSquad.WithdrawalRole = WithdrawalRole.Bound;

        float rate = fixture.Rate(currentAimBonus: null);
        RangedTargetEvaluation oneAimTurn = fixture.Selector.EvaluateRangedTarget(
            fixture.Shooter,
            fixture.Target,
            fixture.Weapon,
            30,
            fixture.Weapon.Template.Accuracy + 1);

        Assert.Equal(oneAimTurn.Score / 2, rate, 4);
    }

    private static Fixture CreateFixture(
        int distance,
        float targetSpeed,
        float maximumRange = 200,
        bool typedAmmunition = false,
        byte rateOfFire = 1,
        ushort recoil = 0,
        int hordeSize = 0)
    {
        const int idBase = 98_400;
        SoldierTemplate shooterTemplate = new(
            idBase + 1,
            TestModelFactory.HumanSpecies,
            "Rate Shooter",
            1,
            1,
            false,
            0,
            Array.Empty<ValueTuple<BaseSkill, float>>(),
            battleValue: 20);
        SoldierTemplate targetTemplate = new(
            idBase + 2,
            TestModelFactory.HumanSpecies,
            "Rate Target",
            1,
            1,
            false,
            0,
            Array.Empty<ValueTuple<BaseSkill, float>>(),
            battleValue: 20);
        Soldier shooterModel = TestModelFactory.CreateSoldier(
            shooterTemplate,
            "Rate Shooter",
            dexterity: 14,
            skills: new Skill(TestSkills.Ranged, 4));
        Soldier targetModel = TestModelFactory.CreateSoldier(targetTemplate, "Rate Target");
        shooterModel.Id = idBase + 101;
        targetModel.Id = idBase + 102;

        BattleSquad shooterSquad = new(
            false,
            TestModelFactory.CreateSquad("Rate Shooters", shooterModel));
        BattleSquad targetSquad = new(
            false,
            TestModelFactory.CreateSquad("Rate Targets", targetModel));
        BattleSoldier shooter = shooterSquad.Soldiers[0];
        BattleSoldier target = targetSquad.Soldiers[0];
        target.CurrentSpeed = targetSpeed;
        RangedWeapon weapon = new(new RangedWeaponTemplate(
            idBase + 201,
            "Rate rifle",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: 3,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            baseDamage: 40,
            maxDistance: maximumRange,
            rof: rateOfFire,
            ammo: 10,
            recoil: recoil,
            bulk: 1,
            doesDamageDegradeWithRange: false,
            reloadTime: 1,
            ammunitionType: typedAmmunition
                ? new AmmunitionType(idBase + 301, "Rate ammunition")
                : null));
        shooter.RangedWeapons.Clear();
        shooter.ClearReadiedRangedWeapons();
        shooter.RangedWeapons.Add(weapon);
        shooter.ReadyWeapon(weapon);

        BattleGridManager grid = new();
        Place(grid, shooter, true, 0);
        Place(grid, target, false, distance);
        Dictionary<int, BattleSoldier> soldiers = new()
        {
            [shooter.Soldier.Id] = shooter,
            [target.Soldier.Id] = target
        };
        AddHorde(grid, soldiers, targetTemplate, hordeSize, idBase + 1_000);
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
            new RangedTargetSelector(new RangedTargetingServices(services)),
            services);
    }

    private static void Place(BattleGridManager grid, BattleSoldier soldier, bool firstSide, int x)
    {
        soldier.TopLeft = (x, 0);
        grid.PlaceSoldier(soldier, firstSide, [soldier.TopLeft.Value]);
    }

    /// <summary>
    /// Able enemies far beyond the weapon's reach: they add to the shooter's share of the work
    /// (RangedShotEvaluator.WorkPerShooter counts every enemy the weapon can hurt at all) without
    /// becoming a target in the shot under test.
    /// </summary>
    internal static void AddHorde(
        BattleGridManager grid,
        Dictionary<int, BattleSoldier> soldiers,
        SoldierTemplate template,
        int count,
        int firstId)
    {
        if (count <= 0)
        {
            return;
        }
        Soldier[] models = new Soldier[count];
        for (int index = 0; index < count; index++)
        {
            models[index] = TestModelFactory.CreateSoldier(template, "Horde");
            models[index].Id = firstId + index;
        }
        BattleSquad horde = new(false, TestModelFactory.CreateSquad("Horde", models));
        for (int index = 0; index < horde.Soldiers.Count; index++)
        {
            BattleSoldier soldier = horde.Soldiers[index];
            soldier.TopLeft = (5_000 + (index % 20), 5 + (index / 20));
            grid.PlaceSoldier(soldier, false, [soldier.TopLeft.Value]);
            soldiers[soldier.Soldier.Id] = soldier;
        }
    }

    private sealed record Fixture(
        BattleSoldier Shooter,
        BattleSoldier Target,
        RangedWeapon Weapon,
        float Range,
        RangedTargetSelector Selector,
        SquadPlanningServices Services)
    {
        internal float Rate(int? currentAimBonus) => Timing(currentAimBonus).PreparedRate;

        internal RangedTargetSelector.FireTiming Timing(
            int? currentAimBonus,
            RangedTargetEvaluation shootNow = null) => Selector.EvaluateFireTiming(
            Shooter,
            Target,
            shootNow,
            Weapon,
            Range,
            currentAimBonus,
            bulkMultiplier: 0,
            aimMultiplier: 1f);

        internal RangedTargetEvaluation ShootNow() =>
            Selector.EvaluateRangedTarget(Shooter, Target, Weapon, Range, 0);

        internal float StandingScarcity()
        {
            Selector.StandingRoundPrice(Shooter, Target, Weapon, Range, 0, null, out float scarcity);
            return scarcity;
        }
    }
}
