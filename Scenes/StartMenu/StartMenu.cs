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

	public void OnNewGameButtonPressed()
	{
		LoadMainGameScene(false);
	}

	public void OnLoadGameButtonPressed()
	{
		LoadMainGameScene(true);
	}

    private async void LoadMainGameScene(bool loadGame)
    {
        // Initialize Game Data (New or Load) BEFORE adding MainGameScene to tree
        if (loadGame)
        {
            LoadGameData(); // Pass MainGameScene instance if needed later
        }
        else
        {
            InitializeNewGame(); // Pass MainGameScene instance if needed later
        }

        // Replace StartMenu with MainGameScene
        // Instantiate MainGameScene
        PackedScene mainGameSceneScene = GD.Load<PackedScene>("res://Scenes/MainGameScreen/main_game_scene.tscn");
        MainGameScene mainGameSceneInstance = mainGameSceneScene.Instantiate<MainGameScene>();
        QueueFree(); // StartMenu removes itself
        GetParent().AddChild(mainGameSceneInstance); // Add MainGameScene to the *parent* of StartMenu

        await Task.Delay(1); // Small delay to ensure MainGameScene is fully in tree before _Ready()
        // Signal MainGameScene that loading is complete (if needed)
        // mainGameSceneInstance.GameWorldLoaded(); // Example signal - implement if needed in MainGameScene
    }

    private void InitializeNewGame()
    {
        GameDataSingleton.Instance.InitializeNewGameData(
            new GameRulesData(),
            new Date(39, 500, 1));
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
