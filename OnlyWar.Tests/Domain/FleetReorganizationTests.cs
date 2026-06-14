using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class FleetReorganizationTests
{
    [Fact]
    public void SplitOffNewFleet_MovesSelectedShipsIntoANewOrbitingTaskForce()
    {
        Fixture fixture = Fixture.Create(shipCount: 3);
        TaskForce original = fixture.TaskForce;
        List<Ship> toSplit = original.Ships.Take(2).ToList();

        TaskForce newFleet = fixture.Sector.SplitOffNewFleet(original, toSplit);

        Assert.Single(original.Ships);
        Assert.Equal(2, newFleet.Ships.Count);
        Assert.All(newFleet.Ships, ship => Assert.Equal(newFleet, ship.Fleet));
        Assert.Equal(fixture.Planet, newFleet.Planet);
        Assert.Contains(newFleet, fixture.Planet.OrbitingTaskForceList);
        Assert.Contains(newFleet.Id, fixture.Sector.Fleets.Keys);
    }

    [Fact]
    public void CombineFleets_FoldsMergingFleetShipsIntoRemainingFleetAndRemovesIt()
    {
        Fixture fixture = Fixture.Create(shipCount: 2);
        TaskForce remaining = fixture.TaskForce;
        // A second fleet at the same planet with an independently-built position tuple,
        // mirroring how two task forces look after a save/load round trip.
        TaskForce merging = new(fixture.Faction)
        {
            Planet = fixture.Planet,
            Position = new Tuple<ushort, ushort>(fixture.Planet.Position.Item1, fixture.Planet.Position.Item2)
        };
        merging.Ships.Add(CreateShip(99, merging));
        fixture.Sector.AddNewFleet(merging);

        fixture.Sector.CombineFleets(remaining, merging);

        Assert.Equal(3, remaining.Ships.Count);
        Assert.All(remaining.Ships, ship => Assert.Equal(remaining, ship.Fleet));
        Assert.DoesNotContain(merging.Id, fixture.Sector.Fleets.Keys);
        Assert.DoesNotContain(merging, fixture.Planet.OrbitingTaskForceList);
    }

    private static Ship CreateShip(int id, TaskForce fleet)
    {
        ShipTemplate template = new(1, "Strike Cruiser", 100, 4, 2);
        return new Ship(id, $"Ship-{id}", template) { Fleet = fleet };
    }

    private sealed class Fixture
    {
        public Sector Sector { get; }
        public TaskForce TaskForce { get; }
        public Planet Planet { get; }
        public Faction Faction { get; }

        private Fixture(Sector sector, TaskForce taskForce, Planet planet, Faction faction)
        {
            Sector = sector;
            TaskForce = taskForce;
            Planet = planet;
            Faction = faction;
        }

        public static Fixture Create(int shipCount)
        {
            Faction faction = CreateFaction();
            Planet planet = new(1, "Origin", new Tuple<ushort, ushort>(10, 10), 1, null, 1, 0);
            TaskForce taskForce = new(faction)
            {
                Planet = planet,
                Position = planet.Position
            };
            for (int i = 0; i < shipCount; i++)
            {
                taskForce.Ships.Add(CreateShip(i, taskForce));
            }
            PlayerForce playerForce = new(faction, null, new Fleet("Test Fleet", null, null));
            Sector sector = new(playerForce, [], [planet], [taskForce]);
            return new Fixture(sector, taskForce, planet, faction);
        }

        private static Faction CreateFaction()
        {
            return new Faction(
                1,
                "Fleet Test Chapter",
                Color.Blue,
                true,
                false,
                false,
                GrowthType.None,
                new Dictionary<int, Species>(),
                new Dictionary<int, SoldierTemplate>(),
                new Dictionary<int, SquadTemplate>(),
                new Dictionary<int, UnitTemplate>(),
                new Dictionary<int, BoatTemplate>(),
                new Dictionary<int, ShipTemplate>(),
                new Dictionary<int, FleetTemplate>());
        }
    }
}
