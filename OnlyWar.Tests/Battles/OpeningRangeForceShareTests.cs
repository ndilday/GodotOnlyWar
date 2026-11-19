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
/// The opening range is asked once per squad against the WHOLE opposing force. Until 2026-09-22
/// each squad then planned as if it alone had to remove the enemy's entire kill target, under the
/// enemy's entire fire; in Grist Nine Epsilon (33 marine squads against 453 orks) no single squad
/// could reach that target, so it never bound, and the battle opened at ~740 instead of ~480. A
/// squad now plans its proportional share of both.
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class OpeningRangeForceShareTests
{
    private const int SpaceMarineFactionId = 1;
    private const int TacticalSquadTemplateId = 1;
    private const int OrkFactionId = 4;
    private const int ShootaBoyzTemplateId = 39;
    private const int SluggaBoyzTemplateId = 40;

    private static readonly float[] Openings = [10, 20, 40, 80, 120, 160, 240, 320, 480, 640, 1000];
    private const float ShortOpening = 20f;
    // "Clearly better": a tenth of the squad's own battle value. Measured 2026-10-27 the gap at
    // 20 yards was about 617 on an own pool of 890 (+134 in force against -483 alone).
    private const float ClearlyBetterShare = 0.1f;

    // This test used to assert that the squad in a force OPENS nearer than alone. That stopped
    // holding on 2026-10-27, and not because force share stopped working. The chosen opening is
    // the shortest within 1% of the best expected net, then within 1% of the best unlucky net
    // among those (BattleEngagementFrameBuilder.CalculateForecastOpeningRange). Force share makes
    // the walk in cheap, which flattens the expected ladder into a plateau (110-300 yd here), and
    // on a plateau the unlucky tie-break decides, and it prefers range: 230 yd in force against
    // 144 yd alone, where a sharp expected peak at 160 yd decided instead. So the claim is
    // asserted on the forecast itself: the squad plans its share of the kill and of the fire.
    [Fact]
    public void SquadInALargerForce_ForecastsAShortOpeningBetterThanTheSameSquadAlone()
    {
        // Three Tactical Squads against two Boyz squads, so the squad alone still has a fight it
        // can win. Against three Boyz squads (Boyz at 11 since 2026-10-27) one Tactical Squad
        // forecasts a loss at every opening, which says nothing about force share.
        (List<BattleSquad> marines, List<BattleSquad> boyz) = CreateForces(marineSquads: 3, boyzSquads: 2);
        BattleSquad squad = marines[0];

        // Force share alone. Since 2026-10-26 a squad with friends also prices their rout as shock
        // (EngagementExchangeForecast.RoutingFriends), which the squad alone has no friends to
        // fear, so it is off for both forecasts.
        bool routingFriends = EngagementExchangeForecast.PriceRoutingFriends;
        OpeningExchangeForecast alone;
        OpeningExchangeForecast inForce;
        float aloneBest;
        float inForceBest;
        float aloneShort;
        float inForceShort;
        string ladders;
        try
        {
            EngagementExchangeForecast.PriceRoutingFriends = false;
            alone = OpeningExchangeForecast.Build(squad, boyz);
            inForce = OpeningExchangeForecast.Build(squad, boyz, marines);
            aloneBest = Openings.Max(opening => ExpectedNet(alone, opening));
            inForceBest = Openings.Max(opening => ExpectedNet(inForce, opening));
            aloneShort = ExpectedNet(alone, ShortOpening);
            inForceShort = ExpectedNet(inForce, ShortOpening);
            ladders = "\nalone:    " + Ladder(alone) + "\nin force: " + Ladder(inForce);
        }
        finally
        {
            EngagementExchangeForecast.PriceRoutingFriends = routingFriends;
        }

        // The premise: alone, the squad faces a harder fight it still expects to win. If it
        // expects to lose at every opening, the comparison below measures nothing.
        Assert.True(
            aloneBest > 0,
            $"the squad alone forecasts at best {aloneBest}; the fixture no longer gives it a "
                + "winnable fight" + ladders);
        Assert.True(
            inForceBest > 0,
            $"the squad in its force forecasts at best {inForceBest}" + ladders);
        // Against each forecast's own best opening. Until 2026-11-01 this compared the raw nets,
        // which in database values put the lone squad at a loss at 20 yd; in tactical values it
        // wins there (+170 against a best of 428), and the squad in its force is capped by its
        // share of the enemy (146), so the raw nets stopped saying anything about force share.
        // What force share must do is make the short opening cost little against the best.
        float aloneShortfall = aloneBest - aloneShort;
        float inForceShortfall = inForceBest - inForceShort;
        Assert.True(
            aloneShortfall - inForceShortfall > ClearlyBetterShare * alone.OwnPool,
            $"at {ShortOpening} yd the squad in its force falls {inForceShortfall} short of its best, "
                + $"against {aloneShortfall} alone -- it is still planning the whole force's share of "
                + "the kill and of the enemy's fire" + ladders);
    }

    private static float ExpectedNet(OpeningExchangeForecast forecast, float opening) =>
        OpeningExchangeForecast.Net(forecast.Evaluate(opening));

    // Forecast net, expected (unlucky), at a ladder of openings, for a failure message.
    private static string Ladder(OpeningExchangeForecast forecast) => string.Join("  ",
        Openings.Select(opening =>
            $"{opening}: {ExpectedNet(forecast, opening):F1}"
            + $" ({OpeningExchangeForecast.Net(forecast.EvaluateUnlucky(opening)):F1})"));

    [Fact]
    public void SquadThatIsItsWholeForce_OpensWhereItWouldAlone()
    {
        (List<BattleSquad> marines, List<BattleSquad> boyz) = CreateForces(marineSquads: 1, boyzSquads: 1);
        BattleSquad squad = marines[0];

        Assert.Equal(
            BattleEngagementFrameBuilder.CalculatePreferredOpeningRange(squad, boyz),
            BattleEngagementFrameBuilder.CalculatePreferredOpeningRange(squad, boyz, marines));
    }

    // SquadInALargerForce_OpensInsideItsUsefulBand (Grist Nine Epsilon, 2026-09-22) was removed on
    // 2026-10-24 with the sufficiency sweep (Design/Reference/ForecastCalibration.md section 1). It pinned that
    // sweep's break limit: the opening had to sit inside the useful band so the marines could punish
    // a retreat. The opening forecast prices a break as the enemy's value leaving the fight, as the
    // gauntlet counts who holds the field, and the measured best opening for Tactical against Shoota
    // Boyz is the end of the 600-yard ladder.

    private static (List<BattleSquad> Marines, List<BattleSquad> Boyz) CreateForces(
        int marineSquads,
        int boyzSquads)
    {
        GameRulesBlob blob = RulesDatabaseFixture.LoadRules();
        Faction marines = blob.Factions.Single(faction => faction.Id == SpaceMarineFactionId);
        Faction orks = blob.Factions.Single(faction => faction.Id == OrkFactionId);
        RNG.Reset(4243);
        OnlyWar.Abstractions.IEntityIdAllocator ids =
            new OnlyWar.Runtime.Allocators.TacticalEntityIdAllocator();
        List<BattleSquad> boyz = Enumerable.Range(0, boyzSquads)
            .Select(i => new BattleSquad(false, SquadFactory.GenerateSquad(
                orks.SquadTemplates[i % 2 == 0 ? SluggaBoyzTemplateId : ShootaBoyzTemplateId],
                new StaticRNG(), ids, $"Boyz {i}")))
            .ToList();
        List<BattleSquad> tacticals = Enumerable.Range(0, marineSquads)
            .Select(i => new BattleSquad(false, SquadFactory.GenerateSquad(
                marines.SquadTemplates[TacticalSquadTemplateId],
                new StaticRNG(), ids, $"Tactical {i}")))
            .ToList();
        return (tacticals, boyz);
    }
}
