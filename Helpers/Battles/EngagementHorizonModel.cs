using System;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// Derives the number of exchange turns for which a frozen engagement geometry is worth
    /// extrapolating. This is deliberately a small, pure value model: battle planning supplies a
    /// frozen force-level battle-value pool and removal rate once per turn, and state potential
    /// consumers read the resulting horizon without changing it per option.
    /// </summary>
    internal static class EngagementHorizonModel
    {
        // The upper end of the observed long-engagement distribution. The reference Xibarrus
        // Zeta battle lasted 183 turns; the cap prevents a zero-rate or a very distant approach
        // from turning the potential into an unbounded promise.
        //
        // MEASURED 2026-08-10 (Design/Active/EngagementHorizonModel.md): lowering this to 50
        // broke all three long-approach guards and changed NONE of the short-range posture
        // failures -- the sniper still chose Run and the flamer still chose Walk, byte-identical
        // outcomes. Posture is insensitive to this constant because Φ(s) cancels, so the
        // discriminating quantity is Φ_net(s'), and every candidate's Φ_net scales with the
        // horizon together; a common factor cannot move an argmax. Do not tune this to chase a
        // posture defect -- it can only trade approach behaviour away for nothing.
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
    }
}
