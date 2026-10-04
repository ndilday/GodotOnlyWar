using System.Collections.Generic;
using OnlyWar.Battles;
using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// WithdrawalForecast, reworked for OnlyWar_TDD.md §6.6:
/// the §7 rear-guard "will not break while holding" predicate, the §8.1 surviving-share metric,
/// and the §8.2 one-level command-collapse projection. No RNG is consumed anywhere here —
/// ProjectOpenGround, Evaluate, and the closed-form rout verdict are all deterministic — so
/// no shared-state collection is required.
///
/// Shares are fractions of the withdrawing side's OWN starting strength (squad weight, its share
/// of the side's starting battle value, times the fraction of the squad still able). No test here
/// carries an absolute battle value, and none compares one side's value with the other's.
/// </summary>
public class WithdrawalForecastTests
{
    private static readonly WithdrawalForecast.Projection Baseline = new(5, 0.30f, 2, 0);

    private static WithdrawalForecast.Candidate Candidate(
        int id, int survivors, float share, float enemyDistance = 4, float delay = 2) =>
        new(id, true, false, true, false, 2, enemyDistance, delay,
            new WithdrawalForecast.Projection(survivors, share, 1, 1));

    private static WithdrawalForecast.Result Evaluate(params WithdrawalForecast.Candidate[] candidates) =>
        WithdrawalForecast.Evaluate(new WithdrawalForecast.Input(9, true, Baseline, candidates));

    [Fact]
    public void OpenGroundProjection_IsDeterministicAndRearGuardDelayCanSaveMainBody()
    {
        var squads = new[]
        {
            new WithdrawalForecast.SquadGeometry(10, 2, 0.3f, 8, 6),
            new WithdrawalForecast.SquadGeometry(11, 4, 0.7f, 12, 6)
        };

        WithdrawalForecast.Projection ordinary = WithdrawalForecast.ProjectOpenGround(
            squads, fastestPursuerSpeed: 9, oneTurnAttackReach: 3);
        WithdrawalForecast.Projection guarded = WithdrawalForecast.ProjectOpenGround(
            squads, fastestPursuerSpeed: 9, oneTurnAttackReach: 3,
            rearGuardSquadId: 10, rearGuardDelayTurns: 2);

        Assert.Equal(0, ordinary.ExpectedAbleSurvivors);
        Assert.Equal(4, guarded.ExpectedAbleSurvivors);
        Assert.Equal(0.7f, guarded.ExpectedSurvivingShare);
        Assert.Equal(1, guarded.ExpectedSquadsIntercepted);
        Assert.Equal(1, guarded.ExpectedMainBodyMaskedDepartures);
    }

    // --- §8.1 surviving-share metric ---

    [Fact]
    public void NoSacrifice_ForNoSurvivingShareBenefit()
    {
        // Same surviving share as ordinary withdrawal (0.30) — nothing is gained, so no sacrifice.
        WithdrawalForecast.Result result = Evaluate(Candidate(10, 9, 0.30f));

        Assert.Null(result.SelectedSquadId);
        Assert.Equal("insufficient_share_improvement", result.CandidateEvaluations[0].Reason);
    }

    [Fact]
    public void ImprovementBelowTheShareGate_IsRejected_AndAtTheGateIsAccepted()
    {
        float gate = WithdrawalForecast.MinimumAdditionalShare;

        Assert.Equal(
            "insufficient_share_improvement",
            Evaluate(Candidate(10, 7, 0.30f + (gate / 2))).CandidateEvaluations[0].Reason);
        Assert.True(Evaluate(Candidate(10, 7, 0.30f + (gate * 2))).CandidateEvaluations[0].Eligible);
    }

    [Fact]
    public void GreatestSurvivingShareBenefit_WinsBeforeSurvivors()
    {
        // Candidate 10 saves fewer soldiers but far more of the force's value (a vehicle-like
        // heavily weighted squad); share-primary must prefer it over the higher-headcount one.
        WithdrawalForecast.Result result = Evaluate(
            Candidate(10, 7, 0.60f),
            Candidate(11, 8, 0.35f));

        Assert.Equal(10, result.SelectedSquadId);
    }

    [Fact]
    public void SurvivorsBreakEqualSurvivingShare()
    {
        WithdrawalForecast.Result result = Evaluate(
            Candidate(10, 7, 0.50f),
            Candidate(11, 8, 0.50f));

        Assert.Equal(11, result.SelectedSquadId);
    }

