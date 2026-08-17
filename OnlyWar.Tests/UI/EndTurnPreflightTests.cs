using OnlyWar.Helpers.Settings;
using OnlyWar.Helpers.Command;
using OnlyWar.Helpers.Turns;
using OnlyWar.Models;
using OnlyWar.Models.Command;
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
    public void Evaluate_LeaderlessScoutSquadNamesTheTrainingCost()
    {
        TestCampaign campaign = CreateCampaign();
        Squad orphaned = AddTemplatedSquad(
            campaign, "Odovocar Squad", campaign.ScoutSquadTemplate, withLeader: false, memberCount: 7);

        EndTurnPreflightReport report = EndTurnPreflight.Evaluate(campaign.Sector, LeaderlessOnly());

        EndTurnAttentionItem item = Assert.Single(report.Items);
        Assert.Equal(EndTurnWarningCategory.LeaderlessSquads, item.Category);
        Assert.Equal(orphaned.Id, item.EntityId);
        Assert.Contains("Odovocar Squad", item.Title);
        Assert.Contains("7 brothers", item.Detail);
        Assert.Contains("Test Sergeant", item.Detail);
        Assert.Contains("three-quarter rate", item.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_LeaderlessLineSquadNamesTheMissionAndCommandCostInstead()
    {
        TestCampaign campaign = CreateCampaign();
        AddTemplatedSquad(campaign, "Wayn Squad", campaign.LedSquadTemplate, withLeader: false);

        EndTurnPreflightReport report = EndTurnPreflight.Evaluate(campaign.Sector, LeaderlessOnly());

        EndTurnAttentionItem item = Assert.Single(report.Items);
        Assert.Contains("1 brother ", item.Detail);
        Assert.Contains("mission checks", item.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("three-quarter rate", item.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_DoesNotReportSquadsThatAreLedEmptyOrLeaderlessByTemplate()
    {
        TestCampaign campaign = CreateCampaign();
        AddTemplatedSquad(campaign, "Led Squad", campaign.LedSquadTemplate, withLeader: true);
        // A template with no leader element (e.g. a Ravener pack) is not missing anything.
        AddSquad(campaign, "Leaderless By Design", campaign.Region);
        // An empty squad kept alive for later staffing has nobody to lead.
        Squad empty = new("Empty Reserve", campaign.RootUnit, campaign.ScoutSquadTemplate)
        {
            CurrentRegion = campaign.Region
        };
        campaign.RootUnit.AddSquad(empty);

        EndTurnPreflightReport report = EndTurnPreflight.Evaluate(campaign.Sector, LeaderlessOnly());

        Assert.Empty(report.Items);
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
        campaign.PlayerPlanetFaction.SetRegionAwareness(campaign.Region, 3f);
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
    public void Evaluate_SpecialMissionWithIntelBelowOneExplainsItWillBeCleared()
    {
        TestCampaign campaign = CreateCampaign();
        campaign.PlayerPlanetFaction.SetRegionAwareness(campaign.Region, 0.5f);
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
        Assert.Contains("intelligence is below 1", item.Detail, StringComparison.OrdinalIgnoreCase);
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
    public void BriefRetainsEveryAttentionFactWhenPreflightPreferencesSuppressInterruption()
    {
        TestCampaign campaign = CreateCampaign();
        AddSquad(campaign, "Squad Unassigned", campaign.Region);
        AddTaskForce(campaign, 41, campaign.Planet, CreateShip(41, "Unassigned Ship"));
        campaign.Region.SpecialMissions.Add(CreateMission(campaign, MissionType.Extermination));

        EndTurnWarningPreferences disabled = new()
        {
            WarnIdleDeployableSquads = false,
            WarnLeaderlessSquads = false,
            WarnActionableTaskForces = false,
            WarnSpecialMissionOpportunities = false,
            WarnRecruitmentProgram = false
        };
        IReadOnlyList<CommandAttentionFact> facts = EndTurnPreflight.EvaluateFacts(campaign.Sector);
        EndTurnPreflightReport report = EndTurnPreflight.Evaluate(campaign.Sector, disabled);
        CommandBriefModel brief = new CommandBriefBuilder().Build(
            new Date(1, 1, 1),
            campaign.Sector,
            null,
            null);

        Assert.Empty(report.Items);
        Assert.NotEmpty(facts);
        Assert.All(facts, fact => Assert.Contains(
            brief.Items,
            item => item.StableKey == fact.StableKey));
    }

    // Design/Reference/SpecialistAttachment.md §7.3. Two ways a formation stops being "idle":
    // it is a personnel pool that never takes orders of its own, or it has a member forward.
    [Fact]
    public void Evaluate_DoesNotFlagAFormationWhoseTemplatePermitsDetachmentAsIdle()
    {
        TestCampaign campaign = CreateCampaign();
        SquadTemplate poolTemplate = new(
            902,
            "Apothecarion",
            campaign.SquadTemplate.DefaultWeapons,
            [],
            campaign.SquadTemplate.Armor,
            [.. campaign.SquadTemplate.Elements],
            SquadTypes.PermitsIndividualDetachment)
        {
            Faction = campaign.PlayerFaction
        };
        Squad pool = new("Apothecarion", campaign.RootUnit, poolTemplate)
        {
            CurrentRegion = campaign.Region
        };
        pool.AddSquadMember(TestModelFactory.CreateSoldier(name: "Brother Apothecary"));
        campaign.RootUnit.AddSquad(pool);
        GetOrAddPlayerRegionFaction(campaign, campaign.Region).LandedSquads.Add(pool);

        EndTurnPreflightReport report = EndTurnPreflight.Evaluate(
            campaign.Sector,
            new EndTurnWarningPreferences
            {
                WarnActionableTaskForces = false,
                WarnSpecialMissionOpportunities = false,
                WarnLeaderlessSquads = false
            });

        Assert.DoesNotContain(report.Items, item => item.EntityId == pool.Id);
        // ...and it stays operational, which surgery staffing and recruitment depend on.
        Assert.True(pool.IsOperational);
    }

    [Fact]
    public void Evaluate_DoesNotFlagASquadWithAMemberAttachedToAnOperation()
    {
        TestCampaign campaign = CreateCampaign();
        Squad ordered = AddSquad(campaign, "Squad Vigilant", campaign.Region);
        Order order = new(
            [ordered], false, false, Aggression.Normal,
            CreateMission(campaign, MissionType.Patrol));

        Squad lender = new("Armory", campaign.RootUnit, campaign.SquadTemplate)
        {
            CurrentRegion = campaign.Region
        };
        PlayerSoldier lent = new(
            TestModelFactory.CreateSoldier(name: "Brother Techmarine"), "Brother Techmarine");
        lender.AddSquadMember(lent);
        campaign.RootUnit.AddSquad(lender);
        GetOrAddPlayerRegionFaction(campaign, campaign.Region).LandedSquads.Add(lender);
        OnlyWar.Helpers.Orders.OrderAttachment.Attach(lent, order);

        EndTurnPreflightReport report = EndTurnPreflight.Evaluate(
            campaign.Sector,
            new EndTurnWarningPreferences
            {
                WarnActionableTaskForces = false,
                WarnSpecialMissionOpportunities = false,
                WarnLeaderlessSquads = false
            });

        Assert.DoesNotContain(report.Items, item => item.EntityId == lender.Id);
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
                WarnLeaderlessSquads = false,
                WarnActionableTaskForces = true,
                WarnSpecialMissionOpportunities = false
            });

            EndTurnWarningPreferences loaded = repository.Load();

            Assert.False(loaded.WarnIdleDeployableSquads);
            Assert.False(loaded.WarnLeaderlessSquads);
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
        Assert.True(loaded.WarnLeaderlessSquads);
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
        // Two templates that actually call for a leader, so the leaderless-squad warning has
        // something to fire on; squadTemplate above deliberately defines no leader slot.
        SquadTemplate ledSquadTemplate = BuildLedTemplate(102, "Led Squad", SquadTypes.None);
        SquadTemplate scoutSquadTemplate = BuildLedTemplate(103, "Scout Squad", SquadTypes.Scout);
        UnitTemplate unitTemplate = new(101, "Chapter", true, [squadTemplate], []);
        Faction player = BuildFaction(
            1,
            "Test Chapter",
            isPlayer: true,
            [squadTemplate, ledSquadTemplate, scoutSquadTemplate],
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
            ledSquadTemplate,
            scoutSquadTemplate,
            planet,
            region,
            playerPlanetFaction,
            enemyRegionFaction);
    }

    private static SquadTemplate BuildLedTemplate(int id, string name, SquadTypes squadTypes)
    {
        return new SquadTemplate(
            id,
            name,
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            [
                new SquadTemplateElement(TestModelFactory.SergeantTemplate, 0, 1),
                new SquadTemplateElement(TestModelFactory.MarineTemplate, 0, 9)
            ],
            squadTypes);
    }

    private static Squad AddTemplatedSquad(
        TestCampaign campaign,
        string name,
        SquadTemplate template,
        bool withLeader,
        int memberCount = 1)
    {
        Squad squad = new(name, campaign.RootUnit, template)
        {
            CurrentRegion = campaign.Region
        };
        if (withLeader)
        {
            squad.AddSquadMember(TestModelFactory.CreateSoldier(
                TestModelFactory.SergeantTemplate, $"{name} Sergeant"));
        }
        for (int i = 0; i < memberCount; i++)
        {
            squad.AddSquadMember(TestModelFactory.CreateSoldier(name: $"{name} Marine {i}"));
        }
        campaign.RootUnit.AddSquad(squad);
        GetOrAddPlayerRegionFaction(campaign, campaign.Region).LandedSquads.Add(squad);
        return squad;
    }

    // The leaderless check is orthogonal to the other categories, and a landed squad with no
    // orders would otherwise also trip the idle-squad warning.
    private static EndTurnWarningPreferences LeaderlessOnly()
    {
        return new EndTurnWarningPreferences
        {
            WarnIdleDeployableSquads = false,
            WarnActionableTaskForces = false,
            WarnSpecialMissionOpportunities = false
        };
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
        IEnumerable<SquadTemplate> squadTemplates = null,
        UnitTemplate unitTemplate = null)
    {
        Dictionary<int, SquadTemplate> squads = squadTemplates == null
            ? []
            : squadTemplates.ToDictionary(template => template.Id);
        Dictionary<int, UnitTemplate> units = unitTemplate == null
            ? []
            : new Dictionary<int, UnitTemplate> { [unitTemplate.Id] = unitTemplate };
        return new Faction(
            id,
            name,
            Color.Red,
            isPlayer,
            isDefaultFaction: false,
            behavior: FactionBehavior.None,
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
        SquadTemplate LedSquadTemplate,
        SquadTemplate ScoutSquadTemplate,
        Planet Planet,
        Region Region,
        PlanetFaction PlayerPlanetFaction,
        RegionFaction EnemyRegionFaction);
}
