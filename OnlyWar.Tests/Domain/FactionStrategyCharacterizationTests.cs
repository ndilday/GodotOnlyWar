using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Strategy;
using OnlyWar.Models;
using OnlyWar.Models.FactionBehaviors;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Xunit;

namespace OnlyWar.Tests.Domain;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public sealed class FactionStrategyCharacterizationTests
{
    [Fact]
    public void GenerateFactionOrders_OnlyPlanetStillCleansTransientSquadsAcrossTheSector()
    {
        Faction faction = CreateFaction(2, "Test Cult");
        Planet selectedPlanet = CreatePlanet(1, "Selected");
        Planet unselectedPlanet = CreatePlanet(2, "Unselected");
        RegionFaction selectedFaction = AddRegionFaction(selectedPlanet, selectedPlanet.Regions[0], faction, 0);
        RegionFaction unselectedFaction = AddRegionFaction(unselectedPlanet, unselectedPlanet.Regions[0], faction, 0);
        Squad selectedPatrol = AddLandedSquad(selectedFaction, MissionType.Patrol);
        Squad unselectedRecon = AddLandedSquad(unselectedFaction, MissionType.Recon);
        Squad unselectedGarrison = AddLandedSquad(unselectedFaction, MissionType.DefenseInDepth);
        Sector sector = new(CreatePlayerForce(), [], [selectedPlanet, unselectedPlanet], []);

        List<Order> orders = new FactionStrategyController()
            .GenerateFactionOrders(faction, sector, selectedPlanet);

        Assert.Empty(orders);
        Assert.DoesNotContain(selectedPatrol, selectedFaction.LandedSquads);
        Assert.DoesNotContain(unselectedRecon, unselectedFaction.LandedSquads);
        Assert.Contains(unselectedGarrison, unselectedFaction.LandedSquads);
    }

    [Fact]
    public void GenerateFactionOrders_TwoKnownTargetsUseStableTargetAndSharedBudgetOrder()
    {
        Faction attacker = CreateFaction(2, "Hive Fleet", GrowthType.Consumption);
        Faction defender = CreateFaction(3, "Defender", GrowthType.Conversion, isDefault: true);
        Planet planet = CreatePlanet(1, "Two Fronts");
        Region staging = planet.Regions.First(region => region.GetAdjacentRegions().Count >= 2);
        Region targetA = staging.GetAdjacentRegions()[0];
        Region targetB = staging.GetAdjacentRegions()[1];
        AddRegionFaction(planet, staging, attacker, 50_000);
        AddRegionFaction(planet, targetA, defender, 10_000, garrison: 10_000);
        AddRegionFaction(planet, targetB, defender, 1_000, garrison: 1_000);
        planet.PlanetFactionMap[attacker.Id].SetRegionAwareness(targetA, FactionStrategyPlanningConstants.ReconIntelThreshold);
        planet.PlanetFactionMap[attacker.Id].SetRegionAwareness(targetB, FactionStrategyPlanningConstants.ReconIntelThreshold);
        Sector sector = new(CreatePlayerForce(), [], [planet], []);

        List<StrategicCombatMission> offensives = new FactionStrategyController()
            .GenerateFactionOrders(attacker, sector)
            .Select(order => order.Mission)
            .OfType<StrategicCombatMission>()
            .Where(mission => mission.MissionType == MissionType.Advance)
            .ToList();

        Assert.Equal(2, offensives.Count);
        // This is the observed signature of the current planner for this deliberately small graph;
        // keep the order pinned while the controller is extracted.
        Assert.Equal(targetB.Name, offensives[0].RegionFaction.Region.Name);
        Assert.Equal(targetA.Name, offensives[1].RegionFaction.Region.Name);
        Assert.True(staging.RegionFactionMap[attacker.Id].MilitaryStrength < 50_000);
        Assert.Equal(
            offensives.Select(mission => mission.RegionFaction.PlanetFaction.Faction.Id).Distinct().Single(),
            defender.Id);
    }

