using System;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Helpers.Turns;
using OnlyWar.Models;
using OnlyWar.Models.Orks;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

public sealed class OrkInfestationStateTests
{
    [Fact]
    public void ConsolidationAndMobilizationUseTheDesignBounds()
    {
        Assert.Equal(0.251, OrkInfestationRules.MobilizationFraction(-3.49), 3);
        Assert.Equal(0.90, OrkInfestationRules.MobilizationFraction(4.0), 3);
        Assert.Equal(0.501, OrkInfestationRules.UpdateConsolidation(0.5, 0.0), 3);
        Assert.Equal(0.0, OrkInfestationRules.UpdateConsolidation(0.0, -10.0), 3);
        Assert.Equal(1.0, OrkInfestationRules.UpdateConsolidation(1.0, 10.0), 3);
    }

    [Fact]
    public void RulesResolveTheOrkCompositionAndValidatedCampaignProfile()
    {
        GameRulesData rules = new();

        Assert.NotNull(rules.OrkFaction);
        Assert.Equal("Orks", rules.OrkFaction.Name);
        Assert.True(rules.OrkFaction.HasBehavior(FactionBehavior.PopulationIsMilitary));
        Assert.True(rules.OrkFaction.HasBehavior(FactionBehavior.UniversallyHostile));
        Assert.True(rules.OrkFaction.HasBehavior(FactionBehavior.Indelible));
        Assert.Same(rules.OrkCampaignRules, rules.OrkInfestationRulesProfile);
        rules.OrkCampaignRules.Validate();
    }

    [Fact]
    public void IndelibleOrkPresenceSurvivesPopulationCulls()
    {
        GameRulesData rules = new();
        Faction orks = rules.OrkFaction;
        Assert.NotNull(orks);
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
        PlanetFaction planetFaction = new(orks) { IsPublic = true };
        RegionFaction presence = new(planetFaction, region)
        {
            Population = 10,
            IsPublic = true
        };
        region.RegionFactionMap[orks.Id] = presence;

        presence.Population = 0;

        Assert.False(presence.IsPublic);
        Assert.False(RegionControlTurnProcessor.CanRemoveRegionFaction(presence));
    }

    [Fact]
    public void ConfirmedFeralCullingReducesStrengthButLeavesTheIndeliblePresence()
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

        PlanetFaction orkPlanetFaction = new(rules.OrkFaction) { IsPublic = false };
        planet.PlanetFactionMap[rules.OrkFaction.Id] = orkPlanetFaction;
        RegionFaction target = new(orkPlanetFaction, region)
        {
            Population = 1_000_000,
            IsPublic = false,
            OrkConsolidation = 1.0
        };
        region.RegionFactionMap[rules.OrkFaction.Id] = target;

        FactionIntelBelief belief = observer.SeedTargetBelief(
            region,
            rules.OrkFaction,
            (float)rules.OrkCampaignRules.FeralInitialBeliefEvidence,
            estimatedPopulation: null,
            estimatedMilitaryStrength: null,
            evidenceWeek: 0);
        OrkFeralCullingResult result = OrkFeralCullingRules.Resolve(
            target,
            belief,
            rules.OrkCampaignRules,
            effectivePdfBattleValue: 100_000);

        Assert.True(result.WasBeliefEligible);
        Assert.False(result.WasFalsePositive);
        Assert.True(result.PopulationRemoved > 0);
        Assert.True(result.ConsolidationRemoved > 0);

        target.RemoveMilitaryStrength(result.PopulationRemoved);
        target.OrkConsolidation = global::System.Math.Clamp(
            target.OrkConsolidation - result.ConsolidationRemoved,
            0.0,
            1.0);

