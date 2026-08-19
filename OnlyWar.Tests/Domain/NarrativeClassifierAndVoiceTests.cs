using OnlyWar.Models.Events;
using System.Linq;
using System.Collections.Generic;
using Xunit;

namespace OnlyWar.Tests.Domain;

public sealed class NarrativeClassifierAndVoiceTests
{
    [Theory]
    [InlineData(10, false, false, CampaignEventImportance.Routine)]
    [InlineData(50, false, false, CampaignEventImportance.Routine)]
    [InlineData(100, true, true, CampaignEventImportance.Major)]
    [InlineData(500, true, true, CampaignEventImportance.Major)]
    [InlineData(1000, true, true, CampaignEventImportance.Defining)]
    public void KillMilestoneMatrixMatchesApprovedThresholds(
        int threshold, bool turn, bool chronicle, CampaignEventImportance importance)
    {
        CampaignEventPublication publication = new CampaignEventClassifier().Classify(new CampaignEventCandidate(
            CampaignEventType.KillMilestone, 1, 1, "battle/1", $"milestone/{threshold}", 1,
            new KillMilestonePayload(threshold, threshold - 1, threshold, null, null)));

        Assert.True(publication.PublishesToServiceRecord);
        Assert.Equal(turn, publication.PublishesToTurnReport);
        Assert.Equal(chronicle, publication.PublishesToChapterChronicle);
        Assert.Equal(importance, publication.Importance);
        Assert.Equal(CampaignEventClassifier.CurrentVersion, publication.ClassifierVersion);
    }

    [Fact]
    public void InitialMilestonesContainNeitherTwentyFiveNorTwoHundredFifty()
    {
        int[] thresholds = KillMilestoneRules.Initial.Rules.Select(rule => rule.Threshold).ToArray();
        Assert.Equal(new[] { 10, 50, 100, 500, 1000 }, thresholds);
        Assert.DoesNotContain(25, thresholds);
        Assert.DoesNotContain(250, thresholds);
    }

    [Theory]
    [InlineData(1, 9, false)]
    [InlineData(2, 0, false)]
    [InlineData(2, 1, true)]
    [InlineData(2, 2, true)]
    [InlineData(3, 0, true)]
    public void SeniorCasualtyBoundaryIsLexicographic(int rank, int subrank, bool notable)
    {
        CampaignEventPublication publication = ClassifyDeath(rank, subrank, honours: false,
            CampaignEventReasonFlags.OfficerCasualty | CampaignEventReasonFlags.VeteranDeath);

        Assert.Equal(notable, publication.ReasonFlags.HasFlag(CampaignEventReasonFlags.SeniorCasualty));
        Assert.False(publication.ReasonFlags.HasFlag(CampaignEventReasonFlags.OfficerCasualty));
        Assert.False(publication.ReasonFlags.HasFlag(CampaignEventReasonFlags.VeteranDeath));
        Assert.Equal(notable, publication.PublishesToTurnReport);
        Assert.Equal(notable, publication.PublishesToChapterChronicle);
    }

    [Fact]
    public void TerminatorHonoursTriggerIsExplicitAndProspective()
    {
        Assert.False(ClassifyDeath(1, 0, false).ReasonFlags.HasFlag(CampaignEventReasonFlags.VeteranDeath));
        Assert.True(ClassifyDeath(1, 0, true).ReasonFlags.HasFlag(CampaignEventReasonFlags.VeteranDeath));
    }

    [Fact]
    public void SquadLeaderUnavailabilityRequiresActualLeaderAndNonDeployability()
    {
        CampaignEventClassifier classifier = new();
        CampaignEventPublication available = classifier.Classify(LeaderCandidate(true, true, "available"));
        CampaignEventPublication notLeader = classifier.Classify(LeaderCandidate(false, false, "not-leader"));
        CampaignEventPublication disrupted = classifier.Classify(LeaderCandidate(true, false, "disrupted"));

        Assert.Equal(CampaignEventSurfaceFlags.None, available.SurfaceFlags);
        Assert.Equal(CampaignEventSurfaceFlags.None, notLeader.SurfaceFlags);
        Assert.True(disrupted.PublishesToTurnReport);
        Assert.False(disrupted.PublishesToChapterChronicle);
    }

