using Godot;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
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
    private SquadScreenController _squadScreen;
    private SoldierView _soldierView;
    private PlanetDetailScreenController _planetDetailScreen;
    private PlanetTacticalScreenController _planetTacticalScreen;
    private Stack<Control> _previousScreenStack;
    private CanvasLayer _mainUILayer;
    private TurnController _turnController;
    private EndOfTurnDialogController _endOfTurnDialog;
    public override void _Ready()
    {
        _bottomMenu = GetNode<BottomMenu>("UILayer/BottomMenu");
        _topMenu = GetNode<TopMenu>("UILayer/TopMenu");
        _topMenu.SaveButtonPressed += OnSaveButtonPressed;
        _bottomMenu.ChapterButtonPressed += OnChapterButtonPressed;
        _bottomMenu.ApothecariumButtonPressed += OnApothecariumButtonPressed;
        _bottomMenu.ConquistorumButtonPressed += OnConquistorumButtonPressed;
        _bottomMenu.EndTurnButtonPressed += OnEndTurnButtonPressed;
        _sectorMap = GetNode<SectorMap>("SectorMap");
        _sectorMap.PlanetClicked += OnPlanetClicked;
        _sectorMap.FleetClicked += OnFleetClicked;
        _mainUILayer = GetNode<CanvasLayer>("UILayer");
        _turnController = new TurnController();
        _previousScreenStack = new Stack<Control>();
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

       /* if (@event is InputEventMouseButton emb)
        {
            if (emb.ButtonIndex == MouseButton.Left && emb.IsPressed() && _sectorMap.Visible)
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
        }*/
    }

    private void SetMainScreenVisibility(bool isVisible)
    {
        _sectorMap.Visible = isVisible;
        _sectorMap.SetProcessInput(isVisible);
        _topMenu.Visible = isVisible;
        _bottomMenu.Visible = isVisible;
    }

    private async void OnSaveButtonPressed(object sender, EventArgs e)
    {
        string message = "";
        var units = GameDataSingleton.Instance.GameRulesData.Factions.SelectMany(f => f.Units);
        try
        {
            GameStateDataAccess.Instance.SaveData(
                "default.s3db",
                GameDataSingleton.Instance.Date,
                GameDataSingleton.Instance.Sector.Characters,
                GameDataSingleton.Instance.Sector.PlayerForce.Requests,
                GameDataSingleton.Instance.Sector.Planets.Values,
                GameDataSingleton.Instance.Sector.Fleets.Values,
                units,
                GameDataSingleton.Instance.Sector.PlayerForce.Army.PlayerSoldierMap.Values,
                GameDataSingleton.Instance.Sector.PlayerForce.BattleHistory);
            message = "SAVED!";
        }
        catch (Exception exception)
        {
            GD.PushWarning($"Save Failed: {exception.Message}");
            message = "SAVE FAILED!";
        }
        finally
        {
            // Update button text temporarily
            _topMenu.SetSaveButtonText(message);

            // Wait for a short duration (e.g., 1.5 seconds) using Godot's Timer
            await ToSignal(GetTree().CreateTimer(2f), "timeout");

            // Revert button text back to original
            _topMenu.SetSaveButtonText("Save");
        }
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
        if(_previousScreenStack.Count > 0)
        {
            Control control = _previousScreenStack.Pop();
            control.Visible = true;
        }
        else
        {
            SetMainScreenVisibility(true);
        }
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
        //LoadPlanetDetailScreen(planet);
        LoadPlanetTacticalScreen(planet);
    }

    private void LoadPlanetDetailScreen(Planet planet)
    {
        if (_planetDetailScreen == null)
        {
            PackedScene planetScene = GD.Load<PackedScene>("res://Scenes/PlanetDetailScreen/planet_detail_screen.tscn");
            _planetDetailScreen = (PlanetDetailScreenController)planetScene.Instantiate();

            _planetDetailScreen.CloseButtonPressed += OnCloseScreen;
            _mainUILayer.AddChild(_planetDetailScreen);
        }
        _planetDetailScreen.PopulatePlanetData(planet);
        _planetDetailScreen.Visible = true;
        SetMainScreenVisibility(false);
        GD.Print($"Planet {planet.Id} Clicked");
    }

    private void LoadPlanetTacticalScreen(Planet planet)
    {
        if (_planetTacticalScreen == null)
        {
            PackedScene planetScene = GD.Load<PackedScene>("res://Scenes/PlanetDetailScreen/planet_tactical_screen.tscn");
            _planetTacticalScreen = (PlanetTacticalScreenController)planetScene.Instantiate();

            _planetTacticalScreen.CloseButtonPressed += OnCloseScreen;
            _planetTacticalScreen.SquadDoubleClicked += OnSquadDoubleClicked;
            _mainUILayer.AddChild(_planetTacticalScreen);
        }
        _planetTacticalScreen.PopulatePlanetData(planet);
        _planetTacticalScreen.Visible = true;
        SetMainScreenVisibility(false);
        GD.Print($"Planet {planet.Id} Clicked");
    }

    private void OnFleetClicked(object sender, int fleetId) 
    { 
    }

    private void OnEndTurnButtonPressed(object sender, EventArgs e)
    {
        // handle squad orders
        _turnController.ProcessTurn(GameDataSingleton.Instance.Sector);
        if(_endOfTurnDialog == null)
        {
            PackedScene endOfTurnScene = GD.Load<PackedScene>("res://Scenes/EndOfTurnDialog.tscn");
            _endOfTurnDialog = (EndOfTurnDialogController)endOfTurnScene.Instantiate();
            _endOfTurnDialog.CloseButtonPressed += OnCloseScreen;
            _mainUILayer.AddChild(_endOfTurnDialog);
        }

        // handle ship movement

        // display end of turn dialog
        _endOfTurnDialog.AddData(_turnController.MissionContexts, _turnController.SpecialMissions);
        _endOfTurnDialog.Visible = true;
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
        _previousScreenStack.Push(control);
        control.Visible = false;
    }

    private void OnSoldierViewCloseButtonPressed(object sender, EventArgs e)
    {
        if (_soldierScreen.FinalizeSoldierTransfer())
        // reset the company list to reflect the transfer
        {
            _chapterScreen.PopulateCompanyList();
        }
        OnCloseScreen(_soldierScreen, e);
    }

    private void OnSquadDoubleClicked(object sender, Squad squad)
    {
        if(_squadScreen == null)
        {
            PackedScene squadScene = GD.Load<PackedScene>("res://Scenes/SquadScreen/squad_screen.tscn");
            _squadScreen = (SquadScreenController)squadScene.Instantiate();
            _mainUILayer.AddChild(_squadScreen);
            _squadScreen.CloseButtonPressed += OnCloseScreen;
        }
        _squadScreen.SetSquad(squad);
        _squadScreen.Visible = true;
        Control control = (Control)sender;
        _previousScreenStack.Push(control);
        control.Visible = false;
    }
}
