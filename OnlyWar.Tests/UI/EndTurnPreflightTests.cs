using OnlyWar.Helpers.Settings;
using OnlyWar.Helpers.Turns;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Xunit;

namespace OnlyWar.Tests.UI;

public class EndTurnPreflightTests
{
    [Fact]
    public void Evaluate_RoutineTurnNeedsNoConfirmation()
    {
        TestCampaign campaign = CreateCampaign();

        EndTurnPreflightReport report = EndTurnPreflight.Evaluate(
            campaign.Sector,
            new EndTurnWarningPreferences());

        Assert.False(report.RequiresConfirmation);
        Assert.Empty(report.Items);
    }

    [Fact]
    public void Evaluate_ReportsIdleCombatCapableSquadsLandedOrEmbarkedInOrbit()
    {
        TestCampaign campaign = CreateCampaign();
        Squad idle = AddSquad(campaign, "Squad Invictus", campaign.Region);
        Squad ordered = AddSquad(campaign, "Squad Vigilant", campaign.Region);
        _ = new Order(
            [ordered],
            Disposition.DugIn,
            false,
            false,
            Aggression.Cautious,
            CreateMission(campaign, MissionType.Patrol));
        Squad embarked = AddSquad(campaign, "Squad Aboard");
        Ship ship = CreateShip(20, "Duty's Honour");
        TaskForce fleet = AddTaskForce(campaign, 20, campaign.Planet, ship);
        ship.LoadSquad(embarked);
        embarked.BoardedLocation = ship;
        Squad inTransit = AddSquad(campaign, "Squad In Transit");
        Ship transitShip = CreateShip(21, "Voyager");
        Planet destination = CreatePlanet(2, "Cadia");
        TaskForce transitFleet = new(
            21,
            campaign.PlayerFaction,
            campaign.Planet.Position,
            null,
            destination,
            [transitShip],
            travelWeeksRemaining: 2,
            travelPhase: FleetTravelPhase.InWarp);
        campaign.Sector.AddNewFleet(transitFleet);
        campaign.PlayerForce.Fleet.TaskForces.Add(transitFleet);
        transitShip.LoadSquad(inTransit);
        inTransit.BoardedLocation = transitShip;
        Squad emptyLanded = new("Empty Reserve", campaign.RootUnit, campaign.SquadTemplate)
        {
            CurrentRegion = campaign.Region
        };
        campaign.RootUnit.AddSquad(emptyLanded);

        EndTurnPreflightReport report = EndTurnPreflight.Evaluate(
            campaign.Sector,
            new EndTurnWarningPreferences
            {
                WarnActionableTaskForces = false,
                WarnSpecialMissionOpportunities = false
            });

        Assert.Equal(2, report.Items.Count);
        Assert.All(report.Items, item =>
            Assert.Equal(EndTurnWarningCategory.IdleDeployableSquads, item.Category));
        EndTurnAttentionItem landedItem = Assert.Single(
            report.Items, item => item.EntityId == idle.Id);
        Assert.Contains("Squad Invictus", landedItem.Title);
        Assert.Contains("Region Primus, Vigilus", landedItem.Detail);
        Assert.Contains("no orders", landedItem.Detail, StringComparison.OrdinalIgnoreCase);

        EndTurnAttentionItem embarkedItem = Assert.Single(
            report.Items, item => item.EntityId == embarked.Id);
        Assert.Contains("Squad Aboard", embarkedItem.Title);
        Assert.Contains("orbiting Vigilus", embarkedItem.Detail);
        Assert.DoesNotContain(report.Items, item => item.EntityId == inTransit.Id);
    }

