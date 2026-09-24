using System;

namespace OnlyWar.Battles
{
    /// <summary>
    /// Derives the number of exchange turns for which a frozen engagement geometry is worth
    /// extrapolating. This is deliberately a small, pure value model: battle planning supplies
    /// frozen battle-value pools and removal rates once per turn, and state potential consumers
    /// read the resulting per-squad horizon without changing it per option.
    ///
    /// <para>An engagement does not run until one side is exterminated. It ends, for a squad, at
    /// whichever comes first: the enemy force reaching the point where it withdraws, or the squad
    /// itself reaching the point where it stops continuing its mission
    /// (<see cref="BattleSquad.ShouldContinueMission"/>). Both are casualty thresholds set by
    /// aggression -- half the starting strength at Normal -- not annihilation, so the horizon is
    /// the shorter of the two times to lose that much. See <see cref="DeriveSquadExchangeTurns"/>.
    /// The squad's own threshold comes from its own orders; the enemy's orders are not the
    /// planner's to know, so every enemy is assumed Attritional (withdrawing at a quarter of its
    /// starting strength).
    /// </para>
    /// </summary>
    internal static class EngagementHorizonModel
    {
        // A cap on how far a frozen geometry is extrapolated, not a measured battle length.
        // Battles have run longer than this, depending on whether a disengagement is counted as
        // the end. The value was taken from the reference Xibarrus Zeta battle (183 turns); its
        // job is to stop a zero-rate or a very distant approach from turning the potential into
        // an unbounded promise.
        //
        // MEASURED 2026-08-10 (Design/Active/EngagementHorizonModel.md): lowering this to 50
        // broke all three long-approach guards and changed NONE of the short-range posture
        // failures -- the sniper still chose Run and the flamer still chose Walk, byte-identical
        // outcomes.
        //
        // CORRECTED 2026-09-23. That result was read as "a common factor cannot move an argmax":
        // every candidate's Φ_net scales with the horizon together. That is only true while Φ_net
        // is the whole comparison. A candidate's score also carries terms that do NOT scale with
        // the horizon -- the immediate exchange and the one-shot readiness of an Aim -- so the
        // horizon sets the exchange rate between a lasting positional gain and a single prepared
        // shot, and it can move the argmax. The 2026-09-23 autocannon case (Brood Brother Weapon
        // Squad vs scouts at 150 yards, RealTemplateEngagementTests) is exactly that trade: a
        // position gain of ~0.016 per turn times an 83-turn horizon (1.35) beat one aimed shot
        // (1.19). Why lowering the cap to 50 left the 2026-08-10 cases unchanged was not
        // re-measured; the likeliest reading is that it did not move their horizons far enough,
        // not that the horizon cannot matter.
        internal const float MaximumExchangeTurns = 183f;
        private const float MinimumExchangeTurns = 1f;
        private const float RemovalRateEpsilon = 0.0001f;

        /// <summary>
        /// Calculates <c>T ≈ BV_at_risk / current_removal_rate</c>, capped at the observed upper
        /// bound. The numerator is the opposing force's active battle value, and the denominator
        /// is the focal side's positive removal rate against that force. In particular, this is an
        /// exchange-duration estimate, not a signed advantage: a side that cannot currently
        /// return fire receives the cap so a long approach remains visible, while a force with a
        /// fast current exchange receives a short horizon.
        /// </summary>
        internal static float DeriveExpectedExchangeTurns(
            float battleValueAtRisk,
            float currentRemovalRate)
        {
            if (battleValueAtRisk <= 0 || float.IsNaN(battleValueAtRisk))
            {
                return 0;
            }

            if (!float.IsFinite(currentRemovalRate)
                || currentRemovalRate <= RemovalRateEpsilon)
            {
                return MaximumExchangeTurns;
            }

            return Math.Clamp(
                battleValueAtRisk / currentRemovalRate,
                MinimumExchangeTurns,
                MaximumExchangeTurns);
        }

        /// <summary>
        /// One squad's horizon: the shorter of the time for its side to strip the enemy force down
        /// to that force's withdrawal point, and the time for the fire aimed at this squad to
        /// strip IT down to its own. Each budget is battle value that can be lost before the
        /// threshold, not the whole pool.
        ///
        /// <para>A budget already spent means that side is at its threshold now, so the exchange is
        /// about to end: the horizon is the one-turn minimum, never zero. A zero rate on either
        /// side leaves that side's clock at the cap, as <see cref="DeriveExpectedExchangeTurns"/>
        /// does.</para>
        /// </summary>
        internal static float DeriveSquadExchangeTurns(
            float enemyBattleValueBeforeWithdrawal,
            float outgoingRemovalRate,
            float ownBattleValueBeforeWithdrawal,
            float incomingRemovalRate) =>
            Math.Min(
                TurnsToLose(enemyBattleValueBeforeWithdrawal, outgoingRemovalRate),
                TurnsToLose(ownBattleValueBeforeWithdrawal, incomingRemovalRate));

        /// <summary>
        /// The two clocks <see cref="DeriveSquadExchangeTurns"/> takes the minimum of, for
        /// diagnostics: turns until the enemy force reaches its withdrawal point, and turns until
        /// this squad reaches its own.
        /// </summary>
        internal static (float EnemyWithdrawalTurns, float OwnWithdrawalTurns) DeriveSquadExchangeClocks(
            float enemyBattleValueBeforeWithdrawal,
            float outgoingRemovalRate,
            float ownBattleValueBeforeWithdrawal,
            float incomingRemovalRate) =>
            (TurnsToLose(enemyBattleValueBeforeWithdrawal, outgoingRemovalRate),
                TurnsToLose(ownBattleValueBeforeWithdrawal, incomingRemovalRate));

        private static float TurnsToLose(float battleValueBudget, float removalRate)
        {
            if (float.IsNaN(battleValueBudget) || battleValueBudget <= 0)
            {
                return MinimumExchangeTurns;
            }
            return DeriveExpectedExchangeTurns(battleValueBudget, removalRate);
        }
    }
}
