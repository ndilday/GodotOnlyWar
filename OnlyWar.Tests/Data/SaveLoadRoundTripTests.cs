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
    private readonly GameStateRoundTripFixture _roundTrip;

    public SaveLoadRoundTripTests()
    {
        Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
        _data = new GameRulesData();
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, null);
        _roundTrip = new GameStateRoundTripFixture(_data, _date);
    }

    [Fact]
    public void SaveThenLoad_MutatedGeneratedSector_PreservesRoundTripFeatures()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Round Trip Chapter");
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, sector);
        _roundTrip.RegisterPlayerArmy(sector);
        Unit armyRoot = sector.PlayerForce.Army.OrderOfBattle;

        sector.PlayerForce.Army.Requisition = 777;
        sector.PlayerForce.GeneseedStockpile = 13;
        sector.PlayerForce.GeneseedPurity = 0.83f;

        CampaignScenario originalScenario = sector.Scenario;
        Assert.NotNull(originalScenario);
        originalScenario.State = ObjectiveState.Won;
        originalScenario.BriefingAcknowledged = true;

        Faction tyranids = _data.Factions.Single(f => f.Name == "Tyranids");
        Planet promised = sector.GetPlanet(originalScenario.PromisedPlanetId);
        const float testMultiplier = 0.4f;
        List<RegionFaction> promisedTyranidFactions = promised.Regions
            .SelectMany(r => r.RegionFactionMap.Values)
            .Where(rf => rf.PlanetFaction.Faction.Id == tyranids.Id)
            .ToList();
        Assert.NotEmpty(promisedTyranidFactions);
        Assert.All(promisedTyranidFactions, rf => rf.GrowthMultiplier = testMultiplier);

        PlayerSoldier procedureSubject = armyRoot.GetAllMembers().OfType<PlayerSoldier>().First();
        MedicalProcedure procedure = new(procedureSubject.Id, 4, MedicalProcedureType.Cybernetic, 5, 40);
        sector.PlayerForce.Army.MedicalProcedures.Add(procedure);

        Planet intelPlanet = sector.Planets.Values
            .First(p => p.PlanetFactionMap.Count > 0 && p.Regions.Any(r => r != null));
        PlanetFaction intelFaction = intelPlanet.PlanetFactionMap.Values.First();
        Region intelRegionA = intelPlanet.Regions.First(r => r != null);
        Region intelRegionB = intelPlanet.Regions.Last(r => r != null);
        float baseIntelA = intelFaction.GetRegionIntel(intelRegionA);
        float baseIntelB = intelFaction.GetRegionIntel(intelRegionB);
        intelFaction.AddRegionIntel(intelRegionA, 2.25f);
        intelFaction.AddRegionIntel(intelRegionB, 0.5f);

        Squad orderedSquad = armyRoot.GetAllSquads().First(s => s.Members.Count > 0);
        Region orderRegion = sector.Planets.Values
            .SelectMany(p => p.Regions)
            .First(r => r.RegionFactionMap.Count > 0);
        RegionFaction orderTarget = orderRegion.RegionFactionMap.Values.First();
        Mission orderMission = new(MissionType.Recon, orderTarget, 0);
        Order order = new([orderedSquad], Disposition.Mobile, true, false, Aggression.Normal, orderMission);
        orderedSquad.CurrentOrders = order;
        sector.AddNewOrder(order);

        PlayerSoldier eventSoldier = armyRoot.GetAllSquads()
            .SelectMany(s => s.Members)
            .OfType<PlayerSoldier>()
            .First();
        SoldierEvent battleEvent = new(_date, SoldierEventType.BattleParticipation,
            "Skirmish in the Northern Waste, Test World. Felled 4 Tyranids.",
            factionId: 7, magnitude: 4, locationName: "Northern Waste, Test World");
        eventSoldier.AddEvent(battleEvent);

        Army army = sector.PlayerForce.Army;
        PlayerSoldier doomed = army.PlayerSoldierMap.Values
            .First(s => s.AssignedSquad != null && s.Id != eventSoldier.Id);
        int doomedId = doomed.Id;
        string doomedName = doomed.Name;
        doomed.AddEvent(new SoldierEvent(_date, SoldierEventType.Death,
            "Killed in battle with the Tyranids by a Scything Talon",
            factionId: 7, weaponTemplateId: 13));
        doomed.AssignedSquad.RemoveSquadMember(doomed);
        doomed.AssignedSquad = null;
        army.PlayerSoldierMap.Remove(doomedId);
        army.FallenBrothers[doomedId] = doomed;

        List<Unit> originalUnits = _roundTrip.CurrentUnits;
        string dbPath = GameStateRoundTripFixture.CreateTempDbPath("onlywar_roundtrip");
        try
        {
            _roundTrip.Save(sector, dbPath, originalUnits);
            GameStateDataBlob loaded = _roundTrip.Load(dbPath);

            Assert.Equal(_date, loaded.CurrentDate);
            Assert.Equal(777, loaded.Requisition);
            Assert.Equal(13, loaded.GeneseedStockpile);
            Assert.Equal(0.83f, loaded.GeneseedPurity, 3);
            Assert.Equal(sector.Planets.Count, loaded.Planets.Count);
            Assert.Equal(sector.Characters.Count(), loaded.Characters.Count);
            Assert.Equal(sector.PlayerForce.Requests.Count, loaded.Requests.Count);

            int originalShips = sector.Fleets.Values.SelectMany(tf => tf.Ships).Count();
            int loadedShips = loaded.Fleets.SelectMany(tf => tf.Ships).Count();
            Assert.Equal(originalShips, loadedShips);

            Assert.Equal(CountSoldiers(originalUnits), CountSoldiers(loaded.Units));
            Assert.Equal(CountSquads(originalUnits), CountSquads(loaded.Units));
            Assert.Equal(TotalPopulation(sector.Planets.Values), TotalPopulation(loaded.Planets));
            Assert.Equal(TotalCarryingCapacity(sector.Planets.Values), TotalCarryingCapacity(loaded.Planets));
            Assert.Equal(TotalMaximumCarryingCapacity(sector.Planets.Values), TotalMaximumCarryingCapacity(loaded.Planets));
            Assert.True(sector.Planets.Values.Sum(p => p.Regions.Length) > 0);

            int? promisedId = sector.Scenario?.PromisedPlanetId;
            Assert.All(
                sector.Planets.Values.SelectMany(p => p.Regions),
                r =>
                {
                    Assert.True(r.CarryingCapacity <= r.MaximumCarryingCapacity,
                        $"Region {r.Id} capacity {r.CarryingCapacity} exceeds max {r.MaximumCarryingCapacity}");
                    if (r.Planet.Id != promisedId)
                    {
                        Assert.Equal(r.CarryingCapacity, r.MaximumCarryingCapacity);
                        Assert.True(r.Population <= r.CarryingCapacity,
                            $"Region {r.Id} population {r.Population} exceeds capacity {r.CarryingCapacity}");
                    }
                });

            Assert.NotNull(loaded.Scenario);
            Assert.Equal(ScenarioType.PromisedWorld, loaded.Scenario.Type);
            Assert.Equal(originalScenario.PromisedPlanetId, loaded.Scenario.PromisedPlanetId);
            Assert.Equal(ObjectiveState.Won, loaded.Scenario.State);
            Assert.True(loaded.Scenario.BriefingAcknowledged);
            Assert.Equal(originalScenario.BriefingText, loaded.Scenario.BriefingText);
            Assert.Equal(originalScenario.OriginalAuthorityCharacterId,
                         loaded.Scenario.OriginalAuthorityCharacterId);

            Planet loadedPromised = loaded.Planets.Single(p => p.Id == originalScenario.PromisedPlanetId);
            List<RegionFaction> loadedTyranidFactions = loadedPromised.Regions
                .SelectMany(r => r.RegionFactionMap.Values)
                .Where(rf => rf.PlanetFaction.Faction.Id == tyranids.Id)
                .ToList();
            Assert.NotEmpty(loadedTyranidFactions);
            Assert.All(loadedTyranidFactions,
                rf => Assert.Equal(testMultiplier, rf.GrowthMultiplier));
            IEnumerable<RegionFaction> others = loaded.Planets
                .SelectMany(p => p.Regions)
                .SelectMany(r => r.RegionFactionMap.Values)
                .Where(rf => rf.PlanetFaction.Faction.Id != tyranids.Id
                             || rf.Region.Planet.Id != originalScenario.PromisedPlanetId);
            Assert.All(others, rf => Assert.Equal(1.0f, rf.GrowthMultiplier));

            MedicalProcedure loadedProcedure = Assert.Single(loaded.MedicalProcedures);
            Assert.Equal(procedureSubject.Id, loadedProcedure.SoldierId);
            Assert.Equal(4, loadedProcedure.HitLocationTemplateId);
            Assert.Equal(MedicalProcedureType.Cybernetic, loadedProcedure.ProcedureType);
            Assert.Equal(5, loadedProcedure.WeeksRemaining);
            Assert.Equal(40, loadedProcedure.RequisitionCost);

            Planet loadedIntelPlanet = loaded.Planets.Single(p => p.Id == intelPlanet.Id);
            PlanetFaction loadedIntelFaction = loadedIntelPlanet.PlanetFactionMap[intelFaction.Faction.Id];
            Region loadedIntelA = loadedIntelPlanet.Regions.Single(r => r != null && r.Id == intelRegionA.Id);
            Region loadedIntelB = loadedIntelPlanet.Regions.Single(r => r != null && r.Id == intelRegionB.Id);
            Assert.Equal(baseIntelA + 2.25f, loadedIntelFaction.GetRegionIntel(loadedIntelA), 3);
            Assert.Equal(baseIntelB + 0.5f, loadedIntelFaction.GetRegionIntel(loadedIntelB), 3);

            Squad loadedSquad = loaded.Units
                .SelectMany(u => u.GetAllSquads())
                .Single(s => s.Id == orderedSquad.Id);
            Assert.NotNull(loadedSquad.CurrentOrders);
            Assert.Equal(MissionType.Recon, loadedSquad.CurrentOrders.Mission.MissionType);
            Assert.DoesNotContain(
                loaded.Planets.SelectMany(p => p.Regions).SelectMany(r => r.SpecialMissions),
                m => m.Id == orderMission.Id);

            PlayerSoldier loadedEventSoldier = loaded.Units
                .SelectMany(u => u.GetAllSquads())
                .SelectMany(s => s.Members)
                .OfType<PlayerSoldier>()
                .Single(ps => ps.Id == eventSoldier.Id);
            SoldierEvent loadedEvent = loadedEventSoldier.SoldierEvents
                .Single(e => e.Type == SoldierEventType.BattleParticipation);
            Assert.Equal(battleEvent.Detail, loadedEvent.Detail);
            Assert.Equal(_date, loadedEvent.Date);
            Assert.Equal(7, loadedEvent.FactionId);
            Assert.Equal(4, loadedEvent.Magnitude);
            Assert.Equal("Northern Waste, Test World", loadedEvent.LocationName);
            Assert.Equal(battleEvent.Render(), loadedEvent.Render());
            Assert.True(loadedEventSoldier.SoldierEvents.Count > 1);

            PlayerSoldier loadedFallen = Assert.Single(loaded.FallenBrothers);
            Assert.Equal(doomedId, loadedFallen.Id);
            Assert.Equal(doomedName, loadedFallen.Name);
            Assert.Null(loadedFallen.AssignedSquad);
            SoldierEvent death = loadedFallen.SoldierEvents
                .Single(e => e.Type == SoldierEventType.Death);
            Assert.Equal(7, death.FactionId);
            Assert.Equal(13, death.WeaponTemplateId);
            Assert.Equal("Killed in battle with the Tyranids by a Scything Talon", death.Render());
            Assert.DoesNotContain(
                loaded.Units.SelectMany(u => u.GetAllSquads()).SelectMany(s => s.Members),
                m => m.Id == doomedId);

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
            GameStateRoundTripFixture.CleanupDb(dbPath);
        }
    }

    [Fact]
    public void Load_LegacySave_HasNullScenarioAndDefaultGrowthMultiplier()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Legacy Save Chapter");
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, sector);
        _roundTrip.RegisterPlayerArmy(sector);

        string dbPath = GameStateRoundTripFixture.CreateTempDbPath("onlywar_roundtrip_legacy");
        try
        {
            _roundTrip.Save(sector, dbPath, _roundTrip.CurrentUnits);
            // Rewrite the save into a pre-scenario ("legacy") shape: a GlobalData row without
            // the Scenario* columns, and a RegionFaction table without GrowthMultiplier. The
            // column-count guards in GlobalDataAccess / PlanetDataAccess must then yield a null
            // scenario and a default 1.0 multiplier.
            DowngradeToLegacySchema(dbPath);

            GameStateDataBlob loaded = _roundTrip.Load(dbPath);

            Assert.Null(loaded.Scenario);
            IEnumerable<RegionFaction> allRegionFactions = loaded.Planets
                .SelectMany(p => p.Regions)
                .SelectMany(r => r.RegionFactionMap.Values);
            Assert.NotEmpty(allRegionFactions);
            Assert.All(allRegionFactions, rf => Assert.Equal(1.0f, rf.GrowthMultiplier));
        }
        finally
        {
            GameStateRoundTripFixture.CleanupDb(dbPath);
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
    public void Load_LegacyRegionIntelligence_MigratesToPlayerAndDefaultRegionIntel()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Legacy Intel Migration Chapter");
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, sector);

        Planet planet = sector.Planets.Values
            .First(p => p.PlanetFactionMap.Values.Any(pf => pf.Faction.IsDefaultFaction)
                && p.Regions.Any(r => r != null));
        Region region = planet.Regions.First(r => r != null);
        planet.PlanetFactionMap[sector.PlayerForce.Faction.Id] = new PlanetFaction(sector.PlayerForce.Faction);

        string dbPath = GameStateRoundTripFixture.CreateTempDbPath("onlywar_legacy_regionintel");
        try
        {
            _roundTrip.Save(sector, dbPath, _roundTrip.CurrentUnits);
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

            GameStateDataBlob loaded = _roundTrip.Load(dbPath);

            Planet loadedPlanet = loaded.Planets.Single(p => p.Id == planet.Id);
            Region loadedRegion = loadedPlanet.Regions.Single(r => r != null && r.Id == region.Id);
            PlanetFaction loadedDefault = loadedPlanet.PlanetFactionMap.Values.Single(pf => pf.Faction.IsDefaultFaction);
            PlanetFaction loadedPlayer = loadedPlanet.PlanetFactionMap[sector.PlayerForce.Faction.Id];

            Assert.Equal(4.5f, loadedDefault.GetRegionIntel(loadedRegion), 3);
            Assert.Equal(4.5f, loadedPlayer.GetRegionIntel(loadedRegion), 3);
        }
        finally
        {
            GameStateRoundTripFixture.CleanupDb(dbPath);
        }
    }

    [Fact]
    public void Load_ReconstructsArmy_WithoutPreSeedingFactionUnits()
    {
        // Regression for the load crash: the real load path (StartMenu -> SavedGameLoader)
        // rebuilds the player's Army from a freshly constructed GameRulesData whose
        // PlayerFaction.Units is empty, relying on the loaded blob to supply the order of
        // battle. Unlike the other round-trip tests, this deliberately does NOT pre-seed the
        // reconstruction faction, reproducing the real load path.
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Load Reconstruct Chapter");
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, sector);
        _roundTrip.RegisterPlayerArmy(sector);
        Unit armyRoot = sector.PlayerForce.Army.OrderOfBattle;
        int expectedSoldierCount = armyRoot.GetAllMembers().Count();

        string dbPath = GameStateRoundTripFixture.CreateTempDbPath("onlywar_load_reconstruct");
        try
        {
            _roundTrip.Save(sector, dbPath, _roundTrip.CurrentUnits);
            GameStateDataBlob loaded = _roundTrip.Load(dbPath);

            // A fresh rules-data instance, exactly as the real load path constructs. Its
            // player faction starts with no units; the loader must populate them from the blob.
            GameRulesData freshRules = new();
            Assert.Empty(freshRules.PlayerFaction.Units);

            Sector rebuilt = SavedGameLoader.BuildSectorFromBlob(loaded, freshRules);

            // The loader registered the loaded order of battle on the previously empty faction,
            // so both the reconstructed army and any subsequent save work.
            Assert.NotEmpty(freshRules.PlayerFaction.Units);
            Assert.Equal(expectedSoldierCount,
                rebuilt.PlayerForce.Army.OrderOfBattle.GetAllMembers().Count());
        }
        finally
        {
            GameStateRoundTripFixture.CleanupDb(dbPath);
        }
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
