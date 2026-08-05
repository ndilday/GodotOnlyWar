using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// Coverage for the per-soldier battle trace records (ACTION, MOVE, MELEE, GRENADE_CHOICE).
///
/// <para>WHY THIS FILE EXISTS. Every other planner test passes <c>null</c> for the log sink, so
/// before these tests the trace code was compiled but never executed by the suite -- a diagnostic
/// nobody runs is a diagnostic that silently rots, which is exactly how the grenade trace ended up
/// wired to an unreachable method. These tests attach a real sink and assert both that the records
/// are emitted and that they stay machine-parseable.</para>
/// </summary>
public class BattleTraceLoggingTests
{
    /// <summary>
    /// The format contract every consumer depends on: a record is a type tag followed by
    /// whitespace-separated <c>key=value</c> pairs, so splitting on spaces must never produce a
    /// fragment without an '='. Soldier names contain spaces, which is precisely the way this
    /// invariant would break unnoticed.
    /// </summary>
    private static void AssertParsesAsKeyValueRecord(string line)
    {
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(parts.Length > 1, $"record carried no fields: {line}");
        foreach (string field in parts.Skip(1))
        {
            Assert.True(
                field.Contains('='),
                $"field '{field}' is not key=value, so '{parts[0]}' cannot be parsed: {line}");
        }
    }

    private static IReadOnlyList<string> RecordsOfType(IEnumerable<string> log, string recordType)
    {
        return log.Where(line => line.StartsWith(recordType + " ", StringComparison.Ordinal))
            .ToList();
    }

    private static string FieldValue(string record, string fieldName)
    {
        string prefix = fieldName + "=";
        string match = record
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(field => field.StartsWith(prefix, StringComparison.Ordinal));
        Assert.NotNull(match);
        return match[prefix.Length..];
    }

    [Fact]
    public void GrenadeThrow_EmitsGrenadeChoiceAndActionRecords()
    {
        // The cluster scenario from GrenadePlannerTests: four frag-vulnerable bodies make the
        // throw beat the rifle, so both the decision record and the action record must appear.
        BattleSquad shooters = CreateSquad("Grenadier", 76_100);
        BattleSoldier shooter = shooters.Soldiers[0];
        ArmWithRifleAndBeltGrenade(shooter);
        BattleSquad enemies = CreateSquad(
            "Cluster", (76_110, 2), (76_111, 2), (76_112, 2), (76_113, 2));
        foreach (BattleSoldier enemy in enemies.Soldiers)
        {
            MakeFragile(enemy, constitution: 10);
        }

        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        Place(grid, enemies.Soldiers.Single(s => s.Soldier.Id == 76_110), false, 10, 0);
        Place(grid, enemies.Soldiers.Single(s => s.Soldier.Id == 76_111), false, 11, 0);
        Place(grid, enemies.Soldiers.Single(s => s.Soldier.Id == 76_112), false, 10, 1);
        Place(grid, enemies.Soldiers.Single(s => s.Soldier.Id == 76_113), false, 11, 1);

        List<string> log = [];
        List<IAction> shootActions = [];
        BattleSquadPlanner planner = CreatePlanner(
            grid, shootActions, [], [], log.Add, shooters, enemies);

        planner.PrepareActions(shooters);

        Assert.Contains(shootActions, action => action is BlastAttackAction);

        string grenade = Assert.Single(RecordsOfType(log, "GRENADE_CHOICE"));
        AssertParsesAsKeyValueRecord(grenade);
        // The throw is only explicable against what it beat; these are the fields that make it so.
        Assert.Equal(shooter.Soldier.Id.ToString(), FieldValue(grenade, "soldier"));
        Assert.NotEqual("none", FieldValue(grenade, "caught_enemies"));
        Assert.NotEqual("none", FieldValue(grenade, "best_conventional"));
        Assert.NotEqual("none", FieldValue(grenade, "margin"));

        string action = Assert.Single(RecordsOfType(log, "ACTION"));
        AssertParsesAsKeyValueRecord(action);
        Assert.Equal("BlastAttack", FieldValue(action, "action"));
        Assert.Equal(shooter.Soldier.Id.ToString(), FieldValue(action, "soldier"));
    }

    [Fact]
    public void BoundWithdrawal_EmitsMoveRecordCarryingTierBudgetAndAchievedDistance()
    {
        // A bound squad runs along its withdrawal heading across open ground, so desired and
        // achieved must agree and `blocked` must be false. This is the record that distinguishes a
        // model that CHOSE a slower tier from one that tried to run and could not fit.
        BattleSquad bound = CreateSquad("Bound", 76_200);
        BattleSquad enemy = CreateSquad("Enemy", 76_210);

        BattleGridManager grid = new();
        Place(grid, bound.Soldiers[0], true, 0, 0);
        Place(grid, enemy.Soldiers[0], false, 0, 10);

        List<string> log = [];
        List<IAction> moveActions = [];
        BattleSquadPlanner planner = CreatePlanner(
            grid, [], moveActions, [], log.Add, bound, enemy);

        planner.PrepareBoundActions(bound, withdrawalHeading: 2);

        Assert.Contains(moveActions, action => action is MoveAction);

        string move = Assert.Single(RecordsOfType(log, "MOVE"));
        AssertParsesAsKeyValueRecord(move);
        Assert.Equal("Run", FieldValue(move, "tier"));
        Assert.Equal("false", FieldValue(move, "blocked"));
        Assert.Equal(
            bound.Soldiers[0].Soldier.Id.ToString(),
            FieldValue(move, "soldier"));
        // Open ground: the soldier got everything the tier offered.
        Assert.Equal(FieldValue(move, "desired"), FieldValue(move, "achieved"));
    }

