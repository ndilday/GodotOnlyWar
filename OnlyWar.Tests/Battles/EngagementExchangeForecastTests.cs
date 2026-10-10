using System;
using System.Linq;

using OnlyWar.Battles;

using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// The coupled exchange forecast behind the engagement potential's net-rate value
/// (<see cref="EngagementExchangeForecast"/>, 2026-10-06). It replaced two independent integrals,
/// each from today's full strength and each capped at its own pool, and a separate access term.
/// These pin the three properties that change was for: losses taken early cost fire later, the
/// forecast cannot have both sides wiped out together, and a squad that breaks stops fighting.
/// </summary>
public class EngagementExchangeForecastTests
{
    // Resolve from Ego through the live curve: a gaunt (Ego 8) is about 0.95, a marine (Ego 14)
    // about 3.85 -- the cases worked by hand in MoraleConstants' header.
    private static readonly SquadBreakProfile Gaunts = new(
        true, 20, BattleMoraleEvaluator.EgoToResolve(8), 0, false, 0, false, 0);
    private static readonly SquadBreakProfile Marines = new(
        true, 10, BattleMoraleEvaluator.EgoToResolve(14), 0, true,
        BattleMoraleEvaluator.EgoToResolve(14), false, 0);

    // Constant rates; an approach of n turns is a path of n yards closed at one yard a turn, and
    // an approach that never arrives closes at nothing.
    private static ExchangeLane Lane(
        float pool,
        float outgoing,
        float incoming,
        SquadBreakProfile enemy = null,
        float approachTurns = 0,
        float approachIncoming = 0)
    {
        bool arrives = float.IsFinite(approachTurns);
        float start = arrives ? approachTurns : 100;
        return new ExchangeLane(
            pool,
            enemy ?? SquadBreakProfile.Unbreakable,
            start,
            approachTurns > 0,
            0,
            arrives ? 1 : 0,
            0,
            start,
            RangeRates.Constant(0, approachIncoming),
            RangeRates.Constant(outgoing, incoming),
            false,
            1,
            0,
            0);
    }

    [Fact]
    public void AChargeThroughTheFireBand_IsShotAtOnTheWayIn()
    {
        // We stand with a 200-yard weapon; a melee enemy starts at 240 and closes 10 a turn. The
        // range-fixed forecast never saw the four turns it spends inside 200 before contact.
        RangeRates standing = RangeRates.Sample(
            [1f, 199f, 201f, 240f],
            range => range <= 200 ? 3f : 0f,
            _ => 0f);
        ExchangeLane charge = new(
            100, SquadBreakProfile.Unbreakable, 240, false, 240, 10, 10, 240,
            RangeRates.None, standing, true, 1, 0, 5);

        EngagementExchangeForecast.Result result = EngagementExchangeForecast.Run(
            100, SquadBreakProfile.Unbreakable, [charge], 30, turnDiscount: 1f);

        Assert.True(result.EnemyValueRemoved > 40, $"removed {result.EnemyValueRemoved:F1}");
        Assert.True(result.OwnValueLost > 0, "the charge arrived and fought in melee");
    }

    [Fact]
    public void BreakHazard_ReproducesTheMoraleHeaderCases()
    {
        // 25% this turn and in all, side losing (force disadvantage 0.44): gaunts usually rout.
        float gaunts = EngagementExchangeForecast.BreakHazard(Gaunts, 0.25f, 0.25f, 0.44f);
        // A marine squad's worst plausible round -- half the squad this turn, badly losing --
        // stays Steady: "effectively never," not "immune".
        float marines = EngagementExchangeForecast.BreakHazard(Marines, 0.5f, 0.5f, 0.76f);

        Assert.True(gaunts > 0.5f, $"gaunts hazard {gaunts:F3}");
        Assert.True(marines < 0.05f, $"marines hazard {marines:F3}");
        Assert.Equal(
            0,
            EngagementExchangeForecast.BreakHazard(SquadBreakProfile.Unbreakable, 1, 1, 1));
    }

    [Fact]
    public void TheWinnerKeepsAShare_NeitherSideIsWipedOutWithTheOther()
    {
        // Our fire is a little stronger. Under the old forecast both pools saturated over a long
        // horizon and the net read as enemy pool minus own pool. Coupled, the enemy is destroyed
        // and the stronger side keeps about sqrt(1 - 5/6) of itself (square law).
        EngagementExchangeForecast.Result result = EngagementExchangeForecast.Run(
            100,
            SquadBreakProfile.Unbreakable,
            [Lane(100, outgoing: 6, incoming: 5)],
            horizonTurns: 183,
            turnDiscount: 1f);

        Assert.InRange(result.EnemyValueRemoved, 99, 100.01f);
        Assert.True(
            result.OwnValueLost < 70,
            $"lost {result.OwnValueLost:F1} of 100 while destroying the enemy");
    }

