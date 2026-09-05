using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// Per-turn, shared-across-workers memo for the pure, frozen-state targeting computations that
    /// battle planning otherwise repeats for many soldiers. One instance is created per
    /// <see cref="BattleTurnResolver"/> planning pass and handed to every
    /// <see cref="BattleSquadPlanner"/> (the per-side planner and every worker sub-planner) so a
    /// result computed for one soldier or squad is reused by the rest instead of recomputed.
    ///
    /// Validity rests on one invariant: the grid layout — soldier positions, sides, injury and
    /// equipment state, loaded ammo — is frozen for the entire planning pass. Movement, reloads,
    /// and casualties are only applied afterwards during execution, so every cached value is a pure
    /// function of that frozen state plus its key, and none consumes battle RNG. Sharing them across
    /// threads and squads therefore cannot change a seeded outcome. The context is discarded and
    /// rebuilt each turn, so cross-turn staleness is impossible.
    ///
    /// Every map is a <see cref="ConcurrentDictionary{TKey, TValue}"/> because worker planners
    /// read and populate them concurrently during the parallel ranged phase. The factories are pure,
    /// so the benign double-compute a concurrent miss can trigger yields identical values.
    /// </summary>
    // Public so the public BattleSquadPlanner constructor can accept it; its members stay internal
    // so the encapsulated RangedTargetEvaluation type need not be widened.
    public sealed class BattlePlanningContext
    {
        // The engagement horizon is a turn-level memo, not an option-level calculation. The gate
        // lets standalone planners and the resolver's parallel planning pass initialize it once
        // from the same frozen state without making the result depend on which squad wins the
        // first worker race.
        internal object EngagementHorizonGate { get; } = new();
        private IReadOnlyDictionary<int, float> _expectedExchangeTurnsBySquad =
            new Dictionary<int, float>();
        private float _engagementBattleValueAtRisk;
        private float _engagementRemovalRate;
        private int _engagementHorizonInitialized;

        internal bool EngagementHorizonInitialized =>
            Volatile.Read(ref _engagementHorizonInitialized) != 0;

        internal float ExpectedExchangeTurnsFor(int squadId) =>
            Volatile.Read(ref _expectedExchangeTurnsBySquad)
                .GetValueOrDefault(squadId, EngagementHorizonModel.MaximumExchangeTurns);

        internal float EngagementBattleValueAtRisk =>
            Volatile.Read(ref _engagementBattleValueAtRisk);

        internal float EngagementRemovalRate =>
            Volatile.Read(ref _engagementRemovalRate);

        internal void SetEngagementHorizon(
            IReadOnlyDictionary<int, float> expectedExchangeTurnsBySquad,
            float battleValueAtRisk,
            float removalRate)
        {
            Volatile.Write(
                ref _expectedExchangeTurnsBySquad,
                expectedExchangeTurnsBySquad);
            Volatile.Write(ref _engagementBattleValueAtRisk, battleValueAtRisk);
            Volatile.Write(ref _engagementRemovalRate, removalRate);
            Volatile.Write(ref _engagementHorizonInitialized, 1);
        }

        // Full ranged-shot evaluation, keyed by everything that varies. This cache lived per-worker,
        // where each soldier was evaluated once against distinct targets, so it never amortized
        // (~0.7% hit rate). Shared across the pass, one soldier's evaluations serve later phases.
        internal ConcurrentDictionary<
            (int ShooterId,
             int TargetId,
             int WeaponId,
             int RangeBits,
             int ModifierBits,
             int TargetSpeedBits,
             int LoadedAmmo),
            RangedTargetEvaluation> RangedEvaluations { get; } = new();

        // (ShooterId, MovementDirection) -> nearest in-range enemy squads. Previously the single
        // largest self-time cost: it rescanned every enemy for the same soldier multiple times per
        // turn (SelectBestRangedTarget and SelectBestTemplateFiringLine each trigger a scan with the
        // same arguments). The nullable ValueTuple key is structurally compared, so distinct
        // directions get distinct entries and never alias.
        internal ConcurrentDictionary<
            (int ShooterId, ValueTuple<int, int>? Direction),
            IReadOnlyList<BattleSquad>> NearestInRangeSquads { get; } = new();

        // SquadId -> its firing geometry for the turn (Phase 3 fire distribution). Squad-level and
        // identical for every member, so computed once per squad.
        internal ConcurrentDictionary<int, SquadEngagementGeometry> SquadGeometry { get; } = new();

        // Exact, bounded current-turn response estimate for one enemy squad against one candidate
        // target squad.  Candidate options differ primarily by declared target speed; geometry is
        // frozen until shooting resolves.  This lets every option reuse live hit/range math without
        // rescanning the battlefield.
        internal ConcurrentDictionary<
            (int AttackerSquadId, int TargetSquadId, int TargetSpeedBits, int AttackerBulkBits),
            float> IncomingResponses { get; } = new();

        // Phase 4 removal-rate table (Design/Reference/BattleLogic.md): one
        // SquadPairRemovalRate per (shooter squad, target squad) pair, holding the closed-form
        // rescalable removal terms the lookahead will use to price its exchanges in the same
        // currency as immediate fire. Stored shooter-squad-major -- ShooterSquadId -> TargetSquadId
        // -> rate -- because the whole row is produced by one pass over the shooter squad's
        // soldiers (each soldier's best target lands in exactly one column), so building a row
        // lazily on the first cell request costs no more than building that cell alone. All cells
        // for a shooter squad are therefore present or absent together; an ABSENT column means no
        // soldier is aimed into that enemy squad, i.e. rate 0.
        //
        // PHASE 5 CONSUMES THIS. BattleSquadPlanner.EvaluateExchangeRate reads it for both halves
        // of every lookahead ply and for the depth-0 terminal, replacing the AggregateRemovalRate
        // capability proxy. Rows are therefore built for a squad's own table AND for its enemies'
        // (the incoming half), all memoized here for the turn.
        internal ConcurrentDictionary<
            int,
            IReadOnlyDictionary<int, SquadPairRemovalRate>> PairRemovalRates { get; } = new();

        // Contact melee removal rate, keyed by the attacker/target squad pair. Like the ranged
        // table this is pure for the frozen planning pass, and the exchange model can ask for the
        // same contact rate repeatedly across candidates and rollout plies.
        internal ConcurrentDictionary<(int AttackerSquadId, int TargetSquadId), float>
            MeleeContactRemovalRates { get; } = new();
    }

    /// <summary>
    /// A squad's engagement frame for one turn: the axis from the squad's centroid toward the enemy
    /// centroid, its perpendicular ("lateral") direction, and the spread strength (base coefficient
    /// scaled by the faction's fire discipline). The lateral direction lets each soldier prefer the
    /// enemy in its own firing lane, spreading a squad's fire across the frontage. A default
    /// (Valid = false) instance carries no preference — targeting falls back to pure value.
    /// </summary>
    internal readonly struct SquadEngagementGeometry
    {
        internal bool Valid { get; }
        internal float CentroidX { get; }
        internal float CentroidY { get; }
        internal float EnemyCentroidX { get; }
        internal float EnemyCentroidY { get; }
        internal float PerpX { get; }
        internal float PerpY { get; }
        internal float SpreadCoefficient { get; }

        internal SquadEngagementGeometry(
            float centroidX,
            float centroidY,
            float enemyCentroidX,
            float enemyCentroidY,
            float perpX,
            float perpY,
            float spreadCoefficient)
        {
            Valid = true;
            CentroidX = centroidX;
            CentroidY = centroidY;
            EnemyCentroidX = enemyCentroidX;
            EnemyCentroidY = enemyCentroidY;
            PerpX = perpX;
            PerpY = perpY;
            SpreadCoefficient = spreadCoefficient;
        }
    }
}
