namespace OnlyWar.Helpers.Turns
{
    public sealed record RecruitmentTurnReport(
        bool Processed,
        string PausedReason,
        int RequisitionSpent,
        int ScreenedCandidates,
        int QualifiedCandidates,
        int AspirantsAdmitted,
        int ImplantationsCompleted,
        int AspirantDeaths,
        int CandidatesAgedOut);
}
