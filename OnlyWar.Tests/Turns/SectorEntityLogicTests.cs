using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Turns;

// Exercises the end-of-turn "Sector Entity Logic" (TDD 6.3): population growth,
// intelligence decay, special mission expiration, and governor request generation.
// Driven through TurnController.ProcessTurn with a seeded global RNG.
public class SectorEntityLogicTests
{
    [Fact]
    public void ProcessTurn_GrowsLogisticFactionByExpectedFraction()
    {
        RNG.Reset(1);
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        // pop chosen so growth (pop * 0.00015) is a whole number => deterministic, no rounding draw
        RegionFaction cult = fixture.AddHiddenFaction(0, GrowthType.Logistic, population: 20000);

        fixture.ProcessTurn();

        Assert.Equal(20003, cult.Population); // 20000 * 0.00015 = 3
    }

    [Fact]
    public void ProcessTurn_ConversionFactionConvertsOneDefaultMemberPerWeek()
    {
        RNG.Reset(1);
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction cult = fixture.AddHiddenFaction(0, GrowthType.Conversion, population: 50);

        fixture.ProcessTurn();

        // one default member is converted to the cult each week (no organic growth under pop 100)
        Assert.Equal(51, cult.Population);
    }

    [Fact]
    public void ProcessTurn_DecaysRegionIntelligenceByQuarter()
    {
        RNG.Reset(1);
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        fixture.Planet.Regions[3].IntelligenceLevel = 4.0f;

        fixture.ProcessTurn();

        Assert.Equal(3.0f, fixture.Planet.Regions[3].IntelligenceLevel, precision: 4);
    }

    [Fact]
    public void ProcessTurn_ExpiresSomeButNotAllStaleSpecialMissions()
    {
        RNG.Reset(98765);
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction cult = fixture.AddHiddenFaction(0, GrowthType.Logistic, population: 20000);
        Region region = fixture.Planet.Regions[0];
        for (int i = 0; i < 100; i++)
        {
            region.SpecialMissions.Add(new Mission(MissionType.Ambush, cult, 1));
        }

        fixture.ProcessTurn();

        // each stale mission has a 25% chance to expire: expect roughly 75 to remain
        Assert.InRange(region.SpecialMissions.Count, 50, 95);
    }

    [Fact]
    public void ProcessTurn_GeneratesGovernorRequestAgainstPublicThreat()
    {
        RNG.Reset(1);
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        // a public hostile faction controlling its own region triggers a real threat
        fixture.AddControllingFaction(1, "Heretic Rebels", population: 5000);
        Character governor = fixture.InstallGovernor(investigation: 1f, neediness: 1f, opinion: 1f);

        fixture.ProcessTurn();

        Assert.NotNull(governor.ActiveRequest);
        Assert.Contains(governor.ActiveRequest, fixture.Sector.PlayerForce.Requests);
    }
}
