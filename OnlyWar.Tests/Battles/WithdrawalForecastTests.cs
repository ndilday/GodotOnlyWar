using System.Collections.Generic;
using OnlyWar.Helpers.Battles;
using Xunit;

namespace OnlyWar.Tests.Battles;

public class WithdrawalForecastTests
{
    private static readonly WithdrawalForecast.Projection Baseline = new(5, 30, 2, 0);

    private static WithdrawalForecast.Candidate Candidate(
        int id, int survivors, float bv, float enemyDistance = 4, float delay = 2) =>
        new(id, true, false, true, false, 2, enemyDistance, delay,
            new WithdrawalForecast.Projection(survivors, bv, 1, 1));

    private static WithdrawalForecast.Result Evaluate(params WithdrawalForecast.Candidate[] candidates) =>
        WithdrawalForecast.Evaluate(new WithdrawalForecast.Input(9, true, Baseline, candidates));

    [Fact]
    public void OpenGroundProjection_IsDeterministicAndRearGuardDelayCanSaveMainBody()
    {
        var squads = new[]
        {
            new WithdrawalForecast.SquadGeometry(10, 2, 12, 8, 6),
            new WithdrawalForecast.SquadGeometry(11, 4, 30, 12, 6)
        };

        WithdrawalForecast.Projection ordinary = WithdrawalForecast.ProjectOpenGround(
            squads, fastestPursuerSpeed: 9, oneTurnAttackReach: 3);
        WithdrawalForecast.Projection guarded = WithdrawalForecast.ProjectOpenGround(
            squads, fastestPursuerSpeed: 9, oneTurnAttackReach: 3,
            rearGuardSquadId: 10, rearGuardDelayTurns: 2);

        Assert.Equal(0, ordinary.ExpectedAbleSurvivors);
        Assert.Equal(4, guarded.ExpectedAbleSurvivors);
        Assert.Equal(30, guarded.ExpectedSurvivingBattleValue);
        Assert.Equal(1, guarded.ExpectedSquadsIntercepted);
        Assert.Equal(1, guarded.ExpectedMainBodyMaskedDepartures);
    }

    [Fact]
    public void NoSacrifice_ForNoWholeSurvivorBenefit()
    {
        WithdrawalForecast.Result result = Evaluate(Candidate(10, 5, 40));

        Assert.Null(result.SelectedSquadId);
        Assert.Equal("insufficient_survivor_improvement", result.CandidateEvaluations[0].Reason);
    }

    [Fact]
    public void SafelyWithdrawingSquad_IsNeverRecalled()
    {
        WithdrawalForecast.Candidate safe = Candidate(10, 9, 60) with { IsSafelyWithdrawing = true };

        WithdrawalForecast.Result result = Evaluate(safe);

        Assert.Null(result.SelectedSquadId);
        Assert.Equal("safely_withdrawing", result.CandidateEvaluations[0].Reason);
    }

    [Fact]
    public void GreatestSurvivorBenefit_WinsBeforeBattleValue()
    {
        WithdrawalForecast.Result result = Evaluate(
            Candidate(10, 7, 100),
            Candidate(11, 8, 31));

        Assert.Equal(11, result.SelectedSquadId);
    }

    [Fact]
    public void BattleValueBreaksEqualSurvivorBenefit()
    {
        WithdrawalForecast.Result result = Evaluate(
            Candidate(10, 7, 45),
            Candidate(11, 7, 50));

        Assert.Equal(11, result.SelectedSquadId);
    }

    [Fact]
    public void EngagedThenDistanceThenDelayThenId_DeterministicallyBreakTies()
    {
        WithdrawalForecast.Candidate farEngaged = Candidate(20, 7, 45, enemyDistance: 9) with { IsEngaged = true };
        WithdrawalForecast.Candidate close = Candidate(10, 7, 45, enemyDistance: 1);

        Assert.Equal(20, Evaluate(close, farEngaged).SelectedSquadId);

        WithdrawalForecast.Candidate shortest = Candidate(30, 7, 45, enemyDistance: 1, delay: 1);
        WithdrawalForecast.Candidate moreDelay = Candidate(31, 7, 45, enemyDistance: 1, delay: 3);
        Assert.Equal(31, Evaluate(shortest, moreDelay).SelectedSquadId);

        Assert.Equal(40, Evaluate(Candidate(41, 7, 45), Candidate(40, 7, 45)).SelectedSquadId);
    }

    [Fact]
    public void RepeatSacrifice_MustSaveDistinctRemainingBody()
    {
        WithdrawalForecast.Candidate candidate = Candidate(10, 8, 50) with { SavesDistinctRemainingBody = false };
        var input = new WithdrawalForecast.Input(10, false, Baseline, new[] { candidate }, IsRepeatSacrifice: true);

        WithdrawalForecast.Result result = WithdrawalForecast.Evaluate(input);

        Assert.Null(result.SelectedSquadId);
        Assert.Equal("repeat_saves_no_distinct_body", result.CandidateEvaluations[0].Reason);
    }

    [Fact]
    public void Trace_IsSortedBySquadId_AndIncludesRejectedReasons()
    {
        WithdrawalForecast.Candidate rejected = Candidate(20, 9, 60) with { OtherSquadsRemaining = 0 };
        WithdrawalForecast.Result result = Evaluate(rejected, Candidate(10, 7, 45));

        Assert.Contains("selected_squad=10 survivor_improvement=2", result.Trace.Render());
        Assert.StartsWith("REARGUARD_EVAL_CANDIDATE squad=10", result.CandidateTraces[0].Render());
        Assert.Contains("REARGUARD_EVAL_CANDIDATE squad=20", result.CandidateTraces[1].Render());
        Assert.Contains("reason=no_other_squad_to_save", result.CandidateTraces[1].Render());
    }
}
