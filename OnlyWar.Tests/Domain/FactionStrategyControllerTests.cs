using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Missions.Raid;
using OnlyWar.Helpers.Strategy;
using OnlyWar.Helpers.StrategicCombat;
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
using Xunit;
using PlanningPotentialOffensive = OnlyWar.Helpers.Strategy.PotentialOffensive;

namespace OnlyWar.Tests.Domain;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class FactionStrategyControllerTests
{
    [Fact]
    public void GenerateFactionOrders_ReturnsEmptyWhenFactionAbsentFromPlanet()
    {
        Faction enemy = CreateNonPlayerFaction();
        Sector sector = BuildSectorWithSingleRegionFaction(
            CreateNonPlayerFaction(id: 99, name: "Other"), population: 1000, organization: 100, isPublic: true);

        List<Order> orders = new FactionStrategyController().GenerateFactionOrders(enemy, sector);

        Assert.Empty(orders);
    }

    [Fact]
    public void GenerateFactionOrders_ReturnsEmptyWhenRegionFactionIsHidden()
    {
        Faction enemy = CreateNonPlayerFaction();
        Sector sector = BuildSectorWithSingleRegionFaction(enemy, population: 1000, organization: 100, isPublic: false);

        List<Order> orders = new FactionStrategyController().GenerateFactionOrders(enemy, sector);

        Assert.Empty(orders);
    }

    [Fact]
    public void GenerateFactionOrders_ReturnsEmptyWhenNoSpareTroops()
    {
        Faction enemy = CreateNonPlayerFaction();
        // Organization 0 => no organized troops => no spare troops => nothing to do
        Sector sector = BuildSectorWithSingleRegionFaction(enemy, population: 1000, organization: 0, isPublic: true);

        List<Order> orders = new FactionStrategyController().GenerateFactionOrders(enemy, sector);

        Assert.Empty(orders);
    }

    [Fact]
    public void GenerateFactionOrders_PeacefulNpcDoesNotConstruct()
    {
        Faction enemy = CreateNonPlayerFaction();
        // No public enemy on the planet: the faction has spare force, but should not spend it on
        // defenses when there is no active threat.
        Sector sector = BuildSectorWithSingleRegionFaction(enemy, population: 1000, organization: 100, isPublic: true);

        List<Order> orders = new FactionStrategyController().GenerateFactionOrders(enemy, sector);

        Assert.Empty(orders);
    }

    [Fact]
    public void GenerateFactionOrders_ThreatenedNpcStillSpendsSpareTroopsOnDefensiveConstruction()
    {
        Faction enemy = CreateNonPlayerFaction();
        Faction pdf = CreateDefaultFaction();
        Sector sector = BuildSectorWithFactions(
            (enemy, population: 1000, organization: 100, isPublic: true),
            (pdf, population: 1000, organization: 100, isPublic: true));

        List<Order> orders = new FactionStrategyController().GenerateFactionOrders(enemy, sector);

        Assert.NotEmpty(orders);
        Assert.Contains(orders, o => o.Mission is ConstructionMission && !o.AssignedSquads.Any());
    }

    [Fact]
    public void GenerateFactionOrders_NewlyRevealedUnrestOpensWithStrategicAmbush()
    {
        Faction unrest = BuildFaction(20, "Insurrectionists", false, false, GrowthType.Unrest);
        Faction pdf = CreateDefaultFaction(21);
        Planet planet = CreatePlanet();
        Region region = planet.Regions[0];
        AddRegionFaction(planet, region, unrest, population: 20_000, organization: 100);
        RegionFaction rebels = region.RegionFactionMap[unrest.Id];
        rebels.ArmedCivilians = 10_000;
        rebels.HasEmergenceAdvantage = true;
        AddRegionFaction(planet, region, pdf, population: 100_000, organization: 100, garrison: 100);
        Sector sector = new(CreatePlayerForce(), [], [planet], []);

        List<Order> orders = new FactionStrategyController().GenerateFactionOrders(unrest, sector);

        Assert.Contains(orders, order =>
            order.Mission is StrategicCombatMission mission
            && mission.MissionType == MissionType.Ambush);
        Assert.False(rebels.HasEmergenceAdvantage);
    }

    // Removed with the mechanic: GenerateFactionOrders_PerceivedThreatBonusPinsReserveAndSuppressesActivity
    // covered RegionFaction.PerceivedThreatBonus, the diversion's garrison-inflation channel. That was
    // dropped deliberately along with the pre-planning shaping phase - a feint begun on Monday cannot
    // retroactively change planning the enemy did on Sunday. A diversion's effect is now same-day and
    // tactical; see MissionStealthDifficultyTests' committed-attention coverage.

    [Fact]
    public void GenerateFactionOrders_DefensivePlanningDoesNotInstantlyRaiseGarrison()
    {
        Faction pdf = CreateDefaultFaction();
        Faction enemy = CreateNonPlayerFaction();

        Planet planet = CreatePlanet();
        Region pdfRegion = planet.Regions[0];
        Region enemyRegion = pdfRegion.GetAdjacentRegions().First();
        AddRegionFaction(planet, pdfRegion, pdf, population: 1_000_000, organization: 100, garrison: 2_000);
        AddRegionFaction(planet, enemyRegion, enemy, population: 1_000, organization: 100);
        planet.PlanetFactionMap[pdf.Id].SetRegionAwareness(enemyRegion, FactionThreatAssessment.GarrisonFullSightIntel);
        Sector sector = new(CreatePlayerForce(), [], [planet], []);

        List<Order> orders = new FactionStrategyController()
            .GenerateFactionOrders(pdf, sector, defensiveOnly: true);

        Assert.NotEmpty(orders);
        Assert.Equal(2_000, pdfRegion.RegionFactionMap[pdf.Id].Garrison);
    }

