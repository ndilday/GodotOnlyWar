using OnlyWar.Helpers;
using OnlyWar.Helpers.Command;
using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Models;
using OnlyWar.Models.Command;
using OnlyWar.Models.Events;
using OnlyWar.Models.Soldiers;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace OnlyWar.Tests.Domain;

public sealed class CampaignEventSpineTests
{
    [Fact]
    public void Recorder_DeduplicatesAndProjectsOnlyOnce()
    {
        PlayerSoldier soldier = CreateSoldier(7, "Brother Acastus");
        CampaignEventLedger ledger = new(id => id == soldier.Id ? soldier : null);
        TurnEventBuffer buffer = new();
        CampaignEventRecorder recorder = new(ledger, turnBuffer: buffer, soldierResolver: id => id == soldier.Id ? soldier : null);
        CampaignEventCandidate candidate = new(
            CampaignEventType.FirstBlood,
            100,
            100,
            "battle/100/1",
            "career/first-blood/7",
            1,
            new FirstBloodPayload(1, 42, 9, "A gaunt"),
            [new CampaignEventEntityRef(CampaignEntityKind.Soldier, 7, CampaignEventEntityRole.Subject, soldier.Name)],
            chronicleTreatmentHint: CampaignEventChronicleTreatment.GroupWithCorrelation);

        CampaignEvent first = recorder.Record(candidate);
        CampaignEvent replay = recorder.Record(candidate);

        Assert.Same(first, replay);
        Assert.Single(ledger.Events);
        Assert.Single(buffer);
        Assert.Single(soldier.SoldierEvents);
        Assert.Equal(first.Id, soldier.SoldierEvents[0].CampaignEventId);
    }

    [Fact]
    public void Recorder_EmitsEveryNewlyCrossedMilestoneInOrder()
    {
        PlayerSoldier soldier = CreateSoldier(8, "Brother Varro");
        CampaignEventLedger ledger = new(id => id == soldier.Id ? soldier : null);
        CampaignEventRecorder recorder = new(ledger, soldierResolver: id => id == soldier.Id ? soldier : null);

        IReadOnlyList<CampaignEvent> events = recorder.RecordKillMilestones(
            soldier, 24, 51, 12, 44, 100, "battle/100/2");

        Assert.Equal(
            new[] { CampaignEventType.KillMilestone, CampaignEventType.KillMilestone },
            events.Select(@event => @event.Type));
        Assert.Equal(new[] { 25, 50 }, events.Select(@event => ((KillMilestonePayload)@event.Payload).Threshold));
        Assert.Equal(2, soldier.SoldierEvents.Count);

        Assert.Empty(recorder.RecordKillMilestones(
            soldier, 24, 51, 12, 44, 100, "battle/100/2"));
        Assert.Equal(2, ledger.Events.Count);
    }

