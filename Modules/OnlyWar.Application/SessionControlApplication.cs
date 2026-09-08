using System;
using OnlyWar.Helpers.Recruitment;
using OnlyWar.Helpers.Settings;
using OnlyWar.Helpers.Storage;
using OnlyWar.Helpers.Turns;
using OnlyWar.Models;
using OnlyWar.Models.Recruitment;

namespace OnlyWar.Application;

/// <summary>
/// Session lifetime, recoverability and turn-gate control. The host asks whether the campaign can
/// be saved or ended and issues commands; it never reads the recoverability tracker or the campaign
/// graph to answer those questions itself.
/// </summary>
public sealed class SessionControlApplication : CampaignScreenApplication,
    ISessionControlApplication
{
    public event EventHandler CampaignStatusChanged;

    public SessionControlApplication(CampaignApplicationContext context) : base(context)
    {
        Context.Recoverability.StateChanged += OnRecoverabilityChanged;
    }

    private void OnRecoverabilityChanged(object sender, EventArgs args) =>
        CampaignStatusChanged?.Invoke(this, EventArgs.Empty);

    public CampaignStatusView QueryStatus()
    {
        bool hasCampaign = ActiveSession != null;
        return new CampaignStatusView(
            hasCampaign,
            hasCampaign && Context.Recoverability.IsDirty,
            CampaignName());
    }

    public void MarkChanged() => Context.MarkChanged();

    public SaveCampaignResult SaveCampaign(SaveCampaignCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (ActiveSession == null || command.SessionToken != SessionToken)
            return new(false, "The campaign changed. Reopen the save menu and try again.");
        // Capture what is being saved before writing, so a failed write cannot mark a later state
        // recoverable. Only a successful write clears the dirty flag.
        CampaignRevision revision = Context.Recoverability.CaptureRevision();
        try
        {
            SaveGameEntry entry = command.Kind switch
            {
                SaveCampaignKind.Manual => Services.Persistence.SaveManager.CreateManualSave(
                    command.DisplayName, CampaignName(), path => Context.Save(path)),
                SaveCampaignKind.Overwrite => Services.Persistence.SaveManager.OverwriteManualSave(
                    command.OverwriteFilePath, command.DisplayName, CampaignName(), path => Context.Save(path)),
                SaveCampaignKind.PostTurnAutosave =>
                    Services.Persistence.SaveManager.SavePostTurnAutosave(CampaignName(), path => Context.Save(path)),
                SaveCampaignKind.InitialAutosave =>
                    Services.Persistence.SaveManager.SaveInitialAutosave(CampaignName(), path => Context.Save(path)),
                _ => Services.Persistence.SaveManager.SaveProtectedPreTurn(CampaignName(), path => Context.Save(path))
            };
            Context.Recoverability.MarkSaveSucceeded(revision);
            return new(true, $"Campaign saved as {entry.DisplayName}.",
                entry.DisplayName, entry.LastWriteTimeLocal);
        }
        catch (Exception exception)
        {
            return new(false, exception.Message);
        }
    }

    /// <summary>
    /// Writes the active campaign to an arbitrary path for a diagnostic bundle. It deliberately
    /// does not touch recoverability: this is a copy for support, not a recovery point.
    /// </summary>
    public void WriteDiagnosticCapture(string filePath) => Context.Save(filePath);

    public bool RequiresRecruitmentSetup() =>
        ActiveSession?.Sector.PlayerForce?.RecruitmentProgram
            is RecruitmentProgram { IsSetupComplete: false };

    public EndTurnPreflightReport QueryEndTurnPreflight(EndTurnWarningPreferences preferences) =>
        ActiveSession == null
            ? new EndTurnPreflightReport([])
            : EndTurnPreflight.EvaluateWithRules(
                ActiveSession.Sector, preferences, ActiveSession.Rules);

    private string CampaignName() =>
        ActiveSession?.Sector.PlayerForce?.Army?.OrderOfBattle?.Name
        ?? ActiveSession?.Sector.PlayerForce?.Army?.ForceName
        ?? "Unknown Chapter";
}
