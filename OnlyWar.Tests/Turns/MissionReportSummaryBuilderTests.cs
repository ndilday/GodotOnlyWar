using OnlyWar.Helpers;
using OnlyWar.Helpers.Missions;
using OnlyWar.Models.Missions;
using Xunit;

namespace OnlyWar.Tests.Turns;

// EndOfTurnDialogController is a Godot partial class and can't be instantiated headlessly, so the
// end-of-turn mission-summary string building lives in this pure, Godot-free helper instead
// (Helpers/MissionReportSummaryBuilder.cs) and is exercised directly here. The builder now renders a
// shared MissionOutcomeClassification (built by MissionOutcomeClassifier from MissionContext's
// structured signals) rather than re-classifying from Log text, so these tests hand it a
// classification directly. Player missions only - NPC-run missions go through
// NpcMissionReportBuilder (see NpcMissionReportBuilderTests) instead.
public class MissionReportSummaryBuilderTests
{
    private static MissionOutcomeClassification Classification(
        MissionType missionType,
        bool wasDetected = false,
        MissionForceDisposition disposition = MissionForceDisposition.Nominal,
        bool noViableTarget = false,
        bool targetLocated = false,
        bool targetEliminated = false,
        int enemiesKilled = 0,
        float impact = 0f) =>
        new()
        {
            MissionType = missionType,
            WasDetected = wasDetected,
            Disposition = disposition,
            NoViableTarget = noViableTarget,
            TargetLocated = targetLocated,
            TargetEliminated = targetEliminated,
            EnemiesKilled = enemiesKilled,
            Impact = impact
        };

    [Fact]
    public void BuildSummary_UndetectedRecon_ReportsUndetected()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(MissionType.Recon, wasDetected: false),
            "Sacred Ground, Terra");

        Assert.Contains("Your forces", summary);
        Assert.Contains("undetected", summary);
    }

    [Fact]
    public void BuildSummary_DetectedReconThatEscapes_ReportsBrokeContact()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(MissionType.Recon, wasDetected: true,
                disposition: MissionForceDisposition.BrokeContact),
            "Sacred Ground, Terra");

        Assert.Contains("detected", summary);
        Assert.Contains("broke contact", summary);
    }

    [Fact]
    public void BuildSummary_DetectedReconThatIsLost_ReportsLostContact()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(MissionType.Recon, wasDetected: true,
                disposition: MissionForceDisposition.LostContact),
            "Hive Sector, Baal");

        Assert.StartsWith("Your forces", summary);
        Assert.Contains("lost contact", summary);
    }

    [Fact]
    public void BuildSummary_CombatMissionWithKills_ReportsKillCount()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(MissionType.Advance, enemiesKilled: 7),
            "Iron Valley, Cadia");

        Assert.Contains("killed 7 enemy troops", summary);
    }

    [Fact]
    public void BuildSummary_CombatMissionWithKillsThenHeavyLosses_ReportsWithdrawalUnderFire()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(MissionType.Advance, enemiesKilled: 4,
                disposition: MissionForceDisposition.WithdrewUnderFire),
            "Iron Valley, Cadia");

        Assert.Contains("killed 4 enemy troops", summary);
        Assert.Contains("heavy losses", summary);
    }

    [Fact]
    public void BuildSummary_CombatMissionWithNoKillsAndNoTarget_ReportsNoTarget()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(MissionType.LightningRaid, noViableTarget: true),
            "Iron Valley, Cadia");

        Assert.Contains("no viable target", summary);
    }

    [Fact]
    public void BuildSummary_CombatMissionWithNoKills_ReportsUnconfirmedCasualties()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(MissionType.Advance),
            "Iron Valley, Cadia");

        Assert.Contains("without confirmed enemy casualties", summary);
    }

    [Fact]
    public void BuildSummary_SabotageWithPositiveImpact_ReportsSuccess()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(MissionType.Sabotage, impact: 2.5f),
            "Forge Complex, Mars");

        Assert.Contains("sabotaged enemy operations", summary);
    }

    [Fact]
    public void BuildSummary_SabotageWithNoImpact_ReportsNoEffect()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(MissionType.Sabotage, impact: -1f),
            "Forge Complex, Mars");

        Assert.Contains("without notable effect", summary);
    }

    [Fact]
    public void BuildSummary_AssassinationTargetEliminated_ReportsElimination()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(MissionType.Assassination, targetLocated: true,
                targetEliminated: true, enemiesKilled: 1),
            "Spire, Necromunda");

        Assert.Contains("eliminated the target", summary);
    }

    [Fact]
    public void BuildSummary_AssassinationTargetLocatedButNotEliminated_ReportsInconclusive()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(MissionType.Assassination, targetLocated: true),
            "Spire, Necromunda");

        Assert.Contains("located the target", summary);
        Assert.Contains("did not conclude cleanly", summary);
    }

    [Fact]
    public void BuildSummary_UnknownMissionType_FallsBackToGeneric()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(MissionType.Construction),
            "Scrapyard, Golgotha");

        Assert.Contains("Your forces", summary);
        Assert.Contains("Construction", summary);
    }

    [Fact]
    public void BuildSubject_ReturnsYourForces()
    {
        Assert.Equal("Your forces", MissionReportSummaryBuilder.BuildSubject());
    }
}
