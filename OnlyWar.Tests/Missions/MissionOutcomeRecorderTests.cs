using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Missions;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Missions;

public class MissionOutcomeRecorderTests
{
    private static int _nextId = 5000;

    [Fact]
    public void PlayerRecon_UndetectedProducesOneEventPerParticipatingSoldier()
    {
        Faction player = CreateFaction(1, "Chapter", isPlayer: true);
        Faction enemy = CreateFaction(2, "Cult", isPlayer: false);
        Region region = CreateRegion("Ashfields", "Gehenna");
        RegionFaction targetFaction = new(new PlanetFaction(enemy), region);

        PlayerSoldier soldierA = CreatePlayerSoldier("Brother Atreus");
        PlayerSoldier soldierB = CreatePlayerSoldier("Brother Bellus");
        Squad squad = CreateSquad("Scout Squad", soldierA, soldierB);
        squad.CurrentOrders = new Order([squad], Disposition.Raiding, true, false,
            Aggression.Cautious, new Mission(MissionType.Recon, targetFaction, 0));
        BattleSquad battleSquad = new(true, squad);

        MissionContext context = new(squad.CurrentOrders, [battleSquad], []);
        // no Spotter set -> undetected; no EnemiesKilled -> pure recon

        MissionOutcomeRecorder.RecordMissionOutcome(context, new Date(1, 1, 1));

        Assert.Single(soldierA.SoldierEvents);
        Assert.Single(soldierB.SoldierEvents);
        SoldierEvent eventA = soldierA.SoldierEvents[0];
        Assert.Equal(SoldierEventType.MissionOutcome, eventA.Type);
        Assert.Equal(enemy.Id, eventA.FactionId);
        Assert.Equal("Ashfields, Gehenna", eventA.LocationName);
        Assert.Contains("infiltrated undetected", eventA.Detail);
    }

    [Fact]
    public void PlayerRecon_DetectedNotesBrokenContact()
    {
        Faction player = CreateFaction(3, "Chapter", isPlayer: true);
        Faction enemy = CreateFaction(4, "Orks", isPlayer: false);
        Region region = CreateRegion("Ironveldt", "Kroll");
        RegionFaction targetFaction = new(new PlanetFaction(enemy), region);
        RegionFaction spotterFaction = new(new PlanetFaction(enemy), region);

        PlayerSoldier soldier = CreatePlayerSoldier("Brother Castus");
        Squad squad = CreateSquad("Scout Squad", soldier);
        squad.CurrentOrders = new Order([squad], Disposition.Raiding, true, false,
            Aggression.Cautious, new Mission(MissionType.Recon, targetFaction, 0));
        BattleSquad battleSquad = new(true, squad);

        MissionContext context = new(squad.CurrentOrders, [battleSquad], [])
        {
            Spotter = spotterFaction
        };

        MissionOutcomeRecorder.RecordMissionOutcome(context, new Date(1, 1, 1));

        Assert.Single(soldier.SoldierEvents);
        Assert.Contains("break contact", soldier.SoldierEvents[0].Detail);
    }

    [Fact]
    public void PlayerAssassination_TargetLocatedAndKilled_RecordsElimination()
    {
        Faction player = CreateFaction(30, "Chapter", isPlayer: true);
        Faction enemy = CreateFaction(31, "Cult", isPlayer: false);
        Region region = CreateRegion("Spire", "Necromunda");
        RegionFaction targetFaction = new(new PlanetFaction(enemy), region);

        PlayerSoldier soldier = CreatePlayerSoldier("Brother Vindicare");
        Squad squad = CreateSquad("Kill Team", soldier);
        squad.CurrentOrders = new Order([squad], Disposition.Raiding, true, false,
            Aggression.Cautious, new Mission(MissionType.Assassination, targetFaction, 0));
        BattleSquad battleSquad = new(true, squad);

        MissionContext context = new(squad.CurrentOrders, [battleSquad], [])
        {
            TargetLocated = true,
            EnemiesKilled = 1
        };

        MissionOutcomeRecorder.RecordMissionOutcome(context, new Date(1, 1, 1));

        Assert.Contains("target eliminated", soldier.SoldierEvents[0].Detail);
    }

    [Fact]
    public void PlayerAssassination_Aborted_RecordsAborted()
    {
        Faction player = CreateFaction(32, "Chapter", isPlayer: true);
        Faction enemy = CreateFaction(33, "Cult", isPlayer: false);
        Region region = CreateRegion("Underhive", "Necromunda");
        RegionFaction targetFaction = new(new PlanetFaction(enemy), region);

        PlayerSoldier soldier = CreatePlayerSoldier("Brother Eversor");
        Squad squad = CreateSquad("Kill Team", soldier);
        squad.CurrentOrders = new Order([squad], Disposition.Raiding, true, false,
            Aggression.Cautious, new Mission(MissionType.Assassination, targetFaction, 0));
        BattleSquad battleSquad = new(true, squad);

        // Force lost behind enemy lines before reaching the target -> the recorder's "aborted" branch.
        MissionContext context = new(squad.CurrentOrders, [battleSquad], [])
        {
            ForceLostContact = true
        };

        MissionOutcomeRecorder.RecordMissionOutcome(context, new Date(1, 1, 1));

        Assert.Contains("aborted", soldier.SoldierEvents[0].Detail);
    }

