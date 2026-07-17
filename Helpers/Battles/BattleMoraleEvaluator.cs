using System;
using System.Collections.Generic;
using System.Linq;

using OnlyWar.Models.Battles;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// The per-turn morale check (Design/Active/MoraleAndRout.md §5-6). Stateless and
    /// recomputed every turn from current state — no accumulator (§5.2). The scalar spatial
    /// inputs (propagation, local outnumbering, synapse coverage) are gathered by the caller
    /// (the resolver, which owns the grid and the turn-start snapshots); this class owns the
    /// stress/resolve math, the per-soldier roll against the battle RNG, and the squad-level
    /// aggregation. Keeping it pure makes the whole check deterministic and unit-testable.
    /// </summary>
    public static class BattleMoraleEvaluator
    {
        public enum MoraleSkipReason
        {
            /// <summary>The squad checks this turn.</summary>
            Check = 0,
            /// <summary>Not active, or no able soldiers.</summary>
            NoAbleSoldiers = 1,
            /// <summary>Already Routing — rout is sticky, so it is never re-rolled (§6).</summary>
            AlreadyRouting = 2,
            /// <summary>Provides synapse itself — never checks (§4.2).</summary>
            ProvidesSynapse = 3,
            /// <summary>Covered by a living friendly synapse provider — skips entirely (§4.2).</summary>
            SynapseCovered = 4
        }

        public sealed record SoldierMoraleInput(int SoldierId, float Ego, bool IsLeader);

        public sealed record MoraleCheckInput(
            IReadOnlyList<SoldierMoraleInput> Soldiers,
            float CasualtyFractionThisTurn,
            float CumulativeCasualtyFraction,
            bool LeaderDead,
            float RoutingVisibleFriendlyFraction,
            float LocalOutnumberRatio,
            float CommandAuraSupport,
            float ForceDisadvantage);

        public sealed record MoraleCheckResult(
            MoraleState Outcome,
            float Shock,
            float Context,
            float Stress,
            int Fails,
            int AbleSoldiers,
            float FailFraction,
            bool LeaderHeld,
            float RoutThreshold,
            float ShakenThreshold);

        /// <summary>
        /// Decides whether <paramref name="squad"/> undergoes the morale check this turn, and
        /// if not, why (§4.2 synapse skip, §6 rout stickiness). Reads post-round physical state
        /// (§5.1): the caller must pass only squads still Active after this round's casualties,
        /// so a provider wiped this round is already absent and its dependents lose coverage the
        /// same turn.
        /// </summary>
        public static MoraleSkipReason ShouldCheckMorale(
            BattleSquad squad,
            IEnumerable<BattleSquad> friendlySquads,
            BattleGridManager grid)
        {
            ArgumentNullException.ThrowIfNull(squad);
            ArgumentNullException.ThrowIfNull(friendlySquads);
            ArgumentNullException.ThrowIfNull(grid);

            if (squad.Status != BattleSquadStatus.Active || squad.AbleSoldiers.Count == 0)
            {
                return MoraleSkipReason.NoAbleSoldiers;
            }
            if (squad.WithdrawalRole == WithdrawalRole.Routing)
            {
                return MoraleSkipReason.AlreadyRouting;
            }
            if (squad.SquadProvidesSynapse)
            {
                return MoraleSkipReason.ProvidesSynapse;
            }
            if (SynapseCoverageEvaluator.IsSynapseCovered(squad, friendlySquads, grid))
            {
                return MoraleSkipReason.SynapseCovered;
            }
            return MoraleSkipReason.Check;
        }

        /// <summary>Morale-owned resolve curve over raw per-soldier Ego (§5.2, §3.6).</summary>
        public static float EgoToResolve(float ego)
        {
            return MoraleConstants.ResolveEgoCoefficient
                * MathF.Pow(Math.Max(0f, ego), MoraleConstants.ResolveEgoExponent);
        }

        /// <summary>
        /// The squad-level shock, global context multiplier, and resulting stress (§5.2). Shared
        /// by the live per-soldier roll (<see cref="Evaluate"/>) and the RNG-free forecast
        /// estimators (<see cref="EstimateExpectedFailFraction"/>, <see cref="EstimateOutcome"/>)
        /// so both read exactly the same stress from the same inputs.
        /// </summary>
        private static (float Shock, float Context, float Stress) ComputeStress(MoraleCheckInput input)
        {
            float shock =
                (MoraleConstants.ShockCasualtyThisTurnWeight
                    * Math.Clamp(input.CasualtyFractionThisTurn, 0f, 1f))
                + (MoraleConstants.ShockCumulativeCasualtyWeight
                    * Math.Clamp(input.CumulativeCasualtyFraction, 0f, 1f))
                + (input.LeaderDead ? MoraleConstants.ShockLeaderDeadWeight : 0f)
                + (MoraleConstants.ShockRoutingVisibleWeight
                    * Math.Clamp(input.RoutingVisibleFriendlyFraction, 0f, 1f))
                + (MoraleConstants.ShockLocalOutnumberWeight * Math.Max(0f, input.LocalOutnumberRatio))
                // §4.3 (Phase 6): SIGNED command-aura term. Positive support (a living HQ in
                // radius, from CommandAuraEvaluator) is subtracted from shock; a negative
                // value (every HQ the side fielded destroyed) adds stress — the aura's loss
                // supplies a positive term. Never a skip, and synapse-covered squads never
                // reach this function at all, so the two auras cannot interact here.
                - (MoraleConstants.CommandAuraSupportWeight * input.CommandAuraSupport);
            shock = Math.Max(0f, shock);

            float context = 1f
                + (MoraleConstants.ContextDisadvantageCoefficient
                    * Math.Clamp(input.ForceDisadvantage, 0f, 1f));
            return (shock, context, shock * context);
        }

        /// <summary>
        /// Standard-normal CDF, Phi(z) (Abramowitz &amp; Stegun 7.1.26). Used only by the
        /// deterministic forecast estimators to replace the per-soldier RNG draw with its closed
        /// form; never on the live roll path. The approximation error (~1e-7) is far below any
        /// morale threshold's sensitivity.
        /// </summary>
        public static float NormalCdf(float z)
        {
            return 0.5f * (1f + Erf(z / MathF.Sqrt(2f)));
        }

        private static float Erf(float x)
        {
            float sign = x < 0f ? -1f : 1f;
            x = MathF.Abs(x);
            float t = 1f / (1f + 0.3275911f * x);
            float y = 1f - (((((1.061405429f * t - 1.453152027f) * t) + 1.421413741f) * t
                - 0.284496736f) * t + 0.254829592f) * t * MathF.Exp(-x * x);
            return sign * y;
        }

        /// <summary>
        /// RNG-free estimate of the fraction of the squad that fails the check (§8, §11): the mean
        /// over able soldiers of the per-soldier fail probability Phi((stress - resolve)/sigma).
        /// This is the deterministic expectation of <see cref="Evaluate"/>'s per-soldier draw — no
        /// battle RNG is consumed, so it is safe inside the withdrawal projection.
        /// </summary>
        public static float EstimateExpectedFailFraction(MoraleCheckInput input)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(input.Soldiers);
            if (input.Soldiers.Count == 0)
            {
                return 0f;
            }
            float stress = ComputeStress(input).Stress;
            float sum = 0f;
            foreach (SoldierMoraleInput soldier in input.Soldiers)
            {
                sum += NormalCdf((stress - EgoToResolve(soldier.Ego)) / MoraleConstants.MoraleRollSigma);
            }
            return sum / input.Soldiers.Count;
        }

        /// <summary>
        /// Deterministic estimate of the squad-level morale outcome (§8.2): the closed-form
        /// expected fail fraction aggregated through the same Steady/Shaken/Routing thresholds
        /// <see cref="Evaluate"/> uses. Used by the withdrawal forecast to price command collapse
        /// without rolling RNG. Structurally generic — any aura loss that changes the stress
        /// inputs is priced by re-running this on the post-loss <see cref="MoraleCheckInput"/>;
        /// synapse severance is the only live consumer (Phase 6 adds command auras).
        /// </summary>
        public static MoraleState EstimateOutcome(MoraleCheckInput input)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(input.Soldiers);
            int count = input.Soldiers.Count;
            if (count == 0)
            {
                return MoraleState.Steady;
            }
            float stress = ComputeStress(input).Stress;
            float sum = 0f;
            bool leaderPresent = false;
            float leaderFailProbability = 0f;
            foreach (SoldierMoraleInput soldier in input.Soldiers)
            {
                float failProbability =
                    NormalCdf((stress - EgoToResolve(soldier.Ego)) / MoraleConstants.MoraleRollSigma);
                sum += failProbability;
                if (soldier.IsLeader)
                {
                    leaderPresent = true;
                    leaderFailProbability = failProbability;
                }
            }

            float failFraction = sum / count;
            // Deterministic counterpart of Evaluate's leaderHeld: a living leader more likely than
            // not to hold his own nerve steadies the squad, raising both bars (§5.3).
            bool leaderHeld = leaderPresent && !input.LeaderDead && leaderFailProbability < 0.5f;
            float bonus = leaderHeld ? MoraleConstants.LeaderThresholdBonus : 0f;
            float routThreshold = MoraleConstants.RoutThreshold + bonus;
            float shakenThreshold = MoraleConstants.ShakenThreshold + bonus;

            return failFraction >= routThreshold
                ? MoraleState.Routing
                : failFraction >= shakenThreshold
                    ? MoraleState.Shaken
                    : MoraleState.Steady;
        }

        /// <summary>
        /// forceDisadvantage in [0,1] from the force-wide BV share and the two-round loss
        /// trend that <see cref="BattleForceEvaluator"/> already computes (§5.2). 0 when
        /// winning/even; approaches 1 when outmatched and bleeding.
        /// </summary>
        public static float ComputeForceDisadvantage(
            int friendlyBattleValue,
            int enemyBattleValue,
            int friendlyLossPreviousTwoRounds,
            int enemyLossPreviousTwoRounds)
        {
            float bvTotal = friendlyBattleValue + enemyBattleValue;
            float bvShare = bvTotal > 0 ? friendlyBattleValue / bvTotal : 0.5f;
            float bvDisadvantage = Math.Clamp((0.5f - bvShare) * 2f, 0f, 1f);

            float lossTotal = friendlyLossPreviousTwoRounds + enemyLossPreviousTwoRounds;
            float lossShare = lossTotal > 0 ? friendlyLossPreviousTwoRounds / lossTotal : 0.5f;
            float lossDisadvantage = Math.Clamp((lossShare - 0.5f) * 2f, 0f, 1f);

            return Math.Clamp(
                (MoraleConstants.ForceDisadvantageBvWeight * bvDisadvantage)
                    + (MoraleConstants.ForceDisadvantageLossWeight * lossDisadvantage),
                0f,
                1f);
        }

        /// <summary>
        /// Rolls the check per able soldier and aggregates to one squad-level outcome (§5.3).
        /// Consumes one standard-normal draw per soldier from <paramref name="random"/>, so the
        /// whole check is deterministic under a battle seed.
        /// </summary>
        public static MoraleCheckResult Evaluate(MoraleCheckInput input, IRNG random)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(input.Soldiers);
            ArgumentNullException.ThrowIfNull(random);

            (float shock, float context, float stress) = ComputeStress(input);

            int fails = 0;
            bool leaderPresent = false;
            bool leaderFailed = false;
            foreach (SoldierMoraleInput soldier in input.Soldiers)
            {
                float resolve = EgoToResolve(soldier.Ego);
                float draw = stress + ((float)random.NextRandomZValue() * MoraleConstants.MoraleRollSigma);
                bool failed = draw > resolve;
                if (failed)
                {
                    fails++;
                }
                if (soldier.IsLeader)
                {
                    leaderPresent = true;
                    leaderFailed = failed;
                }
            }

            int ableSoldiers = input.Soldiers.Count;
            float failFraction = ableSoldiers > 0 ? (float)fails / ableSoldiers : 0f;

            // A living leader who held his own nerve steadies the squad, raising both bars.
            bool leaderHeld = leaderPresent && !leaderFailed && !input.LeaderDead;
            float bonus = leaderHeld ? MoraleConstants.LeaderThresholdBonus : 0f;
            float routThreshold = MoraleConstants.RoutThreshold + bonus;
            float shakenThreshold = MoraleConstants.ShakenThreshold + bonus;

            MoraleState outcome = failFraction >= routThreshold
                ? MoraleState.Routing
                : failFraction >= shakenThreshold
                    ? MoraleState.Shaken
                    : MoraleState.Steady;

            return new MoraleCheckResult(
                outcome,
                shock,
                context,
                stress,
                fails,
                ableSoldiers,
                failFraction,
                leaderHeld,
                routThreshold,
                shakenThreshold);
        }

        /// <summary>
        /// §5.2 propagation term: headcount-weighted fraction of the friendly able soldiers
        /// visible to <paramref name="squad"/> that belong to squads which were Routing at the
        /// START of this turn. LOCAL (within <paramref name="visualRange"/>) and scale-free;
        /// 0 when no friendly companion is visible. Excludes <paramref name="squad"/> itself —
        /// this measures visible companions breaking, not the squad's own state. IMPORTANT
        /// (§5.1): <paramref name="routingSquadIdsAtTurnStart"/> is the turn-start snapshot, so
        /// a squad routing THIS turn only raises neighbours' stress NEXT turn.
        /// </summary>
        public static float ComputeRoutingVisibleFriendlyFraction(
            BattleSquad squad,
            IEnumerable<BattleSquad> friendlySquads,
            ISet<int> routingSquadIdsAtTurnStart,
            BattleGridManager grid,
            float visualRange)
        {
            ArgumentNullException.ThrowIfNull(squad);
            ArgumentNullException.ThrowIfNull(friendlySquads);
            ArgumentNullException.ThrowIfNull(routingSquadIdsAtTurnStart);
            ArgumentNullException.ThrowIfNull(grid);

            List<BattleSoldier> observers = squad.AbleSoldiers;
            if (observers.Count == 0)
            {
                return 0f;
            }

            int visibleFriendly = 0;
            int visibleRouting = 0;
            foreach (BattleSquad companion in friendlySquads)
            {
                if (companion == null || companion.Id == squad.Id)
                {
                    continue;
                }
                bool companionRouting = routingSquadIdsAtTurnStart.Contains(companion.Id);
                foreach (BattleSoldier soldier in companion.AbleSoldiers)
                {
                    if (!IsWithinRange(observers, soldier, grid, visualRange))
                    {
                        continue;
                    }
                    visibleFriendly++;
                    if (companionRouting)
                    {
                        visibleRouting++;
                    }
                }
            }

            return visibleFriendly > 0 ? (float)visibleRouting / visibleFriendly : 0f;
        }

        /// <summary>
        /// §5.2 local-outnumber term: (visible enemy able soldiers / visible friendly able
        /// soldiers, including the squad itself) beyond parity, clamped. 0 when not
        /// outnumbered locally or when no enemy is visible within <paramref name="visualRange"/>.
        /// </summary>
        public static float ComputeLocalOutnumberRatio(
            BattleSquad squad,
            IEnumerable<BattleSquad> friendlySquads,
            IEnumerable<BattleSquad> enemySquads,
            BattleGridManager grid,
            float visualRange)
        {
            ArgumentNullException.ThrowIfNull(squad);
            ArgumentNullException.ThrowIfNull(friendlySquads);
            ArgumentNullException.ThrowIfNull(enemySquads);
            ArgumentNullException.ThrowIfNull(grid);

            List<BattleSoldier> observers = squad.AbleSoldiers;
            if (observers.Count == 0)
            {
                return 0f;
            }

            int friendlyLocal = observers.Count;
            foreach (BattleSquad companion in friendlySquads)
            {
                if (companion == null || companion.Id == squad.Id)
                {
                    continue;
                }
                friendlyLocal += companion.AbleSoldiers
                    .Count(soldier => IsWithinRange(observers, soldier, grid, visualRange));
            }

            int enemyLocal = 0;
            foreach (BattleSquad enemy in enemySquads)
            {
                if (enemy == null)
                {
                    continue;
                }
                enemyLocal += enemy.AbleSoldiers
                    .Count(soldier => IsWithinRange(observers, soldier, grid, visualRange));
            }

            if (enemyLocal == 0 || friendlyLocal == 0)
            {
                return 0f;
            }

            float ratio = ((float)enemyLocal / friendlyLocal) - 1f;
            return Math.Clamp(ratio, 0f, MoraleConstants.LocalOutnumberCap);
        }

        private static bool IsWithinRange(
            List<BattleSoldier> observers,
            BattleSoldier target,
            BattleGridManager grid,
            float visualRange)
        {
            foreach (BattleSoldier observer in observers)
            {
                if (grid.GetDistanceBetweenSoldiers(observer.Soldier.Id, target.Soldier.Id)
                    <= visualRange)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
