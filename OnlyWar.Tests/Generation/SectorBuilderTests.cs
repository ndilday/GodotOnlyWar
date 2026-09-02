using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using OnlyWar.Builders;
using OnlyWar.Helpers.Extensions;
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
        GameDataSingleton.Instance.LoadGameDataFromBlob(rules, currentDate, null);

        Sector sector = SectorBuilder.GenerateSector(
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

        SectorBuilder.GenerateWarpNetwork(narrowSector, narrowRules);
        SectorBuilder.GenerateWarpNetwork(broadSector, broadRules);

        Assert.Equal(2, narrowSector.Subsectors.Count);
        Assert.Single(broadSector.Subsectors);
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

            return new GameRulesData(databasePath);
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
