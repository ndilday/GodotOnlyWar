using Godot;
using OnlyWar.Models;

public partial class MainGamePreviewBootstrap : Node
{
    [Export]
    public string ChapterName { get; set; } = "10th Company";

    [Export]
    public int Seed { get; set; } = 1;

    public override void _Ready()
    {
        if (!GameDataSingleton.Instance.IsInitialized)
        {
            GameDataSingleton.Instance.InitializeNewGameData(
                new GameRulesData(),
                new Date(39, 500, 1),
                ChapterName,
                Seed);
        }

        PackedScene mainGameScene = GD.Load<PackedScene>("res://Scenes/MainGameScreen/main_game_scene.tscn");
        AddChild(mainGameScene.Instantiate());
    }
}
