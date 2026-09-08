using System;
using System.Collections.Generic;
using OnlyWar.Helpers.UI;

namespace OnlyWar.Application;

/// <summary>The Diplomacy board as detached rows.</summary>
public sealed record DiplomacyBoardView(Guid SessionToken, IReadOnlyList<TreeNode> Entries);

public interface IDiplomacyScreenApplication
{
    Guid SessionToken { get; }
    event EventHandler SessionChanged;

    DiplomacyBoardView QueryDiplomacy();
}

public sealed class DiplomacyScreenApplication : CampaignScreenApplication,
    IDiplomacyScreenApplication
{
    private readonly DiplomacyScreenProjector _diplomacyProjector = new();

    public DiplomacyScreenApplication(CampaignApplicationContext context) : base(context) { }

    public DiplomacyBoardView QueryDiplomacy() =>
        new(SessionToken, _diplomacyProjector.Build(ActiveSession?.Sector, ActiveSession?.Rules));
}
