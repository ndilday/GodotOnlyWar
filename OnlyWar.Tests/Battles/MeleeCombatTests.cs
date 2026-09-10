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
using SoldierAttribute = OnlyWar.Domain.Soldiers.Attribute;

namespace OnlyWar.Tests.Battles;

public class MeleeCombatTests
{
    private static readonly BaseSkill PrimaryParrySkill = new(701, SkillCategory.Melee, "Primary Parry", SoldierAttribute.Strength, 0);
    private static readonly BaseSkill OffHandParrySkill = new(702, SkillCategory.Melee, "Off-Hand Parry", SoldierAttribute.Strength, 0);
    private static readonly BaseSkill AttackSkill = new(703, SkillCategory.Melee, "Attack Skill", SoldierAttribute.Strength, 0);

    private static BattleSquad CreateBattleSquad(string squadName, int soldierId, string soldierName, Action<Soldier> configureSoldier = null)
    {
        Soldier soldier = TestModelFactory.CreateSoldier(name: soldierName);
        soldier.Id = soldierId;
        configureSoldier?.Invoke(soldier);
        return new BattleSquad(true, TestModelFactory.CreateSquad(squadName, soldier));
    }

    private static BattleSoldier CreateBattleSoldier(string name, int id)
    {
        Soldier soldier = TestModelFactory.CreateSoldier(name: name);
        soldier.Id = id;
        return new BattleSoldier(soldier, null);
    }

    private static MeleeWeapon CreateMeleeWeapon(
        int id,
        string name,
        BaseSkill relatedSkill,
        float parryModifier = 0,
        float attackSpeedMultiplier = 1,
        float strengthMultiplier = 1)
    {
        return new MeleeWeapon(new MeleeWeaponTemplate(
            id,
            name,
            EquipLocation.OneHand,
            relatedSkill,
            accuracy: 0,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            strengthMultiplier: strengthMultiplier,
            parryMod: parryModifier,
            attackSpeedMultiplier: attackSpeedMultiplier));
    }

    private static BattleState CreateState(BattleSquad attacker, BattleSquad defender)
    {
        return new BattleState(
            new Dictionary<int, BattleSquad> { [attacker.Id] = attacker },
            new Dictionary<int, BattleSquad> { [defender.Id] = defender });
    }

    [Fact]
    public void DualWield_EquipsBothOneHandedWeapons_AndUsesOffHandProfileForDefense()
    {
        BattleSoldier defender = CreateBattleSoldier("Defender", 2);
        defender.Soldier.AddSkillPoints(PrimaryParrySkill, 1);
        defender.Soldier.AddSkillPoints(OffHandParrySkill, 8);

        MeleeWeapon primary = CreateMeleeWeapon(11, "Primary Blade", PrimaryParrySkill, parryModifier: 0);
        MeleeWeapon offHand = CreateMeleeWeapon(12, "Off-Hand Blade", OffHandParrySkill, parryModifier: 1);
        defender.AddWeapons([], [primary, offHand]);

        Assert.True(defender.IsDualWieldingMelee());
        Assert.Equal(2, defender.EquippedMeleeWeapons.Count);
        Assert.Equal(0, defender.HandsFree);
        Assert.Equal(1f, defender.GetMeleeParryModifier());
        // defense comes from parry modifiers only — no flat dual-wield bonus
        Assert.Equal(1f, MeleeAttackAction.GetDefenderDefenseModifier(defender));
        Assert.Equal(defender.Soldier.GetTotalSkillValue(OffHandParrySkill), MeleeAttackAction.GetDefenderMeleeSkill(defender, AttackSkill));
    }

    [Fact]
    public void ParryModifier_ModelSupportsSummingAndNegativeFistValues()
    {
        MeleeWeapon duellingBlade = CreateMeleeWeapon(21, "Duelling Blade", PrimaryParrySkill, parryModifier: 2);
        MeleeWeapon fist = CreateMeleeWeapon(22, "Fist", PrimaryParrySkill, parryModifier: -1);

        float totalParry = duellingBlade.Template.ParryModifier + fist.Template.ParryModifier;

        Assert.Equal(1f, totalParry);
        Assert.Equal(-1f, fist.Template.ParryModifier);
    }