    [Fact]
    public void ChronicleComposer_GroupsCorrelatedEventsAndKeepsContributorIds()
    {
        CampaignIdentity identity = new(System.Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"), 17);
        CampaignEvent first = new(
            1,
            CampaignEventType.FirstBlood,
            10,
            10,
            "battle/10/1",
            "career/first-blood/1",
            1,
            new FirstBloodPayload(1, 2, 3, "A gaunt"),
            [new CampaignEventEntityRef(CampaignEntityKind.Soldier, 1, CampaignEventEntityRole.Subject, "Brother A")],
            new CampaignEventPublication(
                CampaignEventSurfaceFlags.ChapterChronicle,
                CampaignEventImportance.Notable,
                CampaignEventReasonFlags.FirstBlood,
                CampaignEventChronicleTreatment.GroupWithCorrelation,
                1));
        CampaignEvent second = new(
            2,
            CampaignEventType.KillMilestone,
            10,
            10,
            "battle/10/1",
            "career/kill-milestone/1/10",
            1,
            new KillMilestonePayload(10, 9, 10, 2, 3),
            [new CampaignEventEntityRef(CampaignEntityKind.Soldier, 1, CampaignEventEntityRole.Subject, "Brother A")],
            new CampaignEventPublication(
                CampaignEventSurfaceFlags.ChapterChronicle,
                CampaignEventImportance.Notable,
                CampaignEventReasonFlags.KillMilestone,
                CampaignEventChronicleTreatment.GroupWithCorrelation,
                1));

        ChapterChronicleEntry entry = new ChapterChronicleComposer(identity).Compose(
            1, [second, first], "chronicle/correlation/battle/10/1");

        Assert.Equal(new long[] { 1, 2 }, entry.CampaignEventIds);
        Assert.Contains("First Blood", entry.Body);
        Assert.Contains("10 confirmed kills", entry.Body);
    }

    [Fact]
    public void Recorder_ComposesStandaloneFoundingEntryImmediatelyAndOnlyOnce()
    {
        CampaignEventLedger ledger = new();
        ChapterChronicleLedger chronicle = new();
        CampaignEventRecorder recorder = new(ledger, chronicle: chronicle);
        Date date = Date.FromTotalWeeks(4);

        CampaignEvent first = recorder.RecordChapterFounded(
            date,
            new ChapterFoundedPayload(
                "Test Chapter",
                4,
                null,
                "Unknown Chapter Master",
                10,
                "The Sector Lord",
                "Hold the promised world.",
                17,
                "Vigilus"),
            null,
            null,
            17,
            "Vigilus");

        Assert.Single(chronicle.Entries);
        Assert.Equal(first.Id, Assert.Single(chronicle.Entries).CampaignEventIds.Single());
        Assert.Contains("Test Chapter", chronicle.Entries[0].Title);

        recorder.RecordChapterFounded(
            date,
            (ChapterFoundedPayload)first.Payload,
            null,
            null,
            17,
            "Vigilus");

        Assert.Single(chronicle.Entries);
    }

    [Fact]
    public void Projector_LeavesRoutineBattleAloneAndGroupsQualifyingFactsWhenAnchorArrives()
    {
        CampaignEventLedger ledger = new();
        ChapterChronicleLedger chronicle = new();
        CampaignEventRecorder recorder = new(ledger, chronicle: chronicle);
        CampaignEventCandidate firstBlood = new(
            CampaignEventType.FirstBlood,
            10,
            10,
            "battle/10/1",
            "career/first-blood/1",
            1,
            new FirstBloodPayload(1, 2, 3, "A gaunt"),
            [new CampaignEventEntityRef(
                CampaignEntityKind.Soldier,
                1,
                CampaignEventEntityRole.Subject,
                "Brother A")]);

        recorder.Record(firstBlood);

        Assert.Empty(chronicle.Entries);

        recorder.Record(new CampaignEventCandidate(
            CampaignEventType.BattleResolved,
            10,
            10,
            "battle/10/1",
            "battle/resolved/10/1",
            1,
            new BattleResolvedPayload("A routine battle", "The line held.")));

        Assert.Single(chronicle.Entries);
        Assert.Equal(
            new long[] { 1, 2 },
            chronicle.Entries[0].CampaignEventIds);
        Assert.Equal(1, chronicle.GetCategoryCount(ChapterChronicleCategory.Battles));
        Assert.Single(ChapterChronicleBrowser.GetPage(
            chronicle,
            ledger,
            null,
            ChronicleFilter.Battles,
            0));
        Assert.Empty(ChapterChronicleBrowser.GetPage(
            chronicle,
            ledger,
            null,
            ChronicleFilter.Battles,
            1));

        CampaignEventLedger routineLedger = new();
        ChapterChronicleLedger routineChronicle = new();
        CampaignEventRecorder routineRecorder = new(routineLedger, chronicle: routineChronicle);
        routineRecorder.Record(new CampaignEventCandidate(
            CampaignEventType.BattleResolved,
            11,
            11,
            "battle/11/1",
            "battle/resolved/11/1",
            1,
            new BattleResolvedPayload("Another routine battle", "No defining event.")));

        Assert.Empty(routineChronicle.Entries);
    }

    private static PlayerSoldier CreateSoldier(int id, string name)
    {
        Soldier soldier = new(new List<HitLocation>(), new List<Skill>()) { Id = id };
        return new PlayerSoldier(soldier, name);
    }
}
