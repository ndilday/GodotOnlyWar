using OnlyWar.Helpers.Orders;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Orders;

public class InboundOrdersTests
{
    [Fact]
    public void MissionAndAggressionLabel_AppendsOrderAggressionToMission()
    {
        Order order = new(1, [], false, false, Aggression.Aggressive, null);
        InboundOrderInfo info = new(order, "Recon", "local", 1);

        Assert.Equal("Recon (Aggressive)", info.MissionAndAggressionLabel);
    }

    [Fact]
    public void HoverText_MatchesCompactButtonTextForOrdinaryOrder()
    {
        Order order = new(1, [], false, false, Aggression.Aggressive, null);
        InboundOrderInfo info = new(order, "Recon", "local", 2);

        Assert.Equal("Recon (Aggressive) · 2 squads · from local", info.SummaryLabel);
        Assert.Equal(info.SummaryLabel, info.HoverText);
    }

    [Fact]
    public void HoverText_AddsAmbushRecommendationWithoutPuttingItOnButton()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Mission mission = new(
            MissionType.Ambush,
            fixture.AddPublicCult(region: 0, population: 2_000, organization: 100),
            missionSize: 1,
            targetBattleValue: 91);
        Order order = new(1, [], false, false, Aggression.Normal, mission);
        InboundOrderInfo info = new(
            order,
            "Ambush — Genestealer Cult",
            "Ash Wastes",
            1);

        Assert.Equal(
            "Ambush — Genestealer Cult (Normal) · 1 squad · from Ash Wastes",
            info.SummaryLabel);
        Assert.Equal(
            $"{info.SummaryLabel}\nRecommended Minimum Force: 2 squads",
            info.HoverText);
    }
}