    [Fact]
    public void NpcMission_ProducesNoEvents()
    {
        Faction npcFaction = CreateFaction(5, "Imperial Guard", isPlayer: false);
        Faction enemy = CreateFaction(6, "Cult", isPlayer: false);
        Region region = CreateRegion("Dustbowl", "Gorgus");
        RegionFaction targetFaction = new(new PlanetFaction(enemy), region);

        Soldier npcSoldier = TestModelFactory.CreateSoldier(TestModelFactory.MarineTemplate, "Trooper");
        npcSoldier.Id = _nextId++;
        Squad squad = CreateSquad("Guard Squad", npcSoldier);
        squad.CurrentOrders = new Order([squad], Disposition.Raiding, true, false,
            Aggression.Cautious, new Mission(MissionType.Recon, targetFaction, 0));
        BattleSquad battleSquad = new(false, squad);

        MissionContext context = new(squad.CurrentOrders, [battleSquad], []);

        // Should not throw, and there is no PlayerSoldier to have received an event.
        MissionOutcomeRecorder.RecordMissionOutcome(context, new Date(1, 1, 1));

        Assert.IsNotType<PlayerSoldier>(battleSquad.Soldiers[0].Soldier);
    }

    [Fact]
    public void SoldierLostMidMission_DoesNotThrowAndRecordsSurvivors()
    {
        Faction player = CreateFaction(7, "Chapter", isPlayer: true);
        Faction enemy = CreateFaction(8, "Tyranids", isPlayer: false);
        Region region = CreateRegion("Hive Delta", "Behemoth");
        RegionFaction targetFaction = new(new PlanetFaction(enemy), region);

        PlayerSoldier survivor = CreatePlayerSoldier("Brother Survivor");
        PlayerSoldier fallen = CreatePlayerSoldier("Brother Fallen");
        Squad squad = CreateSquad("Raid Squad", survivor, fallen);
        squad.CurrentOrders = new Order([squad], Disposition.Raiding, true, false,
            Aggression.Aggressive, new Mission(MissionType.LightningRaid, targetFaction, 0));
        BattleSquad battleSquad = new(true, squad);

        // Simulate the fallen soldier being removed from the BattleSquad mid-mission, as
        // BattleTurnResolver does when a soldier dies in an embedded engagement.
        BattleSoldier fallenBattleSoldier = battleSquad.Soldiers.First(s => s.Soldier == fallen);
        battleSquad.RemoveSoldier(fallenBattleSoldier);

        MissionContext context = new(squad.CurrentOrders, [battleSquad], [])
        {
            EnemiesKilled = 3
        };

        var exception = Record.Exception(() =>
            MissionOutcomeRecorder.RecordMissionOutcome(context, new Date(1, 1, 1)));

        Assert.Null(exception);
        Assert.Single(survivor.SoldierEvents);
        Assert.Empty(fallen.SoldierEvents);
    }

    private static Squad CreateSquad(string name, params ISoldier[] soldiers)
    {
        Squad squad = new(name, null, TestModelFactory.SquadTemplate);
        foreach (ISoldier soldier in soldiers)
        {
            squad.AddSquadMember(soldier);
        }
        return squad;
    }

    private static Region CreateRegion(string regionName, string planetName)
    {
        Planet planet = new(_nextId++, planetName, new Coordinate(0, 0), 1, null, 0, 0);
        Region region = new(_nextId++, planet, 0, regionName, new RegionCoordinate(0, 0), 0);
        planet.Regions[0] = region;
        return region;
    }

    private static PlayerSoldier CreatePlayerSoldier(string name)
    {
        Soldier soldier = TestModelFactory.CreateSoldier(TestModelFactory.MarineTemplate, name);
        soldier.Id = _nextId++;
        return new PlayerSoldier(soldier, name);
    }

    private static Faction CreateFaction(int id, string name, bool isPlayer) =>
        new(id, name, Color.White, isPlayer, isDefaultFaction: false, canInfiltrate: false, GrowthType.None,
            new Dictionary<int, Species> { [TestModelFactory.HumanSpecies.Id] = TestModelFactory.HumanSpecies },
            new Dictionary<int, SoldierTemplate> { [TestModelFactory.MarineTemplate.Id] = TestModelFactory.MarineTemplate },
            new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, OnlyWar.Models.Units.UnitTemplate>(),
            new Dictionary<int, OnlyWar.Models.Fleets.BoatTemplate>(),
            new Dictionary<int, OnlyWar.Models.Fleets.ShipTemplate>(),
            new Dictionary<int, OnlyWar.Models.Fleets.FleetTemplate>());
}
