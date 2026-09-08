using System;
using System.Collections.Generic;

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
    private CommandScreenContext Screen => Context.Command;

    public CommandScreenApplication(CampaignApplicationContext context) : base(context) { }

    public bool HasCampaign => Screen != null;

    public bool HasLastTurnReport => Screen?.HasLastTurnReport == true;

    public CommandBriefModel QueryBrief() =>
        Screen?.QueryBrief() ?? new CommandBriefModel([]);

    public ChronicleView QueryChronicle(ChronicleFilter filter, int page) =>
        Screen?.QueryChronicle(filter, page)
        ?? new ChronicleView([], ChronicleFilter.All, [], false, false);
}