    [Fact]
    public void LossesTakenOnTheApproach_CostFireAfterArrival()
    {
        const float horizon = 30;
        // Ten turns closing under fire that takes half the squad, then twenty turns standing.
        // Uncoupled, the standing phase would remove 2 x 20 = 40 at full strength.
        EngagementExchangeForecast.Result result = EngagementExchangeForecast.Run(
            100,
            SquadBreakProfile.Unbreakable,
            [Lane(1_000, outgoing: 2, incoming: 0, approachTurns: 10, approachIncoming: 5)],
            horizon,
            turnDiscount: 1f);

        Assert.InRange(result.OwnValueLost, 45, 55);
        Assert.True(
            result.EnemyValueRemoved < 0.8f * 40,
            $"the squad arrived firing at full strength: removed {result.EnemyValueRemoved:F1}");
    }

    [Fact]
    public void ASquadThatBreaks_StopsBuyingKills()
    {
        // A losing exchange: the same squad, once unable to break and once with a gaunt's nerve.
        ExchangeLane lane = Lane(200, outgoing: 3, incoming: 8);
        EngagementExchangeForecast.Result steadfast = EngagementExchangeForecast.Run(
            100, SquadBreakProfile.Unbreakable, [lane], 183, turnDiscount: 1f);
        EngagementExchangeForecast.Result gaunts = EngagementExchangeForecast.Run(
            100, Gaunts, [lane], 183, turnDiscount: 1f);

        Assert.True(
            gaunts.EnemyValueRemoved < 0.6f * steadfast.EnemyValueRemoved,
            $"gaunts removed {gaunts.EnemyValueRemoved:F1}, an unbreakable squad "
                + $"{steadfast.EnemyValueRemoved:F1}");
        // A rout costs the whole remaining squad: the forecast prices who holds the field.
        Assert.InRange(gaunts.OwnValueLost, 95, 100);
    }

    [Fact]
    public void AnEnemyThatBreaks_IsRemovedFromTheFight()
    {
        ExchangeLane steady = Lane(100, outgoing: 8, incoming: 3);
        EngagementExchangeForecast.Result unbreakableEnemy = EngagementExchangeForecast.Run(
            100, SquadBreakProfile.Unbreakable, [steady], 10, turnDiscount: 1f);
        EngagementExchangeForecast.Result breakingEnemy = EngagementExchangeForecast.Run(
            100,
            SquadBreakProfile.Unbreakable,
            [steady with { EnemyBreak = Gaunts }],
            10,
            turnDiscount: 1f);

        Assert.True(breakingEnemy.EnemyValueRemoved > unbreakableEnemy.EnemyValueRemoved);
        Assert.True(breakingEnemy.OwnValueLost < unbreakableEnemy.OwnValueLost);
    }

    [Fact]
    public void ASlowGrind_IsWorthLittle()
    {
        // Undiscounted, any positive rate against a target that cannot reply removes all of it
        // over a 183-turn horizon, so a squad barely in range and one in its best band would be
        // worth the same. The turn discount keeps them apart.
        float Removed(float rate) => EngagementExchangeForecast.Run(
            100, SquadBreakProfile.Unbreakable, [Lane(10, outgoing: rate, incoming: 0)], 183)
            .EnemyValueRemoved;

        Assert.True(Removed(0.05f) < 0.25f * Removed(2f),
            $"slow={Removed(0.05f):F2}, fast={Removed(2f):F2}");
    }

    [Fact]
    public void AnArrivalThatNeverHappens_BuysNothing()
    {
        EngagementExchangeForecast.Result result = EngagementExchangeForecast.Run(
            100,
            SquadBreakProfile.Unbreakable,
            [Lane(100, outgoing: 10, incoming: 0, approachTurns: float.PositiveInfinity)],
            50);

        Assert.Equal(0, result.EnemyValueRemoved);
    }

