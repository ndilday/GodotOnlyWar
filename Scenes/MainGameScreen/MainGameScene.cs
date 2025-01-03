using Godot;
using System;

public partial class MainGameScene : Control
{
    private BottomMenu _bottomMenu;
    private SectorMap _sectorMap;
    private ChapterController _chapterScreen;
    public override void _Ready()
    {
        _bottomMenu = GetNode<BottomMenu>("./SectorMap/CanvasLayer/BottomMenu");
        _bottomMenu.CompanyButtonPressed += OnCompanyButtonPressed;
        _sectorMap = GetNode<SectorMap>("./SectorMap");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel") && _chapterScreen.Visible) // "ui_cancel" is mapped to Escape
        {
            _chapterScreen.Visible = false;
            _sectorMap.Visible = true;
            GetViewport().SetInputAsHandled(); // Consume the input event
        }
    }

    public void OnCompanyButtonPressed(object sender, EventArgs e)
    {
        if(_chapterScreen == null)
        {
            PackedScene chapterScene = GD.Load<PackedScene>("res://Scenes/ChapterScreen/chapter_view.tscn");
            _chapterScreen = (ChapterController)chapterScene.Instantiate();
            AddChild(_chapterScreen);
        }
        _sectorMap.Visible = false;
        _chapterScreen.Visible = true;
    }
}
