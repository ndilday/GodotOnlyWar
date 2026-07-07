using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Aftermath;
using OnlyWar.Helpers.Battles.Resolutions;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Xunit;

namespace OnlyWar.Tests.Battles;

public class BattleAftermathPolicyTests
{
    private static int _nextId = 1000;

    [Fact]
    public void Factory_PureNpcBattleUsesNoOpAftermath()
    {
        BattleSquad attackers = CreateBattleSquad(CreateFaction(10, "PDF", isPlayer: false), "PDF Squad", CreateSoldier("Trooper"));
        BattleSquad defenders = CreateBattleSquad(CreateFaction(11, "Cult", isPlayer: false), "Cult Mob", CreateSoldier("Cultist"));
        BattleHistory history = new();
        BattleAftermathContext context = new([attackers], [defenders], null, history);
        IBattleAftermathPolicy policy = BattleAftermathPolicyFactory.Create(context);
        WoundResolution wound = CreateRangedWound(attackers.Soldiers[0], defenders.Soldiers[0]);

        policy.OnSoldierKilled(wound, WoundLevel.Mortal);

        Assert.IsType<NpcBattleAftermathPolicy>(policy);
        Assert.Equal(0, history.EnemiesKilled);
        Assert.Equal((ushort)0, attackers.Soldiers[0].EnemiesTakenDown);
    }

    [Fact]
    public void PlayerBattleCreditsPlayerKillTracking()
    {
        Faction playerFaction = CreateFaction(20, "Chapter", isPlayer: true);
        Faction enemyFaction = CreateFaction(21, "Tyranids", isPlayer: false);
        PlayerSoldier player = CreatePlayerSoldier("Brother Test");
        BattleSquad attackers = CreateBattleSquad(playerFaction, "Strike Squad", player);
        BattleSquad defenders = CreateBattleSquad(enemyFaction, "Brood", CreateSoldier("Gaunt"));
        BattleHistory history = new();
        BattleAftermathContext context = new([attackers], [defenders], null, history);
        IBattleAftermathPolicy policy = BattleAftermathPolicyFactory.Create(context);
        RangedWeaponTemplate weapon = attackers.Soldiers[0].EquippedRangedWeapons[0].Template;
        WoundResolution wound = CreateRangedWound(attackers.Soldiers[0], defenders.Soldiers[0]);

        policy.OnSoldierKilled(wound, WoundLevel.Mortal);

        Assert.IsType<PlayerChapterBattleAftermathPolicy>(policy);
        Assert.Equal(1, history.EnemiesKilled);
        Assert.Equal((ushort)1, attackers.Soldiers[0].EnemiesTakenDown);
        Assert.Equal((ushort)1, player.RangedWeaponCasualtyCountMap[weapon.Id]);
    }

    private static WoundResolution CreateRangedWound(BattleSoldier inflicter, BattleSoldier sufferer)
    {
        WeaponTemplate weapon = inflicter.EquippedRangedWeapons[0].Template;
        HitLocation hitLocation = sufferer.Soldier.Body.HitLocations.First();
        return new WoundResolution(inflicter, weapon, sufferer, 10, hitLocation);
    }

    private static PlayerSoldier CreatePlayerSoldier(string name)
    {
        Soldier soldier = CreateSoldier(name);
        return new PlayerSoldier(soldier, name);
    }

    private static Soldier CreateSoldier(string name)
    {
        Soldier soldier = TestModelFactory.CreateSoldier(TestModelFactory.MarineTemplate, name);
        soldier.Id = _nextId++;
        return soldier;
    }

    private static BattleSquad CreateBattleSquad(Faction faction, string name, params ISoldier[] soldiers)
    {
        SquadTemplate template = CreateSquadTemplate(faction);
        Squad squad = new(name, null, template);
        foreach (ISoldier soldier in soldiers)
        {
            squad.AddSquadMember(soldier);
        }

        BattleSquad battleSquad = new(faction.IsPlayerFaction, squad);
        foreach (BattleSoldier soldier in battleSquad.Soldiers)
        {
            soldier.TopLeft = new System.Tuple<int, int>(_nextId++, 2);
            soldier.Orientation = 0;
        }
        return battleSquad;
    }

    private static SquadTemplate CreateSquadTemplate(Faction faction)
    {
        SquadTemplate template = new(
            _nextId++,
            $"{faction.Name} Test Squad",
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            [new SquadTemplateElement(TestModelFactory.MarineTemplate, 0, 4)],
            SquadTypes.None)
        {
            Faction = faction
        };
        return template;
    }

    private static Faction CreateFaction(int id, string name, bool isPlayer)
    {
        return new Faction(
            id,
            name,
            Color.Red,
            isPlayer,
            isDefaultFaction: false,
            canInfiltrate: false,
            GrowthType.None,
            new Dictionary<int, Species> { [TestModelFactory.HumanSpecies.Id] = TestModelFactory.HumanSpecies },
            new Dictionary<int, SoldierTemplate> { [TestModelFactory.MarineTemplate.Id] = TestModelFactory.MarineTemplate },
            new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, Models.Units.UnitTemplate>(),
            new Dictionary<int, Models.Fleets.BoatTemplate>(),
            new Dictionary<int, Models.Fleets.ShipTemplate>(),
            new Dictionary<int, Models.Fleets.FleetTemplate>());
    }
}
