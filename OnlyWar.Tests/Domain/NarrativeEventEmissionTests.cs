using Microsoft.Data.Sqlite;
using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Models;
using OnlyWar.Models.Events;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace OnlyWar.Tests.Domain;

public sealed class NarrativeEventEmissionTests
{
    [Fact]
    public void NearDeathProjection_OpensFromTypedIncapacitationAndClosesOnceOnRecovery()
    {
        PlayerSoldier soldier = CreateSoldier(11, "Brother Orestes");
        CampaignEventLedger ledger = new(id => id == soldier.Id ? soldier : null);
        CampaignEventRecorder recorder = new(
            ledger,
            soldierResolver: id => id == soldier.Id ? soldier : null);
        BattleEventContextSnapshot context = Context("battle/10/3/8/0", 10);
        CampaignEventEntityRef subject = new(
            CampaignEntityKind.Soldier,
            soldier.Id,
            CampaignEventEntityRole.Subject,
            soldier.Name);

        CampaignEvent incapacitation = recorder.Record(new CampaignEventCandidate(
            CampaignEventType.Incapacitated,
            10,
            10,
            context.CorrelationKey,
            "battle/incapacitated/11/10",
            3,
            new IncapacitatedPayload(
                context,
                77,
                "Heart",
                DefiningLocationIsVital: true,
                DefiningLocationWasCrippled: true,
                DefiningLocationWasSevered: false,
                QualifiesAsNearDeath: true,
                null,
                null,
                null,
                null,
                null),
            [subject]));

        OpenNearDeathEpisode open = ledger.GetOpenNearDeathEpisode(soldier.Id);
        Assert.NotNull(open);
        Assert.Equal(incapacitation.Id, open.SourceIncapacitationEventId);
        Assert.IsType<IncapacitatedPayload>(CampaignEventPayloadRegistry.Deserialize(
            incapacitation.Id,
            incapacitation.Type,
            incapacitation.PayloadVersion,
            CampaignEventPayloadRegistry.Serialize(incapacitation)));

        CampaignEvent recovery = recorder.RecordNearDeathRecovery(
            soldier,
            Date.FromTotalWeeks(18),
            new NearDeathRecoveryPayload(
                incapacitation.Id,
                8,
                77,
                "Heart",
                NearDeathRecoveryMethod.NaturalOrFieldCare,
                true,
                context));

        Assert.Equal(18, recovery.OccurredWeek);
        Assert.Null(ledger.GetOpenNearDeathEpisode(soldier.Id));
        Assert.Same(
            recovery,
            recorder.RecordNearDeathRecovery(
                soldier,
                Date.FromTotalWeeks(18),
                (NearDeathRecoveryPayload)recovery.Payload));
        Assert.Equal(1, ledger.Events.Count(@event => @event.Type == CampaignEventType.NearDeathRecovery));
        Assert.Contains("returned to deployability", soldier.SoldierEvents.Last().Detail);
    }

    [Fact]
    public void MedicalAndMentorRecordsUseFactualLinesAndCompletionDates()
    {
        PlayerSoldier mentee = CreateSoldier(12, "Brother Æthelred");
        CampaignEventLedger ledger = new(id => id == mentee.Id ? mentee : null);
        CampaignEventRecorder recorder = new(
            ledger,
            soldierResolver: id => id == mentee.Id ? mentee : null);
        BattleEventContextSnapshot context = Context("battle/4/1/2/0", 4);

        CampaignEvent replacement = recorder.RecordBodyPartReplacement(
            mentee,
            Date.FromTotalWeeks(19),
            new BodyPartReplacementPayload(
                77,
                "Left Arm",
                MedicalProcedureType.Cybernetic,
                false,
                4,
                40,
                null),
            context);
        Assert.Equal(19, replacement.OccurredWeek);
        Assert.Contains("cybernetic", replacement.Payload is BodyPartReplacementPayload
            ? mentee.SoldierEvents.Last().Detail
            : string.Empty);
        Assert.DoesNotContain("BodyPartReplacement", mentee.SoldierEvents.Last().Detail);

        PlayerSoldier mentor = CreateSoldier(13, "Scout Sergeant Cael");
        CampaignEvent mentorEvent = recorder.RecordMentorAssigned(
            mentee,
            Date.FromTotalWeeks(20),
            new MentorAssignedPayload(
                MentorRelationshipKind.ScoutMentor,
                MentorAssignmentContext.NeophytePlacement,
                900,
                "Scout Squad IX",
                mentor.Id,
                mentor.Name));

        Assert.Contains(mentor.Name, mentorEvent.Payload is MentorAssignedPayload
            ? mentee.SoldierEvents.Last().Detail
            : string.Empty);
        Assert.Contains("Scout Squad IX", mentee.SoldierEvents.Last().Detail);
        Assert.DoesNotContain("MentorAssigned", mentee.SoldierEvents.Last().Detail);
    }

