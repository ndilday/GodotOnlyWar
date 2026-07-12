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
// classification directly.
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
            true, "Player Chapter", "Sacred Ground, Terra");

        Assert.Contains("Your forces", summary);
        Assert.Contains("undetected", summary);
    }

    [Fact]
    public void BuildSummary_DetectedReconThatEscapes_ReportsBrokeContact()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(MissionType.Recon, wasDetected: true,
                disposition: MissionForceDisposition.BrokeContact),
            true, "Player Chapter", "Sacred Ground, Terra");

        Assert.Contains("detected", summary);
        Assert.Contains("broke contact", summary);
    }

    [Fact]
    public void BuildSummary_DetectedReconThatIsLost_ReportsLostContact()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(MissionType.Recon, wasDetected: true,
                disposition: MissionForceDisposition.LostContact),
            false, "Tyranid Swarm", "Hive Sector, Baal");

        Assert.StartsWith("Tyranid Swarm", summary);
        Assert.Contains("lost contact", summary);
    }

    [Fact]
    public void BuildSummary_CombatMissionWithKills_ReportsKillCount()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(MissionType.Advance, enemiesKilled: 7),
            true, "Player Chapter", "Iron Valley, Cadia");

        Assert.Contains("killed 7 enemy troops", summary);
    }

    [Fact]
    public void BuildSummary_CombatMissionWithKillsThenHeavyLosses_ReportsWithdrawalUnderFire()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(MissionType.Advance, enemiesKilled: 4,
                disposition: MissionForceDisposition.WithdrewUnderFire),
            true, "Player Chapter", "Iron Valley, Cadia");

        Assert.Contains("killed 4 enemy troops", summary);
        Assert.Contains("heavy losses", summary);
    }

    [Fact]
    public void BuildSummary_CombatMissionWithNoKillsAndNoTarget_ReportsNoTarget()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(MissionType.LightningRaid, noViableTarget: true),
            true, "Player Chapter", "Iron Valley, Cadia");

        Assert.Contains("no viable target", summary);
    }

    [Fact]
    public void BuildSummary_SabotageWithPositiveImpact_ReportsSuccess()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(MissionType.Sabotage, impact: 2.5f),
            true, "Player Chapter", "Forge Complex, Mars");

        Assert.Contains("sabotaged enemy operations", summary);
    }

    [Fact]
    public void BuildSummary_SabotageWithNoImpact_ReportsNoEffect()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(MissionType.Sabotage, impact: -1f),
            true, "Player Chapter", "Forge Complex, Mars");

        Assert.Contains("without notable effect", summary);
    }

    [Fact]
    public void BuildSummary_AssassinationTargetEliminated_ReportsElimination()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(MissionType.Assassination, targetLocated: true,
                targetEliminated: true, enemiesKilled: 1),
            true, "Player Chapter", "Spire, Necromunda");

        Assert.Contains("eliminated the target", summary);
    }

    [Fact]
    public void BuildSummary_AssassinationTargetLocatedButNotEliminated_ReportsInconclusive()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(MissionType.Assassination, targetLocated: true),
            true, "Player Chapter", "Spire, Necromunda");

        Assert.Contains("located the target", summary);
        Assert.Contains("did not conclude cleanly", summary);
    }

    [Fact]
    public void BuildSummary_UnknownMissionType_FallsBackToGeneric()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(MissionType.Construction),
            false, "Ork Waaagh", "Scrapyard, Golgotha");

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

    [Fact]
    public void ShouldIncludeInTurnSummary_PlayerMissionWithoutIntel_IsIncluded()
    {
        Assert.True(MissionReportSummaryBuilder.ShouldIncludeInTurnSummary(true, 0f));
    }

    [Fact]
    public void ShouldIncludeInTurnSummary_NpcMissionWithIntel_IsIncluded()
    {
        Assert.True(MissionReportSummaryBuilder.ShouldIncludeInTurnSummary(false, 0.01f));
    }

    [Fact]
    public void ShouldIncludeInTurnSummary_NpcMissionWithoutIntel_IsOmitted()
    {
        Assert.False(MissionReportSummaryBuilder.ShouldIncludeInTurnSummary(false, 0f));
    }
}
