using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// Phase 3 of Design/Active/MoraleAndRout.md: the morale check itself (§5), its outcomes
/// (§6), the synapse skip (§4.2), and the turn-start-snapshot propagation rule (§5.1).
/// These are pure evaluator tests with an injected deterministic RNG — no static RNG state
/// is touched, so no shared-state collection is needed. Resolver wiring is covered by
/// <see cref="BattleMoraleResolverTests"/>.
/// </summary>
public class BattleMoraleEvaluatorTests
{
    // Reference stress case used throughout (mirrors the hand calculation in
    // MoraleConstants): 25% casualties this turn, 25% cumulative, force losing
    // (forceDisadvantage 0.41) -> shock 0.75, context 1.41, stress ~1.0575.
    private const float LosingForceDisadvantage = 0.41f;

    /// <summary>Zero-noise RNG: every z-draw is 0, so fails are purely stress vs resolve.</summary>
    private sealed class ZeroNoiseRNG : IRNG
    {
        public double GetDoubleInRange(double lowerBound, double upperBound) => lowerBound;
        public double GetLinearDouble() => 0.0;
        public int GetIntBelowMax(int min, int max) => min;
        public double NextRandomZValue() => 0.0;
    }

    private static BattleMoraleEvaluator.MoraleCheckInput CreateInput(
        IReadOnlyList<BattleMoraleEvaluator.SoldierMoraleInput> soldiers,
        float casualtyThisTurn = 0f,
        float cumulativeCasualty = 0f,
        bool leaderDead = false,
        float routingVisible = 0f,
        float localOutnumber = 0f,
        float forceDisadvantage = 0f)
    {
        return new BattleMoraleEvaluator.MoraleCheckInput(
            soldiers,
            casualtyThisTurn,
            cumulativeCasualty,
            leaderDead,
            routingVisible,
            localOutnumber,
            CommandAuraSupport: 0f,
            forceDisadvantage);
    }

    private static List<BattleMoraleEvaluator.SoldierMoraleInput> Soldiers(
        float ego, int count, int? leaderIndex = null)
    {
        return Enumerable.Range(0, count)
            .Select(i => new BattleMoraleEvaluator.SoldierMoraleInput(i + 1, ego, i == leaderIndex))
            .ToList();
    }

    // --- resolve curve (§5.2: raw Ego in, resolve out; §3.6: no AddAttributePoints math) ---

    [Fact]
    public void EgoToResolve_IsMonotoneInEgo()
    {
        float gaunt = BattleMoraleEvaluator.EgoToResolve(8f);
        float pdf = BattleMoraleEvaluator.EgoToResolve(10f);
        float marine = BattleMoraleEvaluator.EgoToResolve(14f);
        float warrior = BattleMoraleEvaluator.EgoToResolve(20f);

        Assert.True(gaunt < pdf);
        Assert.True(pdf < marine);
        Assert.True(marine < warrior);
    }

    // --- force disadvantage (§5.2: from BattleForceEvaluator's existing outputs) ---

    [Fact]
    public void ComputeForceDisadvantage_ZeroWhenEvenAndNoLosses()
    {
        Assert.Equal(0f, BattleMoraleEvaluator.ComputeForceDisadvantage(100, 100, 0, 0));
    }

    [Fact]
    public void ComputeForceDisadvantage_PositiveWhenOutmatchedAndBleeding()
    {
        // 100 BV vs 180 BV, two-round losses 40 vs 10 — the reference "losing" case.
        float value = BattleMoraleEvaluator.ComputeForceDisadvantage(100, 180, 40, 10);
        Assert.InRange(value, 0.40f, 0.42f);
    }

    // --- the check: outcomes by species and context (§5, §10 Phase 7 targets) ---

    [Fact]
    public void GauntSquadAtQuarterCasualtiesWhileLosing_Routs()
    {
        BattleMoraleEvaluator.MoraleCheckResult result = BattleMoraleEvaluator.Evaluate(
            CreateInput(
                Soldiers(ego: 8f, count: 8),
                casualtyThisTurn: 0.25f,
                cumulativeCasualty: 0.25f,
                forceDisadvantage: LosingForceDisadvantage),
            new ZeroNoiseRNG());

        // stress 0.75 * 1.41 = 1.0575 > resolve(8) = 0.962 — every gaunt fails.
        Assert.True(result.Stress > BattleMoraleEvaluator.EgoToResolve(8f));
        Assert.Equal(result.AbleSoldiers, result.Fails);
        Assert.Equal(MoraleState.Routing, result.Outcome);
    }