    [Fact]
    public void SoldierNamesWithSpaces_DoNotBreakRecordParsing()
    {
        // Render() separates fields with spaces. Names are the one value that routinely contains
        // one, so this is the regression that would quietly corrupt every downstream parser.
        string record = new BattleDecisionTrace("TEST",
        [
            BattleDecisionTrace.Field("name", "Beryn Phaelenik"),
            BattleDecisionTrace.Field("weapon", "Frag Grenade"),
            BattleDecisionTrace.Field("count", 3)
        ]).Render();

        AssertParsesAsKeyValueRecord(record);
        Assert.Equal("Beryn_Phaelenik", FieldValue(record, "name"));
        Assert.Equal("Frag_Grenade", FieldValue(record, "weapon"));
    }

    private static BattleSquad CreateSquad(
        string name,
        int soldierId,
        int battleValue = 2)
    {
        return CreateSquad(name, (soldierId, battleValue));
    }

    private static BattleSquad CreateSquad(
        string name,
        params (int SoldierId, int BattleValue)[] members)
    {
        List<Soldier> soldiers = members
            .Select(member =>
            {
                SoldierTemplate template = new(
                    31_000 + member.SoldierId,
                    TestModelFactory.HumanSpecies,
                    $"{name} {member.SoldierId} Template",
                    1,
                    1,
                    false,
                    0,
                    Array.Empty<ValueTuple<BaseSkill, float>>(),
                    battleValue: member.BattleValue);
                Soldier soldier = TestModelFactory.CreateSoldier(
                    template,
                    $"{name} {member.SoldierId}");
                soldier.Id = member.SoldierId;
                return soldier;
            })
            .ToList();
        return new BattleSquad(false, TestModelFactory.CreateSquad(name, soldiers.ToArray()));
    }

    private static void ArmWithRifleAndBeltGrenade(BattleSoldier soldier)
    {
        ((Soldier)soldier.Soldier).Dexterity = 22;
        RangedWeapon rifle = new(new RangedWeaponTemplate(
            99_320,
            "Test Marksman Rifle",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: 0,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            baseDamage: 12,
            maxDistance: 100,
            rof: 1,
            ammo: 10,
            recoil: 0,
            bulk: 0,
            doesDamageDegradeWithRange: false,
            reloadTime: 1));
        soldier.RangedWeapons.Clear();
        soldier.ClearReadiedRangedWeapons();
        soldier.RangedWeapons.Add(rifle);
        soldier.ReadyWeapon(rifle);
        soldier.RangedWeapons.Add(new RangedWeapon(TestModelFactory.FragGrenadeTemplate));
    }

    private static void MakeFragile(BattleSoldier soldier, float constitution)
    {
        Soldier raw = (Soldier)soldier.Soldier;
        raw.Constitution = constitution;
        raw.Dexterity = 22;
    }

    private static void Place(
        BattleGridManager grid,
        BattleSoldier soldier,
        bool side,
        int x,
        int y)
    {
        soldier.TopLeft = new ValueTuple<int, int>(x, y);
        grid.PlaceSoldier(soldier, side, [new ValueTuple<int, int>(x, y)]);
    }

    private static BattleSquadPlanner CreatePlanner(
        BattleGridManager grid,
        ICollection<IAction> shootActions,
        ICollection<IAction> moveActions,
        ICollection<IAction> meleeActions,
        Action<string> log,
        params BattleSquad[] squads)
    {
        Dictionary<int, BattleSoldier> soldiers = squads
            .SelectMany(squad => squad.Soldiers)
            .ToDictionary(soldier => soldier.Soldier.Id);
        IReadOnlyDictionary<int, MeleeWeaponTemplate> meleeTemplates = soldiers.Values
            .SelectMany(soldier => soldier.MeleeWeapons
                .Concat(soldier.EquippedMeleeWeapons)
                .Select(weapon => weapon.Template)
                .Append(soldier.Soldier.Template.Species.DefaultUnarmedWeapon))
            .GroupBy(template => template.Id)
            .ToDictionary(group => group.Key, group => group.First());
        return new BattleSquadPlanner(
            grid,
            soldiers,
            shootActions,
            moveActions,
            meleeActions,
            log,
            meleeTemplates,
            new SeededRNG(12345));
    }
}