    [Fact]
    public void LowHeadcountHighWeightAsset_IsNotFedToPursuer()
    {
        // A Rhino-like squad: 1 able soldier but a large share of the force's starting value.
        // Under the old lives-primary rule its single survivor barely improved the count and it
        // was cheerfully sacrificed; share-primary recognises the 0.40 it saves and prefers the
        // rear guard that keeps it alive.
        WithdrawalForecast.Candidate vehicleSaver = Candidate(10, 6, 0.70f);
        WithdrawalForecast.Candidate infantrySaver = Candidate(11, 9, 0.40f);

        Assert.Equal(10, Evaluate(vehicleSaver, infantrySaver).SelectedSquadId);
    }

    [Fact]
    public void StructuralGatesRejectRegardlessOfSurvivingShare()
    {
        // A huge-share candidate that is not exposed is still rejected — the structural gates run
        // before any metric, so share-primary does not reintroduce the sacrifice-for-firepower trap.
        WithdrawalForecast.Candidate notExposed =
            Candidate(10, 30, 0.95f) with { IsClosestSquad = false, IsEngaged = false, WithinNextTurnInterceptReach = false };
        Assert.Equal("not_exposed", Evaluate(notExposed).CandidateEvaluations[0].Reason);

        WithdrawalForecast.Candidate safe = Candidate(11, 30, 0.95f) with { IsSafelyWithdrawing = true };
        Assert.Equal("safely_withdrawing", Evaluate(safe).CandidateEvaluations[0].Reason);
    }

    [Fact]
    public void SafelyWithdrawingSquad_IsNeverRecalled()
    {
        WithdrawalForecast.Candidate safe = Candidate(10, 9, 0.60f) with { IsSafelyWithdrawing = true };

        WithdrawalForecast.Result result = Evaluate(safe);

        Assert.Null(result.SelectedSquadId);
        Assert.Equal("safely_withdrawing", result.CandidateEvaluations[0].Reason);
    }

    [Fact]
    public void EngagedThenDistanceThenDelayThenId_DeterministicallyBreakTies()
    {
        WithdrawalForecast.Candidate farEngaged = Candidate(20, 7, 0.45f, enemyDistance: 9) with { IsEngaged = true };
        WithdrawalForecast.Candidate close = Candidate(10, 7, 0.45f, enemyDistance: 1);

        Assert.Equal(20, Evaluate(close, farEngaged).SelectedSquadId);

        WithdrawalForecast.Candidate shortest = Candidate(30, 7, 0.45f, enemyDistance: 1, delay: 1);
        WithdrawalForecast.Candidate moreDelay = Candidate(31, 7, 0.45f, enemyDistance: 1, delay: 3);
        Assert.Equal(31, Evaluate(shortest, moreDelay).SelectedSquadId);

        Assert.Equal(40, Evaluate(Candidate(41, 7, 0.45f), Candidate(40, 7, 0.45f)).SelectedSquadId);
    }

    [Fact]
    public void RepeatSacrifice_MustSaveDistinctRemainingBody()
    {
        WithdrawalForecast.Candidate candidate = Candidate(10, 8, 0.50f) with { SavesDistinctRemainingBody = false };
        var input = new WithdrawalForecast.Input(10, false, Baseline, new[] { candidate }, IsRepeatSacrifice: true);

        WithdrawalForecast.Result result = WithdrawalForecast.Evaluate(input);

        Assert.Null(result.SelectedSquadId);
        Assert.Equal("repeat_saves_no_distinct_body", result.CandidateEvaluations[0].Reason);
    }

    // --- §7 rear-guard predicate table ---

    [Theory]
    [InlineData(14f, false, true)]  // Space Marines: Ego 14 clears the threshold
    [InlineData(20f, false, true)]  // Tyranid Warriors: Ego 20 — the natural Tyranid rear guard
    [InlineData(10f, false, false)] // PDF Trooper: Ego 10, no coverage
    [InlineData(8f, false, false)]  // Orks / gaunts: Ego 8, no coverage
    public void WillNotBreakWhileHolding_FollowsEgoThreshold(float ego, bool sustainedCoverage, bool eligible)
    {
        WithdrawalForecast.Candidate candidate = Candidate(10, 8, 0.45f)
            with { SquadEgo = ego, WillRemainSynapseCoveredWhileHolding = sustainedCoverage };

        WithdrawalForecast.CandidateEvaluation evaluation = Evaluate(candidate).CandidateEvaluations[0];

        Assert.Equal(eligible, evaluation.Eligible);
        if (!eligible)
        {
            Assert.Equal("will_break_if_held", evaluation.Reason);
        }
    }