    [Fact]
    public void MarineSquadAtSameShock_StaysSteady()
    {
        BattleMoraleEvaluator.MoraleCheckResult result = BattleMoraleEvaluator.Evaluate(
            CreateInput(
                Soldiers(ego: 14f, count: 5),
                casualtyThisTurn: 0.25f,
                cumulativeCasualty: 0.25f,
                forceDisadvantage: LosingForceDisadvantage),
            new ZeroNoiseRNG());

        // stress 1.0575 << resolve(14) = 2.226 — marines shrug it off.
        Assert.True(result.Stress < BattleMoraleEvaluator.EgoToResolve(14f));
        Assert.Equal(0, result.Fails);
        Assert.Equal(MoraleState.Steady, result.Outcome);
    }

    [Fact]
    public void MarineSquadAtModerateCasualtiesLeaderDeadAndLosing_StaysSteady()
    {
        // Even stacking leader death on top of the losing reference case: stress
        // 1.25 * 1.41 = 1.7625 < resolve(14) = 2.226 (§2: marines almost never break).
        BattleMoraleEvaluator.MoraleCheckResult result = BattleMoraleEvaluator.Evaluate(
            CreateInput(
                Soldiers(ego: 14f, count: 5),
                casualtyThisTurn: 0.25f,
                cumulativeCasualty: 0.25f,
                leaderDead: true,
                forceDisadvantage: LosingForceDisadvantage),
            new ZeroNoiseRNG());

        Assert.Equal(MoraleState.Steady, result.Outcome);
    }

    [Fact]
    public void ContextMultiplier_IdenticalLocalShockDiffersWinningVersusLosing()
    {
        // §2 locked decision: global context MULTIPLIES local shock. Identical local
        // shock (25% turn + 25% cumulative casualties) — the only difference is whether
        // the force is winning or losing. Assert the asymmetry directly (§11).
        List<BattleMoraleEvaluator.SoldierMoraleInput> gaunts = Soldiers(ego: 8f, count: 8);

        BattleMoraleEvaluator.MoraleCheckResult winning = BattleMoraleEvaluator.Evaluate(
            CreateInput(
                gaunts,
                casualtyThisTurn: 0.25f,
                cumulativeCasualty: 0.25f,
                forceDisadvantage: 0f),
            new ZeroNoiseRNG());
        BattleMoraleEvaluator.MoraleCheckResult losing = BattleMoraleEvaluator.Evaluate(
            CreateInput(
                gaunts,
                casualtyThisTurn: 0.25f,
                cumulativeCasualty: 0.25f,
                forceDisadvantage: LosingForceDisadvantage),
            new ZeroNoiseRNG());

        Assert.Equal(winning.Shock, losing.Shock);
        Assert.True(losing.Stress > winning.Stress);
        Assert.Equal(MoraleState.Steady, winning.Outcome);
        Assert.Equal(MoraleState.Routing, losing.Outcome);
    }

    // --- aggregation (§5.3) ---

    [Fact]
    public void EgoVariance_ProducesPartialFailureShakenWithoutRout()
    {
        // 3 low-Ego soldiers fail, 7 high-Ego hold: failFraction 0.3 sits between the
        // shaken (0.25) and rout (0.5) thresholds — partial failure, no rout.
        List<BattleMoraleEvaluator.SoldierMoraleInput> mixed = Soldiers(ego: 8f, count: 3)
            .Concat(Soldiers(ego: 14f, count: 7).Select(s =>
                new BattleMoraleEvaluator.SoldierMoraleInput(s.SoldierId + 100, s.Ego, false)))
            .ToList();

        BattleMoraleEvaluator.MoraleCheckResult result = BattleMoraleEvaluator.Evaluate(
            CreateInput(mixed, casualtyThisTurn: 0.5f),
            new ZeroNoiseRNG());

        Assert.Equal(3, result.Fails);
        Assert.Equal(MoraleState.Shaken, result.Outcome);
    }

