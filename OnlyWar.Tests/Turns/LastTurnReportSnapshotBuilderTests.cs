using OnlyWar.Application;
using OnlyWar.Campaign.Turns;
using OnlyWar.Domain;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Reports;
using OnlyWar.Tests.Fixtures;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace OnlyWar.Tests.Turns;

public class LastTurnReportSnapshotBuilderTests
{
    [Fact]
    public void BuildSnapshot_PreservesAllPlayerFacingCardsAndCompactBattleData()
    {
        BattleDebriefView battle = new(
            PlayerDeaths: 1,
            OpposingDeaths: 2,
            PlayerCasualties:
            [
                new BattleCasualtyView(
                    17,
                    "Brother Venn",
                    "Sergeant",
                    "Third Squad",
                    "First Company",
                    BattleCasualtyDispositionView.Dead,
                    0)
            ],
            PlayerIncapacitated: 0);

        List<EndOfTurnReportEntry> entries =
        [
            new EndOfTurnReportEntry(
                "Recon",
                "Third Squad - hostile world",
                "Recon completed.",
                true,
                "CONTACT",
                [new MissionDebriefLineView(
                    "Day 3: Contact confirmed.",
                     BattleReport: battle,
                     Day: 3,
                     SquadName: "Third Squad")]),
            new EndOfTurnReportEntry("Strategic Combat", "Enemy activity", "Combat resolved.", false, isEnemyActivity: true),
            new EndOfTurnReportEntry("Construction", "Region One", "Fortifications improved.", false, "PROGRESSED"),
            new EndOfTurnReportEntry("Fortifications", "Works handed over", "The position still stands.", false, "HANDED OVER"),
            new EndOfTurnReportEntry("Governor's Request", "Governor of a world", "A petition has arrived.", false),
            new EndOfTurnReportEntry("Recruitment", "10th Company recruitment program", "Candidates screened.", false, "PROCESSED"),
            new EndOfTurnReportEntry("No Reports", "No mission activity this turn", "The sector is quiet.", false)
        ];

        LastTurnReportSnapshot snapshot = LastTurnReportSnapshotBuilder.BuildSnapshot(
            new Date(42, 123, 7),
            entries);

        Assert.Equal(new Date(42, 123, 7).GetTotalWeeks(), snapshot.ResolvedDate);
        Assert.Equal(entries.Count, snapshot.Entries.Count);
        Assert.Equal("Recon", snapshot.Entries[0].Title);
        Assert.True(snapshot.Entries[0].Debrief != null);
        Assert.Equal("CONTACT", snapshot.Entries[0].Debrief.OutcomeStatus);

        LastTurnDebriefLineSnapshot line = Assert.Single(snapshot.Entries[0].Debrief.Lines);
        Assert.Equal((ushort)3, line.Day);
        Assert.Equal("Third Squad", line.SquadName);
        Assert.Equal(1, line.BattleSummary.PlayerDeaths);
        Assert.Equal(2, line.BattleSummary.OpposingDeaths);
        BattleCasualtySnapshot casualty = Assert.Single(line.BattleSummary.Casualties);
        Assert.Equal(17, casualty.SoldierId);
        Assert.Equal("Brother Venn", casualty.Name);
        Assert.Equal("Dead", casualty.Disposition);

        Assert.True(snapshot.Entries[1].IsEnemyActivity);
        Assert.Equal("PROCESSED", snapshot.Entries[5].OutcomeStatus);
    }

    [Fact]
    public void Build_UsesTheExistingReportWordingForStrategicConstructionFortificationAndRecruitment()
    {
        TurnResolutionResult result = new();
        result.StrategicCombatResults.Add(new StrategicCombatResult(
            target: null,
            attacker: null,
            committedBattleValue: 10,
            defenderBattleValue: 10,
            attackerEffectiveStrength: 10,
            defenderEffectiveStrength: 10,
            attackerLosses: 1,
            defenderLosses: 2,
            attackerSurvivors: 9,
            outcome: StrategicCombatOutcome.DefenderHeld,
            attackerWon: false,
            controlChanged: false));
        result.ConstructionReports.Add(new ConstructionProgressReport(
            DefenseType.Entrenchment,
            regionFaction: null,
            squadNames: ["Builders"],
            isPlayerConstruction: true,
            levelBefore: 1,
            levelAfter: 2,
            sharedLevelBefore: 1));
        result.FortificationTransfers.Add(new FortificationTransferReport(
            region: null,
            from: SectorSimulationFixture.BuildTestFaction(1, "Test Chapter", isPlayer: true, isDefault: false),
            to: SectorSimulationFixture.BuildTestFaction(2, "Local Guard", isPlayer: false, isDefault: false),
            sharedEntrenchment: 1));
        result.RecruitmentReport = new RecruitmentTurnReport(
            Processed: true,
            PausedReason: null,
            RequisitionSpent: 100,
            ScreenedCandidates: 20,
            QualifiedCandidates: 5,
            AspirantsAdmitted: 2,
            ImplantationsCompleted: 1,
            AspirantDeaths: 0,
            CandidatesAgedOut: 0);

        LastTurnReportBuildResult build = LastTurnReportSnapshotBuilder.Build(
            new Date(1, 1, 2),
            result);

        Assert.Equal(
            ["Construction", "Fortifications", "Distant Fighting", "Recruitment"],
            build.Snapshot.Entries.Select(entry => entry.Title));
        Assert.Equal(
            build.Snapshot.Entries.Select(entry => entry.Title),
            build.PresentationEntries.Select(entry => entry.Title));
    }

