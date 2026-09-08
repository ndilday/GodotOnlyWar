using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;

namespace OnlyWar.Tests.Fixtures;

/// <summary>
/// Builds a compact single-planet sector with explicit rules and date inputs for exercising
/// <see cref="TurnController"/> end-of-turn logic. Every region is
/// controlled by a public default faction (so <c>GetControllingFaction</c> resolves),
/// and helpers add hidden cults, rival controllers, and governors on top.
/// </summary>
internal sealed class SectorSimulationFixture
{
    private const int RegionCount = 16;
    private int _nextFactionId = 100;

    public Faction Default { get; private set; }
    public PlanetFaction DefaultPlanetFaction { get; private set; }
    public Planet Planet { get; private set; }
    public Sector Sector { get; private set; }

    /// <summary>
    /// The rules this fixture loaded, or null when it was created detached. Tests that drive a
    /// policy needing rules pass this explicitly rather than reading another session.
    /// </summary>
    public GameRulesData Rules { get; private set; }

    /// <summary>The campaign date this fixture's session runs at.</summary>
    public Date CurrentDate { get; } = new Date(1, 1, 1);

    /// <summary>
    /// Explicit campaign inputs for order-lifecycle commands, so order tests name the sector they
    /// mutate rather than depending on whichever campaign happens to be active (SB-05a).
    /// </summary>
    public OnlyWar.Operations.Contracts.OrderCommandContext OrderCommands =>
        new(Sector, CurrentDate,
            new OnlyWar.Helpers.Readiness.MedicalReadinessDecisions(),
            Personnel: _personnel);

    /// <summary>Every player soldier the chapter could lend to an operation.</summary>
    public System.Collections.Generic.IEnumerable<OnlyWar.Models.Soldiers.PlayerSoldier> ChapterRoster =>
        Sector?.PlayerForce?.Army?.PlayerSoldierMap?.Values
        ?? System.Linq.Enumerable.Empty<OnlyWar.Models.Soldiers.PlayerSoldier>();

    private readonly RegionFaction[] _defaultRegionFactions = new RegionFaction[RegionCount];
    private readonly OnlyWar.Operations.Contracts.IOperationsPersonnelSurface _personnel =
        TestPersonnelComposition.CreatePersonnel();

    public RegionFaction DefaultRegionFaction(int region) => _defaultRegionFactions[region];

    public static SectorSimulationFixture Create(long defaultRegionPopulation = 20000)
    {
        return CreateCore(defaultRegionPopulation, loadRules: true);
    }

    /// <summary>
    /// Builds the same compact model without changing the process current directory or loading it
    /// with no rules loaded. Use this for pure model/service tests that do not need a campaign
    /// session.
    /// </summary>
    public static SectorSimulationFixture CreateDetached(long defaultRegionPopulation = 20000)
    {
        return CreateCore(defaultRegionPopulation, loadRules: false);
    }

    private static SectorSimulationFixture CreateCore(
        long defaultRegionPopulation,
        bool loadRules)
    {
        if (loadRules)
        {
            Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
        }

        SectorSimulationFixture fixture = new()
        {
            Default = BuildFaction(1, "Imperium", isPlayer: false, isDefault: true, GrowthType.None)
        };
        fixture.Planet = CreatePlanet();
        fixture.DefaultPlanetFaction = new PlanetFaction(fixture.Default) { IsPublic = true };
        fixture.Planet.PlanetFactionMap[fixture.Default.Id] = fixture.DefaultPlanetFaction;

        for (int i = 0; i < RegionCount; i++)
        {
            RegionFaction rf = new(fixture.DefaultPlanetFaction, fixture.Planet.Regions[i])
            {
                Population = defaultRegionPopulation,
                IsPublic = true,
                Garrison = 0,
                Organization = 100
            };
            fixture.Planet.Regions[i].RegionFactionMap[fixture.Default.Id] = rf;
            fixture._defaultRegionFactions[i] = rf;
        }

        Faction player = BuildFaction(2, "Test Chapter", isPlayer: true, isDefault: false, GrowthType.None);
        Army army = new("Test Army", null, null, null, []);
        PlayerForce playerForce = new(player, army, new Fleet("Test Fleet", null, null));
        fixture.Sector = new Sector(playerForce, [], [fixture.Planet], []);
        if (loadRules)
        {
            GameRulesData rules = OnlyWar.Helpers.Database.GameRules.GameRulesLoader.Load(OnlyWar.Tests.Fixtures.RulesDatabaseFixture.DatabasePath);
            // These are single-planet simulations with one governor, so tests force or suppress a
            // request through the governor's traits alone. Pin out the sector-wide throttle (which
            // production sets low enough that a lone governor would almost never petition) so those
            // trait-driven expectations stay deterministic.
            rules.SupplyEconomyRules.RequestGenerationRate = 1m;
            fixture.Rules = rules;
        }

        return fixture;
    }

    // adds a non-public faction alongside the default controller (preserves the
    // one-public-faction-per-region invariant GetControllingFaction relies on)
    public RegionFaction AddHiddenFaction(int region, GrowthType growthType, long population)
    {
        Faction faction = BuildFaction(_nextFactionId++, "Hidden Cult", isPlayer: false, isDefault: false, growthType);
        PlanetFaction planetFaction = new(faction) { IsPublic = false };
        Planet.PlanetFactionMap[faction.Id] = planetFaction;
        RegionFaction rf = new(planetFaction, Planet.Regions[region])
        {
            Population = population,
            IsPublic = false,
            Garrison = 0,
            Organization = 100
        };
        Planet.Regions[region].RegionFactionMap[faction.Id] = rf;
        return rf;
    }

