using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Domain;
using OnlyWar.Campaign.Command;
using OnlyWar.Domain;
using OnlyWar.Domain.Events;

namespace OnlyWar.Application;

/// <summary>
/// State and projections needed by the Command workspace.
/// </summary>
internal sealed class CommandScreenContext
{
    private readonly Sector _sector;
    private readonly GameRulesData _rules;
    private readonly Date _currentDate;
    private readonly CommandBriefBuilder _briefBuilder = new();

    internal CommandScreenContext(Sector sector, GameRulesData rules, Date currentDate)
    {
        _sector = sector ?? throw new ArgumentNullException(nameof(sector));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _currentDate = currentDate ?? throw new ArgumentNullException(nameof(currentDate));
    }

    internal bool HasLastTurnReport =>
        _sector.PlayerForce?.LastTurnReportSnapshot != null;

    internal CommandBriefModel QueryBrief()
    {
        PlayerForce force = _sector.PlayerForce;
        if (force == null) return new CommandBriefModel([]);

        return _briefBuilder.Build(
            _currentDate,
            _sector,
            _rules,
            force.LastTurnReportSnapshot,
            force.CampaignEventLedger.GetEventsInWeekRange(
                _currentDate.GetTotalWeeks(), _currentDate.GetTotalWeeks()));
    }

    internal ChronicleView QueryChronicle(ChronicleFilter filter, int page)
    {
        PlayerForce force = _sector.PlayerForce;
        if (force == null)
            return new ChronicleView([], ChronicleFilter.All, [], false, false);

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
                _sector, effective, page),
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
