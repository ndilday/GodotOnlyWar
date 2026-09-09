using OnlyWar.Domain.Missions;
using System.Collections.Generic;

namespace OnlyWar.Operations.Missions
{
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
                ReturnedToBase = context.ForceReturnedToBase,
                RemainedInTargetRegion = context.ForceRemainedInTargetRegion,
                Disposition = ResolveDisposition(context),
                NoViableTarget = context.NoViableTarget,
                AmbushSpoiled = context.AmbushSpoiled,
                TargetLocated = context.TargetLocated,
                TargetEliminated = context.TargetEliminated,
                EnemiesKilled = killed,
                EnemyKillCredits = context.EnemyKillCredits,
                FriendlyDeaths = context.FriendlyDeaths,
                FriendlyIncapacitated = context.FriendlyIncapacitated,
                FieldCareApothecaries = context.FieldCare?.ApothecaryNames ?? [],
                FieldCareTreatments = context.FieldCare?.TreatmentCount ?? 0,
                FieldCareTreatedBrothers = context.FieldCare?.TreatedSoldierCount ?? 0,
                Impact = context.Impact,
                // The objective's works are known from the posted mission whether or not the raid
                // got to them, so a failed attempt can still name what it went after.
                SabotageTarget = (context.Order?.Mission as SabotageMission)?.DefenseType,
                SabotageDamage = context.SabotageDamageDealt,
                SabotageLevelBefore = context.SabotageDefenseLevelBefore
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
