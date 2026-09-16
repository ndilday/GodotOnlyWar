using System;

namespace OnlyWar.Operations.Missions.Recon
{
    /// <summary>
    /// How a reconnaissance sweep turns leader-test margins into region awareness.
    /// </summary>
    /// <remarks>
    /// The observation check answers "did we learn anything", which is a different question from
    /// the stealth check's "were we seen" - that axis keeps its own consequences in
    /// ReconStealthMissionStep and DetectedMissionStep. These constants must be read against the
    /// awareness thresholds they have to reach: FactionStrategyPlanningConstants.ReconIntelThreshold
    /// (1.0, the level at which an attacker will plan an assault instead of another sweep) and
    /// FactionThreatAssessment.GarrisonFullSightIntel (2.0, full visibility of an adjacent region),
    /// against FactionIntelligenceRules.WeeklyDecayMultiplier of 0.75. Because a region decays 25% a
    /// turn, a force that keeps sweeping the same ground settles at four times its weekly gain.
    /// </remarks>
    public static class ReconIntelligenceRules
    {
        // Base difficulty of turning a day's observation into usable intelligence, before the
        // aggression modifier and the force-size term. A leader whose Tactics equals this breaks
        // even; the /5.0 in IndividualMissionTest makes each point of difficulty 0.2 sigma.
        public const float ObservationDifficulty = 9.0f;

        // Squad size enters as log10(members) - 1: neutral at ten members, a bonus above, a penalty
        // below. A fifteen-strong ork kommando squad gets +0.18, a five-man team -0.30. More eyes
        // cover more ground, with diminishing returns.
        public const float SizeNeutralExponent = 1.0f;

        // Awareness delta = k * sign(R) * sqrt(|R|), where R is the pooled sum of every
        // participating squad's every daily margin (TurnIntelligenceLedger aggregates it per
        // observer and region). The square root gives diminishing returns in both days and squads,
        // and stops one squad's freak week from deciding the whole result.
        public const float AwarenessGainCoefficient = 0.5f;

        // A sweep can come back with genuinely wrong intelligence, but it cannot unlearn the
        // ground: the loss is bounded at roughly five listening-post levels. The cap is loose
        // insurance against a freak run - sqrt alone would allow +2.65 - and binds about once in a
        // hundred missions.
        public const float MinimumAwarenessDelta = -1.0f;
        public const float MaximumAwarenessDelta = 2.0f;

        /// <summary>
        /// The force-size term, added to the observing leader's effective skill.
        /// </summary>
        public static float SizeModifier(int memberCount) =>
            memberCount < 1
                ? 0f
                : (float)Math.Log10(memberCount) - SizeNeutralExponent;

        /// <summary>
        /// Converts a turn's pooled observation margin into a region-awareness delta.
        /// </summary>
        public static float AwarenessDelta(float pooledMargin)
        {
            if (!float.IsFinite(pooledMargin) || pooledMargin == 0f) return 0f;

            float magnitude = AwarenessGainCoefficient
                * (float)Math.Sqrt(Math.Abs(pooledMargin));
            return Math.Clamp(
                pooledMargin > 0f ? magnitude : -magnitude,
                MinimumAwarenessDelta,
                MaximumAwarenessDelta);
        }
    }
}
