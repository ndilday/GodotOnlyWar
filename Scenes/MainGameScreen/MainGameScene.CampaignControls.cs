using Godot;
using OnlyWar.Helpers.Diagnostics;
using OnlyWar.Helpers.Settings;
using OnlyWar.Helpers.Storage;
using OnlyWar.Helpers.Turns;
using OnlyWar.Helpers.UI.SystemMenu;
using OnlyWar.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

public partial class MainGameScene
{
    private enum PendingNavigationKind
    {
        None,
        Load,
        ReturnToTitle,
        Quit
    }

    private SystemMenuController _systemMenu;
    private SaveLoadChooserController _saveLoadChooser;
    private DestructiveNavigationDialog _destructiveNavigationDialog;
    private DiagnosticsExportDialog _diagnosticsExportDialog;
    private TransientFeedbackOverlay _feedbackOverlay;
    private EndTurnPreflightDialog _endTurnPreflightDialog;
    private SaveGameManager _saveGameManager;
    private SaveGameCatalog _saveCatalog;
    private EndTurnWarningPreferencesRepository _warningPreferencesRepository;
    private EndTurnWarningPreferences _warningPreferences;
    private PendingNavigationKind _pendingNavigation;
    private string _pendingLoadPath;
    private string _pendingLoadName;
    private string _startupWarning;
    private string _lastSaveStatus;

    internal bool IsSystemMenuVisibleForSmoke => _systemMenu?.Visible == true;
    internal SaveChooserMode? VisibleSaveChooserModeForSmoke =>
        _saveLoadChooser?.Visible == true ? _saveLoadChooser.Mode : null;
    internal bool IsEndTurnPreflightVisibleForSmoke => _endTurnPreflightDialog?.Visible == true;

    internal void SetStartupWarning(string warning)
    {
        _startupWarning = warning;
    }

    public override void _ExitTree()
    {
        GameDataSingleton.Instance.Recoverability.StateChanged -= OnRecoverabilityStateChanged;
    }