    [Fact]
    public void ChargingSoldier_ForfeitsWeaponParryWithoutAdditionalDefensePenalty()
    {
        BattleSoldier defender = CreateBattleSoldier("Charging Defender", 2);
        defender.AddWeapons([],
        [
            CreateMeleeWeapon(23, "Primary Blade", PrimaryParrySkill, parryModifier: 2),
            CreateMeleeWeapon(24, "Off-Hand Blade", OffHandParrySkill, parryModifier: 1)
        ]);

        Assert.Equal(3f, MeleeAttackAction.GetDefenderDefenseModifier(defender));
        Assert.Equal(0f, MeleeAttackAction.GetDefenderDefenseModifier(
            defender,
            forfeitsWeaponParry: true));
    }

    [Fact]
    public void ChargeAction_IsMarkedAndAlwaysUsesExistingMovedAttackPenalty()
    {
        BattleSoldier attacker = CreateBattleSoldier("Charger", 1);
        BattleSoldier defender = CreateBattleSoldier("Defender", 2);
        MeleeWeapon weapon = CreateMeleeWeapon(25, "Charge Blade", AttackSkill);
        attacker.AddWeapons([], [weapon]);

        MeleeAttackAction action = new(
            attacker,
            defender,
            weapon,
            didMove: false,
            log: null,
            random: new SeededRNG(12345),
            meleeWeaponTemplates: CreateMeleeTemplateMap(attacker, defender),
            isCharge: true);

        Assert.True(action.IsCharge);
        Assert.True(action.UsesMovementAttackPenalty);
        Assert.Equal(2f, MeleeAttackAction.MovementAttackPenalty);
    }

    [Fact]
    public void ApplyChargeParryForfeitures_IsSafeRegardlessOfMeleeExecutionOrder()
    {
        BattleSoldier charger = CreateBattleSoldier("Charger", 1);
        BattleSoldier opponent = CreateBattleSoldier("Opponent", 2);
        MeleeWeapon chargeWeapon = CreateMeleeWeapon(26, "Charge Blade", AttackSkill, parryModifier: 3);
        MeleeWeapon opponentWeapon = CreateMeleeWeapon(27, "Opponent Blade", AttackSkill);
        charger.AddWeapons([], [chargeWeapon]);
        opponent.AddWeapons([], [opponentWeapon]);

        MeleeAttackAction chargeAction = new(
            charger,
            opponent,
            chargeWeapon,
            didMove: true,
            log: null,
            random: new SeededRNG(1),
            meleeWeaponTemplates: CreateMeleeTemplateMap(charger, opponent),
            isCharge: true);
        MeleeAttackAction opponentAction = new(
            opponent,
            charger,
            opponentWeapon,
            didMove: false,
            log: null,
            random: new SeededRNG(2),
            meleeWeaponTemplates: CreateMeleeTemplateMap(charger, opponent));

        MeleeAttackAction.ApplyChargeParryForfeitures([opponentAction, chargeAction]);

        Assert.True(opponentAction.TreatsDefenderAsCharging(charger.Soldier.Id));
        Assert.False(chargeAction.TreatsDefenderAsCharging(opponent.Soldier.Id));
    }

    [Fact]
    public void MeleeAttackAction_IncrementsTurnsSwingingOnceWhenExecuted()
    {
        BattleSquad attacker = CreateBattleSquad("Attackers", 1, "Attacker");
        BattleSquad defender = CreateBattleSquad("Defenders", 2, "Defender");
        BattleSoldier attackerSoldier = attacker.Soldiers[0];
        BattleSoldier defenderSoldier = defender.Soldiers[0];

        MeleeWeapon attackerWeapon = CreateMeleeWeapon(30, "Knife", AttackSkill);
        attackerSoldier.Soldier.AddSkillPoints(AttackSkill, 1_000_000);
        attackerSoldier.AddWeapons([], [attackerWeapon]);
        defenderSoldier.AddWeapons([], [CreateMeleeWeapon(31, "Knife", PrimaryParrySkill)]);
        attackerSoldier.TopLeft = (0, 0);
        defenderSoldier.TopLeft = (1, 0);

        BattleState state = CreateState(attacker, defender);
        MeleeAttackAction action = new(
            attackerSoldier,
            [new PlannedMeleeStrike(defenderSoldier.Soldier.Id, attackerWeapon.Template.Id, defenderSoldier.Soldier.Name, attackerWeapon.Template.Name)],
            false,
            null,
            new SeededRNG(12345),
            CreateMeleeTemplateMap(attackerSoldier, defenderSoldier));

        action.Execute(state);

        Assert.Equal((ushort)1, state.GetSoldier(attackerSoldier.Soldier.Id).TurnsSwinging);
        Assert.Single(action.TargetedDefenderIds);
        Assert.Contains(defenderSoldier.Soldier.Id, action.TargetedDefenderIds);
    }