    [Fact]
    public void GenerateFactionOrders_ClearsLocalEnemyBeforeAdjacentTargets()
    {
        Faction attacker = CreateNonPlayerFaction();
        Faction imperial = CreateDefaultFaction();
        Planet planet = CreatePlanet();

        PlanetFaction attackerPlanetFaction = new(attacker) { IsPublic = true };
        PlanetFaction imperialPlanetFaction = new(imperial) { IsPublic = true };
        planet.PlanetFactionMap[attacker.Id] = attackerPlanetFaction;
        planet.PlanetFactionMap[imperial.Id] = imperialPlanetFaction;

        Region localRegion = planet.Regions[0];
        Region adjacentRegion = planet.Regions[1];
        localRegion.RegionFactionMap[attacker.Id] = new RegionFaction(attackerPlanetFaction, localRegion)
        {
            Population = 50_000,
            Organization = 100,
            IsPublic = true
        };
        RegionFaction localTarget = new(imperialPlanetFaction, localRegion)
        {
            Population = 5_000,
            Garrison = 5_000,
            Organization = 100,
            IsPublic = true
        };
        localRegion.RegionFactionMap[imperial.Id] = localTarget;
        RegionFaction adjacentTarget = new(imperialPlanetFaction, adjacentRegion)
        {
            Population = 1_000,
            Garrison = 1_000,
            Organization = 100,
            IsPublic = true
        };
        adjacentRegion.RegionFactionMap[imperial.Id] = adjacentTarget;
        attackerPlanetFaction.SetRegionAwareness(localRegion, FactionStrategyPlanningConstants.ReconIntelThreshold);
        Sector sector = new(CreatePlayerForce(), [], [planet], []);

        List<Order> orders = new FactionStrategyController().GenerateFactionOrders(attacker, sector);

        StrategicCombatMission assault = orders
            .Select(o => o.Mission)
            .OfType<StrategicCombatMission>()
            .Single();
        Assert.Same(localTarget, assault.RegionFaction);
    }

    [Fact]
    public void GenerateFactionOrders_DefensiveOnly_NotUnderAssault_GeneratesNothing()
    {
        // A peaceful Imperial world: the PDF has plenty of spare force but no enemy to fortify
        // against, so a defensive-only plan produces no orders.
        Faction pdf = CreateDefaultFaction();
        Sector sector = BuildSectorWithFactions((pdf, population: 1_000_000, organization: 100, isPublic: true));

        List<Order> orders = new FactionStrategyController()
            .GenerateFactionOrders(pdf, sector, defensiveOnly: true);

        Assert.Empty(orders);
    }

    [Fact]
    public void GenerateFactionOrders_DefensiveOnly_UnderAssault_BuildsDefensesAndNoOffensive()
    {
        // Default + a public enemy share the region, so the world is under assault and, with two
        // public factions, the region has no single controller — no garrison is pinned and the full
        // PDF force is free to dig in.
        Faction pdf = CreateDefaultFaction();
        Faction enemy = CreateNonPlayerFaction();
        Planet planet = CreatePlanet();
        Region region = planet.Regions[0];
        AddRegionFaction(planet, region, pdf, population: 1_000_000, organization: 100, garrison: 2_000);
        AddRegionFaction(planet, region, enemy, population: 1_000, organization: 100);
        Sector sector = new(CreatePlayerForce(), [], [planet], []);

        List<Order> orders = new FactionStrategyController()
            .GenerateFactionOrders(pdf, sector, defensiveOnly: true);

        Assert.NotEmpty(orders);
        // Defensive only: every order is a fortification / listening-post build, never an offensive.
        Assert.All(orders, o => Assert.IsType<ConstructionMission>(o.Mission));
        Assert.All(orders, o => Assert.Empty(o.AssignedSquads));
    }

    // ----- Q2: reward/risk offensive targeting (PRD §4.24) -----

    [Fact]
    public void CautiousDefenderEstimate_BlindAttackerAssumesAStrongerDefender()
    {
        // With no intel (sigma 0.5) the AI plans against ~1 sigma above the truth, not the truth
        // itself — it hedges against what it cannot see rather than betting on a lucky-low draw.
        Assert.Equal(1500L, FactionOffensiveEvaluator.CautiousDefenderEstimate(1000, intelLevel: 0f));
    }

    [Fact]
    public void CautiousDefenderEstimate_BetterIntelConvergesTowardTheTruth()
    {
        long blindGuess = FactionOffensiveEvaluator.CautiousDefenderEstimate(1000, intelLevel: 0f);   // sigma 0.5 -> 1500
        long scoutedGuess = FactionOffensiveEvaluator.CautiousDefenderEstimate(1000, intelLevel: 4f); // sigma 0.1 -> 1100

        Assert.Equal(1500L, blindGuess);
        Assert.Equal(1100L, scoutedGuess);
        Assert.True(scoutedGuess < blindGuess);
    }

    [Fact]
    public void CalculateDefenderBattleValue_WeightsLandedSquadsByBattleValueNotHeadcount()
    {
        Faction defender = CreateDefaultFaction();
        RegionFaction rf = CreateTargetRegionFaction(defender, population: 1_000, garrison: 50);
        // Three landed soldiers, each worth 2 battle value: 6 points, not 3 bodies.
        rf.LandedSquads.Add(TestModelFactory.CreateSquad("Defenders",
            TestModelFactory.CreateSoldier(TestModelFactory.MarineTemplate),
            TestModelFactory.CreateSoldier(TestModelFactory.MarineTemplate),
            TestModelFactory.CreateSoldier(TestModelFactory.MarineTemplate)));

        Assert.Equal(56L, FactionThreatAssessment.CalculateDefenderBattleValue(rf));
    }

    [Fact]
    public void CalculateDefenderBattleValue_UsesMilitaryStrengthForPopulationIsMilitaryDefenders()
    {
        Faction defender = CreateNonPlayerFaction();
        RegionFaction rf = CreateTargetRegionFaction(defender, population: 1_000, garrison: 200);

        Assert.Equal(1_000L, FactionThreatAssessment.CalculateDefenderBattleValue(rf));
    }