    [Fact]
    public void GenerateFactionOrders_KeepsSameRegionTargetsDistinctByFaction()
    {
        Faction attacker = CreateFaction(2, "Hive Fleet", GrowthType.Consumption);
        Faction defenderA = CreateFaction(3, "Defender A", GrowthType.Conversion);
        Faction defenderB = CreateFaction(4, "Defender B", GrowthType.Conversion);
        Planet planet = CreatePlanet(1, "Shared Region");
        Region region = planet.Regions[0];
        AddRegionFaction(planet, region, attacker, 50_000);
        AddRegionFaction(planet, region, defenderA, 1_000, garrison: 1_000);
        AddRegionFaction(planet, region, defenderB, 1_000, garrison: 1_000);
        Sector sector = new(CreatePlayerForce(), [], [planet], []);

        List<StrategicCombatMission> offensives = new FactionStrategyController()
            .GenerateFactionOrders(attacker, sector)
            .Select(order => order.Mission)
            .OfType<StrategicCombatMission>()
            .Where(mission => mission.MissionType == MissionType.Advance)
            .ToList();

        Assert.Equal(2, offensives.Count);
        Assert.All(offensives, mission => Assert.Same(region, mission.RegionFaction.Region));
        Assert.Equal(
            new[] { defenderA.Id, defenderB.Id }.OrderBy(id => id),
            offensives.Select(mission => mission.RegionFaction.PlanetFaction.Faction.Id).OrderBy(id => id));
    }

    [Fact]
    public void OffensiveEvaluator_TiedScoresPreserveEnumerationOrder()
    {
        Faction attacker = CreateFaction(2, "Attacker");
        Faction defender = CreateFaction(3, "Defender");
        Planet planet = CreatePlanet(1, "Tied Scores");
        Region firstRegion = planet.Regions[0];
        Region secondRegion = planet.Regions[1];
        RegionFaction firstTarget = AddRegionFaction(planet, firstRegion, defender, 100);
        RegionFaction secondTarget = AddRegionFaction(planet, secondRegion, defender, 100);

        PotentialOffensive first = new()
        {
            TargetRegion = firstRegion,
            TargetFaction = firstTarget,
            AvailableAttackingForce = 1_000,
            Reward = 500,
            DefenderBattleValue = 100,
            EstimatedDefenderBattleValue = 100
        };
        PotentialOffensive second = new()
        {
            TargetRegion = secondRegion,
            TargetFaction = secondTarget,
            AvailableAttackingForce = 1_000,
            Reward = 500,
            DefenderBattleValue = 100,
            EstimatedDefenderBattleValue = 100
        };

        Assert.Same(first, FactionOffensiveEvaluator.ChooseBestOffensive([first, second]));
    }

    [Fact]
    public void GenerateFactionOrders_DefensivePostureBuildsOnlyConstructionOrders()
    {
        Faction pdf = CreateFaction(2, "Planetary Defence Force", GrowthType.Conversion, isDefault: true);
        Faction enemy = CreateFaction(3, "Test Cult");
        Planet planet = CreatePlanet(1, "Under Assault");
        Region region = planet.Regions[0];
        AddRegionFaction(planet, region, pdf, 1_000_000, garrison: 2_000);
        AddRegionFaction(planet, region, enemy, 1_000);
        Sector sector = new(CreatePlayerForce(), [], [planet], []);

        List<Order> orders = new FactionStrategyController()
            .GenerateFactionOrders(pdf, sector, defensiveOnly: true);

        Assert.NotEmpty(orders);
        Assert.All(orders, order =>
        {
            Assert.IsType<ConstructionMission>(order.Mission);
            Assert.Empty(order.AssignedSquads);
        });
        Assert.DoesNotContain(orders, order => order.Mission is StrategicCombatMission);
    }

