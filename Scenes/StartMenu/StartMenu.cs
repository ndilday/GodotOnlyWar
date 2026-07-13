using Godot;
using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using System;
using System.Linq;

public partial class StartMenu : Control
{

	private NewGameSetupController _setupScreen;
	private ActivityOverlay _activityOverlay;
	private bool _isTransitioning;

	public override void _Ready()
	{
		_activityOverlay = GetNode<ActivityOverlay>("ActivityOverlay");
	}

	public void OnNewGameButtonPressed()
	{
		if (_isTransitioning)
		{
			return;
		}

		ShowNewGameSetup();
	}

	public async void OnLoadGameButtonPressed()
	{
		if (_isTransitioning)
		{
			return;
		}

		_isTransitioning = true;
		SetMenuButtonsVisible(false);
		_activityOverlay.ShowBusy("LOADING CAMPAIGN", "Restoring the sector, forces, and orders...");
		// Let the modal draw before the synchronous database reconstruction starts.
		await ToSignal(GetTree(), "process_frame");
		await ToSignal(GetTree(), "process_frame");

		try
		{
			LoadGameData();
			LaunchMainGameScene();
		}
		catch (Exception exception)
		{
			GD.PushError($"Load failed: {exception}");
			_activityOverlay.HideBusy();
			SetMenuButtonsVisible(true);
			_isTransitioning = false;
		}
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
                new GameRulesData(),
                new Date(39, 500, 1),
                settings.ChapterName,
                settings.Seed);
            LaunchMainGameScene();
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

    private void LaunchMainGameScene()
    {
        // Replace StartMenu with MainGameScene
        PackedScene mainGameSceneScene = GD.Load<PackedScene>("res://Scenes/MainGameScreen/main_game_scene.tscn");
        MainGameScene mainGameSceneInstance = mainGameSceneScene.Instantiate<MainGameScene>();
        QueueFree(); // StartMenu removes itself
        GetParent().AddChild(mainGameSceneInstance); // Add MainGameScene to the *parent* of StartMenu
    }

    private void LoadGameData()
    {
        GameRulesData gameRulesData = new GameRulesData(); // Load Game Rules Data (if needed for loading)

        GameStateDataBlob gameState = LoadGameData(gameRulesData);
        Sector sector = SavedGameLoader.BuildSectorFromBlob(gameState, gameRulesData);

        GameDataSingleton.Instance.LoadGameDataFromBlob(gameRulesData, gameState.CurrentDate, sector);
        // Subsectors and warp lanes are derived deterministically from planet positions
        // rather than persisted, so rebuild them after the sector is loaded.
        SectorBuilder.GenerateWarpNetwork(sector, gameRulesData);
        // Load other game state data into GameDataSingleton.Instance.Sector, etc.
        // Potentially pass loaded data to mainGameSceneInstance if needed for UI setup
    }

    private GameStateDataBlob LoadGameData(GameRulesData gameRulesData)
    {
        var shipTemplateMap = gameRulesData.Factions.Where(f => f.ShipTemplates != null)
                                                                      .SelectMany(f => f.ShipTemplates.Values)
                                                                      .ToDictionary(s => s.Id);
        var unitTemplateMap = gameRulesData.Factions.Where(f => f.UnitTemplates != null)
                                                          .SelectMany(f => f.UnitTemplates.Values)
                                                          .ToDictionary(u => u.Id);
        var squadTemplateMap = gameRulesData.Factions.Where(f => f.SquadTemplates != null)
                                                           .SelectMany(f => f.SquadTemplates.Values)
                                                           .ToDictionary(s => s.Id);
        var hitLocations = gameRulesData.BodyHitLocationTemplateMap.Values.SelectMany(hl => hl)
                                                                                .Distinct()
                                                                                .ToDictionary(hl => hl.Id);
        var soldierTypeMap = gameRulesData.Factions.Where(f => f.SoldierTemplates != null)
                                                         .SelectMany(f => f.SoldierTemplates.Values)
                                                         .ToDictionary(st => st.Id);
        var gameData =
            GameStateDataAccess.Instance.GetData("default.s3db",
                                                 gameRulesData.Factions.ToDictionary(f => f.Id),
                                                 gameRulesData.PlanetTemplateMap,
                                                 shipTemplateMap, 
                                                 unitTemplateMap, 
                                                 squadTemplateMap,
                                                 gameRulesData.WeaponSets,
                                                 hitLocations,
                                                 gameRulesData.BaseSkillMap,
                                                 soldierTypeMap);
        return gameData;
    }
}
