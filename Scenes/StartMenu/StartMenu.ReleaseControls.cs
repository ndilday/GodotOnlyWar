using Godot;
using OnlyWar.Helpers.Diagnostics;
using OnlyWar.Helpers.Settings;
using OnlyWar.Helpers.Storage;
using OnlyWar.Helpers.UI.SystemMenu;
using OnlyWar.Models;
using System;
using System.IO;

public partial class StartMenu
{
	private SaveLoadChooserController _titleSaveChooser;
	private SystemMenuController _titleOptionsMenu;
	private DiagnosticsExportDialog _titleDiagnosticsDialog;
	private TransientFeedbackOverlay _titleFeedback;
	private EndTurnWarningPreferencesRepository _titleWarningRepository;
	private EndTurnWarningPreferences _titleWarningPreferences;

	private void InitializeTitleControls()
	{
		GetTree().AutoAcceptQuit = true;

		_titleSaveChooser = AddTitleControl<SaveLoadChooserController>(
			"res://Scenes/SystemMenu/save_load_chooser.tscn");
		_titleOptionsMenu = AddTitleControl<SystemMenuController>(
			"res://Scenes/SystemMenu/system_menu.tscn");
		_titleOptionsMenu.SetContext(SystemMenuContext.TitleScreen);
		_titleDiagnosticsDialog = AddTitleControl<DiagnosticsExportDialog>(
			"res://Scenes/SystemMenu/diagnostics_export_dialog.tscn");
		_titleFeedback = AddTitleControl<TransientFeedbackOverlay>(
			"res://Scenes/SystemMenu/transient_feedback_overlay.tscn");

		_titleSaveChooser.CancelRequested += OnTitleSaveChooserCancelled;
		_titleSaveChooser.RefreshRequested += OnTitleSaveChooserRefreshRequested;
		_titleSaveChooser.LoadRequested += OnTitleLoadRequested;
		_titleSaveChooser.DeleteRequested += OnTitleSaveDeleteRequested;

		_titleOptionsMenu.CloseRequested += OnTitleOptionsClosed;
		_titleOptionsMenu.QuitRequested += OnTitleQuitRequested;
		_titleOptionsMenu.ExportDiagnosticsRequested += OnTitleExportDiagnosticsRequested;
		_titleOptionsMenu.WarningPreferencesChanged += OnTitleWarningPreferencesChanged;

		_titleDiagnosticsDialog.CancelRequested += OnTitleDiagnosticsCancelled;
		_titleDiagnosticsDialog.ExportRequested += OnTitleDiagnosticsExportRequested;

		_titleWarningRepository = EndTurnWarningPreferencesRepository.CreateDefault();
		_titleWarningPreferences = _titleWarningRepository.Load();
		ApplyTitleWarningPreferences();
	}

	private T AddTitleControl<T>(string scenePath) where T : Control
	{
		PackedScene scene = GD.Load<PackedScene>(scenePath)
			?? throw new InvalidOperationException($"Could not load required title UI {scenePath}.");
		T control = scene.Instantiate<T>();
		AddChild(control);
		control.Visible = false;
		return control;
	}

	public void OnOptionsButtonPressed()
	{
		if (_isTransitioning) return;
		ApplyTitleWarningPreferences();
		_titleOptionsMenu.ShowMenu();
	}

	public void OnQuitButtonPressed()
	{
		OnTitleQuitRequested(this, EventArgs.Empty);
	}

	private void OnTitleQuitRequested(object sender, EventArgs e)
	{
		GetTree().Quit();
	}

	private void OnTitleOptionsClosed(object sender, EventArgs e)
	{
		_titleOptionsMenu.CloseMenu();
		_titleDiagnosticsDialog.Visible = false;
	}

	private void ShowTitleLoadChooser()
	{
		try
		{
			SaveGameCatalog catalog = new(GameStorage.SaveDirectory);
			_titleSaveChooser.ShowChooser(
				SaveChooserMode.Load,
				SaveSlotViewModelMapper.Map(catalog.Discover()));
		}
		catch (Exception exception)
		{
			GD.PushError($"Could not open the save catalog: {exception}");
			_loadStatusLabel.Text = $"Save catalog unavailable: {exception.Message}";
		}
	}

	private void OnTitleSaveChooserCancelled(object sender, EventArgs e)
	{
		_titleSaveChooser.Visible = false;
	}

	private void OnTitleSaveChooserRefreshRequested(object sender, EventArgs e)
	{
		try
		{
			SaveGameCatalog catalog = new(GameStorage.SaveDirectory);
			_titleSaveChooser.RefreshEntries(
				SaveSlotViewModelMapper.Map(catalog.Discover()));
			RefreshLoadGameAvailability();
		}
		catch (Exception exception)
		{
			_titleSaveChooser.SetOperationError($"Refresh failed: {exception.Message}");
		}
	}

	private async void OnTitleLoadRequested(object sender, SaveSlotSelectionEventArgs args)
	{
		if (_isTransitioning || args?.Slot == null) return;

		_isTransitioning = true;
		_titleSaveChooser.Visible = false;
		SetMenuButtonsVisible(false);
		_activityOverlay.MoveToFront();
		_activityOverlay.ShowBusy(
			"LOADING CAMPAIGN",
			$"Loading Game: {args.Slot.DisplayName}");
		await ToSignal(GetTree(), "process_frame");
		await ToSignal(GetTree(), "process_frame");

		try
		{
			CampaignLoader.LoadIntoSingleton(args.Slot.FilePath);
			LaunchMainGameScene();
		}
		catch (Exception exception)
		{
			GD.PushError($"Load failed: {exception}");
			_activityOverlay.HideBusy();
			SetMenuButtonsVisible(true);
			_isTransitioning = false;
			ShowTitleLoadChooser();
			_titleSaveChooser.SetOperationError($"Load failed: {exception.Message}");
		}
	}

