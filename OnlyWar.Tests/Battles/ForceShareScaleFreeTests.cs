using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Battles;
using OnlyWar.Battles.Models;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// Withdrawal and rout decisions read headcount shares: each squad contributes
/// w * able / starting, where w is its share of its OWN side's starting battle value. Nothing
/// compares battle value across sides, so recalibrating BV (a constant factor on every template,
/// or one side's templates moving relative to the other's) must not change any decision. These
/// tests build the same casualty picture with different BV scales and compare the whole chain:
/// the metrics, the voluntary-withdrawal evaluator and the morale force disadvantage.
/// </summary>
public class ForceShareScaleFreeTests
{
    private sealed record Outcome(
        double AttackerRemaining,
        double OpposingRemaining,
        double AttackerLostPreviousTwoRounds,
        double OpposingLostPreviousTwoRounds,
        VoluntaryWithdrawalDecision AttackerDecision,
        VoluntaryWithdrawalReason AttackerReason,
        VoluntaryWithdrawalDecision OpposingDecision,
        VoluntaryWithdrawalReason OpposingReason,
        float AttackerForceDisadvantage,
        float OpposingForceDisadvantage);

    private static SoldierTemplate Template(int id, int battleValue) => new(
        id,
        TestModelFactory.HumanSpecies,
        $"Share Template {id}",
        1,
        1,
        false,
        0,
        Array.Empty<ValueTuple<BaseSkill, float>>(),
        battleValue: battleValue);

    // The attacker's two squads weigh 50/50 inside the side (60 BV each at scale 1); the
    // opposing squads weigh 20/80 (40 and 160 BV). Weights are the only BV the decisions use.
    private static Outcome Run(int attackerScale, int opposingScale)
    {
        BattleSquad a = MoraleServiceHarness.CreateSquad(
            "Attacker A", 91_001, null, 10, false, troopTemplate: Template(91_101, 6 * attackerScale));
        BattleSquad b = MoraleServiceHarness.CreateSquad(
            "Attacker B", 91_002, null, 5, false, troopTemplate: Template(91_102, 12 * attackerScale));
        BattleSquad c = MoraleServiceHarness.CreateSquad(
            "Opposing C", 91_003, null, 8, false, troopTemplate: Template(91_103, 5 * opposingScale));
        BattleSquad d = MoraleServiceHarness.CreateSquad(
            "Opposing D", 91_004, null, 8, false, troopTemplate: Template(91_104, 20 * opposingScale));

        BattleState state = new(
            new Dictionary<int, BattleSquad> { [a.Id] = a, [b.Id] = b },
            new Dictionary<int, BattleSquad> { [c.Id] = c, [d.Id] = d });
        BattleRoundMetrics metrics = new(state);

        // Attacker: A 10 -> 3 able, B 5 -> 2 able, so 0.5 * 0.3 + 0.5 * 0.4 = 0.35 remains.
        // Opposing: C 8 -> 4, D 8 -> 7, so 0.2 * 0.5 + 0.8 * 0.875 = 0.80 remains.
        Kill(state, a.Id, 7);
        Kill(state, b.Id, 3);
        Kill(state, c.Id, 4);
        Kill(state, d.Id, 1);
        metrics.RecordRound();

        BattleForceMetrics attacker = metrics.BuildMetrics(BattleSide.Attacker);
        BattleForceMetrics opposing = metrics.BuildMetrics(BattleSide.Opposing);
        BattleForceEvaluationResult attackerResult = BattleForceEvaluator.Evaluate(new(
            1, "first", Aggression.Normal, attacker, opposing));
        BattleForceEvaluationResult opposingResult = BattleForceEvaluator.Evaluate(new(
            1, "second", Aggression.Normal, opposing, attacker));

        return new Outcome(
            attacker.RemainingShare,
            opposing.RemainingShare,
            attacker.LostSharePreviousTwoRounds,
            opposing.LostSharePreviousTwoRounds,
            attackerResult.Decision,
            attackerResult.Reason,
            opposingResult.Decision,
            opposingResult.Reason,
            BattleMoraleEvaluator.ComputeForceDisadvantage(
                attacker.LostShareSinceStart,
                opposing.LostShareSinceStart,
                attacker.LostSharePreviousTwoRounds,
                opposing.LostSharePreviousTwoRounds),
            BattleMoraleEvaluator.ComputeForceDisadvantage(
                opposing.LostShareSinceStart,
                attacker.LostShareSinceStart,
                opposing.LostSharePreviousTwoRounds,
                attacker.LostSharePreviousTwoRounds));
    }

    private static void Kill(BattleState state, int squadId, int count)
    {
        BattleSquad squad = state.GetSquad(squadId);
        foreach (BattleSoldier soldier in squad.AbleSoldiers.Take(count).ToList())
        {
            squad.RemoveSoldier(soldier);
        }
    }

    [Fact]
    public void BaselineScenario_HasTheShareValuesAndDecisionsTheScenarioComments()
    {
        Outcome outcome = Run(attackerScale: 1, opposingScale: 1);

        Assert.Equal(0.35, outcome.AttackerRemaining, 9);
        Assert.Equal(0.80, outcome.OpposingRemaining, 9);
        // Everything was lost within the two-round window of this single recorded round.
        Assert.Equal(0.65, outcome.AttackerLostPreviousTwoRounds, 9);
        Assert.Equal(0.20, outcome.OpposingLostPreviousTwoRounds, 9);
        Assert.Equal(VoluntaryWithdrawalDecision.FightingWithdrawal, outcome.AttackerDecision);
        Assert.Equal(VoluntaryWithdrawalReason.EligibleAndOutmatched, outcome.AttackerReason);
        Assert.Equal(VoluntaryWithdrawalDecision.Engaged, outcome.OpposingDecision);
        Assert.Equal(VoluntaryWithdrawalReason.AboveCasualtyThreshold, outcome.OpposingReason);
        Assert.True(outcome.AttackerForceDisadvantage > 0.5f);
        Assert.Equal(0f, outcome.OpposingForceDisadvantage);
    }

    [Theory]
    [InlineData(1, 10)]   // enemy templates at BV x10 (6 -> 60 style): cross-side change only
    [InlineData(10, 1)]   // our templates at BV x10: the old rule would have stopped calling us outmatched
    [InlineData(1, 1000)]
    [InlineData(7, 7)]    // every BV in the game multiplied by a constant
    [InlineData(3, 11)]   // each side rescaled independently
    public void Decisions_DoNotChangeWhenBattleValueIsRescaled(int attackerScale, int opposingScale)
    {
        Outcome baseline = Run(attackerScale: 1, opposingScale: 1);

        Outcome rescaled = Run(attackerScale, opposingScale);

        Assert.Equal(baseline, rescaled);
    }
}
