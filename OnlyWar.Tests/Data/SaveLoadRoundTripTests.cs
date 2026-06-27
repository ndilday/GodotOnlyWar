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
            Assert.True(sector.Planets.Values.Sum(p => p.Regions.Length) > 0);
            Assert.All(
                sector.Planets.Values.SelectMany(p => p.Regions),
                r => Assert.True(r.Population <= r.CarryingCapacity,
                    $"Region {r.Id} population {r.Population} exceeds capacity {r.CarryingCapacity}"));

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

    private static long TotalPopulation(IEnumerable<Models.Planets.Planet> planets)
    {
        return planets.Sum(p => p.Population);
    }
}