    [Fact]
    public void HeroicBattleTriggersAreValidatedByCentralRules()
    {
        CampaignEventClassifier classifier = new();
        BattleEventContextSnapshot context = new("battle/heroic", 1);
        CampaignEventPublication tooSmall = classifier.Classify(new CampaignEventCandidate(
            CampaignEventType.LastSurvivor, 1, 1, context.CorrelationKey, "small", 3,
            new LastSurvivorPayload(context, 4, 1, 2, 1, true)));
        CampaignEventPublication survivor = classifier.Classify(new CampaignEventCandidate(
            CampaignEventType.LastSurvivor, 1, 1, context.CorrelationKey, "survivor", 3,
            new LastSurvivorPayload(context, 5, 1, 2, 2, true)));
        CampaignEventPublication wrongOrder = classifier.Classify(new CampaignEventCandidate(
            CampaignEventType.SquadHeldAgainstOdds, 1, 1, context.CorrelationKey, "wrong-order", 1,
            new SquadHeldAgainstOddsPayload(context, 5, 1, 2, 0.6,
                OnlyWar.Models.Missions.MissionType.Recon, null, true)));

        Assert.False(tooSmall.PublishesToTurnReport);
        Assert.True(survivor.PublishesToTurnReport);
        Assert.True(survivor.PublishesToChapterChronicle);
        Assert.False(wrongOrder.PublishesToTurnReport);
    }

    [Fact]
    public void WorldControlEpisodesCompleteOnlyAtAStableController()
    {
        WorldControlEpisodeTracker tracker = new();
        Assert.Null(tracker.Observe(7, "Vardos", 1, 1, false, 1));
        Assert.Null(tracker.Observe(7, "Vardos", 1, null, true, 3, true));

        WorldControlChangedPayload saved = tracker.Observe(7, "Vardos", 1, 1, false, 8);
        Assert.Equal(CampaignEventType.WorldSaved, saved.EventType);
        Assert.True(saved.ChapterParticipated);

        Assert.Null(tracker.Observe(7, "Vardos", 1, null, true, 12));
        WorldControlChangedPayload lost = tracker.Observe(7, "Vardos", 1, 9, false, 14);
        Assert.Equal(CampaignEventType.WorldLost, lost.EventType);
    }

    [Fact]
    public void NotableDeathWaitsForGeneseedAndFreezesACompleteEulogy()
    {
        CampaignEventLedger ledger = new();
        ChapterChronicleLedger chronicle = new();
        CampaignEventRecorder recorder = new(ledger, chronicle: chronicle);
        BattleEventContextSnapshot context = new("battle/20", 20, PlanetId: 5, PlanetName: "Vardos");
        CampaignEventEntityRef subject = new(CampaignEntityKind.Soldier, 3,
            CampaignEventEntityRole.Subject, "Sergeant Acastus");
        CampaignEvent death = recorder.Record(new CampaignEventCandidate(
            CampaignEventType.Death, 20, 20, context.CorrelationKey, "death/3", 3,
            new DeathPayload(context, DeathDisposition.BodyRecovered, 9, "Tyranids", null,
                "venom cannon", 2, "Tactical Sergeant", 2, 20 - (87 * 52), 126,
                null, null, true, true, SoldierSubrank: 1), [subject]));

        Assert.Empty(chronicle.Entries);
        recorder.Record(new CampaignEventCandidate(
            CampaignEventType.GeneseedRecovery, 20, 20, context.CorrelationKey, "seed/3", 3,
            new GeneseedRecoveryPayload(context, death.Id, GeneseedRecoveryOutcome.Lost, null), [subject]));

        ChapterChronicleEntry entry = Assert.Single(chronicle.Entries);
        Assert.Contains("Sergeant Acastus", entry.Body);
        Assert.Contains("Vardos", entry.Body);
        Assert.Contains("venom cannon", entry.Body);
        Assert.Contains("87 years", entry.Body);
        Assert.Contains("126 confirmed kills", entry.Body);
        Assert.Contains("gene-seed was lost", entry.Body);
        Assert.Equal(new[] { death.Id, death.Id + 1 }, entry.CampaignEventIds);
    }

