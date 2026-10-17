using Microsoft.Data.Sqlite;
using OnlyWar.Application;
using OnlyWar.Campaign.Events;
using OnlyWar.Campaign.Turns;
using OnlyWar.Domain;
using OnlyWar.Domain.Events;
using OnlyWar.Domain.FactionBehaviors;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;
using OnlyWar.Persistence.Database.GameState;
using OnlyWar.Tests.Fixtures;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace OnlyWar.Tests.Turns;

/// <summary>
/// Sector dispatches (OnlyWar_TDD.md §6.1): the aide reports public changes in the
/// sector once per episode, and says what part the Chapter took.
/// </summary>
public sealed class SectorDispatchTests
{
    private static readonly IReadOnlySet<int> NoChapterWorlds = new HashSet<int>();

    [Fact]
    public void Danger_OpensAtOneThird_DoesNotRepeat_AndClosesBelowOneSixth()
    {
        SectorDispatchTracker tracker = new();
        tracker.Seed(1, 0, 16, isImperial: true, revoltActive: false);

        Assert.False(tracker.Observe(1, 5, 16, true, false, false).DangerOpened);
        Assert.True(tracker.Observe(1, 6, 16, true, false, false).DangerOpened);
        // Still in danger at 3 of 16: the episode stays open and reports nothing.
        Assert.False(tracker.Observe(1, 3, 16, true, false, false).DangerOpened);
        Assert.False(tracker.Observe(1, 6, 16, true, false, false).DangerOpened);
        // 2 of 16 is below one sixth, so the episode closes; the next crossing reports again.
        Assert.False(tracker.Observe(1, 2, 16, true, false, false).DangerOpened);
        Assert.True(tracker.Observe(1, 6, 16, true, false, false).DangerOpened);
    }

    [Fact]
    public void Tracker_FirstObservationOnlyRecords()
    {
        SectorDispatchTracker tracker = new();

        Assert.Equal(default(SectorDispatchPlanetChange), tracker.Observe(1, 16, 16, true, true, true));
        Assert.Null(tracker.ObserveSectorLord(4, 1));
        Assert.True(Assert.Single(tracker.States).InDanger);
    }

    [Fact]
    public void Revolt_RemembersChapterParticipationUntilItEnds()
    {
        SectorDispatchTracker tracker = new();
        tracker.Seed(1, 0, 16, true, revoltActive: true);

        Assert.False(tracker.Observe(1, 0, 16, true, true, chapterParticipatedThisWeek: true).RevoltEnded);
        SectorDispatchPlanetChange ended = tracker.Observe(1, 0, 16, true, false, false);

        Assert.True(ended.RevoltEnded);
        Assert.True(ended.RevoltChapterParticipated);
        Assert.False(Assert.Single(tracker.States).RevoltChapterParticipated);
    }

    [Fact]
    public void Processor_ReportsAWorldInDangerOnce()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        SectorDispatchProcessor processor = CreateProcessor(fixture);
        SectorDispatchSnapshot start = processor.BeginTurn();
        for (int region = 10; region < 16; region++)
        {
            fixture.AddControllingFaction(region, $"Raiders {region}", 500);
        }

        SectorDispatchPayload danger = Assert.IsType<SectorDispatchPayload>(
            Assert.Single(processor.EndTurn(start, NoChapterWorlds, 2)).Payload);
        Assert.Equal(SectorDispatchKind.WorldInDanger, danger.Kind);
        Assert.Equal(6, danger.HostileRegionCount);
        Assert.Equal(16, danger.RegionCount);
        Assert.Equal(SectorDispatchChapterPart.Absent, danger.ChapterPart);

