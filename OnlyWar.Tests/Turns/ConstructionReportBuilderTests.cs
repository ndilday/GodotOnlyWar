using OnlyWar.Helpers;
using OnlyWar.Models.Missions;
using Xunit;

namespace OnlyWar.Tests.Turns;

// EndOfTurnDialogController is a Godot partial class and can't be instantiated headlessly, so the
// construction entry's string building lives in Helpers/ConstructionReportBuilder.cs and is
// exercised directly here (same split as MissionReportSummaryBuilderTests).
//
// The behaviour under test is issue #5's answer: construction never "finishes", so the report has
// to show what the squad contributed, what the region is now worth, and when the rating the region
// displays will next change. The last of those is measured against the position as it stands when
// the report is READ, so the report can never disagree with the region dossier.
public class ConstructionReportBuilderTests
{
    private static ConstructionProgressReport Report(
        double levelBefore,
        double levelAfter,
        double sharedBefore,
        DefenseType type = DefenseType.Entrenchment,
        params string[] squadNames) =>
        // RegionFaction is only read by the caller (to derive the location and the live shared
        // level), never by the builder, so it can stay null here.
        new(type, null, squadNames, true, levelBefore, levelAfter, sharedBefore);

    // The Chapter fortifying alone: the side's position is exactly its own contribution.
    private static ConstructionProgressReport Solo(
        double levelBefore,
        double levelAfter,
        DefenseType type = DefenseType.Entrenchment,
        params string[] squadNames) =>
        Report(levelBefore, levelAfter, levelBefore, type, squadNames);

    [Fact]
    public void BuildSummary_SubBucketProgress_StillReportsMovementAndRate()
    {
        // The case that reads as "nothing happened": a week of work that doesn't move the
        // displayed rating off "None".
        string summary = ConstructionReportBuilder.BuildSummary(
            Solo(0.00, 0.18), "Sacred Ground, Terra", 0.18);

        Assert.Contains("Your forces", summary);
        Assert.Contains("entrenchments", summary);
        Assert.Contains("Sacred Ground, Terra", summary);
        Assert.Contains("0.00 to 0.18", summary);
        Assert.Contains("+0.18", summary);
        Assert.Contains("Minimal", summary);
    }

    [Fact]
    public void BuildSummary_NoProgress_SaysSo()
    {
        string summary = ConstructionReportBuilder.BuildSummary(
            Solo(2.00, 2.00), "Sacred Ground, Terra", 2.00);

        Assert.Contains("no measurable progress", summary);
        Assert.DoesNotContain("At this rate", summary);
    }

    [Fact]
    public void BuildSummary_CrossingABucket_CallsOutTheNewRating()
    {
        string summary = ConstructionReportBuilder.BuildSummary(
            Solo(0.40, 0.90), "Sacred Ground, Terra", 0.90);

        Assert.Contains("from None to Minimal", summary);
    }

    // The projection has to aim at the level where GetDefenseLevelDescription actually changes what
    // it prints. That function rounds, so "Mediocre" appears just above 2.5 - not at 3.0. Aiming at
    // the band start would quote a week count the region beats, which is precisely the "it said two
    // weeks but the region already shows it" confusion.
    [Fact]
    public void BuildProjection_TargetsTheLevelWhereTheDisplayedRatingActuallyChanges()
    {
        // 2.40 reads "Minimal". At 0.15/week the next week reaches 2.55, which rounds to 3 and
        // reads "Mediocre" - so the milestone is one week away, not the (3.0 - 2.4) / 0.15 = 4 that
        // aiming at the band start would have quoted.
        string projection = ConstructionReportBuilder.BuildProjection(
            Report(0.00, 0.15, 2.25), 2.40);

        Assert.Contains("Mediocre", projection);
        Assert.Contains("next week", projection);
    }

    [Fact]
    public void BuildProjection_ProjectsWeeksToTheNextVisibleRating()
    {
        // 0.18/week from 0.18 needs to clear 0.5 to read "Minimal": two more weeks.
        string projection = ConstructionReportBuilder.BuildProjection(
            Report(0.00, 0.18, 0.00), 0.18);

        Assert.Contains("Minimal", projection);
        Assert.Contains("2 more weeks", projection);
    }

    [Fact]
    public void BuildProjection_FromWithinABucket_TargetsTheNextBucketNotTheCurrentOne()
    {
        // 1.5 already displays as "Minimal", so the next milestone is "Mediocre" just above 2.5.
        // At 0.5/week that is three weeks, not two: week two lands exactly on 2.5, which still
        // rounds down to "Minimal".
        string projection = ConstructionReportBuilder.BuildProjection(
            Report(1.00, 1.50, 1.00), 1.50);

        Assert.Contains("Mediocre", projection);
        Assert.Contains("3 more weeks", projection);
    }

    [Fact]
    public void BuildProjection_AtTopRating_DoesNotPromiseAFurtherMilestone()
    {
        string projection = ConstructionReportBuilder.BuildProjection(
            Report(9.00, 9.50, 9.00), 9.50);

        Assert.Contains("highest rating", projection);
        Assert.DoesNotContain("At this rate", projection);
    }

