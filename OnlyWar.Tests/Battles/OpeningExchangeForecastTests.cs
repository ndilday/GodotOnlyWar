using System.Collections.Generic;
using System.Linq;

using OnlyWar.Battles;
using OnlyWar.Domain;
using OnlyWar.Domain.Squads;
using OnlyWar.Persistence.Database.GameRules;
using OnlyWar.Tests.Fixtures;

using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// Phase 1 of the opening-range refactor (Design/Reference/ForecastCalibration.md section 1): the exchange
/// forecast's ammunition budget, and the grid-free forecast of a battle that opens at a given
/// range. Phase 0 measured Shoota Boyz arriving at contact with 7-23% of their rounds after long
/// approaches, and the forecast had no ammunition at all, so a longer opening could only look as
/// good as a shorter one.
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class OpeningExchangeForecastTests
{
    private const int GenestealerSquadTemplateId = 16;
    private const int HormagauntSquadTemplateId = 17;
    private const int PdfSquadTemplateId = 34;

    // One enemy at a fixed range: we remove `outgoing` a turn and spend `rounds` doing it.
    private static ExchangeLane StandingLane(
        float outgoing,
        float rounds,
        float incoming = 0,
        float incomingRounds = 0,
        float enemyRounds = float.PositiveInfinity) =>
        new(
            1_000,
            SquadBreakProfile.Unbreakable,
            50,
            false,
            50,
            0,
            0,
            50,
            RangeRates.None,
            new RangeRates([0f], [outgoing], [incoming], [rounds], [incomingRounds]),
            false,
            1,
            0,
            0,
            enemyRounds);

    [Fact]
    public void WithoutABudget_TheRoundsChangeNothing()
    {
        ExchangeLane counted = StandingLane(outgoing: 5, rounds: 10, incoming: 2, incomingRounds: 6);
        ExchangeLane plain = counted with { Standing = RangeRates.Constant(5, 2) };

        EngagementExchangeForecast.Result withRounds = EngagementExchangeForecast.Run(
            100, SquadBreakProfile.Unbreakable, [counted], 40);
        EngagementExchangeForecast.Result without = EngagementExchangeForecast.Run(
            100, SquadBreakProfile.Unbreakable, [plain], 40);

        Assert.Equal(without.EnemyValueRemoved, withRounds.EnemyValueRemoved);
        Assert.Equal(without.OwnValueLost, withRounds.OwnValueLost);
    }

    [Fact]
    public void AShooterThatRunsOutOfRounds_StopsRemoving()
    {
        // Ten rounds a turn and thirty carried: three turns of fire, whatever the horizon.
        EngagementExchangeForecast.Result result = EngagementExchangeForecast.Run(
            100,
            SquadBreakProfile.Unbreakable,
            [StandingLane(outgoing: 5, rounds: 10)],
            horizonTurns: 40,
            turnDiscount: 1f,
            ownRounds: 30);

        Assert.InRange(result.EnemyCasualties, 14.99f, 15.01f);
        Assert.Equal(0f, result.OwnRoundsLeft);
    }

    [Fact]
    public void AnEnemyThatRunsOutOfRounds_StopsHurtingUs()
    {
        EngagementExchangeForecast.Result result = EngagementExchangeForecast.Run(
            100,
            SquadBreakProfile.Unbreakable,
            [StandingLane(outgoing: 0.01f, rounds: 0, incoming: 2, incomingRounds: 4, enemyRounds: 20)],
            horizonTurns: 40,
            turnDiscount: 1f);

        // Five turns of return fire at 2 a turn (it weakens a little as we wear it down).
        Assert.InRange(result.OwnCasualties, 9.9f, 10.01f);
    }

    [Fact]
    public void Melee_SpendsNoRounds_AndIsNotStoppedByAnEmptyMagazine()
    {
        ExchangeLane contact = StandingLane(outgoing: 5, rounds: 10) with
        {
            StartRange = 1,
            StopRange = 1,
            MaximumRange = 1,
            ContactPossible = true,
            ContactOutgoing = 4,
        };

        EngagementExchangeForecast.Result result = EngagementExchangeForecast.Run(
            100,
            SquadBreakProfile.Unbreakable,
            [contact],
            horizonTurns: 10,
            turnDiscount: 1f,
            ownRounds: 0);

        Assert.Equal(0, result.ContactTurn);
        Assert.InRange(result.EnemyCasualties, 39.99f, 40.01f);
    }

    [Fact]
    public void AClosingContactSeeker_ReportsTheTurnItArrivesAndTheRoundsLeftThen()
    {
        // The enemy starts 50 yards off and closes 10 a turn; we fire 10 rounds a turn.
        ExchangeLane charge = StandingLane(outgoing: 1, rounds: 10) with
        {
            StandingClosing = 10,
            ContactPossible = true,
            ContactIncoming = 1,
        };

        EngagementExchangeForecast.Result result = EngagementExchangeForecast.Run(
            100,
            SquadBreakProfile.Unbreakable,
            [charge],
            horizonTurns: 20,
            turnDiscount: 1f,
            ownRounds: 100);

        // 49 yards to close at 10 a turn: contact comes in the fifth turn (turn 4).
        Assert.Equal(4, result.ContactTurn);
        Assert.True(
            result.OwnRoundsAtContact is > 50 and < 70,
            $"{result.OwnRoundsAtContact} rounds left at contact");
    }

    [Fact]
    public void AMeleeOnlySquad_HasNoRangedWeapon_AndSeeksContact()
    {
        (BattleSquad gaunts, BattleSquad pdf) = Pair(HormagauntSquadTemplateId, PdfSquadTemplateId);

        OpeningExchangeForecast forecast = OpeningExchangeForecast.Build(gaunts, [pdf]);

        Assert.False(forecast.HasRangedWeapon);
        Assert.True(forecast.IsContactSeeking);
        Assert.True(float.IsPositiveInfinity(forecast.OwnRounds));
    }

    [Fact]
    public void AGunlineAgainstMelee_InflictsMoreTheLongerTheApproach()
    {
        (BattleSquad pdf, BattleSquad gaunts) = Pair(PdfSquadTemplateId, HormagauntSquadTemplateId);
        OpeningExchangeForecast forecast = OpeningExchangeForecast.Build(pdf, [gaunts]);

        float near = forecast.Evaluate(12).EnemyCasualties;
        float far = forecast.Evaluate(150).EnemyCasualties;

        Assert.True(forecast.HasRangedWeapon);
        Assert.False(forecast.IsContactSeeking);
        Assert.True(far > near, $"inflicted {far:F1} opening at 150 yards, {near:F1} at 12");
    }

    [Fact]
    public void ALongerApproach_SpendsMoreRounds()
    {
        // Melee spends nothing, so what a gunline spends is what it fires on the approach (from
        // 400 yards the forecast may break the Hormagaunts before they arrive at all).
        (BattleSquad pdf, BattleSquad gaunts) = Pair(PdfSquadTemplateId, HormagauntSquadTemplateId);
        OpeningExchangeForecast forecast = OpeningExchangeForecast.Build(pdf, [gaunts]);

        EngagementExchangeForecast.Result near = forecast.Evaluate(50);
        EngagementExchangeForecast.Result far = forecast.Evaluate(400);

        Assert.True(forecast.OwnRounds is > 0 and < float.PositiveInfinity, $"{forecast.OwnRounds} rounds");
        Assert.True(
            far.OwnRoundsLeft < near.OwnRoundsLeft,
            $"{far.OwnRoundsLeft:F0} rounds left opening at 400 yards, {near.OwnRoundsLeft:F0} at 50");
    }

    [Fact]
    public void CountingAmmunition_NeverMakesAnOpeningLookBetter()
    {
        (BattleSquad pdf, BattleSquad gaunts) = Pair(PdfSquadTemplateId, GenestealerSquadTemplateId);
        OpeningExchangeForecast counted = OpeningExchangeForecast.Build(pdf, [gaunts]);
        OpeningExchangeForecast free = OpeningExchangeForecast.Build(
            pdf, [gaunts], options: new OpeningForecastOptions(CountAmmunition: false));

        foreach (float range in new[] { 12f, 100f, 300f, 600f })
        {
            Assert.True(
                OpeningExchangeForecast.Net(counted.Evaluate(range))
                    <= OpeningExchangeForecast.Net(free.Evaluate(range)) + 0.001f,
                $"at {range} yards the counted forecast beat the free one");
        }
    }

    [Fact]
    public void AnUnaimedShot_DoesNotGetTheAimedShotsAccuracy()
    {
        // The curve's own RemovalAt is the shot aimed for one turn (Accuracy and +1 in its hit
        // total). Priced with that bonus, firing every turn beat aiming nearly everywhere and the
        // forecast spent rounds about three times faster than the battles.
        (BattleSquad pdf, BattleSquad gaunts) = Pair(PdfSquadTemplateId, HormagauntSquadTemplateId);
        RangedEffectivenessCurve curve = RangedEffectivenessCurve.Build(
            pdf.AbleSoldiers.ToList(),
            BattleEngagementFrameBuilder.RepresentativeProfile(gaunts.AbleSoldiers.ToList()));

        (float unaimed, _) = curve.SustainedAt(100, CurveFireModel.Unaimed);
        (float aimed, float aimedRounds) = curve.SustainedAt(100, CurveFireModel.SustainedAimed);
        (_, float unaimedRounds) = curve.SustainedAt(100, CurveFireModel.Unaimed);

        Assert.True(unaimed < curve.RemovalAt(100), $"unaimed {unaimed:F2}, aimed-once curve {curve.RemovalAt(100):F2}");
        Assert.True(aimed > unaimed, $"the best cycle {aimed:F2} is no better than firing unaimed {unaimed:F2}");
        Assert.True(aimedRounds < unaimedRounds, "aiming spends no fewer rounds a turn than firing every turn");
    }

    [Fact]
    public void LevelBreaks_BreakLessOftenThanAFreshHazardEveryTurn_NearTheBar()
    {
        // Gaunts losing a steady 2% a turn for twenty turns sit just under the rout bar for the
        // last several. Each turn's check as a fresh chance on the mean path compounds; the chance
        // that the realized losses are over the bar does not.
        SquadBreakProfile gaunts = new(
            true, 20, BattleMoraleEvaluator.EgoToResolve(8), 0, false, 0, false, 0);
        ExchangeLane lane = StandingLane(outgoing: 0, rounds: 0, incoming: 2f);

        EngagementExchangeForecast.Result hazard = EngagementExchangeForecast.Run(
            100, gaunts, [lane], horizonTurns: 20, turnDiscount: 1f);
        EngagementExchangeForecast.Result level = EngagementExchangeForecast.Run(
            100, gaunts, [lane], horizonTurns: 20, turnDiscount: 1f, levelBreaks: true);

        Assert.True(
            level.OwnBreakChance < hazard.OwnBreakChance,
            $"level {level.OwnBreakChance:F3}, hazard {hazard.OwnBreakChance:F3}");
    }

    [Fact]
    public void ForecastOpeningRange_IsNoPreference_ForASquadWithNoRangedWeapon()
    {
        (BattleSquad gaunts, BattleSquad pdf) = Pair(HormagauntSquadTemplateId, PdfSquadTemplateId);

        Assert.Null(BattleEngagementFrameBuilder.CalculateForecastOpeningRange(gaunts, [pdf]));
    }

    [Fact]
    public void ForecastOpeningRange_IsInsideTheSquadsReach()
    {
        (BattleSquad pdf, BattleSquad gaunts) = Pair(PdfSquadTemplateId, HormagauntSquadTemplateId);
        OpeningExchangeForecast forecast = OpeningExchangeForecast.Build(pdf, [gaunts]);

        float? range = BattleEngagementFrameBuilder.CalculateForecastOpeningRange(pdf, [gaunts]);

        Assert.NotNull(range);
        Assert.InRange(range.Value, 1.5f, forecast.Reach);
    }

    // Phase 5 (2026-10-24): the forecast is the only answer to the opening question.
    [Fact]
    public void ThePreferredOpeningRange_IsTheForecasts()
    {
        (BattleSquad pdf, BattleSquad gaunts) = Pair(PdfSquadTemplateId, HormagauntSquadTemplateId);
        float forecast = BattleEngagementFrameBuilder.CalculateForecastOpeningRange(pdf, [gaunts]).Value;

        Assert.Equal(forecast, BattleEngagementFrameBuilder.CalculatePreferredOpeningRange(pdf, [gaunts]));
    }

    // Calibrated against the measured approach curves (Design/Reference/ForecastCalibration.md section 1): a PDF
    // line holds the field best when it opens on Hormagaunts at 300-450 yards, and the forecast
    // must not stop short in the knife-range band the old sufficiency sweep chose (26-28 yards).
    [Fact]
    public void APdfLine_OpensWellOutAgainstHormagaunts()
    {
        (BattleSquad pdf, BattleSquad gaunts) = Pair(PdfSquadTemplateId, HormagauntSquadTemplateId);

        float range = BattleEngagementFrameBuilder.CalculatePreferredOpeningRange(pdf, [gaunts]);

        Assert.True(range >= 150f, $"a PDF line opened {range:0} yards from Hormagaunts");
    }

    private static (BattleSquad First, BattleSquad Second) Pair(int firstTemplateId, int secondTemplateId)
    {
        GameRulesBlob blob = RulesDatabaseFixture.LoadRules();
        RNG.Reset(4243);
        OnlyWar.Abstractions.IEntityIdAllocator ids =
            new OnlyWar.Runtime.Allocators.TacticalEntityIdAllocator();
        BattleSquad Build(int templateId, string name) => new(false, SquadFactory.GenerateSquad(
            blob.Factions
                .Where(faction => faction.SquadTemplates.ContainsKey(templateId))
                .Select(faction => faction.SquadTemplates[templateId])
                .First(),
            new StaticRNG(), ids, name));
        return (Build(firstTemplateId, "First"), Build(secondTemplateId, "Second"));
    }
}
