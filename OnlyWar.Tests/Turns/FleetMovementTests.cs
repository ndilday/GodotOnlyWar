using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Microsoft.Data.Sqlite;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Turns;

public class FleetMovementTests
{
    [Fact]
    public void OrderMoveTo_RemovesTaskForceFromOriginOrbitAndMarksItInTransit()
    {
        FleetMovementFixture fixture = FleetMovementFixture.Create();

        fixture.TaskForce.OrderMoveTo(fixture.Destination, travelWeeks: 2);

        Assert.Null(fixture.TaskForce.Planet);
        Assert.Equal(fixture.Destination, fixture.TaskForce.Destination);
        Assert.Equal(2, fixture.TaskForce.TravelWeeksRemaining);
        Assert.DoesNotContain(fixture.TaskForce, fixture.Origin.OrbitingTaskForceList);
    }

    [Fact]
    public void ProcessTurn_AdvancesFleetMovementAndArrivesAfterTravelTime()
    {
        FleetMovementFixture fixture = FleetMovementFixture.Create();
        fixture.TaskForce.OrderMoveTo(fixture.Destination, travelWeeks: 2);

        fixture.ProcessTurn();

        Assert.Null(fixture.TaskForce.Planet);
        Assert.Equal(1, fixture.TaskForce.TravelWeeksRemaining);
        Assert.DoesNotContain(fixture.TaskForce, fixture.Destination.OrbitingTaskForceList);

        fixture.ProcessTurn();

        Assert.Equal(fixture.Destination, fixture.TaskForce.Planet);
        Assert.Null(fixture.TaskForce.Destination);
        Assert.Equal(0, fixture.TaskForce.TravelWeeksRemaining);
        Assert.Equal(fixture.Destination.Position, fixture.TaskForce.Position);
        Assert.Contains(fixture.TaskForce, fixture.Destination.OrbitingTaskForceList);
    }

    [Fact]
    public void OrderMoveTo_WithRouteCreatesPhasedWarpTravel()
    {
        FleetMovementFixture fixture = FleetMovementFixture.Create();
        FleetRoute route = CreateRoute(fixture.Origin, fixture.Destination, subjectiveWarpWeeks: 3, objectiveWarpWeeks: 5);

        fixture.TaskForce.OrderMoveTo(fixture.Destination, route);

        Assert.Equal(fixture.Origin, fixture.TaskForce.Origin);
        Assert.Null(fixture.TaskForce.Planet);
        Assert.Equal(FleetTravelPhase.OutboundSystemTransit, fixture.TaskForce.TravelPhase);
        Assert.Equal(2, fixture.TaskForce.CurrentPhaseWeeksRemaining);
        Assert.Equal(3, fixture.TaskForce.WarpSubjectiveWeeks);
        Assert.Equal(5, fixture.TaskForce.WarpObjectiveWeeks);
        Assert.Equal(9, fixture.TaskForce.TravelWeeksRemaining);
        Assert.False(fixture.TaskForce.WarpSubjectiveTrainingApplied);
    }

    [Fact]
    public void ProcessTurn_AdvancesPhasedTravelThroughWarpAndInboundTransit()
    {
        FleetMovementFixture fixture = FleetMovementFixture.Create();
        FleetRoute route = CreateRoute(fixture.Origin, fixture.Destination, subjectiveWarpWeeks: 3, objectiveWarpWeeks: 1);
        fixture.TaskForce.OrderMoveTo(fixture.Destination, route);

        fixture.ProcessTurn();
        Assert.Equal(FleetTravelPhase.OutboundSystemTransit, fixture.TaskForce.TravelPhase);

        fixture.ProcessTurn();
        Assert.Equal(FleetTravelPhase.InWarp, fixture.TaskForce.TravelPhase);

        fixture.ProcessTurn();
        Assert.Equal(FleetTravelPhase.InboundSystemTransit, fixture.TaskForce.TravelPhase);
        Assert.True(fixture.TaskForce.WarpSubjectiveTrainingApplied);
        Assert.Null(fixture.TaskForce.Planet);

        fixture.ProcessTurn();
        Assert.Equal(FleetTravelPhase.InboundSystemTransit, fixture.TaskForce.TravelPhase);

        fixture.ProcessTurn();
        Assert.Equal(FleetTravelPhase.InOrbit, fixture.TaskForce.TravelPhase);
        Assert.Equal(fixture.Destination, fixture.TaskForce.Planet);
        Assert.Contains(fixture.TaskForce, fixture.Destination.OrbitingTaskForceList);
    }

