using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Turns;

public class TurnTrainingTests
{
    [Fact]
    public void ProcessTurn_TrainsSoldiersLoadedOnShips()
    {
        TurnTrainingFixture fixture = TurnTrainingFixture.Create();
        Squad squad = fixture.CreatePlayerSquad("Loaded Squad", out ISoldier soldier);
        fixture.Ship.LoadSquad(squad);
        squad.BoardedLocation = fixture.Ship;

        fixture.ProcessTurn();

        Assert.True(GetSkillPoints(soldier, TestSkills.Ranged) > 0);
    }

    [Fact]
    public void ProcessTurn_TrainsLandedSoldiersWithoutMissions()
    {
        TurnTrainingFixture fixture = TurnTrainingFixture.Create();
        Squad squad = fixture.CreatePlayerSquad("Idle Landed Squad", out ISoldier soldier);
        fixture.LandSquad(squad);

        fixture.ProcessTurn();

        Assert.True(GetSkillPoints(soldier, TestSkills.Ranged) > 0);
    }

    [Fact]
    public void ProcessTurn_DoesNotTrainSoldiersAssignedToMissions()
    {
        TurnTrainingFixture fixture = TurnTrainingFixture.Create();
        Squad squad = fixture.CreatePlayerSquad("Mission Squad", out ISoldier soldier);
        fixture.LandSquad(squad);
        fixture.AssignDefensiveMission(squad);

        fixture.ProcessTurn();

        Assert.Equal(0, GetSkillPoints(soldier, TestSkills.Ranged));
    }

    private static float GetSkillPoints(ISoldier soldier, BaseSkill skill)
    {
        return soldier.Skills.SingleOrDefault(s => s.BaseSkill == skill)?.PointsInvested ?? 0;
    }

    private sealed class TurnTrainingFixture
    {
        private readonly List<PlayerSoldier> _soldiers = [];
        private readonly List<Squad> _squads = [];

        public Sector Sector { get; }
        public Ship Ship { get; }
        public Region Region { get; }
        public RegionFaction RegionFaction { get; }
        public SquadTemplate SquadTemplate { get; }
        public SoldierTemplate SoldierTemplate { get; }
        public TestTrainingService TrainingService { get; }

        private TurnTrainingFixture(
            Sector sector,
            Ship ship,
            Region region,
            RegionFaction regionFaction,
            SquadTemplate squadTemplate,
            SoldierTemplate soldierTemplate,
            TestTrainingService trainingService)
        {
            Sector = sector;
            Ship = ship;
            Region = region;
            RegionFaction = regionFaction;
            SquadTemplate = squadTemplate;
            SoldierTemplate = soldierTemplate;
            TrainingService = trainingService;
        }

        public static TurnTrainingFixture Create()
        {
            Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
            GameRulesData rules = new();
            Faction playerFaction = CreatePlayerFaction();
            SoldierTemplate soldierTemplate = CreateTrainingSoldierTemplate();
            SquadTemplate squadTemplate = CreateSquadTemplate(playerFaction);
            UnitTemplate unitTemplate = new(1, "Training Test Unit", true, [squadTemplate], []);
            unitTemplate.Faction = playerFaction;
            Unit orderOfBattle = new(1, "Training Test Force", unitTemplate, []);
            Fleet fleet = new("Training Test Fleet", null, null);

            Planet planet = CreatePlanet();
            PlanetFaction planetFaction = new(playerFaction);
            planet.PlanetFactionMap[playerFaction.Id] = planetFaction;
            Region region = planet.Regions[0];
            RegionFaction regionFaction = new(planetFaction, region)
            {
                Population = 1000
            };
            region.RegionFactionMap[playerFaction.Id] = regionFaction;

            Ship ship = new(1, "Training Test Ship", new ShipTemplate(1, "Training Ship", 200, 0, 0));
            TaskForce taskForce = new(playerFaction)
            {
                Planet = planet,
                Position = planet.Position
            };
            taskForce.Ships.Add(ship);
            ship.Fleet = taskForce;
            fleet.TaskForces.Add(taskForce);

            PlayerForce playerForce = new(playerFaction, new Army("Training Test Army", null, null, orderOfBattle, []), fleet);
            Sector sector = new(playerForce, [], [], [taskForce]);
            GameDataSingleton.Instance.LoadGameDataFromBlob(rules, new Date(1, 1, 1), sector);

            return new TurnTrainingFixture(
                sector,
                ship,
                region,
                regionFaction,
                squadTemplate,
                soldierTemplate,
                new TestTrainingService());
        }

