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
        bool returnedToBase = false,
        bool remainedInTargetRegion = false,
        MissionForceDisposition disposition = MissionForceDisposition.Nominal,
        bool noViableTarget = false,
        bool targetLocated = false,
        bool targetEliminated = false,
        int enemiesKilled = 0,
        float impact = 0f,
        AmbushSpoilStage ambushSpoiled = AmbushSpoilStage.NotSpoiled,
        DefenseType? sabotageTarget = null,
        double sabotageDamage = 0.0,
        double sabotageLevelBefore = 0.0,
        int friendlyDeaths = 0,
        int friendlyIncapacitated = 0) =>
        new()
        {
            FriendlyDeaths = friendlyDeaths,
            FriendlyIncapacitated = friendlyIncapacitated,
            SabotageTarget = sabotageTarget,
            SabotageDamage = sabotageDamage,
            SabotageLevelBefore = sabotageLevelBefore,
            MissionType = missionType,
            WasDetected = wasDetected,
            ReturnedToBase = returnedToBase,
            RemainedInTargetRegion = remainedInTargetRegion,
            Disposition = disposition,
            NoViableTarget = noViableTarget,
            TargetLocated = targetLocated,
            TargetEliminated = targetEliminated,
            EnemiesKilled = enemiesKilled,
            Impact = impact,
            AmbushSpoiled = ambushSpoiled
        };

    // Design/Reference/CasualtyRealism.md §2.3: a brother carried off the field alive is a casualty,
    // not a kill, and the debrief has to say which. Before Phase 1 both read as "dead".
    [Fact]
    public void BuildFriendlyCasualtyLine_NamesKilledAndIncapacitatedSeparately()
    {
        string line = MissionReportSummaryBuilder.BuildFriendlyCasualtyLine(
            Classification(MissionType.Ambush, friendlyDeaths: 2, friendlyIncapacitated: 3));

        Assert.Contains("2 brothers killed", line);
        Assert.Contains("3 brought back incapacitated", line);
    }

    [Fact]
    public void BuildFriendlyCasualtyLine_OmitsTheHalfThatDidNotHappen()
    {
        Assert.Equal(
            " Chapter losses: 1 brought back incapacitated.",
            MissionReportSummaryBuilder.BuildFriendlyCasualtyLine(
                Classification(MissionType.Ambush, friendlyIncapacitated: 1)));
        Assert.Equal(
            " Chapter losses: 1 brother killed.",
            MissionReportSummaryBuilder.BuildFriendlyCasualtyLine(
                Classification(MissionType.Ambush, friendlyDeaths: 1)));
    }

    [Fact]
    public void BuildFriendlyCasualtyLine_IsSilentWhenTheOperationCostNothing()
    {
        Assert.Equal(
            "",
            MissionReportSummaryBuilder.BuildFriendlyCasualtyLine(
                Classification(MissionType.Recon)));
        Assert.Equal("", MissionReportSummaryBuilder.BuildFriendlyCasualtyLine(null));
    }

    // Design/Reference/SpecialistAttachment.md §8 trap 3: an attached Apothecary is in no BattleSquad,
    // so he is in no battle report, earns no field XP through the battle path, and would otherwise
    // leave the debrief with no evidence he was ever sent. This line is the evidence.
    [Fact]
    public void BuildFieldCareLine_NamesTheApothecaryAndWhatHeDid()
    {
        string line = MissionReportSummaryBuilder.BuildFieldCareLine(
            new MissionOutcomeClassification
            {
                MissionType = MissionType.Advance,
                FieldCareApothecaries = ["Apothecary Kadmon"],
                FieldCareTreatments = 5,
                FieldCareTreatedBrothers = 3
            });

        Assert.Contains("Apothecary Kadmon", line);
        Assert.Contains("3 brothers", line);
        Assert.Contains("5 wounds", line);
    }

    [Fact]
    public void BuildFieldCareLine_StillNamesHimWhenNobodyNeededTreating()
    {
        string line = MissionReportSummaryBuilder.BuildFieldCareLine(
            new MissionOutcomeClassification
            {
                MissionType = MissionType.Recon,
                FieldCareApothecaries = ["Apothecary Kadmon"]
            });

        Assert.Contains("Apothecary Kadmon", line);
        Assert.Contains("no field treatment", line);
    }

    [Fact]
    public void BuildFieldCareLine_IsSilentOnAnOrdinaryOrder()
    {
        Assert.Equal("", MissionReportSummaryBuilder.BuildFieldCareLine(
            Classification(MissionType.Advance)));
        Assert.Equal("", MissionReportSummaryBuilder.BuildFieldCareLine(null));
    }

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
    public void BuildSummary_Patrol_ReportsPatroledWithoutUndetectedQualifier()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(MissionType.Patrol, wasDetected: false),
            "Sacred Ground, Terra");

        Assert.Equal("Your forces patroled Sacred Ground, Terra.", summary);
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
    public void BuildSummary_AmbushThatExfiltrated_ReportsReturnToBase()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(MissionType.Ambush, returnedToBase: true, enemiesKilled: 7),
            "Iron Valley, Cadia");

        Assert.Contains("returned to base", summary);
    }

    [Fact]
    public void BuildSummary_NoTargetAmbushThatExfiltrated_ReportsReturnToBase()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(MissionType.Ambush, returnedToBase: true, noViableTarget: true),
            "Iron Valley, Cadia");

        Assert.Contains("no viable target", summary);
        Assert.Contains("returned to base", summary);
    }

    // A detected ambush still ends in a battle, and the kill-count sentence alone read exactly like a
    // successful one. These guard that the debrief names the detection and the fight that replaced it.
    [Fact]
    public void BuildSummary_AmbushSpottedInSetup_ReportsDetectionAndRegularEngagement()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(
                MissionType.Ambush,
                returnedToBase: true,
                enemiesKilled: 7,
                ambushSpoiled: AmbushSpoilStage.DuringSetup),
            "Iron Valley, Cadia");

        Assert.Contains("spotted moving into ambush positions", summary);
        Assert.Contains("regular engagement", summary);
        Assert.Contains("killed 7 enemy troops", summary);
        Assert.Contains("returned to base", summary);
    }

    [Fact]
    public void BuildSummary_AmbushDiscoveredBeforeSpringing_ReportsDetectionAndRegularEngagement()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(
                MissionType.Ambush,
                enemiesKilled: 3,
                disposition: MissionForceDisposition.WithdrewUnderFire,
                ambushSpoiled: AmbushSpoilStage.BeforeSpringing),
            "Iron Valley, Cadia");

        Assert.Contains("discovered before it could be sprung", summary);
        Assert.Contains("regular engagement", summary);
        Assert.Contains("killed 3 enemy troops", summary);
        Assert.Contains("heavy losses", summary);
    }

    [Fact]
    public void BuildSummary_SpoiledAmbushWithNoKills_StillReportsDetection()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(MissionType.Ambush, ambushSpoiled: AmbushSpoilStage.BeforeSpringing),
            "Iron Valley, Cadia");

        Assert.Contains("discovered before it could be sprung", summary);
        Assert.Contains("no enemy casualties were confirmed", summary);
    }

    [Fact]
    public void BuildSummary_SprungAmbush_MakesNoMentionOfDetection()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(MissionType.Ambush, returnedToBase: true, enemiesKilled: 7),
            "Iron Valley, Cadia");

        Assert.DoesNotContain("regular engagement", summary);
        Assert.DoesNotContain("discovered", summary);
    }

    [Fact]
    public void BuildSummary_AmbushUnableToExfiltrate_ReportsContinuedDeployment()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(
                MissionType.Ambush,
                remainedInTargetRegion: true,
                enemiesKilled: 7),
            "Iron Valley, Cadia");

        Assert.Contains("remained deployed there", summary);
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

    // Damage that leaves the position in the band it started in is "weakened" - and never a raw
    // level, which is fog-of-war the region screens would not give the player either.
    [Fact]
    public void BuildSummary_SabotageWithinOneBand_ReportsWeakened()
    {
        // 6.0 -> 5.2: both round into the Moderate band.
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(
                MissionType.Sabotage,
                impact: 2.5f,
                sabotageTarget: DefenseType.AntiAir,
                sabotageDamage: 0.8,
                sabotageLevelBefore: 6.0),
            "Forge Complex, Mars");

        Assert.Contains("enemy anti-air batteries", summary);
        Assert.Contains("weakening them", summary);
        Assert.DoesNotContain("5.2", summary);
        Assert.DoesNotContain("0.8", summary);
    }

    [Fact]
    public void BuildSummary_SabotageDroppingABand_ReportsSubstantialDamageAndNewBand()
    {
        // 6.0 (Moderate) -> 3.5 (Mediocre).
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(
                MissionType.Sabotage,
                impact: 2.5f,
                sabotageTarget: DefenseType.AntiAir,
                sabotageDamage: 2.5,
                sabotageLevelBefore: 6.0),
            "Forge Complex, Mars");

        Assert.Contains("substantial damage", summary);
        Assert.Contains("Mediocre", summary);
        Assert.DoesNotContain("3.5", summary);
    }

    // "the position now reads None" is not how a debrief speaks.
    [Fact]
    public void BuildSummary_SabotageFlatteningThePosition_ReportsDestruction()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(
                MissionType.Sabotage,
                impact: 2.5f,
                sabotageTarget: DefenseType.Entrenchment,
                sabotageDamage: 1.2,
                sabotageLevelBefore: 1.2),
            "Forge Complex, Mars");

        Assert.Contains("destroying them outright", summary);
        Assert.DoesNotContain("None", summary);
    }

    // A failed attempt still knows what it was sent against, so the debrief names the objective
    // rather than falling back to the generic "sabotage" wording.
    [Fact]
    public void BuildSummary_SabotageFailed_StillNamesTargetedWorks()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(
                MissionType.Sabotage,
                impact: -1f,
                sabotageTarget: DefenseType.ListeningPost),
            "Forge Complex, Mars");

        Assert.Contains("enemy listening posts", summary);
        Assert.Contains("without notable effect", summary);
    }

    // Impact says the charges went off; the aftermath measurement says nothing was standing to
    // take them. The report must not credit a level of damage it did not do.
    [Fact]
    public void BuildSummary_SabotageSucceededAgainstNothing_ReportsNoLevelsLost()
    {
        string summary = MissionReportSummaryBuilder.BuildSummary(
            Classification(
                MissionType.Sabotage,
                impact: 2.5f,
                sabotageTarget: DefenseType.Entrenchment,
                sabotageDamage: 0.0),
            "Forge Complex, Mars");

        Assert.Contains("enemy entrenchments", summary);
        Assert.Contains("little left to destroy", summary);
        Assert.DoesNotContain("levels", summary);
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
