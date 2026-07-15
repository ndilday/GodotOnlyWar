using Godot;
using OnlyWar.Helpers.Storage;
using OnlyWar.Models;
using System;
using System.Linq;

public partial class StartMenu : Control
{

	private NewGameSetupController _setupScreen;
	private ActivityOverlay _activityOverlay;
	private Button _loadGameButton;
	private Label _loadStatusLabel;
	private bool _isTransitioning;

	public override void _Ready()
	{
		_activityOverlay = GetNode<ActivityOverlay>("ActivityOverlay");
		_loadGameButton = GetNode<Button>("MenuButtons/LoadGameButton");
		_loadStatusLabel = GetNode<Label>("MenuButtons/LoadStatusLabel");
		InitializeTitleControls();

		try
		{
			GameStorage.InitializeUserStorage();
			RefreshLoadGameAvailability();
		}
		catch (Exception exception)
		{
			GD.PushError($"Could not initialize game storage: {exception}");
			_loadGameButton.Disabled = true;
			_loadStatusLabel.Text = "Save storage is unavailable. See the game log for details.";
		}
	}

	public void OnNewGameButtonPressed()
	{
		if (_isTransitioning)
		{
			return;
		}

		ShowNewGameSetup();
	}

	public void OnLoadGameButtonPressed()
	{
		if (_isTransitioning)
		{
			return;
		}
		ShowTitleLoadChooser();
	}

    private void ShowNewGameSetup()
    {
        SetMenuButtonsVisible(false);
        PackedScene setupScene = GD.Load<PackedScene>("res://Scenes/StartMenu/new_game_setup.tscn");
        _setupScreen = setupScene.Instantiate<NewGameSetupController>();
        _setupScreen.CampaignConfirmed += OnCampaignConfirmed;
        _setupScreen.Cancelled += OnSetupCancelled;
        AddChild(_setupScreen);
    }

    private void OnSetupCancelled(object sender, EventArgs e)
    {
        _setupScreen.QueueFree();
        _setupScreen = null;
        SetMenuButtonsVisible(true);
    }

    private async void OnCampaignConfirmed(object sender, NewGameSettings settings)
    {
        if (_isTransitioning)
        {
            return;
        }

        _isTransitioning = true;
        _setupScreen.Visible = false;
        _activityOverlay.ShowBusy("FOUNDING CHAPTER", "Generating the sector and preparing your command...");
		// Let the modal draw before the synchronous sector generation starts.
        await ToSignal(GetTree(), "process_frame");
		await ToSignal(GetTree(), "process_frame");

        try
        {
            GameDataSingleton.Instance.InitializeNewGameData(
                new GameRulesData(GameStorage.RulesDatabasePath),
                new Date(39, 500, 1),
                settings.ChapterName,
                settings.Seed);
            string startupWarning = TryCreateInitialAutosave(settings.ChapterName);
            LaunchMainGameScene(startupWarning);
        }
        catch (Exception exception)
        {
            GD.PushError($"New game failed: {exception}");
            _activityOverlay.HideBusy();
            _setupScreen.Visible = true;
            _isTransitioning = false;
        }
    }

    private void SetMenuButtonsVisible(bool isVisible)
    {
        GetNode<Control>("MenuButtons").Visible = isVisible;
    }

    private void LaunchMainGameScene(string startupWarning = null)
    {
        // Replace StartMenu with MainGameScene
        PackedScene mainGameSceneScene = GD.Load<PackedScene>("res://Scenes/MainGameScreen/main_game_scene.tscn");
        MainGameScene mainGameSceneInstance = mainGameSceneScene.Instantiate<MainGameScene>();
        mainGameSceneInstance.SetStartupWarning(startupWarning);
        QueueFree(); // StartMenu removes itself
        GetParent().AddChild(mainGameSceneInstance); // Add MainGameScene to the *parent* of StartMenu
    }

	private void RefreshLoadGameAvailability()
	{
		SaveGameCatalog catalog = new(GameStorage.SaveDirectory);
		var saves = catalog.Discover();

		if (saves.Count > 0)
		{
			_loadGameButton.Disabled = false;
			int available = saves.Count(save => save.IsCompatible);
			_loadStatusLabel.Text = available == saves.Count
				? $"{saves.Count} saved campaign{(saves.Count == 1 ? "" : "s")} available."
				: $"{available} loadable / {saves.Count} total; unavailable saves remain visible in the chooser.";
			return;
		}

		_loadGameButton.Disabled = true;
		_loadStatusLabel.Text = "No saved campaign found.";
	}
}