    [Fact]
    public void GenerateFactionOrders_LargeNpcAssaultCreatesStrategicMissionWithoutSquads()
    {
        RNG.Reset(1234);
        Faction attacker = BuildFaction(20, "Swarm", isPlayer: false, isDefault: false, GrowthType.Consumption);
        Faction defender = CreateDefaultFaction();
        Planet planet = CreatePlanet();
        Region staging = planet.Regions[0];
        Region target = staging.GetAdjacentRegions().First();

        AddRegionFaction(planet, staging, attacker, population: 50_000, organization: 100);
        AddRegionFaction(planet, target, defender, population: 10_000, organization: 100, garrison: 10_000);
        RegionFaction targetFaction = target.RegionFactionMap[defender.Id];
        planet.PlanetFactionMap[attacker.Id].AddRegionAwareness(target, FactionStrategyPlanningConstants.ReconIntelThreshold);
        Sector sector = new(CreatePlayerForce(), [], [planet], []);

        List<Order> orders = new FactionStrategyController().GenerateFactionOrders(attacker, sector);

        Order strategicOrder = Assert.Single(orders, o => o.Mission is StrategicCombatMission);
        Assert.Empty(strategicOrder.AssignedSquads);
        StrategicCombatMission mission = Assert.IsType<StrategicCombatMission>(strategicOrder.Mission);
        Assert.True(mission.CommittedBattleValue >= StrategicCombatRules.MassCombatBattleValueFloor);
        Assert.True(staging.RegionFactionMap[attacker.Id].MilitaryStrength < 50_000);
    }

    [Fact]
    public void GenerateFactionOrders_LargeUnknownTargetReconUsesCappedTacticalForce()
    {
        RNG.Reset(1234);
        // Deliberately a private copy of TestModelFactory's squad template rather than the shared
        // static itself. Faction's constructor re-owns every SquadTemplate handed to it
        // (`template.Faction = this`), so passing the shared instance permanently rebinds it to
        // this non-player Swarm for the remainder of the test process. Every squad
        // TestModelFactory.CreateSquad builds afterwards then reports IsPlayerFaction == false,
        // which silently defeats OrderAssignment.IsPlayerOrder and stops it reusing a player's
        // existing order — failing whichever tests happened to be scheduled after this one.
        SquadTemplate swarmSquad = new(
            TestModelFactory.SquadTemplate.Id,
            TestModelFactory.SquadTemplate.Name,
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            [new SquadTemplateElement(TestModelFactory.SergeantTemplate, 0, 1),
             new SquadTemplateElement(TestModelFactory.MarineTemplate, 0, 4)],
            SquadTypes.None);
        Faction attacker = BuildFaction(
            20,
            "Swarm",
            isPlayer: false,
            isDefault: false,
            GrowthType.Consumption,
            new Dictionary<int, SquadTemplate> { [swarmSquad.Id] = swarmSquad });
        Faction defender = CreateDefaultFaction();
        Planet planet = CreatePlanet();
        Region staging = planet.Regions[0];
        Region target = staging.GetAdjacentRegions().First();

        AddRegionFaction(planet, staging, attacker, population: 100_000, organization: 100);
        AddRegionFaction(planet, target, defender, population: 100_000, organization: 100, garrison: 100);
        Sector sector = new(CreatePlayerForce(), [], [planet], []);

        List<Order> orders = new FactionStrategyController().GenerateFactionOrders(attacker, sector);

        Order reconOrder = Assert.Single(orders, o => o.Mission.MissionType == MissionType.Recon);
        long generatedBattleValue = reconOrder.AssignedSquads
            .Sum(s => s.Members.Sum(m => (long)m.Template.BattleValue));
        Assert.InRange(generatedBattleValue, 1, StrategicCombatRules.NpcReconBattleValueCap);
    }

    [Fact]
    public void GenerateFactionOrders_TinyGarrisonStillDrawsAtLeastAFullSquadAssault()
    {
        RNG.Reset(1234);
        // The attacker's only line squad is five 2-BV troopers with a five-man minimum: nothing
        // smaller than a full 10-BV squad can be generated. Against a near-dead garrison (BV 2)
        // the raw assault budget (2x defender = 4) is ungeneratable, so without the
        // minimum-force-request floor the offensive silently fizzles and the region survives.
        SoldierTemplate trooper = new(
            50, TestModelFactory.HumanSpecies, "Cult Trooper", 1, 1, false, 0, [], null, 2);
        SquadTemplate lineSquad = new(
            50, "Cult Squad", TestModelFactory.DefaultWeapons, [], TestModelFactory.TestArmor,
            [new SquadTemplateElement(trooper, 5, 5)], SquadTypes.None);
        Faction attacker = BuildFaction(20, "Cult", isPlayer: false, isDefault: false, GrowthType.Conversion,
            new Dictionary<int, SquadTemplate> { [lineSquad.Id] = lineSquad });
        Faction defender = CreateDefaultFaction();
        Planet planet = CreatePlanet();
        Region staging = planet.Regions[0];
        Region target = staging.GetAdjacentRegions().First();

        AddRegionFaction(planet, staging, attacker, population: 1_000, organization: 100);
        AddRegionFaction(planet, target, defender, population: 10, organization: 100, garrison: 2);
        planet.PlanetFactionMap[attacker.Id].SetRegionAwareness(target, FactionStrategyPlanningConstants.ReconIntelThreshold);
        Sector sector = new(CreatePlayerForce(), [], [planet], []);

        List<Order> orders = new FactionStrategyController().GenerateFactionOrders(attacker, sector);

        Order assault = Assert.Single(orders, o => o.Mission.MissionType == MissionType.Advance);
        long generatedBattleValue = assault.AssignedSquads
            .Sum(s => s.Members.Sum(m => (long)m.Template.BattleValue));
        Assert.Equal(10, attacker.MinimumForceRequest);
        Assert.True(generatedBattleValue >= attacker.MinimumForceRequest);
    }