    [Fact]
    public void MeleeAttackAction_AwardsMeleeSkillXpToBothAttackerAndDefender()
    {
        BattleSquad attacker = CreateBattleSquad("Attackers", 1, "Attacker");
        BattleSquad defender = CreateBattleSquad("Defenders", 2, "Defender");
        BattleSoldier attackerSoldier = attacker.Soldiers[0];
        BattleSoldier defenderSoldier = defender.Soldiers[0];

        MeleeWeapon attackerWeapon = CreateMeleeWeapon(34, "Knife", AttackSkill);
        attackerSoldier.AddWeapons([], [attackerWeapon]);
        defenderSoldier.AddWeapons([], [CreateMeleeWeapon(35, "Knife", PrimaryParrySkill)]);
        attackerSoldier.TopLeft = (0, 0);
        defenderSoldier.TopLeft = (1, 0);

        BattleState state = CreateState(attacker, defender);
        MeleeAttackAction action = new(
            attackerSoldier,
            [new PlannedMeleeStrike(defenderSoldier.Soldier.Id, attackerWeapon.Template.Id, defenderSoldier.Soldier.Name, attackerWeapon.Template.Name)],
            false,
            null,
            new SeededRNG(12345),
            CreateMeleeTemplateMap(attackerSoldier, defenderSoldier));

        action.Execute(state);

        // The attacker learns from the swing; the defender learns from actively turning it aside.
        Assert.True(state.GetSoldier(attackerSoldier.Soldier.Id).MeleeSkillXp > 0);
        Assert.True(state.GetSoldier(defenderSoldier.Soldier.Id).MeleeSkillXp > 0);
    }

    [Fact]
    public void MeleeAttackAction_RepeatedExecute_DoesNotDuplicateWounds()
    {
        BattleSquad attacker = CreateBattleSquad("Attackers", 1, "Attacker",
            soldier => soldier.Strength = float.MaxValue);
        BattleSquad defender = CreateBattleSquad("Defenders", 2, "Defender");
        BattleSoldier attackerSoldier = attacker.Soldiers[0];
        BattleSoldier defenderSoldier = defender.Soldiers[0];

        MeleeWeapon attackerWeapon = CreateMeleeWeapon(32, "Power Knife", AttackSkill, strengthMultiplier: 1);
        attackerSoldier.AddWeapons([], [attackerWeapon]);
        defenderSoldier.AddWeapons([], [CreateMeleeWeapon(33, "Knife", PrimaryParrySkill)]);
        attackerSoldier.TopLeft = (0, 0);
        defenderSoldier.TopLeft = (1, 0);

        BattleState state = CreateState(attacker, defender);
        MeleeAttackAction action = new(
            attackerSoldier,
            [new PlannedMeleeStrike(defenderSoldier.Soldier.Id, attackerWeapon.Template.Id, defenderSoldier.Soldier.Name, attackerWeapon.Template.Name)],
            false,
            null,
            new SeededRNG(12345),
            CreateMeleeTemplateMap(attackerSoldier, defenderSoldier));

        action.Execute(state);
        int woundCountAfterFirstExecute = action.WoundResolutions.Count;
        Assert.NotEmpty(action.WoundResolutions);
        action.Execute(state);

        Assert.Equal(woundCountAfterFirstExecute, action.WoundResolutions.Count);
        Assert.Equal((ushort)1, state.GetSoldier(attackerSoldier.Soldier.Id).TurnsSwinging);
        Assert.Single(action.TargetedDefenderIds);
    }