    [Fact]
    public void LivingSteadyLeader_RaisesBothThresholds()
    {
        // Same 0.3 fail fraction; a living high-Ego leader who held raises the shaken
        // threshold past it, so the squad stays Steady instead of Shaken.
        List<BattleMoraleEvaluator.SoldierMoraleInput> withLeader = Soldiers(ego: 8f, count: 3)
            .Concat(Soldiers(ego: 14f, count: 7, leaderIndex: 0).Select(s =>
                new BattleMoraleEvaluator.SoldierMoraleInput(s.SoldierId + 100, s.Ego, s.IsLeader)))
            .ToList();

        BattleMoraleEvaluator.MoraleCheckResult noLeader = BattleMoraleEvaluator.Evaluate(
            CreateInput(Soldiers(ego: 8f, count: 3)
                .Concat(Soldiers(ego: 14f, count: 7).Select(s =>
                    new BattleMoraleEvaluator.SoldierMoraleInput(s.SoldierId + 100, s.Ego, false)))
                .ToList(),
                casualtyThisTurn: 0.5f),
            new ZeroNoiseRNG());
        BattleMoraleEvaluator.MoraleCheckResult leaderHolds = BattleMoraleEvaluator.Evaluate(
            CreateInput(withLeader, casualtyThisTurn: 0.5f),
            new ZeroNoiseRNG());

        Assert.False(noLeader.LeaderHeld);
        Assert.True(leaderHolds.LeaderHeld);
        Assert.True(leaderHolds.RoutThreshold > noLeader.RoutThreshold);
        Assert.True(leaderHolds.ShakenThreshold > noLeader.ShakenThreshold);
        Assert.Equal(MoraleState.Shaken, noLeader.Outcome);
        Assert.Equal(MoraleState.Steady, leaderHolds.Outcome);
    }

    [Fact]
    public void ShakenIsNotSticky_CalmTurnReturnsSteady()
    {
        // §6: the check is stateless — Shaken carries no accumulator, so a squad that was
        // Shaken last turn and takes no stress this turn is simply Steady again.
        List<BattleMoraleEvaluator.SoldierMoraleInput> gaunts = Soldiers(ego: 8f, count: 8);
        BattleMoraleEvaluator.MoraleCheckResult stressedTurn = BattleMoraleEvaluator.Evaluate(
            CreateInput(gaunts, casualtyThisTurn: 0.2f, forceDisadvantage: 0.3f),
            new ZeroNoiseRNG());
        BattleMoraleEvaluator.MoraleCheckResult calmTurn = BattleMoraleEvaluator.Evaluate(
            CreateInput(gaunts),
            new ZeroNoiseRNG());

        Assert.NotEqual(MoraleState.Routing, stressedTurn.Outcome);
        Assert.Equal(MoraleState.Steady, calmTurn.Outcome);
    }

    // --- skip logic (§4.2 synapse skip; §6 rout stickiness) ---

    private static BattleSoldier Place(BattleGridManager grid, BattleSquad squad, int index,
                                       bool side, int x, int y)
    {
        BattleSoldier soldier = squad.AbleSoldiers[index];
        ValueTuple<int, int> cell = new(x, y);
        grid.PlaceSoldier(soldier, side, new List<ValueTuple<int, int>> { cell });
        soldier.TopLeft = cell;
        return soldier;
    }

    private static BattleSquad CreateSquad(string name, SoldierTemplate template, params int[] ids)
    {
        Soldier[] soldiers = new Soldier[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            Soldier soldier = TestModelFactory.CreateSoldier(template: template, name: $"{name}-{ids[i]}");
            soldier.Id = ids[i];
            soldiers[i] = soldier;
        }
        Squad squad = TestModelFactory.CreateSquad(name, soldiers);
        return new BattleSquad(false, squad);
    }

