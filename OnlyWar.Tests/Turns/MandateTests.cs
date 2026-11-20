using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using OnlyWar.Application;
using OnlyWar.Campaign.Mandates;
using OnlyWar.Campaign.Simulation;
using OnlyWar.Campaign.Turns;
using OnlyWar.Domain;
using OnlyWar.Domain.Events;
using OnlyWar.Domain.Mandates;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Units;
using OnlyWar.Persistence.Database.GameState;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Turns;

// Mandates, legacy and Chapter Master succession (PRD §4.25, TDD §6.15).
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class MandateTests
{
    private const float LordOpinion = 0.5f;
    private readonly GameRulesData _data;
    private readonly Date _date = new(39, 500, 1);

    public MandateTests()
    {
        Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
        _data = OnlyWar.Persistence.Database.GameRules.GameRulesLoader.Load(RulesDatabaseFixture.DatabasePath);
    }

    private int Week => _date.GetTotalWeeks();

    // ---- Domain ---------------------------------------------------------------------------

    [Fact]
    public void ChapterMandates_RefusesASecondActiveMandate()
    {
        ChapterMandates mandates = new();
        mandates.Add(new Mandate(1, MandateFamily.SupplyNetwork, "Build a supply network", null, 10, 20, 60));

        Assert.Throws<InvalidOperationException>(() => mandates.Add(
            new Mandate(2, MandateFamily.Reputation, "Win favour", null, 10, 20, 60)));
    }

    [Fact]
    public void AddLegacy_WritesOneEntryPerSource_AndCreditsTheMasterInOffice()
    {
        ChapterMandates mandates = new();
        mandates.BeginTenure(11, "Brother Varn", 0);

        mandates.AddLegacy(1, 20, LegacySourceKind.WorldSaved, "event/1", 1, "saved");
        mandates.AddLegacy(1, 20, LegacySourceKind.WorldSaved, "event/1", 1, "saved again");
        mandates.EndTenure(2);
        mandates.AddLegacy(3, -5, LegacySourceKind.BrotherKilled, "event/2", 2, "fell");

        Assert.Equal(2, mandates.Ledger.Count);
        Assert.Equal(11, mandates.Ledger[0].ChapterMasterId);
        Assert.Null(mandates.Ledger[1].ChapterMasterId);
        Assert.Equal(15, mandates.ChapterTotal);
        Assert.Equal(20, mandates.TotalFor(11));
        Assert.Equal(-5, mandates.TotalFor(null));
    }

    [Theory]
    [InlineData(150, -75)]
    [InlineData(25, -13)]
    [InlineData(60, -30)]
    public void FailureLegacy_IsHalfTheValueRoundedUp(int value, int expected) =>
        Assert.Equal(expected, MandateConstants.FailureLegacy(value));

    [Theory]
    [InlineData(4999, 0)]
    [InlineData(5000, 10)]
    [InlineData(10000, 20)]
    [InlineData(1000000, 40)]
    public void LargeBattleLegacy_ScalesWithSizeAndStopsAtTheCap(long size, int expected) =>
        Assert.Equal(expected, MandateConstants.LargeBattleLegacy(size));

    // ---- Catalog --------------------------------------------------------------------------

    [Fact]
    public void Catalog_WithoutAChapterWorld_OffersEstablishForAContestedImperialWorld()
    {
        Fixture fixture = CreateFixture();

        List<string> keys = MandateCatalog.ListEligible(fixture.Sector, _data).Select(o => o.Key).ToList();

        Assert.Contains($"establish/{fixture.Contested.Id}", keys);
        Assert.DoesNotContain($"establish/{fixture.Capital.Id}", keys); // no enemy there
        Assert.DoesNotContain(keys, key => key.StartsWith("secure/", StringComparison.Ordinal));
        Assert.Contains("reputation/7001", keys);
        Assert.Contains("supply", keys);
        // One threatened governance seat is not enough for Protect.
        Assert.DoesNotContain(keys, key => key.StartsWith("protect/", StringComparison.Ordinal));
    }

    [Fact]
    public void Catalog_WithAChapterWorldUnderAttack_OffersSecureAndNoEstablish()
    {
        Fixture fixture = CreateFixture();
        fixture.Sector.PlayerForce.HomeWorldPlanetId = fixture.Contested.Id;

        List<string> keys = MandateCatalog.ListEligible(fixture.Sector, _data).Select(o => o.Key).ToList();

        Assert.Contains($"secure/{fixture.Contested.Id}", keys);
        Assert.DoesNotContain(keys, key => key.StartsWith("establish/", StringComparison.Ordinal));
    }

    [Fact]
    public void Catalog_NoReputationOptionOnceTheSectorLordIsWarm()
    {
        Fixture fixture = CreateFixture();
        fixture.Lord.OpinionOfPlayerForce = MandateConstants.ReputationWarmOpinion;

        Assert.DoesNotContain(MandateCatalog.ListEligible(fixture.Sector, _data),
            option => option.Family == MandateFamily.Reputation);
    }

    // ---- Service --------------------------------------------------------------------------

    [Fact]
    public void Swear_ActivatesTheMandate_RecordsTheOath_AndClosesTheChooser()
    {
        Fixture fixture = CreateFixture();
        MandateService service = Service(fixture);

        Mandate mandate = service.Swear("supply");

        Assert.Same(mandate, fixture.Sector.PlayerForce.Mandates.Active);
        Assert.Equal(Week + MandateConstants.DeadlineWeeks(MandateFamily.SupplyNetwork), mandate.DeadlineWeek);
        Assert.Equal(-1, mandate.BaselinePledgeId);
        Assert.Empty(service.ListOptions());
        Assert.Throws<InvalidOperationException>(() => service.Swear("supply"));
        CampaignEvent oath = Assert.Single(Events(fixture, CampaignEventType.Oath));
        Assert.True(oath.Publication.PublishesToChapterChronicle);
    }

    [Fact]
    public void Abandon_BreaksTheVow_AndCostsHalfItsValue()
    {
        Fixture fixture = CreateFixture();
        MandateService service = Service(fixture);
        Mandate mandate = service.Swear("supply");

        service.Abandon();

        Assert.Equal(MandateState.Abandoned, mandate.State);
        Assert.Null(fixture.Sector.PlayerForce.Mandates.Active);
        LegacyLedgerEntry entry = Assert.Single(fixture.Sector.PlayerForce.Mandates.Ledger);
        Assert.Equal(-30, entry.Amount);
        Assert.Equal(LegacySourceKind.MandateFailed, entry.SourceKind);
        CampaignEvent resolved = Assert.Single(Events(fixture, CampaignEventType.MandateResolved));
        Assert.True(resolved.Publication.PublishesToTurnReport);
        Assert.Contains("MANDATE ABANDONED", CampaignEventNarrator.RenderTurnReport(resolved));
    }

    [Fact]
    public void Abandon_Establish_LowersTheSectorLordsOpinion()
    {
        Fixture fixture = CreateFixture();
        MandateService service = Service(fixture);
        service.Swear($"establish/{fixture.Contested.Id}");

        service.Abandon();

        Assert.Equal(LordOpinion - MandateConstants.EstablishSectorLordOpinionPenalty,
            fixture.Lord.OpinionOfPlayerForce, precision: 4);
        Assert.Equal(-75, fixture.Sector.PlayerForce.Mandates.ChapterTotal);
    }

    [Fact]
    public void Abandon_PromisedWorld_LapsesTheScenarioWithItsPenalty()
    {
        Fixture fixture = CreateFixture(promisedScenario: true);
        MandateService service = Service(fixture);
        Mandate promised = service.EnsurePromisedWorld();
        Assert.True(promised.IsPromisedWorld);
        Assert.Null(promised.DeadlineWeek);
        // The founding entry already tells the Promised World directive.
        Assert.False(Assert.Single(Events(fixture, CampaignEventType.Oath)).Publication.PublishesToChapterChronicle);

        service.Abandon();

        Assert.Equal(ObjectiveState.Lapsed, fixture.Sector.Scenario.State);
        float penalty = _data.ScenarioProfiles
            .GetRequiredForScenario(ScenarioKeys.PromisedWorld, _data.SectorFactions.Invader.Id)
            .SectorLordOpinionPenalty;
        Assert.Equal(LordOpinion - penalty, fixture.Lord.OpinionOfPlayerForce, precision: 4);
        Assert.Equal(MandateState.Abandoned, promised.State);
        Assert.Equal(-75, fixture.Sector.PlayerForce.Mandates.ChapterTotal);
    }

    [Fact]
    public void RenounceChapterWorld_ClearsTheHomeWorld_AndBreaksAnActiveSecureVow()
    {
        Fixture fixture = CreateFixture();
        fixture.Sector.PlayerForce.HomeWorldPlanetId = fixture.Contested.Id;
        MandateService service = Service(fixture);
        Mandate secure = service.Swear($"secure/{fixture.Contested.Id}");

        Assert.True(service.RenounceChapterWorld());

        Assert.Null(fixture.Sector.PlayerForce.HomeWorldPlanetId);
        Assert.Equal(MandateState.Failed, secure.State);
        Assert.Equal(-50, fixture.Sector.PlayerForce.Mandates.ChapterTotal);
        Assert.Single(Events(fixture, CampaignEventType.ChapterWorldRenounced));
        Assert.False(service.RenounceChapterWorld());
    }

    // ---- Evaluator ------------------------------------------------------------------------

    [Fact]
    public void Secure_CountsCleanWeeks_ResetsOnOpenEnemyGround_AndIsKeptAtHalfAYear()
    {
        Fixture fixture = CreateFixture();
        fixture.Sector.PlayerForce.HomeWorldPlanetId = fixture.Contested.Id;
        Mandate secure = Service(fixture).Swear($"secure/{fixture.Contested.Id}");

        Assert.Equal(MandateOutcome.Ongoing, Evaluate(secure, fixture).Outcome);
        Assert.Equal(0, secure.CleanWeeks);

        ClearHostiles(fixture.Contested);
        Assert.Equal(MandateOutcome.Ongoing, Evaluate(secure, fixture).Outcome);
        Assert.Equal(1, secure.CleanWeeks);

        secure.CleanWeeks = MandateConstants.SecureCleanWeeksRequired - 1;
        Assert.Equal(MandateOutcome.Fulfilled, Evaluate(secure, fixture).Outcome);
    }

    [Fact]
    public void Evaluate_WithoutAdvance_DoesNotMoveTheCounters()
    {
        Fixture fixture = CreateFixture();
        fixture.Sector.PlayerForce.HomeWorldPlanetId = fixture.Contested.Id;
        Mandate secure = Service(fixture).Swear($"secure/{fixture.Contested.Id}");
        ClearHostiles(fixture.Contested);

        MandateEvaluator.Evaluate(secure, fixture.Sector, _data, Week, advance: false);

        Assert.Equal(0, secure.CleanWeeks);
    }

    [Fact]
    public void Reputation_IsKeptOnceTheSectorLordIsWarm_AndFailsAtTheDeadline()
    {
        Fixture fixture = CreateFixture();
        Mandate reputation = Service(fixture).Swear("reputation/7001");

        Assert.Equal(MandateOutcome.Ongoing, Evaluate(reputation, fixture).Outcome);
        Assert.Equal(MandateOutcome.Failed, MandateEvaluator.Evaluate(
            reputation, fixture.Sector, _data, reputation.DeadlineWeek.Value, advance: false).Outcome);

        fixture.Lord.OpinionOfPlayerForce = MandateConstants.ReputationWarmOpinion;
        Assert.Equal(MandateOutcome.Fulfilled, Evaluate(reputation, fixture).Outcome);
    }

    // ---- Turn pass ------------------------------------------------------------------------

    [Fact]
    public void TurnPass_EstablishKept_GrantsTheWorld_RaisesOpinion_AndWritesLegacy()
    {
        Fixture fixture = CreateFixture();
        Mandate establish = Service(fixture).Swear($"establish/{fixture.Contested.Id}");
        ClearHostiles(fixture.Contested);

        Process(fixture);

        PlayerForce force = fixture.Sector.PlayerForce;
        Assert.Equal(MandateState.Fulfilled, establish.State);
        Assert.Equal(fixture.Contested.Id, force.HomeWorldPlanetId);
        Assert.True(fixture.Contested.PlanetFactionMap.ContainsKey(force.Faction.Id));
        Assert.NotNull(force.RecruitmentProgram);
        Assert.Equal(LordOpinion + MandateConstants.EstablishSectorLordOpinionReward,
            fixture.Lord.OpinionOfPlayerForce, precision: 4);
        Assert.Equal(MandateConstants.EstablishLegacy, force.Mandates.ChapterTotal);
    }

    [Fact]
    public void TurnPass_EstablishFails_WhenNoImperialForceHoldsGround()
    {
        Fixture fixture = CreateFixture();
        Mandate establish = Service(fixture).Swear($"establish/{fixture.Contested.Id}");
        foreach (Region region in fixture.Contested.Regions)
        {
            region.RegionFactionMap.Remove(_data.DefaultFaction.Id);
        }

        Process(fixture);

        Assert.Equal(MandateState.Failed, establish.State);
        Assert.Null(fixture.Sector.PlayerForce.HomeWorldPlanetId);
        Assert.Equal(LordOpinion - MandateConstants.EstablishSectorLordOpinionPenalty,
            fixture.Lord.OpinionOfPlayerForce, precision: 4);
        Assert.Equal(-75, fixture.Sector.PlayerForce.Mandates.ChapterTotal);
    }

    [Fact]
    public void TurnPass_PromisedWorldWon_KeepsTheVowOnce()
    {
        Fixture fixture = CreateFixture(promisedScenario: true);
        Mandate promised = Service(fixture).EnsurePromisedWorld();
        fixture.Sector.Scenario.State = ObjectiveState.Won;

        Process(fixture);
        Process(fixture);

        Assert.Equal(MandateState.Fulfilled, promised.State);
        Assert.Equal(MandateConstants.EstablishLegacy, fixture.Sector.PlayerForce.Mandates.ChapterTotal);
        Assert.Single(fixture.Sector.PlayerForce.Mandates.Ledger);
    }

    [Fact]
    public void TurnPass_PromisedWorldPending_IsNeverJudgedByTheEvaluator()
    {
        Fixture fixture = CreateFixture(promisedScenario: true);
        Mandate promised = Service(fixture).EnsurePromisedWorld();
        ClearHostiles(fixture.Contested);

        Process(fixture);

        Assert.Equal(MandateState.Active, promised.State);
    }

    // ---- Legacy sources -------------------------------------------------------------------

    [Fact]
    public void RecordWeek_ScoresEachSourceOnce()
    {
        Fixture fixture = CreateFixture();
        PlayerForce force = fixture.Sector.PlayerForce;
        CampaignEventRecorder recorder = force.GetCampaignEventRecorder();
        List<CampaignEvent> events =
        [
            recorder.Record(Candidate(CampaignEventType.WorldSaved, "world/1",
                new WorldControlChangedPayload(fixture.Contested.Id, fixture.Contested.Name,
                    _data.DefaultFaction.Id, _data.SectorFactions.Invader.Id, _data.DefaultFaction.Id,
                    Week - 4, Week, ChapterParticipated: true, CurrentControlIsImperial: true))),
            recorder.Record(Candidate(CampaignEventType.WorldSaved, "world/2",
                new WorldControlChangedPayload(fixture.Capital.Id, fixture.Capital.Name,
                    _data.DefaultFaction.Id, _data.SectorFactions.Invader.Id, _data.DefaultFaction.Id,
                    Week - 4, Week, ChapterParticipated: false, CurrentControlIsImperial: true))),
            recorder.Record(Candidate(CampaignEventType.BattleResolved, "battle/1",
                new BattleResolvedPayload("Battle of Test", "Won.", 10000, true))),
            recorder.Record(Candidate(CampaignEventType.BattleResolved, "battle/2",
                new BattleResolvedPayload("Battle of Loss", "Lost.", 5000, false))),
            recorder.Record(Candidate(CampaignEventType.BattleResolved, "battle/3",
                new BattleResolvedPayload("Skirmish", "Won.", 100, true))),
        ];

        int written = LegacyService.RecordWeek(force, events, Week);
        int again = LegacyService.RecordWeek(force, events, Week);

        Assert.Equal(3, written);
        Assert.Equal(0, again);
        Assert.Equal(MandateConstants.WorldSavedLegacy + 20 - 10, force.Mandates.ChapterTotal);
    }

    // ---- Chapter Master office ------------------------------------------------------------

    [Fact]
    public void ChapterMaster_OnlyALivingCaptainCanTakeTheEmptyOffice()
    {
        Fixture fixture = CreateFixture(captain: true);
        PlayerForce force = fixture.Sector.PlayerForce;
        PlayerSoldier captain = force.Army.PlayerSoldierMap[501];
        PlayerSoldier marine = force.Army.PlayerSoldierMap[502];

        Assert.True(ChapterMasterOffice.IsVacant(force, _data));
        Assert.Equal([captain], ChapterMasterOffice.Candidates(force, _data));
        Assert.NotNull(ChapterMasterOffice.WhyCannotPromote(force, _data, marine));

        ChapterMasterOffice.Promote(force, _data, captain, _date);

        Assert.Same(captain, ChapterMasterOffice.FindHolder(force, _data));
        Assert.Equal(captain.Id, force.Mandates.CurrentTenure.SoldierId);
        Assert.Single(Events(fixture, CampaignEventType.ChapterMasterAppointed));
        Assert.Empty(ChapterMasterOffice.Candidates(force, _data));
    }

    [Fact]
    public void ChapterMaster_SynchronizeEndsTheTenureWhenTheHolderIsGone()
    {
        Fixture fixture = CreateFixture(captain: true);
        PlayerForce force = fixture.Sector.PlayerForce;
        PlayerSoldier captain = force.Army.PlayerSoldierMap[501];
        ChapterMasterOffice.Promote(force, _data, captain, _date);

        captain.Template = _data.ChapterDoctrine.Captain;
        ChapterMasterOfficeChange change = ChapterMasterOffice.Synchronize(force, _data, Week + 3);

        Assert.Equal(captain.Id, change.EndedSoldierId);
        Assert.Null(force.Mandates.CurrentTenure);
        Assert.Equal(Week + 3, force.Mandates.Tenures.Single().EndWeek);
    }

    // ---- Save -----------------------------------------------------------------------------

    [Fact]
    public void SaveAndLoad_KeepsMandatesLedgerTenuresAndRaisedCompanies()
    {
        ChapterMandates original = new();
        original.Restore(new ChapterMasterTenure(5, "Brother Old", 100, 150));
        original.BeginTenure(6, "Brother New", 150);
        original.Add(new Mandate(1, MandateFamily.EstablishChapterWorld, "Liberate X", "X", 100, null, 150,
            planetIds: [3], isPromisedWorld: true, state: MandateState.Fulfilled, resolvedWeek: 140));
        original.Add(new Mandate(2, MandateFamily.EradicateThreat, "Eradicate the host", "the host", 150, 300, 100,
            planetIds: [3, 4], factionId: 9, invasionForceId: 77L, threatKind: MandateThreatKind.InvasionForce,
            baselinePledgeId: 12, cleanWeeks: 4));
        original.Restore(new LegacyLedgerEntry(1, 140, 150, LegacySourceKind.MandateFulfilled, "mandate/1", 33, 5, "kept"));
        original.Restore(new LegacyLedgerEntry(2, 151, -1, LegacySourceKind.BrotherKilled, "event/40", 40, null, "fell"));
        original.MarkCompanyRaised(21);

        string path = GameStateRoundTripFixture.CreateTempDbPath("mandates");
        try
        {
            ChapterMandates loaded;
            using (SqliteConnection connection = OpenDatabase(path))
            {
                using (SqliteCommand schema = connection.CreateCommand())
                {
                    schema.CommandText = File.ReadAllText(RulesDatabaseFixture.SaveSchemaPath);
                    schema.ExecuteNonQuery();
                }
                ChapterMandatesDataAccess access = new();
                using (SqliteTransaction transaction = connection.BeginTransaction())
                {
                    access.Save(transaction, original);
                    transaction.Commit();
                }
                loaded = access.Get(connection);
            }

            Assert.Equal(2, loaded.All.Count);
            Mandate eradicate = loaded.Get(2);
            Assert.Same(eradicate, loaded.Active);
            Assert.Equal([3, 4], eradicate.PlanetIds);
            Assert.Equal(77L, eradicate.InvasionForceId);
            Assert.Equal(MandateThreatKind.InvasionForce, eradicate.ThreatKind);
            Assert.Equal(12, eradicate.BaselinePledgeId);
            Assert.Equal(4, eradicate.CleanWeeks);
            Assert.Equal(300, eradicate.DeadlineWeek);
            Mandate promised = loaded.Get(1);
            Assert.True(promised.IsPromisedWorld);
            Assert.Null(promised.DeadlineWeek);
            Assert.Equal(140, promised.ResolvedWeek);
            Assert.Equal(original.Ledger, loaded.Ledger);
            Assert.Equal(2, loaded.Tenures.Count);
            Assert.Equal(6, loaded.CurrentTenure.SoldierId);
            Assert.Equal(150, loaded.Tenures[0].EndWeek);
            Assert.True(loaded.WasCompanyRaised(21));
        }
        finally
        {
            GameStateRoundTripFixture.CleanupDb(path);
        }
    }

    // ---- Screen context -------------------------------------------------------------------

    [Fact]
    public void ScreenContext_SwearAndAbandon_UpdateTheBoard()
    {
        Fixture fixture = CreateFixture();
        MandateScreenContext screen = new(fixture.Sector, _data, _date);

        MandateBoardView before = screen.QueryBoard();
        Assert.Null(before.Active);
        Assert.Contains(before.Options, option => option.Key == "supply");
        Assert.False(screen.Swear("no-such-mandate").Succeeded);

        Assert.True(screen.Swear("supply").Succeeded);
        MandateBoardView sworn = screen.QueryBoard();
        Assert.NotNull(sworn.Active);
        Assert.Empty(sworn.Options);
        Assert.False(screen.Swear("supply").Succeeded);

        Assert.True(screen.Abandon().Succeeded);
        MandateBoardView after = screen.QueryBoard();
        Assert.Null(after.Active);
        Assert.Equal(-30, after.ChapterLegacy);
        Assert.Equal("Abandoned", Assert.Single(after.History).Result);
        Assert.Equal(-30, Assert.Single(after.LegacySeries).Total);
        Assert.False(after.CanRenounceChapterWorld);
    }

    // ---- Fixture --------------------------------------------------------------------------

    private MandateEvaluation Evaluate(Mandate mandate, Fixture fixture) =>
        MandateEvaluator.Evaluate(mandate, fixture.Sector, _data, Week, advance: true);

    private MandateService Service(Fixture fixture) => new(fixture.Sector, _data, _date);

    private void Process(Fixture fixture)
    {
        TestCampaignComposition composition = TestPersonnelComposition.CreateCampaign(new StaticRNG());
        new MandateTurnProcessor(
                new GameSession(_data, fixture.Sector, _date, new StaticRNG()),
                composition.Services.Operations.Personnel,
                composition.Services.Operations.Commitments)
            .Process(fixture.Sector, fixture.Sector.PlayerForce.CurrentTurnEvents.ToList());
    }

    private static IEnumerable<CampaignEvent> Events(Fixture fixture, CampaignEventType type) =>
        fixture.Sector.PlayerForce.CurrentTurnEvents.Where(@event => @event.Type == type);

    private CampaignEventCandidate Candidate(CampaignEventType type, string key, ICampaignEventPayload payload) =>
        new(type, Week, Week, key, key, payload.Version, payload,
            [new CampaignEventEntityRef(CampaignEntityKind.Chapter, 0, CampaignEventEntityRole.Subject, "Test Chapter")]);

    private void ClearHostiles(Planet planet)
    {
        foreach (Region region in planet.Regions)
        {
            region.RegionFactionMap.Remove(_data.SectorFactions.Invader.Id);
            region.RegionFactionMap.Remove(_data.SectorFactions.Infiltrator.Id);
        }
    }

    private Fixture CreateFixture(bool promisedScenario = false, bool captain = false)
    {
        Faction imperial = _data.DefaultFaction;
        Character lord = new() { Id = 7001, Name = "Lord Testus", OpinionOfPlayerForce = LordOpinion };

        Planet contested = CreatePlanet(1001, "Contested World");
        Planet capital = CreatePlanet(1002, "Sector Capital");
        capital.GovernanceTier = GovernanceTier.SectorCapital;

        PlanetFaction capitalImperial = AddPlanetFaction(capital, imperial);
        capitalImperial.Leader = lord;
        AddRegionFaction(capital.Regions[0], capitalImperial, 1000, 100);

        PlanetFaction contestedImperial = AddPlanetFaction(contested, imperial);
        AddRegionFaction(contested.Regions[0], contestedImperial, 1000, 100);
        AddRegionFaction(contested.Regions[1], AddPlanetFaction(contested, _data.SectorFactions.Invader), 250, 250);

        Sector sector = new(CreatePlayerForce(captain), [lord], [contested, capital], []);
        if (promisedScenario)
        {
            sector.Scenario = new CampaignScenario(ScenarioType.PromisedWorld, contested.Id, "Test briefing", lord.Id);
        }
        return new Fixture(sector, contested, capital, lord);
    }

    private PlayerForce CreatePlayerForce(bool withCaptain)
    {
        UnitTemplate template = new(9001, "Test Chapter", true, [], []) { Faction = _data.PlayerFaction };
        Unit root = new(9001, "Test Chapter", template, []);
        Unit scoutCompany = new("Tenth Company", _data.ChapterDoctrine.ScoutCompany) { ParentUnit = root };
        root.ChildUnits.Add(scoutCompany);
        List<PlayerSoldier> roster = [];
        if (withCaptain)
        {
            roster.Add(CreatePlayerSoldier(501, "Captain Tarn", _data.ChapterDoctrine.Captain));
            roster.Add(CreatePlayerSoldier(502, "Brother Kell", TestModelFactory.MarineTemplate));
        }
        Army army = new("Test Chapter Ground Forces", null, "Chapter Master", root, roster);
        return new PlayerForce(_data.PlayerFaction, army, new Fleet("Test Chapter Fleet", null, "Fleet Master"));
    }

    private static PlayerSoldier CreatePlayerSoldier(int id, string name, SoldierTemplate template)
    {
        Soldier soldier = TestModelFactory.CreateSoldier(name: name, template: template);
        soldier.Id = id;
        return new PlayerSoldier(soldier, name);
    }

    private static Planet CreatePlanet(int id, string name)
    {
        Planet planet = new(id, name, new Coordinate((ushort)id, 1), 1, null, 1, 1);
        for (int i = 0; i < planet.Regions.Length; i++)
        {
            planet.Regions[i] = new Region(i + 1 + id * 10, planet, regionType: 1, name: $"Region {i + 1}",
                coordinates: new RegionCoordinate(i, 0), intelligenceLevel: 0, carryingCapacity: 10000);
        }
        return planet;
    }

    private static PlanetFaction AddPlanetFaction(Planet planet, Faction faction)
    {
        PlanetFaction planetFaction = new(faction);
        planet.PlanetFactionMap[faction.Id] = planetFaction;
        return planetFaction;
    }

    private static void AddRegionFaction(Region region, PlanetFaction planetFaction, long population, long garrison) =>
        region.RegionFactionMap[planetFaction.Faction.Id] = new RegionFaction(planetFaction, region)
        {
            Population = population,
            Garrison = garrison,
            IsPublic = true
        };

    private static SqliteConnection OpenDatabase(string path)
    {
        SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            ForeignKeys = false,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private sealed record Fixture(Sector Sector, Planet Contested, Planet Capital, Character Lord);
}
