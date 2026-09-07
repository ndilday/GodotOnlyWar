using System.Collections.Generic;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Squads;

namespace OnlyWar.Helpers.UI
{
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
        RecruitmentPolicy Policy,
        int StrengthHalfSigmaSteps,
        int ConstitutionHalfSigmaSteps,
        int IntelligenceHalfSigmaSteps,
        int DexterityHalfSigmaSteps,
        int EgoHalfSigmaSteps,
        float MinimumGeneticCompatibility);

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
        public RecruitmentForecast Forecast { get; init; }
        public IReadOnlyList<RecruitmentCandidateRow> Candidates { get; init; } = [];
        public IReadOnlyList<RecruitmentAspirantRow> Aspirants { get; init; } = [];
        public IReadOnlyList<ScoutTrainingOption> ScoutTrainingOptions { get; init; } = [];
        public IReadOnlyList<ScoutSquadRow> ScoutSquads { get; init; } = [];
        public IReadOnlyList<string> RecentEvents { get; init; } = [];
    }
}
