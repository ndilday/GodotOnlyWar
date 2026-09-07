using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using OnlyWar.Application;
using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Generation;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class SectorBuilderTests
{
    [Fact]
    public void GenerateSector_UsesSectorDimensionsAndSpawnProbabilityFromRulesData()
    {
        GameRulesData rules = LoadRulesWithProfile(
            "sector-dimensions-and-density", 20, 20, 1.0, 20);
        Date currentDate = new(39, 500, 1);

        Sector sector = TestGeneration.GenerateSector(
            1, rules, currentDate, "Profile Driven Chapter");

        Assert.Equal(20 * 20, sector.Planets.Count);
        Assert.All(sector.Planets.Values, planet =>
        {
            Assert.InRange(planet.Position.X, (ushort)0, (ushort)19);
            Assert.InRange(planet.Position.Y, (ushort)0, (ushort)19);
        });
    }

    [Fact]
    public void GenerateWarpNetwork_UsesMaxSubsectorDiameterFromRulesData()
    {
        GameRulesData narrowRules = LoadRulesWithProfile(
            "narrow-subsectors", 20, 20, 0.0, 1);
        GameRulesData broadRules = LoadRulesWithProfile(
            "broad-subsectors", 20, 20, 0.0, 3);

        Sector narrowSector = CreateSector(new Coordinate(0, 0), new Coordinate(2, 0));
        Sector broadSector = CreateSector(new Coordinate(0, 0), new Coordinate(2, 0));

        OnlyWar.Runtime.WorldGeometry.SectorTopologyBuilder.Rebuild(narrowSector, narrowRules);
        OnlyWar.Runtime.WorldGeometry.SectorTopologyBuilder.Rebuild(broadSector, broadRules);

        Assert.Equal(2, narrowSector.Subsectors.Count);
        Assert.Single(broadSector.Subsectors);
    }

    // SB-09: generation and the opening-scenario warm-up construct a candidate campaign. Nothing is
    // published while they run, so a new game can be generated with no campaign installed at all.
    [Fact]
    public void GenerateSector_BuildsCandidateWithoutPublishingIt()
    {
        GameRulesData rules = LoadRulesWithProfile(
            "unpublished-candidate", 10, 10, 1.0, 20);

        Sector candidate = TestGeneration.GenerateSector(
            3, rules, new Date(39, 500, 1), "Candidate Chapter");

        Assert.NotNull(candidate.Scenario);
        Assert.NotEmpty(candidate.Planets);
        Assert.NotNull(candidate.PlayerForce);
    }

    // SB-09: a failed new game leaves the campaign the player is already in exactly as it was.
    [Fact]
    public void StartNewCampaign_WhenGenerationFails_LeavesTheActiveCampaignInstalled()
    {
        GameRulesData rules = LoadRulesWithProfile("failed-candidate", 10, 10, 1.0, 20);
        Date activeDate = new(39, 400, 1);
        Sector active = CreateSector(new Coordinate(0, 0), new Coordinate(2, 0));
        CampaignApplication application = new(new SeededRNG(41));
        GameSession activeSession = new(rules, active, activeDate, new SeededRNG(42));
        application.Install(activeSession);

        Assert.Throws<InvalidOperationException>(() =>
            application.StartNewCampaign(
                rules, new Date(39, 500, 1), "Doomed Chapter", 4,
                ScenarioFactionSelection.ForFaction(-999)));

        Assert.Same(activeSession, application.ActiveSession);
        Assert.Same(active, application.ActiveSession.Sector);
        Assert.Same(activeDate, application.ActiveSession.CurrentDate);
        Assert.Same(rules, application.ActiveSession.Rules);
    }

    private static Sector CreateSector(params Coordinate[] positions)
    {
        List<Planet> planets = positions
            .Select((position, index) => CreatePlanet(index + 1, position))
            .ToList();
        return new Sector(null, [], planets, []);
    }

    private static Planet CreatePlanet(int id, Coordinate position)
    {
        Planet planet = new(id, $"Test Planet {id}", position, 1, null, 1, 0);
        for (int regionId = 0; regionId < planet.Regions.Length; regionId++)
        {
            planet.Regions[regionId] = new Region(
                regionId,
                planet,
                0,
                $"Region {regionId}",
                RegionExtensions.GetCoordinatesFromRegionNumber(regionId),
                0);
        }

        return planet;
    }

    private static GameRulesData LoadRulesWithProfile(
        string suffix,
        int width,
        int height,
        double spawnProbability,
        int maxSubsectorDiameter)
    {
        string databasePath = Path.Combine(
            Path.GetTempPath(), $"onlywar-sector-builder-{suffix}-{Guid.NewGuid():N}.s3db");
        File.Copy(RulesDatabaseFixture.DatabasePath, databasePath);

        try
        {
            using (SqliteConnection connection = new(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString()))
            {
                connection.Open();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "UPDATE SectorGenerationProfile SET "
                    + "SectorWidth = $width, SectorHeight = $height, "
                    + "PlanetSpawnProbability = $spawnProbability, "
                    + "MaxSubsectorDiameter = $maxSubsectorDiameter WHERE IsDefault = 1;";
                command.Parameters.AddWithValue("$width", width);
                command.Parameters.AddWithValue("$height", height);
                command.Parameters.AddWithValue("$spawnProbability", spawnProbability);
                command.Parameters.AddWithValue("$maxSubsectorDiameter", maxSubsectorDiameter);
                command.ExecuteNonQuery();
            }

            return OnlyWar.Helpers.Database.GameRules.GameRulesLoader.Load(databasePath);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }
}
