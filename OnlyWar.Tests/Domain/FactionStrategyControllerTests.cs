using System.Collections.Generic;
using System.Drawing;
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

namespace OnlyWar.Tests.Domain;

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
    public void GenerateFactionOrders_SpendsSpareTroopsOnDefensiveConstruction()
    {
        Faction enemy = CreateNonPlayerFaction();
        // No adjacent enemy => zero required garrison => the full organized force is spare.
        // The faction has no squad templates, so the only achievable orders are construction.
        Sector sector = BuildSectorWithSingleRegionFaction(enemy, population: 1000, organization: 100, isPublic: true);

        List<Order> orders = new FactionStrategyController().GenerateFactionOrders(enemy, sector);

        Assert.NotEmpty(orders);
        Assert.All(orders, o => Assert.IsType<ConstructionMission>(o.Mission));
        Assert.All(orders, o => Assert.Empty(o.AssignedSquads));
    }

    [Fact]
    public void GenerateFactionOrders_PerceivedThreatBonusPinsGarrisonAndSuppressesActivity()
    {
        Faction enemy = CreateNonPlayerFaction();
        // Org 100, pop 1000 => 1000 organized troops, and no adjacent enemy, so normally the full
        // force is spare and the faction builds defenses. A diversion's perceived-threat bonus that
        // exceeds the organized force should make the region feel it must hold everything as
        // garrison, leaving nothing spare and producing no orders.
        Sector sector = BuildSectorWithSingleRegionFaction(enemy, population: 1000, organization: 100, isPublic: true);
        RegionFaction regionFaction = sector.Planets.Values.First().Regions[0].RegionFactionMap[enemy.Id];
        regionFaction.PerceivedThreatBonus = 2000;

        List<Order> orders = new FactionStrategyController().GenerateFactionOrders(enemy, sector);

        Assert.Empty(orders);
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
        Sector sector = BuildSectorWithFactions(
            (pdf, population: 1_000_000, organization: 100, isPublic: true),
            (enemy, population: 1_000, organization: 100, isPublic: true));

        List<Order> orders = new FactionStrategyController()
            .GenerateFactionOrders(pdf, sector, defensiveOnly: true);

        Assert.NotEmpty(orders);
        // Defensive only: every order is a fortification / listening-post build, never an offensive.
        Assert.All(orders, o => Assert.IsType<ConstructionMission>(o.Mission));
        Assert.All(orders, o => Assert.Empty(o.AssignedSquads));
    }

    // ----- Q2: reward/risk offensive targeting (PRD §4.24) -----

    [Fact]
    public void ApplyIntelNoise_WithZeroDeviation_ReturnsTrueStrength()
    {
        Assert.Equal(1000L, FactionStrategyController.ApplyIntelNoise(1000, intelLevel: 0f, zValue: 0.0));
    }

    [Fact]
    public void ApplyIntelNoise_BetterIntelTightensTheEstimate()
    {
        // Same over-estimate (z = +1), but more intel on the target shrinks the 1-sigma error.
        long blindGuess = FactionStrategyController.ApplyIntelNoise(1000, intelLevel: 0f, zValue: 1.0); // sigma 0.5 -> 1500
        long scoutedGuess = FactionStrategyController.ApplyIntelNoise(1000, intelLevel: 4f, zValue: 1.0); // sigma 0.1 -> 1100

        Assert.Equal(1500L, blindGuess);
        Assert.Equal(1100L, scoutedGuess);
        Assert.True(scoutedGuess < blindGuess);
    }

    [Fact]
    public void ApplyIntelNoise_ClampsToANonZeroFloor()
    {
        // A wild under-estimate must not drive the believed strength to zero or negative.
        Assert.Equal(100L, FactionStrategyController.ApplyIntelNoise(1000, intelLevel: 0f, zValue: -10.0));
    }

    [Fact]
    public void CalculateDefenderBattleValue_WeightsLandedSquadsByBattleValueNotHeadcount()
    {
        Faction defender = CreateNonPlayerFaction();
        RegionFaction rf = CreateTargetRegionFaction(defender, garrison: 50);
        // Three landed soldiers, each worth 2 battle value: 6 points, not 3 bodies.
        rf.LandedSquads.Add(TestModelFactory.CreateSquad("Defenders",
            TestModelFactory.CreateSoldier(TestModelFactory.MarineTemplate),
            TestModelFactory.CreateSoldier(TestModelFactory.MarineTemplate),
            TestModelFactory.CreateSoldier(TestModelFactory.MarineTemplate)));

        Assert.Equal(56L, FactionStrategyController.CalculateDefenderBattleValue(rf));
    }

    [Fact]
    public void CalculateOffensiveReward_ConsumerAlsoValuesTheLand()
    {
        Faction consumer = BuildFaction(10, "Swarm", isPlayer: false, isDefault: false, GrowthType.Consumption);
        Faction raider = CreateNonPlayerFaction();
        RegionFaction target = CreateTargetRegionFaction(raider, population: 1000, carryingCapacity: 5000);

        // A devouring swarm counts the carrying capacity it will eat; a non-consumer only the population.
        Assert.Equal(6000.0, FactionStrategyController.CalculateOffensiveReward(target, consumer));
        Assert.Equal(1000.0, FactionStrategyController.CalculateOffensiveReward(target, raider));
    }

    [Fact]
    public void ChooseBestOffensive_PicksHighestRewardToRiskAmongWinnable()
    {
        Faction attacker = CreateNonPlayerFaction();
        // Easy but poor: winnable, low reward.
        var easy = Offensive(CreateTargetRegionFaction(attacker), attackForce: 1000, estimatedDefenderBv: 100, reward: 500);
        // Richer: also winnable, far better reward-to-risk despite a tougher defender.
        var rich = Offensive(CreateTargetRegionFaction(attacker), attackForce: 1000, estimatedDefenderBv: 200, reward: 5000);

        var chosen = FactionStrategyController.ChooseBestOffensive([easy, rich]);

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

        var chosen = FactionStrategyController.ChooseBestOffensive([unwinnable, winnable]);

        Assert.Same(winnable, chosen);
    }

    [Fact]
    public void ChooseBestOffensive_ReturnsNullWhenNothingIsWinnable()
    {
        Faction attacker = CreateNonPlayerFaction();
        var unwinnable = Offensive(CreateTargetRegionFaction(attacker), attackForce: 100, estimatedDefenderBv: 1000, reward: 5000);

        Assert.Null(FactionStrategyController.ChooseBestOffensive([unwinnable]));
    }

    [Fact]
    public void IsWinnable_ProvocationLowersTheRequiredForceRatio()
    {
        Faction attacker = CreateNonPlayerFaction();
        // 1200 vs 1000: short of the 1.5x edge normally required, but enough at parity.
        var calm = Offensive(CreateTargetRegionFaction(attacker), attackForce: 1200, estimatedDefenderBv: 1000, reward: 100);
        RegionFaction baited = CreateTargetRegionFaction(attacker, provocation: 5);
        var provoked = Offensive(baited, attackForce: 1200, estimatedDefenderBv: 1000, reward: 100);

        Assert.False(FactionStrategyController.IsWinnable(calm));
        Assert.True(FactionStrategyController.IsWinnable(provoked));
    }

    [Fact]
    public void RewardRiskScore_PenalisesEntrenchedDefenders()
    {
        Faction attacker = CreateNonPlayerFaction();
        var open = Offensive(CreateTargetRegionFaction(attacker), attackForce: 1000, estimatedDefenderBv: 100, reward: 1000);
        var dugIn = Offensive(CreateTargetRegionFaction(attacker, entrenchment: 4), attackForce: 1000, estimatedDefenderBv: 100, reward: 1000);

        Assert.True(FactionStrategyController.RewardRiskScore(open) > FactionStrategyController.RewardRiskScore(dugIn));
    }

    // ----- Recon-before-invade: DecideOffensivePlan (PRD §4.24) -----

    [Fact]
    public void DecideOffensivePlan_WinnableButUnknownTarget_ReconsBeforeCommitting()
    {
        Faction attacker = CreateNonPlayerFaction();
        // Winnable on paper, but the attacker has no intelligence on it, so it scouts first.
        var target = Offensive(CreateTargetRegionFaction(attacker), attackForce: 1000, estimatedDefenderBv: 100, reward: 5000);

        var (plan, chosen) = FactionStrategyController.DecideOffensivePlan([target], attacker.Id);

        Assert.Equal(FactionStrategyController.OffensivePlan.Recon, plan);
        Assert.Same(target, chosen);
    }

    [Fact]
    public void DecideOffensivePlan_WellReconnoitredWinnableTarget_Assaults()
    {
        Faction attacker = CreateNonPlayerFaction();
        RegionFaction known = CreateTargetRegionFaction(attacker);
        known.AddObserverIntel(attacker.Id, FactionStrategyController.ReconIntelThreshold);
        var target = Offensive(known, attackForce: 1000, estimatedDefenderBv: 100, reward: 5000);

        var (plan, chosen) = FactionStrategyController.DecideOffensivePlan([target], attacker.Id);

        Assert.Equal(FactionStrategyController.OffensivePlan.Assault, plan);
        Assert.Same(target, chosen);
    }

    [Fact]
    public void DecideOffensivePlan_KnownTargetTooStrong_ReconsAPromisingUnknownInstead()
    {
        Faction attacker = CreateNonPlayerFaction();
        RegionFaction known = CreateTargetRegionFaction(attacker);
        known.AddObserverIntel(attacker.Id, FactionStrategyController.ReconIntelThreshold);
        var tooStrong = Offensive(known, attackForce: 100, estimatedDefenderBv: 1000, reward: 9000);
        var unknownRich = Offensive(CreateTargetRegionFaction(attacker), attackForce: 1000, estimatedDefenderBv: 100, reward: 4000);

        var (plan, chosen) = FactionStrategyController.DecideOffensivePlan([tooStrong, unknownRich], attacker.Id);

        Assert.Equal(FactionStrategyController.OffensivePlan.Recon, plan);
        Assert.Same(unknownRich, chosen);
    }

    [Fact]
    public void DecideOffensivePlan_OnlyKnownUnwinnableTargets_Holds()
    {
        Faction attacker = CreateNonPlayerFaction();
        RegionFaction known = CreateTargetRegionFaction(attacker);
        known.AddObserverIntel(attacker.Id, FactionStrategyController.ReconIntelThreshold);
        var tooStrong = Offensive(known, attackForce: 100, estimatedDefenderBv: 1000, reward: 9000);

        var (plan, chosen) = FactionStrategyController.DecideOffensivePlan([tooStrong], attacker.Id);

        Assert.Equal(FactionStrategyController.OffensivePlan.None, plan);
        Assert.Null(chosen);
    }

    [Fact]
    public void DecideOffensivePlan_NoOffensives_Holds()
    {
        var (plan, chosen) = FactionStrategyController.DecideOffensivePlan([], attackerFactionId: 2);

        Assert.Equal(FactionStrategyController.OffensivePlan.None, plan);
        Assert.Null(chosen);
    }

    [Fact]
    public void ChooseReconTarget_ScoutsTheRichestUnknown()
    {
        Faction attacker = CreateNonPlayerFaction();
        var poor = Offensive(CreateTargetRegionFaction(attacker), attackForce: 1000, estimatedDefenderBv: 100, reward: 500);
        var rich = Offensive(CreateTargetRegionFaction(attacker), attackForce: 1000, estimatedDefenderBv: 100, reward: 9000);

        Assert.Same(rich, FactionStrategyController.ChooseReconTarget([poor, rich]));
    }

    [Fact]
    public void IsWellReconnoitred_TrueOnlyAtOrAboveThreshold()
    {
        Faction attacker = CreateNonPlayerFaction();
        RegionFaction rf = CreateTargetRegionFaction(attacker);
        var offensive = Offensive(rf, attackForce: 1, estimatedDefenderBv: 1, reward: 1);

        Assert.False(FactionStrategyController.IsWellReconnoitred(offensive, attacker.Id));
        rf.AddObserverIntel(attacker.Id, FactionStrategyController.ReconIntelThreshold);
        Assert.True(FactionStrategyController.IsWellReconnoitred(offensive, attacker.Id));
    }

    [Fact]
    public void ResolveReconResult_RaisesScoutingFactionsBelief_WithoutLeakingToPlayerFogOfWar()
    {
        Faction scout = CreateNonPlayerFaction();
        RegionFaction target = CreateTargetRegionFaction(scout);
        float intelBefore = target.Region.IntelligenceLevel;

        TurnController.ResolveReconResult(scout, target, 1.5f);

        Assert.Equal(1.5f, target.GetObserverIntel(scout.Id));
        // Enemy recon does not raise the shared fog-of-war level the player sees.
        Assert.Equal(intelBefore, target.Region.IntelligenceLevel);
    }

    [Fact]
    public void ResolveReconResult_PlayerReconAlsoFeedsTheSharedIntelligenceLevel()
    {
        Faction player = CreatePlayerFaction();
        RegionFaction target = CreateTargetRegionFaction(player);
        float intelBefore = target.Region.IntelligenceLevel;

        TurnController.ResolveReconResult(player, target, 2f);

        Assert.Equal(intelBefore + 2f, target.Region.IntelligenceLevel);
    }

    [Fact]
    public void ResolveReconResult_ADeniedReconGainsNoBelief()
    {
        // A scout that learns nothing (detected and driven off => zero/negative Impact) raises no
        // belief — the natural denial that replaces the old flat interception override.
        Faction scout = CreateNonPlayerFaction();
        RegionFaction target = CreateTargetRegionFaction(scout);

        TurnController.ResolveReconResult(scout, target, 0f);

        Assert.Equal(0f, target.GetObserverIntel(scout.Id));
    }

    // ----- Patrol as a counter-force (PRD §4.24) -----

    [Fact]
    public void GetPatrolStealthPenalty_ZeroWhenUnpatrolled_PositiveWhenPatrolled()
    {
        Faction faction = CreateNonPlayerFaction();
        RegionFaction rf = CreateTargetRegionFaction(faction);
        Assert.Equal(0f, rf.GetPatrolStealthPenalty());

        // A non-patrol defensive squad is not an active screen — no stealth penalty.
        rf.LandedSquads.Add(LandedSquadWithOrder(rf, MissionType.DefenseInDepth));
        Assert.Equal(0f, rf.GetPatrolStealthPenalty());

        // A standing patrol makes the region measurably harder to scout unseen.
        rf.LandedSquads.Add(LandedSquadWithOrder(rf, MissionType.Patrol));
        Assert.True(rf.GetPatrolStealthPenalty() >= RegionFactionExtensions.PatrolActiveScreenBonus);
    }

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
        Order order = new([squad], Disposition.DugIn, true, false, Aggression.Cautious,
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
        AddRegionFaction(planet, pdfRegion, pdf, population: 1_000_000, organization: 100);
        AddRegionFaction(planet, enemyRegion, enemy, population: 1_000, organization: 100);
        Sector sector = new(CreatePlayerForce(), [], [planet], []);

        Assert.False(planet.IsUnderAssault());

        List<Order> orders = new FactionStrategyController()
            .GenerateFactionOrders(pdf, sector, defensiveOnly: true);

        Assert.NotEmpty(orders);
        Assert.All(orders, o => Assert.Empty(o.AssignedSquads));
        Assert.All(orders, o => Assert.Equal(DefenseType.Detection, Assert.IsType<ConstructionMission>(o.Mission).ConstructionType));
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

    private static FactionStrategyController.PotentialOffensive Offensive(
        RegionFaction target, long attackForce, long estimatedDefenderBv, double reward)
    {
        return new FactionStrategyController.PotentialOffensive
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
        int entrenchment = 0, float provocation = 0, long carryingCapacity = 0)
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
            ProvocationLevel = provocation,
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
        GrowthType growthType = GrowthType.Conversion)
    {
        return new Faction(
            id,
            name,
            Color.Red,
            isPlayer,
            isDefault,
            false,
            growthType,
            new Dictionary<int, Species> { [TestModelFactory.HumanSpecies.Id] = TestModelFactory.HumanSpecies },
            new Dictionary<int, SoldierTemplate>(),
            new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, UnitTemplate>(),
            new Dictionary<int, BoatTemplate>(),
            new Dictionary<int, ShipTemplate>(),
            new Dictionary<int, FleetTemplate>());
    }
}
