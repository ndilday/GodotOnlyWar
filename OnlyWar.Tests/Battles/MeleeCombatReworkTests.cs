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
using SoldierAttribute = OnlyWar.Models.Soldiers.Attribute;

namespace OnlyWar.Tests.Battles;

public class MeleeCombatReworkTests
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
        attackerSoldier.TopLeft = new Tuple<int, int>(0, 0);
        defenderSoldier.TopLeft = new Tuple<int, int>(1, 0);

        BattleState state = CreateState(attacker, defender);
        MeleeAttackAction action = new(
            attackerSoldier,
            [new PlannedMeleeStrike(defenderSoldier.Soldier.Id, attackerWeapon.Template.Id, defenderSoldier.Soldier.Name, attackerWeapon.Template.Name)],
            false,
            null);

        action.Execute(state);

        Assert.Equal((ushort)1, state.GetSoldier(attackerSoldier.Soldier.Id).TurnsSwinging);
        Assert.Single(action.TargetedDefenderIds);
        Assert.Contains(defenderSoldier.Soldier.Id, action.TargetedDefenderIds);
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
        attackerSoldier.TopLeft = new Tuple<int, int>(0, 0);
        defenderSoldier.TopLeft = new Tuple<int, int>(1, 0);

        BattleState state = CreateState(attacker, defender);
        MeleeAttackAction action = new(
            attackerSoldier,
            [new PlannedMeleeStrike(defenderSoldier.Soldier.Id, attackerWeapon.Template.Id, defenderSoldier.Soldier.Name, attackerWeapon.Template.Name)],
            false,
            null);

        action.Execute(state);
        int woundCountAfterFirstExecute = action.WoundResolutions.Count;
        Assert.NotEmpty(action.WoundResolutions);
        action.Execute(state);

        Assert.Equal(woundCountAfterFirstExecute, action.WoundResolutions.Count);
        Assert.Equal((ushort)1, state.GetSoldier(attackerSoldier.Soldier.Id).TurnsSwinging);
        Assert.Single(action.TargetedDefenderIds);
    }

    [Fact]
    public void AttackSpeed_10_YieldsOneBaseAttack_And15HasASubstantialSecondSwingChance()
    {
        Assert.Equal(1.0f, MeleeMath.CalculateBaseAttackCount(10, 1), precision: 4);
        Assert.Equal(1, MeleeMath.CalculateGuaranteedAttackCount(15, 1));
        Assert.Equal(0.5f, MeleeMath.CalculateFractionalAttackChance(15, 1), precision: 4);

        RNG.Reset(12345);
        int secondSwingCount = 0;
        for (int trial = 0; trial < 10_000; trial++)
        {
            int attacks = MeleeMath.CalculateGuaranteedAttackCount(15, 1);
            if (RNG.GetLinearDouble() < MeleeMath.CalculateFractionalAttackChance(15, 1))
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
        BattleSquad firstDefenderSquad = CreateBattleSquad("First Defender", 20, "First Defender");
        BattleSquad secondDefenderSquad = CreateBattleSquad("Second Defender", 30, "Second Defender");

        BattleSoldier attacker = attackerSquad.Soldiers[0];
        attacker.EquippedRangedWeapons.Clear();
        attacker.EquippedMeleeWeapons.Clear();
        attacker.EquippedMeleeWeapons.Add(attacker.MeleeWeapons[0]);

        BattleSoldier firstDefender = firstDefenderSquad.Soldiers[0];
        BattleSoldier secondDefender = secondDefenderSquad.Soldiers[0];
        attacker.TopLeft = new Tuple<int, int>(0, 0);
        firstDefender.TopLeft = new Tuple<int, int>(1, 0);
        secondDefender.TopLeft = new Tuple<int, int>(0, 1);

        BattleGridManager grid = new();
        grid.PlaceSoldier(attacker, true, attacker.PositionList.ToList());
        grid.PlaceSoldier(firstDefender, false, firstDefender.PositionList.ToList());
        grid.PlaceSoldier(secondDefender, false, secondDefender.PositionList.ToList());

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
            new List<IAction>(),
            meleeActions,
            null,
            attacker.EquippedMeleeWeapons[0]);

        attackerSquad.IsInMelee = true;
        planner.PrepareActions(attackerSquad);

        MeleeAttackAction action = Assert.Single(meleeActions.OfType<MeleeAttackAction>());
        Assert.Equal(3, action.StrikePlans.Count);
        Assert.Equal(2, action.StrikePlans.Count(strike => strike.TargetId == firstDefender.Soldier.Id));
        Assert.Equal(1, action.StrikePlans.Count(strike => strike.TargetId == secondDefender.Soldier.Id));
    }
}