    // replaces the default controller in a region with a public hostile faction
    public RegionFaction AddControllingFaction(int region, string name, long population)
    {
        Faction faction = BuildFaction(_nextFactionId++, name, isPlayer: false, isDefault: false, GrowthType.None);
        PlanetFaction planetFaction = new(faction) { IsPublic = true };
        Planet.PlanetFactionMap[faction.Id] = planetFaction;

        Planet.Regions[region].RegionFactionMap.Remove(Default.Id);
        RegionFaction rf = new(planetFaction, Planet.Regions[region])
        {
            Population = population,
            IsPublic = true,
            Garrison = 0,
            Organization = 100
        };
        Planet.Regions[region].RegionFactionMap[faction.Id] = rf;
        return rf;
    }

    // adds a public biomass-consuming faction (Tyranids) to a region alongside its controller
    public RegionFaction AddConsumptionFaction(int region, long population, int organization)
    {
        Faction faction = BuildFaction(_nextFactionId++, "Tyranids", isPlayer: false, isDefault: false, GrowthType.Consumption);
        PlanetFaction planetFaction = new(faction) { IsPublic = true };
        Planet.PlanetFactionMap[faction.Id] = planetFaction;
        RegionFaction rf = new(planetFaction, Planet.Regions[region])
        {
            Population = population,
            IsPublic = true,
            Garrison = 0,
            Organization = organization
        };
        Planet.Regions[region].RegionFactionMap[faction.Id] = rf;
        return rf;
    }

    // adds a public Genestealer-Cult-style faction (Conversion growth) to a region alongside its
    // controller, used to exercise the Cult idle-force maneuvers (relocation / sacrificial predation)
    public RegionFaction AddPublicCult(int region, long population, int organization)
    {
        Faction faction = BuildFaction(_nextFactionId++, "Genestealer Cult", isPlayer: false, isDefault: false, GrowthType.Conversion);
        PlanetFaction planetFaction = new(faction) { IsPublic = true };
        Planet.PlanetFactionMap[faction.Id] = planetFaction;
        RegionFaction rf = new(planetFaction, Planet.Regions[region])
        {
            Population = population,
            IsPublic = true,
            Garrison = 0,
            Organization = organization
        };
        Planet.Regions[region].RegionFactionMap[faction.Id] = rf;
        return rf;
    }

    public Character InstallGovernor(float investigation, float neediness, float opinion)
    {
        Character governor = new()
        {
            Id = 1,
            Age = 40,
            Investigation = investigation,
            Paranoia = 0f,
            Neediness = neediness,
            Patience = 1f,
            Appreciation = 0.1f,
            OpinionOfPlayerForce = opinion
        };
        DefaultPlanetFaction.Leader = governor;
        Sector.Characters.Add(governor);
        return governor;
    }

    public void ProcessTurn()
    {
        TestCampaignComposition composition =
            TestPersonnelComposition.CreateCampaign(new StaticRNG());
        new TurnController(
            new OnlyWar.Helpers.Simulation.GameSession(
                Rules ?? throw new InvalidOperationException("This fixture has no rules."),
                Sector,
                CurrentDate,
                new StaticRNG()),
            composition.Services.Readiness.Decisions,
            composition.Services.Operations.Personnel,
            composition.Services.Operations.Commitments,
            composition.Services.Battle)
            .ProcessTurn(Sector);
    }

    private static Planet CreatePlanet()
    {
        PlanetTemplate template = new(
            1,
            "Sector Test World",
            1,
            new LogNormalValueTemplate { Floor = 1000, Scale = 0 },
            new LogNormalValueTemplate { Floor = 2000, Scale = 0 },
            new NormalizedValueTemplate { BaseValue = 1, StandardDeviation = 0 },
            new LinearValueTemplate { MinValue = 0, MaxValue = 0 });
        Planet planet = new(1, "Sector Test World", new Coordinate(1, 1), 1, template, 1, 0);
        for (int i = 0; i < planet.Regions.Length; i++)
        {
            planet.Regions[i] = new Region(
                i, planet, 0, $"Region {i}",
                RegionExtensions.GetCoordinatesFromRegionNumber(i), 0);
        }
        return planet;
    }

    // Bare faction with no planet or region attached, for tests that only need the faction flags
    // (FactionRelationshipServiceTests).
    public static Faction BuildTestFaction(int id, string name, bool isPlayer, bool isDefault) =>
        BuildFaction(id, name, isPlayer, isDefault, GrowthType.None);

    private static Faction BuildFaction(int id, string name, bool isPlayer, bool isDefault, GrowthType growthType)
    {
        FactionBehavior behavior = isPlayer || isDefault
            ? FactionBehavior.None
            : FactionBehavior.PopulationIsMilitary;
        if (growthType is GrowthType.Consumption or GrowthType.Unrest)
        {
            behavior |= FactionBehavior.InvadesOnVictory;
        }
        if (growthType is GrowthType.Conversion or GrowthType.Unrest)
        {
            behavior |= FactionBehavior.DefendsHostWhileHidden;
        }
        if (growthType == GrowthType.Unrest)
        {
            behavior |= FactionBehavior.OffersExternalEnemyTruce;
        }

        return new Faction(
            id, name, Color.Red, isPlayer, isDefault, behavior, growthType,
            new Dictionary<int, Species> { [TestModelFactory.HumanSpecies.Id] = TestModelFactory.HumanSpecies },
            new Dictionary<int, SoldierTemplate>(),
            new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, UnitTemplate>(),
            new Dictionary<int, BoatTemplate>(),
            new Dictionary<int, ShipTemplate>(),
            new Dictionary<int, FleetTemplate>());
    }
}
