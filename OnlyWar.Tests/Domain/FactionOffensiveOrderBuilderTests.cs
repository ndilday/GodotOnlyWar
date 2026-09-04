using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Strategy;
using OnlyWar.Models;
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
public sealed class FactionOffensiveOrderBuilderTests
{
    [Fact]
    public void IssueOffensive_WhenTacticalGenerationFails_ReturnsLiveForceButKeepsBudgetDebit()
    {
        (Faction attacker, RegionFaction source, PotentialOffensive offensive) = CreateScenario();
        RegionForceState state = new(source, 0, 0, 100, 0);
        List<Order> orders = [];
        FactionOffensiveOrderBuilder builder = new((request, random) => []);

        bool issued = builder.IssueOffensive(
            attacker,
            offensive,
            [state],
            orders,
            intendedBattleValue: 50,
            MissionType.Advance,
            Aggression.Normal,
            new FixedRNG());

        Assert.False(issued);
        Assert.Empty(orders);
        Assert.Equal(100, source.MilitaryStrength);
        // This asymmetry is deliberate legacy behavior: the live-pool refund does not rewrite the
        // already-consumed planning budget.
        Assert.Equal(50, state.SpareTroops);
    }

    [Fact]
    public void IssueOffensive_WhenTacticalGenerationFallsShort_ReturnsExcessToLargestContributor()
    {
        (Faction attacker, RegionFaction source, PotentialOffensive offensive) = CreateScenario();
        RegionForceState state = new(source, 0, 0, 100, 0);
        Squad partialSquad = TestModelFactory.CreateSquad(
            "Partial assault",
            TestModelFactory.CreateSoldier(TestModelFactory.MarineTemplate));
        FactionOffensiveOrderBuilder builder = new((request, random) => [partialSquad]);
        List<Order> orders = [];

        bool issued = builder.IssueOffensive(
            attacker,
            offensive,
            [state],
            orders,
            intendedBattleValue: 50,
            MissionType.Advance,
            Aggression.Normal,
            new FixedRNG());

        Order order = Assert.Single(orders);
        Assert.True(issued);
        Assert.Equal(MissionType.Advance, order.Mission.MissionType);
        Assert.Same(attacker, order.OwnerFaction);
        Assert.Same(source.Region, partialSquad.CurrentRegion);
        Assert.Equal(2, partialSquad.Members.Sum(member => member.Template.BattleValue));
        Assert.Equal(98, source.MilitaryStrength);
        Assert.Equal(50, state.SpareTroops);
    }

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
                AvailableAttackingForce = 100,
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
