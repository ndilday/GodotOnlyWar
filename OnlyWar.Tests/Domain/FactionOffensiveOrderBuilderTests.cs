using OnlyWar.Domain;
using OnlyWar.Domain.Extensions;
using OnlyWar.Campaign.Strategy;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;
using OnlyWar.Tests.Fixtures;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Xunit;

namespace OnlyWar.Tests.Domain;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public sealed class FactionOffensiveOrderBuilderTests
{
    [Fact]
    public void IssueAllocatedOffensive_WhenTacticalGenerationFails_ReturnsLiveForce()
    {
        (Faction attacker, RegionFaction source, PotentialOffensive offensive) = CreateScenario();
        List<Order> orders = [];
        FactionOffensiveOrderBuilder builder = new((request, random) => []);

        bool issued = builder.IssueAllocatedOffensive(
            attacker,
            offensive,
            [(source, 50L)],
            totalBattleValue: 50,
            MissionType.Advance,
            Aggression.Normal,
            orders,
            new FixedRNG());

        Assert.False(issued);
        Assert.Empty(orders);
        Assert.Equal(100, source.MilitaryStrength);
    }

    [Fact]
    public void IssueAllocatedOffensive_WhenTacticalGenerationFallsShort_ReturnsExcessToLargestContributor()
    {
        (Faction attacker, RegionFaction source, PotentialOffensive offensive) = CreateScenario();
        Squad partialSquad = TestModelFactory.CreateSquad(
            "Partial assault",
            TestModelFactory.CreateSoldier(TestModelFactory.MarineTemplate));
        FactionOffensiveOrderBuilder builder = new((request, random) => [partialSquad]);
        List<Order> orders = [];

        bool issued = builder.IssueAllocatedOffensive(
            attacker,
            offensive,
            [(source, 50L)],
            totalBattleValue: 50,
            MissionType.Advance,
            Aggression.Normal,
            orders,
            new FixedRNG());

        Order order = Assert.Single(orders);
        Assert.True(issued);
        Assert.Equal(MissionType.Advance, order.Mission.MissionType);
        Assert.Same(attacker, order.OwnerFaction);
        Assert.Same(source.Region, partialSquad.CurrentRegion);
        Assert.Equal(2, partialSquad.Members.Sum(member => member.Template.BattleValue));
        Assert.Equal(98, source.MilitaryStrength);
    }

    // Generation fields whole squads, so it can build more than the award. The staging region pays the
    // difference; before, nobody did, and the survivors credited back created battle value.
    [Fact]
    public void IssueAllocatedOffensive_WhenTacticalGenerationOvershoots_DrawsExcessFromStaging()
    {
        (Faction attacker, RegionFaction source, PotentialOffensive offensive) = CreateScenario();
        Squad largeSquad = CreateMarineSquad("Large assault", soldiers: 10);
        FactionOffensiveOrderBuilder builder = new((request, random) => [largeSquad]);
        List<Order> orders = [];

        bool issued = builder.IssueAllocatedOffensive(
            attacker,
            offensive,
            [(source, 4L)],
            totalBattleValue: 4,
            MissionType.Advance,
            Aggression.Normal,
            orders,
            new FixedRNG());

        Order order = Assert.Single(orders);
        Assert.True(issued);
        // 100 held, 4 awarded, 20 fielded: the region pays all 20.
        Assert.Equal(80, source.MilitaryStrength);
        Assert.Equal(1.0, order.SurvivorCreditFraction);
    }

    [Fact]
    public void IssueAllocatedOffensive_WhenStagingCannotPayTheOvershoot_CreditsOnlyThePaidShare()
    {
        (Faction attacker, RegionFaction source, PotentialOffensive offensive) = CreateScenario();
        source.Population = 10;
        Squad largeSquad = CreateMarineSquad("Large assault", soldiers: 10);
        FactionOffensiveOrderBuilder builder = new((request, random) => [largeSquad]);
        List<Order> orders = [];

        builder.IssueAllocatedOffensive(
            attacker,
            offensive,
            [(source, 4L)],
            totalBattleValue: 4,
            MissionType.Advance,
            Aggression.Normal,
            orders,
            new FixedRNG());

        Order order = Assert.Single(orders);
        // 10 held: 4 awarded and 6 more drawn, against 20 fielded.
        Assert.Equal(0, source.MilitaryStrength);
        Assert.Equal(0.5, order.SurvivorCreditFraction, precision: 6);
    }

    private static Squad CreateMarineSquad(string name, int soldiers) =>
        TestModelFactory.CreateSquad(
            name,
            Enumerable.Range(0, soldiers)
                .Select(_ => TestModelFactory.CreateSoldier(TestModelFactory.MarineTemplate))
                .ToArray());

    private static (Faction attacker, RegionFaction source, PotentialOffensive offensive) CreateScenario()
    {
        Faction attacker = CreateFaction(2, "Attacker", isPlayer: false);
        Faction player = CreateFaction(1, "Chapter", isPlayer: true);
        Planet planet = new(1, "Order Builder World", new Coordinate(1, 1), 1, null, 1, 0);
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

        Region sourceRegion = planet.Regions[0];
        Region targetRegion = sourceRegion.GetAdjacentRegions().First();
        PlanetFaction attackerPlanetFaction = new(attacker) { IsPublic = true };
        PlanetFaction playerPlanetFaction = new(player) { IsPublic = true };
        planet.PlanetFactionMap[attacker.Id] = attackerPlanetFaction;
        planet.PlanetFactionMap[player.Id] = playerPlanetFaction;
        RegionFaction source = new(attackerPlanetFaction, sourceRegion)
        {
            Population = 100,
            Organization = 100,
            IsPublic = true
        };
        RegionFaction target = new(playerPlanetFaction, targetRegion)
        {
            Population = 10,
            Garrison = 10,
            Organization = 100,
            IsPublic = true
        };
        sourceRegion.RegionFactionMap[attacker.Id] = source;
        targetRegion.RegionFactionMap[player.Id] = target;

        return (
            attacker,
            source,
            new PotentialOffensive
            {
                TargetRegion = targetRegion,
                TargetFaction = target,
                AttackingRegions = [sourceRegion],
                DefenderBattleValue = 10,
                EstimatedDefenderBattleValue = 10,
                Reward = 100
            });
    }

    private static Faction CreateFaction(int id, string name, bool isPlayer) =>
        new(
            id,
            name,
            Color.Red,
            isPlayer,
            isDefaultFaction: false,
            behavior: isPlayer ? FactionBehavior.None : FactionBehavior.PopulationIsMilitary,
            GrowthType.Conversion,
            new Dictionary<int, Species> { [TestModelFactory.HumanSpecies.Id] = TestModelFactory.HumanSpecies },
            new Dictionary<int, SoldierTemplate>(),
            new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, UnitTemplate>(),
            new Dictionary<int, BoatTemplate>(),
            new Dictionary<int, ShipTemplate>(),
            new Dictionary<int, FleetTemplate>());
}