    [Fact]
    public void MissionStepOrchestrator_LightningRaidUsesRaidStep()
    {
        RegionFaction target = CreateTargetRegionFaction(CreateDefaultFaction(), population: 1_000, garrison: 100);
        Squad squad = TestModelFactory.CreateSquad("Raiders",
            TestModelFactory.CreateSoldier(TestModelFactory.MarineTemplate));
        squad.CurrentRegion = target.Region;
        Order order = new([squad], true, true, Aggression.Cautious,
            new Mission(MissionType.LightningRaid, target, 0));
        MissionContext context = new(order, [], []);
        MissionExecutionContext execution = TestExecutionContextFactory.CreateMission(
            context,
            StaticRNG.Instance);

        Assert.IsType<LightningRaidMissionStep>(
            MissionStepOrchestrator.GetMainInitialStep(execution));
    }

    [Fact]
    public void GenerateFactionOrders_CanCreateMultipleStrategicOffensivesOnOnePlanet()
    {
        RNG.Reset(1234);
        Faction attacker = BuildFaction(20, "Swarm", isPlayer: false, isDefault: false, GrowthType.Consumption);
        Faction defender = CreateDefaultFaction();
        Planet planet = CreatePlanet();

        Region stagingA = planet.Regions[0];
        Region targetA = stagingA.GetAdjacentRegions().First();
        Region stagingB = planet.Regions
            .First(region => region != stagingA
                             && region != targetA
                             && region.GetAdjacentRegions().Any(adjacent => adjacent != stagingA && adjacent != targetA));
        Region targetB = stagingB.GetAdjacentRegions().First(adjacent => adjacent != stagingA && adjacent != targetA);

        AddRegionFaction(planet, stagingA, attacker, population: 50_000, organization: 100);
        AddRegionFaction(planet, stagingB, attacker, population: 50_000, organization: 100);
        AddRegionFaction(planet, targetA, defender, population: 10_000, organization: 100, garrison: 10_000);
        AddRegionFaction(planet, targetB, defender, population: 10_000, organization: 100, garrison: 10_000);
        planet.PlanetFactionMap[attacker.Id].SetRegionAwareness(targetA, FactionStrategyPlanningConstants.ReconIntelThreshold);
        planet.PlanetFactionMap[attacker.Id].SetRegionAwareness(targetB, FactionStrategyPlanningConstants.ReconIntelThreshold);
        Sector sector = new(CreatePlayerForce(), [], [planet], []);

        List<Order> orders = new FactionStrategyController().GenerateFactionOrders(attacker, sector);

        List<StrategicCombatMission> offensives = orders
            .Select(order => order.Mission)
            .OfType<StrategicCombatMission>()
            .Where(mission => mission.MissionType == MissionType.Advance)
            .ToList();
        Assert.True(offensives.Count >= 2);
        Assert.Contains(offensives, mission => mission.RegionFaction.Region == targetA);
        Assert.Contains(offensives, mission => mission.RegionFaction.Region == targetB);
    }

    [Fact]
    public void GenerateFactionOrders_KnownTooStrongTargetCreatesLightningRaid()
    {
        RNG.Reset(1234);
        Faction attacker = BuildFaction(20, "Cult", isPlayer: false, isDefault: false, GrowthType.Conversion);
        Faction defender = CreateDefaultFaction();
        Planet planet = CreatePlanet();
        Region staging = planet.Regions[0];
        Region target = staging.GetAdjacentRegions().First();

        AddRegionFaction(planet, staging, attacker, population: 10_000, organization: 100);
        AddRegionFaction(planet, target, defender, population: 100_000, organization: 100, garrison: 10_000);
        planet.PlanetFactionMap[attacker.Id].SetRegionAwareness(target, FactionStrategyPlanningConstants.ReconIntelThreshold);
        Sector sector = new(CreatePlayerForce(), [], [planet], []);

        List<Order> orders = new FactionStrategyController().GenerateFactionOrders(attacker, sector);

        StrategicCombatMission raid = Assert.Single(orders
            .Select(order => order.Mission)
            .OfType<StrategicCombatMission>(),
            mission => mission.MissionType == MissionType.LightningRaid);
        Assert.Same(target.RegionFactionMap[defender.Id], raid.RegionFaction);
        Assert.False(raid.InvadesOnVictory);
    }

    [Fact]
    public void GenerateFactionOrders_PullsFromLeastFlexibleAdjacentSourceFirst()
    {
        RNG.Reset(1234);
        Faction attacker = BuildFaction(20, "Swarm", isPlayer: false, isDefault: false, GrowthType.Consumption);
        Faction defender = CreateDefaultFaction();
        Planet planet = CreatePlanet();
        Region target = planet.Regions.First(region => region.GetAdjacentRegions().Count >= 2);
        Region focusedSource = target.GetAdjacentRegions().First();
        Region flexibleSource = target.GetAdjacentRegions().Skip(1).First();
        Region extraTarget = flexibleSource.GetAdjacentRegions().First(region => region != target && region != focusedSource);

        AddRegionFaction(planet, focusedSource, attacker, population: 50_000, organization: 100);
        AddRegionFaction(planet, flexibleSource, attacker, population: 50_000, organization: 100);
        AddRegionFaction(planet, target, defender, population: 10_000, organization: 100, garrison: 10_000);
        AddRegionFaction(planet, extraTarget, defender, population: 1_000, organization: 100, garrison: 100);
        planet.PlanetFactionMap[attacker.Id].SetRegionAwareness(target, FactionStrategyPlanningConstants.ReconIntelThreshold);
        Sector sector = new(CreatePlayerForce(), [], [planet], []);

        new FactionStrategyController().GenerateFactionOrders(attacker, sector);

        long focusedRemaining = focusedSource.RegionFactionMap[attacker.Id].MilitaryStrength;
        long flexibleRemaining = flexibleSource.RegionFactionMap[attacker.Id].MilitaryStrength;
        Assert.True(focusedRemaining < flexibleRemaining);
    }

