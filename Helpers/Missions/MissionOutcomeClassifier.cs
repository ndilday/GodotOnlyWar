using OnlyWar.Models.Missions;

namespace OnlyWar.Helpers.Missions
{
    // The strike force's terminal fate, distilled from MissionContext's structured signals. This is
    // orthogonal to whether the mission's objective succeeded (see MissionOutcomeClassification's other
    // members) - a force can, say, break contact cleanly having found no target.
    public enum MissionForceDisposition
    {
        // Nothing notable happened to the force itself: an undetected recon, an overt diversion, or a
        // mission that simply ran its course.
        Nominal = 0,
        // Detected, but slipped back out (evaded its interceptors / exfiltrated to base).
        BrokeContact,
        // Detected and could not get out; lost behind enemy lines (assumed dead / gone to ground).
        LostContact,
        // An engagement left the force combat-ineffective; it withdrew under fire with heavy losses.
        WithdrewUnderFire,
        // Could not reach the objective before acting (failed to infiltrate / too many casualties).
        AbortedBeforeObjective
    }

    // The single, structured verdict on a non-battle mission. Built once, by MissionOutcomeClassifier,
    // from the signals the mission steps set on MissionContext, and consumed by BOTH the career-log
    // recorder (MissionOutcomeRecorder, third-person past tense) and the end-of-turn commander report
    // (MissionReportSummaryBuilder, second-person "Your forces ..."). Keeping the classification here -
    // rather than re-deriving it from Log text in each consumer - means the two renderings can never
    // silently disagree, and a change to a step's log wording can't misclassify anything.
    public sealed class MissionOutcomeClassification
    {
        public MissionType MissionType { get; init; }
        public bool WasDetected { get; init; }
        public MissionForceDisposition Disposition { get; init; }
        public bool NoViableTarget { get; init; }
        public bool TargetLocated { get; init; }
        public bool TargetEliminated { get; init; }
        public int EnemiesKilled { get; init; }
        public int EnemyKillCredits { get; init; }
        public float Impact { get; init; }
    }

    public static class MissionOutcomeClassifier
    {
        public static MissionOutcomeClassification Classify(MissionContext context)
        {
            // Mirrors EndOfTurnDialogController/MissionOutcomeRecorder's own null handling: a missing
            // mission degrades to Patrol rather than throwing.
            MissionType missionType = context.Order?.Mission?.MissionType ?? MissionType.Patrol;
            int killed = context.EnemiesKilled;
            return new MissionOutcomeClassification
            {
                MissionType = missionType,
                // Spotter is set the moment a detection resolves; its presence is the detection signal.
                WasDetected = context.Spotter != null,
                Disposition = ResolveDisposition(context),
                NoViableTarget = context.NoViableTarget,
                TargetLocated = context.TargetLocated,
                TargetEliminated = context.TargetEliminated,
                EnemiesKilled = killed,
                EnemyKillCredits = context.EnemyKillCredits,
                Impact = context.Impact
            };
        }

        // Priority order matters: a force can set more than one disposition signal across a mission's
        // steps (e.g. break contact once, then be lost on a later exfil attempt), so the worse/terminal
        // fate wins over a clean break.
        private static MissionForceDisposition ResolveDisposition(MissionContext context)
        {
            if (context.ForceLostContact) return MissionForceDisposition.LostContact;
            if (context.ForceWithdrewUnderFire) return MissionForceDisposition.WithdrewUnderFire;
            if (context.ObjectiveAborted) return MissionForceDisposition.AbortedBeforeObjective;
            if (context.ForceBrokeContact) return MissionForceDisposition.BrokeContact;
            return MissionForceDisposition.Nominal;
        }
    }
}
