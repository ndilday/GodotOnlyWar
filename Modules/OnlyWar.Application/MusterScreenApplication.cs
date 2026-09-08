using System;
using System.Collections.Generic;

namespace OnlyWar.Application;

public sealed class MusterScreenApplication : CampaignScreenApplication,
    IMusterScreenApplication
{
    private const string StaleSessionMessage = "The campaign changed. Reopen the Muster screen.";

    private MusterScreenContext Screen => Context.Muster;

    public MusterScreenApplication(CampaignApplicationContext context) : base(context) { }

    public bool HasChapter => Screen?.HasChapter == true;

    public IReadOnlyList<MusterScopeOption> QueryMusterScopes() =>
        Screen?.QueryMusterScopes() ?? [];

    public ChapterFilterOptions QueryMusterFilterOptions(int? scopeCompanyId) =>
        Screen?.QueryMusterFilterOptions(scopeCompanyId) ?? new ChapterFilterOptions([], []);

    public IReadOnlyList<MusterCandidateViewModel> QueryMusterCandidates(
        int? scopeCompanyId,
        MusterPopulationMode mode,
        IReadOnlyList<SoldierFilterCondition> filters) =>
        Screen?.QueryMusterCandidates(scopeCompanyId, mode, filters) ?? [];

    public IReadOnlyList<MusterFormationRow> QueryMusterFormations(int soldierId) =>
        Screen?.QueryMusterFormations(soldierId) ?? [];

    public MusterPreviewView QueryMusterPreview(int? soldierId, string formationSelectionKey) =>
        Screen?.QueryMusterPreview(soldierId, formationSelectionKey)
        ?? new MusterPreviewView(
            "Select a candidate and formation",
            "The preview will make the resulting role explicit.",
            null,
            false,
            "Select a candidate and a legal destination before staging a change.");

    public MusterPlanView QueryMusterPlan() =>
        Screen?.QueryMusterPlan()
        ?? new MusterPlanView(
            [],
            MusterPlanState.Empty,
            "PLAN EMPTY",
            "No changes are staged. Add at least one personnel action to begin a Muster plan.",
            "Stage a personnel action to evaluate logistics.",
            false,
            "Stage at least one change before review.",
            string.Empty);

    public int StagedActionCount => Screen?.StagedActionCount ?? 0;

    public bool IsStaged(int soldierId) => Screen?.IsStaged(soldierId) == true;

    public string DescribeStagedAction(int soldierId) =>
        Screen?.DescribeStagedAction(soldierId) ?? string.Empty;

    public Guid? StageMusterAction(Guid sessionToken, int soldierId, string formationSelectionKey)
    {
        if (!IsCurrentSession(sessionToken)) return null;
        return Screen?.StageMusterAction(sessionToken, soldierId, formationSelectionKey);
    }

    public bool UndoMusterAction(Guid sessionToken, Guid actionId) =>
        IsCurrentSession(sessionToken)
        && Screen?.UndoMusterAction(sessionToken, actionId) == true;

    public bool UndoLastMusterAction(Guid sessionToken) =>
        IsCurrentSession(sessionToken)
        && Screen?.UndoLastMusterAction(sessionToken) == true;

    public void ClearMusterPlan(Guid sessionToken)
    {
        if (IsCurrentSession(sessionToken))
        {
            Screen?.ClearMusterPlan(sessionToken);
        }
    }

    public MusterCommitResultView CommitMusterPlan(Guid sessionToken)
    {
        if (!IsCurrentSession(sessionToken))
        {
            return new MusterCommitResultView(false, StaleSessionMessage);
        }

        return Screen?.CommitMusterPlan(sessionToken)
            ?? new MusterCommitResultView(false, StaleSessionMessage);
    }
}