    private void InitializeCampaignControls()
    {
        GetTree().AutoAcceptQuit = false;

        _systemMenu = AddCampaignControl<SystemMenuController>(
            "res://Scenes/SystemMenu/system_menu.tscn");
        _systemMenu.SetContext(SystemMenuContext.Campaign);
        _saveLoadChooser = AddCampaignControl<SaveLoadChooserController>(
            "res://Scenes/SystemMenu/save_load_chooser.tscn");
        _destructiveNavigationDialog = AddCampaignControl<DestructiveNavigationDialog>(
            "res://Scenes/SystemMenu/destructive_navigation_dialog.tscn");
        _diagnosticsExportDialog = AddCampaignControl<DiagnosticsExportDialog>(
            "res://Scenes/SystemMenu/diagnostics_export_dialog.tscn");
        _feedbackOverlay = AddCampaignControl<TransientFeedbackOverlay>(
            "res://Scenes/SystemMenu/transient_feedback_overlay.tscn");
        _endTurnPreflightDialog = AddCampaignControl<EndTurnPreflightDialog>(
            "res://Scenes/MainGameScreen/end_turn_preflight_dialog.tscn");

        _systemMenu.ResumeRequested += OnSystemMenuResumeRequested;
        _systemMenu.SaveRequested += OnSystemMenuSaveRequested;
        _systemMenu.LoadRequested += OnSystemMenuLoadRequested;
        _systemMenu.ReturnToTitleRequested += OnReturnToTitleRequested;
        _systemMenu.QuitRequested += OnQuitRequested;
        _systemMenu.ExportDiagnosticsRequested += OnExportDiagnosticsRequested;
        _systemMenu.WarningPreferencesChanged += OnWarningPreferencesChanged;

        _saveLoadChooser.CancelRequested += OnSaveChooserCancelled;
        _saveLoadChooser.RefreshRequested += OnSaveChooserRefreshRequested;
        _saveLoadChooser.SaveRequested += OnManualSaveRequested;
        _saveLoadChooser.LoadRequested += OnSelectedSaveLoadRequested;
        _saveLoadChooser.DeleteRequested += OnManualSaveDeleteRequested;

        _destructiveNavigationDialog.SaveAndContinueRequested += OnSaveAndContinueRequested;
        _destructiveNavigationDialog.DiscardAndContinueRequested += OnDiscardAndContinueRequested;
        _destructiveNavigationDialog.Cancelled += OnDestructiveNavigationCancelled;

        _diagnosticsExportDialog.CancelRequested += OnDiagnosticsCancelled;
        _diagnosticsExportDialog.ExportRequested += OnDiagnosticsExportRequested;

        _endTurnPreflightDialog.EndTurnAnywayPressed += OnEndTurnAnywayPressed;
        _endTurnPreflightDialog.CancelPressed += OnEndTurnPreflightCancelled;
        _endTurnPreflightDialog.WarningPreferencesChanged += OnPreflightPreferencesChanged;

        _warningPreferencesRepository = EndTurnWarningPreferencesRepository.CreateDefault();
        _warningPreferences = _warningPreferencesRepository.Load();
        ApplyWarningPreferencesToMenu();

        try
        {
            GameStorage.InitializeUserStorage();
            _saveGameManager = new SaveGameManager(GameStorage.SaveDirectory);
            _saveCatalog = new SaveGameCatalog(GameStorage.SaveDirectory);
        }
        catch (Exception exception)
        {
            GD.PushError($"Save storage is unavailable: {exception}");
            _lastSaveStatus = "Save storage is unavailable. See the game log for details.";
        }

        GameDataSingleton.Instance.Recoverability.StateChanged += OnRecoverabilityStateChanged;
        UpdateSystemMenuState();

        if (!string.IsNullOrWhiteSpace(_startupWarning))
        {
            _feedbackOverlay.ShowError(_startupWarning, 8.0);
        }
    }

    private T AddCampaignControl<T>(string scenePath) where T : Control
    {
        PackedScene scene = GD.Load<PackedScene>(scenePath)
            ?? throw new InvalidOperationException($"Could not load required UI scene {scenePath}.");
        T control = scene.Instantiate<T>();
        _mainUILayer.AddChild(control);
        control.Visible = false;
        return control;
    }

    private void OnSystemOptionsButtonPressed(object sender, EventArgs e)
    {
        ToggleSystemMenu();
    }

    private bool HandleGlobalCampaignInput(InputEvent inputEvent)
    {
        if (inputEvent.IsActionPressed("ui_cancel"))
        {
            if (_isProcessingTurn)
            {
                return true;
            }

            ToggleSystemMenu();
            return true;
        }

        if (inputEvent is not InputEventKey keyEvent
            || !keyEvent.Pressed
            || keyEvent.Echo
            || (keyEvent.Keycode != Key.X && keyEvent.PhysicalKeycode != Key.X))
        {
            return false;
        }

        Control focus = GetViewport().GuiGetFocusOwner();
        if (focus is LineEdit or TextEdit)
        {
            return false;
        }

        // Escape owns the System Menu. X is reserved for closing the top gameplay surface.
        if (_systemMenu?.Visible == true)
        {
            return false;
        }

        return CloseTopmostGameplaySurface();
    }

    private void ToggleSystemMenu()
    {
        if (_systemMenu == null)
        {
            return;
        }

        if (_systemMenu.Visible)
        {
            CloseSystemMenuAndChildren();
            return;
        }

        UpdateSystemMenuState();
        _systemMenu.ShowMenu();
    }

