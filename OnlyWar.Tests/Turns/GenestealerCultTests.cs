using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Turns;

// Coverage for Genestealer Cult idle-force behavior (PRD §4.24): a public Cult grinds down the PDF
// via cross-border raids (the offensive machinery, tested elsewhere); ResolveCultManeuvers governs
// the force in regions with no active Imperial enemy in reach — it flows toward the front where one
// remains adjacent, and slaughters the local Imperial population as sacrifices (with no growth of
// its own) where the fight is wholly out of reach. Exercised directly for exact assertions.
public class GenestealerCultTests
{
    [Fact]
    public void ResolveCultManeuvers_IsolatedCult_SacrificiallyPredates_WithoutGrowing()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region region = fixture.Planet.Regions[0];
        // A defenceless local Imperial population (public but garrison 0), with no active PDF anywhere.
        RegionFaction prey = fixture.DefaultRegionFaction(0);
        prey.Population = 500_000;
        RegionFaction cult = fixture.AddPublicCult(0, population: 100_000, organization: 100);
        long preyBefore = prey.Population;
        long cultBefore = cult.Population;

        TurnController.ResolveCultManeuvers(region);

        Assert.True(prey.Population < preyBefore, "the Cult should slaughter local Imperials");
        // Conversion, not slaughter, is the Cult's growth: sacrifices add nothing to its numbers.
        Assert.Equal(cultBefore, cult.Population);
    }

    [Fact]
    public void ResolveCultManeuvers_WithActivePdfNearby_LeavesTheFightToRaids()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region region = fixture.Planet.Regions[0];
        RegionFaction prey = fixture.DefaultRegionFaction(0);
        prey.Population = 500_000;
        prey.Garrison = 1_000; // an active PDF garrison => this region is on the front
        RegionFaction cult = fixture.AddPublicCult(0, population: 100_000, organization: 100);
        long preyBefore = prey.Population;
        long cultBefore = cult.Population;

        TurnController.ResolveCultManeuvers(region);

        // A front region's force is committed to the raid machinery, not idle predation/relocation.
        Assert.Equal(preyBefore, prey.Population);
        Assert.Equal(cultBefore, cult.Population);
    }

    [Fact]
    public void ResolveCultManeuvers_RelocatesIdleForceTowardTheFront()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region r0 = fixture.Planet.Regions[0];
        Region r1 = r0.GetAdjacentRegions().First();
        // A region two hops from r0 (adjacent to r1, not to r0), holding an active PDF: r0 is idle,
        // r1 is on the front.
        Region r2 = r1.GetAdjacentRegions().First(r => r != r0 && !r0.GetAdjacentRegions().Contains(r));
        fixture.DefaultRegionFaction(r2.Id).Garrison = 1_000;
        RegionFaction cult = fixture.AddPublicCult(0, population: 100_000, organization: 100);
        int cultFactionId = cult.PlanetFaction.Faction.Id;
        long cultBefore = cult.Population;

        TurnController.ResolveCultManeuvers(r0);

        long moved = cultBefore - cult.Population;
        Assert.True(moved > 0, "idle force should flow toward the front");
        // The moved force appears in a front-adjacent region, conserving the Cult's numbers.
        long relocated = r0.GetAdjacentRegions()
            .Where(r => r.RegionFactionMap.ContainsKey(cultFactionId))
            .Sum(r => r.RegionFactionMap[cultFactionId].Population);
        Assert.Equal(moved, relocated);
    }

    [Fact]
    public void ConversionFaction_RaidsRatherThanInvades()
    {
        // The Cult withdraws after a victorious raid rather than planting a foothold (PRD §4.24);
        // only Consumption swarms invade on victory.
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction cult = fixture.AddPublicCult(0, population: 1_000, organization: 100);

        Assert.False(cult.PlanetFaction.Faction.InvadesOnVictory);
    }
}
