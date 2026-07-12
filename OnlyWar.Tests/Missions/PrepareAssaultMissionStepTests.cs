using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Missions.Assault;
using OnlyWar.Helpers.StrategicCombat;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Missions;

public class PrepareAssaultMissionStepTests
{
    [Fact]
    public void RegionalDefenders_IncludeAlliedFactionButExcludeEnemy()
    {
        Faction player = CreateFaction(1, "Chapter", isPlayer: true);
        Faction imperial = CreateFaction(2, "Imperial", isDefault: true);
        Faction enemy = CreateFaction(3, "Cult");
        Planet planet = new(1, "Terra", new Coordinate(0, 0), 1, null, 0, 0);
        Region region = new(1, planet, 0, "Terra Lambda", new RegionCoordinate(0, 0), 0);
        planet.Regions[0] = region;
        RegionFaction target = AddPresence(region, imperial);
        RegionFaction playerPresence = AddPresence(region, player);
        RegionFaction enemyPresence = AddPresence(region, enemy);
        Squad alliedDefender = AddDefender(playerPresence, "Allied defenders");
        Squad enemyDefender = AddDefender(enemyPresence, "Enemy defenders");

        List<Squad> defenders = PrepareAssaultMissionStep.GetRegionalDefensiveSquads(target);

        Assert.Contains(alliedDefender, defenders);
        Assert.DoesNotContain(enemyDefender, defenders);
    }

    [Fact]
    public void AssembleDefendingForce_GarrisonBudgetUsesBattleValueDirectly()
    {
        var rules = RulesDatabaseFixture.LoadRules();
        Faction pdf = rules.Factions.Single(f => f.IsDefaultFaction);

        Planet planet = new(1, "Terra", new Coordinate(0, 0), 1, null, 0, 0);
        Region region = new(1, planet, 0, "Terra Lambda", new RegionCoordinate(0, 0), 0);
        planet.Regions[0] = region;
        RegionFaction target = AddPresence(region, pdf);
        target.Population = 10_000;
        target.Garrison = StrategicCombatRules.PdfTrooperBattleValue * 10;

        List<BattleSquad> defenders = new PrepareAssaultMissionStep()
            .AssembleDefendingForce(target, attackerMarginOfSuccess: 0f);

        long generatedBattleValue = defenders
            .SelectMany(squad => squad.Squad.Members)
            .Sum(soldier => (long)soldier.Template.BattleValue);

        Assert.NotEmpty(defenders);
        Assert.InRange(generatedBattleValue, 1, target.Garrison);
        Assert.True(generatedBattleValue < StrategicCombatRules.MassCombatBattleValueFloor);
    }

    private static RegionFaction AddPresence(Region region, Faction faction)
    {
        RegionFaction presence = new(new PlanetFaction(faction), region);
        region.RegionFactionMap[faction.Id] = presence;
        return presence;
    }

    private static Squad AddDefender(RegionFaction presence, string name)
    {
        Squad squad = TestModelFactory.CreateSquad(name, TestModelFactory.CreateSoldier());
        squad.CurrentOrders = new Order([squad], Disposition.DugIn, true, false,
            Aggression.Cautious, new Mission(MissionType.DefenseInDepth, presence, 0));
        presence.LandedSquads.Add(squad);
        return squad;
    }

    private static Faction CreateFaction(int id, string name, bool isPlayer = false, bool isDefault = false) =>
        new(id, name, Color.White, isPlayer, isDefault, false, GrowthType.Logistic,
            new Dictionary<int, OnlyWar.Models.Soldiers.Species>(),
            new Dictionary<int, OnlyWar.Models.Soldiers.SoldierTemplate>(),
            new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, OnlyWar.Models.Units.UnitTemplate>(),
            new Dictionary<int, OnlyWar.Models.Fleets.BoatTemplate>(),
            new Dictionary<int, OnlyWar.Models.Fleets.ShipTemplate>(),
            new Dictionary<int, OnlyWar.Models.Fleets.FleetTemplate>());
}