    [Fact]
    public void Evaluate_ReportsOnlyInOrbitPlayerTaskForcesWithShipsAndNoDestination()
    {
        TestCampaign campaign = CreateCampaign();
        TaskForce ready = AddTaskForce(campaign, 31, campaign.Planet, CreateShip(31, "Ready Ship"));

        Planet destination = CreatePlanet(2, "Cadia");
        TaskForce moving = new(
            32,
            campaign.PlayerFaction,
            campaign.Planet.Position,
            null,
            destination,
            [CreateShip(32, "Moving Ship")],
            travelWeeksRemaining: 2,
            travelPhase: FleetTravelPhase.InWarp);
        campaign.Sector.AddNewFleet(moving);
        campaign.PlayerForce.Fleet.TaskForces.Add(moving);

        TaskForce empty = new(
            33,
            campaign.PlayerFaction,
            campaign.Planet.Position,
            campaign.Planet,
            null,
            []);
        campaign.Sector.AddNewFleet(empty);
        campaign.PlayerForce.Fleet.TaskForces.Add(empty);

        EndTurnPreflightReport report = EndTurnPreflight.Evaluate(
            campaign.Sector,
            new EndTurnWarningPreferences
            {
                WarnIdleDeployableSquads = false,
                WarnSpecialMissionOpportunities = false
            });

        EndTurnAttentionItem item = Assert.Single(report.Items);
        Assert.Equal(EndTurnWarningCategory.ActionableTaskForces, item.Category);
        Assert.Equal(ready.Id, item.EntityId);
        Assert.Contains("Task Force 31", item.Title);
        Assert.Contains("orbiting Vigilus", item.Detail);
        Assert.Contains("no destination", item.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_UnassignedSpecialMissionExplainsIndependentTwentyFivePercentRisk()
    {
        TestCampaign campaign = CreateCampaign();
        campaign.PlayerPlanetFaction.SetRegionIntel(campaign.Region, 3f);
        Mission mission = CreateMission(campaign, MissionType.Sabotage);
        campaign.Region.SpecialMissions.Add(mission);

        EndTurnPreflightReport report = EndTurnPreflight.Evaluate(
            campaign.Sector,
            new EndTurnWarningPreferences
            {
                WarnIdleDeployableSquads = false,
                WarnActionableTaskForces = false
            });

        EndTurnAttentionItem item = Assert.Single(report.Items);
        Assert.Equal(EndTurnWarningCategory.SpecialMissionOpportunities, item.Category);
        Assert.Equal(mission.Id, item.EntityId);
        Assert.Contains("Sabotage opportunity", item.Title);
        Assert.Contains("independent 25% chance", item.Detail);
        Assert.DoesNotContain("turns remaining", item.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_SpecialMissionWithZeroIntelExplainsItWillBeCleared()
    {
        TestCampaign campaign = CreateCampaign();
        Mission mission = CreateMission(campaign, MissionType.Assassination);
        campaign.Region.SpecialMissions.Add(mission);

        EndTurnPreflightReport report = EndTurnPreflight.Evaluate(
            campaign.Sector,
            new EndTurnWarningPreferences
            {
                WarnIdleDeployableSquads = false,
                WarnActionableTaskForces = false
            });

        EndTurnAttentionItem item = Assert.Single(report.Items);
        Assert.Contains("intelligence is already zero", item.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cleared when the turn advances", item.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_DoesNotWarnForSpecialMissionAlreadyAssignedThisTurn()
    {
        TestCampaign campaign = CreateCampaign();
        Squad squad = AddSquad(campaign, "Squad Resolute", campaign.Region);
        Mission mission = CreateMission(campaign, MissionType.Ambush);
        campaign.Region.SpecialMissions.Add(mission);
        _ = new Order(
            [squad],
            Disposition.DugIn,
            true,
            true,
            Aggression.Normal,
            mission);

        EndTurnPreflightReport report = EndTurnPreflight.Evaluate(
            campaign.Sector,
            new EndTurnWarningPreferences
            {
                WarnIdleDeployableSquads = false,
                WarnActionableTaskForces = false
            });

        Assert.Empty(report.Items);
    }

    [Fact]
    public void Evaluate_HonorsEachWarningPreferenceIndependently()
    {
        TestCampaign campaign = CreateCampaign();
        AddSquad(campaign, "Squad Unassigned", campaign.Region);
        AddTaskForce(campaign, 40, campaign.Planet, CreateShip(40, "Unassigned Ship"));
        campaign.Region.SpecialMissions.Add(CreateMission(campaign, MissionType.Extermination));

        EndTurnPreflightReport report = EndTurnPreflight.Evaluate(
            campaign.Sector,
            new EndTurnWarningPreferences
            {
                WarnIdleDeployableSquads = false,
                WarnActionableTaskForces = true,
                WarnSpecialMissionOpportunities = false
            });

        EndTurnAttentionItem item = Assert.Single(report.Items);
        Assert.Equal(EndTurnWarningCategory.ActionableTaskForces, item.Category);
    }

    [Fact]
    public void PreferencesRepository_RoundTripsGlobalWarningChoices()
    {
        string directory = Path.Combine(Path.GetTempPath(), "OnlyWarEndTurnPreferences", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "warnings.json");
        try
        {
            EndTurnWarningPreferencesRepository repository = new(path);
            repository.Save(new EndTurnWarningPreferences
            {
                WarnIdleDeployableSquads = false,
                WarnActionableTaskForces = true,
                WarnSpecialMissionOpportunities = false
            });

            EndTurnWarningPreferences loaded = repository.Load();

            Assert.False(loaded.WarnIdleDeployableSquads);
            Assert.True(loaded.WarnActionableTaskForces);
            Assert.False(loaded.WarnSpecialMissionOpportunities);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void PreferencesRepository_MissingFileUsesEnabledDefaults()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "warnings.json");

        EndTurnWarningPreferences loaded = new EndTurnWarningPreferencesRepository(path).Load();

        Assert.True(loaded.WarnIdleDeployableSquads);
        Assert.True(loaded.WarnActionableTaskForces);
        Assert.True(loaded.WarnSpecialMissionOpportunities);
    }

    private static TestCampaign CreateCampaign()
    {
        SquadTemplate squadTemplate = new(
            101,
            "Tactical Squad",
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            [new SquadTemplateElement(TestModelFactory.MarineTemplate, 0, 10)],
            SquadTypes.None);
        UnitTemplate unitTemplate = new(101, "Chapter", true, [squadTemplate], []);
        Faction player = BuildFaction(
            1,
            "Test Chapter",
            isPlayer: true,
            squadTemplate,
            unitTemplate);
        Unit rootUnit = new("Test Chapter", unitTemplate);
        Army army = new("Test Army", null, null, rootUnit, []);
        Fleet fleet = new("Test Fleet", null, null);
        PlayerForce playerForce = new(player, army, fleet);

        Planet planet = CreatePlanet(1, "Vigilus");
        Region region = new(1, planet, 0, "Region Primus", new RegionCoordinate(1, 1), 0f);
        planet.Regions[0] = region;
        PlanetFaction playerPlanetFaction = new(player);
        planet.PlanetFactionMap[player.Id] = playerPlanetFaction;

        Faction enemy = BuildFaction(2, "Enemy", isPlayer: false);
        PlanetFaction enemyPlanetFaction = new(enemy);
        planet.PlanetFactionMap[enemy.Id] = enemyPlanetFaction;
        RegionFaction enemyRegionFaction = new(enemyPlanetFaction, region)
        {
            IsPublic = true,
            Population = 100,
            Garrison = 100
        };
        region.RegionFactionMap[enemy.Id] = enemyRegionFaction;

        Sector sector = new(playerForce, [], [planet], []);
        return new TestCampaign(
            sector,
            playerForce,
            player,
            rootUnit,
            squadTemplate,
            planet,
            region,
            playerPlanetFaction,
            enemyRegionFaction);
    }

    private static Squad AddSquad(TestCampaign campaign, string name, Region region = null)
    {
        Squad squad = new(name, campaign.RootUnit, campaign.SquadTemplate)
        {
            CurrentRegion = region
        };
        squad.AddSquadMember(TestModelFactory.CreateSoldier(name: $"{name} Marine"));
        campaign.RootUnit.AddSquad(squad);
        if (region != null)
        {
            RegionFaction playerPresence = GetOrAddPlayerRegionFaction(campaign, region);
            playerPresence.LandedSquads.Add(squad);
        }
        return squad;
    }

    private static RegionFaction GetOrAddPlayerRegionFaction(TestCampaign campaign, Region region)
    {
        if (!region.RegionFactionMap.TryGetValue(campaign.PlayerFaction.Id, out RegionFaction presence))
        {
            presence = new RegionFaction(campaign.PlayerPlanetFaction, region)
            {
                IsPublic = true
            };
            region.RegionFactionMap[campaign.PlayerFaction.Id] = presence;
        }
        return presence;
    }

    private static TaskForce AddTaskForce(TestCampaign campaign, int id, Planet planet, params Ship[] ships)
    {
        TaskForce taskForce = new(id, campaign.PlayerFaction, planet.Position, planet, null, [.. ships]);
        campaign.Sector.AddNewFleet(taskForce);
        campaign.PlayerForce.Fleet.TaskForces.Add(taskForce);
        return taskForce;
    }

    private static Mission CreateMission(TestCampaign campaign, MissionType type)
    {
        return new Mission(type, campaign.EnemyRegionFaction, 1);
    }

    private static Ship CreateShip(int id, string name)
    {
        return new Ship(id, name, new ShipTemplate(id, "Strike Cruiser", 100, 0, 1));
    }

    private static Planet CreatePlanet(int id, string name)
    {
        return new Planet(id, name, new Coordinate((ushort)id, (ushort)id), 1, null, 1, 0);
    }

    private static Faction BuildFaction(
        int id,
        string name,
        bool isPlayer,
        SquadTemplate squadTemplate = null,
        UnitTemplate unitTemplate = null)
    {
        Dictionary<int, SquadTemplate> squads = squadTemplate == null
            ? []
            : new Dictionary<int, SquadTemplate> { [squadTemplate.Id] = squadTemplate };
        Dictionary<int, UnitTemplate> units = unitTemplate == null
            ? []
            : new Dictionary<int, UnitTemplate> { [unitTemplate.Id] = unitTemplate };
        return new Faction(
            id,
            name,
            Color.Red,
            isPlayer,
            isDefaultFaction: false,
            canInfiltrate: false,
            GrowthType.None,
            new Dictionary<int, Species> { [TestModelFactory.HumanSpecies.Id] = TestModelFactory.HumanSpecies },
            new Dictionary<int, SoldierTemplate>(),
            squads,
            units,
            new Dictionary<int, BoatTemplate>(),
            new Dictionary<int, ShipTemplate>(),
            new Dictionary<int, FleetTemplate>());
    }

    private sealed record TestCampaign(
        Sector Sector,
        PlayerForce PlayerForce,
        Faction PlayerFaction,
        Unit RootUnit,
        SquadTemplate SquadTemplate,
        Planet Planet,
        Region Region,
        PlanetFaction PlayerPlanetFaction,
        RegionFaction EnemyRegionFaction);
}
