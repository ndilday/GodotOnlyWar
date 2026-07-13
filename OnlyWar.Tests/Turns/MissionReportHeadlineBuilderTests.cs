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
            ["Melee Carnifex"],
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
            ["Alpha", "Bravo"],
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
            ["Melee Carnifex"],
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
            ["Alpha", "Bravo"],
            "Genestealer Cult",
            "Terra Lambda",
            "Terra");

        Assert.Equal("2 squads Extermination on Genestealer Cult in Terra Lambda, Terra", headline);
    }
}