        Assert.Same(target, region.RegionFactionMap[rules.OrkFaction.Id]);
        Assert.True(target.Population < 1_000_000);
        Assert.True(target.Population > 0);
        Assert.False(target.IsPublic);
        Assert.False(RegionControlTurnProcessor.CanRemoveRegionFaction(target));
    }

    [Fact]
    public void WaaaghFormationLeavesTheGhostSourceEcosystemBehind()
    {
        GameRulesData rules = new();
        Date campaignDate = new(39, 500, 1);
        GameDataSingleton.Instance.LoadGameDataFromBlob(rules, campaignDate, null);
        Sector sector = SectorBuilder.GenerateSector(1, rules, campaignDate, "Ork Source Test");
        PlanetTemplate template = rules.PlanetTemplateEligibility
            .GetEligibleTemplateIds(PlanetTemplateEligibilityKeys.OrkGhostSource)
            .Select(id => rules.PlanetTemplateMap[id])
            .First();
        OrkGhostSource source = new(
            sector.OrkGhostSources.Select(existing => existing.Id).DefaultIfEmpty(0).Max() + 1,
            new Coordinate(0, 0),
            template,
            100_000,
            100_000,
            1.0);
        sector.AddOrkGhostSource(source);

        int populationBefore = (int)source.Population;
        OrkCampaignProcessor processor = new(
            new GameSession(rules, sector, campaignDate, new FixedRNG()));
        processor.ProcessWeeklyState(sector);

        Assert.Contains(sector.OrkWaaaghs, waaagh => waaagh.IsActive);
        Assert.Contains(source, sector.OrkGhostSources);
        Assert.True(source.Population < populationBefore);
    }

    [Fact]
    public void PersistentWaaaghRoundTripsWithItsCommandSquadOutsideLandedSquads()
    {
        GameRulesData rules = new();
        Date campaignDate = new(39, 500, 1);
        GameDataSingleton.Instance.LoadGameDataFromBlob(rules, campaignDate, null);
        Sector sector = SectorBuilder.GenerateSector(1, rules, campaignDate, "Ork Save Test");
        Faction orks = rules.OrkFaction;
        Assert.NotNull(orks);
        Planet planet = sector.Planets.Values.First();
        Region region = planet.Regions.First();
        SquadTemplate hq = orks.SquadTemplates.Values
            .Single(template => template.Name == "'Eavy Warboss");
        Squad command = SquadFactory.GenerateSquad(hq, new FixedRNG(), name: "Persistent Warboss");
        UnitTemplate unitTemplate = orks.UnitTemplates.Values
            .FirstOrDefault(template => template.HQSquad == hq)
            ?? orks.UnitTemplates.Values.First();
        Unit unit = new(900000, "Persistent Warband", unitTemplate, [command]);
        command.ParentUnit = unit;
        orks.Units.Add(unit);

        PlanetFaction planetFaction = planet.PlanetFactionMap.TryGetValue(
            orks.Id,
            out PlanetFaction existingPlanetFaction)
            ? existingPlanetFaction
            : new PlanetFaction(orks) { IsPublic = true };
        planet.PlanetFactionMap[orks.Id] = planetFaction;
        RegionFaction presence = new(planetFaction, region)
        {
            Population = 25000,
            IsPublic = true,
            OrkWaaaghId = 77,
            OrkConsolidation = 1.0
        };
        region.RegionFactionMap[orks.Id] = presence;
        command.CurrentRegion = region;
        OrkWaaagh waaagh = new(77, orks, command, region, planet);
        waaagh.TrackRegion(presence);
        sector.AddOrkWaaagh(waaagh);

        string dbPath = GameStateRoundTripFixture.CreateTempDbPath("ork_state");
        try
        {
            GameStateRoundTripFixture roundTrip = new(rules, campaignDate);
            roundTrip.RegisterPlayerArmy(sector);
            roundTrip.Save(sector, dbPath, rules.Factions.SelectMany(faction => faction.Units));

            GameStateDataBlob blob = roundTrip.Load(dbPath);
            Assert.Single(blob.OrkWaaaghs);
            Assert.Equal(77, blob.OrkWaaaghs[0].Id);
            Assert.Equal(0, blob.OrkWaaaghs[0].TransitBattleValue);

            GameRulesData loadedRules = new();
            Sector loadedSector = SavedGameLoader.BuildSectorFromBlob(blob, loadedRules);
            OrkWaaagh loaded = Assert.Single(loadedSector.OrkWaaaghs);
            Assert.Equal(77, loaded.Id);
            Assert.Equal(900000, loaded.CommandSquad.ParentUnit.Id);
            Assert.Equal(loadedSector.Planets.Values.First().Regions.First(), loaded.CurrentRegion);
            Assert.DoesNotContain(loaded.CommandSquad,
                loaded.CurrentRegion.RegionFactionMap[loadedRules.OrkFaction.Id].LandedSquads);
        }
        finally
        {
            GameStateRoundTripFixture.CleanupDb(dbPath);
        }
    }
}
