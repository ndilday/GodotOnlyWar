using OnlyWar.Helpers.Fortifications;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Turns;

// Fortifications belong to the ground, not to a faction's private inventory: allies holding a
// region man one set of works between them. These tests cover who pools with whom, and the
// invariant that makes handing works to an ally safe - the side's position does not change.
public class RegionDefensesTests
{
    private static RegionFaction AddPlayerRegionFaction(
        SectorSimulationFixture fixture,
        int region,
        long garrison = 0)
    {
        Faction player = fixture.Sector.PlayerForce.Faction;
        if (!fixture.Planet.PlanetFactionMap.TryGetValue(player.Id, out PlanetFaction planetFaction))
        {
            planetFaction = new PlanetFaction(player) { IsPublic = true };
            fixture.Planet.PlanetFactionMap[player.Id] = planetFaction;
        }

        RegionFaction rf = new(planetFaction, fixture.Planet.Regions[region])
        {
            IsPublic = true,
            Garrison = garrison
        };
        fixture.Planet.Regions[region].RegionFactionMap[player.Id] = rf;
        return rf;
    }

    [Fact]
    public void GetShared_PoolsTheChaptersWorksWithThePdfs()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction pdf = fixture.DefaultRegionFaction(0);
        RegionFaction marines = AddPlayerRegionFaction(fixture, 0);
        pdf.Entrenchment = 1.0;
        marines.Entrenchment = 1.0;

