using System.Collections.Generic;
using System.Drawing;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.StrategicCombat;
using OnlyWar.Models;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class PlanetControlAndDispositionTests
{
    [Fact]
    public void GetCapitalRegion_SelectsOnceAndDoesNotFollowLaterPopulationChanges()
    {
        Faction imperial = CreateFaction(1, "Imperium", isDefault: true);
        Faction rebels = CreateFaction(2, "Insurrectionists");
        Planet planet = CreatePlanet(imperial, rebels, regionCount: 2);
        planet.Regions[0].RegionFactionMap[imperial.Id].Population = 5000;
        planet.Regions[1].RegionFactionMap[imperial.Id].Population = 1000;

        Assert.Same(planet.Regions[0], planet.GetCapitalRegion());

        planet.Regions[0].RegionFactionMap[imperial.Id].Population = 100;
        planet.Regions[1].RegionFactionMap[imperial.Id].Population = 10_000;

        Assert.Same(planet.Regions[0], planet.GetCapitalRegion());
    }

    [Fact]
    public void GetControllingFaction_RequiresCapitalAndUniquePlurality()
    {
        Faction imperial = CreateFaction(1, "Imperium", isDefault: true);
        Faction rebels = CreateFaction(2, "Insurrectionists");
        Planet planet = CreatePlanet(imperial, rebels, regionCount: 5);
        planet.SetCapitalRegion(planet.Regions[0].Id);

        SetCleanController(planet.Regions[0], rebels);
        SetCleanController(planet.Regions[1], rebels);
        SetCleanController(planet.Regions[2], imperial);
        SetCleanController(planet.Regions[3], imperial);
        SetCleanController(planet.Regions[4], imperial);

        Assert.Null(planet.GetControllingFaction());

        SetCleanController(planet.Regions[4], rebels);

        Assert.Same(rebels, planet.GetControllingFaction());
        Assert.False(planet.IsContested());
    }

    [Fact]
    public void GetControllingFaction_ReturnsNullWhenCapitalIsContested()
    {
        Faction imperial = CreateFaction(1, "Imperium", isDefault: true);
        Faction rebels = CreateFaction(2, "Insurrectionists");
        Planet planet = CreatePlanet(imperial, rebels, regionCount: 3);
        planet.SetCapitalRegion(planet.Regions[0].Id);
        SetCleanController(planet.Regions[1], imperial);
        SetCleanController(planet.Regions[2], imperial);
        planet.Regions[0].RegionFactionMap[imperial.Id].IsPublic = true;
        planet.Regions[0].RegionFactionMap[rebels.Id].IsPublic = true;

        Assert.Null(planet.GetControllingFaction());
        Assert.True(planet.IsContested());
    }

    [Fact]
    public void Disposition_PublicHumanRebelsTruceWithImperiumDuringExternalAttack()
    {
        Faction imperial = CreateFaction(1, "Imperium", isDefault: true);
        Faction rebels = CreateFaction(2, "Insurrectionists");
        rebels.OffersExternalEnemyTruce = true;
        rebels.DefendsHostWhileHidden = true;
        Faction xenos = CreateFaction(3, "Xenos");
        Planet planet = CreatePlanet(imperial, rebels, xenos, regionCount: 1);

        Assert.True(FactionDispositionService.AreEnemies(imperial, rebels, planet));

        planet.Regions[0].RegionFactionMap[xenos.Id].IsPublic = true;

        Assert.False(FactionDispositionService.AreEnemies(imperial, rebels, planet));
        Assert.True(FactionDispositionService.AreEnemies(imperial, xenos, planet));
        Assert.True(FactionDispositionService.AreEnemies(rebels, xenos, planet));
        Assert.True(FactionDispositionService.DefendsHostAgainst(
            HiddenPresence(planet, rebels), xenos));
    }

    [Fact]
    public void Disposition_PublicCultDoesNotReceiveHumanRebelTruce()
    {
        Faction imperial = CreateFaction(1, "Imperium", isDefault: true);
        Faction cult = CreateFaction(2, "Cult", canInfiltrate: true, growthType: GrowthType.Conversion);
        Faction xenos = CreateFaction(3, "Xenos");
        Planet planet = CreatePlanet(imperial, cult, xenos, regionCount: 1);
        planet.Regions[0].RegionFactionMap[cult.Id].IsPublic = true;
        planet.Regions[0].RegionFactionMap[xenos.Id].IsPublic = true;

        Assert.True(cult.DefendsHostWhileHidden);
        Assert.False(cult.OffersExternalEnemyTruce);
        Assert.True(FactionDispositionService.AreEnemies(imperial, cult, planet));
    }

    [Fact]
    public void StrategicDefense_IncludesHiddenHostDefenderAgainstExternalEnemy()
    {
        Faction imperial = CreateFaction(1, "Imperium", isDefault: true);
        Faction cult = CreateFaction(2, "Cult", growthType: GrowthType.Conversion);
        Faction xenos = CreateFaction(3, "Xenos");
        Planet planet = CreatePlanet(imperial, cult, xenos, regionCount: 1);
        RegionFaction loyalists = planet.Regions[0].RegionFactionMap[imperial.Id];
        loyalists.Garrison = 100;
        loyalists.IsPublic = true;
        RegionFaction hiddenCult = planet.Regions[0].RegionFactionMap[cult.Id];
        hiddenCult.Population = 200;
        hiddenCult.IsPublic = false;

        Assert.Equal(300,
            StrategicCombatResolver.CalculateDefenderBattleValueAgainst(loyalists, xenos));
        Assert.Equal(100,
            StrategicCombatResolver.CalculateDefenderBattleValueAgainst(loyalists, cult));
    }

    private static RegionFaction HiddenPresence(Planet planet, Faction faction)
    {
        RegionFaction presence = planet.Regions[0].RegionFactionMap[faction.Id];
        presence.IsPublic = false;
        return presence;
    }

    private static Planet CreatePlanet(Faction first, Faction second, int regionCount) =>
        CreatePlanet(first, second, null, regionCount);

    private static Planet CreatePlanet(Faction first, Faction second, Faction third, int regionCount)
    {
        Planet planet = new(1, "Test", new Coordinate(0, 0), 16, null, 1, 0);
        foreach (Faction faction in new[] { first, second, third })
        {
            if (faction != null) planet.PlanetFactionMap[faction.Id] = new PlanetFaction(faction);
        }

        for (int index = 0; index < regionCount; index++)
        {
            Region region = new(index + 10, planet, 0, $"Region {index}",
                RegionExtensions.GetCoordinatesFromRegionNumber(index), 0);
            foreach (PlanetFaction planetFaction in planet.PlanetFactionMap.Values)
            {
                region.RegionFactionMap[planetFaction.Faction.Id] = new RegionFaction(planetFaction, region)
                {
                    Population = 1000,
                    IsPublic = false
                };
            }
            planet.Regions[index] = region;
        }
        return planet;
    }

    private static void SetCleanController(Region region, Faction controller)
    {
        foreach (RegionFaction presence in region.RegionFactionMap.Values)
        {
            presence.IsPublic = presence.PlanetFaction.Faction.Id == controller.Id;
        }
    }

    private static Faction CreateFaction(int id, string name, bool isDefault = false,
        bool canInfiltrate = false, GrowthType growthType = GrowthType.None)
    {
        return new Faction(id, name, Color.Red, false, isDefault, canInfiltrate, growthType,
            new Dictionary<int, Species>(), new Dictionary<int, SoldierTemplate>(),
            new Dictionary<int, SquadTemplate>(), new Dictionary<int, UnitTemplate>(),
            new Dictionary<int, BoatTemplate>(), new Dictionary<int, ShipTemplate>(),
            new Dictionary<int, FleetTemplate>());
    }
}