    [Fact]
    public void GauntsWhoseProviderLeaves_AreRejectedWillBreak_WhileSustainedCoveragePasses()
    {
        // Ego-8 gaunts whose Warriors withdraw with the main body: coverage lapses mid-hold.
        WithdrawalForecast.Candidate coverageLapses =
            Candidate(10, 8, 0.45f) with { SquadEgo = 8f, WillRemainSynapseCoveredWhileHolding = false };
        Assert.Equal("will_break_if_held", Evaluate(coverageLapses).CandidateEvaluations[0].Reason);

        // Same gaunts, but a provider is projected to hold within radius the whole time
        // (the composite-rear-guard case, §12) — constructed artificially, as the live planner
        // rarely produces it.
        WithdrawalForecast.Candidate sustained =
            Candidate(11, 8, 0.45f) with { SquadEgo = 8f, WillRemainSynapseCoveredWhileHolding = true };
        Assert.True(Evaluate(sustained).CandidateEvaluations[0].Eligible);
    }

    [Fact]
    public void ShakenSquad_IsIneligibleRegardlessOfEgo()
    {
        // Ego high enough to pass WillNotBreakWhileHolding, but Shaken squads cannot hold (§6/§7).
        WithdrawalForecast.Candidate shaken = Candidate(10, 8, 0.45f) with { SquadEgo = 20f, IsShaken = true };

        WithdrawalForecast.CandidateEvaluation evaluation = Evaluate(shaken).CandidateEvaluations[0];

        Assert.False(evaluation.Eligible);
        Assert.Equal("shaken", evaluation.Reason);
    }

    // --- §8.2 command-collapse projection: the KEYSTONE regression (§3.3 trap, §11) ---

    // Warriors (3 soldiers) hold a 20% share of the force; the gaunt swarm (20) holds 80%.
    private const float WarriorShare = 0.2f;
    private const float GauntShare = 0.8f;

    private static WithdrawalForecast.SquadGeometry Warrior(int id) =>
        new(id, 3, WarriorShare, 4, 8, ProvidesSynapse: true);

    private static WithdrawalForecast.SquadGeometry Gaunts(int id, bool routsIfSevered) =>
        new(id, 20, GauntShare, 4, 8, DependsOnSynapse: true, RoutsIfSevered: routsIfSevered);

    [Fact]
    public void Keystone_GauntsRejected_AndWarriorsHoldBranchPricesTheCoverageLapse()
    {
        // (a) The gaunts are not a viable rear guard: their coverage lapses when the Warriors run.
        WithdrawalForecast.Candidate gauntCandidate =
            Candidate(2, 20, GauntShare) with { SquadEgo = 8f, WillRemainSynapseCoveredWhileHolding = false };
        Assert.Equal("will_break_if_held", Evaluate(gauntCandidate).CandidateEvaluations[0].Reason);

        // (b) The Warriors-hold projection must be charged for the escaping swarm's coverage lapse:
        // toggling command-collapse modelling off changes the projection.
        var geometry = new[] { Warrior(1), Gaunts(2, routsIfSevered: true) };
        WithdrawalForecast.Projection modelled = WithdrawalForecast.ProjectOpenGround(
            geometry, fastestPursuerSpeed: 10, oneTurnAttackReach: 3,
            rearGuardSquadId: 1, rearGuardDelayTurns: 1, modelCommandCollapse: true);
        WithdrawalForecast.Projection blind = WithdrawalForecast.ProjectOpenGround(
            geometry, fastestPursuerSpeed: 10, oneTurnAttackReach: 3,
            rearGuardSquadId: 1, rearGuardDelayTurns: 1, modelCommandCollapse: false);

        // Blind to command collapse, the swarm walks away behind the Warriors; modelling it, the
        // severed swarm routs and is run down at the higher interception rate.
        Assert.Equal(20, blind.ExpectedAbleSurvivors);
        Assert.Equal(GauntShare, blind.ExpectedSurvivingShare);
        Assert.Equal(0, modelled.ExpectedAbleSurvivors);
        Assert.Equal(0f, modelled.ExpectedSurvivingShare);
        Assert.NotEqual(blind.ExpectedAbleSurvivors, modelled.ExpectedAbleSurvivors);
    }

