using System.Collections.Generic;

namespace OnlyWar.Application;

/// <summary>
/// The mandate chooser, the active mandate, the legacy record and Chapter Master succession
/// (TDD §6.15 *Application*). Every value is a presentation snapshot.
/// </summary>
public sealed record MandateLinkView(string Label, CampaignNavigationTarget Target);

/// <summary>One mandate the chooser offers. Family groups the list (§4 step 2).</summary>
public sealed record MandateOptionView(
    string Key,
    string Family,
    string Title,
    string Goal,
    int DeadlineWeeks,
    string DeadlineLabel,
    int LegacyValue,
    int FailureCost,
    IReadOnlyList<MandateLinkView> Links);

public sealed record ActiveMandateView(
    int Id,
    string Family,
    string Title,
    string Progress,
    string DeadlineLabel,
    int? WeeksLeft,
    int LegacyValue,
    int FailureCost,
    bool IsPromisedWorld,
    string AbandonWarning,
    IReadOnlyList<MandateLinkView> Links);

/// <summary>A sworn mandate that has resolved.</summary>
public sealed record MandateHistoryView(
    int Id,
    string Title,
    string Result,
    string SwornLabel,
    string ResolvedLabel,
    int LegacyAmount);

/// <summary>The Chapter's legacy after each week that changed it, for the graph (§6.3).</summary>
public sealed record LegacyPointView(int Week, string DateLabel, int Total, string MarkerLabel);

/// <summary>One Chapter Master's tenure and the legacy earned and lost under him.</summary>
public sealed record LegacyMasterView(int? SoldierId, string Name, string TenureLabel, int Gained, int Lost)
{
    public int Total => Gained + Lost;
}

public sealed record LegacyEntryView(
    int Week,
    string DateLabel,
    int Amount,
    string Source,
    string Description,
    string MasterName);

public sealed record MandateBoardView(
    bool HasCampaign,
    ActiveMandateView Active,
    IReadOnlyList<MandateOptionView> Options,
    IReadOnlyList<MandateHistoryView> History,
    int ChapterLegacy,
    IReadOnlyList<LegacyPointView> LegacySeries,
    IReadOnlyList<LegacyMasterView> Masters,
    IReadOnlyList<LegacyEntryView> RecentEntries,
    string ChapterWorldName,
    string RenounceWarning)
{
    public static readonly MandateBoardView Empty = new(
        false, null, [], [], 0, [], [], [], null, null);

    public bool CanRenounceChapterWorld => ChapterWorldName != null;
}

public sealed record MandateCommandResult(bool Succeeded, string Message);

public sealed record ChapterMasterCandidateView(
    int SoldierId,
    string Name,
    string SquadName,
    bool IsDutyReady);

/// <summary>Who holds the office of Chapter Master, and who may take it when it is empty.</summary>
public sealed record ChapterMasterSuccessionView(
    bool IsVacant,
    string HolderName,
    IReadOnlyList<ChapterMasterCandidateView> Candidates,
    string Guidance);
