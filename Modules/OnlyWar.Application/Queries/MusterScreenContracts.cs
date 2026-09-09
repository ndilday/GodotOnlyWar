using System;
using System.Collections.Generic;

namespace OnlyWar.Application;

/// <summary>One entry in the Muster's scope selector; id 0 is the whole chapter.</summary>
public sealed record MusterScopeOption(int Id, string Label);

/// <summary>
/// One legal destination for the selected candidate. <c>SelectionKey</c> identifies this exact
/// option and <c>TargetKey</c> the formation it lands in, so a selection survives a rebuild even
/// when staging turns a new formation into a provisional one.
/// </summary>
public sealed record MusterFormationRow(
    FormationVacancyGroup Group,
    string GroupLabel,
    string FormationName,
    string StateLabel,
    string TypeLabel,
    string SquadIconKey,
    string RosterText,
    string RosterTooltip,
    string Location,
    string ResultingRole,
    string SelectionKey,
    string TargetKey,
    bool IsFull,
    bool IsStagedProjection,
    ProjectedSquadRowViewModel CommonRow);

/// <summary>The staging preview: what the move would do, and whether it can be staged.</summary>
public sealed record MusterPreviewView(
    string Title,
    string Detail,
    string StageButtonText,
    bool CanStage,
    string StageTooltip);

public sealed record MusterPlanRow(
    Guid ActionId,
    int SoldierId,
    string IconKey,
    string IconTooltip,
    string Text);

public enum MusterPlanState { Empty, Valid, Blocked }

public sealed record MusterPlanView(
    IReadOnlyList<MusterPlanRow> Rows,
    MusterPlanState State,
    string StatusLabel,
    string StatusTooltip,
    string ConstraintText,
    bool CanReview,
    string ReviewTooltip,
    string ReviewText);

public sealed record MusterCommitResultView(bool Succeeded, string ErrorText);

/// <summary>
/// The Bulk Transfer (Muster) workspace. The staged plan lives here, bound to this session, so a
/// campaign replaced underneath the screen cannot commit a plan drawn against the old roster.
/// </summary>
public interface IMusterScreenApplication
{
    Guid SessionToken { get; }
    event EventHandler SessionChanged;

    bool HasChapter { get; }

    IReadOnlyList<MusterScopeOption> QueryMusterScopes();

    ChapterFilterOptions QueryMusterFilterOptions(int? scopeCompanyId);

    IReadOnlyList<MusterCandidateViewModel> QueryMusterCandidates(
        int? scopeCompanyId,
        MusterPopulationMode mode,
        IReadOnlyList<SoldierFilterCondition> filters);

    IReadOnlyList<MusterFormationRow> QueryMusterFormations(int soldierId);

    MusterPreviewView QueryMusterPreview(int? soldierId, string formationSelectionKey);

    MusterPlanView QueryMusterPlan();

    int StagedActionCount { get; }

    bool IsStaged(int soldierId);

    /// <summary>The staged change already covering this soldier, for the "STAGED:" readout.</summary>
    string DescribeStagedAction(int soldierId);

    /// <summary>
    /// Stages one move. Returns the action id so the screen can reselect the provisional
    /// formation it just created, or null when the move is not legal.
    /// </summary>
    Guid? StageMusterAction(Guid sessionToken, int soldierId, string formationSelectionKey);

    bool UndoMusterAction(Guid sessionToken, Guid actionId);

    bool UndoLastMusterAction(Guid sessionToken);

    void ClearMusterPlan(Guid sessionToken);

    MusterCommitResultView CommitMusterPlan(Guid sessionToken);
}
