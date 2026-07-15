using System.Collections.Generic;
using System.IO;
using System.Linq;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Helpers.Turns;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Turns;

public class CivilUnrestTurnProcessorIntegrationTests
{
    [Fact]
    public void PlanetTurnProcessor_InvokesCivilUnrestSimulation()
    {
        CivilFixture fixture = CivilFixture.Create();
        RegionFaction loyalists = fixture.Loyalists(0);
        loyalists.Population = 10_000;
        loyalists.Garrison = 3_000;
        loyalists.Contentment = 0;

        fixture.ProcessPlanetTurn();

        Assert.NotNull(fixture.Unrest(0));
        Assert.True(fixture.Unrest(0).Population > 0);
    }

    [Fact]
    public void LowContentment_RecruitsCiviliansAndPdfWithoutChangingNominalPdfRoster()
    {
        CivilFixture fixture = CivilFixture.Create();
        RegionFaction loyalists = fixture.Loyalists(0);
        loyalists.Population = 10_000;
        loyalists.Garrison = 3_000;
        loyalists.Contentment = 0;
        long nominalPdfBefore = loyalists.Region.PlanetaryDefenseForces;

        fixture.ProcessCivilUnrest();

        RegionFaction unrest = fixture.Unrest(0);
        Assert.NotNull(unrest);
        Assert.True(unrest.Population > 0);
        Assert.True(unrest.Garrison > 0);
        Assert.True(loyalists.Garrison < 3_000);
        Assert.Equal(nominalPdfBefore, loyalists.Region.PlanetaryDefenseForces);
    }

    [Fact]
    public void TwoToOneStrength_RevealsAndConvertsEmbeddedPdfToArmedCivilians()
    {
        CivilFixture fixture = CivilFixture.Create();
        RegionFaction loyalists = fixture.Loyalists(0);
        loyalists.Population = 100_000;
        loyalists.Garrison = 50;
        loyalists.Contentment = 0;
        RegionFaction unrest = fixture.AddUnrest(0, population: 30_000, garrison: 1_000,
            armedCivilians: 2_000, isPublic: false);
        long combinedPopulationBefore = loyalists.Population + unrest.Population;

        fixture.ProcessCivilUnrest();

        Assert.True(unrest.IsPublic);
        Assert.Equal(0, unrest.Garrison);
        Assert.True(unrest.ArmedCivilians >= 3_000);
        Assert.Equal(combinedPopulationBefore, loyalists.Population + unrest.Population);
    }

    [Fact]
    public void PublicExternalEnemy_BlocksOtherwiseOverwhelmingReveal()
    {
        CivilFixture fixture = CivilFixture.Create();
        RegionFaction loyalists = fixture.Loyalists(0);
        loyalists.Population = 100_000;
        loyalists.Garrison = 1;
        loyalists.Contentment = 0;
        RegionFaction unrest = fixture.AddUnrest(0, population: 30_000, garrison: 1_000,
            armedCivilians: 10_000, isPublic: false);
        fixture.AddTyranids(5, 1_000);

        fixture.ProcessCivilUnrest();

        Assert.False(unrest.IsPublic);
        Assert.True(unrest.MilitaryStrength >= loyalists.MilitaryStrength * 2);
    }

    [Fact]
    public void PublicRevoltBelowHalfLocalLoyalStrength_ReturnsToHiding()
    {
        CivilFixture fixture = CivilFixture.Create();
        RegionFaction loyalists = fixture.Loyalists(0);
        loyalists.Population = 100_000;
        loyalists.Garrison = 10_000;
        loyalists.Contentment = 100;
        RegionFaction unrest = fixture.AddUnrest(0, population: 1_000, garrison: 0,
            armedCivilians: 100, isPublic: true);

        fixture.ProcessCivilUnrest();

        Assert.False(unrest.IsPublic);
        Assert.True(unrest.MilitaryStrength < loyalists.MilitaryStrength * 0.5);
    }

    [Fact]
    public void HiddenSupporters_MigrateOnlyOneRegionTowardPublicRevolt()
    {
        CivilFixture fixture = CivilFixture.Create();
        fixture.Loyalists(0).Contentment = 0;
        fixture.Loyalists(0).Garrison = 10_000;
        fixture.AddUnrest(0, population: 10_000, garrison: 500,
            armedCivilians: 2_000, isPublic: false);

        fixture.Loyalists(3).Garrison = 1;
        fixture.Loyalists(3).Contentment = 0;
        fixture.AddUnrest(3, population: 20_000, garrison: 0,
            armedCivilians: 10_000, isPublic: true);
        Assert.Null(fixture.Unrest(1));

        fixture.ProcessCivilUnrest();

        RegionFaction firstStep = fixture.Unrest(1);
        Assert.NotNull(firstStep);
        Assert.True(firstStep.Population > 0);
        Assert.True(firstStep.ArmedCivilians > 0);
        Assert.False(firstStep.IsPublic);
    }