	private void OnTitleSaveDeleteRequested(object sender, SaveSlotSelectionEventArgs args)
	{
		if (args?.Slot == null) return;

		try
		{
			SaveGameManager manager = new(GameStorage.SaveDirectory);
			manager.DeleteManualSave(args.Slot.FilePath);
			_titleFeedback.ShowSuccess($"Deleted {args.Slot.DisplayName}.");
			OnTitleSaveChooserRefreshRequested(this, EventArgs.Empty);
		}
		catch (Exception exception)
		{
			GD.PushError($"Could not delete manual save: {exception}");
			_titleSaveChooser.SetOperationError($"Delete failed: {exception.Message}");
		}
	}

	private string TryCreateInitialAutosave(string campaignName)
	{
		CampaignRecoverabilityTracker tracker = GameDataSingleton.Instance.Recoverability;
		CampaignRevision revision = tracker.CaptureRevision();
		try
		{
			SaveGameManager manager = new(GameStorage.SaveDirectory);
			manager.SaveInitialAutosave(campaignName, CurrentCampaignSaveWriter.Write);
			tracker.MarkSaveSucceeded(revision);
			return null;
		}
		catch (Exception exception)
		{
			GD.PushError($"Initial campaign autosave failed: {exception}");
			return "The campaign opened, but its initial autosave failed. Save manually before leaving the campaign.";
		}
	}

	private void OnTitleWarningPreferencesChanged(
		object sender,
		WarningPreferencesChangedEventArgs args)
	{
		_titleWarningPreferences = new EndTurnWarningPreferences
		{
			WarnIdleDeployableSquads = args.WarnIdleDeployableSquads,
			WarnActionableTaskForces = args.WarnActionableTaskForces,
			WarnSpecialMissionOpportunities = args.WarnSpecialMissionOpportunities
		};

		try
		{
			_titleWarningRepository.Save(_titleWarningPreferences);
		}
		catch (Exception exception)
		{
			GD.PushWarning($"Could not save warning preferences: {exception.Message}");
			_titleFeedback.ShowWarning("Warning preferences could not be saved.");
		}
	}

	private void ApplyTitleWarningPreferences()
	{
		_titleOptionsMenu?.SetWarningPreferences(
			_titleWarningPreferences.WarnIdleDeployableSquads,
			_titleWarningPreferences.WarnActionableTaskForces,
			_titleWarningPreferences.WarnSpecialMissionOpportunities);
	}

	private void OnTitleExportDiagnosticsRequested(object sender, EventArgs e)
	{
		_titleDiagnosticsDialog.ShowDialog();
	}

	private void OnTitleDiagnosticsCancelled(object sender, EventArgs e)
	{
		_titleDiagnosticsDialog.Visible = false;
	}

	private async void OnTitleDiagnosticsExportRequested(
		object sender,
		DiagnosticsExportRequestedEventArgs args)
	{
		if (args.IncludeCurrentCampaign)
		{
			_titleDiagnosticsDialog.ShowExportResult(
				false,
				"No campaign is loaded on the title screen. Clear ‘Include current campaign’ and retry.");
			return;
		}

		_titleDiagnosticsDialog.SetBusy(true);
		await ToSignal(GetTree(), "process_frame");
		DiagnosticBundleExporter exporter = new();
		DiagnosticExportResult result;
		try
		{
			result = exporter.Export(new DiagnosticExportRequest
			{
				DestinationPath = args.DestinationPath,
				BuildVersion = GetTitleBuildVersion(),
				SettingsFiles = File.Exists(_titleWarningRepository.PreferencesFilePath)
					? new[] { _titleWarningRepository.PreferencesFilePath }
					: Array.Empty<string>(),
				LogFiles = DiagnosticBundleExporter.DiscoverRecentLogs(
					ProjectSettings.GlobalizePath("user://logs")),
				IncludeCurrentCampaign = false
			});
		}
		catch (Exception exception)
		{
			GD.PushError($"Diagnostic export failed: {exception}");
			_titleDiagnosticsDialog.ShowExportResult(
				false,
				$"Diagnostic export failed: {exception.Message}");
			_titleFeedback.ShowError("Diagnostic export failed. See the export dialog for details.");
			return;
		}

		string message = result.Successful
			? $"Diagnostic bundle written to {result.DestinationPath}."
			: $"Diagnostic export failed: {result.ErrorMessage}";
		_titleDiagnosticsDialog.ShowExportResult(result.Successful, message);
		if (result.Successful)
		{
			_titleFeedback.ShowSuccess("Diagnostic bundle exported.");
		}
		else
		{
			_titleFeedback.ShowError("Diagnostic export failed. See the export dialog for details.");
		}
	}

	private static string GetTitleBuildVersion()
	{
		Variant configured = ProjectSettings.GetSetting(
			"application/config/version",
			Variant.From("Alpha 0.7.1"));
		return configured.AsString();
	}
}
