using System.Collections.Generic;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Squads;

namespace OnlyWar.Application
{
    public enum RecruitmentPolicyChoice
    {
        VoluntaryPresentation = 0,
        PlanetaryTithe = 1
    }

    public sealed record RecruitmentScreenRulesView(
        int MinimumAttributeFilterHalfSteps,
        int MaximumAttributeFilterHalfSteps,
        double AttributeFilterStepSigma)
    {
        public static RecruitmentScreenRulesView Default { get; } = new(-4, 6, 0.5);
    }

    public sealed record RecruitmentForecastView(
        double ChildrenReachingRecruitmentAge,
        double EligibleMaleCohort,
        double UnscreenedBacklog,
        double ScreeningDemand,
        double NonGeneticScreeningCapacity,
        double GeneticScreeningCapacity,
        double SpiritualScreeningCapacity,
        double ScreeningCapacity,
        double ScreeningCoverage,
        double ExpectedScreenedCandidates,
        double PublicCompliance,
        double WeeklyPublicSentimentChange,
        double ExpectedCompliantCandidates,
        double GeneticPassRate,
        double AttributePassRate,
        double ExpectedQualifiedCandidates,
        int AspirantTrainingCapacity,
        int AvailablePhaseZeroPlaces,
        int QualifiedCandidateWaitlist,
        int AvailablePhaseZeroPlacesAfterWaitlist,
        double ExpectedNewPhaseZeroAdmissions,
        double ExpectedCandidateOverflow,
        double ExpectedPhase12Survivors,
        double ExpectedPhase13BattleBrothers,
        double ExpectedPhase12SurvivalRate,
        double ExpectedPhase13SurvivalRate,
        int WeeklyRequisitionCost,
        double SourceAttributeMeanModifierSigma);

    public sealed record ScoutTrainingOptionView(string Key, string DisplayName);

    public sealed record RecruitmentStaffSummary(
        int ScoutSergeants,
        int Apothecaries,
        int Chaplains)
    {
        public bool IsComplete =>
            ScoutSergeants > 0 && Apothecaries > 0 && Chaplains > 0;
    }

    /// <summary>
    /// The recruitment doctrine the player is staging. It is a plain value, so the screen can hold
    /// an unsaved edit without touching the live program.
    /// </summary>
    public sealed record RecruitmentDoctrineDraft(
        RecruitmentPolicyChoice Policy,
        int StrengthHalfSigmaSteps,
        int ConstitutionHalfSigmaSteps,
        int IntelligenceHalfSigmaSteps,
        int DexterityHalfSigmaSteps,
        int EgoHalfSigmaSteps,
        float MinimumGeneticCompatibility)
    {
        // Keep integration callers source-compatible while the screen boundary uses its own
        // value vocabulary. The campaign policy is translated back inside Application commands.
        public RecruitmentDoctrineDraft(
            RecruitmentPolicy policy,
            int strengthHalfSigmaSteps,
            int constitutionHalfSigmaSteps,
            int intelligenceHalfSigmaSteps,
            int dexterityHalfSigmaSteps,
            int egoHalfSigmaSteps,
            float minimumGeneticCompatibility)
            : this(
                (RecruitmentPolicyChoice)policy,
                strengthHalfSigmaSteps,
                constitutionHalfSigmaSteps,
                intelligenceHalfSigmaSteps,
                dexterityHalfSigmaSteps,
                egoHalfSigmaSteps,
                minimumGeneticCompatibility)
        {
        }
    }

    public sealed record RecruitmentAspirantRow(
        int Id,
        string Designation,
        string Phase,
        string Age,
        float TrainingProgress,
        bool CanBecomeNeophyte);

    public sealed record RecruitmentCandidateRow(
        int Id,
        string Designation,
        string Age,
        float GeneticCompatibility);

    public sealed record ScoutPromotionRow(
        int SoldierId,
        string Name,
        bool IsReady);

    public sealed record ScoutSquadRow(
        int Id,
        string Label,
        string TrainingOptionKey,
        string ReadinessReport,
        IReadOnlyList<ScoutPromotionRow> PromotionRows,
        SquadRowViewModel CommonRow = null);

    public sealed class RecruitmentScreenSnapshot
    {
        public bool IsUnlocked { get; init; }
        /// <summary>Why the 10th Company screen is locked; empty once a program exists.</summary>
        public string LockedMessage { get; init; } = string.Empty;
        public bool IsSetupComplete { get; init; }
        public string HomeWorldName { get; init; }
        public long ChapterPopulation { get; init; }
        public int Requisition { get; init; }
        public ushort Geneseed { get; init; }
        public RecruitmentStaffSummary Staff { get; init; }
        public RecruitmentDoctrineDraft Doctrine { get; init; }
        public RecruitmentForecastView Forecast { get; init; }
        public RecruitmentScreenRulesView Rules { get; init; } = RecruitmentScreenRulesView.Default;
        public IReadOnlyList<RecruitmentCandidateRow> Candidates { get; init; } = [];
        public IReadOnlyList<RecruitmentAspirantRow> Aspirants { get; init; } = [];
        public IReadOnlyList<ScoutTrainingOptionView> ScoutTrainingOptions { get; init; } = [];
        public IReadOnlyList<ScoutSquadRow> ScoutSquads { get; init; } = [];
        public IReadOnlyList<string> RecentEvents { get; init; } = [];
    }
}
