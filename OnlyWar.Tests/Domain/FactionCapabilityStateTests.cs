using System;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Helpers.Turns;
using OnlyWar.Models;
using OnlyWar.Models.FactionBehaviors;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

public sealed class FactionCapabilityStateTests
{
    [Fact]
    public void ConsolidationAndMobilizationUseTheDesignBounds()
    {
        Assert.Equal(0.251, DormantPopulationRules.MobilizationFraction(-3.49), 3);
        Assert.Equal(0.90, DormantPopulationRules.MobilizationFraction(4.0), 3);
        Assert.Equal(0.501, DormantPopulationRules.UpdateConsolidation(0.5, 0.0), 3);
        Assert.Equal(0.0, DormantPopulationRules.UpdateConsolidation(0.0, -10.0), 3);
        Assert.Equal(1.0, DormantPopulationRules.UpdateConsolidation(1.0, 10.0), 3);
    }

    [Fact]
    public void RulesResolveInvasionCapabilityAndValidatedCampaignProfile()
    {
        GameRulesData rules = new();
        Faction invasionFaction = GetInvasionFaction(rules);

        Assert.NotNull(invasionFaction);
        Assert.Equal("Orks", invasionFaction.Name);
        Assert.True(invasionFaction.HasBehavior(FactionBehavior.PopulationIsMilitary));
        Assert.True(invasionFaction.HasBehavior(FactionBehavior.UniversallyHostile));
        Assert.True(invasionFaction.HasBehavior(FactionBehavior.Indelible));
        Assert.Same(rules.FactionBehaviorRules,
            rules.FactionBehaviorRulesProfiles.Values.Single());
        rules.FactionBehaviorRules.Validate();
    }

    [Fact]
    public void IndeliblePresenceSurvivesPopulationCulls()
    {
        GameRulesData rules = new();
        Faction invasionFaction = GetInvasionFaction(rules);
        Assert.NotNull(invasionFaction);
        Planet planet = new(
            1,
            "Ork Test World",
            new Coordinate(1, 1),
            1,
            rules.PlanetTemplateMap.Values.First(),
            1,
            0);
        Region region = new(
            1,
            planet,
            0,
            "Test Region",
            new RegionCoordinate(0, 0),
            0);
        planet.Regions[0] = region;
        PlanetFaction planetFaction = new(invasionFaction) { IsPublic = true };
        RegionFaction presence = new(planetFaction, region)
        {
            Population = 10,
            IsPublic = true
        };
        region.RegionFactionMap[invasionFaction.Id] = presence;

        presence.Population = 0;

        Assert.False(presence.IsPublic);
        Assert.False(RegionControlTurnProcessor.CanRemoveRegionFaction(presence));
    }

    [Fact]
    public void ConfirmedDormantCullingReducesStrengthButLeavesTheIndeliblePresence()
    {
        GameRulesData rules = new();
        Planet planet = new(
            2,
            "Feral Test World",
            new Coordinate(2, 2),
            1,
            rules.PlanetTemplateMap.Values.First(),
            1,
            0);
        Region region = new(
            2,
            planet,
            0,
            "Feral Region",
            new RegionCoordinate(0, 0),
            0);
        planet.Regions[0] = region;

        PlanetFaction observer = new(rules.DefaultFaction);
        planet.PlanetFactionMap[observer.Faction.Id] = observer;
        region.RegionFactionMap[observer.Faction.Id] = new RegionFaction(observer, region)
        {
            Population = 100_000,
            IsPublic = true
        };

        Faction invasionFaction = GetInvasionFaction(rules);
        PlanetFaction invasionPlanetFaction = new(invasionFaction) { IsPublic = false };
        planet.PlanetFactionMap[invasionFaction.Id] = invasionPlanetFaction;
        RegionFaction target = new(invasionPlanetFaction, region)
        {
            Population = 1_000_000,
            IsPublic = false,
            DormantConsolidation = 1.0
        };
        region.RegionFactionMap[invasionFaction.Id] = target;

        FactionIntelBelief belief = observer.SeedTargetBelief(
            region,
            invasionFaction,
            (float)rules.FactionBehaviorRules.DormantInitialBeliefEvidence,
            estimatedPopulation: null,
            estimatedMilitaryStrength: null,
            evidenceWeek: 0);
        DormantPopulationCullingResult result = DormantPopulationCulling.Resolve(
            target,
            belief,
            rules.FactionBehaviorRules,
            effectivePdfBattleValue: 100_000);

        Assert.True(result.WasBeliefEligible);
        Assert.False(result.WasFalsePositive);
        Assert.True(result.PopulationRemoved > 0);
        Assert.True(result.ConsolidationRemoved > 0);

        target.RemoveMilitaryStrength(result.PopulationRemoved);
        target.DormantConsolidation = global::System.Math.Clamp(
            target.DormantConsolidation - result.ConsolidationRemoved,
            0.0,
            1.0);

        Assert.Same(target, region.RegionFactionMap[invasionFaction.Id]);
        Assert.True(target.Population < 1_000_000);
        Assert.True(target.Population > 0);
        Assert.False(target.IsPublic);
        Assert.False(RegionControlTurnProcessor.CanRemoveRegionFaction(target));
    }