    private sealed class CivilFixture
    {
        private readonly GameRulesData _rules;
        private readonly GameSession _session;
        private readonly PlanetFaction _unrestPlanetFaction;

        internal Planet Planet { get; }

        private CivilFixture(GameRulesData rules, Sector sector, Planet planet)
        {
            _rules = rules;
            Planet = planet;
            _session = new GameSession(rules, sector, new Date(1, 1, 1), new FixedRNG());
            _unrestPlanetFaction = new PlanetFaction(rules.SectorFactions.Insurrectionists)
            {
                IsPublic = false
            };
        }

        internal static CivilFixture Create()
        {
            Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
            GameRulesData rules = new();
            PlanetTemplate template = rules.PlanetTemplateMap.Values.First();
            int tax = rules.PlanetTemplateMap.Values.Max(item => item.TaxRange.MaxValue);
            Planet planet = new(900, "Unrest Integration World", new Coordinate(1, 1), 16,
                template, 1, tax);
            PlanetFaction defaultPlanetFaction = new(rules.DefaultFaction) { IsPublic = true };
            planet.PlanetFactionMap[rules.DefaultFaction.Id] = defaultPlanetFaction;

            for (int index = 0; index < planet.Regions.Length; index++)
            {
                Region region = new(index, planet, 0, $"Region {index}",
                    RegionExtensions.GetCoordinatesFromRegionNumber(index), 0,
                    carryingCapacity: 1_000_000, maximumCarryingCapacity: 1_000_000);
                region.RegionFactionMap[rules.DefaultFaction.Id] = new RegionFaction(defaultPlanetFaction, region)
                {
                    Population = 100_000,
                    Garrison = 10_000,
                    Contentment = 100,
                    IsPublic = true,
                    Organization = 100
                };
                planet.Regions[index] = region;
            }
            planet.SetCapitalRegion(planet.Regions[0].Id);

            Army army = new("Test Army", null, null, null, []);
            PlayerForce playerForce = new(rules.PlayerFaction, army, new Fleet("Test Fleet", null, null));
            Sector sector = new(playerForce, [], [planet], []);
            return new CivilFixture(rules, sector, planet);
        }

        internal RegionFaction Loyalists(int region) =>
            Planet.Regions[region].RegionFactionMap[_rules.DefaultFaction.Id];

        internal RegionFaction Unrest(int region) =>
            Planet.Regions[region].RegionFactionMap.TryGetValue(
                _rules.SectorFactions.Insurrectionists.Id, out RegionFaction unrest)
                    ? unrest
                    : null;

        internal RegionFaction AddUnrest(
            int region,
            long population,
            long garrison,
            long armedCivilians,
            bool isPublic)
        {
            if (!Planet.PlanetFactionMap.ContainsKey(_unrestPlanetFaction.Faction.Id))
            {
                Planet.PlanetFactionMap[_unrestPlanetFaction.Faction.Id] = _unrestPlanetFaction;
            }
            RegionFaction unrest = new(_unrestPlanetFaction, Planet.Regions[region])
            {
                Population = population,
                Garrison = garrison,
                ArmedCivilians = armedCivilians,
                IsPublic = isPublic,
                Organization = 100
            };
            Planet.Regions[region].RegionFactionMap[_unrestPlanetFaction.Faction.Id] = unrest;
            if (isPublic) _unrestPlanetFaction.IsPublic = true;
            return unrest;
        }

        internal void AddTyranids(int region, long population)
        {
            Faction faction = _rules.SectorFactions.Invader;
            PlanetFaction planetFaction = new(faction) { IsPublic = true };
            Planet.PlanetFactionMap[faction.Id] = planetFaction;
            Planet.Regions[region].RegionFactionMap[faction.Id] = new RegionFaction(
                planetFaction, Planet.Regions[region])
            {
                Population = population,
                IsPublic = true,
                Organization = 100
            };
        }

        internal void ProcessCivilUnrest() => new CivilUnrestTurnProcessor(_session).ProcessPlanet(Planet);

        internal void ProcessPlanetTurn() => new PlanetTurnProcessor(_session, []).UpdatePlanet(Planet);
    }
}
