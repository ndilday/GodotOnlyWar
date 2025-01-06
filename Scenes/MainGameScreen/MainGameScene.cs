using Godot;
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
    private CanvasItem _previousScreen;
    private CanvasLayer _mainUILayer;
    public override void _Ready()
    {
        _bottomMenu = GetNode<BottomMenu>("./SectorMap/UILayer/BottomMenu");
        _bottomMenu.CompanyButtonPressed += OnCompanyButtonPressed;
        _sectorMap = GetNode<SectorMap>("./SectorMap");
        _mainUILayer = GetNode<CanvasLayer>("./SectorMap/UILayer");
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
                _soldierScreen.FinalizeSoldierTransfer();
                // reset the company list to reflect the transfer
                _chapterScreen.PopulateCompanyList();
                _soldierScreen.Visible = false;
                _previousScreen.Visible = true;
                _previousScreen = null;
            }
        }
    }

    public void OnCompanyButtonPressed(object sender, EventArgs e)
    {
        if(_chapterScreen == null)
        {
            PackedScene chapterScene = GD.Load<PackedScene>("res://Scenes/ChapterScreen/chapter_view.tscn");
            _chapterScreen = (ChapterController)chapterScene.Instantiate();
            _chapterScreen.SoldierSelectedForDisplay += (object s, int soldierId) => OnSoldierSelectedForDisplay(s, soldierId);
            AddChild(_chapterScreen);
        }
        _sectorMap.Visible = false;
        _mainUILayer.Visible = false;
        _chapterScreen.Visible = true;
    }

    public void OnSoldierSelectedForDisplay(object sender, int soldierId)
    {
        if(_soldierScreen == null)
        {
            PackedScene soldierScene = GD.Load<PackedScene>("res://Scenes/SoldierScreen/soldier_view.tscn");
            _soldierScreen = (SoldierController)soldierScene.Instantiate();
            AddChild(_soldierScreen);
        }
        PlayerSoldier soldier = (PlayerSoldier)GameDataSingleton.Instance.Sector.PlayerForce.Army.OrderOfBattle.GetAllMembers().First(s => s.Id == soldierId);
        _soldierScreen.DisplaySoldierData(soldier);
        _previousScreen = _chapterScreen;
        _chapterScreen.Visible = false;
        _soldierScreen.Visible = true;
    }
}