    [Fact]
    public void FleetDataAccess_PersistsPhasedTravelState()
    {
        FleetMovementFixture fixture = FleetMovementFixture.Create();
        FleetRoute route = CreateRoute(fixture.Origin, fixture.Destination, subjectiveWarpWeeks: 2.5, objectiveWarpWeeks: 4.5);
        fixture.TaskForce.OrderMoveTo(fixture.Destination, route);
        FleetDataAccess dataAccess = new();

        using SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = @"CREATE TABLE Fleet (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, FactionId INTEGER NOT NULL,
                x REAL NOT NULL, y REAL NOT NULL, DestinationPlanetId INTEGER, TravelWeeksRemaining INTEGER NOT NULL DEFAULT 0,
                OriginPlanetId INTEGER, TravelPhase INTEGER NOT NULL DEFAULT 0, CurrentPhaseWeeksRemaining INTEGER NOT NULL DEFAULT 0,
                WarpSubjectiveWeeks REAL NOT NULL DEFAULT 0, WarpObjectiveWeeks REAL NOT NULL DEFAULT 0,
                WarpSubjectiveTrainingApplied BOOLEAN NOT NULL DEFAULT 1);";
            command.ExecuteNonQuery();
        }

        using (var transaction = connection.BeginTransaction())
        {
            dataAccess.SaveFleet(transaction, fixture.TaskForce);
            transaction.Commit();
        }

        List<TaskForce> loadedFleets = dataAccess.GetFleetsByFactionId(
            connection,
            new Dictionary<int, List<Ship>> { [fixture.TaskForce.Id] = [] },
            new Dictionary<int, Faction> { [fixture.TaskForce.Faction.Id] = fixture.TaskForce.Faction },
            [fixture.Origin, fixture.Destination]);

        TaskForce loaded = Assert.Single(loadedFleets);
        Assert.Null(loaded.Planet);
        Assert.Equal(fixture.Origin, loaded.Origin);
        Assert.Equal(fixture.Destination, loaded.Destination);
        Assert.Equal(FleetTravelPhase.OutboundSystemTransit, loaded.TravelPhase);
        Assert.Equal(2, loaded.CurrentPhaseWeeksRemaining);
        Assert.Equal(2.5, loaded.WarpSubjectiveWeeks);
        Assert.Equal(4.5, loaded.WarpObjectiveWeeks);
        Assert.False(loaded.WarpSubjectiveTrainingApplied);
    }

    private sealed class FleetMovementFixture
    {
        public Sector Sector { get; }
        public TaskForce TaskForce { get; }
        public Planet Origin { get; }
        public Planet Destination { get; }

        private FleetMovementFixture(Sector sector, TaskForce taskForce, Planet origin, Planet destination)
        {
            Sector = sector;
            TaskForce = taskForce;
            Origin = origin;
            Destination = destination;
        }

        public static FleetMovementFixture Create()
        {
            Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
            GameRulesData rules = new();
            Faction playerFaction = CreatePlayerFaction();
            Planet origin = CreatePlanet(1, "Origin", 10, 10, playerFaction);
            Planet destination = CreatePlanet(2, "Destination", 20, 10, playerFaction);
            TaskForce taskForce = new(playerFaction)
            {
                Planet = origin,
                Position = origin.Position
            };
            Fleet fleet = new("Test Fleet", null, null);
            fleet.TaskForces.Add(taskForce);
            PlayerForce playerForce = new(playerFaction, null, fleet);
            Sector sector = new(playerForce, [], [], [taskForce]);
            GameDataSingleton.Instance.LoadGameDataFromBlob(rules, new Date(1, 1, 1), sector);
            return new FleetMovementFixture(sector, taskForce, origin, destination);
        }

        public void ProcessTurn()
        {
            new TurnController(new NoOpTrainingService()).ProcessTurn(Sector);
        }

        private static Faction CreatePlayerFaction()
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

        private static Planet CreatePlanet(int id, string name, ushort x, ushort y, Faction faction)
        {
            Planet planet = new(id, name, new Coordinate(x, y), 1, null, 1, 0);
            PlanetFaction planetFaction = new(faction);
            planet.PlanetFactionMap[faction.Id] = planetFaction;
            for (int i = 0; i < planet.Regions.Length; i++)
            {
                Region region = new(i + (id * 100), planet, 0, $"Region {i}", RegionExtensions.GetCoordinatesFromRegionNumber(i), 0);
                RegionFaction regionFaction = new(planetFaction, region)
                {
                    Population = 1000,
                    IsPublic = true
                };
                region.RegionFactionMap[faction.Id] = regionFaction;
                planet.Regions[i] = region;
            }

            return planet;
        }
    }

    private static FleetRoute CreateRoute(Planet origin, Planet destination, double subjectiveWarpWeeks, double objectiveWarpWeeks)
    {
        double totalObjectiveWeeks = 4 + objectiveWarpWeeks;
        return new FleetRoute(
            FleetRouteType.Direct,
            FleetRouteScope.SameSubsector,
            [origin, destination],
            1,
            1,
            subjectiveWarpWeeks,
            objectiveWarpWeeks,
            4 + subjectiveWarpWeeks,
            totalObjectiveWeeks,
            (int)System.Math.Ceiling(totalObjectiveWeeks),
            (int)System.Math.Floor(totalObjectiveWeeks),
            (int)System.Math.Ceiling(totalObjectiveWeeks));
    }

    private sealed class NoOpTrainingService : ISoldierTrainingService
    {
        public void UpdateRatings(Date date, PlayerSoldier soldier)
        {
        }

        public void EvaluateSoldier(PlayerSoldier soldier, Date trainingFinishedYear)
        {
        }

        public void AwardSoldier(PlayerSoldier soldier, Date awardDate, string awardName, string type, ushort level)
        {
        }

        public void ApplySoldierWorkExperience(ISoldier soldier, float points)
        {
        }

        public void TrainScouts(IEnumerable<Squad> scoutSquads, Dictionary<int, TrainingFocuses> squadFocusMap, float points = 0.2f)
        {
        }
    }
}
