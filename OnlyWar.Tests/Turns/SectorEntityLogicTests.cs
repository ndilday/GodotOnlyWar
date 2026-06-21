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
        // pop chosen so growth (pop * 0.0006) is a whole number => deterministic, no rounding draw.
        // region capacity is 0 here (uncapped), so the crowding factor does not apply.
        RegionFaction cult = fixture.AddHiddenFaction(0, GrowthType.Logistic, population: 20000);

        fixture.ProcessTurn();

        Assert.Equal(20012, cult.Population); // 20000 * 0.0006 = 12
    }

    [Fact]
    public void ProcessTurn_HaltsLogisticGrowthWhenRegionAtCarryingCapacity()
    {
        RNG.Reset(1);
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction cult = fixture.AddHiddenFaction(0, GrowthType.Logistic, population: 20000);
        // region 0 holds the default faction (20000) plus the cult (20000); capacity equals
        // that combined total, so the crowding factor is 0 and no organic growth occurs
        fixture.Planet.Regions[0].CarryingCapacity = 40000;

        fixture.ProcessTurn();

        Assert.Equal(20000, cult.Population);
    }

    [Fact]
    public void ProcessTurn_RetiresFractionOfGarrisonEachWeek()
    {
        RNG.Reset(1);
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction pdf = fixture.DefaultRegionFaction(0);
        pdf.Garrison = 100000;
        // capacity equals the region's population (20000), so crowding is 0 and no new
        // garrison is recruited this week; only the 0.1%/week attrition applies
        fixture.Planet.Regions[0].CarryingCapacity = 20000;

        fixture.ProcessTurn();

        Assert.Equal(99900, pdf.Garrison); // 100000 - 100000 * 0.001
    }

    [Fact]
    public void ProcessTurn_DeclinesGentlyWhenRegionAboveCarryingCapacity()
    {
        RNG.Reset(1);
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction cult = fixture.AddHiddenFaction(0, GrowthType.Logistic, population: 20000);
        // region 0 holds the default faction (20000) plus the cult (20000) = 40000 total,
        // against a capacity of 20000: crowding is 1 - 40000/20000 = -1, so the cult's
        // logistic growth (20000 * 0.0006 = 12) is negated into a decline of 12
        fixture.Planet.Regions[0].CarryingCapacity = 20000;

        fixture.ProcessTurn();

        Assert.Equal(19988, cult.Population);
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
