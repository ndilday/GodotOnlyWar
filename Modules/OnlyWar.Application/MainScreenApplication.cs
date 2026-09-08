using System;

namespace OnlyWar.Application;

/// <summary>
/// The main workspace boundary. Campaign reads and mutations live in its focused context;
/// this service only applies session-token and recoverability policies.
/// </summary>
public sealed class MainScreenApplication : CampaignScreenApplication, IMainScreenApplication
{
    private const string StaleSessionMessage =
        "The campaign changed. Reopen the campaign and try again.";

    private MainScreenContext Screen => Context.Main;

    public MainScreenApplication(CampaignApplicationContext context) : base(context) { }

    public CampaignHeaderView QueryHeader() =>
        Screen?.QueryHeader() ?? new CampaignHeaderView("", 0);

    public MainScreenStartupView QueryStartup() =>
        Screen?.QueryStartup() ?? new MainScreenStartupView(null, false);

    public void AcknowledgeOpeningBrief(Guid sessionToken)
    {
        if (!IsCurrentSession(sessionToken)) return;
        if (Screen?.AcknowledgeOpeningBrief() == true)
        {
            RecordChange();
        }
    }

    public TurnReportView QueryLastTurnReport() =>
        Screen?.QueryLastTurnReport()
        ?? new TurnReportView(
            null,
            "No previous turn report is available for this save.",
            []);

    public ResolveTurnView ResolveTurn(Guid sessionToken)
    {
        if (!IsCurrentSession(sessionToken))
        {
            return new ResolveTurnView(false, StaleSessionMessage);
        }

        return Screen?.ResolveTurn()
            ?? new ResolveTurnView(false, StaleSessionMessage);
    }

    public NeophytePlacementOptions QueryNeophytePlacementTargets() =>
        Screen?.QueryNeophytePlacementTargets()
        ?? new NeophytePlacementOptions(
            false, "The Chapter has no active recruitment program.", []);

    public NeophytePlacementResult PlaceNeophyte(
        Guid sessionToken, int aspirantId, int squadId)
    {
        if (!IsCurrentSession(sessionToken))
        {
            return new NeophytePlacementResult(
                false, "The campaign changed. Reopen the recruitment screen and try again.");
        }

        NeophytePlacementResult result = Screen?.PlaceNeophyte(aspirantId, squadId)
            ?? new NeophytePlacementResult(
                false, "The campaign changed. Reopen the recruitment screen and try again.");
        if (result.Succeeded)
        {
            RecordChange();
        }
        return result;
    }
}
