using Godot;
using OnlyWar.Helpers.Sector;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using System;
using System.Linq;

public partial class MainGameScene : Control
{
    private BottomMenu _bottomMenu;
    private TopMenu _topMenu;
    private SectorMap _sectorMap;
    private ChapterController _chapterScreen;
    private ApothecariumScreenController _apothecariumScreen;
    private ConquistorumScreenController _conquistorumScreen;
    private SoldierController _soldierScreen;
    private SoldierView _soldierView;
    private CanvasItem _previousScreen;
    private CanvasLayer _mainUILayer;
    private TurnController _turnController;
    public override void _Ready()
    {
        _bottomMenu = GetNode<BottomMenu>("UILayer/BottomMenu");
        _topMenu = GetNode<TopMenu>("UILayer/TopMenu");
        _bottomMenu.ChapterButtonPressed += HandleChapterButtonPressed;
        _bottomMenu.ApothecariumButtonPressed += HandleApothecariumButtonPressed;
        _bottomMenu.ConquistorumButtonPressed += HandleConquistorumButtonPressed;
        _bottomMenu.EndTurnButtonPressed += HandleEndTurnButtonPressed;
        _sectorMap = GetNode<SectorMap>("SectorMap");
        _mainUILayer = GetNode<CanvasLayer>("UILayer");
        _turnController = new TurnController();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))    // "ui_cancel" is mapped to Escape)
        {
            if (_chapterScreen.Visible) 
            {
                HandleCloseChapterScreen(null, EventArgs.Empty);
            }
            else if(_apothecariumScreen.Visible)
            {
                HandleCloseApothecariumScreen(null, EventArgs.Empty);
            }
            else if(_conquistorumScreen.Visible)
            {
                HandleCloseConquistorumScreen(null, EventArgs.Empty);
            }
            else if (_soldierScreen.Visible)
            {
                OnSoldierViewCloseButtonPressed(null, null);
            }
        }

        if (@event is InputEventMouseButton emb)
        {
            if (emb.ButtonIndex == MouseButton.Left)
            {
                Vector2 gmpos = GetGlobalMousePosition();
                Vector2I mousePosition = new((int)(gmpos.X), (int)(gmpos.Y));
                GD.Print($"Left click at {mousePosition.X},{mousePosition.Y}");
                Vector2I gridPosition = _sectorMap.CalculateGridCoordinates(mousePosition);
                int index = _sectorMap.GridPositionToIndex(gridPosition);
                string text = $"({gridPosition.X},{gridPosition.Y})\n{mousePosition.X},{mousePosition.Y}";
                _topMenu.SetDebugText(text);
            }
        }
    }

    private void HandleChapterButtonPressed(object sender, EventArgs e)
    {
        if(_chapterScreen == null)
        {
            PackedScene chapterScene = GD.Load<PackedScene>("res://Scenes/ChapterScreen/chapter_screen.tscn");
            _chapterScreen = (ChapterController)chapterScene.Instantiate();
            _chapterScreen.SoldierSelectedForDisplay += (object s, int soldierId) => OnSoldierSelectedForDisplay(s, soldierId);
            _chapterScreen.CloseButtonPressed += HandleCloseChapterScreen;
            AddChild(_chapterScreen);
        }
        _sectorMap.Visible = false;
        _mainUILayer.Visible = false;
        _chapterScreen.Visible = true;
    }

    private void HandleCloseChapterScreen(object sender, EventArgs e)
    {
        _chapterScreen.Visible = false;
        _sectorMap.Visible = true;
        _mainUILayer.Visible = true;
    }

    private void HandleApothecariumButtonPressed(object sender, EventArgs e)
    {
        // open the Apothecarium screen
        if (_apothecariumScreen == null)
        {
            PackedScene apothecariumScene = GD.Load<PackedScene>("res://Scenes/ApothecariumScreen/apothecarium_screen.tscn");
            _apothecariumScreen = (ApothecariumScreenController)apothecariumScene.Instantiate();
            _apothecariumScreen.CloseButtonPressed += HandleCloseApothecariumScreen;
            AddChild(_apothecariumScreen);
        }
        _sectorMap.Visible = false;
        _mainUILayer.Visible = false;
        _apothecariumScreen.Visible = true;
    }

    private void HandleCloseApothecariumScreen(object sender, EventArgs e)
    {
        _apothecariumScreen.Visible = false;
        _sectorMap.Visible = true;
        _mainUILayer.Visible = true;
    }

    private void HandleConquistorumButtonPressed(object sender, EventArgs e)
    {
        // open the Conquistorum screen
        if (_conquistorumScreen == null)
        {
            PackedScene conquistorumScene = GD.Load<PackedScene>("res://Scenes/ConquistorumScreen/conquistorum_screen.tscn");
            _conquistorumScreen = (ConquistorumScreenController)conquistorumScene.Instantiate();
            _conquistorumScreen.CloseButtonPressed += HandleCloseConquistorumScreen;
            AddChild(_conquistorumScreen);
        }
        _sectorMap.Visible = false;
        _mainUILayer.Visible = false;
        _conquistorumScreen.Visible = true;
    }

    private void HandleCloseConquistorumScreen(object sender, EventArgs e)
    {
        _conquistorumScreen.Visible = false;
        _sectorMap.Visible = true;
        _mainUILayer.Visible = true;
    }


    private void HandleEndTurnButtonPressed(object sender, EventArgs e)
    {
        // handle squad orders
        _turnController.ProcessTurn(GameDataSingleton.Instance.Sector);
        // handle ship movement
    }

    private void OnSoldierSelectedForDisplay(object sender, int soldierId)
    {
        if(_soldierScreen == null)
        {
            PackedScene soldierScene = GD.Load<PackedScene>("res://Scenes/SoldierScreen/soldier_screen.tscn");
            _soldierScreen = (SoldierController)soldierScene.Instantiate();
            AddChild(_soldierScreen);
            _soldierView = _soldierScreen.GetNode<SoldierView>("SoldierView");
            _soldierView.CloseButtonPressed += OnSoldierViewCloseButtonPressed;
        }
        PlayerSoldier soldier = (PlayerSoldier)GameDataSingleton.Instance.Sector.PlayerForce.Army.OrderOfBattle.GetAllMembers().First(s => s.Id == soldierId);
        _soldierScreen.DisplaySoldierData(soldier);
        _previousScreen = _chapterScreen;
        _chapterScreen.Visible = false;
        _soldierScreen.Visible = true;
    }

    private void OnSoldierViewCloseButtonPressed(object sender, EventArgs e)
    {
        if (_soldierScreen.FinalizeSoldierTransfer())
        // reset the company list to reflect the transfer
        {
            _chapterScreen.PopulateCompanyList();
        }
        _soldierScreen.Visible = false;
        _previousScreen.Visible = true;
        _previousScreen = null;
    }
}