    [Fact]
    public void MeleeAttackAction_DescriptionUsesSingularTimeAndReportsArmorStoppedHit()
    {
        BattleSquad attacker = CreateBattleSquad("Attackers", 1, "Attacker");
        BattleSquad defender = CreateBattleSquad("Defenders", 2, "Defender");
        BattleSoldier attackerSoldier = attacker.Soldiers[0];
        BattleSoldier defenderSoldier = defender.Soldiers[0];
        MeleeWeapon weapon = CreateMeleeWeapon(34, "Knife", AttackSkill);
        attackerSoldier.Soldier.AddSkillPoints(AttackSkill, 1_000_000);
        attackerSoldier.AddWeapons([], [weapon]);
        defenderSoldier.Armor = new Armor(
            new ArmorTemplate(999, "Heavy Armor", byte.MaxValue, 0));
        attackerSoldier.TopLeft = (0, 0);
        defenderSoldier.TopLeft = (1, 0);
        MeleeAttackAction action = new(
            attackerSoldier,
            [new PlannedMeleeStrike(
                defenderSoldier.Soldier.Id,
                weapon.Template.Id,
                defenderSoldier.Soldier.Name,
                weapon.Template.Name)],
            didMove: false,
            log: null,
            random: new SeededRNG(1),
            meleeWeaponTemplates: CreateMeleeTemplateMap(attackerSoldier, defenderSoldier));

        action.Execute(CreateState(attacker, defender));

        Assert.Empty(action.WoundResolutions);
        Assert.Contains("Hitting 1 time, but doing no damage", action.Description());
    }

    [Fact]
    public void AttackSpeed_10_YieldsOneBaseAttack_And15HasASubstantialSecondSwingChance()
    {
        Assert.Equal(1.0f, MeleeMath.CalculateBaseAttackCount(10, 1), precision: 4);
        Assert.Equal(1, MeleeMath.CalculateGuaranteedAttackCount(15, 1));
        Assert.Equal(0.5f, MeleeMath.CalculateFractionalAttackChance(15, 1), precision: 4);

        SeededRNG random = new(12345);
        int secondSwingCount = 0;
        for (int trial = 0; trial < 10_000; trial++)
        {
            int attacks = MeleeMath.CalculateGuaranteedAttackCount(15, 1);
            if (random.GetLinearDouble() < MeleeMath.CalculateFractionalAttackChance(15, 1))
            {
                attacks++;
            }

            if (attacks == 2)
            {
                secondSwingCount++;
            }
        }

        Assert.InRange((double)secondSwingCount / 10_000, 0.47, 0.53);
    }

    [Fact]
    public void SpeedMultiplier_OneLeavesBaseAttackCountUnchanged()
    {
        Assert.Equal(2.0f, MeleeMath.CalculateBaseAttackCount(20, 1), precision: 4);
        Assert.Equal(2, MeleeMath.CalculateGuaranteedAttackCount(20, 1));
    }

    [Fact]
    public void MultipleAttackConfidence_ReachesSeventyFivePercentAtExpectedTrialCount()
    {
        int trials = MeleeMath.CalculateTrialsForCumulativeSuccess(0.5f);

        Assert.Equal(2, trials);
        Assert.Equal(0.75, 1 - System.Math.Pow(1 - 0.5, trials), precision: 6);
    }

