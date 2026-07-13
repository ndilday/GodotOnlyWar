using OnlyWar.Helpers;
using OnlyWar.Models.Missions;
using Xunit;

namespace OnlyWar.Tests.Turns;

public class MissionReportHeadlineBuilderTests
{
    [Fact]
    public void Build_SinglePlayerRecon_UsesSquadAndRegionWithoutFactionTarget()
    {
        string headline = MissionReportHeadlineBuilder.Build(
            MissionType.Recon,
            true,
            ["Melee Carnifex"],
            "Imperial",
            "Tyranids",
            "Terra Lambda",
            "Terra");

        Assert.Equal("Melee Carnifex Recon Terra Lambda Terra", headline);
    }

    [Fact]
    public void Build_MultiplePlayerPatrol_UsesSquadCountAndRegion()
    {
        string headline = MissionReportHeadlineBuilder.Build(
            MissionType.Patrol,
            true,
            ["Alpha", "Bravo"],
            "Imperial",
            "Tyranids",
            "Terra Lambda",
            "Terra");

        Assert.Equal("2 squads Recon Terra Lambda, Terra", headline);
    }

    [Fact]
    public void Build_SinglePlayerMission_UsesSquadAndEnemyFaction()
    {
        string headline = MissionReportHeadlineBuilder.Build(
            MissionType.Advance,
            true,
            ["Melee Carnifex"],
            "Imperial",
            "Tyranids",
            "Terra Lambda",
            "Terra");

        Assert.Equal("Melee Carnifex Advance on Tyranids in Terra Lambda, Terra", headline);
    }

    [Fact]
    public void Build_MultiplePlayerMission_UsesSquadCountAndEnemyFaction()
    {
        string headline = MissionReportHeadlineBuilder.Build(
            MissionType.Extermination,
            true,
            ["Alpha", "Bravo"],
            "Imperial",
            "Genestealer Cult",
            "Terra Lambda",
            "Terra");

        Assert.Equal("2 squads Extermination on Genestealer Cult in Terra Lambda, Terra", headline);
    }

    [Fact]
    public void Build_EnemyMission_UsesActingFactionAndRegion()
    {
        string headline = MissionReportHeadlineBuilder.Build(
            MissionType.Advance,
            false,
            ["Melee Carnifex"],
            "Tyranids",
            "Imperial",
            "Terra Lambda",
            "Terra");

        Assert.Equal("Tyranids Advance in Terra Lambda, Terra", headline);
    }
}
