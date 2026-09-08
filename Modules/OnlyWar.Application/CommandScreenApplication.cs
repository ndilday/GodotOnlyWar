using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Command;
using OnlyWar.Models;
using OnlyWar.Models.Events;

namespace OnlyWar.Application;

/// <summary>Command Brief and Chapter Chronicle projections for the Command workspace.</summary>
public sealed record ChronicleFilterOption(ChronicleFilter Filter, string Label, int Count);

public sealed record ChronicleView(
    IReadOnlyList<ChronicleFilterOption> Filters,
    ChronicleFilter Filter,
    IReadOnlyList<ChronicleEntryViewModel> Entries,
    bool HasOlder,
    bool HasAnyEntries);

public interface ICommandScreenApplication
{
    Guid SessionToken { get; }
    event EventHandler SessionChanged;

    bool HasCampaign { get; }
    bool HasLastTurnReport { get; }

    /// <summary>The live brief for the current turn, built from the session the caller is on.</summary>
    CommandBriefModel QueryBrief();

    ChronicleView QueryChronicle(ChronicleFilter filter, int page);
}

public sealed class CommandScreenApplication : CampaignScreenApplication,
    ICommandScreenApplication
{
    private readonly CommandBriefBuilder _briefBuilder = new();

    public CommandScreenApplication(CampaignApplicationContext context) : base(context) { }

    public bool HasCampaign => ActiveSession != null;

    public bool HasLastTurnReport =>
        ActiveSession?.Sector.PlayerForce?.LastTurnReportSnapshot != null;

    public CommandBriefModel QueryBrief()
    {
        if (ActiveSession == null) return new CommandBriefModel([]);
        Sector sector = ActiveSession.Sector;
        Date date = ActiveSession.CurrentDate;
        return _briefBuilder.Build(
            date,
            sector,
            ActiveSession.Rules,
            sector.PlayerForce.LastTurnReportSnapshot,
            sector.PlayerForce.CampaignEventLedger.GetEventsInWeekRange(
                date.GetTotalWeeks(), date.GetTotalWeeks()));
    }

    public ChronicleView QueryChronicle(ChronicleFilter filter, int page)
    {
        if (ActiveSession == null)
            return new ChronicleView([], ChronicleFilter.All, [], false, false);
        PlayerForce force = ActiveSession.Sector.PlayerForce;
        IReadOnlyList<ChronicleFilter> available = ChapterChronicleBrowser.GetAvailableFilters(
            force.ChapterChronicle, force.CampaignEventLedger);
        // A filter the campaign no longer offers falls back to All rather than showing nothing.
        ChronicleFilter effective = available.Contains(filter) ? filter : ChronicleFilter.All;
        return new ChronicleView(
            available.Select(candidate => new ChronicleFilterOption(
                candidate,
                ChronicleFilterLabel(candidate),
                ChapterChronicleBrowser.Count(
                    force.ChapterChronicle, force.CampaignEventLedger, candidate))).ToList(),
            effective,
            ChapterChronicleBrowser.GetPage(
                force.ChapterChronicle, force.CampaignEventLedger,
                ActiveSession.Sector, effective, page),
            ChapterChronicleBrowser.HasPage(
                force.ChapterChronicle, force.CampaignEventLedger, effective, page + 1),
            force.ChapterChronicle.Entries.Count > 0);
    }

    private static string ChronicleFilterLabel(ChronicleFilter filter) => filter switch
    {
        ChronicleFilter.Defining => "Defining",
        ChronicleFilter.Battles => "Battles",
        ChronicleFilter.Brothers => "Brothers",
        ChronicleFilter.Worlds => "Worlds",
        ChronicleFilter.Chapter => "Chapter",
        _ => "All"
    };
}