    [Fact]
    public void Planner_CommitsToTakeOutTargetsBeforeMovingOn()
    {
        BattleSquad attackerSquad = CreateBattleSquad("Attackers", 1, "Attacker",
            soldier =>
            {
                soldier.AttackSpeed = 30;
                soldier.Strength = 1_000;
                soldier.AddSkillPoints(TestSkills.Melee, 1_000_000);
            });
        // Two adjacent defenders in SEPARATE squads, deliberately. A soldier commits his blows to
        // the enemies he is actually in contact with, whatever squad they belong to; collapsing
        // them into one squad would hide the cross-squad adjacency gap rather than test it.
        BattleSquad firstDefenderSquad = CreateBattleSquad("First Defender", 20, "First Defender");
        BattleSquad secondDefenderSquad = CreateBattleSquad("Second Defender", 30, "Second Defender");

        BattleSoldier attacker = attackerSquad.Soldiers[0];
        attacker.ClearReadiedRangedWeapons();
        attacker.ClearReadiedMeleeWeapons();
        attacker.ReadyWeapon(attacker.MeleeWeapons[0]);

        BattleSoldier firstDefender = firstDefenderSquad.Soldiers[0];
        BattleSoldier secondDefender = secondDefenderSquad.Soldiers[0];
        attacker.TopLeft = (0, 0);
        firstDefender.TopLeft = (1, 0);
        secondDefender.TopLeft = (0, 1);

        BattleGridManager grid = new();
        grid.PlaceSoldier(attacker, true, attacker.PositionList.ToList());
        grid.PlaceSoldier(firstDefender, false, firstDefender.PositionList.ToList());
        grid.PlaceSoldier(secondDefender, false, secondDefender.PositionList.ToList());

        List<IAction> moveActions = [];
        List<IAction> meleeActions = [];
        BattleSquadPlanner planner = new(
            grid,
            new Dictionary<int, BattleSoldier>
            {
                [attacker.Soldier.Id] = attacker,
                [firstDefender.Soldier.Id] = firstDefender,
                [secondDefender.Soldier.Id] = secondDefender
            },
            new List<IAction>(),
            moveActions,
            meleeActions,
            null,
            CreateMeleeTemplateMap(attacker, firstDefender, secondDefender),
            new SeededRNG(12345));

        attackerSquad.IsInMelee = true;
        EngagementPathDriver.PlanAndResolveClosingMoves(
            planner, attackerSquad, [firstDefenderSquad, secondDefenderSquad], moveActions);

        MeleeAttackAction action = Assert.Single(meleeActions.OfType<MeleeAttackAction>());
        Assert.Equal(3, action.StrikePlans.Count);
        Assert.Equal(2, action.StrikePlans.Count(strike => strike.TargetId == firstDefender.Soldier.Id));
        Assert.Equal(1, action.StrikePlans.Count(strike => strike.TargetId == secondDefender.Soldier.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ChargeMarkedOnArrival_IsSpentByTheFollowingTurnsStrike(bool arrivedLastTurn)
    {
        // A charge is split across the turn boundary: the closing pass marks the arriving soldier
        // and the next turn's attack phase spends the mark (TDD §6.6).
        // BOTH CASES MATTER. Without the false case this passes against a planner that marks every
        // strike as a charge, which is the same defect in the opposite direction — a soldier who
        // has stood in contact since before last turn is fighting set, not charging.
        ChargeFixture fixture = CreateAdjacentPairFixture();
        fixture.Attacker.ChargedIntoContactLastTurn = arrivedLastTurn;

        EngagementPathDriver.Plan(
            fixture.Planner, fixture.AttackerSquad, [fixture.DefenderSquad]);

        MeleeAttackAction action = Assert.Single(fixture.MeleeActions.OfType<MeleeAttackAction>());
        Assert.Equal(arrivedLastTurn, action.IsCharge);
        Assert.Equal(arrivedLastTurn, action.UsesMovementAttackPenalty);
    }

    [Fact]
    public void ClosingSquadWhoseTargetsAreAllGone_ResolvesWithoutMovingOrStriking()
    {
        // The closing pass runs after the attack phase, so its target squad can have been wiped out
        // by fire in the same turn. It must resolve to nothing rather than throwing or charging an
        // empty square.
        ChargeFixture fixture = CreateAdjacentPairFixture(separation: 6);
        fixture.AttackerSquad.IsInMelee = true;

        EngagementPathDriver.Plan(
            fixture.Planner, fixture.AttackerSquad, [fixture.DefenderSquad]);
        SquadClosingMoveAction closing =
            Assert.Single(fixture.MoveActions.OfType<SquadClosingMoveAction>());

        fixture.Grid.RemoveSoldier(fixture.Defender.Soldier.Id);
        fixture.Defender.TopLeft = null;

        closing.Execute(null);

        Assert.Empty(closing.ResolvedMovementActions);
        Assert.Empty(fixture.MeleeActions.OfType<MeleeAttackAction>());
        Assert.False(fixture.Attacker.ChargedIntoContactLastTurn);
    }

    private sealed class ChargeFixture
    {
        public BattleSquad AttackerSquad { get; init; }
        public BattleSquad DefenderSquad { get; init; }
        public BattleSoldier Attacker { get; init; }
        public BattleSoldier Defender { get; init; }
        public BattleGridManager Grid { get; init; }
        public BattleSquadPlanner Planner { get; init; }
        public List<IAction> MeleeActions { get; init; }
        public List<IAction> MoveActions { get; init; }
    }

    private static ChargeFixture CreateAdjacentPairFixture(int separation = 1)
    {
        MeleeWeaponTemplate claw = CreateMeleeWeapon(811, "Charge Claw", AttackSkill).Template;
        MeleeWeaponTemplate guard = CreateMeleeWeapon(812, "Set Guard", PrimaryParrySkill).Template;
        Species attackerSpecies = CreateSpecies(811, "Charging Species", claw);
        Species defenderSpecies = CreateSpecies(812, "Braced Species", guard);
        SoldierTemplate attackerTemplate = new(
            811, attackerSpecies, "Charging Fighter", 1, 1, false, 0, []);
        SoldierTemplate defenderTemplate = new(
            812, defenderSpecies, "Braced Fighter", 1, 1, false, 0, []);
        Soldier attackerModel = TestModelFactory.CreateSoldier(
            attackerTemplate, "Charger", skills: new Skill(AttackSkill, 8));
        Soldier defenderModel = TestModelFactory.CreateSoldier(
            defenderTemplate, "Braced", skills: new Skill(PrimaryParrySkill, 8));
        attackerModel.Id = 811;
        defenderModel.Id = 812;
        BattleSquad attackerSquad = new(
            true, TestModelFactory.CreateSquad("Chargers", attackerModel));
        BattleSquad defenderSquad = new(
            false, TestModelFactory.CreateSquad("Braced", defenderModel));
        BattleSoldier attacker = attackerSquad.Soldiers.Single();
        BattleSoldier defender = defenderSquad.Soldiers.Single();
        foreach (BattleSoldier soldier in new[] { attacker, defender })
        {
            soldier.RangedWeapons.Clear();
            soldier.ClearReadiedRangedWeapons();
            soldier.MeleeWeapons.Clear();
            soldier.ClearReadiedMeleeWeapons();
        }
        attacker.TopLeft = (0, 0);
        defender.TopLeft = (separation, 0);

        BattleGridManager grid = new();
        grid.PlaceSoldier(attacker, true, attacker.PositionList.ToList());
        grid.PlaceSoldier(defender, false, defender.PositionList.ToList());
        List<IAction> meleeActions = [];
        List<IAction> moveActions = [];
        BattleSquadPlanner planner = new(
            grid,
            new Dictionary<int, BattleSoldier>
            {
                [attacker.Soldier.Id] = attacker,
                [defender.Soldier.Id] = defender
            },
            new List<IAction>(),
            moveActions,
            meleeActions,
            null,
            CreateMeleeTemplateMap(attacker, defender),
            new SeededRNG(12345));
        attackerSquad.IsInMelee = separation <= 1;

        return new ChargeFixture
        {
            AttackerSquad = attackerSquad,
            DefenderSquad = defenderSquad,
            Attacker = attacker,
            Defender = defender,
            Grid = grid,
            Planner = planner,
            MeleeActions = meleeActions,
            MoveActions = moveActions
        };
    }

    [Fact]
    public void UnarmedPlannerAndDefense_UseEachCombatantsSpeciesDefault()
    {
        MeleeWeaponTemplate attackerDefault = CreateMeleeWeapon(
            801,
            "Species Claw",
            AttackSkill).Template;
        MeleeWeaponTemplate defenderDefault = CreateMeleeWeapon(
            802,
            "Species Guard",
            PrimaryParrySkill).Template;
        Species attackerSpecies = CreateSpecies(801, "Clawed Species", attackerDefault);
        Species defenderSpecies = CreateSpecies(802, "Guarding Species", defenderDefault);
        SoldierTemplate attackerTemplate = new(
            801, attackerSpecies, "Clawed Fighter", 1, 1, false, 0, []);
        SoldierTemplate defenderTemplate = new(
            802, defenderSpecies, "Guarding Fighter", 1, 1, false, 0, []);
        Soldier attackerModel = TestModelFactory.CreateSoldier(
            attackerTemplate,
            "Attacker",
            skills: new Skill(AttackSkill, 4));
        Soldier defenderModel = TestModelFactory.CreateSoldier(
            defenderTemplate,
            "Defender",
            skills: new Skill(PrimaryParrySkill, 16));
        attackerModel.Id = 801;
        defenderModel.Id = 802;
        BattleSquad attackerSquad = new(
            true,
            TestModelFactory.CreateSquad("Attackers", attackerModel));
        BattleSquad defenderSquad = new(
            false,
            TestModelFactory.CreateSquad("Defenders", defenderModel));
        BattleSoldier attacker = attackerSquad.Soldiers.Single();
        BattleSoldier defender = defenderSquad.Soldiers.Single();
        attacker.RangedWeapons.Clear();
        attacker.ClearReadiedRangedWeapons();
        attacker.MeleeWeapons.Clear();
        attacker.ClearReadiedMeleeWeapons();
        defender.RangedWeapons.Clear();
        defender.ClearReadiedRangedWeapons();
        defender.MeleeWeapons.Clear();
        defender.ClearReadiedMeleeWeapons();
        attacker.TopLeft = (0, 0);
        defender.TopLeft = (1, 0);

        BattleGridManager grid = new();
        grid.PlaceSoldier(attacker, true, attacker.PositionList.ToList());
        grid.PlaceSoldier(defender, false, defender.PositionList.ToList());
        List<IAction> moveActions = [];
        List<IAction> meleeActions = [];
        BattleSquadPlanner planner = new(
            grid,
            new Dictionary<int, BattleSoldier>
            {
                [attacker.Soldier.Id] = attacker,
                [defender.Soldier.Id] = defender
            },
            new List<IAction>(),
            moveActions,
            meleeActions,
            null,
            CreateMeleeTemplateMap(attacker, defender),
            new SeededRNG(12345));

        attackerSquad.IsInMelee = true;
        EngagementPathDriver.PlanAndResolveClosingMoves(
            planner, attackerSquad, [defenderSquad], moveActions);

        MeleeAttackAction action = Assert.Single(meleeActions.OfType<MeleeAttackAction>());
        PlannedMeleeStrike strike = Assert.Single(action.StrikePlans);
        Assert.Equal(attackerDefault.Id, strike.WeaponTemplateId);
        Assert.Equal(
            defender.Soldier.GetTotalSkillValue(PrimaryParrySkill),
            MeleeAttackAction.GetDefenderMeleeSkill(defender, AttackSkill));
    }

    private static Species CreateSpecies(
        int id,
        string name,
        MeleeWeaponTemplate defaultUnarmedWeapon)
    {
        return new Species(
            id,
            name,
            Value(10),
            Value(10),
            Value(10),
            Value(10),
            Value(10),
            Value(10),
            Value(10),
            Value(0),
            Value(10),
            Value(6),
            Value(1),
            1,
            1,
            0,
            0,
            SpeciesAbilities.None,
            HumanBodyTemplate.Instance,
            defaultUnarmedWeapon);
    }

    private static NormalizedValueTemplate Value(float value) => new()
    {
        BaseValue = value,
        StandardDeviation = 0
    };

    private static IReadOnlyDictionary<int, MeleeWeaponTemplate> CreateMeleeTemplateMap(
        params BattleSoldier[] soldiers)
    {
        return soldiers
            .SelectMany(soldier => soldier.MeleeWeapons
                .Concat(soldier.EquippedMeleeWeapons)
                .Select(weapon => weapon.Template)
                .Append(soldier.Soldier.Template.Species.DefaultUnarmedWeapon))
            .GroupBy(template => template.Id)
            .ToDictionary(group => group.Key, group => group.First());
    }
}