    [Fact]
    public void GenerateFactionOrders_CivilianOnlyEnemyRegionReinforcesAdjacentFront()
    {
        Faction attacker = BuildFaction(20, "Cult", isPlayer: false, isDefault: false, GrowthType.Conversion);
        Faction defender = CreateDefaultFaction();
        Planet planet = CreatePlanet();
        Region source = planet.Regions[0];
        Region destination = source.GetAdjacentRegions().First();
        Region enemyFront = destination.GetAdjacentRegions().First(region => region != source);

        AddRegionFaction(planet, source, attacker, population: 10_000, organization: 100);
        AddRegionFaction(planet, destination, attacker, population: 1_000, organization: 100);
        AddRegionFaction(planet, source, defender, population: 5_000, organization: 100, garrison: 0);
        AddRegionFaction(planet, enemyFront, defender, population: 100_000, organization: 100, garrison: 10_000);
        long destinationBefore = destination.RegionFactionMap[attacker.Id].MilitaryStrength;
        Sector sector = new(CreatePlayerForce(), [], [planet], []);

        new FactionStrategyController().GenerateFactionOrders(attacker, sector);

        Assert.True(source.RegionFactionMap[attacker.Id].MilitaryStrength < 10_000);
        Assert.True(destination.RegionFactionMap[attacker.Id].MilitaryStrength > destinationBefore);
    }

    [Fact]
    public void GenerateFactionOrders_SpareTroopsReinforceAdjacentGarrisonShortfall()
    {
        // A rear region flush with spare troops shores up an adjacent friendly region that faces a
        // strong enemy front it cannot self-garrison, before any spare force is spent on offensives.
        Faction attacker = BuildFaction(20, "Cult", isPlayer: false, isDefault: false, GrowthType.Conversion);
        Faction defender = CreateDefaultFaction();
        Planet planet = CreatePlanet();
        Region rear = planet.Regions[0];
        Region needy = rear.GetAdjacentRegions().First();
        Region enemyFront = needy.GetAdjacentRegions().First(region => region != rear);

        // Rear holds a large spare pool with no adjacent enemy; the needy region is thinly held and
        // pressed by a strong enemy garrison next door, leaving it far below its required garrison.
        AddRegionFaction(planet, rear, attacker, population: 10_000, organization: 100);
        AddRegionFaction(planet, needy, attacker, population: 500, organization: 100);
        AddRegionFaction(planet, enemyFront, defender, population: 100_000, organization: 100, garrison: 5_000);
        // The attacker must see the enemy front to perceive the threat that sizes needy's garrison.
        planet.PlanetFactionMap[attacker.Id].SetRegionAwareness(
            enemyFront, FactionThreatAssessment.GarrisonFullSightIntel);
        Sector sector = new(CreatePlayerForce(), [], [planet], []);

        new FactionStrategyController().GenerateFactionOrders(attacker, sector);

        // Needy is topped up to exactly its required garrison (5,000) and the rear paid for it.
        Assert.Equal(5_000, needy.RegionFactionMap[attacker.Id].MilitaryStrength);
        Assert.True(rear.RegionFactionMap[attacker.Id].MilitaryStrength < 10_000);
    }

    [Fact]
    public void ShouldUseStrategicCombat_PlayerTargetStaysTactical()
    {
        Faction attacker = CreateNonPlayerFaction();
        Faction player = CreatePlayerFaction();
        RegionFaction target = CreateTargetRegionFaction(player, population: 1_000, garrison: 100);
        var offensive = Offensive(target, attackForce: 100_000, estimatedDefenderBv: 100, reward: 1_000);

        Assert.False(FactionOffensiveOrderBuilder.ShouldUseStrategicCombat(attacker, offensive, committedBattleValue: 100_000));
    }

    [Fact]
    public void CalculateOffensiveReward_ConsumerAlsoValuesTheLand()
    {
        Faction consumer = BuildFaction(10, "Swarm", isPlayer: false, isDefault: false, GrowthType.Consumption);
        Faction raider = CreateNonPlayerFaction();
        RegionFaction target = CreateTargetRegionFaction(raider, population: 1000, carryingCapacity: 5000);

        // A devouring swarm counts the carrying capacity it will eat; a non-consumer only the population.
        Assert.Equal(6000.0, FactionOffensiveEvaluator.CalculateOffensiveReward(target, consumer, 1, 1));
        Assert.Equal(1000.0, FactionOffensiveEvaluator.CalculateOffensiveReward(target, raider, 1, 1));
    }

    [Fact]
    public void ChooseBestOffensive_PicksHighestRewardToRiskAmongWinnable()
    {
        Faction attacker = CreateNonPlayerFaction();
        // Easy but poor: winnable, low reward.
        var easy = Offensive(CreateTargetRegionFaction(attacker), attackForce: 1000, estimatedDefenderBv: 100, reward: 500);
        // Richer: also winnable, far better reward-to-risk despite a tougher defender.
        var rich = Offensive(CreateTargetRegionFaction(attacker), attackForce: 1000, estimatedDefenderBv: 200, reward: 5000);

        var chosen = FactionOffensiveEvaluator.ChooseBestOffensive([easy, rich]);

        Assert.Same(rich, chosen);
    }

    [Fact]
    public void ChooseBestOffensive_SkipsAnUnwinnableRichTargetForAWinnableOne()
    {
        // The old logic could pick a single "best" target and then a downstream strength check
        // would veto the whole turn; now an unwinnable target is simply excluded from selection.
        Faction attacker = CreateNonPlayerFaction();
        var unwinnable = Offensive(CreateTargetRegionFaction(attacker), attackForce: 100, estimatedDefenderBv: 1000, reward: 1_000_000_000);
        var winnable = Offensive(CreateTargetRegionFaction(attacker), attackForce: 1000, estimatedDefenderBv: 100, reward: 500);

        var chosen = FactionOffensiveEvaluator.ChooseBestOffensive([unwinnable, winnable]);

        Assert.Same(winnable, chosen);
    }

