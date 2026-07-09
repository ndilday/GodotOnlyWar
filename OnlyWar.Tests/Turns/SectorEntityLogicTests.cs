using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Turns;

// Exercises the end-of-turn "Sector Entity Logic" (TDD 6.3): population growth,
// intelligence decay, special mission expiration, and governor request generation.
// Driven through TurnController.ProcessTurn with a seeded global RNG.
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
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
        pdf.Population = 100000;
        pdf.Garrison = 100000;
        // capacity equals the region's population (100000), so crowding is 0 and no new
        // garrison is recruited this week; only the 0.1%/week attrition applies
        fixture.Planet.Regions[0].CarryingCapacity = 100000;

        fixture.ProcessTurn();

        Assert.Equal(99900, pdf.Garrison); // 100000 - 100000 * 0.001
    }

    [Fact]
    public void EndOfTurnRegionFactionsUpdate_PublicEnemyInRegionDraftsMorePdfFromGrowth()
    {
        RNG.Reset(1);
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction pdf = fixture.DefaultRegionFaction(0);
        pdf.Population = 1_000_000;
        pdf.Garrison = 0;
        fixture.AddPublicCult(0, population: 100, organization: 100);

        new TurnController().EndOfTurnRegionFactionsUpdate(pdf, pdfRatio: 0.5f);

        Assert.Equal(60, pdf.Garrison); // 1,000,000 * 0.0004 baseline growth * 15%
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
    public void ProcessTurn_PublicConversionFactionDoesNotConvert()
    {
        RNG.Reset(1);
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        // A revealed (public) cult is in open warfare, not stealing converts at night, so it makes
        // no new converts and does not grow this turn — unlike the hidden cult above. (An idle
        // public cult may still sacrificially predate the populace, but that is a separate path
        // that never adds to the cult's own numbers.)
        RegionFaction cult = fixture.AddPublicCult(0, population: 50, organization: 1);

        fixture.ProcessTurn();

        Assert.Equal(50, cult.Population);
    }

    [Fact]
    public void ProcessTurn_DecaysRegionIntelligenceByQuarter()
    {
        RNG.Reset(1);
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region region = fixture.Planet.Regions[3];
        fixture.DefaultPlanetFaction.SetRegionIntel(region, 4.0f);

        fixture.ProcessTurn();

        Assert.Equal(3.0f, fixture.DefaultPlanetFaction.GetRegionIntel(region), precision: 4);
        Assert.Equal(3.0f, region.GetPlayerVisibleIntel(), precision: 4);
    }

    [Fact]
    public void ProcessTurn_PlayerAndDefaultSharingPoolsOnlyThisTurnIntelGains()
    {
        RNG.Reset(1);
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region region = fixture.Planet.Regions[3];
        Faction playerFaction = fixture.Sector.PlayerForce.Faction;
        PlanetFaction playerPlanetFaction = new(playerFaction) { IsPublic = true };
        fixture.Planet.PlanetFactionMap[playerFaction.Id] = playerPlanetFaction;
        fixture.Planet.Regions[3].RegionFactionMap[playerFaction.Id] =
            new RegionFaction(playerPlanetFaction, region)
            {
                Population = 1,
                IsPublic = true,
                Organization = 100,
                ListeningPost = 3
            };
        fixture.DefaultRegionFaction(3).ListeningPost = 2;
        playerPlanetFaction.SetRegionIntel(region, 2.0f);
        fixture.DefaultPlanetFaction.SetRegionIntel(region, 3.0f);

        fixture.ProcessTurn();

        Assert.Equal(2.5f, playerPlanetFaction.GetRegionIntel(region), precision: 4);
        Assert.Equal(3.25f, fixture.DefaultPlanetFaction.GetRegionIntel(region), precision: 4);
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
    public void ProcessTurn_PrunesSpecialMissionsForRemovedRegionFactions()
    {
        RNG.Reset(98765);
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction cult = fixture.AddHiddenFaction(0, GrowthType.Logistic, population: 20000);
        Region region = fixture.Planet.Regions[0];
        Mission staleMission = new(MissionType.Ambush, cult, 1);
        region.SpecialMissions.Add(staleMission);
        region.RegionFactionMap.Remove(cult.PlanetFaction.Faction.Id);

        fixture.ProcessTurn();

        Assert.DoesNotContain(staleMission, region.SpecialMissions);
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

    [Fact]
    public void ProcessTurn_FulfilledRequest_GrantsRequisition()
    {
        RNG.Reset(1);
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Character governor = fixture.InstallGovernor(investigation: 0f, neediness: 0f, opinion: 1f);
        // an already-fulfilled request (completed on construction via a non-null fulfilled date)
        PresenceRequest request = new(
            1, fixture.Planet, governor, null, new Date(1, 1, 1), new Date(1, 1, 1));
        governor.ActiveRequest = request;
        fixture.Sector.PlayerForce.Requests.Add(request);
        int before = fixture.Sector.PlayerForce.Army.Requisition;

        fixture.ProcessTurn();

        // fulfilling the request fires the Requisition faucet (PRD 4.23) and clears the request
        Assert.True(fixture.Sector.PlayerForce.Army.Requisition > before);
        Assert.Null(governor.ActiveRequest);
        Assert.DoesNotContain(request, fixture.Sector.PlayerForce.Requests);
    }
}