    private bool CloseTopmostGameplaySurface()
    {
        IReadOnlyList<Node> blockers = GetTree()
            .GetNodesInGroup(DialogController.DialogInputBlockerGroup)
            .Where(node => node is CanvasItem item && item.IsVisibleInTree())
            .ToList();

        for (int index = blockers.Count - 1; index >= 0; index--)
        {
            switch (blockers[index])
            {
                case DialogController dialog:
                    dialog.RequestClose();
                    return true;
            }
        }

        if (_soldierScreen?.Visible == true)
        {
            OnSoldierViewCloseButtonPressed(_soldierScreen, EventArgs.Empty);
            return true;
        }
        if (_trainingUnitScreen?.Visible == true)
        {
            OnCloseScreen(_trainingUnitScreen, EventArgs.Empty);
            return true;
        }
        if (_chapterScreen?.Visible == true)
        {
            OnCloseScreen(_chapterScreen, EventArgs.Empty);
            return true;
        }

        return false;
    }

    private void OnSystemMenuResumeRequested(object sender, EventArgs e)
    {
        CloseSystemMenuAndChildren();
    }

    private void CloseSystemMenuAndChildren()
    {
        if (_saveLoadChooser != null) _saveLoadChooser.Visible = false;
        if (_destructiveNavigationDialog != null) _destructiveNavigationDialog.Visible = false;
        if (_diagnosticsExportDialog != null) _diagnosticsExportDialog.Visible = false;
        _pendingNavigation = PendingNavigationKind.None;
        _pendingLoadPath = null;
        _pendingLoadName = null;
        _systemMenu?.CloseMenu();
    }

    private void OnSystemMenuSaveRequested(object sender, EventArgs e)
    {
        ShowSaveChooser();
    }

    private void OnSystemMenuLoadRequested(object sender, EventArgs e)
    {
        ShowLoadChooser();
    }

    private void ShowSaveChooser()
    {
        if (_saveCatalog == null)
        {
            _feedbackOverlay.ShowError("Save storage is unavailable. See the game log for details.");
            return;
        }

        _saveLoadChooser.ShowChooser(
            SaveChooserMode.Save,
            SaveSlotViewModelMapper.Map(_saveCatalog.Discover()));
    }

    private void ShowLoadChooser()
    {
        if (_saveCatalog == null)
        {
            _feedbackOverlay.ShowError("Save storage is unavailable. See the game log for details.");
            return;
        }

        _saveLoadChooser.ShowChooser(
            SaveChooserMode.Load,
            SaveSlotViewModelMapper.Map(_saveCatalog.Discover()));
    }

    private void OnSaveChooserCancelled(object sender, EventArgs e)
    {
        _saveLoadChooser.Visible = false;
        _pendingNavigation = PendingNavigationKind.None;
        _pendingLoadPath = null;
    }

    private void OnSaveChooserRefreshRequested(object sender, EventArgs e)
    {
        RefreshSaveChooser();
    }

    private void RefreshSaveChooser()
    {
        if (_saveCatalog == null) return;
        _saveLoadChooser.RefreshEntries(
            SaveSlotViewModelMapper.Map(_saveCatalog.Discover()));
        UpdateSystemMenuState();
    }

    private async void OnManualSaveRequested(object sender, SaveSlotRequestedEventArgs args)
    {
        if (_saveGameManager == null)
        {
            _saveLoadChooser.SetOperationError("Save storage is unavailable.");
            return;
        }

        ShowActivity("SAVING CAMPAIGN", "Writing an atomic recovery point...");
        await YieldForActivityOverlay();
        CampaignRecoverabilityTracker tracker = GameDataSingleton.Instance.Recoverability;
        CampaignRevision revision = tracker.CaptureRevision();
        bool continueAfterSave = false;

        try
        {
            SaveGameEntry saved = args.OverwriteTarget == null
                ? _saveGameManager.CreateManualSave(
                    args.Name, GetCampaignName(), CurrentCampaignSaveWriter.Write)
                : _saveGameManager.OverwriteManualSave(
                    args.OverwriteTarget.FilePath,
                    args.Name,
                    GetCampaignName(),
                    CurrentCampaignSaveWriter.Write);
            tracker.MarkSaveSucceeded(revision);
            _lastSaveStatus = $"Saved {saved.DisplayName} at {saved.LastWriteTimeLocal:t}.";
            _feedbackOverlay.ShowSuccess($"Campaign saved as {saved.DisplayName}.");
            _saveLoadChooser.Visible = false;
            UpdateSystemMenuState();

            if (_pendingNavigation != PendingNavigationKind.None)
            {
                continueAfterSave = true;
            }
            else
            {
                _systemMenu.CloseMenu();
            }
        }
        catch (Exception exception)
        {
            GD.PushError($"Manual save failed: {exception}");
            _saveLoadChooser.SetOperationError($"Save failed: {exception.Message}");
            _feedbackOverlay.ShowError("Save failed. The previous recovery point remains intact.");
        }
        finally
        {
            HideActivity();
        }

        if (continueAfterSave)
        {
            ExecutePendingNavigation();
        }
    }

