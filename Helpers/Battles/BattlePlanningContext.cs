using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

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
    /// All three maps are <see cref="ConcurrentDictionary{TKey, TValue}"/> because worker planners
    /// read and populate them concurrently during the parallel ranged phase. The factories are pure,
    /// so the benign double-compute a concurrent miss can trigger yields identical values.
    /// </summary>
    // Public so the public BattleSquadPlanner constructor can accept it; its members stay internal
    // so the encapsulated RangedTargetEvaluation type need not be widened.
    public sealed class BattlePlanningContext
    {
        // (AttackerSquadId, TargetSquadId) -> imminence. A squad-vs-squad quantity, identical for
        // every soldier in the attacking squad, so this is the largest cross-soldier reuse: it was
        // previously recomputed once per soldier in isolated per-worker caches.
        internal ConcurrentDictionary<(int AttackerSquadId, int TargetSquadId), float>
            SquadImminence { get; } = new();

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
            BattleSquadPlanner.RangedTargetEvaluation> RangedEvaluations { get; } = new();

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
