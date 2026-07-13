using OnlyWar.Helpers;
using OnlyWar.Helpers.Missions;
using OnlyWar.Models.Missions;
using System;
using Xunit;

namespace OnlyWar.Tests.Turns;

// NpcMissionReportBuilder replaces the old binary intel gate for NPC-run missions (full detail above
// some threshold, nothing at zero) with three evidence channels - Contact, Aftermath, Surveillance -
// checked in priority order. These tests exercise the pure builder directly, same rationale as
// MissionReportSummaryBuilderTests: EndOfTurnDialogController is a Godot partial class and can't be
// instantiated headlessly.
public class NpcMissionReportBuilderTests
{
    private static MissionOutcomeClassification Classification(
        MissionType missionType = MissionType.Advance,
        bool wasDetected = false,
        MissionForceDisposition disposition = MissionForceDisposition.Nominal,
        bool targetEliminated = false,
        int enemiesKilled = 0,
        float impact = 0f) =>
        new()
        {
            MissionType = missionType,
            WasDetected = wasDetected,
            Disposition = disposition,
            TargetEliminated = targetEliminated,
            EnemiesKilled = enemiesKilled,
            Impact = impact
        };

    // --- Tier thresholds ---

    [Theory]
    [InlineData(0f, NpcReportTier.None)]
    [InlineData(0.01f, NpcReportTier.Movement)]
    [InlineData(1.99f, NpcReportTier.Movement)]
    [InlineData(2f, NpcReportTier.Identified)]
    [InlineData(3.99f, NpcReportTier.Identified)]
    [InlineData(4f, NpcReportTier.Assessment)]
    public void GetTier_MapsIntelToExpectedTier(float intel, NpcReportTier expected)
    {
        Assert.Equal(expected, NpcMissionReportBuilder.GetTier(intel));
    }

    // --- Null cases ---

    [Fact]
    public void Build_UndetectedSuccessfulMissionWithZeroIntel_ReturnsNull()
    {
        NpcMissionReport report = NpcMissionReportBuilder.Build(
            Classification(wasDetected: false, enemiesKilled: 3),
            spotterIsPlayerSide: false,
            targetIsPlayerSide: false,
            playerForcesEngaged: false,
            "Tyranids", "Imperial", "Iron Valley, Cadia", playerVisibleIntel: 0f);

        Assert.Null(report);
    }

    [Fact]
    public void Build_DetectedButSpotterNotPlayerSideWithZeroIntel_ReturnsNull()
    {
        NpcMissionReport report = NpcMissionReportBuilder.Build(
            Classification(wasDetected: true),
            spotterIsPlayerSide: false,
            targetIsPlayerSide: false,
            playerForcesEngaged: false,
            "Tyranids", "Ork Waaagh", "Iron Valley, Cadia", playerVisibleIntel: 0f);

        Assert.Null(report);
    }

    // --- Engagement channel ---
    // The player's own soldiers fought this mission's force directly (context.OpposingSquads contained
    // a player squad) - the strongest possible evidence, so it fires and names the faction regardless
    // of ambient intel, detection, or casualties.

