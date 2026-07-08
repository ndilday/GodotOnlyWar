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
    public void ProcessTurn_FortifyingSquadRaisesRegionDefense()
    {
        TurnTrainingFixture fixture = TurnTrainingFixture.Create();
        Squad squad = fixture.CreatePlayerSquad("Engineer Squad", out _);
        fixture.LandSquad(squad);
        fixture.RegionFaction.Entrenchment = 0;
        fixture.AssignFortifyMission(squad, DefenseType.Entrenchment);

        fixture.ProcessTurn();

        // the squad spends the turn building; even an untrained squad makes minimal progress,
        // and the construction targets only the chosen defense type
        Assert.True(fixture.RegionFaction.Entrenchment >= 1);
        Assert.Equal(0, fixture.RegionFaction.ListeningPost);
        Assert.Equal(0, fixture.RegionFaction.AntiAir);
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

    [Fact]
    public void ProcessTurn_TrainsScoutSquadsThroughScoutTraining()
    {
        TurnTrainingFixture fixture = TurnTrainingFixture.Create();
        Squad squad = fixture.CreatePlayerScoutSquad("Scout Squad", out ISoldier scout);
        fixture.LandSquad(squad);
        squad.TrainingFocus = TrainingFocuses.Ranged;

        fixture.ProcessTurn();

        Assert.Contains(squad, fixture.TrainingService.ScoutTrainingSquads);
        Assert.Equal(TrainingFocuses.Ranged, fixture.TrainingService.ScoutFocusMap[squad.Id]);
        Assert.DoesNotContain(scout, fixture.TrainingService.WorkExperienceSoldiers);
        Assert.True(GetSkillPoints(scout, TestSkills.Stealth) > 0);
    }

    [Fact]
    public void ProcessTurn_DoesNotTrainScoutSquadsAssignedToMissions()
    {
        TurnTrainingFixture fixture = TurnTrainingFixture.Create();
        Squad squad = fixture.CreatePlayerScoutSquad("Deployed Scout Squad", out ISoldier scout);
        fixture.LandSquad(squad);
        fixture.AssignDefensiveMission(squad);

        fixture.ProcessTurn();

        Assert.Contains(squad, fixture.TrainingService.ScoutTrainingSquads);
        Assert.DoesNotContain(scout, fixture.TrainingService.WorkExperienceSoldiers);
        Assert.Equal(0, GetSkillPoints(scout, TestSkills.Stealth));
    }

    [Fact]
    public void ProcessTurn_DoesNotApplyWeeklyTrainingWhileSquadIsInWarp()
    {
        TurnTrainingFixture fixture = TurnTrainingFixture.Create();
        Squad squad = fixture.CreatePlayerSquad("Warp Squad", out ISoldier soldier);
        fixture.Ship.LoadSquad(squad);
        squad.BoardedLocation = fixture.Ship;
        fixture.PutTaskForceInWarp(currentPhaseWeeksRemaining: 2, subjectiveWarpWeeks: 3);

        fixture.ProcessTurn();

        Assert.Equal(0, GetSkillPoints(soldier, TestSkills.Ranged));
    }

    [Fact]
    public void ProcessTurn_AppliesSubjectiveWarpTrainingWhenFleetExitsWarp()
    {
        TurnTrainingFixture fixture = TurnTrainingFixture.Create();
        Squad squad = fixture.CreatePlayerSquad("Emerging Warp Squad", out ISoldier soldier);
        fixture.Ship.LoadSquad(squad);
        squad.BoardedLocation = fixture.Ship;
        fixture.PutTaskForceInWarp(currentPhaseWeeksRemaining: 1, subjectiveWarpWeeks: 3);

        fixture.ProcessTurn();

        Assert.Equal(0.6f, GetSkillPoints(soldier, TestSkills.Ranged), precision: 6);
        Assert.True(fixture.TaskForce.WarpSubjectiveTrainingApplied);
        Assert.Equal(FleetTravelPhase.InboundSystemTransit, fixture.TaskForce.TravelPhase);
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
        public TaskForce TaskForce { get; }
        public Region Region { get; }
        public RegionFaction RegionFaction { get; }
        public SquadTemplate SquadTemplate { get; }
        public SquadTemplate ScoutSquadTemplate { get; }
        public SoldierTemplate SoldierTemplate { get; }
        public TestTrainingService TrainingService { get; }

        private TurnTrainingFixture(
            Sector sector,
            Ship ship,
            TaskForce taskForce,
            Region region,
            RegionFaction regionFaction,
            SquadTemplate squadTemplate,
            SquadTemplate scoutSquadTemplate,
            SoldierTemplate soldierTemplate,
            TestTrainingService trainingService)
        {
            Sector = sector;
            Ship = ship;
            TaskForce = taskForce;
            Region = region;
            RegionFaction = regionFaction;
            SquadTemplate = squadTemplate;
            ScoutSquadTemplate = scoutSquadTemplate;
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
            SquadTemplate scoutSquadTemplate = CreateScoutSquadTemplate(playerFaction);
            UnitTemplate unitTemplate = new(1, "Training Test Unit", true, [squadTemplate, scoutSquadTemplate], []);
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
                taskForce,
                region,
                regionFaction,
                squadTemplate,
                scoutSquadTemplate,
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

        public Squad CreatePlayerScoutSquad(string name, out ISoldier scout)
        {
            Soldier leaderBaseSoldier = TestModelFactory.CreateSoldier(TestModelFactory.SergeantTemplate);
            PlayerSoldier leader = new(leaderBaseSoldier, name + " Sergeant");
            Soldier scoutBaseSoldier = TestModelFactory.CreateSoldier(SoldierTemplate);
            PlayerSoldier playerScout = new(scoutBaseSoldier, name + " Scout");
            Squad squad = new(name, Sector.PlayerForce.Army.OrderOfBattle, ScoutSquadTemplate);
            squad.AddSquadMember(leader);
            squad.AddSquadMember(playerScout);
            Sector.PlayerForce.Army.OrderOfBattle.AddSquad(squad);
            _soldiers.Add(leader);
            _soldiers.Add(playerScout);
            _squads.Add(squad);
            scout = playerScout;
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

        public void AssignFortifyMission(Squad squad, DefenseType defenseType)
        {
            ConstructionMission mission = new(defenseType, 0, RegionFaction);
            Order order = new([squad], Disposition.DugIn, true, false, Aggression.Avoid, mission);
            squad.CurrentOrders = order;
            Sector.AddNewOrder(order);
        }

        public void PutTaskForceInWarp(int currentPhaseWeeksRemaining, double subjectiveWarpWeeks)
        {
            Planet destination = new(2, "Training Destination", new Coordinate(2, 1), 1, null, 1, 0);
            TaskForce.Origin = TaskForce.Planet;
            TaskForce.Destination = destination;
            TaskForce.Planet.OrbitingTaskForceList.Remove(TaskForce);
            TaskForce.Planet = null;
            TaskForce.TravelPhase = FleetTravelPhase.InWarp;
            TaskForce.CurrentPhaseWeeksRemaining = currentPhaseWeeksRemaining;
            TaskForce.TravelWeeksRemaining = currentPhaseWeeksRemaining + OnlyWar.Models.Fleets.TaskForce.SystemTransitWeeksPerEnd;
            TaskForce.WarpSubjectiveWeeks = subjectiveWarpWeeks;
            TaskForce.WarpObjectiveWeeks = currentPhaseWeeksRemaining;
            TaskForce.WarpSubjectiveTrainingApplied = false;
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
                SquadTypes.None);
            squadTemplate.Faction = playerFaction;
            return squadTemplate;
        }

        private static SquadTemplate CreateScoutSquadTemplate(Faction playerFaction)
        {
            SquadTemplate squadTemplate = new(
                2,
                "Turn Training Scout Squad",
                TestModelFactory.DefaultWeapons,
                [],
                TestModelFactory.TestArmor,
                [new SquadTemplateElement(TestModelFactory.SergeantTemplate, 1, 1), new SquadTemplateElement(TestModelFactory.MarineTemplate, 1, 10)],
                SquadTypes.Scout);
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
                new LogNormalValueTemplate { Floor = 1000, Scale = 0 },
                new LogNormalValueTemplate { Floor = 2000, Scale = 0 },
                new NormalizedValueTemplate { BaseValue = 1, StandardDeviation = 0 },
                new LinearValueTemplate { MinValue = 0, MaxValue = 0 });
            Planet planet = new(1, "Training Test World", new Coordinate(1, 1), 1, template, 1, 0);

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
        public List<ISoldier> WorkExperienceSoldiers { get; } = [];
        public List<Squad> ScoutTrainingSquads { get; } = [];
        public Dictionary<int, TrainingFocuses> ScoutFocusMap { get; private set; } = [];

        public void UpdateRatings(Date date, PlayerSoldier soldier)
        {
        }

        public void EvaluateSoldier(PlayerSoldier soldier, Date trainingFinishedYear)
        {
        }

        public void ApplySoldierWorkExperience(ISoldier soldier, Squad squad, float points)
        {
            WorkExperienceSoldiers.Add(soldier);
            soldier.AddSkillPoints(TestSkills.Ranged, points);
        }

        public void TrainScouts(IEnumerable<Squad> scoutSquads, Dictionary<int, TrainingFocuses> squadFocusMap, float points = 0.2f)
        {
            ScoutFocusMap = squadFocusMap;
            ScoutTrainingSquads.AddRange(scoutSquads);
            foreach (ISoldier soldier in scoutSquads.Where(s => s.CurrentOrders == null).SelectMany(s => s.Members))
            {
                soldier.AddSkillPoints(TestSkills.Stealth, points);
            }
        }
    }
}
