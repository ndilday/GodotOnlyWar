using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// Phase 5b of Design/Active/EngagementScoringOverhaul.md: the lambda sweep and the reference
/// scenario it is calibrated against -- ~30 bolter marines at 200 yards from four melee-only
/// Tyranids (Hive Tyrant BV 84, Lictor BV 37, two melee Carnifexes BV 30). Stats are taken from
/// Database/OnlyWar.s3db (species attribute templates, Boltgun, soldier-template battle values);
/// chitin armour values are the plausible 20mm/10mm assignment, which is the one number here not
/// read from the rules database.
///
/// <para>The objective: at 200 yards, thirty bolters grinding a Carnifex down is a better trade
/// than thirty marines in melee with it, so Hold must beat CloseToContact. The invariant governs
/// where the two conflict -- see <see cref="GradedRemovalTests"/>.</para>
///
/// <para>PHASE 7. The sweep no longer WRITES a static: lambda is a const in shipping code and the
/// only way to move it is <c>BattleSquadPlanner.OverrideWoundProgressCreditWeight</c>, an internal
/// scope that restores on dispose. This class is that seam's only caller. It still runs in the
/// shared-state collection, because the override is process-wide for its duration.</para>
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class GradedRemovalCalibrationTests
{
    private readonly ITestOutputHelper _output;

    public GradedRemovalCalibrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // The sweep points reported in the constant's comment block in BattleSquadPlanner.
    private static readonly float[] SweptLambdas =
        [0f, 0.05f, 0.1f, 0.15f, 0.2f, 0.25f, 0.35f, 0.5f, 0.75f, 1f];

    private sealed record ScenarioResult(
        EngagementOptionKind Chosen,
        float Outgoing,
        float Future,
        float HoldScore,
        float CloseScore)
    {
        internal float Margin => HoldScore - CloseScore;
    }

    [Fact]
    public void LambdaSweep_ReferenceScenarioAt200Yards()
    {
        _output.WriteLine(
            "lambda | chosen          | outgoing | future  | Hold - Close");
        _output.WriteLine(
            "-------+-----------------+----------+---------+--------------");
        foreach (float lambda in SweptLambdas)
        {
            using (BattleSquadPlanner.OverrideWoundProgressCreditWeight(lambda))
            {
                ScenarioResult result = RunReferenceScenario();
                _output.WriteLine(
                    $"{lambda,6:0.00} | {result.Chosen,-15} | {result.Outgoing,8:0.###} | "
                        + $"{result.Future,7:0.###} | {result.Margin,12:0.###}");
            }
        }

        // PHASE 7. The seam's whole justification is that it cannot leave the engine mis-tuned.
        Assert.Equal(
            BattleSquadPlanner.WoundProgressCreditWeight,
            BattleSquadPlanner.EffectiveWoundProgressCreditWeight);
    }

    [Fact]
    public void ShippedLambda_MakesTheBolterSquadStandAndShootAt200Yards()
    {
        // The reported defect: at 200 yards from melee-only Carnifexes the marines walked into
        // contact because the immediate fire term was ~0 and the lookahead's capability proxy
        // slightly preferred closing. Thirty bolters grinding is the better trade.
        ScenarioResult result = RunReferenceScenario();

        _output.WriteLine(
            $"lambda {BattleSquadPlanner.WoundProgressCreditWeight}: chosen {result.Chosen}, "
                + $"outgoing {result.Outgoing:0.###}, future {result.Future:0.###}, "
                + $"Hold - Close {result.Margin:0.###}");
        Assert.NotEqual(EngagementOptionKind.CloseToContact, result.Chosen);
        Assert.True(
            result.Margin > 0f,
            $"Hold should outscore CloseToContact; margin was {result.Margin:0.#####}");
    }

    [Fact]
    public void ShippedLambda_StillRefusesToPlinkAtAnImpenetrableTarget()
    {
        // The invariant, exercised through the whole scoring stack rather than the wound model
        // alone: the same geometry against a target this weapon simply cannot hurt must not
        // produce immediate fire value. Otherwise lambda has bought exactly the bug take-out
        // probability was introduced to fix.
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

    /// <summary>
    /// XIBARRUS ZETA REGRESSION (2026-08-04). Rostadi/Scharel/Rostzin ambushed a Broodlord, three
    /// Tyrant Guard and a Melee Carnifex and the derived opening range was ONE YARD, so the marines
    /// were in contact on turn 1 and 29 of 30 died. The cause was
    /// <c>CalculatePreferredOpeningRange</c> delegating to the mid-fight snapshot argmax, whose
    /// every term decreases with range once the enemy has no ranged weapons -- making contact the
    /// argmax for any loadout. This pins the fix: against a melee-only force a bolter line opens far
    /// enough to actually shoot it before it arrives.
    /// </summary>
    [Theory]
    [InlineData((byte)5)]
    [InlineData((byte)10)]
    [InlineData((byte)15)]
    [InlineData((byte)20)]
    public void PreferredOpeningRange_AgainstClosingMeleeOnlyForce_StandsWellOffContact(byte armor)
    {
        BattleSquad marines = BolterSquad("Rostadi", 91_100);
        List<BattleSquad> tyranids = XibarrusZetaTyranids(armor);

        int marineRange = marines.GetPreferredOpeningRange(tyranids);
        // The force closes at 8 yards a turn, so 24 is "at least three turns of fire before they
        // arrive" rather than a tuned figure. Measured 2026-08-05: 67/55/47/40 yards for armour
        // 5/10/15/20 -- five to eight turns. The defect being pinned produced 1, for every loadout.
        _output.WriteLine($"armor {armor}: marine preferred opening range {marineRange}");
        Assert.True(
            marineRange >= 24,
            $"a bolter line should open well off contact against melee-only monsters that close "
                + $"at 8 yards a turn; got {marineRange}");
    }

    /// <summary>
    /// The toughness question that prompted the fix: a force too tough to destroy inside the useful
    /// band needs more turns of fire, more turns is more distance, so the opening range moves
    /// OUTWARD -- with no constitution term anywhere in the derivation.
    ///
    /// <para>Only asserted where toughness is the BINDING constraint. Below that the saturation
    /// floor decides and the answer is flat in constitution, and pinning monotonicity there would be
    /// pinning noise.</para>
    ///
    /// <para>THE BINDING REGION MOVED UP on 2026-08-05, when SaturationFraction went 0.5 -> 0.1.
    /// A wider useful band is a higher floor, so sufficiency now has to clear a lot more before it
    /// decides anything. Measured against five Carnifexes, before and after: 94 / 242 / 687 / 996 at
    /// constitution 400 / 800 / 1600 / 3200, against 929 / 929 / 929 / 996 now. Only the top pair
    /// still has toughness binding, so only the top pair is still asserted -- the 400/800 and
    /// 800/1600 cases were dropped because the property genuinely does not hold there any more, NOT
    /// because the numbers moved. If the floor is ever lowered again, restore them.</para>
    /// </summary>
    [Theory]
    [InlineData(1600f, 3200f)]
    public void PreferredOpeningRange_RisesWithOpposingConstitution(float softer, float tougher)
    {
        int softRange = OpeningRangeAgainstCarnifexHerd("Softer", 92_000, softer);
        int toughRange = OpeningRangeAgainstCarnifexHerd("Tougher", 93_000, tougher);

        _output.WriteLine(
            $"constitution {softer:0} opens at {softRange}, {tougher:0} opens at {toughRange}");
        Assert.True(
            toughRange > softRange,
            $"a tougher force needs more turns of fire and so a longer opening range; got "
                + $"{toughRange} at constitution {tougher:0} vs {softRange} at {softer:0}");
    }

    private static int OpeningRangeAgainstCarnifexHerd(
        string name,
        int firstId,
        float constitution)
    {
        BattleSquad marines = BolterSquad(name, firstId);
        List<BattleSquad> herd = Enumerable.Range(0, 5)
            .Select(index => XibarrusZetaCarnifex(firstId + 500 + (index * 10), constitution))
            .ToList();
        return marines.GetPreferredOpeningRange(herd);
    }

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

    private static List<BattleSquad> XibarrusZetaTyranids(byte armor) =>
    [
        Tyranid("Broodlord", 91_200, battleValue: 44, constitution: 120,
            size: 3.06f, moveSpeed: 8.001f, dexterity: 18, armor: armor),
        Tyranid("Tyrant Guard 1", 91_210, battleValue: 28, constitution: 120,
            size: 2.6f, moveSpeed: 7.001f, dexterity: 12, armor: armor),
        Tyranid("Tyrant Guard 2", 91_220, battleValue: 28, constitution: 120,
            size: 2.6f, moveSpeed: 7.001f, dexterity: 12, armor: armor),
        Tyranid("Tyrant Guard 3", 91_230, battleValue: 28, constitution: 120,
            size: 2.6f, moveSpeed: 7.001f, dexterity: 12, armor: armor),
        Tyranid("Melee Carnifex", 91_240, battleValue: 30, constitution: 224,
            size: 8f, moveSpeed: 7.001f, dexterity: 10, armor: armor)
    ];

    private static BattleSquad XibarrusZetaCarnifex(int soldierId, float constitution) =>
        Tyranid("Melee Carnifex", soldierId, battleValue: 30, constitution: constitution,
            size: 8f, moveSpeed: 7.001f, dexterity: 10, armor: 20);

    private static ScenarioResult RunReferenceScenario(byte enemyArmor = 20)
    {
        BattleSquad first = BolterSquad("Rostadi", 82_100);
        BattleSquad second = BolterSquad("Rostzin", 82_120);
        BattleSquad third = BolterSquad("Scharel", 82_140);
        BattleSquad tyrant = Tyranid(
            "Hive Tyrant", 82_200, battleValue: 84, constitution: 240,
            size: 6.8f, moveSpeed: 8.001f, dexterity: 20, armor: enemyArmor);
        BattleSquad lictor = Tyranid(
            "Lictor", 82_210, battleValue: 37, constitution: 120,
            size: 3.06f, moveSpeed: 8.001f, dexterity: 18,
            armor: System.Math.Min(enemyArmor, (byte)10));
        BattleSquad carnifexA = Tyranid(
            "Melee Carnifex A", 82_220, battleValue: 30, constitution: 224,
            size: 8f, moveSpeed: 7.001f, dexterity: 10, armor: enemyArmor);
        BattleSquad carnifexB = Tyranid(
            "Melee Carnifex B", 82_230, battleValue: 30, constitution: 224,
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
        EngagementOptionEvaluation close = decision.Candidates
            .Single(candidate => candidate.Kind == EngagementOptionKind.CloseToContact);
        return new ScenarioResult(
            decision.Chosen.Kind,
            hold.ImmediateEnemyRemoval,
            hold.FutureExchange.Sum(),
            hold.Score,
            close.Score);
    }

    private static BattleSquad BolterSquad(string name, int firstSoldierId)
    {
        List<Soldier> soldiers = [];
        for (int index = 0; index < 10; index++)
        {
            bool sergeant = index == 0;
            SoldierTemplate template = new(
                60_000 + firstSoldierId + index,
                TestModelFactory.HumanSpecies,
                sergeant ? $"{name} Sergeant Template" : $"{name} Marine Template",
                sergeant ? (byte)2 : (byte)1,
                1,
                sergeant,
                0,
                Array.Empty<ValueTuple<BaseSkill, float>>(),
                battleValue: sergeant ? 11 : 9,
                // The trace's marines are a tactical squad: nominally ranged, with a chainsword
                // for when the charge arrives.
                meleeFraction: 0.05f);
            // Space Marine species: Strength 15, Dexterity 15, Constitution 30 (attribute
            // templates 18/18/28 in the rules database); the trace reports Dexterity 15.4.
            // 2^1.4 skill points reproduce the reported Gun (Bolter) skill bonus of 1.4, since
            // SkillBonus = log2(points) - difficulty and the test ranged skill has difficulty 0.
            Soldier soldier = TestModelFactory.CreateSoldier(
                template,
                $"{name} {index}",
                dexterity: 15.4f,
                strength: 15f,
                charisma: 10f,
                new Skill(TestSkills.Ranged, (float)System.Math.Pow(2, 1.4)));
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
        int battleValue,
        float constitution,
        float size,
        float moveSpeed,
        float dexterity,
        byte armor)
    {
        SoldierTemplate template = new(
            60_000 + soldierId,
            TestModelFactory.HumanSpecies,
            $"{name} Template",
            1,
            1,
            false,
            0,
            Array.Empty<ValueTuple<BaseSkill, float>>(),
            battleValue: battleValue,
            // Melee-only, exactly as in the trace -- these creatures have no ranged option at all.
            meleeFraction: 1f);
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