    [Fact]
    public void Keystone_DecisionFlipsWhenCommandCollapseIsModelled()
    {
        // A Warrior squad (the only viable rear guard) + a gaunt swarm. Whether holding the
        // Warriors is worth it depends entirely on whether the swarm survives the hold.
        var geometry = new[] { Warrior(1), Gaunts(2, routsIfSevered: true) };

        int? Decide(bool modelCommandCollapse)
        {
            WithdrawalForecast.Projection baseline = WithdrawalForecast.ProjectOpenGround(
                geometry, 10, 3, modelCommandCollapse: modelCommandCollapse);
            WithdrawalForecast.Projection warriorsHold = WithdrawalForecast.ProjectOpenGround(
                geometry, 10, 3, rearGuardSquadId: 1, rearGuardDelayTurns: 1,
                modelCommandCollapse: modelCommandCollapse);
            // Warriors are the only eligible candidate; the Ego-8 gaunts are rejected will_break.
            WithdrawalForecast.Candidate warriors = new(
                1, true, false, true, false, 1, 4, 5, warriorsHold, SquadEgo: 20f);
            WithdrawalForecast.Candidate gaunts = new(
                2, true, false, true, false, 1, 4, 5,
                WithdrawalForecast.ProjectOpenGround(geometry, 10, 3, rearGuardSquadId: 2,
                    rearGuardDelayTurns: 1, modelCommandCollapse: modelCommandCollapse),
                SquadEgo: 8f);
            return WithdrawalForecast.Evaluate(
                new WithdrawalForecast.Input(9, true, baseline, new[] { warriors, gaunts })).SelectedSquadId;
        }

        // Blind to collapse: holding the Warriors lets the swarm's 80% share escape -> hold.
        Assert.Equal(1, Decide(modelCommandCollapse: false));
        // Modelling collapse: the swarm routs whether the Warriors hold or not, so the hold buys
        // nothing -> no rear guard. The decision flips on the command-collapse term.
        Assert.Null(Decide(modelCommandCollapse: true));
    }

    [Fact]
    public void CommandCollapse_IsCappedAtOneLevel_AndDoesNotChaseSecondOrderCascades()
    {
        // Provider held as rear guard severs BOTH dependents. Dependent 2 routs (its closed-form
        // verdict); dependent 3 does NOT (RoutsIfSevered=false). A second-order cascade model
        // would let squad 2's rout raise squad 3's stress and drag it into routing as well; the
        // one-level cap forbids that, so squad 3 keeps its own verdict and escapes behind the delay.
        var geometry = new[]
        {
            new WithdrawalForecast.SquadGeometry(1, 3, 0.15f, 4, 8, ProvidesSynapse: true),
            new WithdrawalForecast.SquadGeometry(2, 20, 0.45f, 4, 8, DependsOnSynapse: true, RoutsIfSevered: true),
            new WithdrawalForecast.SquadGeometry(3, 15, 0.40f, 4, 8, DependsOnSynapse: true, RoutsIfSevered: false)
        };

        WithdrawalForecast.Projection projection = WithdrawalForecast.ProjectOpenGround(
            geometry, fastestPursuerSpeed: 10, oneTurnAttackReach: 3,
            rearGuardSquadId: 1, rearGuardDelayTurns: 1, modelCommandCollapse: true);

        // Squad 3 survives; only the rear guard (1) and the routed dependent (2) are intercepted.
        Assert.Equal(15, projection.ExpectedAbleSurvivors);
        Assert.Equal(0.40f, projection.ExpectedSurvivingShare);
        Assert.Equal(2, projection.ExpectedSquadsIntercepted);
    }

    [Fact]
    public void Trace_IsSortedBySquadId_AndIncludesRejectedReasons()
    {
        WithdrawalForecast.Candidate rejected = Candidate(20, 9, 0.60f) with { OtherSquadsRemaining = 0 };
        WithdrawalForecast.Result result = Evaluate(rejected, Candidate(10, 7, 0.45f));

        Assert.Contains("selected_squad=10", result.Trace.Render());
        Assert.StartsWith("REARGUARD_EVAL_CANDIDATE squad=10", result.CandidateTraces[0].Render());
        Assert.Contains("REARGUARD_EVAL_CANDIDATE squad=20", result.CandidateTraces[1].Render());
        Assert.Contains("reason=no_other_squad_to_save", result.CandidateTraces[1].Render());
    }
}
