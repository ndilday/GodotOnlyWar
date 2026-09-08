using System;
using System.Collections.Generic;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;

namespace OnlyWar.Application;

public sealed class ChapterScreenApplication : CampaignScreenApplication,
    IChapterScreenApplication
{
    private ChapterScreenContext Screen => Context.Chapter;

    public ChapterScreenApplication(CampaignApplicationContext context) : base(context) { }

    public bool HasChapter => Screen?.HasChapter == true;

    public ChapterBrowserView QueryChapterBrowser(ChapterBrowserQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Screen?.QueryChapterBrowser(query)
            ?? ChapterScreenContext.BuildNoChapterView();
    }

    public ChapterFilterOptions QueryFilterOptions(ChapterBrowserQuery query) =>
        Screen?.QueryFilterOptions(query) ?? new ChapterFilterOptions([], []);

    public int? FindCompanyForSquad(int squadId) =>
        Screen?.FindCompanyForSquad(squadId);

    public bool CanNavigateToSquad(int squadId) =>
        Screen?.CanNavigateToSquad(squadId) == true;

    public ChapterPrompt DescribeTransfer(int soldierId, int optionIndex) =>
        Screen?.DescribeTransfer(soldierId, optionIndex) ?? ChapterPrompt.None;

    public ChapterTransferResult ConfirmTransfer(
        Guid sessionToken, int soldierId, int optionIndex, IReadOnlyList<int> contextSoldierIds)
    {
        if (!IsCurrentSession(sessionToken))
        {
            return EmptyTransferResult();
        }

        return Screen?.ConfirmTransfer(sessionToken, soldierId, optionIndex, contextSoldierIds)
            ?? EmptyTransferResult();
    }

    public ChapterPrompt DescribeRecall(int soldierId) =>
        Screen?.DescribeRecall(soldierId) ?? ChapterPrompt.None;

    public ChapterTransferResult ConfirmRecall(Guid sessionToken, int soldierId)
    {
        if (!IsCurrentSession(sessionToken))
        {
            return EmptyTransferResult();
        }

        return Screen?.ConfirmRecall(sessionToken, soldierId) ?? EmptyTransferResult();
    }

    // Kept as internal compatibility ports for the public CampaignApplication test surface.
    internal static IEnumerable<ISoldier> OrderFilteredSoldiers(IEnumerable<ISoldier> soldiers) =>
        ChapterScreenContext.OrderFilteredSoldiers(soldiers);

    internal static IEnumerable<Squad> OrderSquads(IEnumerable<Squad> squads) =>
        ChapterScreenContext.OrderSquads(squads);

    private static ChapterTransferResult EmptyTransferResult() =>
        new(false, ChapterPrompt.None, false, false, null, null);
}
