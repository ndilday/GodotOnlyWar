using System;
using OnlyWar.Helpers.Recruitment;
using OnlyWar.Helpers.Storage;
using OnlyWar.Helpers.Turns;
using OnlyWar.Models;
using OnlyWar.Models.Recruitment;

namespace OnlyWar.Application;

/// <summary>
/// Owns save, recoverability, and end-turn gate operations for one campaign.
/// </summary>
internal sealed class SessionControlContext
{
    private readonly Sector _sector;
    private readonly GameRulesData _rules;
    private readonly SaveGameManager _saveManager;
    private readonly CampaignRecoverabilityTracker _recoverability;
    private readonly Action<string> _save;

    internal SessionControlContext(
        Sector sector,
        GameRulesData rules,
        SaveGameManager saveManager,
        CampaignRecoverabilityTracker recoverability,
        Action<string> save)
    {
        _sector = sector ?? throw new ArgumentNullException(nameof(sector));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _saveManager = saveManager ?? throw new ArgumentNullException(nameof(saveManager));
        _recoverability = recoverability
            ?? throw new ArgumentNullException(nameof(recoverability));
        _save = save ?? throw new ArgumentNullException(nameof(save));
    }

    internal CampaignStatusView QueryStatus() =>
        new(true, _recoverability.IsDirty, CampaignName());

    internal SaveCampaignResult SaveCampaign(SaveCampaignCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Capture what is being saved before writing, so a failed write cannot mark a later state
        // recoverable. Only a successful write clears the dirty flag.
        CampaignRevision revision = _recoverability.CaptureRevision();
        try
        {
            SaveGameEntry entry = command.Kind switch
            {
                SaveCampaignKind.Manual => _saveManager.CreateManualSave(
                    command.DisplayName, CampaignName(), _save),
                SaveCampaignKind.Overwrite => _saveManager.OverwriteManualSave(
                    command.OverwriteFilePath, command.DisplayName, CampaignName(), _save),
                SaveCampaignKind.PostTurnAutosave =>
                    _saveManager.SavePostTurnAutosave(CampaignName(), _save),
                SaveCampaignKind.InitialAutosave =>
                    _saveManager.SaveInitialAutosave(CampaignName(), _save),
                _ => _saveManager.SaveProtectedPreTurn(CampaignName(), _save)
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

    internal void WriteDiagnosticCapture(string filePath) => _save(filePath);

    internal bool RequiresRecruitmentSetup() =>
        _sector.PlayerForce?.RecruitmentProgram
            is RecruitmentProgram { IsSetupComplete: false };

    internal EndTurnPreflightReport QueryEndTurnPreflight(
        EndTurnWarningPreferences preferences) =>
        EndTurnPreflight.EvaluateWithRules(
            _sector, preferences, _rules);

    private string CampaignName() =>
        _sector.PlayerForce?.Army?.OrderOfBattle?.Name
        ?? _sector.PlayerForce?.Army?.ForceName
        ?? "Unknown Chapter";
}