    [Fact]
    public void Build_NewOpportunityCarriesItsPlanetSoTheReportCanGoThere()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction cult = fixture.AddPublicCult(0, population: 1000, organization: 100);
        TurnResolutionResult result = new();
        result.SpecialMissions.Add(new Mission(1, MissionType.Ambush, cult, 1));

        LastTurnReportBuildResult build = LastTurnReportSnapshotBuilder.Build(
            new Date(1, 1, 2),
            result);

        EndOfTurnReportEntry entry = Assert.Single(build.PresentationEntries);
        Assert.Equal("New Opportunity", entry.Title);
        Assert.Equal(cult.Region.Planet.Id, entry.PlanetId);
        Assert.Equal(cult.Region.Planet.Id, Assert.Single(build.Snapshot.Entries).PlanetId);
    }

    [Fact]
    public void Build_CardsWithoutAWorldOfferNoPlanet()
    {
        LastTurnReportBuildResult build = LastTurnReportSnapshotBuilder.Build(
            new Date(1, 1, 1),
            new TurnResolutionResult());

        Assert.Null(Assert.Single(build.PresentationEntries).PlanetId);
    }

    [Fact]
    public void BuildPresentationEntries_RestoresThePlanetWithAndWithoutADebrief()
    {
        LastTurnReportSnapshot snapshot = LastTurnReportSnapshotBuilder.BuildSnapshot(
            null,
            [
                new EndOfTurnReportEntry("Opportunity", "Somewhere", "Found.", false, planetId: 91),
                new EndOfTurnReportEntry(
                    "Mission", "A mission", "Outcome", true, "COMPLETE",
                    [new MissionDebriefLineView("Day 1")], planetId: 12)
            ]);

        IReadOnlyList<EndOfTurnReportEntry> restored =
            LastTurnReportSnapshotBuilder.BuildPresentationEntries(snapshot);

        Assert.Equal([91, 12], restored.Select(entry => entry.PlanetId));
    }

    [Fact]
    public void SnapshotDto_PlanetRoundTripsAndOlderPayloadsLoadWithoutOne()
    {
        LastTurnReportSnapshot snapshot = new(
            5,
            [new LastTurnReportEntrySnapshot("Opportunity", "Somewhere", "Found.", "", false, planetId: 91)]);

        LastTurnReportSnapshot restored = JsonSerializer.Deserialize<LastTurnReportSnapshot>(
            JsonSerializer.Serialize(snapshot));
        LastTurnReportSnapshot legacy = JsonSerializer.Deserialize<LastTurnReportSnapshot>(
            """{"ResolvedDate":5,"Entries":[{"Title":"Opportunity","Subtitle":"Somewhere","Summary":"Found.","OutcomeStatus":"","IsEnemyActivity":false,"Debrief":null}]}""");

        Assert.Equal(91, Assert.Single(restored.Entries).PlanetId);
        Assert.Null(Assert.Single(legacy.Entries).PlanetId);
    }

    [Fact]
    public void Build_EmptyResultProducesAnIntentionalNoReportsEntry()
    {
        LastTurnReportBuildResult build = LastTurnReportSnapshotBuilder.Build(
            new Date(1, 1, 1),
            new TurnResolutionResult());

        LastTurnReportEntrySnapshot entry = Assert.Single(build.Snapshot.Entries);
        Assert.Equal("No Reports", entry.Title);
        Assert.Equal("No mission activity this turn", entry.Subtitle);
        Assert.Null(entry.Debrief);
    }

    [Fact]
    public void BuildPresentationEntries_RendersCompactBattleSummaryWithoutReplayHistory()
    {
        LastTurnReportSnapshot snapshot = LastTurnReportSnapshotBuilder.BuildSnapshot(
            null,
            [new EndOfTurnReportEntry(
                "Mission",
                "A mission",
                "Mission outcome",
                true,
                "COMPLETE",
                 [new MissionDebriefLineView(
                     "Battle summary",
                     BattleReport: new BattleDebriefView(1, 3, [], 1))])]);

        EndOfTurnReportEntry restored = Assert.Single(
            LastTurnReportSnapshotBuilder.BuildPresentationEntries(snapshot));
        MissionDebriefLineView line = Assert.Single(restored.DebriefLines);

        Assert.True(line.HasBattle);
        Assert.Null(line.BattleReplayId);
        Assert.NotNull(line.BattleReport);
        Assert.Equal(1, line.BattleReport.PlayerDeaths);
        Assert.Equal(1, line.BattleReport.PlayerIncapacitated);
    }

    [Fact]
    public void SnapshotDto_RoundTripsThroughSystemTextJson()
    {
        LastTurnReportSnapshot snapshot = new(
            123,
            [new LastTurnReportEntrySnapshot(
                "Mission",
                "A mission",
                "Outcome",
                "COMPLETE",
                false,
                new LastTurnDebriefSnapshot(
                    "Mission",
                    "A mission",
                    "COMPLETE",
                    "Outcome",
                    [new LastTurnDebriefLineSnapshot(
                        "Battle summary",
                        battleSummary: new BattleSummarySnapshot(
                            1,
                            2,
                            1,
                            [new BattleCasualtySnapshot(
                                9,
                                "Brother Nine",
                                "Marine",
                                "Squad",
                                "Company",
                                "Incapacitated",
                                3)]) )]))]);

        LastTurnReportSnapshot restored = JsonSerializer.Deserialize<LastTurnReportSnapshot>(
            JsonSerializer.Serialize(snapshot));

        Assert.Equal(123, restored.ResolvedDate);
        BattleCasualtySnapshot casualty = Assert.Single(
            Assert.Single(Assert.Single(restored.Entries).Debrief.Lines).BattleSummary.Casualties);
        Assert.Equal(9, casualty.SoldierId);
        Assert.Equal("Incapacitated", casualty.Disposition);
    }
}