    [Fact]
    public void CoveredSquad_SkipsTheCheckEntirely()
    {
        BattleGridManager grid = new();
        BattleSquad provider = CreateSquad("Provider", TestModelFactory.SynapseProviderTemplate, 1);
        BattleSquad gaunts = CreateSquad("Gaunts", TestModelFactory.MarineTemplate, 2, 3);
        Place(grid, provider, 0, side: true, x: 0, y: 0);
        Place(grid, gaunts, 0, side: true, x: 5, y: 0);
        Place(grid, gaunts, 1, side: true, x: 6, y: 0);

        Assert.Equal(
            BattleMoraleEvaluator.MoraleSkipReason.SynapseCovered,
            BattleMoraleEvaluator.ShouldCheckMorale(gaunts, new[] { provider, gaunts }, grid));
    }

    [Fact]
    public void SynapseProviderSquad_NeverChecks()
    {
        BattleGridManager grid = new();
        BattleSquad provider = CreateSquad("Provider", TestModelFactory.SynapseProviderTemplate, 1);
        Place(grid, provider, 0, side: true, x: 0, y: 0);

        Assert.Equal(
            BattleMoraleEvaluator.MoraleSkipReason.ProvidesSynapse,
            BattleMoraleEvaluator.ShouldCheckMorale(provider, new[] { provider }, grid));
    }

    [Fact]
    public void UncoveredSquad_Checks()
    {
        BattleGridManager grid = new();
        BattleSquad provider = CreateSquad("Provider", TestModelFactory.SynapseProviderTemplate, 1);
        BattleSquad gaunts = CreateSquad("Gaunts", TestModelFactory.MarineTemplate, 2);
        Place(grid, provider, 0, side: true, x: 0, y: 0);
        // Provider radius is 10; 50 away is well outside.
        Place(grid, gaunts, 0, side: true, x: 50, y: 0);

        Assert.Equal(
            BattleMoraleEvaluator.MoraleSkipReason.Check,
            BattleMoraleEvaluator.ShouldCheckMorale(gaunts, new[] { provider, gaunts }, grid));
    }

    [Fact]
    public void ProviderKilledMidBattle_DependentsCheckTheSameTurn()
    {
        // §5.1: severance bites the same turn the provider dies — no grace period.
        BattleGridManager grid = new();
        BattleSquad provider = CreateSquad("Provider", TestModelFactory.SynapseProviderTemplate, 1);
        BattleSquad gaunts = CreateSquad("Gaunts", TestModelFactory.MarineTemplate, 2);
        BattleSoldier providerSoldier = Place(grid, provider, 0, side: true, x: 0, y: 0);
        Place(grid, gaunts, 0, side: true, x: 5, y: 0);

        Assert.Equal(
            BattleMoraleEvaluator.MoraleSkipReason.SynapseCovered,
            BattleMoraleEvaluator.ShouldCheckMorale(gaunts, new[] { provider, gaunts }, grid));

        provider.RemoveSoldier(providerSoldier);

        Assert.Equal(
            BattleMoraleEvaluator.MoraleSkipReason.Check,
            BattleMoraleEvaluator.ShouldCheckMorale(gaunts, new[] { provider, gaunts }, grid));
    }

    [Fact]
    public void RoutingSquad_IsStickyAndNeverReChecks()
    {
        BattleGridManager grid = new();
        BattleSquad gaunts = CreateSquad("Gaunts", TestModelFactory.MarineTemplate, 1);
        Place(grid, gaunts, 0, side: true, x: 0, y: 0);
        gaunts.WithdrawalRole = WithdrawalRole.Routing;

        Assert.Equal(
            BattleMoraleEvaluator.MoraleSkipReason.AlreadyRouting,
            BattleMoraleEvaluator.ShouldCheckMorale(gaunts, new[] { gaunts }, grid));
    }

    // --- propagation (§5.1/§5.2): local, headcount-weighted, turn-start snapshot ---

