using Godot;
using OnlyWar.Helpers.Sector;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using System;
using System.Linq;

public partial class MainGameScene : Control
{
    private BottomMenu _bottomMenu;
    private SectorMap _sectorMap;
    private ChapterController _chapterScreen;
    private SoldierController _soldierScreen;
    private SoldierView _soldierView;
    private CanvasItem _previousScreen;
    private CanvasLayer _mainUILayer;
    private TurnController _turnController;
    public override void _Ready()
    {
        _bottomMenu = GetNode<BottomMenu>("./SectorMap/UILayer/BottomMenu");
        _bottomMenu.ChapterButtonPressed += OnCompanyButtonPressed;
        _bottomMenu.EndTurnButtonPressed += OnEndTurnButtonPressed;
        _bottomMenu.ConquistorumButtonPressed += OnConquistorumButtonPressed;
        _sectorMap = GetNode<SectorMap>("./SectorMap");
        _mainUILayer = GetNode<CanvasLayer>("./SectorMap/UILayer");
        _turnController = new TurnController();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))    // "ui_cancel" is mapped to Escape)
        {
            if (_chapterScreen.Visible) 
            {
                _chapterScreen.Visible = false;
                _sectorMap.Visible = true;
                _mainUILayer.Visible = true;
            }
            else if (_soldierScreen.Visible)
            {
                OnSoldierViewCloseButtonPressed(null, null);
            }
        }
    }

    private void OnCompanyButtonPressed(object sender, EventArgs e)
    {
        if(_chapterScreen == null)
        {
            PackedScene chapterScene = GD.Load<PackedScene>("res://Scenes/ChapterScreen/chapter_screen.tscn");
            _chapterScreen = (ChapterController)chapterScene.Instantiate();
            _chapterScreen.SoldierSelectedForDisplay += (object s, int soldierId) => OnSoldierSelectedForDisplay(s, soldierId);
            AddChild(_chapterScreen);
        }
        _sectorMap.Visible = false;
        _mainUILayer.Visible = false;
        _chapterScreen.Visible = true;
    }

    private void OnEndTurnButtonPressed(object sender, EventArgs e)
    {
        // handle squad orders
        _turnController.ProcessTurn(GameDataSingleton.Instance.Sector);
        // handle ship movement
    }

    private void OnConquistorumButtonPressed(object sender, EventArgs e)
    {
        // open the Conquistorum screen
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