        // Two allies who have each paid for one level hold a 1.28 position between them, not a 2.
        Assert.Equal(1.2788, RegionDefenses.GetShared(marines, DefenseType.Entrenchment), 4);
        Assert.Equal(1.2788, RegionDefenses.GetShared(pdf, DefenseType.Entrenchment), 4);
    }

    [Fact]
    public void GetShared_IgnoresEnemyWorks()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction pdf = fixture.DefaultRegionFaction(0);
        RegionFaction tyranids = fixture.AddConsumptionFaction(0, population: 5000, organization: 100);
        pdf.Entrenchment = 1.0;
        tyranids.Entrenchment = 3.0;

        Assert.Equal(1.0, RegionDefenses.GetShared(pdf, DefenseType.Entrenchment), 6);
        Assert.Equal(3.0, RegionDefenses.GetShared(tyranids, DefenseType.Entrenchment), 6);
    }

    [Fact]
    public void GetShared_IgnoresAlliesThatHaveGoneToGround()
    {
        // Works nobody is manning cannot fortify the ally still standing in the open - that is the
        // premise DecayUnmannedDefenses already runs on.
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction pdf = fixture.DefaultRegionFaction(0);
        RegionFaction marines = AddPlayerRegionFaction(fixture, 0);
        pdf.Entrenchment = 2.0;
        pdf.IsPublic = false;
        marines.Entrenchment = 1.0;

        Assert.Equal(1.0, RegionDefenses.GetShared(marines, DefenseType.Entrenchment), 6);
    }

    [Fact]
    public void Build_AddsPointsSoRepeatedEffortDecelerates()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction marines = AddPlayerRegionFaction(fixture, 0);

        RegionDefenses.Build(marines, DefenseType.Entrenchment, 0.6);
        double afterFirstWeek = marines.Entrenchment;
        RegionDefenses.Build(marines, DefenseType.Entrenchment, 0.6);
        double secondWeekGain = marines.Entrenchment - afterFirstWeek;

        Assert.True(afterFirstWeek > 0.75);
        Assert.True(secondWeekGain < afterFirstWeek);
    }

    [Fact]
    public void Damage_SpreadsTheLossAcrossContributors()
    {
        // A raid wrecks the position that is there, so it comes out of whoever actually built it -
        // not only the faction that happened to be the mission's nominal target.
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction pdf = fixture.DefaultRegionFaction(0);
        RegionFaction marines = AddPlayerRegionFaction(fixture, 0);
        pdf.Entrenchment = 2.0;
        marines.Entrenchment = 2.0;
        double sharedBefore = RegionDefenses.GetShared(pdf, DefenseType.Entrenchment);

        RegionDefenses.Damage(pdf, DefenseType.Entrenchment, 0.5);

        Assert.Equal(
            sharedBefore - 0.5,
            RegionDefenses.GetShared(pdf, DefenseType.Entrenchment),
            4);
        Assert.True(marines.Entrenchment < 2.0, "the Chapter's own works should have taken damage too");
        Assert.Equal(marines.Entrenchment, pdf.Entrenchment, 6);
    }

    [Fact]
    public void TransferToAlly_LeavesTheSidesPositionUnchanged()
    {
        // The whole reason handover can happen automatically: custody moves, the trench line does
        // not. Level 2 works (11 points) handed to a level 1 ally (1 point) leave that ally holding
        // 12 points - the 2.04 the two of them already had between them.
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction pdf = fixture.DefaultRegionFaction(0);
        pdf.Garrison = 5000;
        RegionFaction marines = AddPlayerRegionFaction(fixture, 0);
        pdf.Entrenchment = 1.0;
        marines.Entrenchment = 2.0;
        double sharedBefore = RegionDefenses.GetShared(marines, DefenseType.Entrenchment);

        RegionFaction inheritor = RegionDefenses.TransferToAlly(marines);

        Assert.Same(pdf, inheritor);
        Assert.Equal(0.0, marines.Entrenchment);
        Assert.Equal(sharedBefore, pdf.Entrenchment, 6);
        Assert.Equal(2.0374, pdf.Entrenchment, 4);
    }

    [Fact]
    public void TransferToAlly_MovesEveryKindOfWorks()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction pdf = fixture.DefaultRegionFaction(0);
        pdf.Garrison = 5000;
        RegionFaction marines = AddPlayerRegionFaction(fixture, 0);
        marines.Entrenchment = 1.0;
        marines.ListeningPost = 2.0;
        marines.AntiAir = 0.5;

        RegionDefenses.TransferToAlly(marines);

        Assert.Equal(0.0, marines.Entrenchment);
        Assert.Equal(0.0, marines.ListeningPost);
        Assert.Equal(0.0, marines.AntiAir);
        Assert.Equal(1.0, pdf.Entrenchment, 6);
        Assert.Equal(2.0, pdf.ListeningPost, 6);
        Assert.Equal(0.5, pdf.AntiAir, 6);
    }

    [Fact]
    public void TransferToAlly_RefusesWhenNoAllyCanManTheWorks()
    {
        // A PDF with no garrison left cannot inherit: works nobody can hold are abandoned, and the
        // caller falls back to its own halve-and-decay path.
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction pdf = fixture.DefaultRegionFaction(0);
        pdf.Garrison = 0;
        RegionFaction marines = AddPlayerRegionFaction(fixture, 0);
        marines.Entrenchment = 2.0;

        Assert.Null(RegionDefenses.TransferToAlly(marines));
        Assert.Equal(2.0, marines.Entrenchment, 6);
    }

    [Fact]
    public void TransferToAlly_NeverHandsWorksToAnEnemy()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        fixture.DefaultRegionFaction(0).Garrison = 0;
        fixture.AddConsumptionFaction(0, population: 50_000, organization: 100);
        RegionFaction marines = AddPlayerRegionFaction(fixture, 0);
        marines.Entrenchment = 2.0;

        Assert.Null(RegionDefenses.TransferToAlly(marines));
        Assert.Equal(2.0, marines.Entrenchment, 6);
    }

    [Fact]
    public void GetAlliedPoints_ExcludesTheAskingFactionsOwnContribution()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction pdf = fixture.DefaultRegionFaction(0);
        RegionFaction marines = AddPlayerRegionFaction(fixture, 0);
        pdf.Entrenchment = 2.0;
        marines.Entrenchment = 1.0;

        Assert.Equal(
            FortificationMath.LevelToPoints(2.0),
            RegionDefenses.GetAlliedPoints(marines, DefenseType.Entrenchment),
            6);
    }
}
