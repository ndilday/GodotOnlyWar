using Godot;
using OnlyWar.Builders;
using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using System;
using System.Linq;
using System.Threading.Tasks;

public partial class StartMenu : Control
{

	private NewGameSetupController _setupScreen;

	public void OnNewGameButtonPressed()
	{
		ShowNewGameSetup();
	}

	public void OnLoadGameButtonPressed()
	{
		LoadGameData();
		LaunchMainGameScene();
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

    private void OnCampaignConfirmed(object sender, NewGameSettings settings)
    {
        GameDataSingleton.Instance.InitializeNewGameData(
            new GameRulesData(),
            new Date(39, 500, 1),
            settings.ChapterName,
            settings.Seed);
        LaunchMainGameScene();
    }

    private void SetMenuButtonsVisible(bool isVisible)
    {
        GetNode<Button>("NewGameButton").Visible = isVisible;
        GetNode<Button>("LoadGameButton").Visible = isVisible;
    }

    private async void LaunchMainGameScene()
    {
        // Replace StartMenu with MainGameScene
        PackedScene mainGameSceneScene = GD.Load<PackedScene>("res://Scenes/MainGameScreen/main_game_scene.tscn");
        MainGameScene mainGameSceneInstance = mainGameSceneScene.Instantiate<MainGameScene>();
        QueueFree(); // StartMenu removes itself
        GetParent().AddChild(mainGameSceneInstance); // Add MainGameScene to the *parent* of StartMenu

        await Task.Delay(1); // Small delay to ensure MainGameScene is fully in tree before _Ready()
    }

    private void LoadGameData()
    {
        GameRulesData gameRulesData = new GameRulesData(); // Load Game Rules Data (if needed for loading)

        GameStateDataBlob gameState = LoadGameData(gameRulesData);
        Army army = new Army(
            "Player Chapter",
            null,
            "Chapter Master",
            gameRulesData.PlayerFaction.Units.First(),
            gameRulesData.PlayerFaction.Units.First().GetAllMembers().Select(m => (PlayerSoldier)m));
        army.Requisition = gameState.Requisition;
        army.MedicalProcedures.AddRange(gameState.MedicalProcedures ?? []);
        // Restore the fallen brothers, who belong to no unit and so are carried separately.
        foreach (PlayerSoldier fallen in gameState.FallenBrothers ?? [])
        {
            army.FallenBrothers[fallen.Id] = fallen;
        }
        Fleet fleet = new Fleet(
            "Chapter Navy",
            null,
            "Chapter Master");
        fleet.TaskForces.AddRange(gameState.Fleets.Where(f => f.Faction.Id == gameRulesData.PlayerFaction.Id));
        PlayerForce playerForce = new PlayerForce(
            gameRulesData.PlayerFaction,
            army,
            fleet);
        Sector sector = new Sector(playerForce, gameState.Characters, gameState.Planets, gameState.Fleets);

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
