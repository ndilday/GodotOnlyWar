using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Aftermath;
using OnlyWar.Helpers.Battles.Resolutions;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using System;
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
        BattleAftermathContext context = new(
            [attackers], [defenders], null, history, CreateDependencies());
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
        BattleAftermathContext context = new(
            [attackers], [defenders], null, history, CreateDependencies());
        IBattleAftermathPolicy policy = BattleAftermathPolicyFactory.Create(context);
        RangedWeaponTemplate weapon = attackers.Soldiers[0].EquippedRangedWeapons[0].Template;
        WoundResolution wound = CreateRangedWound(attackers.Soldiers[0], defenders.Soldiers[0]);

        policy.OnSoldierKilled(wound, WoundLevel.Mortal);

        Assert.IsType<PlayerChapterBattleAftermathPolicy>(policy);
        Assert.Equal(1, history.EnemiesKilled);
        Assert.Equal((ushort)1, attackers.Soldiers[0].EnemiesTakenDown);
        Assert.Equal((ushort)1, player.RangedWeaponCasualtyCountMap[weapon.Id]);
    }

    [Fact]
    public void PlayerBattleCreditsOneTakedownWhenAnEnemyBothFallsAndDies()
    {
        Faction playerFaction = CreateFaction(25, "Chapter", isPlayer: true);
        Faction enemyFaction = CreateFaction(26, "Tyranids", isPlayer: false);
        PlayerSoldier player = CreatePlayerSoldier("Brother Thorough");
        BattleSquad attackers = CreateBattleSquad(playerFaction, "Strike Squad", player);
        BattleSquad defenders = CreateBattleSquad(enemyFaction, "Brood", CreateSoldier("Gaunt"));
        BattleHistory history = new();
        BattleAftermathContext context = new(
            [attackers], [defenders], null, history, CreateDependencies());
        IBattleAftermathPolicy policy = BattleAftermathPolicyFactory.Create(context);
        BattleSoldier killer = attackers.Soldiers[0];
        BattleSoldier victim = defenders.Soldiers[0];
        RangedWeaponTemplate rangedWeapon = killer.EquippedRangedWeapons[0].Template;

        // Hit locations resolve independently, so a single wound-resolution pass can take this
        // gaunt's leg off (the fall hook) and cripple his chest (the death hook). He is still one
        // enemy, and must be credited to his killer once.
        policy.OnSoldierDowned(CreateRangedWound(killer, victim), WoundLevel.Critical);
        policy.OnSoldierKilled(CreateMeleeWound(killer, victim), WoundLevel.Mortal);

        Assert.Equal((ushort)1, killer.EnemiesTakenDown);
        Assert.Equal((ushort)1, player.RangedWeaponCasualtyCountMap[rangedWeapon.Id]);
        Assert.Empty(player.MeleeWeaponCasualtyCountMap);
        // The aggregate is deliberately per-hit and is not deduplicated.
        Assert.Equal(1, history.EnemiesKilled);
    }

    [Fact]
    public void PlayerBattleDoesNotCreditFriendlyFireAsAnEnemyKill()
    {
        Faction playerFaction = CreateFaction(30, "Chapter", isPlayer: true);
        Faction enemyFaction = CreateFaction(31, "Tyranids", isPlayer: false);
        PlayerSoldier player = CreatePlayerSoldier("Brother Incinerator");
        Soldier ally = CreateSoldier("Attached Guardsman");
        BattleSquad attackers = CreateBattleSquad(
            playerFaction,
            "Mixed Strike Squad",
            player,
            ally);
        BattleSquad defenders = CreateBattleSquad(
            enemyFaction,
            "Brood",
            CreateSoldier("Gaunt"));
        BattleHistory history = new();
        BattleAftermathContext context = new(
            [attackers], [defenders], null, history, CreateDependencies());
        IBattleAftermathPolicy policy = BattleAftermathPolicyFactory.Create(context);
        WoundResolution wound = CreateRangedWound(attackers.Soldiers[0], attackers.Soldiers[1]);

        policy.OnSoldierKilled(wound, WoundLevel.Mortal);

        Assert.Equal(0, history.EnemiesKilled);
        Assert.Equal((ushort)0, attackers.Soldiers[0].EnemiesTakenDown);
        Assert.Empty(player.RangedWeaponCasualtyCountMap);
    }

    [Fact]
    public void PlayerBattleCompletion_UsesRecordingSinkWithoutMutatingCampaignArmy()
    {
        Date battleDate = new(1, 10, 1);
        RecordingPlayerBattleAftermathSink sink = new();
        BattleAftermathDependencies dependencies = new(battleDate, new FixedRNG(), sink);
        Faction playerFaction = CreateFaction(40, "Chapter", isPlayer: true);
        Faction enemyFaction = CreateFaction(41, "Orks", isPlayer: false);
        PlayerSoldier player = CreatePlayerSoldier("Brother Reliquary");
        player.ProgenoidImplantDate = new Date(1, 1, 1);
        BattleSquad attackers = CreateBattleSquad(playerFaction, "Strike Squad", player);
        BattleSquad defenders = CreateBattleSquad(enemyFaction, "Warband", CreateSoldier("Boy"));
        Models.Army campaignArmy = new("Test Army", null, null, null, [player]);
        Squad assignedSquad = player.AssignedSquad;
        Region region = CreateRegion("Ash Wastes", "Calth");
        BattleAftermathContext context = new(
            [attackers], [defenders], region, new BattleHistory(), dependencies);
        IBattleAftermathPolicy policy = BattleAftermathPolicyFactory.Create(context);
        BattleState finalState = new(
            new Dictionary<int, BattleSquad> { [attackers.Id] = attackers },
            new Dictionary<int, BattleSquad> { [defenders.Id] = defenders });
        HitLocation vitalLocation = player.Body.HitLocations.First(
            location => location.Template.IsVital && !location.Template.HoldsProgenoid);
        vitalLocation.Wounds.AddWound(WoundLevel.Massive);

        policy.OnBattleCompleted(finalState);

        Assert.Same(player, Assert.Single(sink.FallenBrothers));
        Assert.Single(sink.RecoveredGeneseedPurities);
        Assert.Equal(GeneseedRules.FoundingPurity - GeneseedRules.RecoveredPurityDrift,
            sink.RecoveredGeneseedPurities[0], 4);
        (Date date, string title, List<string> subEvents) = Assert.Single(sink.BattleHistoryEntries);
        Assert.Same(battleDate, date);
        Assert.Equal("A skirmish in Ash Wastes, Calth", title);
        Assert.Contains(subEvents, entry => entry.Contains("Geneseed: Recovered"));

        Assert.Same(player, campaignArmy.PlayerSoldierMap[player.Id]);
        Assert.Empty(campaignArmy.FallenBrothers);
        Assert.Same(assignedSquad, player.AssignedSquad);
        Assert.Contains(player, assignedSquad.Members);
        Assert.All(player.SoldierEvents, soldierEvent => Assert.Equal(battleDate, soldierEvent.Date));
    }

    [Fact]
    public void PlayerBattleAftermathSink_AppliesCampaignEffectsAtBoundary()
    {
        Faction playerFaction = CreateFaction(50, "Chapter", isPlayer: true);
        PlayerSoldier player = CreatePlayerSoldier("Brother Boundary");
        BattleSquad battleSquad = CreateBattleSquad(playerFaction, "Strike Squad", player);
        Squad assignedSquad = battleSquad.Squad;
        Models.Army army = new("Test Army", null, null, null, [player]);
        PlayerForce force = new(playerFaction, army, null);
        PlayerBattleAftermathSink sink = new(force);
        Date date = new(1, 2, 3);

        sink.MoveToFallenBrothers(player);
        sink.AddRecoveredGeneseed(0.96f);
        sink.AddToBattleHistory(date, "Test battle", ["Test event"]);

        Assert.DoesNotContain(player.Id, army.PlayerSoldierMap.Keys);
        Assert.Same(player, army.FallenBrothers[player.Id]);
        Assert.Null(player.AssignedSquad);
        Assert.DoesNotContain(player, assignedSquad.Members);
        Assert.Equal((ushort)1, force.GeneseedStockpile);
        Assert.Equal(0.96f, force.GeneseedPurity, 4);
        EventHistory history = Assert.Single(force.BattleHistory[date]);
        Assert.Equal("Test battle", history.EventTitle);
        Assert.Equal("Test event", Assert.Single(history.SubEvents));
    }

    [Fact]
    public void PlayerBattleAftermathSink_MissingPlayerForceFailsWhenPlayerEffectIsApplied()
    {
        PlayerBattleAftermathSink sink = new(null);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            sink.AddToBattleHistory(new Date(1, 2, 3), "Test battle", ["Test event"]));

        Assert.Contains("requires a player force", exception.Message);
    }

    [Fact]
    public void PlayerBattleCompletion_UnarmedExperienceUsesSpeciesDefaultWeaponSkill()
    {
        Faction playerFaction = CreateFaction(60, "Chapter", isPlayer: true);
        Faction enemyFaction = CreateFaction(61, "Necrons", isPlayer: false);
        PlayerSoldier player = CreatePlayerSoldier("Brother Pugilist");
        BattleSquad attackers = CreateBattleSquad(playerFaction, "Strike Squad", player);
        BattleSquad defenders = CreateBattleSquad(enemyFaction, "Phalanx", CreateSoldier("Warrior"));
        BattleSoldier battleSoldier = attackers.Soldiers[0];
        battleSoldier.MeleeWeapons.Clear();
        battleSoldier.ClearReadiedMeleeWeapons();
        // Melee skill XP is now the roll-based accrual banked on the BattleSoldier, not a function of
        // TurnsSwinging; an unarmed fighter's accrual must still route to the species default weapon skill.
        battleSoldier.MeleeSkillXp = 0.05f;
        BattleAftermathContext context = new(
            [attackers],
            [defenders],
            CreateRegion("Catacombs", "Sanctum"),
            new BattleHistory(),
            CreateDependencies());
        IBattleAftermathPolicy policy = BattleAftermathPolicyFactory.Create(context);
        BattleState finalState = new(
            new Dictionary<int, BattleSquad> { [attackers.Id] = attackers },
            new Dictionary<int, BattleSquad> { [defenders.Id] = defenders });
        BaseSkill unarmedSkill = player.Template.Species.DefaultUnarmedWeapon.RelatedSkill;

        policy.OnBattleCompleted(finalState);

        Skill awardedSkill = Assert.Single(
            player.Skills,
            skill => ReferenceEquals(skill.BaseSkill, unarmedSkill));
        Assert.Equal(0.05f, awardedSkill.PointsInvested, 6);
    }

    [Fact]
    public void PlayerBattleCompletion_RecordsOneDeathEventNamingTheFirstMortalWound()
    {
        Faction playerFaction = CreateFaction(70, "Chapter", isPlayer: true);
        Faction enemyFaction = CreateFaction(71, "Genestealer Cult", isPlayer: false);
        PlayerSoldier player = CreatePlayerSoldier("Brother Twice-Slain");
        BattleSquad attackers = CreateBattleSquad(playerFaction, "Strike Squad", player);
        BattleSquad defenders = CreateBattleSquad(enemyFaction, "Brood", CreateSoldier("Acolyte"));
        BattleAftermathContext context = new(
            [attackers], [defenders], CreateRegion("Hive Xi", "Brendine II"),
            new BattleHistory(), CreateDependencies());
        IBattleAftermathPolicy policy = BattleAftermathPolicyFactory.Create(context);
        BattleSoldier killer = defenders.Soldiers[0];
        BattleSoldier victim = attackers.Soldiers[0];
        WeaponTemplate firstWeapon = killer.EquippedRangedWeapons[0].Template;

        // Several mortal wounds can land before the wound queue drains, but only one brother died.
        policy.OnSoldierKilled(CreateRangedWound(killer, victim), WoundLevel.Mortal);
        policy.OnSoldierKilled(CreateMeleeWound(killer, victim), WoundLevel.Unsurvivable);
        SeverVitalLocation(player);

        policy.OnBattleCompleted(CreateFinalState(attackers, defenders));

        SoldierEvent death = Assert.Single(
            player.SoldierEvents, soldierEvent => soldierEvent.Type == SoldierEventType.Death);
        Assert.Equal(enemyFaction.Id, death.FactionId);
        Assert.Equal(firstWeapon.Id, death.WeaponTemplateId);
        Assert.Contains("Killed in battle with the Genestealer Cult", death.Render());
    }

    [Fact]
    public void PlayerBattleCompletion_SurvivorOfAMortalWoundIsNotRecordedAsKilled()
    {
        RecordingPlayerBattleAftermathSink sink = new();
        BattleAftermathDependencies dependencies = new(new Date(1, 1, 1), new FixedRNG(), sink);
        Faction playerFaction = CreateFaction(72, "Chapter", isPlayer: true);
        Faction enemyFaction = CreateFaction(73, "Genestealer Cult", isPlayer: false);
        PlayerSoldier player = CreatePlayerSoldier("Brother Wayn");
        BattleSquad attackers = CreateBattleSquad(playerFaction, "Strike Squad", player);
        BattleSquad defenders = CreateBattleSquad(enemyFaction, "Brood", CreateSoldier("Acolyte"));
        BattleAftermathContext context = new(
            [attackers], [defenders], CreateRegion("Hive Xi", "Brendine II"),
            new BattleHistory(), dependencies);
        IBattleAftermathPolicy policy = BattleAftermathPolicyFactory.Create(context);

        // The wound resolver raises its death hook on a crippled vital location; nothing here is
        // severed, so this brother is gravely wounded but alive and must not be buried.
        policy.OnSoldierKilled(
            CreateRangedWound(defenders.Soldiers[0], attackers.Soldiers[0]), WoundLevel.Mortal);

        policy.OnBattleCompleted(CreateFinalState(attackers, defenders));

        Assert.DoesNotContain(
            player.SoldierEvents, soldierEvent => soldierEvent.Type == SoldierEventType.Death);
        Assert.Empty(sink.FallenBrothers);
    }

    [Fact]
    public void PlayerBattleCompletion_DeathWithNoRecordedMortalWoundStillRecordsTheFall()
    {
        Faction playerFaction = CreateFaction(74, "Chapter", isPlayer: true);
        Faction enemyFaction = CreateFaction(75, "Orks", isPlayer: false);
        PlayerSoldier player = CreatePlayerSoldier("Brother Unwitnessed");
        BattleSquad attackers = CreateBattleSquad(playerFaction, "Strike Squad", player);
        BattleSquad defenders = CreateBattleSquad(enemyFaction, "Warband", CreateSoldier("Boy"));
        BattleAftermathContext context = new(
            [attackers], [defenders], CreateRegion("Ash Wastes", "Calth"),
            new BattleHistory(), CreateDependencies());
        IBattleAftermathPolicy policy = BattleAftermathPolicyFactory.Create(context);

        // A severing blow to an already-crippled location raises no death hook, so there is no
        // weapon on record -- the fall must still be written down.
        SeverVitalLocation(player);

        policy.OnBattleCompleted(CreateFinalState(attackers, defenders));

        SoldierEvent death = Assert.Single(
            player.SoldierEvents, soldierEvent => soldierEvent.Type == SoldierEventType.Death);
        Assert.Null(death.WeaponTemplateId);
        Assert.Equal("Killed in battle with the Orks", death.Render());
    }

    private static void SeverVitalLocation(PlayerSoldier soldier)
    {
        soldier.Body.HitLocations
            .First(location => location.Template.IsVital && !location.Template.HoldsProgenoid)
            .Wounds.AddWound(WoundLevel.Massive);
    }

    private static BattleState CreateFinalState(BattleSquad attackers, BattleSquad defenders)
    {
        return new BattleState(
            new Dictionary<int, BattleSquad> { [attackers.Id] = attackers },
            new Dictionary<int, BattleSquad> { [defenders.Id] = defenders });
    }

    private static BattleAftermathDependencies CreateDependencies() =>
        new(new Date(1, 1, 1), new FixedRNG(), new RecordingPlayerBattleAftermathSink());

    private static Region CreateRegion(string regionName, string planetName)
    {
        Planet planet = new(1, planetName, new Coordinate(0, 0), 1, null, 1, 0);
        return new Region(1, planet, 0, regionName, new RegionCoordinate(0, 0), 0);
    }

    private static WoundResolution CreateRangedWound(BattleSoldier inflicter, BattleSoldier sufferer)
    {
        WeaponTemplate weapon = inflicter.EquippedRangedWeapons[0].Template;
        HitLocation hitLocation = sufferer.Soldier.Body.HitLocations.First();
        return new WoundResolution(inflicter, weapon, sufferer, 10, hitLocation);
    }

    private static WoundResolution CreateMeleeWound(BattleSoldier inflicter, BattleSoldier sufferer)
    {
        WeaponTemplate weapon = inflicter.MeleeWeapons[0].Template;
        HitLocation hitLocation = sufferer.Soldier.Body.HitLocations.First();
        return new WoundResolution(inflicter, weapon, sufferer, 10, hitLocation);
    }

    private static PlayerSoldier CreatePlayerSoldier(string name)
    {
        Soldier soldier = CreateSoldier(name);
        // Geneseed recovery ages the progenoids off this date, so any test whose soldier dies
        // needs one; give every brother a default that individual tests can override.
        return new PlayerSoldier(soldier, name) { ProgenoidImplantDate = new Date(1, 1, 1) };
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
            soldier.TopLeft = (_nextId++, 2);
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
            behavior: FactionBehavior.None,
            GrowthType.None,
            new Dictionary<int, Species> { [TestModelFactory.HumanSpecies.Id] = TestModelFactory.HumanSpecies },
            new Dictionary<int, SoldierTemplate> { [TestModelFactory.MarineTemplate.Id] = TestModelFactory.MarineTemplate },
            new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, Models.Units.UnitTemplate>(),
            new Dictionary<int, Models.Fleets.BoatTemplate>(),
            new Dictionary<int, Models.Fleets.ShipTemplate>(),
            new Dictionary<int, Models.Fleets.FleetTemplate>());
    }

    private sealed class RecordingPlayerBattleAftermathSink : IPlayerBattleAftermathSink
    {
        public List<PlayerSoldier> FallenBrothers { get; } = [];
        public List<float> RecoveredGeneseedPurities { get; } = [];
        public List<(Date Date, string Title, List<string> SubEvents)> BattleHistoryEntries { get; } = [];

        public void MoveToFallenBrothers(PlayerSoldier soldier) => FallenBrothers.Add(soldier);

        public void AddRecoveredGeneseed(float purity) => RecoveredGeneseedPurities.Add(purity);

        public void AddToBattleHistory(Date date, string title, IReadOnlyList<string> subEvents) =>
            BattleHistoryEntries.Add((date, title, subEvents.ToList()));
    }
}
