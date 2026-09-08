using Godot;
using OnlyWar.Application;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Database.GameRules;
using OnlyWar.Models;

public partial class MainGamePreviewBootstrap : Node
{
    [Export]
    public string ChapterName { get; set; } = "10th Company";

    [Export]
    public int Seed { get; set; } = 1;

    private CampaignApplication _campaignApplication;

    public override void _Ready()
    {
        OnlyWar.Helpers.Storage.GameStorage storage =
            OnlyWar.Host.Composition.GodotHostPaths.CreateStorage();
        _campaignApplication = new CampaignApplication(
            OnlyWar.Host.Composition.GodotHostPaths.CreateCampaignServices(
                new SeededRNG(Seed), storage));
        _campaignApplication.StartNewCampaign(
            GameRulesLoader.Load(
                _campaignApplication.Services.Persistence.Storage.RulesDatabasePath),
            new Date(39, 500, 1),
            ChapterName,
            Seed);

        PackedScene mainGameScene = GD.Load<PackedScene>("res://Scenes/MainGameScreen/main_game_scene.tscn");
        MainGameScene instance = mainGameScene.Instantiate<MainGameScene>();
        instance.Configure(_campaignApplication);
        AddChild(instance);
    }
}
