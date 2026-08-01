using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Orders;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Orders;

// Exercises OrderAssignment.AssignSquadsToMission - the pure-logic extraction of
// OrderDialogController.OnOrdersConfirmed's Mission-construction and Order-creation logic,
// generalized to accept more than one squad (future multi-squad operations board).
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class OrderAssignmentTests
{
    [Fact]
    public void AssignSquadsToMission_SingleSquadAttack_CreatesOrderAgainstSelectedEnemy()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction enemy = fixture.AddControllingFaction(5, "Orks", 5000);
        Region targetRegion = fixture.Planet.Regions[5];
        Region originRegion = fixture.Planet.Regions[0];

        Squad squad = TestModelFactory.CreateSquad("Test Squad One", TestModelFactory.CreateSoldier());
        squad.CurrentRegion = originRegion;

        AvailableMission attackMission = new("Attack", MissionAvailabilityKind.Attack);

        Order order = OrderAssignment.AssignSquadsToMission(
            new List<Squad> { squad }, targetRegion, attackMission, enemy.PlanetFaction.Faction.Id, Aggression.Normal);

        Assert.NotNull(order);
        Assert.Equal(MissionType.Advance, order.Mission.MissionType);
        Assert.Same(enemy, order.Mission.RegionFaction);
        Assert.Single(order.AssignedSquads);
        Assert.Same(squad, order.AssignedSquads[0]);
        Assert.Same(order, squad.CurrentOrders);
    }

    [Fact]
    public void AssignSquadsToMission_FactionSpecificAttackUsesButtonTarget()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction enemy = fixture.AddControllingFaction(5, "Orks", 5000);
        Squad squad = TestModelFactory.CreateSquad(
            "Test Squad", TestModelFactory.CreateSoldier());
        AvailableMission attack = new(
            "Attack (Orks)",
            MissionAvailabilityKind.Attack,
            targetFaction: enemy);

        Order order = OrderAssignment.AssignSquadsToMission(
            [squad], enemy.Region, attack, targetFactionId: -1,
            aggression: Aggression.Normal);

        Assert.NotNull(order);
        Assert.Same(enemy, order.Mission.RegionFaction);
    }

    [Fact]
    public void AssignSquadsToMission_VanishedAttackTargetDoesNotBecomeMove()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction enemy = fixture.AddControllingFaction(5, "Orks", 5000);
        Squad squad = TestModelFactory.CreateSquad(
            "Test Squad", TestModelFactory.CreateSoldier());
        AvailableMission attack = new(
            "Attack (Orks)",
            MissionAvailabilityKind.Attack,
            targetFaction: enemy);
        enemy.Region.RegionFactionMap.Remove(enemy.PlanetFaction.Faction.Id);

        Order order = OrderAssignment.AssignSquadsToMission(
            [squad], enemy.Region, attack, targetFactionId: -1,
            aggression: Aggression.Normal);

        Assert.Null(order);
        Assert.Null(squad.CurrentOrders);
    }

    [Fact]
    public void AssignSquadsToMission_TwoSquads_ShareASingleOrder()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction enemy = fixture.AddControllingFaction(5, "Orks", 5000);
        Region targetRegion = fixture.Planet.Regions[5];
        Region originRegion = fixture.Planet.Regions[0];

        Squad squadOne = TestModelFactory.CreateSquad("Test Squad One", TestModelFactory.CreateSoldier());
        Squad squadTwo = TestModelFactory.CreateSquad("Test Squad Two", TestModelFactory.CreateSoldier());
        squadOne.CurrentRegion = originRegion;
        squadTwo.CurrentRegion = originRegion;

        AvailableMission attackMission = new("Attack", MissionAvailabilityKind.Attack);

        Order order = OrderAssignment.AssignSquadsToMission(
            new List<Squad> { squadOne, squadTwo }, targetRegion, attackMission,
            enemy.PlanetFaction.Faction.Id, Aggression.Normal);

        Assert.NotNull(order);
        Assert.Equal(2, order.AssignedSquads.Count);
        Assert.Contains(squadOne, order.AssignedSquads);
        Assert.Contains(squadTwo, order.AssignedSquads);
        Assert.Same(order, squadOne.CurrentOrders);
        Assert.Same(order, squadTwo.CurrentOrders);
    }

    [Fact]
    public void AssignSquadsToMission_ExistingPatrol_ReusesOrderAndUpdatesAggression()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region targetRegion = fixture.Planet.Regions[0];
        fixture.Planet.PlanetFactionMap[fixture.Sector.PlayerForce.Faction.Id] =
            new PlanetFaction(fixture.Sector.PlayerForce.Faction) { IsPublic = true };
        AvailableMission patrol = new("Patrol", MissionAvailabilityKind.Patrol);
        Squad first = TestModelFactory.CreateSquad(
            "First Patrol", TestModelFactory.CreateSoldier());
        Squad reinforcement = TestModelFactory.CreateSquad(
            "Patrol Reinforcement", TestModelFactory.CreateSoldier());
        first.CurrentRegion = targetRegion;
        reinforcement.CurrentRegion = targetRegion;

        Order original = OrderAssignment.AssignSquadsToMission(
            [first], targetRegion, patrol, -1, Aggression.Cautious);
        Mission originalMission = original.Mission;
        Order reused = OrderAssignment.AssignSquadsToMission(
            [reinforcement], targetRegion, patrol, -1, Aggression.Aggressive);

        Assert.Same(original, reused);
        Assert.Same(originalMission, reused.Mission);
        Assert.Equal(Aggression.Aggressive, reused.LevelOfAggression);
        Assert.Equal(2, reused.AssignedSquads.Count);
        Assert.Same(reused, first.CurrentOrders);
        Assert.Same(reused, reinforcement.CurrentOrders);
        Assert.Single(fixture.Sector.Orders);
    }

    [Fact]
    public void AssignSquadsToMission_SameSquadAndMission_UpdatesAggression()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region targetRegion = fixture.Planet.Regions[0];
        fixture.Planet.PlanetFactionMap[fixture.Sector.PlayerForce.Faction.Id] =
            new PlanetFaction(fixture.Sector.PlayerForce.Faction) { IsPublic = true };
        AvailableMission patrol = new("Patrol", MissionAvailabilityKind.Patrol);
        Squad squad = TestModelFactory.CreateSquad(
            "Existing Patrol", TestModelFactory.CreateSoldier());
        squad.CurrentRegion = targetRegion;

        Order original = OrderAssignment.AssignSquadsToMission(
            [squad], targetRegion, patrol, -1, Aggression.Cautious);
        Order updated = OrderAssignment.AssignSquadsToMission(
            [squad], targetRegion, patrol, -1, Aggression.Attritional);

        Assert.Same(original, updated);
        Assert.Equal(Aggression.Attritional, updated.LevelOfAggression);
        Assert.Same(updated, squad.CurrentOrders);
        Assert.Single(updated.AssignedSquads);
        Assert.Single(fixture.Sector.Orders);
    }

    [Fact]
    public void AssignSquadsToMission_DifferentSpecialMissionIds_RemainSeparateOrders()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction enemy = fixture.AddPublicCult(
            region: 0, population: 2_000, organization: 100);
        Mission firstMission = new(MissionType.Ambush, enemy, missionSize: 1);
        Mission secondMission = new(MissionType.Ambush, enemy, missionSize: 1);
        enemy.Region.SpecialMissions.Add(firstMission);
        enemy.Region.SpecialMissions.Add(secondMission);
        Squad first = TestModelFactory.CreateSquad(
            "First Squad", TestModelFactory.CreateSoldier());
        Squad second = TestModelFactory.CreateSquad(
            "Second Squad", TestModelFactory.CreateSoldier());

        Order firstOrder = OrderAssignment.AssignSquadsToMission(
            [first], enemy.Region,
            new AvailableMission("Ambush A", MissionAvailabilityKind.Special, firstMission),
            -1, Aggression.Normal);
        Order secondOrder = OrderAssignment.AssignSquadsToMission(
            [second], enemy.Region,
            new AvailableMission("Ambush B", MissionAvailabilityKind.Special, secondMission),
            -1, Aggression.Normal);

        Assert.NotSame(firstOrder, secondOrder);
        Assert.Equal(2, fixture.Sector.Orders.Count);
        Assert.Same(firstMission, firstOrder.Mission);
        Assert.Same(secondMission, secondOrder.Mission);
    }

    [Fact]
    public void AssignSquadsToMission_AttacksAgainstDifferentFactions_RemainSeparateOrders()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction orks = fixture.AddControllingFaction(5, "Orks", 5_000);
        RegionFaction cult = fixture.AddPublicCult(
            region: 5, population: 2_000, organization: 100);
        Region targetRegion = fixture.Planet.Regions[5];
        AvailableMission attack = new("Attack", MissionAvailabilityKind.Attack);
        Squad first = TestModelFactory.CreateSquad(
            "Ork Hunters", TestModelFactory.CreateSoldier());
        Squad second = TestModelFactory.CreateSquad(
            "Cult Hunters", TestModelFactory.CreateSoldier());

        Order orkOrder = OrderAssignment.AssignSquadsToMission(
            [first], targetRegion, attack,
            orks.PlanetFaction.Faction.Id, Aggression.Normal);
        Order cultOrder = OrderAssignment.AssignSquadsToMission(
            [second], targetRegion, attack,
            cult.PlanetFaction.Faction.Id, Aggression.Normal);

        Assert.NotSame(orkOrder, cultOrder);
        Assert.Equal(2, fixture.Sector.Orders.Count);
        Assert.Same(orks, orkOrder.Mission.RegionFaction);
        Assert.Same(cult, cultOrder.Mission.RegionFaction);
    }

    [Fact]
    public void AssignSquadsToMission_AdministrativeSquad_IsRejected()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction enemy = fixture.AddControllingFaction(5, "Orks", 5000);
        Squad squad = TestModelFactory.CreateSquad(
            "10th Company HQ", TestModelFactory.CreateSoldier());
        squad.IsAdministrative = true;

        Order order = OrderAssignment.AssignSquadsToMission(
            [squad],
            fixture.Planet.Regions[5],
            new AvailableMission("Attack", MissionAvailabilityKind.Attack),
            enemy.PlanetFaction.Faction.Id,
            Aggression.Normal);

        Assert.Null(order);
        Assert.Null(squad.CurrentOrders);
    }

    [Fact]
    public void UnassignSquads_OneOfTwoSquads_UpdatesInboundOrderCount()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction enemy = fixture.AddControllingFaction(5, "Orks", 5000);
        Region targetRegion = fixture.Planet.Regions[5];
        Region originRegion = fixture.Planet.Regions[0];
        Squad remaining = TestModelFactory.CreateSquad(
            "Remaining Squad", TestModelFactory.CreateSoldier());
        Squad unassigned = TestModelFactory.CreateSquad(
            "Unassigned Squad", TestModelFactory.CreateSoldier());
        remaining.CurrentRegion = originRegion;
        unassigned.CurrentRegion = originRegion;

        Order order = OrderAssignment.AssignSquadsToMission(
            [remaining, unassigned],
            targetRegion,
            new AvailableMission("Attack", MissionAvailabilityKind.Attack),
            enemy.PlanetFaction.Faction.Id,
            Aggression.Normal);

        bool changed = OrderAssignment.UnassignSquads([unassigned]);
        InboundOrderInfo inbound = Assert.Single(InboundOrders.ForRegion(targetRegion));

        Assert.True(changed);
        Assert.Null(unassigned.CurrentOrders);
        Assert.Same(order, remaining.CurrentOrders);
        Assert.Single(order.AssignedSquads);
        Assert.Same(remaining, order.AssignedSquads[0]);
        Assert.Equal(1, inbound.SquadCount);
        Assert.Contains("1 squad", inbound.SummaryLabel);
    }
}