    [Fact]
    public void ChooseBestOffensive_ReturnsNullWhenNothingIsWinnable()
    {
        Faction attacker = CreateNonPlayerFaction();
        var unwinnable = Offensive(CreateTargetRegionFaction(attacker), attackForce: 100, estimatedDefenderBv: 1000, reward: 5000);

        Assert.Null(FactionOffensiveEvaluator.ChooseBestOffensive([unwinnable]));
    }

    // Removed with the mechanic: IsWinnable_ProvocationLowersTheRequiredForceRatio covered
    // RegionFaction.ProvocationLevel, which baited a counterattack by shaving this threshold during
    // NPC planning. The bait now lives in the diversion itself as Roll B
    // (DemonstrateForceMissionStep), which draws a real counterattack the same day from the feinting
    // player's own dice rather than nudging a planning threshold a turn later.

    [Fact]
    public void RewardRiskScore_PenalisesEntrenchedDefenders()
    {
        Faction attacker = CreateNonPlayerFaction();
        var open = Offensive(CreateTargetRegionFaction(attacker), attackForce: 1000, estimatedDefenderBv: 100, reward: 1000);
        var dugIn = Offensive(CreateTargetRegionFaction(attacker, entrenchment: 4), attackForce: 1000, estimatedDefenderBv: 100, reward: 1000);

        Assert.True(FactionOffensiveEvaluator.RewardRiskScore(open) > FactionOffensiveEvaluator.RewardRiskScore(dugIn));
    }

    // ----- Recon-before-invade: DecideOffensivePlan (PRD §4.24) -----

    [Fact]
    public void ChooseReconTarget_ScoutsTheRichestUnknown()
    {
        Faction attacker = CreateNonPlayerFaction();
        var poor = Offensive(CreateTargetRegionFaction(attacker), attackForce: 1000, estimatedDefenderBv: 100, reward: 500);
        var rich = Offensive(CreateTargetRegionFaction(attacker), attackForce: 1000, estimatedDefenderBv: 100, reward: 9000);

        Assert.Same(rich, FactionOffensiveEvaluator.ChooseReconTarget([poor, rich]));
    }

    [Fact]
    public void IsWellReconnoitred_TrueOnlyAtOrAboveThreshold()
    {
        Faction attacker = CreateNonPlayerFaction();
        RegionFaction rf = CreateTargetRegionFaction(attacker);
        var offensive = Offensive(rf, attackForce: 1, estimatedDefenderBv: 1, reward: 1);

        Assert.False(FactionOffensiveEvaluator.IsWellReconnoitred(offensive, attacker.Id));
        rf.PlanetFaction.AddRegionAwareness(rf.Region, FactionStrategyPlanningConstants.ReconIntelThreshold);
        Assert.True(FactionOffensiveEvaluator.IsWellReconnoitred(offensive, attacker.Id));
    }

    [Fact]
    public void ResolveReconResult_RaisesScoutingFactionsBelief_WithoutLeakingToPlayerFogOfWar()
    {
        Faction scout = CreateNonPlayerFaction();
        RegionFaction target = CreateTargetRegionFaction(scout);

        MissionAftermathProcessor.ResolveReconResult(scout, target, 1.5f);

        Assert.Equal(1.5f, target.PlanetFaction.GetRegionAwareness(target.Region));
        Assert.Equal(0f, target.Region.GetPlayerVisibleIntel());
    }

    [Fact]
    public void ResolveReconResult_PlayerReconFeedsPlayerVisibleRegionAwareness()
    {
        Faction player = CreatePlayerFaction();
        RegionFaction target = CreateTargetRegionFaction(CreateNonPlayerFaction());

        MissionAftermathProcessor.ResolveReconResult(player, target, 2f);

        Assert.True(target.Region.Planet.PlanetFactionMap.ContainsKey(player.Id));
        Assert.Equal(2f, target.Region.GetPlayerVisibleIntel());
    }

    [Fact]
    public void ResolveReconResult_ADeniedReconGainsNoBelief()
    {
        // A scout that learns nothing (detected and driven off => zero/negative Impact) raises no
        // belief — the natural denial that replaces the old flat interception override.
        Faction scout = CreateNonPlayerFaction();
        RegionFaction target = CreateTargetRegionFaction(scout);

        MissionAftermathProcessor.ResolveReconResult(scout, target, 0f);

        Assert.Equal(0f, target.PlanetFaction.GetRegionAwareness(target.Region));
    }

    // ----- Patrol as a counter-force (PRD §4.24) -----

    [Fact]
    public void GenerateFactionOrders_ClearsPreviousTurnsPatrolSquads_ButKeepsOtherLandedSquads()
    {
        // Patrol screens are transient AI forces; a new planning pass must discard the prior turn's
        // rather than let them accumulate, while leaving genuine landed squads alone.
        Faction faction = CreateNonPlayerFaction();
        // Organization 0 => no spare troops => no new patrol/offensive planning, isolating the clear.
        Sector sector = BuildSectorWithFactions((faction, population: 1000, organization: 0, isPublic: true));
        RegionFaction rf = sector.Planets.Values.First().Regions[0].RegionFactionMap[faction.Id];
        Squad patrol = LandedSquadWithOrder(rf, MissionType.Patrol);
        Squad garrison = LandedSquadWithOrder(rf, MissionType.DefenseInDepth);
        rf.LandedSquads.Add(patrol);
        rf.LandedSquads.Add(garrison);

        new FactionStrategyController().GenerateFactionOrders(faction, sector);

        Assert.DoesNotContain(patrol, rf.LandedSquads);
        Assert.Contains(garrison, rf.LandedSquads);
    }

    private static Squad LandedSquadWithOrder(RegionFaction rf, MissionType missionType)
    {
        Squad squad = TestModelFactory.CreateSquad("Screen",
            TestModelFactory.CreateSoldier(TestModelFactory.MarineTemplate));
        Order order = new([squad], true, false, Aggression.Cautious,
            new Mission(missionType, rf, 0));
        squad.CurrentOrders = order;
        return squad;
    }

