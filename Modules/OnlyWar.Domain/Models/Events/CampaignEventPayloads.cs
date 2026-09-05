using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.Events
{
    public sealed record FactionIntelEventPayload(
        int PlanetId,
        int RegionId,
        int ObserverFactionId,
        int TargetFactionId,
        IntelLevel PreviousLevel,
        IntelLevel CurrentLevel,
        float Evidence,
        bool IsFirstContact,
        CampaignEventType EventType) : ICampaignEventPayload
    {
        public ushort Version => 1;
    }

    public sealed record FactionRelationshipEventPayload(
        int LowerFactionId,
        int HigherFactionId,
        FactionStance PreviousStance,
        FactionStance CurrentStance) : ICampaignEventPayload
    {
        public CampaignEventType EventType => CampaignEventType.FactionRelationshipChanged;
        public ushort Version => 1;
    }

    public sealed record LegacySoldierEventPayload(
        CampaignEventType SourceType,
        string Detail,
        int? FactionId,
        int? WeaponTemplateId,
        int? Magnitude,
        string LocationName,
        IReadOnlyList<int> RelatedSoldierIds,
        ushort PayloadVersion = 1) : ICampaignEventPayload
    {
        public CampaignEventType EventType => SourceType;
        public ushort Version => PayloadVersion;
    }

    public sealed record FirstBloodPayload(
        int NewCumulativeTotal,
        int? OpposingFactionId,
        int? WeaponTemplateId,
        string VictimDisplayName) : ICampaignEventPayload
    {
        public CampaignEventType EventType => CampaignEventType.FirstBlood;
        public ushort Version => 1;
    }

    public sealed record KillMilestonePayload(
        int Threshold,
        int PreviousTotal,
        int NewTotal,
        int? OpposingFactionId,
        int? WeaponTemplateId) : ICampaignEventPayload
    {
        public CampaignEventType EventType => CampaignEventType.KillMilestone;
        public ushort Version => 1;
    }

    public sealed record LegacyChapterHistoryPayload(
        string Title,
        IReadOnlyList<string> SubEvents) : ICampaignEventPayload
    {
        public CampaignEventType EventType => CampaignEventType.LegacyChapterHistory;
        public ushort Version => 1;
    }

    public sealed record BattleResolvedPayload(
        string Title,
        string Summary) : ICampaignEventPayload
    {
        public CampaignEventType EventType => CampaignEventType.BattleResolved;
        public ushort Version => 1;
    }

    public sealed record ChapterFoundedPayload(
        string ChapterName,
        int FoundingWeek,
        int? ChapterMasterId,
        string ChapterMasterName,
        int InitialActiveStrength,
        string OpeningAuthorityName,
        string OpeningDirective,
        int PromisedPlanetId,
        string PromisedPlanetName) : ICampaignEventPayload
    {
        public CampaignEventType EventType => CampaignEventType.ChapterFounded;
        public ushort Version => 1;
    }

    /// <summary>
    /// Immutable facts shared by every typed event emitted from one tactical battle. The envelope
    /// correlation key remains authoritative; repeating it here makes the payload self-describing
    /// for consumers that do not first join the relational event row.
    /// </summary>
    public sealed record BattleEventContextSnapshot(
        string CorrelationKey,
        int OccurredWeek,
        int? RegionId = null,
        string RegionName = null,
        int? PlanetId = null,
        string PlanetName = null,
        int? OpposingFactionId = null,
        string OpposingFactionName = null,
        int? MissionId = null,
        string MissionName = null,
        MissionType? MissionType = null,
        int? OrderId = null,
        string OrderName = null,
        Aggression? Aggression = null,
        bool? PlayerHeldField = null)
    {
        public string LocationName => string.IsNullOrWhiteSpace(RegionName)
            ? PlanetName
            : string.IsNullOrWhiteSpace(PlanetName)
                ? RegionName
                : $"{RegionName}, {PlanetName}";

        public BattleEventContextSnapshot ForOpposingFaction(
            int? factionId,
            string factionName,
            bool? playerHeldField = null) =>
            this with
            {
                OpposingFactionId = factionId,
                OpposingFactionName = factionName,
                PlayerHeldField = playerHeldField ?? PlayerHeldField
            };
    }

    /// <summary>
    /// The disposition recorded by a typed death event. A procedural death is deliberately
    /// represented here instead of being made to look like a battle casualty.
    /// </summary>
    public enum DeathDisposition
    {
        BodyRecovered = 0,
        BodyLeftPresumedDead = 1,
        NonBattleProcedural = 2
    }

    public enum GeneseedRecoveryOutcome
    {
        Recovered = 0,
        Destroyed = 1,
        Lost = 2,
        Immature = 3
    }

    public enum MentorRelationshipKind
    {
        ScoutMentor = 0
    }

    public enum MentorAssignmentContext
    {
        NeophytePlacement = 0
    }

    public enum NearDeathRecoveryMethod
    {
        NaturalOrFieldCare = 0,
        Cybernetic = 1,
        VatGrown = 2
    }

    public sealed record BattleParticipationPayload(
        BattleEventContextSnapshot BattleContext,
        int EnemiesTakenDown,
        int WoundsReceived,
        int? OpposingFactionId,
        string OpposingFactionName) : ICampaignEventPayload
    {
        public CampaignEventType EventType => CampaignEventType.BattleParticipation;
        public ushort Version => 3;
    }

    public sealed record IncapacitatedPayload(
        BattleEventContextSnapshot BattleContext,
        int? DefiningHitLocationTemplateId,
        string DefiningHitLocationName,
        bool DefiningLocationIsVital,
        bool DefiningLocationWasCrippled,
        bool DefiningLocationWasSevered,
        bool QualifiesAsNearDeath,
        int? CausingWeaponTemplateId,
        string CausingWeaponName,
        int? SoldierTemplateId,
        string SoldierTemplateName,
        int? SoldierRank) : ICampaignEventPayload
    {
        public CampaignEventType EventType => CampaignEventType.Incapacitated;
        public ushort Version => 3;
    }

    public sealed record DeathPayload(
        BattleEventContextSnapshot BattleContext,
        DeathDisposition Disposition,
        int? OpposingFactionId,
        string OpposingFactionName,
        int? CausingWeaponTemplateId,
        string CausingWeaponName,
        int? SoldierTemplateId,
        string SoldierTemplateName,
        int? SoldierRank,
        int ServiceStartWeek,
        int FinalConfirmedKillCount,
        int? DefiningHitLocationTemplateId,
        string DefiningHitLocationName,
        bool DefiningLocationIsVital,
        bool BodyRecovered,
        string Detail = null,
        int? SoldierSubrank = null,
        bool HadTerminatorHonours = false) : ICampaignEventPayload
    {
        public CampaignEventType EventType => CampaignEventType.Death;
        public ushort Version => 3;

        public bool IsBattleDeath => BattleContext != null;
    }

    public sealed record SquadLeaderUnavailablePayload(
        int SquadId,
        string SquadName,
        int SoldierRank,
        int SoldierSubrank,
        bool WasActualLeader,
        bool IsDeployableAfterInjury,
        BattleEventContextSnapshot BattleContext) : ICampaignEventPayload
    {
        public CampaignEventType EventType => CampaignEventType.SquadLeaderUnavailable;
        public ushort Version => 1;
    }

    public sealed record WorldControlChangedPayload(
        int PlanetId,
        string PlanetName,
        int ImperialFactionId,
        int PreviousControllingFactionId,
        int CurrentControllingFactionId,
        int EpisodeStartedWeek,
        int EpisodeCompletedWeek,
        bool ChapterParticipated,
        bool? CurrentControlIsImperial = null) : ICampaignEventPayload
    {
        public CampaignEventType EventType => (CurrentControlIsImperial
            ?? CurrentControllingFactionId == ImperialFactionId)
            ? CampaignEventType.WorldSaved
            : CampaignEventType.WorldLost;
        public ushort Version => 1;
    }

    public sealed record HiddenCultRevealedPayload(
        int PlanetId,
        string PlanetName,
        int FactionId,
        string FactionName,
        int RevealedWeek) : ICampaignEventPayload
    {
        public CampaignEventType EventType => CampaignEventType.HiddenCultRevealed;
        public ushort Version => 1;
    }

    public sealed record GeneseedRecoveryPayload(
        BattleEventContextSnapshot BattleContext,
        long SourceDeathEventId,
        GeneseedRecoveryOutcome Result,
        float? Purity) : ICampaignEventPayload
    {
        public CampaignEventType EventType => CampaignEventType.GeneseedRecovery;
        public ushort Version => 3;

        // Outcome is a readable alias for callers that use the design vocabulary.
        public GeneseedRecoveryOutcome Outcome => Result;
    }

    public sealed record LastSurvivorPayload(
        BattleEventContextSnapshot BattleContext,
        int StartingChapterParticipantCount,
        int EndingCombatEffectiveCount,
        int KilledCount,
        int IncapacitatedCount,
        bool ChapterHeldField) : ICampaignEventPayload
    {
        public CampaignEventType EventType => CampaignEventType.LastSurvivor;
        public ushort Version => 3;

        public bool IsOnlyBrotherStillAbleToFight => EndingCombatEffectiveCount == 1;
    }

    public sealed record SquadHeldAgainstOddsPayload(
        BattleEventContextSnapshot BattleContext,
        int StartingSquadParticipantCount,
        int KilledCount,
        int IncapacitatedCount,
        double CasualtyFraction,
        MissionType? DefensiveMissionType,
        Aggression? AggressionSnapshot,
        bool ChapterHeldField) : ICampaignEventPayload
    {
        public CampaignEventType EventType => CampaignEventType.SquadHeldAgainstOdds;
        public ushort Version => 1;
    }

    public sealed record MentorAssignedPayload(
        MentorRelationshipKind RelationshipKind,
        MentorAssignmentContext AssignmentContext,
        int ScoutSquadId,
        string ScoutSquadName,
        int MentorSoldierId,
        string MentorDisplayName) : ICampaignEventPayload
    {
        public CampaignEventType EventType => CampaignEventType.MentorAssigned;
        public ushort Version => 3;
    }

    public sealed record NearDeathRecoveryPayload(
        long SourceIncapacitationEventId,
        int RecoveryDurationWeeks,
        int? DefiningVitalLocationTemplateId,
        string DefiningVitalLocationName,
        NearDeathRecoveryMethod RecoveryMethod,
        bool LesserWoundsRemain,
        BattleEventContextSnapshot BattleContext = null) : ICampaignEventPayload
    {
        public CampaignEventType EventType => CampaignEventType.NearDeathRecovery;
        public ushort Version => 3;
    }

    public sealed record BodyPartReplacementPayload(
        int PrimaryHitLocationTemplateId,
        string PrimaryHitLocationName,
        MedicalProcedureType ReplacementMethod,
        bool WasAlreadyCybernetic,
        int? ProcedureDurationWeeks,
        int? RequisitionCost,
        long? SourceIncapacitationEventId) : ICampaignEventPayload
    {
        public CampaignEventType EventType => CampaignEventType.BodyPartReplacement;
        public ushort Version => 1;

        public MedicalProcedureType Method => ReplacementMethod;
    }

    public sealed record KillMilestoneRule(
        int Threshold,
        CampaignEventImportance Importance,
        CampaignEventChronicleTreatment ChronicleTreatment);

    public sealed class KillMilestoneRules
    {
        public IReadOnlyList<KillMilestoneRule> Rules { get; }

        public KillMilestoneRules(IEnumerable<KillMilestoneRule> rules)
        {
            List<KillMilestoneRule> ordered = new();
            foreach (KillMilestoneRule rule in rules ?? [])
            {
                if (rule == null) throw new ArgumentException("A milestone rule cannot be null.");
                if (rule.Threshold <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(rules),
                        "Kill milestone thresholds must be positive.");
                }
                ordered.Add(rule);
            }

            if (ordered.Select(rule => rule.Threshold).Distinct().Count() != ordered.Count)
                throw new ArgumentException("Kill milestone thresholds must be unique.", nameof(rules));
            if (ordered.Any() && !ordered.SequenceEqual(ordered.OrderBy(rule => rule.Threshold)))
                throw new ArgumentException("Kill milestone thresholds must be strictly increasing.", nameof(rules));

            Rules = ordered.AsReadOnly();
        }

        public static KillMilestoneRules Initial { get; } = new(
        [
            new(10, CampaignEventImportance.Routine, CampaignEventChronicleTreatment.None),
            new(50, CampaignEventImportance.Routine, CampaignEventChronicleTreatment.None),
            new(100, CampaignEventImportance.Major, CampaignEventChronicleTreatment.Standalone),
            new(500, CampaignEventImportance.Major, CampaignEventChronicleTreatment.Standalone),
            new(1000, CampaignEventImportance.Defining, CampaignEventChronicleTreatment.Standalone)
        ]);
    }

    /// <summary>
    /// Central thresholds for the first narrative achievement pass. Keeping these values in one
    /// validated object makes the emitter bounded and prevents the rules from drifting between
    /// event types while leaving balance-data migration for a later pass.
    /// </summary>
    public sealed class NarrativeEventRules
    {
        public int LastSurvivorMinimumParticipants { get; }
        public int SquadHeldMinimumParticipants { get; }
        public double SquadHeldMinimumCasualtyFraction { get; }
        public int NotableCasualtyMinimumRank { get; }
        public int NotableCasualtyMinimumSubrank { get; }

        public NarrativeEventRules(
            int lastSurvivorMinimumParticipants = 5,
            int squadHeldMinimumParticipants = 5,
            double squadHeldMinimumCasualtyFraction = 0.5,
            int notableCasualtyMinimumRank = 2,
            int notableCasualtyMinimumSubrank = 1)
        {
            if (lastSurvivorMinimumParticipants <= 0)
                throw new ArgumentOutOfRangeException(nameof(lastSurvivorMinimumParticipants));
            if (squadHeldMinimumParticipants <= 0)
                throw new ArgumentOutOfRangeException(nameof(squadHeldMinimumParticipants));
            if (squadHeldMinimumCasualtyFraction is < 0 or > 1)
                throw new ArgumentOutOfRangeException(nameof(squadHeldMinimumCasualtyFraction));
            if (notableCasualtyMinimumRank < 0)
                throw new ArgumentOutOfRangeException(nameof(notableCasualtyMinimumRank));
            if (notableCasualtyMinimumSubrank < 0)
                throw new ArgumentOutOfRangeException(nameof(notableCasualtyMinimumSubrank));

            LastSurvivorMinimumParticipants = lastSurvivorMinimumParticipants;
            SquadHeldMinimumParticipants = squadHeldMinimumParticipants;
            SquadHeldMinimumCasualtyFraction = squadHeldMinimumCasualtyFraction;
            NotableCasualtyMinimumRank = notableCasualtyMinimumRank;
            NotableCasualtyMinimumSubrank = notableCasualtyMinimumSubrank;
        }

        public static NarrativeEventRules Initial { get; } = new();

        public bool IsDefensiveCommitment(MissionType? missionType) => missionType is
            MissionType.DefenseInDepth
                or MissionType.Fortify
                or MissionType.LastStand
                or MissionType.ShowOfForce;

        public bool IsNotableCasualty(int? rank, int? subrank) => rank.HasValue
            && (rank.Value > NotableCasualtyMinimumRank
                || (rank.Value == NotableCasualtyMinimumRank
                    && (subrank ?? 0) >= NotableCasualtyMinimumSubrank));
    }
}