    [Fact]
    public void Build_PlayerForcesEngaged_FiresEvenAtZeroIntelAndZeroKills()
    {
        NpcMissionReport report = NpcMissionReportBuilder.Build(
            Classification(wasDetected: false, enemiesKilled: 0),
            spotterIsPlayerSide: false,
            targetIsPlayerSide: false,
            playerForcesEngaged: true,
            "Tyranids", "Imperial", "Iron Valley, Cadia", playerVisibleIntel: 0f);

        Assert.NotNull(report);
        Assert.Equal("Enemy Attack", report.Title);
        Assert.Contains("Tyranids", report.Summary);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    [InlineData(2f)]
    [InlineData(4f)]
    public void Build_Engagement_NamesFactionAtEveryIntelTier(float intel)
    {
        NpcMissionReport report = NpcMissionReportBuilder.Build(
            Classification(),
            spotterIsPlayerSide: false,
            targetIsPlayerSide: false,
            playerForcesEngaged: true,
            "Tyranids", "Imperial", "Iron Valley, Cadia", playerVisibleIntel: intel);

        Assert.Contains("Tyranids", report.Summary);
    }

    [Fact]
    public void Build_Engagement_CasualtySentenceOnlyWhenEnemiesKilled()
    {
        NpcMissionReport noKills = NpcMissionReportBuilder.Build(
            Classification(enemiesKilled: 0),
            spotterIsPlayerSide: false,
            targetIsPlayerSide: false,
            playerForcesEngaged: true,
            "Tyranids", "Imperial", "Iron Valley, Cadia", playerVisibleIntel: 0f);
        NpcMissionReport withKills = NpcMissionReportBuilder.Build(
            Classification(enemiesKilled: 2),
            spotterIsPlayerSide: false,
            targetIsPlayerSide: false,
            playerForcesEngaged: true,
            "Tyranids", "Imperial", "Iron Valley, Cadia", playerVisibleIntel: 0f);

        Assert.DoesNotContain("Casualties were sustained", noKills.Summary);
        Assert.Contains("Casualties were sustained", withKills.Summary);
    }

    [Fact]
    public void Build_Engagement_BeatsContactAndAftermath()
    {
        // Detected (would fire Contact) AND a casualty aftermath against the player side (would fire
        // Aftermath) - Engagement must still win because the player's own squads fought directly.
        NpcMissionReport report = NpcMissionReportBuilder.Build(
            Classification(MissionType.Advance, wasDetected: true, enemiesKilled: 5),
            spotterIsPlayerSide: true,
            targetIsPlayerSide: true,
            playerForcesEngaged: true,
            "Tyranids", "Imperial", "Iron Valley, Cadia", playerVisibleIntel: 0f);

        Assert.Equal("Enemy Attack", report.Title);
        Assert.Contains("attacked by Tyranids", report.Summary);
    }

    // --- Contact channel ---

    [Fact]
    public void Build_Detected_SpotterIsPlayerSide_FiresContact()
    {
        NpcMissionReport report = NpcMissionReportBuilder.Build(
            Classification(wasDetected: true),
            spotterIsPlayerSide: true,
            targetIsPlayerSide: false,
            playerForcesEngaged: false,
            "Tyranids", "Imperial", "Iron Valley, Cadia", playerVisibleIntel: 0f);

        Assert.NotNull(report);
        Assert.Equal("Enemy Contact", report.Title);
        Assert.Contains("detected in Iron Valley, Cadia", report.Summary);
        Assert.Contains("purpose is unknown", report.Summary);
    }

    [Fact]
    public void Build_Contact_FactionNameWithheldBelowIdentifiedTier()
    {
        NpcMissionReport report = NpcMissionReportBuilder.Build(
            Classification(wasDetected: true),
            spotterIsPlayerSide: true,
            targetIsPlayerSide: false,
            playerForcesEngaged: false,
            "Tyranids", "Imperial", "Iron Valley, Cadia", playerVisibleIntel: 1f); // Movement tier

        Assert.DoesNotContain("Tyranids", report.Summary);
        Assert.Contains("unidentified", report.Summary);
    }

    [Fact]
    public void Build_Contact_FactionNameShownAtIdentifiedTierOrAbove()
    {
        NpcMissionReport report = NpcMissionReportBuilder.Build(
            Classification(wasDetected: true),
            spotterIsPlayerSide: true,
            targetIsPlayerSide: false,
            playerForcesEngaged: false,
            "Tyranids", "Imperial", "Iron Valley, Cadia", playerVisibleIntel: 2f); // Identified tier

        Assert.StartsWith("Tyranids", report.Summary);
    }

    [Theory]
    [InlineData(MissionForceDisposition.BrokeContact, "slipped away")]
    [InlineData(MissionForceDisposition.LostContact, "destroyed or scattered")]
    [InlineData(MissionForceDisposition.WithdrewUnderFire, "driven off with heavy losses")]
    public void Build_Contact_AppendsDispositionTail(MissionForceDisposition disposition, string expectedFragment)
    {
        NpcMissionReport report = NpcMissionReportBuilder.Build(
            Classification(wasDetected: true, disposition: disposition),
            spotterIsPlayerSide: true,
            targetIsPlayerSide: false,
            playerForcesEngaged: false,
            "Tyranids", "Imperial", "Iron Valley, Cadia", playerVisibleIntel: 0f);

        Assert.Contains(expectedFragment, report.Summary);
    }

    [Fact]
    public void Build_Contact_NominalDisposition_HasNoTail()
    {
        NpcMissionReport report = NpcMissionReportBuilder.Build(
            Classification(wasDetected: true, disposition: MissionForceDisposition.Nominal),
            spotterIsPlayerSide: true,
            targetIsPlayerSide: false,
            playerForcesEngaged: false,
            "Tyranids", "Imperial", "Iron Valley, Cadia", playerVisibleIntel: 0f);

        Assert.EndsWith("purpose is unknown.", report.Summary);
    }

    // --- Aftermath channel ---

    [Fact]
    public void Build_AssassinationTargetEliminated_FiresAftermath()
    {
        NpcMissionReport report = NpcMissionReportBuilder.Build(
            Classification(MissionType.Assassination, targetEliminated: true),
            spotterIsPlayerSide: false,
            targetIsPlayerSide: true,
            playerForcesEngaged: false,
            "Genestealer Cult", "Imperial", "Spire, Necromunda", playerVisibleIntel: 0f);

        Assert.Equal("Assassination", report.Title);
        Assert.Contains("Imperial leadership was found dead", report.Summary);
        Assert.Contains("Spire, Necromunda", report.Summary);
    }

    [Theory]
    [InlineData(MissionType.Sabotage)]
    [InlineData(MissionType.Diversion)]
    public void Build_SabotageOrDiversionWithImpact_FiresAftermath(MissionType missionType)
    {
        NpcMissionReport report = NpcMissionReportBuilder.Build(
            Classification(missionType, impact: 2.5f),
            spotterIsPlayerSide: false,
            targetIsPlayerSide: true,
            playerForcesEngaged: false,
            "Genestealer Cult", "Imperial", "Forge Complex, Mars", playerVisibleIntel: 0f);

        Assert.Equal("Sabotage Reported", report.Title);
        Assert.Contains("Explosions and sabotage damaged operations", report.Summary);
    }

    [Fact]
    public void Build_CombatMissionWithKillsAgainstPlayerSide_FiresAftermathCasualtyReport()
    {
        NpcMissionReport report = NpcMissionReportBuilder.Build(
            Classification(MissionType.Advance, enemiesKilled: 5),
            spotterIsPlayerSide: false,
            targetIsPlayerSide: true,
            playerForcesEngaged: false,
            "Tyranids", "Imperial", "Iron Valley, Cadia", playerVisibleIntel: 0f);

        Assert.Contains("came under attack and suffered casualties", report.Summary);
        Assert.DoesNotContain("Advance", report.Summary);
    }

    [Fact]
    public void Build_CombatMissionWithKillsAgainstThirdParty_DoesNotFireAftermathWithoutIntel()
    {
        // Target is a third faction, not player/default - with zero ambient intel there is no
        // evidence channel available at all.
        NpcMissionReport report = NpcMissionReportBuilder.Build(
            Classification(MissionType.Advance, enemiesKilled: 5),
            spotterIsPlayerSide: false,
            targetIsPlayerSide: false,
            playerForcesEngaged: false,
            "Tyranids", "Ork Waaagh", "Iron Valley, Cadia", playerVisibleIntel: 0f);

        Assert.Null(report);
    }

    // --- Surveillance channel ---

    [Fact]
    public void Build_MovementTier_ReportsGenericActivityWithNoFactionName()
    {
        NpcMissionReport report = NpcMissionReportBuilder.Build(
            Classification(),
            spotterIsPlayerSide: false,
            targetIsPlayerSide: false,
            playerForcesEngaged: false,
            "Tyranids", "Imperial", "Iron Valley, Cadia", playerVisibleIntel: 1f);

        Assert.Contains("Listening posts report enemy movement", report.Summary);
        Assert.DoesNotContain("Tyranids", report.Summary);
    }

    [Fact]
    public void Build_IdentifiedTier_ReportsFactionNameButNoInference()
    {
        NpcMissionReport report = NpcMissionReportBuilder.Build(
            Classification(),
            spotterIsPlayerSide: false,
            targetIsPlayerSide: false,
            playerForcesEngaged: false,
            "Tyranids", "Imperial", "Iron Valley, Cadia", playerVisibleIntel: 2f);

        Assert.Contains("Tyranids forces are active", report.Summary);
        Assert.DoesNotContain("Analysis:", report.Summary);
    }

    [Fact]
    public void Build_AssessmentTier_AddsInferenceSentence()
    {
        NpcMissionReport report = NpcMissionReportBuilder.Build(
            Classification(MissionType.LightningRaid),
            spotterIsPlayerSide: false,
            targetIsPlayerSide: false,
            playerForcesEngaged: false,
            "Tyranids", "Imperial", "Iron Valley, Cadia", playerVisibleIntel: 4f);

        Assert.Contains("Tyranids forces are active", report.Summary);
        Assert.Contains("Analysis: pattern consistent with", report.Summary);
        Assert.Contains("confidence: moderate", report.Summary);
    }

    // --- Priority order ---

    [Fact]
    public void Build_ContactBeatsAftermath_WhenBothCouldFire()
    {
        // Detected AND an eliminated assassination target - Contact must win, never revealing that
        // the mission was in fact a successful assassination.
        NpcMissionReport report = NpcMissionReportBuilder.Build(
            Classification(MissionType.Assassination, wasDetected: true, targetEliminated: true),
            spotterIsPlayerSide: true,
            targetIsPlayerSide: true,
            playerForcesEngaged: false,
            "Genestealer Cult", "Imperial", "Spire, Necromunda", playerVisibleIntel: 0f);

        Assert.Equal("Enemy Contact", report.Title);
    }

    [Fact]
    public void Build_AftermathBeatsSurveillance_WhenBothCouldFire()
    {
        // Not detected (no Contact), but a visible casualty aftermath against the player side at a
        // tier that would otherwise only justify ambient surveillance wording.
        NpcMissionReport report = NpcMissionReportBuilder.Build(
            Classification(MissionType.Advance, wasDetected: false, enemiesKilled: 5),
            spotterIsPlayerSide: false,
            targetIsPlayerSide: true,
            playerForcesEngaged: false,
            "Tyranids", "Imperial", "Iron Valley, Cadia", playerVisibleIntel: 4f);

        Assert.Contains("came under attack and suffered casualties", report.Summary);
        Assert.DoesNotContain("Analysis:", report.Summary);
    }

    // --- No raw MissionType leakage ---

    // Assassination/Sabotage/Diversion are excluded here: those aftermath titles are intentionally
    // the plain English word for the event (an assassination, a sabotage), which happens to be
    // identical to the enum's name - not a leak of classified intent, since the physical effect
    // itself (a dead leader, explosions) is what makes it observable in the first place.
    public static readonly MissionType[] NonAftermathMissionTypes =
    {
        MissionType.LightningRaid, MissionType.HitAndRun, MissionType.EstablishAirhead,
        MissionType.CloseAirSupport, MissionType.Recon, MissionType.Patrol, MissionType.Advance,
        MissionType.DeepStrike, MissionType.Fortify, MissionType.DefenseInDepth, MissionType.LastStand,
        MissionType.ObjectiveRaid, MissionType.Ambush, MissionType.Extermination, MissionType.Training,
        MissionType.Construction, MissionType.Infiltrate
    };

    public static TheoryData<MissionType, NpcReportTier> SurveillanceTierSweep()
    {
        TheoryData<MissionType, NpcReportTier> data = new();
        foreach (MissionType missionType in NonAftermathMissionTypes)
        {
            data.Add(missionType, NpcReportTier.Movement);
            data.Add(missionType, NpcReportTier.Identified);
            data.Add(missionType, NpcReportTier.Assessment);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(SurveillanceTierSweep))]
    public void Build_Surveillance_NeverLeaksRawMissionTypeName(MissionType missionType, NpcReportTier tier)
    {
        float intel = tier switch
        {
            NpcReportTier.Movement => 1f,
            NpcReportTier.Identified => 2f,
            NpcReportTier.Assessment => 4f,
            _ => throw new ArgumentOutOfRangeException(nameof(tier))
        };

        NpcMissionReport report = NpcMissionReportBuilder.Build(
            Classification(missionType),
            spotterIsPlayerSide: false,
            targetIsPlayerSide: false,
            playerForcesEngaged: false,
            "Tyranids", "Imperial", "Iron Valley, Cadia", playerVisibleIntel: intel);

        Assert.NotNull(report);
        Assert.DoesNotContain(missionType.ToString(), report.Title, StringComparison.Ordinal);
        Assert.DoesNotContain(missionType.ToString(), report.Subtitle, StringComparison.Ordinal);
        Assert.DoesNotContain(missionType.ToString(), report.Summary, StringComparison.Ordinal);
    }

    public static TheoryData<MissionType> AllMissionTypesData()
    {
        TheoryData<MissionType> data = new();
        foreach (MissionType missionType in Enum.GetValues(typeof(MissionType)))
        {
            data.Add(missionType);
        }
        return data;
    }

    // Engagement fires first and is checked over every MissionType, including the Assassination/
    // Sabotage/Diversion types excluded from NonAftermathMissionTypes above - unlike Aftermath's
    // titles, Engagement's wording never varies by mission type at all, so there is nothing
    // type-specific to leak.
    [Theory]
    [MemberData(nameof(AllMissionTypesData))]
    public void Build_Engagement_NeverLeaksRawMissionTypeName(MissionType missionType)
    {
        NpcMissionReport report = NpcMissionReportBuilder.Build(
            Classification(missionType, targetEliminated: true, enemiesKilled: 5, impact: 3f),
            spotterIsPlayerSide: true,
            targetIsPlayerSide: true,
            playerForcesEngaged: true,
            "Tyranids", "Imperial", "Iron Valley, Cadia", playerVisibleIntel: 0f);

        Assert.NotNull(report);
        Assert.Equal("Enemy Attack", report.Title);
        Assert.DoesNotContain(missionType.ToString(), report.Title, StringComparison.Ordinal);
        Assert.DoesNotContain(missionType.ToString(), report.Subtitle, StringComparison.Ordinal);
        Assert.DoesNotContain(missionType.ToString(), report.Summary, StringComparison.Ordinal);
    }
}