    private void OnManualSaveDeleteRequested(object sender, SaveSlotSelectionEventArgs args)
    {
        try
        {
            _saveGameManager.DeleteManualSave(args.Slot.FilePath);
            _feedbackOverlay.ShowSuccess($"Deleted {args.Slot.DisplayName}.");
            RefreshSaveChooser();
        }
        catch (Exception exception)
        {
            GD.PushError($"Could not delete manual save: {exception}");
            _saveLoadChooser.SetOperationError($"Delete failed: {exception.Message}");
        }
    }

    private void OnSelectedSaveLoadRequested(object sender, SaveSlotSelectionEventArgs args)
    {
        BeginDestructiveNavigation(
            PendingNavigationKind.Load,
            args.Slot.FilePath,
            args.Slot.DisplayName,
            "loading the selected campaign");
    }

    private void OnReturnToTitleRequested(object sender, EventArgs e)
    {
        BeginDestructiveNavigation(
            PendingNavigationKind.ReturnToTitle,
            null,
            null,
            "returning to the title screen");
    }

    private void OnQuitRequested(object sender, EventArgs e)
    {
        BeginDestructiveNavigation(PendingNavigationKind.Quit, null, null, "quitting the game");
    }

    private void BeginDestructiveNavigation(
        PendingNavigationKind kind,
        string loadPath,
        string loadName,
        string actionName)
    {
        _pendingNavigation = kind;
        _pendingLoadPath = loadPath;
        _pendingLoadName = loadName;
        if (!GameDataSingleton.Instance.Recoverability.IsDirty)
        {
            ExecutePendingNavigation();
            return;
        }

        _destructiveNavigationDialog.ShowFor(actionName);
    }

    private void OnSaveAndContinueRequested(object sender, EventArgs e)
    {
        _destructiveNavigationDialog.Visible = false;
        ShowSaveChooser();
    }

    private void OnDiscardAndContinueRequested(object sender, EventArgs e)
    {
        _destructiveNavigationDialog.Visible = false;
        ExecutePendingNavigation();
    }

    private void OnDestructiveNavigationCancelled(object sender, EventArgs e)
    {
        _destructiveNavigationDialog.Visible = false;
        _pendingNavigation = PendingNavigationKind.None;
        _pendingLoadPath = null;
        _pendingLoadName = null;
    }

    private void ExecutePendingNavigation()
    {
        PendingNavigationKind action = _pendingNavigation;
        string loadPath = _pendingLoadPath;
        string loadName = _pendingLoadName;
        _pendingNavigation = PendingNavigationKind.None;
        _pendingLoadPath = null;
        _pendingLoadName = null;

        switch (action)
        {
            case PendingNavigationKind.Load:
                LoadSelectedCampaign(loadPath, loadName);
                break;
            case PendingNavigationKind.ReturnToTitle:
                ReturnToTitle();
                break;
            case PendingNavigationKind.Quit:
                GetTree().Quit();
                break;
        }
    }

