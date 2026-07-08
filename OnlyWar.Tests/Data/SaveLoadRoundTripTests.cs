using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Soldiers.Ratings;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Data;

// End-to-end save/load coverage (TDD 9.2.1 #1). Generates a real new-game sector,
// writes it through GameStateDataAccess.SaveData, reads it back through GetData, and
// asserts the high-level state survives the round trip. This is the regression guard
// for schema drift: any future change to the save schema that is not propagated to
// both SaveData and GetData will fail here.
public class SaveLoadRoundTripTests
{
    private readonly GameRulesData _data;
    private readonly Date _date = new(39, 500, 1);

    public SaveLoadRoundTripTests()
    {
        Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
        _data = new GameRulesData();
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, null);
    }

    [Fact]
    public void GeneratedChapter_SeedsFoundingRequisition()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Requisition Founding Chapter");
        // The founding seed (PRD 4.23 / Supply & Requisition Phase 1) is a generous,
        // non-zero starting pool.
        Assert.True(sector.PlayerForce.Army.Requisition > 0);
    }

    [Fact]
    public void SaveThenLoad_PreservesGeneseedStockpileAndPurity()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Geneseed Round Trip Chapter");
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, sector);
        Unit armyRoot = sector.PlayerForce.Army.OrderOfBattle;
        if (!_data.PlayerFaction.Units.Contains(armyRoot))
        {
            _data.PlayerFaction.Units.Add(armyRoot);
        }
        // Distinctive values so the assertion proves the saved figures round-trip rather
        // than coincidentally matching a default (PRD 4.8).
        sector.PlayerForce.GeneseedStockpile = 13;
        sector.PlayerForce.GeneseedPurity = 0.83f;

        string dbPath = Path.Combine(
            Path.GetTempPath(), $"onlywar_roundtrip_geneseed_{Guid.NewGuid():N}.s3db");
        try
        {
            Save(sector, dbPath, _data.Factions.SelectMany(f => f.Units).ToList());
            GameStateDataBlob loaded = Load(dbPath);

            Assert.Equal(13, loaded.GeneseedStockpile);
            Assert.Equal(0.83f, loaded.GeneseedPurity, 3);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup of a temp file; ignore if still locked.
            }
        }
    }

    [Fact]
    public void SaveThenLoad_PreservesScenarioAndGrowthMultiplier()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Scenario Round Trip Chapter");
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, sector);
        Unit armyRoot = sector.PlayerForce.Army.OrderOfBattle;
        if (!_data.PlayerFaction.Units.Contains(armyRoot))
        {
            _data.PlayerFaction.Units.Add(armyRoot);
        }

        // The generated sector carries a stamped Promised World scenario (PRD 5.3 /
        // Design/OpeningScenario.md §3). Mutate the settable fields to distinctive values so the
        // assertion proves they round-trip rather than coincidentally matching the defaults.
        CampaignScenario original = sector.Scenario;
        Assert.NotNull(original);
        original.State = ObjectiveState.Won;
        original.BriefingAcknowledged = true;

        Faction tyranids = _data.Factions.Single(f => f.Name == "Tyranids");
        Planet promised = sector.GetPlanet(original.PromisedPlanetId);
        // GrowthMultiplier is a general primitive the scenario no longer sets (Tyranids grow by
        // consumption — PRD §4.24), so stamp a distinctive value by hand to prove the *column*
        // round-trips rather than coincidentally matching the 1.0 default.
        const float testMultiplier = 0.4f;
        List<RegionFaction> promisedTyranidFactions = promised.Regions
            .SelectMany(r => r.RegionFactionMap.Values)
            .Where(rf => rf.PlanetFaction.Faction.Id == tyranids.Id)
            .ToList();
        Assert.NotEmpty(promisedTyranidFactions);
        Assert.All(promisedTyranidFactions, rf => rf.GrowthMultiplier = testMultiplier);

        string dbPath = Path.Combine(
            Path.GetTempPath(), $"onlywar_roundtrip_scenario_{Guid.NewGuid():N}.s3db");
        try
        {
            Save(sector, dbPath, _data.Factions.SelectMany(f => f.Units).ToList());
            GameStateDataBlob loaded = Load(dbPath);

            Assert.NotNull(loaded.Scenario);
            Assert.Equal(ScenarioType.PromisedWorld, loaded.Scenario.Type);
            Assert.Equal(original.PromisedPlanetId, loaded.Scenario.PromisedPlanetId);
            Assert.Equal(ObjectiveState.Won, loaded.Scenario.State);
            Assert.True(loaded.Scenario.BriefingAcknowledged);
            Assert.Equal(original.BriefingText, loaded.Scenario.BriefingText);
            Assert.Equal(original.OriginalAuthorityCharacterId,
                         loaded.Scenario.OriginalAuthorityCharacterId);

            // The hand-set GrowthMultiplier survives on the promised world's Tyranid regions.
            Planet loadedPromised = loaded.Planets.Single(p => p.Id == original.PromisedPlanetId);
            List<RegionFaction> loadedTyranidFactions = loadedPromised.Regions
                .SelectMany(r => r.RegionFactionMap.Values)
                .Where(rf => rf.PlanetFaction.Faction.Id == tyranids.Id)
                .ToList();
            Assert.NotEmpty(loadedTyranidFactions);
            Assert.All(loadedTyranidFactions,
                rf => Assert.Equal(testMultiplier, rf.GrowthMultiplier));

            // Nothing else was altered: every other region faction keeps the default 1.0.
            IEnumerable<RegionFaction> others = loaded.Planets
                .SelectMany(p => p.Regions)
                .SelectMany(r => r.RegionFactionMap.Values)
                .Where(rf => rf.PlanetFaction.Faction.Id != tyranids.Id
                             || rf.Region.Planet.Id != original.PromisedPlanetId);
            Assert.All(others, rf => Assert.Equal(1.0f, rf.GrowthMultiplier));
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public void Load_LegacySave_HasNullScenarioAndDefaultGrowthMultiplier()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Legacy Save Chapter");
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, sector);
        Unit armyRoot = sector.PlayerForce.Army.OrderOfBattle;
        if (!_data.PlayerFaction.Units.Contains(armyRoot))
        {
            _data.PlayerFaction.Units.Add(armyRoot);
        }

        string dbPath = Path.Combine(
            Path.GetTempPath(), $"onlywar_roundtrip_legacy_{Guid.NewGuid():N}.s3db");
        try
        {
            Save(sector, dbPath, _data.Factions.SelectMany(f => f.Units).ToList());
            // Rewrite the save into a pre-scenario ("legacy") shape: a GlobalData row without
            // the Scenario* columns, and a RegionFaction table without GrowthMultiplier. The
            // column-count guards in GlobalDataAccess / PlanetDataAccess must then yield a null
            // scenario and a default 1.0 multiplier (Design/OpeningScenario.md §7).
            DowngradeToLegacySchema(dbPath);

            GameStateDataBlob loaded = Load(dbPath);

            Assert.Null(loaded.Scenario);
            IEnumerable<RegionFaction> allRegionFactions = loaded.Planets
                .SelectMany(p => p.Regions)
                .SelectMany(r => r.RegionFactionMap.Values);
            Assert.NotEmpty(allRegionFactions);
            Assert.All(allRegionFactions, rf => Assert.Equal(1.0f, rf.GrowthMultiplier));
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    // Strips the columns added by the Opening Scenario work from an existing save, reproducing a
    // database written before those columns existed. GlobalData is recreated with its original
    // 7-column shape; RegionFaction's GrowthMultiplier column is dropped in place.
    private static void DowngradeToLegacySchema(string dbPath)
    {
        var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = dbPath
        };
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(builder.ToString());
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
                CREATE TABLE GlobalData_legacy (Millenium INTEGER NOT NULL, Year INTEGER NOT NULL, Week INTEGER NOT NULL, SaveVersion INTEGER NOT NULL, Requisition INTEGER NOT NULL DEFAULT 0, GeneseedStockpile INTEGER NOT NULL DEFAULT 0, GeneseedPurity REAL NOT NULL DEFAULT 1.0);
                INSERT INTO GlobalData_legacy (Millenium, Year, Week, SaveVersion, Requisition, GeneseedStockpile, GeneseedPurity)
                    SELECT Millenium, Year, Week, SaveVersion, Requisition, GeneseedStockpile, GeneseedPurity FROM GlobalData;
                DROP TABLE GlobalData;
                ALTER TABLE GlobalData_legacy RENAME TO GlobalData;
                ALTER TABLE RegionFaction DROP COLUMN GrowthMultiplier;";
            command.ExecuteNonQuery();
        }
        connection.Close();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    [Fact]
    public void SaveThenLoad_PreservesRequisition()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Requisition Round Trip Chapter");
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, sector);
        Unit armyRoot = sector.PlayerForce.Army.OrderOfBattle;
        if (!_data.PlayerFaction.Units.Contains(armyRoot))
        {
            _data.PlayerFaction.Units.Add(armyRoot);
        }
        // Set a distinctive value so the assertion proves the saved figure round-trips
        // rather than coincidentally matching a default.
        sector.PlayerForce.Army.Requisition = 777;

        string dbPath = Path.Combine(
            Path.GetTempPath(), $"onlywar_roundtrip_req_{Guid.NewGuid():N}.s3db");
        try
        {
            Save(sector, dbPath, _data.Factions.SelectMany(f => f.Units).ToList());
            GameStateDataBlob loaded = Load(dbPath);

            Assert.Equal(777, loaded.Requisition);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup of a temp file; ignore if still locked.
            }
        }
    }

    [Fact]
    public void SaveThenLoad_PreservesMedicalProcedures()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Procedure Round Trip Chapter");
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, sector);
        Unit armyRoot = sector.PlayerForce.Army.OrderOfBattle;
        if (!_data.PlayerFaction.Units.Contains(armyRoot))
        {
            _data.PlayerFaction.Units.Add(armyRoot);
        }
        PlayerSoldier subject = armyRoot.GetAllMembers().OfType<PlayerSoldier>().First();
        MedicalProcedure procedure = new(subject.Id, 4, MedicalProcedureType.Cybernetic, 5, 40);
        sector.PlayerForce.Army.MedicalProcedures.Add(procedure);

        string dbPath = Path.Combine(
            Path.GetTempPath(), $"onlywar_roundtrip_proc_{Guid.NewGuid():N}.s3db");
        try
        {
            Save(sector, dbPath, _data.Factions.SelectMany(f => f.Units).ToList());
            GameStateDataBlob loaded = Load(dbPath);

            MedicalProcedure loadedProcedure = Assert.Single(loaded.MedicalProcedures);
            Assert.Equal(subject.Id, loadedProcedure.SoldierId);
            Assert.Equal(4, loadedProcedure.HitLocationTemplateId);
            Assert.Equal(MedicalProcedureType.Cybernetic, loadedProcedure.ProcedureType);
            Assert.Equal(5, loadedProcedure.WeeksRemaining);
            Assert.Equal(40, loadedProcedure.RequisitionCost);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup of a temp file; ignore if still locked.
            }
        }
    }

    [Fact]
    public void SaveThenLoad_GeneratedSector_PreservesHighLevelState()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Round Trip Chapter");
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, sector);
        // The new-game setup flow registers the generated army's root unit on the player
        // faction; the save path reads units from faction.Units, so mirror that here.
        Unit armyRoot = sector.PlayerForce.Army.OrderOfBattle;
        if (!_data.PlayerFaction.Units.Contains(armyRoot))
        {
            _data.PlayerFaction.Units.Add(armyRoot);
        }

        string dbPath = Path.Combine(
            Path.GetTempPath(), $"onlywar_roundtrip_{Guid.NewGuid():N}.s3db");
        try
        {
            List<Unit> originalUnits = _data.Factions.SelectMany(f => f.Units).ToList();
            Save(sector, dbPath, originalUnits);
            GameStateDataBlob loaded = Load(dbPath);

            Assert.Equal(_date, loaded.CurrentDate);
            Assert.Equal(sector.Planets.Count, loaded.Planets.Count);
            Assert.Equal(sector.Characters.Count(), loaded.Characters.Count);
            Assert.Equal(sector.PlayerForce.Requests.Count, loaded.Requests.Count);

            int originalShips = sector.Fleets.Values.SelectMany(tf => tf.Ships).Count();
            int loadedShips = loaded.Fleets.SelectMany(tf => tf.Ships).Count();
            Assert.Equal(originalShips, loadedShips);

            Assert.Equal(CountSoldiers(originalUnits), CountSoldiers(loaded.Units));
            Assert.Equal(CountSquads(originalUnits), CountSquads(loaded.Units));
            Assert.Equal(TotalPopulation(sector.Planets.Values), TotalPopulation(loaded.Planets));

            // Carrying capacity is generated, persisted, and restored per region; and no
            // region is generated above its carrying capacity (PRD Strategic Layer Phase 2).
            Assert.Equal(TotalCarryingCapacity(sector.Planets.Values), TotalCarryingCapacity(loaded.Planets));
            // MaximumCarryingCapacity (PRD §4.24) is persisted and restored alongside it.
            Assert.Equal(TotalMaximumCarryingCapacity(sector.Planets.Values), TotalMaximumCarryingCapacity(loaded.Planets));
            Assert.True(sector.Planets.Values.Sum(p => p.Regions.Length) > 0);
            int? promisedId = sector.Scenario?.PromisedPlanetId;
            Assert.All(
                sector.Planets.Values.SelectMany(p => p.Regions),
                r =>
                {
                    // No region ever sits above its natural ceiling.
                    Assert.True(r.CarryingCapacity <= r.MaximumCarryingCapacity,
                        $"Region {r.Id} capacity {r.CarryingCapacity} exceeds max {r.MaximumCarryingCapacity}");
                    // Away from the invaded promised world, generation leaves capacity pristine.
                    // The promised world may start blighted: the opening sim lets the stranded
                    // Tyranids scour carrying capacity before the player arrives (PRD §4.24).
                    if (r.Planet.Id != promisedId)
                    {
                        Assert.Equal(r.CarryingCapacity, r.MaximumCarryingCapacity);
                        // No region starts above capacity (Strategic Layer Phase 2); on the
                        // promised world consumption can transiently leave survivors above the
                        // scoured capacity, so that world is excluded from this check.
                        Assert.True(r.Population <= r.CarryingCapacity,
                            $"Region {r.Id} population {r.Population} exceeds capacity {r.CarryingCapacity}");
                    }
                });

            // Open-ended evaluation ratings survive the round trip (the SoldierEvaluation
            // / SoldierEvaluationRating split). Every loaded evaluation carries its keyed
            // rating values.
            List<SoldierEvaluation> loadedEvaluations = loaded.Units
                .SelectMany(u => u.GetAllSquads())
                .SelectMany(s => s.Members)
                .OfType<PlayerSoldier>()
                .SelectMany(ps => ps.SoldierEvaluationHistory)
                .ToList();
            Assert.NotEmpty(loadedEvaluations);
            Assert.All(loadedEvaluations, e => Assert.Contains(RatingKeys.Melee, e.Ratings.Keys));
        }
        finally
        {
            // GameStateDataAccess closes but does not dispose its connections, so the
            // pooled handle keeps the file open; clear the pool before deleting.
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup of a temp file; ignore if still locked.
            }
        }
    }

    [Fact]
    public void SaveThenLoad_PlanetFactionRegionIntel_SurvivesRoundTrip()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "RegionIntel Round Trip Chapter");
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, sector);

        Planet planet = sector.Planets.Values
            .First(p => p.PlanetFactionMap.Count > 0 && p.Regions.Any(r => r != null));
        PlanetFaction planetFaction = planet.PlanetFactionMap.Values.First();
        Region regionA = planet.Regions.First(r => r != null);
        Region regionB = planet.Regions.Last(r => r != null);

        float baseA = planetFaction.GetRegionIntel(regionA);
        float baseB = planetFaction.GetRegionIntel(regionB);
        planetFaction.AddRegionIntel(regionA, 2.25f);
        planetFaction.AddRegionIntel(regionB, 0.5f);

        string dbPath = Path.Combine(
            Path.GetTempPath(), $"onlywar_regionintel_roundtrip_{Guid.NewGuid():N}.s3db");
        try
        {
            Save(sector, dbPath, _data.Factions.SelectMany(f => f.Units).ToList());
            GameStateDataBlob loaded = Load(dbPath);

            Planet loadedPlanet = loaded.Planets.Single(p => p.Id == planet.Id);
            PlanetFaction loadedFaction = loadedPlanet.PlanetFactionMap[planetFaction.Faction.Id];
            Region loadedA = loadedPlanet.Regions.Single(r => r != null && r.Id == regionA.Id);
            Region loadedB = loadedPlanet.Regions.Single(r => r != null && r.Id == regionB.Id);

            Assert.Equal(baseA + 2.25f, loadedFaction.GetRegionIntel(loadedA), 3);
            Assert.Equal(baseB + 0.5f, loadedFaction.GetRegionIntel(loadedB), 3);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup of a temp file; ignore if still locked.
            }
        }
    }

    [Fact]
    public void Load_LegacyRegionIntelligence_MigratesToPlayerAndDefaultRegionIntel()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Legacy Intel Migration Chapter");
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, sector);

        Planet planet = sector.Planets.Values
            .First(p => p.PlanetFactionMap.Values.Any(pf => pf.Faction.IsDefaultFaction)
                && p.Regions.Any(r => r != null));
        Region region = planet.Regions.First(r => r != null);
        planet.PlanetFactionMap[sector.PlayerForce.Faction.Id] = new PlanetFaction(sector.PlayerForce.Faction);

        string dbPath = Path.Combine(
            Path.GetTempPath(), $"onlywar_legacy_regionintel_{Guid.NewGuid():N}.s3db");
        try
        {
            Save(sector, dbPath, _data.Factions.SelectMany(f => f.Units).ToList());
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "DELETE FROM PlanetFactionRegionIntel WHERE RegionId = $regionId";
                    command.Parameters.AddWithValue("$regionId", region.Id);
                    command.ExecuteNonQuery();
                }
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "UPDATE Region SET IntelligenceLevel = $intel WHERE Id = $regionId";
                    command.Parameters.AddWithValue("$intel", 4.5f);
                    command.Parameters.AddWithValue("$regionId", region.Id);
                    command.ExecuteNonQuery();
                }
            }

            GameStateDataBlob loaded = Load(dbPath);

            Planet loadedPlanet = loaded.Planets.Single(p => p.Id == planet.Id);
            Region loadedRegion = loadedPlanet.Regions.Single(r => r != null && r.Id == region.Id);
            PlanetFaction loadedDefault = loadedPlanet.PlanetFactionMap.Values.Single(pf => pf.Faction.IsDefaultFaction);
            PlanetFaction loadedPlayer = loadedPlanet.PlanetFactionMap[sector.PlayerForce.Faction.Id];

            Assert.Equal(4.5f, loadedDefault.GetRegionIntel(loadedRegion), 3);
            Assert.Equal(4.5f, loadedPlayer.GetRegionIntel(loadedRegion), 3);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup of a temp file; ignore if still locked.
            }
        }
    }

    [Fact]
    public void SaveThenLoad_PlayerOrderWithNonSpecialMission_SurvivesRoundTrip()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Order Round Trip Chapter");
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, sector);
        Unit armyRoot = sector.PlayerForce.Army.OrderOfBattle;
        if (!_data.PlayerFaction.Units.Contains(armyRoot))
        {
            _data.PlayerFaction.Units.Add(armyRoot);
        }

        // Assign a player squad a Recon order. The mission is created on the order and never
        // added to any region's SpecialMissions — exactly the case that previously failed to
        // round-trip (the loader resolved order missions only from SpecialMissions).
        Squad squad = sector.PlayerForce.Army.OrderOfBattle.GetAllSquads().First();
        Region region = sector.Planets.Values
            .SelectMany(p => p.Regions)
            .First(r => r.RegionFactionMap.Count > 0);
        RegionFaction target = region.RegionFactionMap.Values.First();
        Mission mission = new(MissionType.Recon, target, 0);
        Order order = new([squad], Disposition.Mobile, true, false, Aggression.Normal, mission);
        squad.CurrentOrders = order;
        sector.AddNewOrder(order);

        string dbPath = Path.Combine(
            Path.GetTempPath(), $"onlywar_order_roundtrip_{Guid.NewGuid():N}.s3db");
        try
        {
            Save(sector, dbPath, _data.Factions.SelectMany(f => f.Units).ToList());
            GameStateDataBlob loaded = Load(dbPath);

            Squad loadedSquad = loaded.Units
                .SelectMany(u => u.GetAllSquads())
                .Single(s => s.Id == squad.Id);
            Assert.NotNull(loadedSquad.CurrentOrders);
            Assert.Equal(MissionType.Recon, loadedSquad.CurrentOrders.Mission.MissionType);

            // the order's mission must not have leaked into any region's special missions
            Assert.DoesNotContain(
                loaded.Planets.SelectMany(p => p.Regions).SelectMany(r => r.SpecialMissions),
                m => m.Id == mission.Id);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup of a temp file; ignore if still locked.
            }
        }
    }

    [Fact]
    public void SaveThenLoad_SoldierEvents_SurviveRoundTrip()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Event Round Trip Chapter");
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, sector);
        Unit armyRoot = sector.PlayerForce.Army.OrderOfBattle;
        if (!_data.PlayerFaction.Units.Contains(armyRoot))
        {
            _data.PlayerFaction.Units.Add(armyRoot);
        }

        // Attach a structured battle event with all the queryable fields populated.
        PlayerSoldier soldier = armyRoot.GetAllSquads()
            .SelectMany(s => s.Members)
            .OfType<PlayerSoldier>()
            .First();
        SoldierEvent battleEvent = new(_date, SoldierEventType.BattleParticipation,
            "Skirmish in the Northern Waste, Test World. Felled 4 Tyranids.",
            factionId: 7, magnitude: 4, locationName: "Northern Waste, Test World");
        soldier.AddEvent(battleEvent);

        string dbPath = Path.Combine(
            Path.GetTempPath(), $"onlywar_events_roundtrip_{Guid.NewGuid():N}.s3db");
        try
        {
            Save(sector, dbPath, _data.Factions.SelectMany(f => f.Units).ToList());
            GameStateDataBlob loaded = Load(dbPath);

            PlayerSoldier loadedSoldier = loaded.Units
                .SelectMany(u => u.GetAllSquads())
                .SelectMany(s => s.Members)
                .OfType<PlayerSoldier>()
                .Single(ps => ps.Id == soldier.Id);

            // Generation events (training, promotion) round-trip too, so just isolate ours.
            SoldierEvent loadedEvent = loadedSoldier.SoldierEvents
                .Single(e => e.Type == SoldierEventType.BattleParticipation);
            Assert.Equal(battleEvent.Detail, loadedEvent.Detail);
            Assert.Equal(_date, loadedEvent.Date);
            Assert.Equal(7, loadedEvent.FactionId);
            Assert.Equal(4, loadedEvent.Magnitude);
            Assert.Equal("Northern Waste, Test World", loadedEvent.LocationName);
            Assert.Equal(battleEvent.Render(), loadedEvent.Render());
            // Soldiers always have generation history; confirm it survived too.
            Assert.True(loadedSoldier.SoldierEvents.Count > 1);
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public void SaveThenLoad_FallenBrother_IsPreservedWithDossier()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Fallen Round Trip Chapter");
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, sector);
        Unit armyRoot = sector.PlayerForce.Army.OrderOfBattle;
        if (!_data.PlayerFaction.Units.Contains(armyRoot))
        {
            _data.PlayerFaction.Units.Add(armyRoot);
        }

        // Simulate the death path (mirrors BattleTurnResolver.RemoveSoldiersKilledInBattle):
        // record a death event, then move the brother out of his squad and the active roster
        // into the fallen store.
        Army army = sector.PlayerForce.Army;
        PlayerSoldier doomed = army.PlayerSoldierMap.Values.First(s => s.AssignedSquad != null);
        int doomedId = doomed.Id;
        string doomedName = doomed.Name;
        doomed.AddEvent(new SoldierEvent(_date, SoldierEventType.Death,
            "Killed in battle with the Tyranids by a Scything Talon",
            factionId: 7, weaponTemplateId: 13));
        doomed.AssignedSquad.RemoveSquadMember(doomed);
        doomed.AssignedSquad = null;
        army.PlayerSoldierMap.Remove(doomedId);
        army.FallenBrothers[doomedId] = doomed;

        string dbPath = Path.Combine(
            Path.GetTempPath(), $"onlywar_fallen_roundtrip_{Guid.NewGuid():N}.s3db");
        try
        {
            Save(sector, dbPath, _data.Factions.SelectMany(f => f.Units).ToList());
            GameStateDataBlob loaded = Load(dbPath);

            PlayerSoldier loadedFallen = Assert.Single(loaded.FallenBrothers);
            Assert.Equal(doomedId, loadedFallen.Id);
            Assert.Equal(doomedName, loadedFallen.Name);
            Assert.Null(loadedFallen.AssignedSquad);

            SoldierEvent death = loadedFallen.SoldierEvents
                .Single(e => e.Type == SoldierEventType.Death);
            Assert.Equal(7, death.FactionId);
            Assert.Equal(13, death.WeaponTemplateId);
            Assert.Equal("Killed in battle with the Tyranids by a Scything Talon", death.Render());

            // The fallen brother is gone from the active roster reachable through the units.
            Assert.DoesNotContain(
                loaded.Units.SelectMany(u => u.GetAllSquads()).SelectMany(s => s.Members),
                m => m.Id == doomedId);
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    [Fact]
    public void Load_ReconstructsArmy_WithoutPreSeedingFactionUnits()
    {
        // Regression for the load crash: the real load path (StartMenu -> SavedGameLoader)
        // rebuilds the player's Army from a freshly constructed GameRulesData whose
        // PlayerFaction.Units is empty, relying on the loaded blob to supply the order of
        // battle. It previously threw "Sequence contains no elements" because nothing
        // registered the loaded units onto the faction. Unlike the other round-trip tests,
        // this deliberately does NOT pre-seed the *reconstruction* faction, reproducing the
        // real load path; the generation-side faction is still seeded so the save can write.
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Load Reconstruct Chapter");
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, sector);
        Unit armyRoot = sector.PlayerForce.Army.OrderOfBattle;
        if (!_data.PlayerFaction.Units.Contains(armyRoot))
        {
            _data.PlayerFaction.Units.Add(armyRoot);
        }
        int expectedSoldierCount = armyRoot.GetAllMembers().Count();

        string dbPath = Path.Combine(
            Path.GetTempPath(), $"onlywar_load_reconstruct_{Guid.NewGuid():N}.s3db");
        try
        {
            Save(sector, dbPath, _data.Factions.SelectMany(f => f.Units).ToList());
            GameStateDataBlob loaded = Load(dbPath);

            // A fresh rules-data instance, exactly as the real load path constructs. Its
            // player faction starts with no units; the loader must populate them from the blob.
            GameRulesData freshRules = new();
            Assert.Empty(freshRules.PlayerFaction.Units);

            Sector rebuilt = SavedGameLoader.BuildSectorFromBlob(loaded, freshRules);

            // The loader registered the loaded order of battle on the previously empty faction,
            // so both the reconstructed army and any subsequent save (which enumerates units via
            // Faction.Units) work.
            Assert.NotEmpty(freshRules.PlayerFaction.Units);
            Assert.Equal(expectedSoldierCount,
                rebuilt.PlayerForce.Army.OrderOfBattle.GetAllMembers().Count());
        }
        finally
        {
            CleanupDb(dbPath);
        }
    }

    private static void CleanupDb(string dbPath)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of a temp file; ignore if still locked.
        }
    }

    private void Save(Sector sector, string dbPath, IEnumerable<Unit> units)
    {
        string schemaPath = Path.Combine(
            RulesDatabaseFixture.RepositoryRoot, "Database", "SaveStructure.sql");
        GameStateDataAccess.Instance.SaveData(
            dbPath,
            _date,
            sector.PlayerForce.Army.Requisition,
            sector.PlayerForce.GeneseedStockpile,
            sector.PlayerForce.GeneseedPurity,
            sector.Scenario,
            sector.PlayerForce.Army.MedicalProcedures,
            sector.Characters,
            sector.PlayerForce.Requests,
            sector.Planets.Values,
            sector.Fleets.Values,
            units,
            sector.PlayerForce.Army.PlayerSoldierMap.Values,
            sector.PlayerForce.Army.FallenBrothers.Values,
            sector.PlayerForce.BattleHistory,
            schemaPath);
    }

    private GameStateDataBlob Load(string dbPath)
    {
        var shipTemplateMap = _data.Factions.Where(f => f.ShipTemplates != null)
            .SelectMany(f => f.ShipTemplates.Values).ToDictionary(s => s.Id);
        var unitTemplateMap = _data.Factions.Where(f => f.UnitTemplates != null)
            .SelectMany(f => f.UnitTemplates.Values).ToDictionary(u => u.Id);
        var squadTemplateMap = _data.Factions.Where(f => f.SquadTemplates != null)
            .SelectMany(f => f.SquadTemplates.Values).ToDictionary(s => s.Id);
        var hitLocations = _data.BodyHitLocationTemplateMap.Values.SelectMany(hl => hl)
            .Distinct().ToDictionary(hl => hl.Id);
        var soldierTypeMap = _data.Factions.Where(f => f.SoldierTemplates != null)
            .SelectMany(f => f.SoldierTemplates.Values).ToDictionary(st => st.Id);

        return GameStateDataAccess.Instance.GetData(
            dbPath,
            _data.Factions.ToDictionary(f => f.Id),
            _data.PlanetTemplateMap,
            shipTemplateMap,
            unitTemplateMap,
            squadTemplateMap,
            _data.WeaponSets,
            hitLocations,
            _data.BaseSkillMap,
            soldierTypeMap);
    }

    private static int CountSoldiers(IEnumerable<Unit> rootUnits)
    {
        return rootUnits.Sum(u => u.GetAllSquads().Sum(s => s.Members.Count));
    }

    private static int CountSquads(IEnumerable<Unit> rootUnits)
    {
        return rootUnits.Sum(u => u.GetAllSquads().Count());
    }

    private static long TotalCarryingCapacity(IEnumerable<Models.Planets.Planet> planets)
    {
        return planets.Sum(p => p.Regions.Sum(r => r.CarryingCapacity));
    }

    private static long TotalMaximumCarryingCapacity(IEnumerable<Models.Planets.Planet> planets)
    {
        return planets.Sum(p => p.Regions.Sum(r => r.MaximumCarryingCapacity));
    }

    private static long TotalPopulation(IEnumerable<Models.Planets.Planet> planets)
    {
        return planets.Sum(p => p.Population);
    }
}