    /// <summary>
    /// Observed 2026-10-07: Hormagaunts running at PDF changed to a walk at 30 yards. The turn a
    /// charge arrived on was priced at the range the turn STARTED at -- point-blank fire at 3-8
    /// yards for the rest of the turn -- with no contact until the next turn, so a 3-yard change
    /// in the start range decided whether the forecast squad broke there. The arrival turn's rest
    /// is spent in contact, and the forecast moves smoothly as the start range does.
    /// </summary>
    [Fact]
    public void AChargeArrivingMidTurn_FightsInContactForTheRestOfTheTurn()
    {
        // Our melee squad runs 8 yards a turn to contact. Standing near the enemy is murderous
        // (point-blank aimed fire), running in is not, and contact favours us.
        RangeRates standing = RangeRates.Sample([2f, 40f], _ => 0f, range => 40f - range);
        RangeRates moving = RangeRates.Constant(0f, 1f);
        float[] values = Enumerable.Range(0, 153)
            .Select(step => 2f + step * 0.25f)
            .Select(start =>
            {
                ExchangeLane charge = new(
                    100, Gaunts, start, true, 1, 8, 0, start,
                    moving, standing, true, 1, 20, 2);
                EngagementExchangeForecast.Result result = EngagementExchangeForecast.Run(
                    100, Gaunts, [charge], 20);
                return result.EnemyValueRemoved - result.OwnValueLost;
            })
            .ToArray();

        float span = values.Max() - values.Min();
        float largestStep = Enumerable.Range(0, values.Length - 1)
            .Max(index => System.Math.Abs(values[index + 1] - values[index]));
        Assert.True(values[0] > values[^1], "a shorter run under fire should be worth more");
        Assert.True(
            largestStep <= 0.1f * span,
            $"cliff: a quarter-yard of start range carried {largestStep:F2} of a {span:F2} span");
        // Never standing in the point-blank band on the way in: each start range loses little.
        Assert.True(
            values.Min() > 0,
            $"a charge that never stands under fire forecast a net loss: {values.Min():F2}");
    }

    /// <summary>
    /// Observed 2026-10-09: a charge's value was a sawtooth with the period of one turn's run.
    /// Each moving turn was priced at the range it began, and fire climbs steeply in the last
    /// yards, so whether the final moving turn began at 4, 8 or 12 yards decided much of the
    /// incoming. A turn is now priced over the ground it covers, so the value falls steadily as
    /// the run gets longer.
    /// </summary>
    [Fact]
    public void AChargeIntoFireThatClimbsCloseIn_LosesValueSteadilyWithDistance()
    {
        float[] samples = [2f, 3f, 5f, 8f, 13f, 21f, 34f, 55f, 89f];
        RangeRates moving = RangeRates.Sample(samples, _ => 0f, range => 30f / range);
        float[] values = Enumerable.Range(0, 121)
            .Select(step => 20f + step * 0.5f)
            .Select(start =>
            {
                ExchangeLane charge = new(
                    100, SquadBreakProfile.Unbreakable, start, true, 1, 8, 0, start,
                    moving, RangeRates.Constant(0f, 0f), true, 1, 20, 0);
                EngagementExchangeForecast.Result result = EngagementExchangeForecast.Run(
                    100, SquadBreakProfile.Unbreakable, [charge], 20, turnDiscount: 1f);
                return result.EnemyValueRemoved - result.OwnValueLost;
            })
            .ToArray();

        for (int index = 1; index < values.Length; index++)
        {
            Assert.True(
                values[index] <= values[index - 1] + 0.01f,
                $"a longer run under fire was worth more: {values[index - 1]:F3} at "
                    + $"{20 + (index - 1) * 0.5f} yards, {values[index]:F3} at {20 + index * 0.5f}");
        }
    }

    [Fact]
    public void TheForecast_MovesSmoothlyWithTheApproachTime()
    {
        float[] values = Enumerable.Range(0, 81)
            .Select(step => step * 0.25f)
            .Select(turns =>
            {
                EngagementExchangeForecast.Result result = EngagementExchangeForecast.Run(
                    100,
                    Gaunts,
                    [Lane(150, outgoing: 6, incoming: 4, Gaunts, turns, approachIncoming: 3)],
                    40);
                return result.EnemyValueRemoved - result.OwnValueLost;
            })
            .ToArray();

        float span = values.Max() - values.Min();
        float largestStep = Enumerable.Range(0, values.Length - 1)
            .Max(index => System.Math.Abs(values[index + 1] - values[index]));
        Assert.True(span > 0);
        Assert.True(
            largestStep <= 0.25f * span,
            $"cliff: one quarter-turn carried {largestStep:F2} of a {span:F2} span");
    }
}
