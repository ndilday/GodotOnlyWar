using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// Phase 5 of Design/Active/EngagementScoringOverhaul.md: the graded damage metric
/// <c>removal = BV * [ P(takeout) + lambda * E[woundProgress; no takeout] ]</c>.
///
/// <para>These tests are written against the two component quantities rather than against a
/// particular lambda, so they hold at every setting of
/// <see cref="BattleSquadPlanner.WoundProgressCreditWeight"/> -- including the 0 that makes the
/// whole phase behaviour-neutral. The first of them is the invariant the phase must not trade
/// away: a squad must not be paid for firing at something it cannot damage.</para>
/// </summary>
public class GradedRemovalTests
{
    private static BattleSoldier Target(string name, int soldierId, int constitution)
    {
        SoldierTemplate template = new(
            30_000 + soldierId,
            TestModelFactory.HumanSpecies,
            $"{name} Template",
            1,
            1,
            false,
            0,
            Array.Empty<ValueTuple<BaseSkill, float>>(),
            battleValue: 10);
        Soldier soldier = TestModelFactory.CreateSoldier(template, name);
        soldier.Id = soldierId;
        soldier.Constitution = constitution;
        BattleSquad squad = new(false, TestModelFactory.CreateSquad(name, soldier));
        return squad.Soldiers[0];
    }

    private static (float TakeOut, float Progress) Components(
        BattleSoldier target,
        float damageCoefficient,
        float effectiveArmor)
    {
        IReadOnlyList<TakeOutLocationTerm> terms = BattleSquadPlanner.BuildTakeOutLocationTerms(
            target, effectiveArmor, weaponWoundMultiplier: 1f);
        return (
            BattleSquadPlanner.EvaluateTakeOutProbability(terms, damageCoefficient),
            BattleSquadPlanner.EvaluateWoundProgress(terms, damageCoefficient));
    }

    [Fact]
    public void GradedRemoval_IsZeroAgainstATargetThatCannotBePenetrated()
    {
        // THE INVARIANT (design doc, "Invariants"): take-out probability replaced raw to-hit
        // scoring precisely so squads would stop firing at targets they cannot hurt. The graded
        // term must not reopen that door -- when the damage roll cannot reach even the
        // wound-ONSET threshold, the Gaussian mass between onset and disable is nil and both
        // terms vanish, at any lambda.
        BattleSoldier armoured = Target("Impenetrable", 30_401, constitution: 20);

        (float takeOut, float progress) = Components(
            armoured, damageCoefficient: 1f, effectiveArmor: 500f);

        Assert.Equal(0f, takeOut, 6);
        Assert.Equal(0f, progress, 6);
        Assert.Equal(
            0f,
            BattleSquadPlanner.CalculateRemovalFractionOnHit(
                armoured,
                damageCoefficient: 1f,
                effectiveArmor: 500f,
                weaponWoundMultiplier: 1f),
            6);
    }

    [Fact]
    public void GradedRemoval_IsPositiveAgainstAPenetrableButNotOneShottableTarget()
    {
        // The gradient the planner was missing. This target's armour is nil, so every hit wounds
        // it, but its constitution puts a one-hit disable far out in the damage roll's tail. Under
        // pure take-out scoring the squad reads "shooting this is worth nothing" and the decision
        // falls entirely to the lookahead -- the reported defect.
        BattleSoldier tough = Target("Carnifex-shaped", 30_402, constitution: 40);

        (float takeOut, float progress) = Components(
            tough, damageCoefficient: 2f, effectiveArmor: 0f);

        Assert.True(
            takeOut < 0.05f,
            $"the scenario needs a target that is hard to one-shot; take-out was {takeOut:0.#####}");
        Assert.True(
            progress > 0.01f,
            $"expected positive credit for wounding a penetrable target, got {progress:0.#####}");
    }

    [Fact]
    public void GradedRemoval_RisesAsTheTargetIsSoftened()
    {
        // Credit assignment, the point of the phase: the twenty hits that soften a target are what
        // make the twenty-first lethal, and the score must reflect that in BOTH directions -- the
        // softened figure is worth more to shoot at, and the hits that softened it were worth
        // taking. CalculateTakeOutProbabilityOnHit was already wound-state aware; this asserts the
        // shipped, lambda-weighted quantity inherits that monotonicity rather than inverting it.
        BattleSoldier fresh = Target("Fresh", 30_403, constitution: 20);
        BattleSoldier softened = Target("Softened", 30_404, constitution: 20);

        HitLocation location = softened.Soldier.Body.HitLocations
            .First(candidate => candidate.Template.IsMotive || candidate.Template.IsVital);
        uint setupWounds = location.Template.CrippleWound switch
        {
            (uint)WoundLevel.Moderate => 5u * (uint)WoundLevel.Minor,
            (uint)WoundLevel.Major => 5u * (uint)WoundLevel.Moderate,
            (uint)WoundLevel.Critical => 5u * (uint)WoundLevel.Major,
            (uint)WoundLevel.Massive => 5u * (uint)WoundLevel.Critical,
            _ => 0u
        };
        Assert.True(setupWounds > 0, "the chosen location must have a reachable cripple threshold");
        location.Wounds = new Wounds(setupWounds, 0);

        float freshRemoval = BattleSquadPlanner.CalculateRemovalFractionOnHit(
            fresh, damageCoefficient: 2f, effectiveArmor: 0f, weaponWoundMultiplier: 1f);
        float softenedRemoval = BattleSquadPlanner.CalculateRemovalFractionOnHit(
            softened, damageCoefficient: 2f, effectiveArmor: 0f, weaponWoundMultiplier: 1f);

        Assert.True(
            softenedRemoval > freshRemoval,
            $"a softened target must score strictly higher ({freshRemoval:0.#####} -> "
                + $"{softenedRemoval:0.#####})");
    }

    [Fact]
    public void GradedRemoval_NeverExceedsTheTargetsWholeBattleValue()
    {
        // lambda interpolates a decomposition of E[progress], so for lambda in [0, 1] the bracket
        // is bounded by 1 and a single hit can never be credited with removing more than the
        // target is worth. (The conditional expectation the design doc's notation suggests would
        // NOT have this property, which is why the partial expectation is used instead.)
        Assert.InRange(BattleSquadPlanner.WoundProgressCreditWeight, 0f, 1f);
        BattleSoldier weak = Target("Soft", 30_405, constitution: 1);

        float removal = BattleSquadPlanner.CalculateRemovalFractionOnHit(
            weak, damageCoefficient: 50f, effectiveArmor: 0f, weaponWoundMultiplier: 1f);

        Assert.InRange(removal, 0f, 1f);
    }
}