    [Fact]
    public void InvasionFormationLeavesTheGhostSourceEcosystemBehind()
    {
        GameRulesData rules = new();
        Date campaignDate = new(39, 500, 1);
        GameDataSingleton.Instance.LoadGameDataFromBlob(rules, campaignDate, null);
        Sector sector = SectorBuilder.GenerateSector(1, rules, campaignDate, "Invasion Source Test");
        PlanetTemplate template = rules.PlanetTemplateEligibility
            .GetEligibleTemplateIds(PlanetTemplateEligibilityKeys.GhostPopulationSource)
            .Select(id => rules.PlanetTemplateMap[id])
            .First();
        GhostPopulationSource source = new(
            sector.GhostPopulationSources.Select(existing => existing.Id).DefaultIfEmpty(0).Max() + 1,
            new Coordinate(0, 0),
            template,
            100_000,
            100_000,
            1.0);
        sector.AddGhostPopulationSource(source);

        int populationBefore = (int)source.Population;
        FactionCapabilityCampaignProcessor processor = new(
            new GameSession(rules, sector, campaignDate, new FixedRNG()));
        processor.ProcessWeeklyState(sector);

        Assert.Contains(sector.StrategicInvasionForces, force => force.IsActive);
        Assert.Contains(source, sector.GhostPopulationSources);
        Assert.True(source.Population < populationBefore);
    }

    [Fact]
    public void PersistentInvasionForceRoundTripsWithItsCommandSquadOutsideLandedSquads()
    {
        GameRulesData rules = new();
        Date campaignDate = new(39, 500, 1);
        GameDataSingleton.Instance.LoadGameDataFromBlob(rules, campaignDate, null);
        Sector sector = SectorBuilder.GenerateSector(1, rules, campaignDate, "Invasion Save Test");
        Faction invasionFaction = GetInvasionFaction(rules);
        Assert.NotNull(invasionFaction);
        Planet planet = sector.Planets.Values.First();
        Region region = planet.Regions.First();
        SquadTemplate hq = invasionFaction.SquadTemplates.Values
            .Single(template => template.Name == "'Eavy Warboss");
        Squad command = SquadFactory.GenerateSquad(hq, new FixedRNG(), name: "Persistent Warboss");
        UnitTemplate unitTemplate = invasionFaction.UnitTemplates.Values
            .FirstOrDefault(template => template.HQSquad == hq)
            ?? invasionFaction.UnitTemplates.Values.First();
        Unit unit = new(900000, "Persistent Warband", unitTemplate, [command]);
        command.ParentUnit = unit;
        invasionFaction.Units.Add(unit);

        PlanetFaction planetFaction = planet.PlanetFactionMap.TryGetValue(
            invasionFaction.Id,
            out PlanetFaction existingPlanetFaction)
            ? existingPlanetFaction
            : new PlanetFaction(invasionFaction) { IsPublic = true };
        planet.PlanetFactionMap[invasionFaction.Id] = planetFaction;
        RegionFaction presence = new(planetFaction, region)
        {
            Population = 25000,
            IsPublic = true,
            StrategicInvasionForceId = 77,
            DormantConsolidation = 1.0
        };
        region.RegionFactionMap[invasionFaction.Id] = presence;
        command.CurrentRegion = region;
        StrategicInvasionForce invasionForce = new(77, invasionFaction, command, region, planet);
        invasionForce.TrackRegion(presence);
        sector.AddStrategicInvasionForce(invasionForce);

        string dbPath = GameStateRoundTripFixture.CreateTempDbPath("invasion_state");
        try
        {
            GameStateRoundTripFixture roundTrip = new(rules, campaignDate);
            roundTrip.RegisterPlayerArmy(sector);
            roundTrip.Save(sector, dbPath, rules.Factions.SelectMany(faction => faction.Units));

            GameStateDataBlob blob = roundTrip.Load(dbPath);
            Assert.Single(blob.StrategicInvasionForces);
            Assert.Equal(77, blob.StrategicInvasionForces[0].Id);
            Assert.Equal(0, blob.StrategicInvasionForces[0].TransitBattleValue);

            GameRulesData loadedRules = new();
            Sector loadedSector = SavedGameLoader.BuildSectorFromBlob(blob, loadedRules);
            StrategicInvasionForce loaded = Assert.Single(loadedSector.StrategicInvasionForces);
            Assert.Equal(77, loaded.Id);
            Assert.Equal(900000, loaded.CommandSquad.ParentUnit.Id);
            Assert.Equal(loadedSector.Planets.Values.First().Regions.First(), loaded.CurrentRegion);
            Assert.DoesNotContain(loaded.CommandSquad,
                loaded.CurrentRegion.RegionFactionMap[GetInvasionFaction(loadedRules).Id].LandedSquads);
        }
        finally
        {
            GameStateRoundTripFixture.CleanupDb(dbPath);
        }
    }

    private static Faction GetInvasionFaction(GameRulesData rules) =>
        FactionCapabilities.WithCapability(
            rules.Factions, FactionBehavior.GeneratesInvasions).Single();
}