    [Fact]
    public void GenerateFactionOrders_DefensiveOnly_BordersEnemyButNotAssaulted_BuildsListeningPosts()
    {
        // The PDF holds a region bordering an enemy-held (but not co-located) region. The world is
        // not yet formally under assault, but the PDF raises sensors on the threatened border so it
        // is not blind when the assault comes — listening posts only, never fortification/maneuver.
        Faction pdf = CreateDefaultFaction();
        Faction enemy = CreateNonPlayerFaction();

        Planet planet = CreatePlanet();
        Region pdfRegion = planet.Regions[0];
        Region enemyRegion = pdfRegion.GetAdjacentRegions().First();
        AddRegionFaction(planet, pdfRegion, pdf, population: 1_000_000, organization: 100, garrison: 2_000);
        AddRegionFaction(planet, enemyRegion, enemy, population: 1_000, organization: 100);
        Sector sector = new(CreatePlayerForce(), [], [planet], []);

        Assert.False(planet.IsUnderAssault());

        List<Order> orders = new FactionStrategyController()
            .GenerateFactionOrders(pdf, sector, defensiveOnly: true);

        Assert.NotEmpty(orders);
        Assert.All(orders, o => Assert.Empty(o.AssignedSquads));
        Assert.All(orders, o => Assert.Equal(DefenseType.ListeningPost, Assert.IsType<ConstructionMission>(o.Mission).ConstructionType));
    }

    [Fact]
    public void GenerateFactionOrders_DefensiveOnly_ThinRegionBuildsFractionalListeningPost()
    {
        // A border region whose spare force cannot cover a whole listening-post level (level 0
        // costs 2 build points = 200 troops) builds the fraction it can afford instead of
        // staying blind.
        //
        // 150 garrison at full organization is 150 deployable. The region can see nothing next door
        // (no intel is set on the enemy region, so CalculateRequiredDefensiveBattleValue's threat term
        // contributes nothing), so its reserve falls to the MinimumDefensiveReserveFraction floor of
        // 20% = 30. That leaves 120 spare, which buys 1.2 build points = 0.6 of a level. Before the
        // floor existed the region reserved nothing at all and spent all 150 on 0.75 of a level.
        Faction pdf = CreateDefaultFaction();
        Faction enemy = CreateNonPlayerFaction();

        Planet planet = CreatePlanet();
        Region pdfRegion = planet.Regions[0];
        Region enemyRegion = pdfRegion.GetAdjacentRegions().First();
        AddRegionFaction(planet, pdfRegion, pdf, population: 1_000_000, organization: 100, garrison: 150);
        AddRegionFaction(planet, enemyRegion, enemy, population: 1_000, organization: 100);
        Sector sector = new(CreatePlayerForce(), [], [planet], []);

        List<Order> orders = new FactionStrategyController()
            .GenerateFactionOrders(pdf, sector, defensiveOnly: true);

        Order order = Assert.Single(orders);
        ConstructionMission mission = Assert.IsType<ConstructionMission>(order.Mission);
        Assert.Equal(DefenseType.ListeningPost, mission.ConstructionType);
        Assert.Equal(0.6, mission.BuildAmount, precision: 6);
    }

    // The pool-reading fix. RegionFaction.Garrison is Imperial-specific - FactionRevealService zeroes it
    // when a cult or revolt reveals, and a PopulationIsMilitary horde never had one - so a defence sized
    // off raw Garrison came out at zero for every revealed non-Imperial faction. Sizing it off
    // GetDeployedStrength (which resolves through MilitaryStrength) is what makes a hive defend itself.
    [Fact]
    public void RequiredDefensiveBattleValue_HordeWithNoGarrison_IsStillNonZero()
    {
        RegionFaction horde = CreateTargetRegionFaction(CreateNonPlayerFaction(), population: 10_000);

        Assert.Equal(0, horde.Garrison);
        Assert.True(horde.GetDeployedStrength() > 0, "a horde's strength lives in Population");
        Assert.True(
            FactionThreatAssessment.CalculateRequiredDefensiveBattleValue(horde) > 0,
            "a region with fielded troops must hold some of them back to defend itself");
    }

    // With nothing visible next door the threat term contributes nothing, so the reserve is exactly the
    // floor. This is what stops an interior or blind region from materialising no defence at all.
    [Fact]
    public void RequiredDefensiveBattleValue_WithNoVisibleThreat_FallsBackToTheFloor()
    {
        RegionFaction quiet = CreateTargetRegionFaction(CreateNonPlayerFaction(), population: 10_000);

        long expected = (long)(quiet.GetDeployedStrength()
            * FactionThreatAssessment.MinimumDefensiveReserveFraction);

        Assert.Equal(expected, FactionThreatAssessment.CalculateRequiredDefensiveBattleValue(quiet));
    }

    // --- Feeding as a planned tasking (Design/Reference/ConsumptionFeedingAsMission.md) ---

    // Feeding used to be a planet-update side effect that recomputed the swarm's whole deployed
    // strength, so the same troops fed, defended and patrolled in the same week. It is now allocated
    // against the same per-region budget as everything else, and what reaches the allocator is the
    // residual - never the whole swarm.
    [Fact]
    public void GenerateFactionOrders_Swarm_CommitsOnlyItsSpareTroopsToFeeding()
    {
        Faction swarm = BuildFaction(2, "Hive Fleet", isPlayer: false, isDefault: false, GrowthType.Consumption);
        Sector sector = BuildSectorWithSingleRegionFaction(
            swarm, population: 1_000_000, organization: 100, isPublic: true);
        RegionFaction regionFaction =
            sector.Planets.Values.First().Regions[0].RegionFactionMap[swarm.Id];

        List<Order> orders = new FactionStrategyController().GenerateFactionOrders(swarm, sector);

        Order feedOrder = Assert.Single(orders, o => o.Mission is FeedMission);
        FeedMission mission = Assert.IsType<FeedMission>(feedOrder.Mission);
        Assert.Empty(feedOrder.AssignedSquads);
        Assert.Equal(regionFaction, mission.RegionFaction);

        // With no enemy visible the defensive reserve is exactly the minimum floor, so feeding gets
        // what is left of the deployed strength and nothing more.
        long reserve = (long)(regionFaction.GetDeployedStrength()
            * FactionThreatAssessment.MinimumDefensiveReserveFraction);
        Assert.Equal(regionFaction.GetDeployedStrength() - reserve, mission.CommittedBattleValue);
        Assert.True(mission.CommittedBattleValue < regionFaction.GetDeployedStrength(),
            "the swarm must never commit its whole deployed strength to feeding");
    }

