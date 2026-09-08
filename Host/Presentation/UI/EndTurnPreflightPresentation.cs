namespace OnlyWar.Host.Presentation.UI;

public static class EndTurnPreflightPresentation
{
    public static string GetCategoryTitle(EndTurnWarningCategory category) => category switch
    {
        EndTurnWarningCategory.IdleDeployableSquads => "Idle deployed squads",
        EndTurnWarningCategory.LeaderlessSquads => "Squads without a leader",
        EndTurnWarningCategory.ActionableTaskForces => "Task forces awaiting orders",
        EndTurnWarningCategory.SpecialMissionOpportunities => "Opportunities at risk",
        EndTurnWarningCategory.RecruitmentProgram => "Recruitment program",
        _ => "Unresolved attention"
    };

    public static string GetPreferenceLabel(EndTurnWarningCategory category) => category switch
    {
        EndTurnWarningCategory.IdleDeployableSquads => "Warn about idle deployed squads",
        EndTurnWarningCategory.LeaderlessSquads => "Warn about squads missing a leader",
        EndTurnWarningCategory.ActionableTaskForces => "Warn about task forces without destinations",
        EndTurnWarningCategory.SpecialMissionOpportunities => "Warn about unassigned special missions",
        EndTurnWarningCategory.RecruitmentProgram => "Warn about recruitment decisions and funding",
        _ => "Warn about this category"
    };
}
