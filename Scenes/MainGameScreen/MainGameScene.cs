using Godot;
using OnlyWar.Helpers.Sector;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
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
    private PlanetDetailScreenController _planetDetailScreen;
    private Control _previousScreen;
    private CanvasLayer _mainUILayer;
    private TurnController _turnController;
    public override void _Ready()
    {
        _bottomMenu = GetNode<BottomMenu>("UILayer/BottomMenu");
        _topMenu = GetNode<TopMenu>("UILayer/TopMenu");
        _bottomMenu.ChapterButtonPressed += OnChapterButtonPressed;
        _bottomMenu.ApothecariumButtonPressed += OnApothecariumButtonPressed;
        _bottomMenu.ConquistorumButtonPressed += OnConquistorumButtonPressed;
        _bottomMenu.EndTurnButtonPressed += OnEndTurnButtonPressed;
        _sectorMap = GetNode<SectorMap>("SectorMap");
        _sectorMap.PlanetClicked += OnPlanetClicked;
        _sectorMap.FleetClicked += OnFleetClicked;
        _mainUILayer = GetNode<CanvasLayer>("UILayer");
        _turnController = new TurnController();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))    // "ui_cancel" is mapped to Escape)
        {
            if (_chapterScreen.Visible) 
            {
                OnCloseScreen(_chapterScreen, EventArgs.Empty);
            }
            else if(_apothecariumScreen.Visible)
            {
                OnCloseScreen(_apothecariumScreen, EventArgs.Empty);
            }
            else if(_conquistorumScreen.Visible)
            {
                OnCloseScreen(_conquistorumScreen, EventArgs.Empty);
            }
            else if (_soldierScreen.Visible)
            {
                OnSoldierViewCloseButtonPressed(null, null);
            }
        }

        if (@event is InputEventMouseButton emb)
        {
            if (emb.ButtonIndex == MouseButton.Left && emb.IsPressed())
            {
                Vector2 gmpos = GetGlobalMousePosition();
                Vector2I mousePosition = new((int)(gmpos.X), (int)(gmpos.Y));
                GD.Print($"Left click at {mousePosition.X},{mousePosition.Y}");
                Vector2I gridPosition = _sectorMap.CalculateGridCoordinates(mousePosition);
                int index = _sectorMap.GridPositionToIndex(gridPosition);
                string text = $"({gridPosition.X},{gridPosition.Y})\n{mousePosition.X},{mousePosition.Y}";
                _topMenu.SetDebugText(text);
                GetViewport().SetInputAsHandled();
            }
        }
    }

    private void SetMainScreenVisibility(bool isVisible)
    {
        _sectorMap.Visible = isVisible;
        _topMenu.Visible = isVisible;
        _bottomMenu.Visible = isVisible;
    }

    private void OnChapterButtonPressed(object sender, EventArgs e)
    {
        if(_chapterScreen == null)
        {
            PackedScene chapterScene = GD.Load<PackedScene>("res://Scenes/ChapterScreen/chapter_screen.tscn");
            _chapterScreen = (ChapterController)chapterScene.Instantiate();
            _chapterScreen.SoldierSelectedForDisplay += (object s, int soldierId) => OnSoldierSelectedForDisplay(s, soldierId);
            _chapterScreen.CloseButtonPressed += OnCloseScreen;
            _mainUILayer.AddChild(_chapterScreen);
        }
        _chapterScreen.Visible = true;
        SetMainScreenVisibility(false);
    }

    private void OnCloseScreen(object sender, EventArgs e)
    {
        SetMainScreenVisibility(true);
        ((Control)(sender)).Visible = false;
    }

    private void OnApothecariumButtonPressed(object sender, EventArgs e)
    {
        // open the Apothecarium screen
        if (_apothecariumScreen == null)
        {
            PackedScene apothecariumScene = GD.Load<PackedScene>("res://Scenes/ApothecariumScreen/apothecarium_screen.tscn");
            _apothecariumScreen = (ApothecariumScreenController)apothecariumScene.Instantiate();
            _apothecariumScreen.CloseButtonPressed += OnCloseScreen;
            _mainUILayer.AddChild(_apothecariumScreen);
        }
        _apothecariumScreen.Visible = true;
        SetMainScreenVisibility(false);
    }

    private void OnConquistorumButtonPressed(object sender, EventArgs e)
    {
        // open the Conquistorum screen
        if (_conquistorumScreen == null)
        {
            PackedScene conquistorumScene = GD.Load<PackedScene>("res://Scenes/ConquistorumScreen/conquistorum_screen.tscn");
            _conquistorumScreen = (ConquistorumScreenController)conquistorumScene.Instantiate();
            _conquistorumScreen.CloseButtonPressed += OnCloseScreen;
            _conquistorumScreen.SoldierLinkClicked += OnSoldierSelectedForDisplay;
            _mainUILayer.AddChild(_conquistorumScreen);
        }
        _conquistorumScreen.Visible = true;
        SetMainScreenVisibility(false);
    }

    private void OnPlanetClicked(object sender, int planetId)
    {
        Planet planet = GameDataSingleton.Instance.Sector.Planets[planetId];
        if (_planetDetailScreen == null)
        {
            PackedScene planetScene = GD.Load<PackedScene>("res://Scenes/PlanetDetailScreen/planet_detail_screen.tscn");
            _planetDetailScreen = (PlanetDetailScreenController)planetScene.Instantiate();
            
            _planetDetailScreen.CloseButtonPressed += OnCloseScreen;
            _mainUILayer.AddChild(_planetDetailScreen);
        }
        _planetDetailScreen.PopulateFleetTree(planet);
        _planetDetailScreen.PopulateRegionTree(planet);
        _planetDetailScreen.Visible = true;
        SetMainScreenVisibility(false);
        GD.Print($"Planet {planetId} Clicked");
    }

    private void OnFleetClicked(object sender, int fleetId) 
    { 
    }

    private void OnEndTurnButtonPressed(object sender, EventArgs e)
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
            _mainUILayer.AddChild(_soldierScreen);
            _soldierView = _soldierScreen.GetNode<SoldierView>("SoldierView");
            _soldierView.CloseButtonPressed += OnSoldierViewCloseButtonPressed;
        }
        PlayerSoldier soldier = (PlayerSoldier)GameDataSingleton.Instance.Sector.PlayerForce.Army.OrderOfBattle.GetAllMembers().First(s => s.Id == soldierId);
        _soldierScreen.DisplaySoldierData(soldier);
        _soldierScreen.Visible = true;
        Control control = (Control)sender;
        _previousScreen = control;
        control.Visible = false;
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