        public Squad CreatePlayerSquad(string name, out ISoldier soldier)
        {
            Soldier baseSoldier = TestModelFactory.CreateSoldier(SoldierTemplate);
            PlayerSoldier playerSoldier = new(baseSoldier, name + " Marine");
            Squad squad = new(name, Sector.PlayerForce.Army.OrderOfBattle, SquadTemplate);
            squad.AddSquadMember(playerSoldier);
            Sector.PlayerForce.Army.OrderOfBattle.AddSquad(squad);
            _soldiers.Add(playerSoldier);
            _squads.Add(squad);
            soldier = playerSoldier;
            return squad;
        }

        public void LandSquad(Squad squad)
        {
            squad.CurrentRegion = Region;
            RegionFaction.LandedSquads.Add(squad);
        }

        public void AssignDefensiveMission(Squad squad)
        {
            Mission mission = new(MissionType.DefenseInDepth, RegionFaction, 0);
            Order order = new([squad], Disposition.DugIn, true, false, Aggression.Avoid, mission);
            squad.CurrentOrders = order;
            Sector.AddNewOrder(order);
        }

        public void ProcessTurn()
        {
            new TurnController(TrainingService).ProcessTurn(Sector);
        }

        private static SoldierTemplate CreateTrainingSoldierTemplate()
        {
            TrainingProfile profile = new(
                1,
                "turn_training_test",
                [new TrainingProfileEntry(TestSkills.Ranged, 1)]);

            return new SoldierTemplate(
                1,
                TestModelFactory.HumanSpecies,
                "Turn Training Marine",
                1,
                1,
                false,
                0,
                Array.Empty<Tuple<BaseSkill, float>>(),
                profile);
        }

        private static SquadTemplate CreateSquadTemplate(Faction playerFaction)
        {
            SquadTemplate squadTemplate = new(
                1,
                "Turn Training Squad",
                TestModelFactory.DefaultWeapons,
                [],
                TestModelFactory.TestArmor,
                [new SquadTemplateElement(TestModelFactory.MarineTemplate, 0, 10)],
                SquadTypes.None,
                10);
            squadTemplate.Faction = playerFaction;
            return squadTemplate;
        }

        private static Faction CreatePlayerFaction()
        {
            return new Faction(
                1,
                "Training Test Chapter",
                Color.Blue,
                true,
                false,
                false,
                GrowthType.None,
                new Dictionary<int, Species> { [TestModelFactory.HumanSpecies.Id] = TestModelFactory.HumanSpecies },
                new Dictionary<int, SoldierTemplate>(),
                new Dictionary<int, SquadTemplate>(),
                new Dictionary<int, UnitTemplate>(),
                new Dictionary<int, BoatTemplate>(),
                new Dictionary<int, ShipTemplate>(),
                new Dictionary<int, FleetTemplate>());
        }

        private static Planet CreatePlanet()
        {
            PlanetTemplate template = new(
                1,
                "Training Test World",
                1,
                new NormalizedValueTemplate { BaseValue = 1000, StandardDeviation = 0 },
                new NormalizedValueTemplate { BaseValue = 1, StandardDeviation = 0 },
                new LinearValueTemplate { MinValue = 0, MaxValue = 0 });
            Planet planet = new(1, "Training Test World", new Tuple<ushort, ushort>(1, 1), 1, template, 1, 0);

            for (int i = 0; i < planet.Regions.Length; i++)
            {
                planet.Regions[i] = new Region(
                    i,
                    planet,
                    0,
                    $"Training Region {i}",
                    RegionExtensions.GetCoordinatesFromRegionNumber(i),
                    0);
            }

            return planet;
        }
    }

    private sealed class TestTrainingService : ISoldierTrainingService
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
            soldier.AddSkillPoints(TestSkills.Ranged, points);
        }

        public void TrainScouts(IEnumerable<Squad> scoutSquads, Dictionary<int, TrainingFocuses> squadFocusMap)
        {
        }
    }
}
