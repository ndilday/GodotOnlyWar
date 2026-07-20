using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Helpers.Battles.Aftermath;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

// Phase 3 reporting coverage for grenades: a resolved BlastAttackAction surfaces in
// the replay summary as a fire-phase volley, and the live turn resolver queues its
// wounds so they are actually applied.
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class GrenadeReportingTests
{
    [Fact]
    public void ReplaySummary_ReportsAResolvedBlastAsAFirePhaseVolley()
    {
        BattleSquad throwers = CreateBattleSquad(true, "Alpha", 900, "Brother Thrower");
        BattleSquad victims = CreateBattleSquad(false, "Cult Mob", 910, "Cultist One", "Cultist Two");
        RangedWeapon grenade = new(TestModelFactory.FragGrenadeTemplate);
        BattleSoldier thrower = throwers.Soldiers[0];
        // Skilled enough that a mean roll from the scripted RNG lands on the aim cell.
        ((Soldier)thrower.Soldier).Dexterity = 20;
        thrower.RangedWeapons.Clear();
        thrower.EquippedRangedWeapons.Clear();
        thrower.RangedWeapons.Add(grenade);
        thrower.EquippedRangedWeapons.Add(grenade);
        foreach (BattleSoldier soldier in throwers.Soldiers.Concat(victims.Soldiers))
        {
            soldier.Armor = new Armor(new ArmorTemplate(990 + soldier.Soldier.Id, "Cloth", 0, 0));
        }

        BattleGridManager grid = new();
        Place(grid, thrower, true, 0, 0);
        Place(grid, victims.Soldiers[0], false, 10, 0);
        Place(grid, victims.Soldiers[1], false, 11, 0);
        BattleState initialState = new(
            new Dictionary<int, BattleSquad> { [throwers.Id] = throwers },
            new Dictionary<int, BattleSquad> { [victims.Id] = victims });
        BattleState currentState = new(initialState);

        BlastAttackAction action = new(
            thrower.Soldier.Id,
            victims.Soldiers[0].Soldier.Id,
            grenade.Template.Id,
            range: 10f,
            useBulk: false,
            grid,
            new AlwaysOnTargetRNG());
        action.Execute(currentState);
        Assert.True(action.WoundResolutions.Count > 0);

        BattleHistory history = new();
        history.Turns.Add(new BattleTurn(initialState, []));
        history.Turns.Add(new BattleTurn(currentState, [action]));
        BattleReplayDisplay display = new BattleReplaySummaryBuilder()
            .Build(history, 1, throwers.Id);

        Assert.Equal("Fire phase", display.PhaseLabel);
        BattleEventEntry eventEntry = Assert.Single(display.CurrentTurnEvents);
        Assert.Equal("Volley", eventEntry.EventType);
        Assert.Equal("Brother Thrower", eventEntry.ActorName);
        Assert.Contains("hurls", eventEntry.Text);
        Assert.Equal(BattleEventSeverity.Warning, eventEntry.Severity);
    }

    [Fact]
    public void TurnResolver_QueuesBlastWoundsSoTheyAreApplied()
    {
        GameRulesData rules = new();
        Date battleDate = new(1, 1, 1);
        string originalDirectory = Environment.CurrentDirectory;
        try
        {
            Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
            GameDataSingleton.Instance.LoadGameDataFromBlob(rules, battleDate, null);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
        }
        RNG.Reset(42);

        BattleSquad grenadiers = CreateResolverSquad(
            CreateFaction(81_001, "Grenadiers"), "Grenadiers", 81_101, 1);
        BattleSoldier grenadier = grenadiers.Soldiers[0];
        // A practiced thrower whose grenade lands where it is aimed, with a payload
        // heavy enough that the clustered victims reliably take wounds.
        ((Soldier)grenadier.Soldier).Dexterity = 25;
        RangedWeapon grenade = new(new RangedWeaponTemplate(
            99_400,
            "Test Heavy Grenade",
            EquipLocation.OneHand,
            TestSkills.Ranged,
            accuracy: 0,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 4,
            baseDamage: 10,
            maxDistance: 3,
            rof: 1,
            ammo: 1,
            recoil: 0,
            bulk: 0,
            doesDamageDegradeWithRange: false,
            reloadTime: 1,
            templateType: 3,
            areaRadius: 6,
            fuelPerBurst: 0));
        grenadier.RangedWeapons.Clear();
        grenadier.EquippedRangedWeapons.Clear();
        grenadier.RangedWeapons.Add(grenade);
        grenadier.EquippedRangedWeapons.Add(grenade);

        BattleSquad victims = CreateResolverSquad(
            CreateFaction(81_002, "Victims"), "Victims", 81_201, 3);
        foreach (BattleSoldier victim in victims.Soldiers)
        {
            victim.RangedWeapons.Clear();
            victim.EquippedRangedWeapons.Clear();
        }

        BattleGridManager grid = new();
        Place(grid, grenadier, side: true, x: 0, y: 0);
        Place(grid, victims.Soldiers[0], side: false, x: 10, y: 0);
        Place(grid, victims.Soldiers[1], side: false, x: 11, y: 0);
        Place(grid, victims.Soldiers[2], side: false, x: 10, y: 1);
        BattleAftermathDependencies aftermath = new(
            battleDate, StaticRNG.Instance, NoOpPlayerBattleAftermathSink.Instance);
        BattleExecutionContext execution = new(rules, StaticRNG.Instance, aftermath);
        BattleTurnResolver resolver = new(
            grid, [grenadiers], [victims], region: null, execution);

        resolver.ProcessNextTurn();

        BlastAttackAction blast = resolver.BattleHistory.Turns[1].Actions
            .OfType<BlastAttackAction>()
            .Single();
        Assert.True(blast.WoundResolutions.Count > 0);
        // The wound resolver stamps a description onto every wound it processes; an
        // unqueued wound would still have a null description (and no applied damage).
        Assert.All(blast.WoundResolutions, wound =>
            Assert.Contains("suffers", wound.Description));
    }

    private static BattleSquad CreateBattleSquad(
        bool isPlayerSquad,
        string squadName,
        int firstSoldierId,
        params string[] soldierNames)
    {
        List<Soldier> soldiers = [];
        for (int index = 0; index < soldierNames.Length; index++)
        {
            Soldier soldier = TestModelFactory.CreateSoldier(name: soldierNames[index]);
            soldier.Id = firstSoldierId + index;
            soldiers.Add(soldier);
        }

        Squad squad = new(squadName, null, TestModelFactory.SquadTemplate);
        foreach (Soldier soldier in soldiers)
        {
            squad.AddSquadMember(soldier);
        }

        BattleSquad battleSquad = new(isPlayerSquad, squad);
        foreach (BattleSoldier soldier in battleSquad.Soldiers)
        {
            soldier.TopLeft = (0, 0);
        }

        return battleSquad;
    }

    private static BattleSquad CreateResolverSquad(
        Faction faction,
        string name,
        int firstSoldierId,
        int soldierCount)
    {
        SquadTemplate template = new(
            firstSoldierId,
            $"{name} Template",
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            [new SquadTemplateElement(TestModelFactory.MarineTemplate, 0, (byte)soldierCount)],
            SquadTypes.None)
        {
            Faction = faction
        };
        Squad squad = new(name, null, template);
        for (int index = 0; index < soldierCount; index++)
        {
            Soldier soldier = TestModelFactory.CreateSoldier(name: $"{name} Soldier {index + 1}");
            soldier.Id = firstSoldierId + index;
            squad.AddSquadMember(soldier);
        }

        return new BattleSquad(false, squad);
    }

    private static void Place(BattleGridManager grid, BattleSoldier soldier, bool side, int x, int y)
    {
        soldier.TopLeft = new ValueTuple<int, int>(x, y);
        grid.PlaceSoldier(soldier, side, [new ValueTuple<int, int>(x, y)]);
    }

    private static Faction CreateFaction(int id, string name)
    {
        return new Faction(
            id,
            name,
            Color.Red,
            isPlayerFaction: false,
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

    /// <summary>
    /// Every Z roll is the distribution's fixed point, so a competent thrower's check
    /// succeeds (no scatter) and every damage roll lands at its 3.5-multiple mean.
    /// </summary>
    private sealed class AlwaysOnTargetRNG : IRNG
    {
        public double GetDoubleInRange(double lowerBound, double upperBound) => lowerBound;

        public double GetLinearDouble() => 0.0;

        public int GetIntBelowMax(int min, int max) => min;

        public double NextRandomZValue() => 0.0;
    }

    private sealed class NoOpPlayerBattleAftermathSink : IPlayerBattleAftermathSink
    {
        public static NoOpPlayerBattleAftermathSink Instance { get; } = new();

        public void MoveToFallenBrothers(PlayerSoldier soldier)
        {
        }

        public void AddRecoveredGeneseed(float purity)
        {
        }

        public void AddToBattleHistory(Date date, string title, IReadOnlyList<string> subEvents)
        {
        }
    }
}
