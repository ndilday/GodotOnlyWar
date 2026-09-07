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
public sealed partial class CampaignApplication : ISessionControlApplication
{
    private SaveGameManager _saves;
    private bool _statusSubscribed;

    public event EventHandler CampaignStatusChanged;

    /// <summary>
    /// Startup composition supplies the storage manager. Without it the application still runs but
    /// reports saving as unavailable rather than silently doing nothing.
    /// </summary>
    public void ConfigureStorage(SaveGameManager saves)
    {
        _saves = saves;
        SubscribeStatus();
    }

    private void SubscribeStatus()
    {
        if (_statusSubscribed) return;
        _recoverability.StateChanged += OnRecoverabilityChanged;
        _statusSubscribed = true;
    }

    private void OnRecoverabilityChanged(object sender, EventArgs args) =>
        CampaignStatusChanged?.Invoke(this, EventArgs.Empty);

    public CampaignStatusView QueryStatus()
    {
        SubscribeStatus();
        bool hasCampaign = _activeSession != null;
        return new CampaignStatusView(
            hasCampaign,
            hasCampaign && _recoverability.IsDirty,
            CampaignName());
    }

    public void MarkChanged() => _recoverability.MarkChanged();

    public SaveCampaignResult SaveCampaign(SaveCampaignCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_activeSession == null || command.SessionToken != SessionToken)
            return new(false, "The campaign changed. Reopen the save menu and try again.");
        if (_saves == null)
            return new(false, "Save storage is unavailable.");

        // Capture what is being saved before writing, so a failed write cannot mark a later state
        // recoverable. Only a successful write clears the dirty flag.
        CampaignRevision revision = _recoverability.CaptureRevision();
        try
        {
            SaveGameEntry entry = command.Kind switch
            {
                SaveCampaignKind.Manual => _saves.CreateManualSave(
                    command.DisplayName, CampaignName(), path => Save(path)),
                SaveCampaignKind.Overwrite => _saves.OverwriteManualSave(
                    command.OverwriteFilePath, command.DisplayName, CampaignName(), path => Save(path)),
                SaveCampaignKind.PostTurnAutosave =>
                    _saves.SavePostTurnAutosave(CampaignName(), path => Save(path)),
                SaveCampaignKind.InitialAutosave =>
                    _saves.SaveInitialAutosave(CampaignName(), path => Save(path)),
                _ => _saves.SaveProtectedPreTurn(CampaignName(), path => Save(path))
            };
            _recoverability.MarkSaveSucceeded(revision);
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
    public void WriteDiagnosticCapture(string filePath) => Save(filePath);

    public bool RequiresRecruitmentSetup() =>
        _activeSession?.Sector.PlayerForce?.RecruitmentProgram
            is RecruitmentProgram { IsSetupComplete: false };

    public EndTurnPreflightReport QueryEndTurnPreflight(EndTurnWarningPreferences preferences) =>
        _activeSession == null
            ? new EndTurnPreflightReport([])
            : EndTurnPreflight.EvaluateWithRules(
                _activeSession.Sector, preferences, _activeSession.Rules);

    private string CampaignName() =>
        _activeSession?.Sector.PlayerForce?.Army?.OrderOfBattle?.Name
        ?? _activeSession?.Sector.PlayerForce?.Army?.ForceName
        ?? "Unknown Chapter";
}
