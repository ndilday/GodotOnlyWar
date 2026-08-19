using System;
using System.Linq;

namespace OnlyWar.Models.Events
{
    public sealed class CampaignEventClassifier
    {
        public const int CurrentVersion = 2;
        private readonly KillMilestoneRules _milestoneRules;
        private readonly NarrativeEventRules _eventRules;

        public CampaignEventClassifier(
            KillMilestoneRules milestoneRules = null,
            NarrativeEventRules eventRules = null)
        {
            _milestoneRules = milestoneRules ?? KillMilestoneRules.Initial;
            _eventRules = eventRules ?? NarrativeEventRules.Initial;
        }

        public KillMilestoneRules MilestoneRules => _milestoneRules;
        public NarrativeEventRules EventRules => _eventRules;

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
                    surfaces = CampaignEventSurfaceFlags.ServiceRecord;
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
                            if (rule.Threshold >= 100)
                                surfaces |= CampaignEventSurfaceFlags.TurnReport;
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
                case CampaignEventType.WorldSaved:
                    surfaces = CampaignEventSurfaceFlags.TurnReport | CampaignEventSurfaceFlags.ChapterChronicle;
                    importance = CampaignEventImportance.Major;
                    reasons |= CampaignEventReasonFlags.WorldChangedHands | CampaignEventReasonFlags.WorldSaved;
                    treatment = CampaignEventChronicleTreatment.Standalone;
                    break;
                case CampaignEventType.WorldLost:
                    surfaces = CampaignEventSurfaceFlags.TurnReport | CampaignEventSurfaceFlags.ChapterChronicle;
                    importance = CampaignEventImportance.Major;
                    reasons |= CampaignEventReasonFlags.WorldChangedHands | CampaignEventReasonFlags.WorldLost;
                    treatment = CampaignEventChronicleTreatment.Standalone;
                    break;
                case CampaignEventType.HiddenCultRevealed:
                    surfaces = CampaignEventSurfaceFlags.TurnReport | CampaignEventSurfaceFlags.ChapterChronicle;
                    importance = CampaignEventImportance.Notable;
                    reasons |= CampaignEventReasonFlags.HiddenCultRevealed;
                    treatment = CampaignEventChronicleTreatment.Standalone;
                    break;
                case CampaignEventType.Death:
                    surfaces |= CampaignEventSurfaceFlags.ServiceRecord;
                    if (candidate.Payload is DeathPayload death)
                    {
                        reasons &= ~(CampaignEventReasonFlags.VeteranDeath
                            | CampaignEventReasonFlags.OfficerCasualty
                            | CampaignEventReasonFlags.SeniorCasualty);
                        if (death.HadTerminatorHonours)
                            reasons |= CampaignEventReasonFlags.VeteranDeath;
                        if (_eventRules.IsNotableCasualty(death.SoldierRank, death.SoldierSubrank))
                            reasons |= CampaignEventReasonFlags.SeniorCasualty;
                    }
                    if (reasons.HasFlag(CampaignEventReasonFlags.VeteranDeath)
                        || reasons.HasFlag(CampaignEventReasonFlags.SeniorCasualty))
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
                case CampaignEventType.SquadLeaderUnavailable:
                    if (candidate.Payload is SquadLeaderUnavailablePayload unavailable
                        && unavailable.WasActualLeader
                        && !unavailable.IsDeployableAfterInjury)
                    {
                        surfaces = CampaignEventSurfaceFlags.TurnReport;
                        importance = CampaignEventImportance.Notable;
                        reasons |= CampaignEventReasonFlags.SquadLeaderUnavailable;
                    }
                    else
                    {
                        surfaces = CampaignEventSurfaceFlags.None;
                        importance = CampaignEventImportance.Routine;
                        treatment = CampaignEventChronicleTreatment.None;
                    }
                    break;
                case CampaignEventType.LastSurvivor:
                    surfaces |= CampaignEventSurfaceFlags.ServiceRecord;
                    reasons &= ~CampaignEventReasonFlags.LastSurvivor;
                    if (candidate.Payload is LastSurvivorPayload survivor
                        && survivor.StartingChapterParticipantCount >= _eventRules.LastSurvivorMinimumParticipants
                        && survivor.IsOnlyBrotherStillAbleToFight)
                    {
                        reasons |= CampaignEventReasonFlags.LastSurvivor;
                        importance = importance == CampaignEventImportance.Routine
                            ? CampaignEventImportance.Notable
                            : importance;
                        surfaces |= CampaignEventSurfaceFlags.TurnReport
                            | CampaignEventSurfaceFlags.ChapterChronicle;
                        treatment = CampaignEventChronicleTreatment.GroupWithCorrelation;
                    }
                    break;
                case CampaignEventType.SquadHeldAgainstOdds:
                    surfaces |= CampaignEventSurfaceFlags.ServiceRecord;
                    reasons &= ~CampaignEventReasonFlags.SquadHeldAgainstOdds;
                    if (candidate.Payload is SquadHeldAgainstOddsPayload held
                        && held.StartingSquadParticipantCount >= _eventRules.SquadHeldMinimumParticipants
                        && held.CasualtyFraction >= _eventRules.SquadHeldMinimumCasualtyFraction
                        && held.ChapterHeldField
                        && _eventRules.IsDefensiveCommitment(held.DefensiveMissionType))
                    {
                        reasons |= CampaignEventReasonFlags.SquadHeldAgainstOdds;
                        importance = importance == CampaignEventImportance.Routine
                            ? CampaignEventImportance.Notable
                            : importance;
                        surfaces |= CampaignEventSurfaceFlags.TurnReport
                            | CampaignEventSurfaceFlags.ChapterChronicle;
                        treatment = CampaignEventChronicleTreatment.GroupWithCorrelation;
                    }
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
                case CampaignEventType.AcceptedToTraining:
                case CampaignEventType.PsychicDetected:
                case CampaignEventType.Promotion:
                case CampaignEventType.Transfer:
                case CampaignEventType.RatingFlag:
                case CampaignEventType.AwardReceived:
                case CampaignEventType.BattleParticipation:
                case CampaignEventType.Incapacitated:
                case CampaignEventType.GeneseedRecovery:
                case CampaignEventType.MissionOutcome:
                case CampaignEventType.Oath:
                case CampaignEventType.Founding:
                    surfaces |= CampaignEventSurfaceFlags.ServiceRecord;
                    break;
                default:
                    throw new NotSupportedException($"No publication matrix entry exists for {candidate.Type}.");
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
