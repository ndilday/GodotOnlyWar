using System;
using System.Linq;

namespace OnlyWar.Models.Events
{
    public sealed class CampaignEventClassifier
    {
        public const int CurrentVersion = 1;
        private readonly KillMilestoneRules _milestoneRules;

        public CampaignEventClassifier(KillMilestoneRules milestoneRules = null)
        {
            _milestoneRules = milestoneRules ?? KillMilestoneRules.Initial;
        }

        public KillMilestoneRules MilestoneRules => _milestoneRules;

        public CampaignEventPublication Classify(CampaignEventCandidate candidate)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));

            CampaignEventSurfaceFlags surfaces = candidate.SurfaceHint;
            CampaignEventImportance importance = candidate.ImportanceHint ?? CampaignEventImportance.Routine;
            CampaignEventReasonFlags reasons = candidate.ReasonHint;
            CampaignEventChronicleTreatment treatment =
                candidate.ChronicleTreatmentHint ?? CampaignEventChronicleTreatment.None;

            switch (candidate.Type)
            {
                case CampaignEventType.FirstBlood:
                    surfaces = CampaignEventSurfaceFlags.ServiceRecord
                        | CampaignEventSurfaceFlags.TurnReport
                        | CampaignEventSurfaceFlags.ChapterChronicle;
                    importance = CampaignEventImportance.Notable;
                    reasons |= CampaignEventReasonFlags.FirstBlood;
                    treatment = CampaignEventChronicleTreatment.GroupWithCorrelation;
                    break;
                case CampaignEventType.KillMilestone:
                    surfaces |= CampaignEventSurfaceFlags.ServiceRecord
                        | CampaignEventSurfaceFlags.TurnReport;
                    reasons |= CampaignEventReasonFlags.KillMilestone;
                    if (candidate.Payload is KillMilestonePayload milestone
                        && _milestoneRules.Rules is { Count: > 0 })
                    {
                        KillMilestoneRule rule = _milestoneRules.Rules
                            .FirstOrDefault(item => item.Threshold == milestone.Threshold);
                        if (rule != null)
                        {
                            importance = rule.Importance;
                            treatment = rule.ChronicleTreatment;
                        }
                    }
                    if (treatment != CampaignEventChronicleTreatment.None)
                        surfaces |= CampaignEventSurfaceFlags.ChapterChronicle;
                    break;
                case CampaignEventType.LegacyChapterHistory:
                    surfaces = CampaignEventSurfaceFlags.ChapterChronicle;
                    treatment = CampaignEventChronicleTreatment.Standalone;
                    break;
                case CampaignEventType.BattleResolved:
                    surfaces |= CampaignEventSurfaceFlags.TurnReport;
                    // A resolved battle is a Turn Report fact by default. It enters the Chronicle
                    // only when a correlated achievement or a major strategic hint promotes it.
                    if (importance != CampaignEventImportance.Routine
                        || treatment != CampaignEventChronicleTreatment.None)
                    {
                        surfaces |= CampaignEventSurfaceFlags.ChapterChronicle;
                        treatment = treatment == CampaignEventChronicleTreatment.None
                            ? CampaignEventChronicleTreatment.GroupWithCorrelation
                            : treatment;
                    }
                    break;
                case CampaignEventType.ChapterFounded:
                    surfaces = CampaignEventSurfaceFlags.ChapterChronicle;
                    importance = CampaignEventImportance.Defining;
                    treatment = CampaignEventChronicleTreatment.Standalone;
                    break;
                case CampaignEventType.FactionPresenceConfirmed:
                    surfaces |= CampaignEventSurfaceFlags.TurnReport;
                    break;
                case CampaignEventType.FactionPresenceLocated:
                    surfaces |= CampaignEventSurfaceFlags.TurnReport;
                    importance = importance == CampaignEventImportance.Routine
                        ? CampaignEventImportance.Notable
                        : importance;
                    break;
                case CampaignEventType.FactionPresenceDisproven:
                    surfaces |= CampaignEventSurfaceFlags.TurnReport;
                    break;
                case CampaignEventType.FactionFirstContact:
                    surfaces |= CampaignEventSurfaceFlags.TurnReport
                        | CampaignEventSurfaceFlags.ChapterChronicle;
                    importance = CampaignEventImportance.Notable;
                    reasons |= CampaignEventReasonFlags.FirstContact;
                    treatment = CampaignEventChronicleTreatment.Standalone;
                    break;
                case CampaignEventType.FactionRelationshipChanged:
                    surfaces |= CampaignEventSurfaceFlags.TurnReport;
                    importance = importance == CampaignEventImportance.Routine
                        ? CampaignEventImportance.Notable
                        : importance;
                    break;
                case CampaignEventType.Death:
                    surfaces |= CampaignEventSurfaceFlags.ServiceRecord;
                    if (reasons.HasFlag(CampaignEventReasonFlags.VeteranDeath)
                        || reasons.HasFlag(CampaignEventReasonFlags.OfficerCasualty))
                    {
                        surfaces |= CampaignEventSurfaceFlags.TurnReport
                            | CampaignEventSurfaceFlags.ChapterChronicle;
                        treatment = CampaignEventChronicleTreatment.Standalone;
                        importance = CampaignEventImportance.Major;
                    }
                    else if (treatment == CampaignEventChronicleTreatment.Standalone
                        && importance >= CampaignEventImportance.Major)
                    {
                        surfaces |= CampaignEventSurfaceFlags.ChapterChronicle;
                    }
                    break;
                case CampaignEventType.LastSurvivor:
                    surfaces |= CampaignEventSurfaceFlags.ServiceRecord;
                    reasons |= CampaignEventReasonFlags.LastSurvivor;
                    importance = importance == CampaignEventImportance.Routine
                        ? CampaignEventImportance.Notable
                        : importance;
                    surfaces |= CampaignEventSurfaceFlags.ChapterChronicle;
                    treatment = CampaignEventChronicleTreatment.GroupWithCorrelation;
                    break;
                case CampaignEventType.SquadHeldAgainstOdds:
                    surfaces |= CampaignEventSurfaceFlags.ServiceRecord;
                    reasons |= CampaignEventReasonFlags.SquadHeldAgainstOdds;
                    importance = importance == CampaignEventImportance.Routine
                        ? CampaignEventImportance.Notable
                        : importance;
                    surfaces |= CampaignEventSurfaceFlags.ChapterChronicle;
                    treatment = CampaignEventChronicleTreatment.GroupWithCorrelation;
                    break;
                case CampaignEventType.MentorAssigned:
                    surfaces |= CampaignEventSurfaceFlags.ServiceRecord;
                    break;
                case CampaignEventType.NearDeathRecovery:
                    surfaces |= CampaignEventSurfaceFlags.ServiceRecord;
                    reasons |= CampaignEventReasonFlags.NearDeathRecovery;
                    break;
                case CampaignEventType.BodyPartReplacement:
                    surfaces |= CampaignEventSurfaceFlags.ServiceRecord;
                    reasons |= CampaignEventReasonFlags.BodyPartReplacement;
                    break;
                default:
                    surfaces |= CampaignEventSurfaceFlags.ServiceRecord;
                    break;
            }

            return new CampaignEventPublication(
                surfaces,
                importance,
                reasons,
                treatment,
                CurrentVersion);
        }
    }
}
