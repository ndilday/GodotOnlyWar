using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OnlyWar.Generation.World;
using OnlyWar.Domain;
using OnlyWar.Domain.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Data;

// Regression guard for the new-game save FK failure: a freshly generated chapter must be
// savable through the same aggregate mapping used by CurrentCampaignSaveWriter, without test-only
// registration of the army root on the player faction. Generation itself must register the
// OrderOfBattle root on Faction.Units, otherwise the save writes no Soldier rows and the first
// PlayerSoldier insert violates PlayerSoldier.SoldierId -> Soldier.Id.
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class NewGameSaveTests : IClassFixture<NewGameSaveFixture>
{
    private readonly GameRulesData _data;
    private readonly Date _date;
    private readonly GameStateRoundTripFixture _roundTrip;
    private readonly Sector _sector;

    public NewGameSaveTests(NewGameSaveFixture fixture)
    {
        _data = fixture.Data;
        _date = fixture.Date;
        _roundTrip = fixture.RoundTrip;
        _sector = fixture.Sector;
    }

    [Fact]
    public void NewGame_SavesThroughProductionPath_WithoutManualRegistration()
    {
        // The production-path save below depends on generation registering the root unit. Keep the
        // direct assertion here rather than generating a second sector in a registration-only test.
        Assert.Contains(_sector.PlayerForce.Army.OrderOfBattle, _data.PlayerFaction.Units);

        // Match CurrentCampaignSaveWriter's unit selection: no manual Units.Add.
        List<Unit> units = _data.Factions.SelectMany(f => f.Units).ToList();

        string dbPath = GameStateRoundTripFixture.CreateTempDbPath("onlywar_newgame");
        try
        {
            _roundTrip.Save(_sector, dbPath, units);

            // Sanity: the player's soldiers actually made it into the file.
            Assert.True(File.Exists(dbPath));
            long soldierCount = GameStateRoundTripFixture.CountRows(dbPath, "Soldier");
            Assert.True(soldierCount > 0, "expected Soldier rows to be written");
        }
        finally
        {
            GameStateRoundTripFixture.CleanupDb(dbPath);
        }
    }

    // The opening Chronicle of a real new game (user reports, 2026-11-19). It is here, not in a
    // narrative test class, so it reads this fixture's sector instead of generating a second one.
    // The founding myth is the first entry and the founding the second
    // (TDD §6.9); first contacts seen in the generation warm-up follow
    // them, in the founding week, with their own title; the Imperium is never a first contact; and
    // no entry recalls an intel report or the Chapter Master's history.
    [Fact]
    public void NewGame_ChronicleOpensWithTheFoundingAndItsFirstContacts()
    {
        IReadOnlyList<OnlyWar.Domain.Events.ChapterChronicleEntry> entries =
            _sector.PlayerForce.ChapterChronicle.Entries.OrderBy(entry => entry.Id).ToList();
        Assert.True(entries.Count >= 2);
        Assert.StartsWith("The Founding of the ", entries[0].Title);
        OnlyWar.Domain.Events.ChapterChronicleEntry founding = entries[1];
        Assert.EndsWith("is Founded", founding.Title);

        List<OnlyWar.Domain.Events.ChapterChronicleEntry> firstContacts = entries
            .Where(entry => entry.Title.StartsWith("First Contact with the ", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(firstContacts);
        Assert.All(firstContacts, entry => Assert.Equal(founding.OccurredWeek, entry.OccurredWeek));
        Assert.DoesNotContain(entries, entry => entry.Title.Contains(_data.DefaultFaction.Name, StringComparison.Ordinal));
        Assert.DoesNotContain(entries, entry => entry.Title == "Campaign Service Record");
        Assert.DoesNotContain(entries, entry => entry.Body.Contains("recalled an earlier entry", StringComparison.Ordinal));
    }

    // TDD §6.9: on 500.M39 the chapter belongs to the 24th
    // Founding of 496.M39. The myth is dated at the decree, recorded on the first turn, recorded
    // before ChapterFounded, and stands alone in the Chronicle as a defining Chapter entry.
    [Fact]
    public void NewGame_RecordsTheFoundingMythBeforeTheFounding()
    {
        OnlyWar.Domain.Events.CampaignEventLedger ledger = _sector.PlayerForce.CampaignEventLedger;
        OnlyWar.Domain.Events.CampaignEvent myth = ledger.GetByDedupeKey(
            OnlyWar.Campaign.Events.CampaignEventRecorder.FoundingMythDedupeKey);
        OnlyWar.Domain.Events.CampaignEvent founded = ledger.Events.Single(
            @event => @event.Type == OnlyWar.Domain.Events.CampaignEventType.ChapterFounded);
        Assert.NotNull(myth);
        Assert.True(myth.Id < founded.Id);
        Assert.Equal(new Date(39, 496, 1).GetTotalWeeks(), myth.OccurredWeek);
        Assert.Equal(_date.GetTotalWeeks(), myth.RecordedWeek);

        var payload = Assert.IsType<OnlyWar.Domain.Events.ChapterFoundingMythPayload>(myth.Payload);
        Assert.Equal(24, payload.FoundingNumber);
        Assert.Equal(38496, payload.FoundingYear);
        Assert.Equal(_sector.PlayerForce.Army.PlayerSoldierMap.Count, payload.FounderCount);
        Assert.DoesNotContain(_sector.Planets.Values, planet => planet.Name == payload.AspirantWorldName);

        OnlyWar.Domain.Events.ChapterChronicleEntry entry = _sector.PlayerForce.ChapterChronicle
            .GetByDedupeKey($"chronicle/event/{myth.Id}");
        Assert.NotNull(entry);
        Assert.Equal(payload.Title, entry.Title);
        Assert.Equal(payload.Text, entry.Body);
        Assert.Equal(OnlyWar.Domain.Events.CampaignEventImportance.Defining, entry.Importance);
        Assert.Contains(OnlyWar.Domain.Events.ChapterChronicleCategory.Chapter, entry.Categories);
        Assert.Contains("of uncertain lineage", entry.Body);
        Assert.Contains("Twenty-Fourth Founding", entry.Body);
        Assert.Contains("496.M39", entry.Body);
    }

    // D5: on the first turn the Founding Directive card links to the myth's Chronicle entry, and
    // that entry is on a page the Command screen can open.
    [Fact]
    public void NewGame_FoundingDirectiveLinksToTheFoundingMyth()
    {
        OnlyWar.Application.Abstractions.CommandBriefModel brief = new OnlyWar.Campaign.Command.CommandBriefBuilder()
            .Build(_date, _sector, _data, null);
        OnlyWar.Application.Abstractions.CommandBriefItem directive = brief.Items.Single(
            item => item.StableKey == "scenario/founding-directive");
        OnlyWar.Application.Abstractions.CommandBriefRelatedLink link = Assert.Single(directive.RelatedLinks,
            related => related.Target.Kind == OnlyWar.Application.Abstractions.CampaignNavigationTargetKind.ChronicleEntry);
        OnlyWar.Domain.Events.ChapterChronicleEntry entry = _sector.PlayerForce.ChapterChronicle
            .Entries.Single(candidate => candidate.Id == link.Target.PrimaryId);
        Assert.StartsWith("The Founding of the ", entry.Title);
        Assert.True(OnlyWar.Campaign.Command.ChapterChronicleBrowser.FindPage(
            _sector.PlayerForce.ChapterChronicle, entry.Id) >= 0);

        // After the first turn the card no longer links to it.
        OnlyWar.Application.Abstractions.CommandBriefModel later = new OnlyWar.Campaign.Command.CommandBriefBuilder()
            .Build(Date.FromTotalWeeks(_date.GetTotalWeeks() + 1), _sector, _data, null);
        Assert.DoesNotContain(
            later.Items.Single(item => item.StableKey == "scenario/founding-directive").RelatedLinks,
            related => related.Target.Kind == OnlyWar.Application.Abstractions.CampaignNavigationTargetKind.ChronicleEntry);
    }

    // D9: the myth's text and attributes are frozen in the event payload and survive a save.
    [Fact]
    public void NewGame_FoundingMythSurvivesSaveAndLoad()
    {
        var original = Assert.IsType<OnlyWar.Domain.Events.ChapterFoundingMythPayload>(
            _sector.PlayerForce.CampaignEventLedger.GetByDedupeKey(
                OnlyWar.Campaign.Events.CampaignEventRecorder.FoundingMythDedupeKey).Payload);
        List<Unit> units = _data.Factions.SelectMany(f => f.Units).ToList();
        string dbPath = GameStateRoundTripFixture.CreateTempDbPath("onlywar_foundingmyth");
        try
        {
            _roundTrip.Save(_sector, dbPath, units);
            var loaded = _roundTrip.Load(dbPath);
            var restored = Assert.IsType<OnlyWar.Domain.Events.ChapterFoundingMythPayload>(
                loaded.CampaignEventLedger.GetByDedupeKey(
                    OnlyWar.Campaign.Events.CampaignEventRecorder.FoundingMythDedupeKey).Payload);
            Assert.Equal(original, restored);
            Assert.Contains(loaded.ChapterChronicle.Entries, entry => entry.Body == original.Text);
        }
        finally
        {
            GameStateRoundTripFixture.CleanupDb(dbPath);
        }
    }

    [Fact]
    public void FailedSave_LeavesPreviousSaveIntact_AndNoTempFiles()
    {
        List<Unit> units = _data.Factions.SelectMany(f => f.Units).ToList();

        string dir = Path.Combine(Path.GetTempPath(), $"onlywar_atomic_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string dbPath = Path.Combine(dir, "save.s3db");
        try
        {
            // First, a good save.
            _roundTrip.Save(_sector, dbPath, units);
            long originalSoldiers = GameStateRoundTripFixture.CountRows(dbPath, "Soldier");
            Assert.True(originalSoldiers > 0);

            // Now a save guaranteed to fail mid-write: duplicating the root unit forces a
            // primary-key violation on the Unit insert, after the schema is created.
            List<Unit> corruptUnits = units.Concat(units).ToList();
            Assert.ThrowsAny<Exception>(() => _roundTrip.Save(_sector, dbPath, corruptUnits));

            // The prior good save must survive untouched.
            Assert.True(File.Exists(dbPath), "previous save file was destroyed by a failed save");
            Assert.Equal(originalSoldiers, GameStateRoundTripFixture.CountRows(dbPath, "Soldier"));

            // No temp scratch files should be left behind.
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
            Assert.Single(Directory.GetFiles(dir));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}

public sealed class NewGameSaveFixture
{
    internal GameRulesData Data { get; }
    internal Date Date { get; } = new(39, 500, 1);
    internal GameStateRoundTripFixture RoundTrip { get; }
    internal Sector Sector { get; }

    public NewGameSaveFixture()
    {
        Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
        Data = OnlyWar.Persistence.Database.GameRules.GameRulesLoader.Load(OnlyWar.Tests.Fixtures.RulesDatabaseFixture.DatabasePath);
        Sector = TestGeneration.GenerateSector(1, Data, Date, "New Game Save Fixture Chapter");
        RoundTrip = new GameStateRoundTripFixture(Data, Date);
    }
}
