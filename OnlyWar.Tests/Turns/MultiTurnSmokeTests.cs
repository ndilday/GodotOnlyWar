using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Turns;

// Seeded multi-turn smoke test (TDD 9.2.1 #8): run several end-of-turn cycles with a
// fixed RNG against a compact hand-built sector and assert high-level invariants hold.
public class MultiTurnSmokeTests
{
    private const int TurnCount = 12;

    [Fact]
    public void RunningManyTurns_KeepsSectorInvariantsConsistent()
    {
        RNG.Reset(424242);
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction cult = fixture.AddHiddenFaction(0, GrowthType.Conversion, population: 50);
        fixture.AddControllingFaction(1, "Heretic Rebels", population: 5000);
        fixture.InstallGovernor(investigation: 1f, neediness: 1f, opinion: 1f);
        fixture.Planet.Regions[5].IntelligenceLevel = 10.0f;

        long startingPopulation = fixture.Planet.Population;

        for (int turn = 0; turn < TurnCount; turn++)
        {
            fixture.ProcessTurn();
        }

        // the planet remains populated and no region went negative
        Assert.True(fixture.Planet.Population > 0);
        Assert.All(fixture.Planet.Regions,
            r => Assert.All(r.RegionFactionMap.Values, rf => Assert.True(rf.Population >= 0)));

        // the default Imperial faction persists on the planet
        Assert.True(fixture.Planet.PlanetFactionMap.ContainsKey(fixture.Default.Id));

        // the conversion cult steadily recruited from the default population
        Assert.True(cult.Population > 50);

        // intelligence decays geometrically (0.75^12 ~ 0.03) toward zero
        Assert.True(fixture.Planet.Regions[5].IntelligenceLevel < 1.0f);

        // the governor raised (and is still waiting on) at least one request for aid
        Assert.True(fixture.Sector.PlayerForce.Requests.Count >= 1);

        // total population stayed within sane bounds (conversion moves people, growth is slow)
        Assert.True(fixture.Planet.Population >= startingPopulation - TurnCount * RegionCountSafety);
    }

    // conversion can only move a bounded number of people per turn; this guards against
    // a runaway depopulation regression without asserting an exact (RNG-dependent) total.
    private const int RegionCountSafety = 1000;
}
