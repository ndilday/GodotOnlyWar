using System.Collections.Generic;
using System.Drawing;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class FactionRevealServiceTests
{
    [Fact]
    public void Reveal_UnrestMovesEmbeddedPdfToArmedCiviliansWithoutChangingPopulation()
    {
        var (_, _, unrest) = BuildRegion(GrowthType.Unrest, 10_000, 700, 1_300);

        FactionRevealService.Reveal(unrest);

        Assert.True(unrest.IsPublic);
        Assert.True(unrest.PlanetFaction.IsPublic);
        Assert.Equal(10_000, unrest.Population);
        Assert.Equal(0, unrest.Garrison);
        Assert.Equal(2_000, unrest.ArmedCivilians);
    }

    [Fact]
    public void Reveal_ConversionZerosEmbeddedPdfWithoutDoubleCountingPopulation()
    {
        var (_, _, cult) = BuildRegion(GrowthType.Conversion, 10_000, 700);

        FactionRevealService.Reveal(cult);

        Assert.True(cult.IsPublic);
        Assert.True(cult.PlanetFaction.IsPublic);
        Assert.Equal(10_000, cult.Population);
        Assert.Equal(0, cult.Garrison);
        Assert.Equal(0, cult.ArmedCivilians);
    }

    [Fact]
    public void Reveal_IsIdempotentForArmedPoolTransfers()
    {
        var (_, _, unrest) = BuildRegion(GrowthType.Unrest, 10_000, 700, 1_300);

        FactionRevealService.Reveal(unrest);
        FactionRevealService.Reveal(unrest);

        Assert.Equal(10_000, unrest.Population);
        Assert.Equal(0, unrest.Garrison);
        Assert.Equal(2_000, unrest.ArmedCivilians);
    }

    [Fact]
    public void PlanetaryDefenseForces_IncludesHiddenHostDefendersButNeverArmedCivilians()
    {
        var (region, _, unrest) = BuildRegion(GrowthType.Unrest, 10_000, 700, 2_000);

        Assert.True(unrest.PlanetFaction.Faction.DefendsHostWhileHidden);
        Assert.Equal(3_700, region.PlanetaryDefenseForces);
    }

    [Fact]
    public void PlanetaryDefenseForces_ExcludesHiddenFactionThatDoesNotDefendHost()
    {
        var (region, _, hiddenXenos) = BuildRegion(GrowthType.Logistic, 10_000, 700);

        Assert.False(hiddenXenos.PlanetFaction.Faction.DefendsHostWhileHidden);
        Assert.Equal(3_000, region.PlanetaryDefenseForces);
    }

    [Fact]
    public void PlanetaryDefenseForces_StopsCountingEmbeddedGarrisonWhenFactionReveals()
    {
        var (region, _, unrest) = BuildRegion(GrowthType.Unrest, 10_000, 700);

        Assert.Equal(3_700, region.PlanetaryDefenseForces);

        FactionRevealService.Reveal(unrest);

        Assert.Equal(3_000, region.PlanetaryDefenseForces);
        Assert.Equal(700, unrest.ArmedCivilians);
    }

    private static (Region Region, RegionFaction Imperial, RegionFaction Hidden) BuildRegion(
        GrowthType hiddenGrowthType,
        long hiddenPopulation,
        long hiddenGarrison,
        long hiddenArmedCivilians = 0)
    {
        PlanetTemplate template = new(
            1,
            "Test World",
            1,
            new LogNormalValueTemplate { Floor = 1_000, Scale = 0 },
            new LogNormalValueTemplate { Floor = 2_000, Scale = 0 },
            new NormalizedValueTemplate { BaseValue = 1, StandardDeviation = 0 },
            new LinearValueTemplate { MinValue = 0, MaxValue = 0 });
        Planet planet = new(1, "Test World", new Coordinate(1, 1), 1, template, 1, 0);
        Region region = new(1, planet, 0, "Capital", new RegionCoordinate(0, 0), 0);
        planet.Regions[0] = region;

        Faction imperialFaction = BuildFaction(1, GrowthType.None, isDefault: true);
        PlanetFaction imperialPlanetFaction = new(imperialFaction) { IsPublic = true };
        RegionFaction imperial = new(imperialPlanetFaction, region)
        {
            Population = 20_000,
            Garrison = 3_000,
            IsPublic = true
        };
        planet.PlanetFactionMap[imperialFaction.Id] = imperialPlanetFaction;
        region.RegionFactionMap[imperialFaction.Id] = imperial;

        Faction hiddenFaction = BuildFaction(2, hiddenGrowthType, isDefault: false);
        PlanetFaction hiddenPlanetFaction = new(hiddenFaction) { IsPublic = false };
        RegionFaction hidden = new(hiddenPlanetFaction, region)
        {
            Population = hiddenPopulation,
            Garrison = hiddenGarrison,
            ArmedCivilians = hiddenArmedCivilians,
            IsPublic = false
        };
        planet.PlanetFactionMap[hiddenFaction.Id] = hiddenPlanetFaction;
        region.RegionFactionMap[hiddenFaction.Id] = hidden;

        return (region, imperial, hidden);
    }

    private static Faction BuildFaction(int id, GrowthType growthType, bool isDefault) =>
        new(
            id,
            growthType.ToString(),
            Color.Red,
            isPlayerFaction: false,
            isDefaultFaction: isDefault,
            canInfiltrate: false,
            growthType,
            new Dictionary<int, Species> { [TestModelFactory.HumanSpecies.Id] = TestModelFactory.HumanSpecies },
            new Dictionary<int, SoldierTemplate>(),
            new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, UnitTemplate>(),
            new Dictionary<int, BoatTemplate>(),
            new Dictionary<int, ShipTemplate>(),
            new Dictionary<int, FleetTemplate>());
}
