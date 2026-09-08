using System;

namespace OnlyWar.Application;

/// <summary>
/// Session lifetime, recoverability and turn-gate control. The host asks whether the campaign can
/// be saved or ended and issues commands; campaign state stays inside the focused context.
/// </summary>
public sealed class SessionControlApplication : CampaignScreenApplication,
    ISessionControlApplication
{
    private SessionControlContext Screen => Context.SessionControl;

    public event EventHandler CampaignStatusChanged;

    public SessionControlApplication(CampaignApplicationContext context) : base(context)
    {
        Context.Recoverability.StateChanged += OnRecoverabilityChanged;
    }

    private void OnRecoverabilityChanged(object sender, EventArgs args) =>
        CampaignStatusChanged?.Invoke(this, EventArgs.Empty);

    public CampaignStatusView QueryStatus() =>
        Screen?.QueryStatus() ?? new CampaignStatusView(false, false, "Unknown Chapter");

    public void MarkChanged() => RecordChange();

    public SaveCampaignResult SaveCampaign(SaveCampaignCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!IsCurrentSession(command.SessionToken))
            return new(false, "The campaign changed. Reopen the save menu and try again.");

        return Screen?.SaveCampaign(command)
            ?? new(false, "The campaign changed. Reopen the save menu and try again.");
    }

    /// <summary>
    /// Writes the active campaign to an arbitrary path for a diagnostic bundle. It deliberately
    /// does not touch recoverability: this is a copy for support, not a recovery point.
    /// </summary>
    public void WriteDiagnosticCapture(string filePath) => Screen?.WriteDiagnosticCapture(filePath);

    public bool RequiresRecruitmentSetup() => Screen?.RequiresRecruitmentSetup() == true;

    public EndTurnPreflightReport QueryEndTurnPreflight(EndTurnWarningPreferences preferences) =>
        Screen?.QueryEndTurnPreflight(preferences) ?? new EndTurnPreflightReport([]);
}
