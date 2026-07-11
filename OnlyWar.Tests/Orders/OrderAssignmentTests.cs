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
}