    [Fact]
    public void ContinuityPrefersPersonalRelationshipAndPersistsContributorId()
    {
        CampaignEventPublication routine = CampaignEventPublication.ServiceRecordOnly();
        CampaignEvent mentor = new(1, CampaignEventType.MentorAssigned, 1, 1, null, "mentor", 3,
            new MentorAssignedPayload(MentorRelationshipKind.ScoutMentor,
                MentorAssignmentContext.NeophytePlacement, 5, "Scout Squad IX", 8, "Sergeant Decimus"),
            [
                new CampaignEventEntityRef(CampaignEntityKind.Soldier, 3,
                    CampaignEventEntityRole.Subject, "Brother Acastus"),
                new CampaignEventEntityRef(CampaignEntityKind.Soldier, 8,
                    CampaignEventEntityRole.Related, "Sergeant Decimus")
            ], routine);
        CampaignEvent milestone = new(2, CampaignEventType.KillMilestone, 2, 2, "battle/2", "kills", 1,
            new KillMilestonePayload(100, 99, 100, null, null),
            [new CampaignEventEntityRef(CampaignEntityKind.Soldier, 3,
                CampaignEventEntityRole.Subject, "Brother Acastus")], routine);
        CampaignEvent anchor = new(3, CampaignEventType.WorldSaved, 3, 3, null, "saved", 1,
            new WorldControlChangedPayload(9, "Vardos", 1, 1, 1, 2, 3, true),
            [
                new CampaignEventEntityRef(CampaignEntityKind.Soldier, 3,
                    CampaignEventEntityRole.Subject, "Brother Acastus"),
                new CampaignEventEntityRef(CampaignEntityKind.Planet, 9,
                    CampaignEventEntityRole.Location, "Vardos")
            ], new CampaignEventPublication(CampaignEventSurfaceFlags.ChapterChronicle,
                CampaignEventImportance.Major, CampaignEventReasonFlags.WorldSaved,
                CampaignEventChronicleTreatment.Standalone, CampaignEventClassifier.CurrentVersion));

        ChapterChronicleEntry entry = new ChapterChronicleComposer().Compose(
            1, [anchor], earlierEvents: new List<CampaignEvent> { milestone, mentor });

        Assert.Equal(new long[] { mentor.Id }, entry.CallbackEventIds);
        Assert.Contains("Sergeant Decimus", entry.Body);
    }

    [Fact]
    public void AnnotationLinksCorrectionWithoutChangingFrozenEntry()
    {
        ChapterChronicleLedger ledger = new();
        ChapterChronicleEntry entry = new(1, 1, 1, CampaignEventImportance.Major, null,
            "entry", "Original", "The first account remains.",
            CampaignEventNarrator.ChapterInternalNarratorKey,
            CampaignEventNarrator.CurrentVersion, 0, [1L]);
        ledger.Append(entry);
        ledger.AppendAnnotation(new ChapterChronicleAnnotation(
            1, entry.Id, 2, 4, "Subsequent testimony corrected the account.", "annotation/1"));

        Assert.Equal("The first account remains.", entry.Body);
        ChapterChronicleAnnotation annotation = Assert.Single(ledger.GetAnnotations(entry.Id));
        Assert.StartsWith("Later annotation:", annotation.Body);
        Assert.True(annotation.IsCorrection);
        Assert.Equal(2, annotation.EvidenceEventId);
    }

    private static CampaignEventPublication ClassifyDeath(
        int rank, int subrank, bool honours, CampaignEventReasonFlags hint = CampaignEventReasonFlags.None) =>
        new CampaignEventClassifier().Classify(new CampaignEventCandidate(
            CampaignEventType.Death, 10, 10, null, $"death/{rank}/{subrank}/{honours}", 3,
            new DeathPayload(null, DeathDisposition.NonBattleProcedural, null, null, null, null,
                null, "Role", rank, 0, 0, null, null, false, true,
                SoldierSubrank: subrank, HadTerminatorHonours: honours),
            reasonHint: hint));

    private static CampaignEventCandidate LeaderCandidate(bool actual, bool deployable, string key) =>
        new(CampaignEventType.SquadLeaderUnavailable, 1, 1, null, key, 1,
            new SquadLeaderUnavailablePayload(4, "Third Squad", 2, 1, actual, deployable, null));
}