    [Fact]
    public void BuildOutcomeStatus_ReflectsWhetherTheRatingMoved()
    {
        Assert.Equal(
            "NO PROGRESS",
            ConstructionReportBuilder.BuildOutcomeStatus(Solo(2.0, 2.0), 2.0));
        Assert.Equal(
            "WORK IN PROGRESS",
            ConstructionReportBuilder.BuildOutcomeStatus(Solo(0.0, 0.2), 0.2));
        Assert.Equal(
            "FORTIFICATIONS IMPROVED",
            ConstructionReportBuilder.BuildOutcomeStatus(Solo(0.4, 0.9), 0.9));
    }

    [Fact]
    public void BuildSubtitle_NamesTheSquadAndTheWork()
    {
        string subtitle = ConstructionReportBuilder.BuildSubtitle(
            Solo(0.0, 0.5, DefenseType.AntiAir, "Devastator Squad Kranon"),
            "Sacred Ground, Terra");

        Assert.Contains("Devastator Squad Kranon", subtitle);
        Assert.Contains("anti-air defenses", subtitle);
        Assert.Contains("Sacred Ground, Terra", subtitle);
    }

    [Fact]
    public void BuildSubtitle_MultipleSquads_CountsThem()
    {
        string subtitle = ConstructionReportBuilder.BuildSubtitle(
            Solo(0.0, 0.5, DefenseType.ListeningPost, "Squad Alpha", "Squad Beta"),
            "Sacred Ground, Terra");

        Assert.Contains("2 squads", subtitle);
        Assert.Contains("listening post", subtitle);
    }

    [Fact]
    public void BuildSummary_WithAlliedWorks_ReportsBothTheContributionAndThePosition()
    {
        // Chapter moved its own stock 0.00 -> 0.60; pooled with the PDF's the region stands at
        // 1.60, which is the rating the dossier will show.
        string summary = ConstructionReportBuilder.BuildSummary(
            Report(0.00, 0.60, 1.30), "Sacred Ground, Terra", 1.60);

        Assert.Contains("+0.60", summary);
        Assert.Contains("Combined with allied works", summary);
        Assert.Contains("1.60", summary);
    }

    [Fact]
    public void BuildSummary_WithoutAlliedWorks_DoesNotMentionAllies()
    {
        string summary = ConstructionReportBuilder.BuildSummary(
            Solo(0.00, 0.60), "Sacred Ground, Terra", 0.60);

        Assert.DoesNotContain("Combined with allied works", summary);
    }

    // The rate is how far the POSITION moved, not how far the Chapter's own stock did, so an ally
    // that built alongside is already priced into the week count the player is quoted.
    [Fact]
    public void BuildProjection_CountsAlliedBuildingTowardTheRate()
    {
        // The Chapter added 0.20 of its own, but the position moved 2.00 -> 2.40 because the PDF
        // was digging too. One more week at that rate clears 2.5 into "Mediocre".
        string projection = ConstructionReportBuilder.BuildProjection(
            Report(0.00, 0.20, 2.00), 2.40);

        Assert.Contains("Mediocre", projection);
        Assert.Contains("next week", projection);
    }

    [Fact]
    public void BuildProjection_WhenThePositionLostGround_DeclinesToProject()
    {
        // Sabotage or decay outran the week's building: promising a milestone would be a lie.
        string projection = ConstructionReportBuilder.BuildProjection(
            Report(0.00, 0.20, 2.40), 2.10);

        Assert.Contains("lost as much ground", projection);
        Assert.DoesNotContain("At this rate", projection);
    }

    [Fact]
    public void BuildProjection_WhenAlliedWorksDominate_DeclinesToProjectAWeekCount()
    {
        // Each level costs ten times the last, so a squad adding to a position an ally has already
        // built high barely moves the shared rating. "About 4000 weeks" would be useless.
        string projection = ConstructionReportBuilder.BuildProjection(
            Report(0.00, 0.60, 4.0000), 4.0002);

        Assert.Contains("barely moved it", projection);
        Assert.DoesNotContain("At this rate", projection);
    }

    [Fact]
    public void BuildOutcomeStatus_TracksTheSharedRating_NotTheChaptersOwnStock()
    {
        // The Chapter's own stock stays inside "None", but its contribution tips the combined
        // position from Minimal to Mediocre - which is what the player sees change.
        Assert.Equal(
            "FORTIFICATIONS IMPROVED",
            ConstructionReportBuilder.BuildOutcomeStatus(Report(0.00, 0.40, 2.40), 2.60));
    }

    [Fact]
    public void BuildSummary_MissingLocation_FallsBackRatherThanRenderingEmpty()
    {
        string summary = ConstructionReportBuilder.BuildSummary(Solo(0.0, 0.5), null, 0.5);

        Assert.Contains("an unknown location", summary);
    }
}