    [Fact]
    public void RoutingVisibleFraction_ReadsTurnStartSnapshotNotThisTurnsOutcomes()
    {
        BattleGridManager grid = new();
        BattleSquad observer = CreateSquad("Observer", TestModelFactory.MarineTemplate, 1);
        BattleSquad neighbour = CreateSquad("Neighbour", TestModelFactory.MarineTemplate, 2, 3, 4);
        Place(grid, observer, 0, side: true, x: 0, y: 0);
        Place(grid, neighbour, 0, side: true, x: 5, y: 0);
        Place(grid, neighbour, 1, side: true, x: 6, y: 0);
        Place(grid, neighbour, 2, side: true, x: 7, y: 0);

        // The neighbour routed THIS turn (role already flipped), but the turn-start
        // snapshot is empty: it contributes nothing until next turn (§5.1). This is what
        // makes squad iteration order irrelevant and prevents same-turn cascades.
        neighbour.WithdrawalRole = WithdrawalRole.Routing;
        float sameTurn = BattleMoraleEvaluator.ComputeRoutingVisibleFriendlyFraction(
            observer, new[] { observer, neighbour }, new HashSet<int>(), grid,
            MoraleConstants.VisualRange);
        Assert.Equal(0f, sameTurn);

        // Next turn the snapshot contains it: all 3 visible friendly soldiers are routing.
        float nextTurn = BattleMoraleEvaluator.ComputeRoutingVisibleFriendlyFraction(
            observer, new[] { observer, neighbour }, new HashSet<int> { neighbour.Id }, grid,
            MoraleConstants.VisualRange);
        Assert.Equal(1f, nextTurn);
    }

    [Fact]
    public void RoutingVisibleFraction_IsHeadcountWeighted()
    {
        BattleGridManager grid = new();
        BattleSquad observer = CreateSquad("Observer", TestModelFactory.MarineTemplate, 1);
        BattleSquad bolters = CreateSquad("Bolters", TestModelFactory.MarineTemplate, 2, 3, 4);
        BattleSquad steady = CreateSquad("Steady", TestModelFactory.MarineTemplate, 5);
        Place(grid, observer, 0, side: true, x: 0, y: 0);
        Place(grid, bolters, 0, side: true, x: 5, y: 0);
        Place(grid, bolters, 1, side: true, x: 6, y: 0);
        Place(grid, bolters, 2, side: true, x: 7, y: 0);
        Place(grid, steady, 0, side: true, x: 8, y: 0);

        // 3 routing bodies out of 4 visible friendlies — a bolting swarm is scarier
        // than a fleeing fire team (§5.2).
        float fraction = BattleMoraleEvaluator.ComputeRoutingVisibleFriendlyFraction(
            observer, new[] { observer, bolters, steady }, new HashSet<int> { bolters.Id },
            grid, MoraleConstants.VisualRange);
        Assert.Equal(0.75f, fraction);
    }

    [Fact]
    public void RoutingVisibleFraction_ZeroWhenRouterBeyondVisualRange()
    {
        BattleGridManager grid = new();
        BattleSquad observer = CreateSquad("Observer", TestModelFactory.MarineTemplate, 1);
        BattleSquad farRouter = CreateSquad("FarRouter", TestModelFactory.MarineTemplate, 2);
        Place(grid, observer, 0, side: true, x: 0, y: 0);
        Place(grid, farRouter, 0, side: true, x: (int)MoraleConstants.VisualRange + 40, y: 0);

        float fraction = BattleMoraleEvaluator.ComputeRoutingVisibleFriendlyFraction(
            observer, new[] { observer, farRouter }, new HashSet<int> { farRouter.Id },
            grid, MoraleConstants.VisualRange);
        Assert.Equal(0f, fraction);
    }

    [Fact]
    public void LocalOutnumberRatio_MeasuresExcessBeyondParityAndIsCapped()
    {
        BattleGridManager grid = new();
        BattleSquad observer = CreateSquad("Observer", TestModelFactory.MarineTemplate, 1);
        BattleSquad enemies = CreateSquad("Enemies", TestModelFactory.MarineTemplate, 2, 3, 4, 5, 6, 7);
        Place(grid, observer, 0, side: true, x: 0, y: 0);
        for (int i = 0; i < 6; i++)
        {
            Place(grid, enemies, i, side: false, x: 5 + i, y: 0);
        }

        // 6 enemies vs 1 friendly = ratio 5 beyond parity, capped at LocalOutnumberCap.
        float ratio = BattleMoraleEvaluator.ComputeLocalOutnumberRatio(
            observer, new[] { observer }, new[] { enemies }, grid, MoraleConstants.VisualRange);
        Assert.Equal(MoraleConstants.LocalOutnumberCap, ratio);
    }
}
