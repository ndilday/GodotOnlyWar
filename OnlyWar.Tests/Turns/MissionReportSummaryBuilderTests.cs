using System.Collections.Generic;
using OnlyWar.Helpers;
using OnlyWar.Models.Missions;
using Xunit;

namespace OnlyWar.Tests.Turns;

// EndOfTurnDialogController is a Godot partial class and can't be instantiated headlessly, so the
// end-of-turn mission-summary string building lives in this pure, Godot-free helper instead
// (Helpers/MissionReportSummaryBuilder.cs) and is exercised directly here.
public class MissionReportSummaryBuilderTests
{
    [Fact]
    public void BuildSummary_UndetectedRecon_ReportsUndetected()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            MissionType.Recon, true, "Player Chapter", "Sacred Ground, Terra",
            enemiesKilled: 0, daysElapsed: 3, impact: 1f, wasDetected: false, log: new List<string>());

        Assert.Contains("Your forces", summary);
        Assert.Contains("undetected", summary);
    }

    [Fact]
    public void BuildSummary_DetectedReconThatEscapes_ReportsBrokeContact()
    {
        var log = new List<string> { "Day 4: Force successfully escaped enemy force" };

        string summary = MissionReportSummaryBuilder.BuildSummary(
            MissionType.Recon, true, "Player Chapter", "Sacred Ground, Terra",
            enemiesKilled: 0, daysElapsed: 4, impact: 0f, wasDetected: true, log: log);

        Assert.Contains("detected", summary);
        Assert.Contains("broke contact", summary);
    }

    [Fact]
    public void BuildSummary_DetectedReconThatIsLost_ReportsLostContact()
    {
        var log = new List<string> { "Day 5: Contact lost with mission force, assumed dead." };

        string summary = MissionReportSummaryBuilder.BuildSummary(
            MissionType.Recon, false, "Tyranid Swarm", "Hive Sector, Baal",
            enemiesKilled: 0, daysElapsed: 5, impact: 0f, wasDetected: true, log: log);

        Assert.StartsWith("Tyranid Swarm", summary);
        Assert.Contains("lost contact", summary);
    }

    [Fact]
    public void BuildSummary_CombatMissionWithKills_ReportsKillCount()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            MissionType.Advance, true, "Player Chapter", "Iron Valley, Cadia",
            enemiesKilled: 7, daysElapsed: 2, impact: 0f, wasDetected: false, log: new List<string>());

        Assert.Contains("killed 7 enemy troops", summary);
    }

    [Fact]
    public void BuildSummary_CombatMissionWithNoKillsAndNoTarget_ReportsNoTarget()
    {
        var log = new List<string> { "Day 1: Force searches for an exposed target in Iron Valley." };

        string summary = MissionReportSummaryBuilder.BuildSummary(
            MissionType.LightningRaid, true, "Player Chapter", "Iron Valley, Cadia",
            enemiesKilled: 0, daysElapsed: 1, impact: 0f, wasDetected: false,
            log: new List<string> { "Day 1: The raiders find no isolated force to engage." });

        Assert.Contains("no viable target", summary);
    }

    [Fact]
    public void BuildSummary_SabotageWithPositiveImpact_ReportsSuccess()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            MissionType.Sabotage, true, "Player Chapter", "Forge Complex, Mars",
            enemiesKilled: 0, daysElapsed: 3, impact: 2.5f, wasDetected: false, log: new List<string>());

        Assert.Contains("sabotaged enemy operations", summary);
    }

    [Fact]
    public void BuildSummary_SabotageWithNoImpact_ReportsNoEffect()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            MissionType.Sabotage, true, "Player Chapter", "Forge Complex, Mars",
            enemiesKilled: 0, daysElapsed: 3, impact: -1f, wasDetected: false, log: new List<string>());

        Assert.Contains("without notable effect", summary);
    }

    [Fact]
    public void BuildSummary_AssassinationTargetLocatedAndImpactPositive_ReportsElimination()
    {
        var log = new List<string> { "Day 6: Force has located the assassination target" };

        string summary = MissionReportSummaryBuilder.BuildSummary(
            MissionType.Assassination, true, "Player Chapter", "Spire, Necromunda",
            enemiesKilled: 0, daysElapsed: 6, impact: 1f, wasDetected: false, log: log);

        Assert.Contains("eliminated the target", summary);
    }

    [Fact]
    public void BuildSummary_UnknownMissionType_FallsBackToGeneric()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            MissionType.Construction, false, "Ork Waaagh", "Scrapyard, Golgotha",
            enemiesKilled: 0, daysElapsed: 1, impact: 0f, wasDetected: false, log: new List<string>());

        Assert.Contains("Ork Waaagh", summary);
        Assert.Contains("Construction", summary);
    }

    [Fact]
    public void BuildUnconfirmedSummary_MentionsMissionTypeAndLocation()
    {
        string summary = MissionReportSummaryBuilder.BuildUnconfirmedSummary(
            MissionType.Ambush, "Iron Valley, Cadia");

        Assert.Contains("Unconfirmed", summary);
        Assert.Contains("Ambush", summary);
        Assert.Contains("Iron Valley, Cadia", summary);
    }

    [Fact]
    public void BuildSubject_PlayerFaction_ReturnsYourForces()
    {
        Assert.Equal("Your forces", MissionReportSummaryBuilder.BuildSubject(true, "Player Chapter"));
    }

    [Fact]
    public void BuildSubject_NpcFaction_ReturnsFactionName()
    {
        Assert.Equal("Ork Waaagh", MissionReportSummaryBuilder.BuildSubject(false, "Ork Waaagh"));
    }
}