    [Fact]
    public void GeneSeedRecoveryReferencesDeathAndRetainsBattleCorrelation()
    {
        PlayerSoldier soldier = CreateSoldier(14, "Brother Hanno");
        CampaignEventLedger ledger = new(id => id == soldier.Id ? soldier : null);
        CampaignEventRecorder recorder = new(
            ledger,
            soldierResolver: id => id == soldier.Id ? soldier : null);
        BattleEventContextSnapshot context = Context("battle/22/4/9/0", 22);
        DeathPayload deathPayload = new(
            context,
            DeathDisposition.BodyRecovered,
            41,
            "The enemy",
            null,
            null,
            null,
            null,
            null,
            3,
            27,
            null,
            null,
            false,
            true);

        CampaignEvent death = recorder.RecordDeath(soldier, Date.FromTotalWeeks(22), deathPayload);
        CampaignEvent geneSeed = recorder.RecordGeneseedRecovery(
            soldier,
            Date.FromTotalWeeks(22),
            new GeneseedRecoveryPayload(
                context,
                death.Id,
                GeneseedRecoveryOutcome.Recovered,
                0.91f));

        Assert.Equal(death.CorrelationKey, geneSeed.CorrelationKey);
        Assert.Equal(death.Id, ((GeneseedRecoveryPayload)geneSeed.Payload).SourceDeathEventId);
        Assert.IsType<GeneseedRecoveryPayload>(CampaignEventPayloadRegistry.Deserialize(
            geneSeed.Id,
            geneSeed.Type,
            geneSeed.PayloadVersion,
            CampaignEventPayloadRegistry.Serialize(geneSeed)));
    }

    [Fact]
    public void CampaignEventDataAccess_RoundTripsTypedPayloadsEntitiesAndProjection()
    {
        PlayerSoldier soldier = CreateSoldier(16, "Brother Æthelstan");
        CampaignEventLedger ledger = new(id => id == soldier.Id ? soldier : null);
        CampaignEventRecorder recorder = new(
            ledger,
            soldierResolver: id => id == soldier.Id ? soldier : null);
        BattleEventContextSnapshot context = Context("battle/30/5/6/0", 30);
        CampaignEventEntityRef subject = new(
            CampaignEntityKind.Soldier,
            soldier.Id,
            CampaignEventEntityRole.Subject,
            soldier.Name);
        CampaignEvent source = recorder.Record(new CampaignEventCandidate(
            CampaignEventType.Incapacitated,
            30,
            30,
            context.CorrelationKey,
            "save/incapacitated/16",
            3,
            new IncapacitatedPayload(
                context,
                77,
                "Heart",
                true,
                true,
                false,
                true,
                null,
                null,
                null,
                null,
                null),
            [subject]));
        recorder.RecordNearDeathRecovery(
            soldier,
            Date.FromTotalWeeks(35),
            new NearDeathRecoveryPayload(
                source.Id,
                5,
                77,
                "Heart",
                NearDeathRecoveryMethod.Cybernetic,
                false,
                context));
        recorder.RecordBodyPartReplacement(
            soldier,
            Date.FromTotalWeeks(34),
            new BodyPartReplacementPayload(
                77,
                "Left Arm",
                MedicalProcedureType.Cybernetic,
                false,
                4,
                40,
                source.Id),
            context);

        using SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();
        using (SqliteCommand schema = connection.CreateCommand())
        {
            schema.CommandText = @"
                CREATE TABLE CampaignEvent (
                    Id INTEGER PRIMARY KEY,
                    EventType INTEGER NOT NULL,
                    OccurredWeek INTEGER NOT NULL,
                    RecordedWeek INTEGER NOT NULL,
                    CorrelationKey TEXT,
                    DedupeKey TEXT NOT NULL UNIQUE,
                    PayloadVersion INTEGER NOT NULL,
                    PayloadJson TEXT NOT NULL);
                CREATE TABLE CampaignEventEntity (
                    CampaignEventId INTEGER NOT NULL,
                    EntityKind INTEGER NOT NULL,
                    EntityId INTEGER NOT NULL,
                    EntityRole INTEGER NOT NULL,
                    DisplayName TEXT NOT NULL,
                    SortOrder INTEGER NOT NULL,
                    PRIMARY KEY (CampaignEventId, EntityKind, EntityId, EntityRole));
                CREATE TABLE CampaignEventPublication (
                    CampaignEventId INTEGER PRIMARY KEY,
                    PublishServiceRecord BOOLEAN NOT NULL,
                    PublishTurnReport BOOLEAN NOT NULL,
                    PublishChapterChronicle BOOLEAN NOT NULL,
                    Importance INTEGER NOT NULL,
                    ReasonFlags INTEGER NOT NULL,
                    ChronicleTreatment INTEGER NOT NULL,
                    ClassifierVersion INTEGER NOT NULL);";
            schema.ExecuteNonQuery();
        }

        CampaignEventDataAccess dataAccess = new();
        using (SqliteTransaction transaction = connection.BeginTransaction())
        {
            dataAccess.SaveLedger(transaction, ledger);
            transaction.Commit();
        }

        CampaignEventLedger loaded = dataAccess.GetLedger(
            connection,
            id => id == soldier.Id ? soldier : null);

        Assert.Equal(ledger.Count, loaded.Count);
        Assert.Equal("Æther", loaded.Events.SelectMany(@event => @event.Entities)
            .First(entity => entity.Kind == CampaignEntityKind.Planet)
            .DisplayNameSnapshot);
        Assert.Null(loaded.GetOpenNearDeathEpisode(soldier.Id));
        Assert.Equal(
            new[] { CampaignEventType.Incapacitated, CampaignEventType.NearDeathRecovery, CampaignEventType.BodyPartReplacement },
            loaded.Events.Select(@event => @event.Type));
    }

