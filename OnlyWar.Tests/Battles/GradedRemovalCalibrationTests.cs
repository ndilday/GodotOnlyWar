using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Domain;
using OnlyWar.Battles;
using OnlyWar.Battles.Actions;
using OnlyWar.Battles.Models;
using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// Phase 5b of Design/Reference/BattleLogic.md: the reference scenario the engagement
/// model is calibrated against -- ~30 bolter marines at 200 yards from four melee-only
/// Tyranids (Hive Tyrant BV 84, Lictor BV 37, two melee Carnifexes BV 30). Stats are taken from
/// Database/OnlyWar.s3db (species attribute templates, Boltgun, soldier-template battle values);
/// chitin armour values are the plausible 20mm/10mm assignment, which is the one number here not
/// read from the rules database.
///
/// <para>The objective: at 200 yards, thirty bolters grinding a Carnifex down is a better trade
/// than thirty marines in melee with it, so Hold must beat a long-range RunToward approach. The
/// invariant governs where the two conflict -- see <see cref="GradedRemovalTests"/>.</para>
///
/// <para>The cases below pin the shipped calibration through the reference scenario and its
/// important boundary conditions.</para>
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class GradedRemovalCalibrationTests
{
    private readonly ITestOutputHelper _output;

    public GradedRemovalCalibrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private sealed record ScenarioResult(
        EngagementOptionKind Chosen,
        float Outgoing,
        float Future,
        float HoldScore,
        float ApproachScore)
    {
        internal float Margin => HoldScore - ApproachScore;
    }

    // Renamed 2026-10-06 from ShippedLambda_*: the graded credit is now measured on the engine's
    // wound counter rather than weighted by lambda (RemovalMath), and these properties are the
    // ones that change must keep.
    [Fact]
    public void GradedRemoval_MakesTheBolterSquadStandAndShootAt200Yards()
    {
        // The reported defect: at 200 yards from melee-only Carnifexes the marines walked into
        // contact because the immediate fire term was ~0 and the lookahead's capability proxy
        // slightly preferred closing. Thirty bolters grinding is the better trade.
        ScenarioResult result = RunReferenceScenario();

        _output.WriteLine(
            $"chosen {result.Chosen}, "
                + $"outgoing {result.Outgoing:0.###}, future {result.Future:0.###}, "
                + $"Hold - Run {result.Margin:0.###}");
        Assert.NotEqual(EngagementOptionKind.RunToward, result.Chosen);
        Assert.True(
            result.Margin > 0f,
            $"Hold should outscore RunToward; margin was {result.Margin:0.#####}");
    }

    [Fact]
    public void GradedRemoval_StillRefusesToPlinkAtAnImpenetrableTarget()
    {
        // The invariant, exercised through the whole scoring stack rather than the wound model
        // alone: the same geometry against a target this weapon simply cannot hurt must not
        // produce immediate fire value. Otherwise graded credit has bought exactly the bug
        // take-out probability was introduced to fix.
        ScenarioResult penetrable = RunReferenceScenario();
        ScenarioResult impenetrable = RunReferenceScenario(enemyArmor: 255);

        _output.WriteLine(
            $"penetrable outgoing {penetrable.Outgoing:0.#####}, "
                + $"impenetrable outgoing {impenetrable.Outgoing:0.#####}");
        Assert.True(
            penetrable.Outgoing > 0.5f,
            $"the control case must have real fire value, got {penetrable.Outgoing:0.#####}");
        Assert.True(
            impenetrable.Outgoing < 0.0001f,
            "a squad that cannot penetrate must not be paid for shooting, got "
                + $"{impenetrable.Outgoing:0.#####}");
    }

    // The Xibarrus Zeta guard (a bolter line must not open in contact with melee-only monsters)
    // moved to RealTemplateEngagementTests on 2026-10-24, on real templates: under the opening
    // forecast this file's hand-built monsters die before they arrive from any opening past 9
    // yards, so they no longer test it. The constitution-monotonicity test went with the
    // sufficiency sweep whose property it pinned (Design/Reference/ForecastCalibration.md section 1).

    /// <summary>
    /// The counterpart the fix must NOT break: a force that shoots back sets the standoff through
    /// the incoming curve, and opening beyond that is paid for in return fire. This is the
    /// Xibarrus Nu case (marines vs Genestealer Cult), which was never the broken one.
    /// </summary>
    [Fact]
    public void PreferredOpeningRange_AgainstAForceThatShootsBack_IsNotJustMaximumReach()
    {
        BattleSquad marines = BolterSquad("Rostadi", 91_700);
        BattleSquad shooters = BolterSquad("Hybrids", 91_800);

        int range = marines.GetPreferredOpeningRange([shooters]);

        _output.WriteLine($"vs a shooting force, preferred opening range {range}");
        Assert.True(range > 0, $"a gun line must still stand off a gun line; got {range}");
        Assert.True(
            range < 1_000,
            $"return fire must bound the opening range below weapon reach; got {range}");
    }

    private static ScenarioResult RunReferenceScenario(byte enemyArmor = 20)
    {
        BattleSquad first = BolterSquad("Rostadi", 82_100);
        BattleSquad second = BolterSquad("Rostzin", 82_120);
        BattleSquad third = BolterSquad("Scharel", 82_140);
        BattleSquad tyrant = Tyranid(
            "Hive Tyrant", 82_200, strategicValue: 84, constitution: 240,
            size: 6.8f, moveSpeed: 8.001f, dexterity: 20, armor: enemyArmor);
        BattleSquad lictor = Tyranid(
            "Lictor", 82_210, strategicValue: 37, constitution: 120,
            size: 3.06f, moveSpeed: 8.001f, dexterity: 18,
            // Preserve the reference Lictor's lighter armour in the ordinary sweep, but make the
            // impenetrable-target invariant genuinely impenetrable for every target in this
            // whole-stack scenario.
            armor: enemyArmor == byte.MaxValue
                ? enemyArmor
                : System.Math.Min(enemyArmor, (byte)10));
        BattleSquad carnifexA = Tyranid(
            "Melee Carnifex A", 82_220, strategicValue: 30, constitution: 224,
            size: 8f, moveSpeed: 7.001f, dexterity: 10, armor: enemyArmor);
        BattleSquad carnifexB = Tyranid(
            "Melee Carnifex B", 82_230, strategicValue: 30, constitution: 224,
            size: 8f, moveSpeed: 7.001f, dexterity: 10, armor: enemyArmor);

        BattleGridManager grid = new();
        PlaceSquad(grid, first, true, 0, 0);
        PlaceSquad(grid, second, true, 0, 30);
        PlaceSquad(grid, third, true, 0, 60);
        PlaceSquad(grid, tyrant, false, 200, 0);
        PlaceSquad(grid, lictor, false, 200, 20);
        PlaceSquad(grid, carnifexA, false, 200, 40);
        PlaceSquad(grid, carnifexB, false, 200, 60);

        List<BattleSquad> marines = [first, second, third];
        List<BattleSquad> tyranids = [tyrant, lictor, carnifexA, carnifexB];
        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build(marines, tyranids);
        BattleSquadPlanner planner = Planner(
            grid, [.. marines, .. tyranids]);

        SquadEngagementDecision decision = planner.ChooseEngagementOption(
            first,
            paired.Frames[first.Id],
            paired.Profiles,
            paired.Frames,
            marines,
            tyranids);

        EngagementOptionEvaluation hold = decision.Candidates
            .Single(candidate => candidate.Kind == EngagementOptionKind.Hold);
        EngagementOptionEvaluation approach = decision.Candidates
            .Single(candidate => candidate.Kind == EngagementOptionKind.RunToward);
        // Hold's fire value is its immediate removal plus its readiness: a marine who spends the
        // turn aiming is paid in readiness at the per-turn value of the shot he is preparing
        // (RangedTargetSelector.EvaluateFireTiming), in the same currency as firing now.
        return new ScenarioResult(
            decision.Chosen.Kind,
            hold.ImmediateEnemyRemoval + hold.ReadinessValue,
            hold.FutureExchange.Sum(),
            hold.Score,
            approach.Score);
    }

    private static BattleSquad BolterSquad(string name, int firstSoldierId)
    {
        List<Soldier> soldiers = [];
        for (int index = 0; index < 10; index++)
        {
            bool sergeant = index == 0;
            SoldierTemplate template = new TestSoldierTemplate(
                60_000 + firstSoldierId + index,
                TestModelFactory.HumanSpecies,
                sergeant ? $"{name} Sergeant Template" : $"{name} Marine Template",
                sergeant ? (byte)2 : (byte)1,
                1,
                sergeant,
                0,
                Array.Empty<ValueTuple<BaseSkill, float>>(),
                strategicValue: sergeant ? 11 : 9);
            // Space Marine species: Strength 15, Dexterity 15, Constitution 30 (attribute
            // templates 18/18/28 in the rules database); the trace reports Dexterity 15.4.
            // 2^1.4 - 2^-4 skill points reproduce the reported Gun (Bolter) skill bonus of 1.4,
            // since SkillBonus = log2(points + 2^(difficulty - 4)) - difficulty and the test
            // ranged skill has difficulty 0.
            Soldier soldier = TestModelFactory.CreateSoldier(
                template,
                $"{name} {index}",
                dexterity: 15.4f,
                strength: 15f,
                charisma: 10f,
                new Skill(TestSkills.Ranged, (float)(System.Math.Pow(2, 1.4) - System.Math.Pow(2, -4))));
            soldier.Id = firstSoldierId + index;
            soldier.Constitution = 30;
            soldier.Size = 2.4f;
            soldier.MoveSpeed = 6.001f;
            soldiers.Add(soldier);
        }
        BattleSquad squad = new(
            false, TestModelFactory.CreateSquad(name, soldiers.ToArray()));
        foreach (BattleSoldier member in squad.Soldiers)
        {
            member.Armor = new Armor(new ArmorTemplate(
                69_000 + member.Soldier.Id, "Astartes Power Armor Mk VII", 20, -3));
            EquipBoltgun(member, 69_500);
        }
        return squad;
    }

    private static BattleSquad Tyranid(
        string name,
        int soldierId,
        int strategicValue,
        float constitution,
        float size,
        float moveSpeed,
        float dexterity,
        byte armor)
    {
        SoldierTemplate template = new TestSoldierTemplate(
            60_000 + soldierId,
            TestModelFactory.HumanSpecies,
            $"{name} Template",
            1,
            1,
            false,
            0,
            Array.Empty<ValueTuple<BaseSkill, float>>(),
            strategicValue: strategicValue);
        Soldier soldier = TestModelFactory.CreateSoldier(
            template, name, dexterity: dexterity, strength: 24f);
        soldier.Id = soldierId;
        soldier.Constitution = constitution;
        soldier.Size = size;
        soldier.MoveSpeed = moveSpeed;
        BattleSquad squad = new(false, TestModelFactory.CreateSquad(name, soldier));
        BattleSoldier member = squad.Soldiers[0];
        member.Armor = new Armor(new ArmorTemplate(
            69_100 + soldierId, $"{name} Chitin", armor, 0));
        EquipTalons(member, 69_600 + soldierId);
        return squad;
    }

    // Boltgun, read from Database/OnlyWar.s3db RangedWeaponTemplate id 0.
    private static void EquipBoltgun(BattleSoldier soldier, int templateId)
    {
        RangedWeapon boltgun = new(new RangedWeaponTemplate(
            templateId,
            "Boltgun",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: 3,
            armorMultiplier: 1,
            penetrationMultiplier: 2,
            requiredStrength: 12,
            baseDamage: 6,
            maxDistance: 1_000,
            rof: 9,
            ammo: 30,
            recoil: 2,
            bulk: 4,
            doesDamageDegradeWithRange: false,
            reloadTime: 3));
        soldier.RangedWeapons.Clear();
        soldier.ClearReadiedRangedWeapons();
        soldier.RangedWeapons.Add(boltgun);
        soldier.ReadyWeapon(boltgun);
    }

    private static void EquipTalons(BattleSoldier soldier, int templateId)
    {
        MeleeWeapon talons = new(new MeleeWeaponTemplate(
            templateId,
            "Scything Talons",
            EquipLocation.OneHand,
            TestSkills.Melee,
            accuracy: 4,
            armorMultiplier: 1,
            penetrationMultiplier: 2,
            requiredStrength: 0,
            strengthMultiplier: 2,
            parryMod: 0,
            attackSpeedMultiplier: 2));
        soldier.RangedWeapons.Clear();
        soldier.ClearReadiedRangedWeapons();
        soldier.MeleeWeapons.Clear();
        soldier.ClearReadiedMeleeWeapons();
        soldier.MeleeWeapons.Add(talons);
        soldier.ReadyWeapon(talons);
    }

    private static void PlaceSquad(
        BattleGridManager grid,
        BattleSquad squad,
        bool side,
        int x,
        int y)
    {
        for (int index = 0; index < squad.Soldiers.Count; index++)
        {
            BattleSoldier soldier = squad.Soldiers[index];
            ValueTuple<int, int> cell = (x, y + (index * 2));
            soldier.TopLeft = cell;
            grid.PlaceSoldier(soldier, side, [cell]);
        }
    }

    private static BattleSquadPlanner Planner(
        BattleGridManager grid,
        IReadOnlyList<BattleSquad> squads)
    {
        Dictionary<int, BattleSoldier> soldiers = squads
            .SelectMany(squad => squad.Soldiers)
            .ToDictionary(soldier => soldier.Soldier.Id);
        Dictionary<int, MeleeWeaponTemplate> melee = soldiers.Values
            .SelectMany(soldier => soldier.MeleeWeapons
                .Select(weapon => weapon.Template)
                .Append(soldier.Soldier.Template.Species.DefaultUnarmedWeapon))
            .GroupBy(template => template.Id)
            .ToDictionary(group => group.Key, group => group.First());
        return new BattleSquadPlanner(
            grid,
            soldiers,
            new List<IAction>(),
            new List<IAction>(),
            new List<IAction>(),
            null,
            melee,
            new SeededRNG(82_000));
    }
}