    [Fact]
    public void ExplicitStrategyDependencies_UseTheSuppliedRandomForReconGeneration()
    {
        SquadTemplate lineSquad = CreateLineSquadTemplate();
        Faction attacker = CreateFaction(
            2,
            "Test Cult",
            squadTemplates: new Dictionary<int, SquadTemplate> { [lineSquad.Id] = lineSquad });
        Faction defender = CreateFaction(3, "Defender", isDefault: true);
        Planet planet = CreatePlanet(1, "Recon Dependency");
        Region staging = planet.Regions[0];
        Region target = staging.GetAdjacentRegions().First();
        AddRegionFaction(planet, staging, attacker, 2_000);
        AddRegionFaction(planet, target, defender, 100, garrison: 100);
        Sector sector = new(CreatePlayerForce(), [], [planet], []);
        RecordingRng random = new();

        List<Order> orders = new FactionStrategyController(random, null)
            .GenerateFactionOrders(attacker, sector);

        Order recon = Assert.Single(orders, order => order.Mission.MissionType == MissionType.Recon);
        Assert.NotEmpty(recon.AssignedSquads);
        Assert.True(random.CallCount > 0, "force generation must draw from the injected stream");
    }

    [Fact]
    public void ExplicitStrategyDependencies_UseTheSuppliedBehaviorRules()
    {
        double singletonRatio = GameDataSingleton.Instance.GameRulesData?.FactionBehaviorRules?.DefendedLandingRatio ?? 2.0;
        Assert.NotEqual(100.0, singletonRatio);

        List<Order> permissiveOrders = BuildKnownInvasionPlan(0.5);
        List<Order> restrictiveOrders = BuildKnownInvasionPlan(100.0);

        Assert.True(
            permissiveOrders.Any(order => order.Mission is StrategicCombatMission),
            $"orders={string.Join(",", permissiveOrders.Select(order => order.Mission?.MissionType.ToString() ?? "null"))}");
        Assert.DoesNotContain(restrictiveOrders, order => order.Mission is StrategicCombatMission);
    }

    [Fact]
    public void SharedPlanningModels_PreserveMutableBudgetAndRegionIdentity()
    {
        Faction faction = CreateFaction(2, "Test Cult");
        Planet planet = CreatePlanet(1, "Model Identity");
        RegionFaction regionFaction = AddRegionFaction(planet, planet.Regions[0], faction, 1_000);
        RegionForceState state = new(regionFaction, 200, 200, 800, 0);

        state.SpareTroops = 350;

        Assert.Same(regionFaction, state.RegionFaction);
        Assert.Equal(350, state.SpareTroops);
        Assert.Equal(200, state.RequiredDefensiveBattleValue);
        Assert.Equal(200, state.AssignedDefensiveBattleValue);
    }

    private static List<Order> BuildKnownInvasionPlan(double defendedLandingRatio)
    {
        Faction attacker = CreateFaction(
            2,
            "Insurrectionists",
            GrowthType.Unrest,
            generatesInvasions: true);
        Faction defender = CreateFaction(3, "Defender", isDefault: true);
        Planet planet = CreatePlanet(1, "Rules Dependency");
        Region staging = planet.Regions[0];
        Region target = staging.GetAdjacentRegions().First();
        RegionFaction attackerRegion = AddRegionFaction(planet, staging, attacker, 4_000);
        attackerRegion.ArmedCivilians = 4_000;
        AddRegionFaction(planet, target, defender, 100, garrison: 1_000);
        planet.PlanetFactionMap[attacker.Id].SetRegionAwareness(
            target,
            FactionStrategyPlanningConstants.ReconIntelThreshold);
        Sector sector = new(CreatePlayerForce(), [], [planet], []);

        return new FactionStrategyController(new FixedRNG(), CreateBehaviorRules(defendedLandingRatio))
            .GenerateFactionOrders(attacker, sector);
    }

    private static FactionBehaviorRulesProfile CreateBehaviorRules(double defendedLandingRatio) =>
        new(
            "strategy-test",
            0.1,
            1,
            1.0,
            0.0,
            0.5,
            0.1,
            0.0,
            1.0,
            defendedLandingRatio,
            100,
            100,
            0.1,
            1.0,
            0.1,
            0.1,
            1.0,
            1.0,
            1.0,
            100,
            0.1,
            0.1,
            0.1,
            0.0,
            0.0,
            0.0,
            0.0,
            0.0,
            0.0,
            0.0,
            0.0,
            1.0);