    [Fact]
    public void InvalidNearDeathFactsAndSourceCorrelationsAreRejected()
    {
        BattleEventContextSnapshot first = Context("battle/1", 1);
        BattleEventContextSnapshot second = Context("battle/2", 2);
        CampaignEventEntityRef subject = new(
            CampaignEntityKind.Soldier,
            15,
            CampaignEventEntityRole.Subject,
            "Brother Ivo");

        Assert.Throws<ArgumentException>(() => new CampaignEventCandidate(
            CampaignEventType.Incapacitated,
            1,
            1,
            first.CorrelationKey,
            "invalid/near-death",
            3,
            new IncapacitatedPayload(
                first,
                77,
                "Hand",
                true,
                true,
                true,
                true,
                null,
                null,
                null,
                null,
                null),
            [subject]));

        PlayerSoldier soldier = CreateSoldier(15, "Brother Ivo");
        CampaignEventLedger ledger = new(id => id == soldier.Id ? soldier : null);
        CampaignEventRecorder recorder = new(
            ledger,
            soldierResolver: id => id == soldier.Id ? soldier : null);
        CampaignEvent source = recorder.Record(new CampaignEventCandidate(
            CampaignEventType.Incapacitated,
            1,
            1,
            first.CorrelationKey,
            "source/incapacitated",
            3,
            new IncapacitatedPayload(
                first,
                77,
                "Heart",
                true,
                true,
                false,
                true,
                null,
                null,
                null,
                null,
                null),
            [subject]));

        Assert.Throws<InvalidDataException>(() => recorder.RecordNearDeathRecovery(
            soldier,
            Date.FromTotalWeeks(2),
            new NearDeathRecoveryPayload(
                source.Id,
                1,
                77,
                "Heart",
                NearDeathRecoveryMethod.NaturalOrFieldCare,
                false,
                second)));
    }

    private static BattleEventContextSnapshot Context(string correlation, int week) =>
        new(
            correlation,
            week,
            RegionId: 3,
            RegionName: "Aquila Reach",
            PlanetId: 4,
            PlanetName: "Æther",
            OpposingFactionId: 41,
            OpposingFactionName: "The enemy");

    private static PlayerSoldier CreateSoldier(int id, string name)
    {
        Soldier soldier = new(new List<HitLocation>(), new List<Skill>()) { Id = id };
        return new PlayerSoldier(soldier, name);
    }
}
