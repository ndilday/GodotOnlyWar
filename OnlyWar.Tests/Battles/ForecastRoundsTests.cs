using OnlyWar.Battles;
using OnlyWar.Domain.Equippables;
using OnlyWar.Tests.Fixtures;

using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// The rounds the removal-rate table spends per turn, for the engagement potential's ammunition
/// budget, and the two ways a Close lane can fire on the way (2026-11-08,
/// Design/Active/StrategicAndTacticalValue.md). Before, the potential counted no rounds, and a
/// squad closing on the enemy fired nothing until it stood.
/// </summary>
public class ForecastRoundsTests
{
    [Fact]
    public void ASustainedAimCycle_SpendsItsShotsRoundsOverTheTurnsItTakes()
    {
        PairRemovalTerm term = AimingTerm();
        (float _, float turns) = term.SustainedCycleAt(50f);

        Assert.True(turns > 1, "fixture must make the cycle an aim");
        Assert.Equal(1f / turns, term.SustainedRoundsAt(50f), 4);
        Assert.Equal(1f, term.RoundsPerTurnAt(50f), 4);
    }

    [Fact]
    public void FireBeyondReach_SpendsNoRounds()
    {
        PairRemovalTerm term = AimingTerm();

        Assert.Equal(0f, term.SustainedRoundsAt(250f));
        Assert.Equal(0f, term.RoundsPerTurnAt(250f));
    }

    [Fact]
    public void AWeaponWhoseSupplyNeverRunsDown_SpendsNoRounds()
    {
        PairRemovalTerm term = AimingTerm(countsRounds: false);

        Assert.True(term.SustainedRateAt(50f) > 0f);
        Assert.Equal(0f, term.SustainedRoundsAt(50f));
        Assert.Equal(0f, term.RoundsPerTurnAt(50f));
    }

    [Fact]
    public void ASquadsRounds_AreTheSumOfItsShooters()
    {
        SquadPairRemovalRate rate = new(1, 2, [AimingTerm(1, 101), AimingTerm(2, 102)]);

        Assert.Equal(2f * AimingTerm().SustainedRoundsAt(50f), rate.SustainedRoundsAtRange(50f), 4);
        Assert.Equal(2f, rate.RoundsPerTurnAtRange(50f), 4);
    }

    [Fact]
    public void ACloseThatFiresOnTheWay_RemovesMoreThanOneThatRunsInSilent()
    {
        ExchangeLane silent = Approach(RangeRates.Constant(0f, 1f));
        ExchangeLane firing = Approach(RangeRates.Constant(1f, 1f));

        EngagementExchangeForecast.Result runIn = EngagementExchangeForecast.Run(
            100f, SquadBreakProfile.Unbreakable, [silent], 60f);
        EngagementExchangeForecast.Result walkIn = EngagementExchangeForecast.Run(
            100f, SquadBreakProfile.Unbreakable, [firing], 60f);

        Assert.True(walkIn.EnemyValueRemoved > runIn.EnemyValueRemoved,
            $"walk in {walkIn.EnemyValueRemoved:F2}, run in {runIn.EnemyValueRemoved:F2}");
    }

    [Fact]
    public void ASquadThatRunsOutOfRounds_StopsFiring()
    {
        // Two battle value a turn for one round a turn: ten rounds last ten turns of sixty.
        RangeRates standing = new([0f], [2f], [0f], [1f], [0f]);
        ExchangeLane stand = Approach(RangeRates.None) with { Moves = false, Standing = standing };

        EngagementExchangeForecast.Result free = EngagementExchangeForecast.Run(
            100f, SquadBreakProfile.Unbreakable, [stand], 60f, turnDiscount: 1f);
        EngagementExchangeForecast.Result counted = EngagementExchangeForecast.Run(
            100f, SquadBreakProfile.Unbreakable, [stand], 60f, turnDiscount: 1f, ownRounds: 10f);

        Assert.Equal(100f, free.EnemyValueRemoved, 1);
        Assert.Equal(20f, counted.EnemyValueRemoved, 1);
    }

    [Fact]
    public void ADeadSoldiersRounds_GoWithHim()
    {
        // One round a turn for the whole squad, ten rounds, and the squad loses 5% of itself a
        // turn. Each survivor has rounds for ten turns, so the fire stops after turn 9 whatever
        // the losses: 2 x (1 + 0.95 + ... + 0.55) = 15.5. When the dead men's rounds stayed in
        // the budget the survivors fired on to about turn 12 and removed 20 (2026-11-09). A large
        // enemy pool keeps the enemy's fire, which falls with its losses, at 5 a turn.
        RangeRates standing = new([0f], [2f], [5f], [1f], [0f]);
        ExchangeLane stand = Approach(RangeRates.None) with
        {
            Moves = false,
            Standing = standing,
            EnemyPool = 10_000f,
        };

        EngagementExchangeForecast.Result counted = EngagementExchangeForecast.Run(
            100f, SquadBreakProfile.Unbreakable, [stand], 60f, turnDiscount: 1f, ownRounds: 10f);

        Assert.Equal(15.5f, counted.EnemyValueRemoved, 1);
    }

    private static ExchangeLane Approach(RangeRates moving) => new(
        EnemyPool: 100f,
        EnemyBreak: SquadBreakProfile.Unbreakable,
        StartRange: 500f,
        Moves: true,
        StopRange: 100f,
        MovingClosing: 10f,
        StandingClosing: 0f,
        MaximumRange: 500f,
        Moving: moving,
        Standing: RangeRates.Constant(2f, 1f),
        ContactPossible: false,
        ContactRange: 1f,
        ContactOutgoing: 0f,
        ContactIncoming: 0f);

    private static PairRemovalTerm AimingTerm(int shooter = 1, int target = 101, bool countsRounds = true) => new(
        shooter,
        target,
        Rifle(),
        referenceHitTotal: 8f,
        referenceShotsToFire: 1,
        referenceRange: 50f,
        referenceTargetSpeed: 0f,
        referenceTakeOut: 0.5f,
        referenceWoundProgress: 0f,
        targetBattleValue: 20f,
        maximumEffectiveRange: 200f,
        takeOutTerms: null,
        aimPlans:
        [
            new AimPlan(10f, 1, 2),
            new AimPlan(12f, 1, 3),
            new AimPlan(14f, 1, 4),
            new AimPlan(16f, 1, 5),
        ],
        standingShot: new AimPlan(6f, 1, 1))
    {
        CountsRounds = countsRounds,
    };

    private static RangedWeaponTemplate Rifle() => new(
        98_702,
        "Rounds rifle",
        EquipLocation.TwoHand,
        TestSkills.Ranged,
        accuracy: 3,
        armorMultiplier: 1,
        penetrationMultiplier: 1,
        requiredStrength: 0,
        baseDamage: 40,
        maxDistance: 200,
        rof: 1,
        ammo: 10,
        recoil: 0,
        bulk: 1,
        doesDamageDegradeWithRange: false,
        reloadTime: 1,
        ammunitionType: null);
}
