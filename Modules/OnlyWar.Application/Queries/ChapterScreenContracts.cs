using System;
using System.Collections.Generic;
using OnlyWar.Helpers;

namespace OnlyWar.Application;

/// <summary>
/// What the chapter browser is looking at. Path, selection and the active filter are the screen's
/// own transient navigation state; the application turns them into one detached render.
/// </summary>
public sealed record ChapterBrowserQuery(
    int? CompanyId,
    int? SquadId,
    int? SoldierId,
    ChapterBrowserLevel? SelectedLevel,
    int? SelectedId,
    IReadOnlyList<SoldierFilterCondition> Filter,
    int? HistoricalSoldierId);

/// <summary>One complete chapter-browser render.</summary>
public sealed record ChapterBrowserView(
    bool HasChapter,
    IReadOnlyList<ChapterBreadcrumbItem> Breadcrumbs,
    string LeftMenuTitle,
    IReadOnlyList<ChapterBrowserMenuItem> LeftMenu,
    ChapterBrowserDetail Detail,
    int? DetailSoldierId,
    int? DetailSoldierSquadId,
    IReadOnlyList<string> TransferOptions,
    // Ordered soldier ids of the list on screen, so a transfer can advance to the next one.
    IReadOnlyList<int> ContextSoldierIds,
    // The soldier the selection resolved to, so the screen can retarget its own path.
    int? ResolvedSelectedSoldierId);

/// <summary>The role and honor vocabularies the filter dialog offers at the current scope.</summary>
public sealed record ChapterFilterOptions(
    IReadOnlyList<string> Roles,
    IReadOnlyList<SoldierHonorFilterOption> Honors);

public enum ChapterPromptKind
{
    /// <summary>Nothing to ask; the action is not available.</summary>
    None,
    /// <summary>An acknowledgement: the action cannot proceed.</summary>
    Blocked,
    /// <summary>A confirmation the player may accept.</summary>
    Confirm
}

public sealed record ChapterPrompt(ChapterPromptKind Kind, string Title, string Message)
{
    public static readonly ChapterPrompt None = new(ChapterPromptKind.None, null, null);
}

/// <summary>
/// A completed transfer or promotion. <see cref="DidTransfer"/> separates a transfer that moved
/// the soldier from a scheduled surgery, and the new posting is reported so a browser standing on
/// a squad that the move disbanded can follow him instead of rendering a vanished path.
/// </summary>
public sealed record ChapterTransferResult(
    bool Succeeded,
    ChapterPrompt Acknowledgement,
    bool DidTransfer,
    bool OriginSquadRemoved,
    int? NewCompanyId,
    int? NewSquadId);

/// <summary>
/// The Chapter overview. The browser path, the staged filter and the pending confirmation stay in
/// the screen; every roster fact, transfer rule and campaign write lives here.
/// </summary>
public interface IChapterScreenApplication
{
    Guid SessionToken { get; }
    event EventHandler SessionChanged;

    bool HasChapter { get; }

    ChapterBrowserView QueryChapterBrowser(ChapterBrowserQuery query);

    ChapterFilterOptions QueryFilterOptions(ChapterBrowserQuery query);

    /// <summary>The company a squad belongs to, or null for a chapter-level command squad.</summary>
    int? FindCompanyForSquad(int squadId);

    /// <summary>Where a squad can be opened on the map, if anywhere.</summary>
    bool CanNavigateToSquad(int squadId);

    /// <summary>What confirming this transfer option would mean, or why it is refused.</summary>
    ChapterPrompt DescribeTransfer(int soldierId, int optionIndex);

    ChapterTransferResult ConfirmTransfer(
        Guid sessionToken, int soldierId, int optionIndex, IReadOnlyList<int> contextSoldierIds);

    ChapterPrompt DescribeRecall(int soldierId);

    ChapterTransferResult ConfirmRecall(Guid sessionToken, int soldierId);
}