        Assert.Empty(processor.EndTurn(processor.BeginTurn(), NoChapterWorlds, 3));
    }

    [Fact]
    public void Processor_ReportsACrushedRevoltWithTheChaptersEarlierPart()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        // The rebels add population to region 4; keep the capital where the Imperium holds it.
        fixture.Planet.SetCapitalRegion(0);
        RegionFaction rebels = fixture.AddHiddenFaction(4, GrowthType.Unrest, 300);
        rebels.IsPublic = true;
        rebels.PlanetFaction.IsPublic = true;
        SectorDispatchProcessor processor = CreateProcessor(fixture, rebels.PlanetFaction.Faction);
        HashSet<int> chapterOnWorld = [fixture.Planet.Id];

        Assert.Empty(processor.EndTurn(processor.BeginTurn(), chapterOnWorld, 2));
        SectorDispatchSnapshot start = processor.BeginTurn();
        rebels.Population = 0;
        rebels.IsPublic = false;
        CampaignEvent dispatch = Assert.Single(processor.EndTurn(start, NoChapterWorlds, 3));

        SectorDispatchPayload payload = Assert.IsType<SectorDispatchPayload>(dispatch.Payload);
        Assert.Equal(SectorDispatchKind.RevoltCrushed, payload.Kind);
        Assert.Equal(SectorDispatchChapterPart.TookPart, payload.ChapterPart);
    }

    [Fact]
    public void Processor_ReportsASwarmOnlyWhereItHadNoPublicPresence()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        SectorDispatchProcessor processor = CreateProcessor(fixture);
        SectorDispatchSnapshot start = processor.BeginTurn();
        fixture.AddConsumptionFaction(2, population: 200, organization: 100);

        SectorDispatchPayload swarm = Assert.IsType<SectorDispatchPayload>(
            Assert.Single(processor.EndTurn(start, NoChapterWorlds, 2)).Payload);
        Assert.Equal(SectorDispatchKind.SwarmReachedWorld, swarm.Kind);
        Assert.Equal("Tyranids", swarm.FactionName);

        Assert.Empty(processor.EndTurn(processor.BeginTurn(), NoChapterWorlds, 3));
    }

    [Fact]
    public void Processor_ReportsAnInvasionForceOncePerCommander()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        SectorDispatchProcessor processor = CreateProcessor(fixture);
        SectorDispatchSnapshot start = processor.BeginTurn();
        RegionFaction presence = fixture.AddControllingFaction(5, "Orks", 800);
        Squad command = new("Warlord's retinue", null, null, fixture.Identity);
        StrategicInvasionForce invasion = new(
            1, presence.PlanetFaction.Faction, command, presence.Region, fixture.Planet);
        presence.StrategicInvasionForceId = invasion.Id;
        invasion.TrackRegion(presence);
        fixture.Sector.AddStrategicInvasionForce(invasion);

        SectorDispatchPayload formed = Assert.IsType<SectorDispatchPayload>(
            Assert.Single(processor.EndTurn(start, NoChapterWorlds, 2)).Payload);
        Assert.Equal(SectorDispatchKind.InvasionForceFormed, formed.Kind);
        Assert.Equal("Orks", formed.FactionName);
        Assert.Equal(fixture.Planet.Id, formed.PlanetId);

        // The command squad is the identity: a later turn, or a merge that keeps the same
        // commander, reports nothing new.
        Assert.Empty(processor.EndTurn(processor.BeginTurn(), NoChapterWorlds, 3));
    }

    [Fact]
    public void Processor_ReportsALostWorldAsSecessionAndTakesItOutOfTheMainReport()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction rebels = fixture.AddHiddenFaction(4, GrowthType.Unrest, 300);
        Faction insurrectionists = rebels.PlanetFaction.Faction;
        SectorDispatchProcessor processor = CreateProcessor(fixture, insurrectionists);
        SectorDispatchSnapshot start = processor.BeginTurn();
        CampaignEvent lost = fixture.Sector.PlayerForce.GetCampaignEventRecorder().RecordWorldControlChanged(
            new WorldControlChangedPayload(fixture.Planet.Id, fixture.Planet.Name, fixture.Default.Id,
                fixture.Default.Id, insurrectionists.Id, 1, 2, false, false));

        CampaignEvent dispatch = Assert.Single(processor.EndTurn(start, NoChapterWorlds, 2));

        SectorDispatchPayload payload = Assert.IsType<SectorDispatchPayload>(dispatch.Payload);
        Assert.Equal(SectorDispatchKind.WorldLost, payload.Kind);
        Assert.True(payload.IsSecession);
        Assert.Equal(lost.Id, payload.SourceEventId);
        Assert.False(lost.Publication.PublishesToTurnReport);
        Assert.True(lost.Publication.PublishesToChapterChronicle);
        Assert.True(dispatch.Publication.PublishesToTurnReport);
        Assert.False(dispatch.Publication.PublishesToChapterChronicle);
        Assert.Equal("World Seceded", CampaignEventNarrator.RenderDispatchTitle(payload));
    }

    [Fact]
    public void Processor_DoesNotReportAWorldTheChapterHelpedSave()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        SectorDispatchProcessor processor = CreateProcessor(fixture);
        SectorDispatchSnapshot start = processor.BeginTurn();
        CampaignEvent saved = fixture.Sector.PlayerForce.GetCampaignEventRecorder().RecordWorldControlChanged(
            new WorldControlChangedPayload(fixture.Planet.Id, fixture.Planet.Name, fixture.Default.Id,
                fixture.Default.Id, fixture.Default.Id, 1, 2, true, true));

        Assert.Empty(processor.EndTurn(start, NoChapterWorlds, 2));
        Assert.True(saved.Publication.PublishesToTurnReport);
    }

    [Fact]
    public void Processor_ReportsANewSectorLord()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        fixture.Planet.GovernanceTier = GovernanceTier.SectorCapital;
        Character first = fixture.InstallGovernor(0.5f, 0.5f, 0.5f);
        first.Name = "Lord Aurel";
        SectorDispatchProcessor processor = CreateProcessor(fixture);
        SectorDispatchSnapshot start = processor.BeginTurn();
        Character successor = new() { Id = 2, Name = "Lady Vessa" };
        fixture.Sector.Characters.Add(successor);
        fixture.DefaultPlanetFaction.Leader = successor;

        CampaignEvent dispatch = Assert.Single(processor.EndTurn(start, NoChapterWorlds, 2));

        SectorDispatchPayload payload = Assert.IsType<SectorDispatchPayload>(dispatch.Payload);
        Assert.Equal(SectorDispatchKind.NewSectorLord, payload.Kind);
        Assert.Equal("Lord Aurel", payload.PreviousSectorLordName);
        Assert.Equal("Lady Vessa", payload.SectorLordName);
        Assert.False(payload.CapitalMoved);
        string text = CampaignEventNarrator.RenderDispatch(dispatch, payload);
        Assert.Contains("Lady Vessa", text);
        Assert.Contains("Lord Aurel", text);
    }

    [Fact]
    public void Processor_NamesChapterFormationsPresentInTheSubsectorButNotCommitted()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Unit company = new("Third Company", new UnitTemplate(
            1, "Company", true, new List<SquadTemplate> { TestModelFactory.SquadTemplate }, []));
        Squad squad = new("Tactical Squad", company, TestModelFactory.SquadTemplate);
        company.AddSquad(squad);
        PlayerSoldier brother = new(
            TestModelFactory.CreateSoldier(TestModelFactory.MarineTemplate, "Brother Castor"), "Brother Castor");
        squad.AddSquadMember(brother);
        squad.CurrentRegion = fixture.Planet.Regions[0];
        PlayerForce force = new(
            SectorSimulationFixture.BuildTestFaction(2, "Test Chapter", true, false),
            new Army("Test Army", null, "Test Chapter", company, [brother]),
            null);
        Sector sector = new(force, [], [fixture.Planet], []);
        sector.InitializeWarpNetwork([new Subsector("Varan Subsector", 1, [fixture.Planet], [])], []);
        SectorDispatchProcessor processor = new(sector, fixture.Default, null, [fixture.Default]);
        SectorDispatchSnapshot start = processor.BeginTurn();
        fixture.AddConsumptionFaction(2, population: 200, organization: 100);

        CampaignEvent dispatch = Assert.Single(processor.EndTurn(start, NoChapterWorlds, 2));

        SectorDispatchPayload payload = Assert.IsType<SectorDispatchPayload>(dispatch.Payload);
        Assert.Equal(SectorDispatchChapterPart.PresentNotEngaged, payload.ChapterPart);
        Assert.Equal(new[] { "Third Company" }, payload.PresentFormationNames);
        Assert.EndsWith(
            "Brothers of the Third Company stood in the Varan Subsector; they were not committed.",
            CampaignEventNarrator.RenderDispatch(dispatch, payload));
    }

    [Fact]
    public void TurnReport_PutsDispatchesInTheirOwnSectionAfterTheMainList()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        SectorDispatchProcessor processor = CreateProcessor(fixture);
        SectorDispatchSnapshot start = processor.BeginTurn();
        fixture.AddConsumptionFaction(2, population: 200, organization: 100);
        processor.EndTurn(start, NoChapterWorlds, 2);

        List<EndOfTurnReportEntry> entries = TurnReportProjector.BuildReportEntries(
            [], null, null, null, null,
            campaignEvents: fixture.Sector.PlayerForce.CurrentTurnEvents,
            campaignIdentity: fixture.Sector.PlayerForce.CampaignIdentity);

        EndOfTurnReportEntry entry = Assert.Single(entries);
        Assert.Equal(TurnReportSections.Dispatches, entry.Section);
        Assert.Equal("Swarm Sighted", entry.Title);
        Assert.Equal(fixture.Planet.Id, entry.PlanetId);
        Assert.StartsWith("My lord,", entry.Summary);
        Assert.Equal(TurnReportSections.Dispatches, Assert.Single(
            LastTurnReportSnapshotBuilder.BuildSnapshot(null, entries).Entries).Section);
    }

    [Fact]
    public void DispatchPayloadAndTrackerState_RoundTripThroughTheSave()
    {
        SectorDispatchPayload payload = new(
            SectorDispatchKind.WorldLost, 3, "Varan Prime", 1, "Varan Subsector", 4, "Orks",
            SectorDispatchChapterPart.PresentNotEngaged, ["Third Company"], SourceEventId: 9);
        CampaignEvent @event = new(1, CampaignEventType.SectorDispatch, 2, 2, null, "dispatch/1", 1,
            payload, [], CampaignEventPublication.ServiceRecordOnly());
        SectorDispatchPayload loadedPayload = Assert.IsType<SectorDispatchPayload>(
            CampaignEventPayloadRegistry.Deserialize(
                1, CampaignEventType.SectorDispatch, 1, CampaignEventPayloadRegistry.Serialize(@event)));
        Assert.Equal(payload.Kind, loadedPayload.Kind);
        Assert.Equal(new[] { "Third Company" }, loadedPayload.PresentFormationNames);
        Assert.Equal(9, loadedPayload.SourceEventId);

        string path = GameStateRoundTripFixture.CreateTempDbPath("sector_dispatch_state");
        try
        {
            using SqliteConnection connection = new(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                ForeignKeys = false,
                Pooling = false
            }.ToString());
            connection.Open();
            using (SqliteCommand schema = connection.CreateCommand())
            {
                schema.CommandText = File.ReadAllText(
                    Path.Combine(RulesDatabaseFixture.RepositoryRoot, "Database", "SaveStructure.sql"));
                schema.ExecuteNonQuery();
            }
            // The planet rows these states name are not written here.
            using (SqliteCommand pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA foreign_keys = OFF";
                pragma.ExecuteNonQuery();
            }
            SectorDispatchTracker tracker = new(
                [new SectorDispatchPlanetState(3, true, true, true), new SectorDispatchPlanetState(5, false, false, false)],
                new SectorLordState(null, 3));
            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                new SectorDispatchDataAccess().Save(transaction, tracker);
                transaction.Commit();
            }

            SectorDispatchDataAccess dataAccess = new();
            Assert.Equal(
                tracker.States.OrderBy(state => state.PlanetId),
                dataAccess.GetPlanetStates(connection));
            Assert.Equal(new SectorLordState(null, 3), dataAccess.GetSectorLord(connection));
        }
        finally
        {
            GameStateRoundTripFixture.CleanupDb(path);
        }
    }

    private static SectorDispatchProcessor CreateProcessor(
        SectorSimulationFixture fixture,
        Faction insurrectionists = null) =>
        new(fixture.Sector, fixture.Default, insurrectionists,
            fixture.Planet.PlanetFactionMap.Values.Select(planetFaction => planetFaction.Faction).ToList());
}