    private static SquadTemplate CreateLineSquadTemplate()
    {
        SoldierTemplate trooper = new(
            50,
            TestModelFactory.HumanSpecies,
            "Strategy Trooper",
            1,
            1,
            false,
            0,
            [],
            null,
            2);
        return new SquadTemplate(
            50,
            "Strategy Squad",
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            [new SquadTemplateElement(trooper, 5, 5)],
            SquadTypes.None);
    }

    private static Squad AddLandedSquad(RegionFaction regionFaction, MissionType missionType)
    {
        Squad squad = TestModelFactory.CreateSquad("Transient", TestModelFactory.CreateSoldier(TestModelFactory.MarineTemplate));
        Order order = new([squad], true, false, Aggression.Cautious,
            new Mission(missionType, regionFaction, 0));
        squad.CurrentOrders = order;
        regionFaction.LandedSquads.Add(squad);
        return squad;
    }

    private static RegionFaction AddRegionFaction(
        Planet planet,
        Region region,
        Faction faction,
        long population,
        long garrison = 0)
    {
        PlanetFaction planetFaction = planet.PlanetFactionMap.TryGetValue(faction.Id, out PlanetFaction existing)
            ? existing
            : new PlanetFaction(faction) { IsPublic = true };
        planet.PlanetFactionMap[faction.Id] = planetFaction;
        RegionFaction regionFaction = new(planetFaction, region)
        {
            Population = population,
            Garrison = garrison,
            Organization = 100,
            IsPublic = true
        };
        region.RegionFactionMap[faction.Id] = regionFaction;
        return regionFaction;
    }

    private static Planet CreatePlanet(int id, string name)
    {
        Planet planet = new(id, name, new Coordinate((ushort)id, (ushort)id), 1, null, 1, 0);
        for (int i = 0; i < planet.Regions.Length; i++)
        {
            planet.Regions[i] = new Region(
                i, planet, 0, $"Region {i}",
                RegionExtensions.GetCoordinatesFromRegionNumber(i), 0);
        }

        return planet;
    }

    private static PlayerForce CreatePlayerForce()
    {
        Faction player = CreateFaction(1, "Test Chapter", isPlayer: true);
        return new PlayerForce(
            player,
            new Army("Test Army", null, null, null, []),
            new Fleet("Test Fleet", null, null));
    }

    private static Faction CreateFaction(
        int id,
        string name,
        GrowthType growthType = GrowthType.Conversion,
        bool isPlayer = false,
        bool isDefault = false,
        bool generatesInvasions = false,
        IReadOnlyDictionary<int, SquadTemplate> squadTemplates = null)
    {
        FactionBehavior behavior = isPlayer || isDefault
            ? FactionBehavior.None
            : FactionBehavior.PopulationIsMilitary;
        if (growthType is GrowthType.Consumption or GrowthType.Unrest)
        {
            behavior |= FactionBehavior.InvadesOnVictory;
        }
        if (growthType is GrowthType.Conversion or GrowthType.Unrest)
        {
            behavior |= FactionBehavior.DefendsHostWhileHidden;
        }
        if (growthType == GrowthType.Unrest)
        {
            behavior |= FactionBehavior.OffersExternalEnemyTruce;
        }
        if (generatesInvasions)
        {
            behavior |= FactionBehavior.GeneratesInvasions;
        }

        return new Faction(
            id,
            name,
            Color.Red,
            isPlayer,
            isDefault,
            behavior,
            growthType,
            new Dictionary<int, Species> { [TestModelFactory.HumanSpecies.Id] = TestModelFactory.HumanSpecies },
            new Dictionary<int, SoldierTemplate>(),
            squadTemplates ?? new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, UnitTemplate>(),
            new Dictionary<int, BoatTemplate>(),
            new Dictionary<int, ShipTemplate>(),
            new Dictionary<int, FleetTemplate>());
    }

    private sealed class RecordingRng : IRNG
    {
        public int CallCount { get; private set; }

        public double GetDoubleInRange(double lowerBound, double upperBound)
        {
            CallCount++;
            return lowerBound;
        }

        public double GetLinearDouble()
        {
            CallCount++;
            return 0.0;
        }

        public int GetIntBelowMax(int min, int max)
        {
            CallCount++;
            return min;
        }

        public double NextRandomZValue()
        {
            CallCount++;
            return 0.0;
        }
    }
}
