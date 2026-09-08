using Godot;
using OnlyWar.Application;
using OnlyWar.Helpers.Diagnostics;
using OnlyWar.Helpers.Storage;
using OnlyWar.Host.Presentation.UI.SystemMenu;
using OnlyWar.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

public partial class MainGameScene
{
	private const int GlobalOverlayZIndex = 100;

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
	private IEndTurnWarningPreferencesRepository _warningPreferencesRepository;
	private EndTurnWarningPreferences _warningPreferences;
	private PendingNavigationKind _pendingNavigation;
	private string _pendingLoadPath;
	private string _pendingLoadName;
	private string _startupWarning;
	private string _lastSaveStatus;

	internal void SetStartupWarning(string warning)
	{
		_startupWarning = warning;
	}

	public override void _ExitTree()
	{
		if (_campaignApplication != null)
			_campaignApplication.CampaignStatusChanged -= OnRecoverabilityStateChanged;
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

		_warningPreferencesRepository = OnlyWar.Host.Composition.GodotHostPaths.CreateWarningPreferences();
		_warningPreferences = _warningPreferencesRepository.Load();
		ApplyWarningPreferencesToMenu();

		try
		{
			GameStorage storage = _campaignApplication.Storage;
			storage.InitializeUserStorage();
			_saveGameManager = _campaignApplication.SaveManager;
			_saveCatalog = new SaveGameCatalog(storage.SaveDirectory);
		}
		catch (Exception exception)
		{
			GD.PushError($"Save storage is unavailable: {exception}");
			_lastSaveStatus = "Save storage is unavailable. See the game log for details.";
		}

		_campaignApplication.CampaignStatusChanged += OnRecoverabilityStateChanged;
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
		_modalLayer.AddChild(control);
		// Gameplay surfaces can contain children with an explicit positive Z index (the
		// planet tactical hexes use 3). Keep global menus above the entire gameplay stack
		// while preserving sibling order between the menu and its child dialogs.
		control.ZIndex = GlobalOverlayZIndex;
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
		DialogController topDialog = DialogController.FindTopmostVisibleDialog(
			blockers.OfType<DialogController>());
		if (topDialog != null)
		{
			topDialog.RequestClose();
			return true;
		}

		if (_primaryContentHost != null)
		{
			for (int index = _primaryContentHost.GetChildCount() - 1; index >= 0; index--)
			{
				if (_primaryContentHost.GetChild(index) is MainScreenController screen
					&& screen.IsVisibleInTree())
				{
					screen.RequestClose();
					return true;
				}
			}
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
		bool continueAfterSave = false;

		SaveCampaignResult result = _campaignApplication.SaveCampaign(new(
			_campaignApplication.SessionToken,
			args.OverwriteTarget == null ? SaveCampaignKind.Manual : SaveCampaignKind.Overwrite,
			args.Name,
			args.OverwriteTarget?.FilePath));
		HideActivity();

		if (result.Succeeded)
		{
			_lastSaveStatus = $"Saved {result.DisplayName} at {result.WrittenLocal:t}.";
			_feedbackOverlay.ShowSuccess(result.Message);
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
		else
		{
			GD.PushError($"Manual save failed: {result.Message}");
			_saveLoadChooser.SetOperationError($"Save failed: {result.Message}");
			_feedbackOverlay.ShowError("Save failed. The previous recovery point remains intact.");
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
		if (!_campaignApplication.QueryStatus().IsDirty)
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
			_campaignApplication.LoadAndInstall(savePath);
			PackedScene mainScene = GD.Load<PackedScene>(
				"res://Scenes/MainGameScreen/main_game_scene.tscn");
			MainGameScene replacement = mainScene.Instantiate<MainGameScene>();
			replacement.Configure(_campaignApplication);
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
		_campaignApplication.Close();
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
		_campaignApplication.MarkChanged();
	}

	private void OnCampaignChanged(object sender, EventArgs e)
	{
		MarkCampaignChanged();
		if (_commandScreen != null && _activePrimaryScreen == _commandScreen)
		{
			_commandScreen.RefreshFromExternalChange();
		}
	}

	private void OnRecoverabilityStateChanged(object sender, EventArgs e)
	{
		UpdateSystemMenuState();
	}

	private void UpdateSystemMenuState()
	{
		if (_systemMenu == null) return;

		CampaignStatusView status = _campaignApplication.QueryStatus();
		bool canSave = _saveGameManager != null && status.HasCampaign;
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
		string message = _lastSaveStatus;
		if (string.IsNullOrWhiteSpace(message))
		{
			message = status.IsDirty
				? "Campaign has changes that are not yet recoverable."
				: "Current campaign state has a recovery point.";
		}
		_systemMenu.SetLastSaveStatus(message);
	}

	private void RequestEndTurn()
	{
		if (_isProcessingTurn) return;
		if (_campaignApplication.RequiresRecruitmentSetup())
		{
			PushVisibleOverlaySurface();
			OpenTrainingUnitScreen(mandatorySetup: true);
			_feedbackOverlay.ShowError(
				"Establish the Home World recruitment program before ending the turn.");
			return;
		}

		EndTurnPreflightReport report =
			_campaignApplication.QueryEndTurnPreflight(_warningPreferences);
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

		SaveCampaignResult protectedSave = _campaignApplication.SaveCampaign(new(
			_campaignApplication.SessionToken, SaveCampaignKind.ProtectedPreTurn));
		if (!protectedSave.Succeeded)
		{
			GD.PushError($"Protected pre-turn save failed: {protectedSave.Message}");
			HideActivity();
			_isProcessingTurn = false;
			_feedbackOverlay.ShowError(
				$"Turn not advanced: the protected pre-turn save failed. {protectedSave.Message}",
				8.0);
			UpdateSystemMenuState();
			return;
		}
		_lastSaveStatus = $"Protected before turn at {protectedSave.WrittenLocal:t}.";

		_campaignApplication.MarkChanged();
		ShowActivity("RESOLVING TURN", "Processing orders, movement, and the wider war...");
		await YieldForActivityOverlay();

		bool turnCompleted = false;
		try
		{
			turnCompleted = ProcessTurnCore();
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
			SaveCampaignResult autosave = _campaignApplication.SaveCampaign(new(
				_campaignApplication.SessionToken, SaveCampaignKind.PostTurnAutosave));
			if (autosave.Succeeded)
			{
				_lastSaveStatus = $"Autosaved resolved turn at {autosave.WrittenLocal:t}.";
				_feedbackOverlay.ShowSuccess("Turn resolved and autosaved.");
			}
			else
			{
				GD.PushError($"Post-turn autosave failed: {autosave.Message}");
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
			// A diagnostic capture is still a write of the active campaign, so it goes through the
			// application rather than reaching for the current-campaign writer.
			_campaignApplication.WriteDiagnosticCapture(path);
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