    // A faction that does not eat biomass plans no feeding at all.
    [Fact]
    public void GenerateFactionOrders_NonConsumptionFaction_PlansNoFeeding()
    {
        Faction cult = CreateNonPlayerFaction();
        Sector sector = BuildSectorWithSingleRegionFaction(
            cult, population: 1_000_000, organization: 100, isPublic: true);

        List<Order> orders = new FactionStrategyController().GenerateFactionOrders(cult, sector);

        Assert.DoesNotContain(orders, o => o.Mission is FeedMission);
    }

    private static void AddRegionFaction(Planet planet, Region region, Faction faction,
        long population = 0, int organization = 100, long garrison = 0)
    {
        if (!planet.PlanetFactionMap.TryGetValue(faction.Id, out PlanetFaction planetFaction))
        {
            planetFaction = new PlanetFaction(faction) { IsPublic = true };
            planet.PlanetFactionMap[faction.Id] = planetFaction;
        }
        region.RegionFactionMap[faction.Id] = new RegionFaction(planetFaction, region)
        {
            Population = population,
            Organization = organization,
            Garrison = garrison,
            IsPublic = true
        };
    }

    private static PlanningPotentialOffensive Offensive(
        RegionFaction target, long attackForce, long estimatedDefenderBv, double reward)
    {
        return new PlanningPotentialOffensive
        {
            TargetRegion = target.Region,
            TargetFaction = target,
            AvailableAttackingForce = attackForce,
            EstimatedDefenderBattleValue = estimatedDefenderBv,
            DefenderBattleValue = estimatedDefenderBv,
            Reward = reward
        };
    }

    private static RegionFaction CreateTargetRegionFaction(Faction faction, long population = 0, long garrison = 0,
        int entrenchment = 0, long carryingCapacity = 0)
    {
        Planet planet = CreatePlanet();
        Region region = planet.Regions[0];
        region.CarryingCapacity = carryingCapacity;
        PlanetFaction planetFaction = new(faction) { IsPublic = true };
        planet.PlanetFactionMap[faction.Id] = planetFaction;
        RegionFaction regionFaction = new(planetFaction, region)
        {
            Population = population,
            Garrison = garrison,
            Entrenchment = entrenchment,
            IsPublic = true
        };
        region.RegionFactionMap[faction.Id] = regionFaction;
        return regionFaction;
    }

    private static Sector BuildSectorWithFactions(
        params (Faction faction, long population, int organization, bool isPublic)[] factions)
    {
        Planet planet = CreatePlanet();
        foreach ((Faction faction, long population, int organization, bool isPublic) in factions)
        {
            PlanetFaction planetFaction = new(faction) { IsPublic = isPublic };
            planet.PlanetFactionMap[faction.Id] = planetFaction;
            RegionFaction regionFaction = new(planetFaction, planet.Regions[0])
            {
                Population = population,
                Organization = organization,
                IsPublic = isPublic
            };
            planet.Regions[0].RegionFactionMap[faction.Id] = regionFaction;
        }
        return new Sector(CreatePlayerForce(), [], [planet], []);
    }

    private static Faction CreateDefaultFaction(int id = 3, string name = "Imperium")
    {
        return BuildFaction(id, name, isPlayer: false, isDefault: true);
    }

    private static Sector BuildSectorWithSingleRegionFaction(
        Faction faction, long population, int organization, bool isPublic)
    {
        Planet planet = CreatePlanet();
        PlanetFaction planetFaction = new(faction) { IsPublic = isPublic };
        planet.PlanetFactionMap[faction.Id] = planetFaction;

        RegionFaction regionFaction = new(planetFaction, planet.Regions[0])
        {
            Population = population,
            Organization = organization,
            IsPublic = isPublic
        };
        planet.Regions[0].RegionFactionMap[faction.Id] = regionFaction;

        return new Sector(CreatePlayerForce(), [], [planet], []);
    }

    private static PlayerForce CreatePlayerForce()
    {
        Faction playerFaction = CreatePlayerFaction();
        Fleet fleet = new("Test Fleet", null, null);
        Army army = new("Test Army", null, null, null, []);
        return new PlayerForce(playerFaction, army, fleet);
    }

    private static Planet CreatePlanet()
    {
        PlanetTemplate template = new(
            1,
            "Strategy Test World",
            1,
            new LogNormalValueTemplate { Floor = 1000, Scale = 0 },
            new LogNormalValueTemplate { Floor = 2000, Scale = 0 },
            new NormalizedValueTemplate { BaseValue = 1, StandardDeviation = 0 },
            new LinearValueTemplate { MinValue = 0, MaxValue = 0 });
        Planet planet = new(1, "Strategy Test World", new Coordinate(1, 1), 1, template, 1, 0);

        for (int i = 0; i < planet.Regions.Length; i++)
        {
            planet.Regions[i] = new Region(
                i,
                planet,
                0,
                $"Region {i}",
                RegionExtensions.GetCoordinatesFromRegionNumber(i),
                0);
        }

        return planet;
    }

    private static Faction CreateNonPlayerFaction(int id = 2, string name = "Test Cult")
    {
        return BuildFaction(id, name, isPlayer: false, isDefault: false);
    }

    private static Faction CreatePlayerFaction()
    {
        return BuildFaction(1, "Test Chapter", isPlayer: true, isDefault: false);
    }

    private static Faction BuildFaction(int id, string name, bool isPlayer, bool isDefault,
        GrowthType growthType = GrowthType.Conversion,
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
}