    private async void LoadSelectedCampaign(string savePath, string saveName)
    {
        if (string.IsNullOrWhiteSpace(savePath) || _isProcessingTurn) return;

        _isProcessingTurn = true;
        CloseSystemMenuAndChildren();
        ShowActivity("LOADING CAMPAIGN", $"Loading Game: {saveName}");
        await YieldForActivityOverlay();

        try
        {
            CampaignLoader.LoadIntoSingleton(savePath);
            PackedScene mainScene = GD.Load<PackedScene>(
                "res://Scenes/MainGameScreen/main_game_scene.tscn");
            MainGameScene replacement = mainScene.Instantiate<MainGameScene>();
            Node parent = GetParent();
            HideActivity();
            parent.AddChild(replacement);
            QueueFree();
        }
        catch (Exception exception)
        {
            GD.PushError($"Load failed: {exception}");
            HideActivity();
            _isProcessingTurn = false;
            _systemMenu.ShowMenu();
            ShowLoadChooser();
            _saveLoadChooser.SetOperationError($"Load failed: {exception.Message}");
        }
    }

    private void ReturnToTitle()
    {
        GetTree().AutoAcceptQuit = true;
        GameDataSingleton.Instance.ClearCampaign();
        PackedScene titleScene = GD.Load<PackedScene>("res://Scenes/StartMenu/StartMenu.tscn");
        Control title = titleScene.Instantiate<Control>();
        Node parent = GetParent();
        parent.AddChild(title);
        QueueFree();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest && !_isProcessingTurn)
        {
            BeginDestructiveNavigation(PendingNavigationKind.Quit, null, null, "quitting the game");
        }
    }

    private void MarkCampaignChanged()
    {
        GameDataSingleton.Instance.Recoverability.MarkChanged();
    }

    private void OnCampaignChanged(object sender, EventArgs e)
    {
        MarkCampaignChanged();
    }

    private void OnRecoverabilityStateChanged(object sender, EventArgs e)
    {
        UpdateSystemMenuState();
    }

    private void UpdateSystemMenuState()
    {
        if (_systemMenu == null) return;

        bool canSave = _saveGameManager != null && GameDataSingleton.Instance.IsInitialized;
        bool canLoad = false;
        if (_saveCatalog != null)
        {
            try
            {
                canLoad = _saveCatalog.Discover().Count > 0;
            }
            catch (Exception exception)
            {
                GD.PushWarning($"Could not refresh save catalog: {exception.Message}");
            }
        }

        _systemMenu.SetSaveAvailability(canSave, canLoad);
        string status = _lastSaveStatus;
        if (string.IsNullOrWhiteSpace(status))
        {
            status = GameDataSingleton.Instance.Recoverability.IsDirty
                ? "Campaign has changes that are not yet recoverable."
                : "Current campaign state has a recovery point.";
        }
        _systemMenu.SetLastSaveStatus(status);
    }

    private string GetCampaignName()
    {
        return GameDataSingleton.Instance.Sector?.PlayerForce?.Army?.OrderOfBattle?.Name
            ?? GameDataSingleton.Instance.Sector?.PlayerForce?.Army?.ForceName
            ?? "Unknown Chapter";
    }

    private void RequestEndTurn()
    {
        if (_isProcessingTurn) return;

        EndTurnPreflightReport report = EndTurnPreflight.Evaluate(
            GameDataSingleton.Instance.Sector,
            _warningPreferences);
        if (!report.RequiresConfirmation)
        {
            ResolveEndTurnWithProtection();
            return;
        }

        _endTurnPreflightDialog.SetData(report, _warningPreferences);
        _endTurnPreflightDialog.Visible = true;
        _endTurnPreflightDialog.MoveToFront();
    }

    private void OnEndTurnAnywayPressed(object sender, EventArgs e)
    {
        ResolveEndTurnWithProtection();
    }

    private void OnEndTurnPreflightCancelled(object sender, EventArgs e)
    {
        _endTurnPreflightDialog.Visible = false;
    }

    private void OnPreflightPreferencesChanged(
        object sender,
        EndTurnWarningPreferences preferences)
    {
        SaveWarningPreferences(preferences);
    }

    private async void ResolveEndTurnWithProtection()
    {
        if (_isProcessingTurn) return;
        if (_saveGameManager == null)
        {
            _feedbackOverlay.ShowError(
                "Turn resolution is blocked because the protected pre-turn save cannot be written: save storage is unavailable.");
            return;
        }

        _isProcessingTurn = true;
        _endTurnPreflightDialog.Visible = false;
        ShowActivity("PROTECTING CAMPAIGN", "Writing the protected pre-turn recovery point...");
        await YieldForActivityOverlay();

        CampaignRecoverabilityTracker tracker = GameDataSingleton.Instance.Recoverability;
        CampaignRevision preTurnRevision = tracker.CaptureRevision();
        try
        {
            SaveGameEntry protectedSave = _saveGameManager.SaveProtectedPreTurn(
                GetCampaignName(),
                CurrentCampaignSaveWriter.Write);
            tracker.MarkSaveSucceeded(preTurnRevision);
            _lastSaveStatus = $"Protected before turn at {protectedSave.LastWriteTimeLocal:t}.";
        }
        catch (Exception exception)
        {
            GD.PushError($"Protected pre-turn save failed: {exception}");
            HideActivity();
            _isProcessingTurn = false;
            _feedbackOverlay.ShowError(
                $"Turn not advanced: the protected pre-turn save failed. {exception.Message}",
                8.0);
            UpdateSystemMenuState();
            return;
        }

        tracker.MarkChanged();
        ShowActivity("RESOLVING TURN", "Processing orders, movement, and the wider war...");
        await YieldForActivityOverlay();

        bool turnCompleted = false;
        try
        {
            ProcessTurnCore();
            turnCompleted = true;
        }
        catch (Exception exception)
        {
            GD.PushError($"Turn resolution failed: {exception}");
            _feedbackOverlay.ShowError(
                "Turn resolution failed. The protected pre-turn recovery point is available.",
                8.0);
        }

        if (turnCompleted)
        {
            ShowActivity("AUTOSAVING CAMPAIGN", "Securing the resolved turn...");
            await YieldForActivityOverlay();
            CampaignRevision postTurnRevision = tracker.CaptureRevision();
            try
            {
                SaveGameEntry autosave = _saveGameManager.SavePostTurnAutosave(
                    GetCampaignName(),
                    CurrentCampaignSaveWriter.Write);
                tracker.MarkSaveSucceeded(postTurnRevision);
                _lastSaveStatus = $"Autosaved resolved turn at {autosave.LastWriteTimeLocal:t}.";
                _feedbackOverlay.ShowSuccess("Turn resolved and autosaved.");
            }
            catch (Exception exception)
            {
                GD.PushError($"Post-turn autosave failed: {exception}");
                _feedbackOverlay.ShowError(
                    "The turn resolved, but its autosave failed. Save manually before leaving the campaign.",
                    8.0);
            }
        }

        HideActivity();
        _isProcessingTurn = false;
        UpdateSystemMenuState();
    }

    private void OnWarningPreferencesChanged(
        object sender,
        WarningPreferencesChangedEventArgs args)
    {
        SaveWarningPreferences(new EndTurnWarningPreferences
        {
            WarnIdleDeployableSquads = args.WarnIdleDeployableSquads,
            WarnActionableTaskForces = args.WarnActionableTaskForces,
            WarnSpecialMissionOpportunities = args.WarnSpecialMissionOpportunities
        });
    }

    private void SaveWarningPreferences(EndTurnWarningPreferences preferences)
    {
        _warningPreferences = preferences?.Clone() ?? new EndTurnWarningPreferences();
        try
        {
            _warningPreferencesRepository.Save(_warningPreferences);
        }
        catch (Exception exception)
        {
            GD.PushWarning($"Could not save End Turn warning preferences: {exception.Message}");
            _feedbackOverlay?.ShowWarning("Warning preferences could not be saved.");
        }
        ApplyWarningPreferencesToMenu();
    }

    private void ApplyWarningPreferencesToMenu()
    {
        _systemMenu?.SetWarningPreferences(
            _warningPreferences.WarnIdleDeployableSquads,
            _warningPreferences.WarnActionableTaskForces,
            _warningPreferences.WarnSpecialMissionOpportunities);
    }

    private void OnExportDiagnosticsRequested(object sender, EventArgs e)
    {
        _diagnosticsExportDialog.ShowDialog();
    }

    private void OnDiagnosticsCancelled(object sender, EventArgs e)
    {
        _diagnosticsExportDialog.Visible = false;
    }

    private async void OnDiagnosticsExportRequested(
        object sender,
        DiagnosticsExportRequestedEventArgs args)
    {
        _diagnosticsExportDialog.SetBusy(true);
        await ToSignal(GetTree(), "process_frame");

        DiagnosticBundleExporter exporter = new();
        DiagnosticExportResult result;
        try
        {
            result = exporter.Export(new DiagnosticExportRequest
            {
                DestinationPath = args.DestinationPath,
                BuildVersion = GetBuildVersion(),
                SettingsFiles = File.Exists(_warningPreferencesRepository.PreferencesFilePath)
                    ? new[] { _warningPreferencesRepository.PreferencesFilePath }
                    : Array.Empty<string>(),
                LogFiles = DiagnosticBundleExporter.DiscoverRecentLogs(
                    ProjectSettings.GlobalizePath("user://logs")),
                IncludeCurrentCampaign = args.IncludeCurrentCampaign,
                CurrentCampaignSnapshotFactory = CaptureDiagnosticCampaign
            });
        }
        catch (Exception exception)
        {
            GD.PushError($"Diagnostic export failed: {exception}");
            _diagnosticsExportDialog.ShowExportResult(
                false,
                $"Diagnostic export failed: {exception.Message}");
            _feedbackOverlay.ShowError("Diagnostic export failed. See the export dialog for details.");
            return;
        }

        string message = result.Successful
            ? $"Diagnostic bundle written to {result.DestinationPath}."
            : $"Diagnostic export failed: {result.ErrorMessage}";
        _diagnosticsExportDialog.ShowExportResult(result.Successful, message);
        if (result.Successful)
        {
            _feedbackOverlay.ShowSuccess("Diagnostic bundle exported.");
        }
        else
        {
            _feedbackOverlay.ShowError("Diagnostic export failed. See the export dialog for details.");
        }
    }

    private DiagnosticAttachment CaptureDiagnosticCampaign()
    {
        string directory = Path.Combine(Path.GetTempPath(), "OnlyWar", "diagnostics");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"campaign-{Guid.NewGuid():N}.s3db");
        try
        {
            CurrentCampaignSaveWriter.Write(path);
            return new DiagnosticAttachment("current-campaign.s3db", File.ReadAllBytes(path));
        }
        finally
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
                // The OS will eventually clean the temp directory; preserve the export result.
            }
        }
    }

    private static string GetBuildVersion()
    {
        Variant configured = ProjectSettings.GetSetting(
            "application/config/version",
            Variant.From("Alpha 0.7.1"));
        return configured.AsString();
    }

    private void ShowActivity(string title, string detail)
    {
        _mainUILayer.MoveChild(_activityOverlay, _mainUILayer.GetChildCount() - 1);
        _activityOverlay.ShowBusy(title, detail);
    }

    private async System.Threading.Tasks.Task YieldForActivityOverlay()
    {
        await ToSignal(GetTree(), "process_frame");
        await ToSignal(GetTree(), "process_frame");
    }

    private void HideActivity()
    {
        _activityOverlay.HideBusy();
    }
}
