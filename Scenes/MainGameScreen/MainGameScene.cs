using Godot;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
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
	private LeftMapTools _leftMapTools;
	private SystemInspector _systemInspector;
	private SectorMap _sectorMap;
	private ChapterController _chapterScreen;
	private ApothecariumScreenController _apothecariumScreen;
	private TrainingUnitScreenController _trainingUnitScreen;
	private FleetScreenController _fleetScreen;
	private DiplomacyScreenController _diplomacyScreen;
	private FleetMoveDialogController _fleetMoveDialog;
	private FleetDivideDialogController _fleetDivideDialog;
	private FleetMergeDialogController _fleetMergeDialog;
	private PopupMenu _fleetContextMenu;
	private int _contextFleetId;
	private SoldierController _soldierScreen;
	private SquadScreenController _squadScreen;
	private SoldierView _soldierView;
	private PlanetDetailScreenController _planetDetailScreen;
	private PlanetTacticalScreenController _planetTacticalScreen;
	private RegionScreenController _regionScreen;
	private Stack<Control> _previousScreenStack;
	private CanvasLayer _mainUILayer;
	private TurnController _turnController;
	private EndOfTurnDialogController _endOfTurnDialog;
	private BriefingDialogController _briefingDialog;
	private BriefingDialogController _scenarioNotificationDialog;
	private CampaignScenario _pendingBriefingScenario;
	private int? _selectedPlanetId;
	private int? _selectedFleetId;
	public override void _Ready()
	{
		// Route the headless-safe battle/turn engine log seam to the Godot console. Engine code
		// under Helpers never calls GD.Print directly so it can run headless (tests, balance
		// tuning); in-game we wire it here so battle output still reaches the editor output.
		BattleLog.Sink = GD.Print;

		if (!GameDataSingleton.Instance.IsInitialized)
		{
			GD.PushError("MainGameScene requires initialized game data. Use StartMenu or Scenes/Debug/main_game_preview_bootstrap.tscn.");
			SetProcess(false);
			SetProcessInput(false);
			return;
		}

		_bottomMenu = GetNode<BottomMenu>("UILayer/BottomMenu");
		_topMenu = GetNode<TopMenu>("UILayer/TopMenu");
		_leftMapTools = GetNode<LeftMapTools>("UILayer/LeftMapTools");
		_systemInspector = GetNode<SystemInspector>("UILayer/SystemInspector");
		_topMenu.SaveButtonPressed += OnSaveButtonPressed;
		_leftMapTools.MapToolPressed += OnMapToolPressed;
		_systemInspector.OpenSystemPressed += OnInspectorOpenSystemPressed;
		_systemInspector.PlotCoursePressed += OnInspectorPlotCoursePressed;
		_systemInspector.DivideFleetPressed += OnInspectorDivideFleetPressed;
		_systemInspector.MergeFleetPressed += OnInspectorMergeFleetPressed;
		_systemInspector.LandSquadsPressed += OnInspectorOpenFleetPlanetPressed;
		_systemInspector.LoadSquadsPressed += OnInspectorOpenFleetPlanetPressed;
		_bottomMenu.ChapterButtonPressed += OnChapterButtonPressed;
		_bottomMenu.ApothecariumButtonPressed += OnApothecariumButtonPressed;
		_bottomMenu.TrainingUnitButtonPressed += OnTrainingUnitButtonPressed;
		_bottomMenu.FleetButtonPressed += OnFleetButtonPressed;
		_bottomMenu.DiplomacyButtonPressed += OnDiplomacyButtonPressed;
		_bottomMenu.EndTurnButtonPressed += OnEndTurnButtonPressed;
		_sectorMap = GetNode<SectorMap>("SectorMap");
		_sectorMap.PlanetClicked += OnPlanetClicked;
		_sectorMap.PlanetDoubleClicked += OnPlanetDoubleClicked;
		_sectorMap.FleetClicked += OnFleetClicked;
		_sectorMap.FleetRightClicked += OnFleetRightClicked;
		_mainUILayer = GetNode<CanvasLayer>("UILayer");
		_turnController = new TurnController();
		_previousScreenStack = new Stack<Control>();
		// Start with the world the chapter fleet is orbiting selected (the promised world at game
		// start), mirroring the camera's initial centring in SectorMap. Fall back to the first
		// planet if there's no fleet/orbit (e.g. legacy saves with a fleet in deep space).
		Planet initialPlanet =
			GameDataSingleton.Instance.Sector.PlayerForce.Fleet.TaskForces.FirstOrDefault()?.Planet
			?? GameDataSingleton.Instance.Sector.Planets.Values.FirstOrDefault();
		_sectorMap.SetSelectedPlanet(initialPlanet?.Id);
		_systemInspector.DisplayPlanet(initialPlanet);

		// One-shot opening briefing (Design/OpeningScenario.md §5): show on first entry after a
		// new game and never again. BriefingAcknowledged is persisted, so a freshly stamped
		// scenario shows it once; a reloaded, acknowledged game does not.
		CampaignScenario scenario = GameDataSingleton.Instance.Sector.Scenario;
		if (scenario is { State: ObjectiveState.Pending, BriefingAcknowledged: false })
		{
			ShowBriefingDialog(scenario);
		}
	}

	private void ShowBriefingDialog(CampaignScenario scenario)
	{
		if (_briefingDialog == null)
		{
			PackedScene briefingScene = GD.Load<PackedScene>("res://Scenes/MainGameScreen/briefing_dialog.tscn");
			_briefingDialog = (BriefingDialogController)briefingScene.Instantiate();
			_briefingDialog.CloseButtonPressed += OnBriefingDialogClosed;
			_mainUILayer.AddChild(_briefingDialog);
		}
		_pendingBriefingScenario = scenario;
		_briefingDialog.SetBriefing(scenario.BriefingText);
		_briefingDialog.Visible = true;
	}

	private void OnBriefingDialogClosed(object sender, EventArgs e)
	{
		if (_pendingBriefingScenario != null)
		{
			// Persisted on the next save; the guard survives reload (§5).
			_pendingBriefingScenario.BriefingAcknowledged = true;
			_pendingBriefingScenario = null;
		}
		_briefingDialog.Visible = false;
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
			else if(_trainingUnitScreen.Visible)
			{
				OnCloseScreen(_trainingUnitScreen, EventArgs.Empty);
			}
			else if(_fleetScreen != null && _fleetScreen.Visible)
			{
				OnCloseScreen(_fleetScreen, EventArgs.Empty);
			}
			else if(_diplomacyScreen != null && _diplomacyScreen.Visible)
			{
				OnCloseScreen(_diplomacyScreen, EventArgs.Empty);
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

	private void SetMainScreenVisibility(bool isVisible, bool keepTopMenuVisible = false)
	{
		_sectorMap.Visible = isVisible;
		_sectorMap.SetProcessInput(isVisible);
		_topMenu.Visible = isVisible || keepTopMenuVisible;
		_leftMapTools.Visible = isVisible;
		_systemInspector.Visible = isVisible;
		_bottomMenu.Visible = isVisible;
	}

	private void OnMapToolPressed(object sender, string actionKey)
	{
		if (actionKey == "focus")
		{
			_sectorMap.CenterOnSelectedPlanet();
			return;
		}

		if (actionKey == "zoom_in")
		{
			_sectorMap.ZoomIn();
			return;
		}

		if (actionKey == "zoom_out")
		{
			_sectorMap.ZoomOut();
			return;
		}

		_topMenu.SetDebugText(actionKey);
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
				GameDataSingleton.Instance.Sector.PlayerForce.Army.Requisition,
				GameDataSingleton.Instance.Sector.PlayerForce.GeneseedStockpile,
				GameDataSingleton.Instance.Sector.PlayerForce.GeneseedPurity,
				GameDataSingleton.Instance.Sector.Scenario,
				GameDataSingleton.Instance.Sector.PlayerForce.Army.MedicalProcedures,
				GameDataSingleton.Instance.Sector.Characters,
				GameDataSingleton.Instance.Sector.PlayerForce.Requests,
				GameDataSingleton.Instance.Sector.Planets.Values,
				GameDataSingleton.Instance.Sector.Fleets.Values,
				units,
				GameDataSingleton.Instance.Sector.PlayerForce.Army.PlayerSoldierMap.Values,
				GameDataSingleton.Instance.Sector.PlayerForce.Army.FallenBrothers.Values,
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
			_chapterScreen.CloseButtonPressed += OnCloseScreen;
			_mainUILayer.AddChild(_chapterScreen);
		}
		_chapterScreen.Visible = true;
		_topMenu.SetScreenText("Chapter Overview");
		SetMainScreenVisibility(false, keepTopMenuVisible: true);
	}

	private void OnCloseScreen(object sender, EventArgs e)
	{
		if(_previousScreenStack.Count > 0)
		{
			Control control = _previousScreenStack.Pop();
			control.Visible = true;
			if (control == _chapterScreen)
			{
				_topMenu.SetScreenText("Chapter Overview");
				_topMenu.Visible = true;
			}
		}
		else
		{
			_topMenu.SetScreenText("Sector Map");
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

	private void OnTrainingUnitButtonPressed(object sender, EventArgs e)
	{
		if (_trainingUnitScreen == null)
		{
			PackedScene trainingUnitScene = GD.Load<PackedScene>("res://Scenes/TrainingUnitScreen/training_unit_screen.tscn");
			_trainingUnitScreen = (TrainingUnitScreenController)trainingUnitScene.Instantiate();
			_trainingUnitScreen.CloseButtonPressed += OnCloseScreen;
			_trainingUnitScreen.SoldierLinkClicked += OnSoldierSelectedForDisplay;
			_mainUILayer.AddChild(_trainingUnitScreen);
		}
		_trainingUnitScreen.Visible = true;
		SetMainScreenVisibility(false);
	}

	private void OnFleetButtonPressed(object sender, EventArgs e)
	{
		if (_fleetScreen == null)
		{
			PackedScene fleetScene = GD.Load<PackedScene>("res://Scenes/FleetScreen/fleet_screen.tscn");
			_fleetScreen = (FleetScreenController)fleetScene.Instantiate();
			_fleetScreen.CloseButtonPressed += OnCloseScreen;
			_mainUILayer.AddChild(_fleetScreen);
		}
		_fleetScreen.PopulateFleetData();
		_fleetScreen.Visible = true;
		SetMainScreenVisibility(false);
	}

	private void OnDiplomacyButtonPressed(object sender, EventArgs e)
	{
		if (_diplomacyScreen == null)
		{
			PackedScene diplomacyScene = GD.Load<PackedScene>("res://Scenes/DiplomacyScreen/diplomacy_screen.tscn");
			_diplomacyScreen = (DiplomacyScreenController)diplomacyScene.Instantiate();
			_diplomacyScreen.CloseButtonPressed += OnCloseScreen;
			_mainUILayer.AddChild(_diplomacyScreen);
		}
		_diplomacyScreen.PopulateRequestData();
		_diplomacyScreen.Visible = true;
		SetMainScreenVisibility(false);
	}

	private void OnPlanetClicked(object sender, int planetId)
	{
		Planet planet = GameDataSingleton.Instance.Sector.Planets[planetId];
		SelectPlanet(planet);
	}

	private void OnPlanetDoubleClicked(object sender, int planetId)
	{
		Planet planet = GameDataSingleton.Instance.Sector.Planets[planetId];
		SelectPlanet(planet);
		//LoadPlanetDetailScreen(planet);
		LoadPlanetTacticalScreen(planet);
	}

	private void SelectPlanet(Planet planet, int? selectedFleetId = null)
	{
		_selectedPlanetId = planet?.Id;
		_selectedFleetId = selectedFleetId;
		_sectorMap.SetSelectedPlanet(planet?.Id);
		_systemInspector.DisplayPlanet(planet, selectedFleetId);
	}

	private void RefreshSelectedSystemInspector()
	{
		if (!_selectedPlanetId.HasValue)
		{
			_systemInspector.DisplayEmptyState();
			return;
		}

		if (!GameDataSingleton.Instance.Sector.Planets.TryGetValue(_selectedPlanetId.Value, out Planet planet))
		{
			SelectPlanet(null);
			return;
		}

		_systemInspector.DisplayPlanet(planet, _selectedFleetId);
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
			_planetTacticalScreen.OrbitalSquadDoubleClicked += OnOrbitalSquadDoubleClicked;
			_planetTacticalScreen.RegionDoubleClicked += OnRegionDoubleClicked;
			_mainUILayer.AddChild(_planetTacticalScreen);
		}
		_planetTacticalScreen.PopulatePlanetData(planet);
		_planetTacticalScreen.Visible = true;
		SetMainScreenVisibility(false);
		GD.Print($"Planet {planet.Id} Clicked");
	}

	private const int FleetMenuPlotCourse = 0;
	private const int FleetMenuDivide = 1;
	private const int FleetMenuMerge = 2;

	private void OnFleetClicked(object sender, int fleetId)
	{
		TaskForce taskForce = GameDataSingleton.Instance.Sector.Fleets[fleetId];
		Planet contextPlanet = taskForce.Planet ?? taskForce.Origin ?? taskForce.Destination;
		if (contextPlanet != null)
		{
			SelectPlanet(contextPlanet, fleetId);
		}
	}

	private void OnFleetRightClicked(object sender, int fleetId)
	{
		TaskForce taskForce = GameDataSingleton.Instance.Sector.Fleets[fleetId];
		Planet contextPlanet = taskForce.Planet ?? taskForce.Origin ?? taskForce.Destination;
		if (contextPlanet != null)
		{
			SelectPlanet(contextPlanet, fleetId);
		}

		ShowFleetContextMenu(taskForce);
	}

	private void ShowFleetContextMenu(TaskForce taskForce)
	{
		// Only player task forces sitting in orbit can be re-tasked; a fleet already
		// in transit cannot change course or be reorganized until it arrives.
		if (taskForce.Faction != GameDataSingleton.Instance.Sector.PlayerForce.Faction) return;
		if (taskForce.TravelPhase != FleetTravelPhase.InOrbit || taskForce.Planet == null) return;

		_contextFleetId = taskForce.Id;

		if (_fleetContextMenu == null)
		{
			_fleetContextMenu = new PopupMenu();
			_fleetContextMenu.AddItem("Plot Course", FleetMenuPlotCourse);
			_fleetContextMenu.AddItem("Divide Task Force", FleetMenuDivide);
			_fleetContextMenu.AddItem("Merge Task Force", FleetMenuMerge);
			_fleetContextMenu.IdPressed += OnFleetContextMenuIdPressed;
			_mainUILayer.AddChild(_fleetContextMenu);
		}

		bool canDivide = taskForce.Ships.Count > 1;
		bool canMerge = FleetMergeDialogController.GetMergeCandidates(taskForce).Any();
		_fleetContextMenu.SetItemDisabled(_fleetContextMenu.GetItemIndex(FleetMenuDivide), !canDivide);
		_fleetContextMenu.SetItemDisabled(_fleetContextMenu.GetItemIndex(FleetMenuMerge), !canMerge);

		_fleetContextMenu.Position = (Vector2I)GetViewport().GetMousePosition();
		_fleetContextMenu.ResetSize();
		_fleetContextMenu.Popup();
	}

	private void OnInspectorOpenSystemPressed(object sender, int planetId)
	{
		if (!GameDataSingleton.Instance.Sector.Planets.TryGetValue(planetId, out Planet planet)) return;

		SelectPlanet(planet);
		LoadPlanetTacticalScreen(planet);
	}

	private void OnInspectorPlotCoursePressed(object sender, int fleetId)
	{
		if (!TryGetActionableFleet(fleetId, out TaskForce taskForce)) return;
		OpenFleetMoveDialog(taskForce);
	}

	private void OnInspectorDivideFleetPressed(object sender, int fleetId)
	{
		if (!TryGetActionableFleet(fleetId, out TaskForce taskForce)) return;
		OpenFleetDivideDialog(taskForce);
	}

	private void OnInspectorMergeFleetPressed(object sender, int fleetId)
	{
		if (!TryGetActionableFleet(fleetId, out TaskForce taskForce)) return;
		OpenFleetMergeDialog(taskForce);
	}

	private void OnInspectorOpenFleetPlanetPressed(object sender, int fleetId)
	{
		if (!TryGetActionableFleet(fleetId, out TaskForce taskForce)) return;
		SelectPlanet(taskForce.Planet, fleetId);
		LoadPlanetTacticalScreen(taskForce.Planet);
	}

	private bool TryGetActionableFleet(int fleetId, out TaskForce taskForce)
	{
		taskForce = null;
		if (!GameDataSingleton.Instance.Sector.Fleets.TryGetValue(fleetId, out TaskForce foundFleet)) return false;
		if (foundFleet.Faction != GameDataSingleton.Instance.Sector.PlayerForce.Faction) return false;
		if (foundFleet.TravelPhase != FleetTravelPhase.InOrbit || foundFleet.Planet == null) return false;

		taskForce = foundFleet;
		return true;
	}

	private void OnFleetContextMenuIdPressed(long id)
	{
		TaskForce taskForce = GameDataSingleton.Instance.Sector.Fleets[_contextFleetId];
		switch ((int)id)
		{
			case FleetMenuPlotCourse:
				OpenFleetMoveDialog(taskForce);
				break;
			case FleetMenuDivide:
				OpenFleetDivideDialog(taskForce);
				break;
			case FleetMenuMerge:
				OpenFleetMergeDialog(taskForce);
				break;
		}
	}

	private void OpenFleetMoveDialog(TaskForce taskForce)
	{
		if (_fleetMoveDialog == null)
		{
			PackedScene fleetMoveScene = GD.Load<PackedScene>("res://Scenes/FleetScreen/fleet_move_dialog.tscn");
			_fleetMoveDialog = (FleetMoveDialogController)fleetMoveScene.Instantiate();
			_fleetMoveDialog.CloseButtonPressed += (s, e) => _fleetMoveDialog.Visible = false;
			_fleetMoveDialog.CoursePlotted += OnFleetActionCompleted;
			_mainUILayer.AddChild(_fleetMoveDialog);
		}
		_fleetMoveDialog.SetTaskForce(taskForce);
		_fleetMoveDialog.Visible = true;
	}

	private void OpenFleetDivideDialog(TaskForce taskForce)
	{
		if (_fleetDivideDialog == null)
		{
			PackedScene fleetDivideScene = GD.Load<PackedScene>("res://Scenes/FleetScreen/fleet_divide_dialog.tscn");
			_fleetDivideDialog = (FleetDivideDialogController)fleetDivideScene.Instantiate();
			_fleetDivideDialog.CloseButtonPressed += (s, e) => _fleetDivideDialog.Visible = false;
			_fleetDivideDialog.FleetDivided += OnFleetActionCompleted;
			_mainUILayer.AddChild(_fleetDivideDialog);
		}
		_fleetDivideDialog.SetTaskForce(taskForce);
		_fleetDivideDialog.Visible = true;
	}

	private void OpenFleetMergeDialog(TaskForce taskForce)
	{
		if (_fleetMergeDialog == null)
		{
			PackedScene fleetMergeScene = GD.Load<PackedScene>("res://Scenes/FleetScreen/fleet_merge_dialog.tscn");
			_fleetMergeDialog = (FleetMergeDialogController)fleetMergeScene.Instantiate();
			_fleetMergeDialog.CloseButtonPressed += (s, e) => _fleetMergeDialog.Visible = false;
			_fleetMergeDialog.FleetsMerged += OnFleetActionCompleted;
			_mainUILayer.AddChild(_fleetMergeDialog);
		}
		_fleetMergeDialog.SetTaskForce(taskForce);
		_fleetMergeDialog.Visible = true;
	}

	private void OnFleetActionCompleted(object sender, EventArgs e)
	{
		((Control)sender).Visible = false;
		_sectorMap.RefreshFleets();
		RefreshSelectedSystemInspector();
	}

	private void OnEndTurnButtonPressed(object sender, EventArgs e)
	{
		// handle squad orders
		_turnController.ProcessTurn(GameDataSingleton.Instance.Sector);
		_sectorMap.RefreshFleets();
		RefreshSelectedSystemInspector();
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

		// Surface the opening-scenario resolution (win/lapse) if it fired this turn
		// (Design/OpeningScenario.md §6.2).
		if (!string.IsNullOrEmpty(_turnController.ScenarioNotification))
		{
			ShowScenarioNotification(_turnController.ScenarioNotification);
		}
	}

	// Reuses the briefing dialog scene (a BBCode message + single acknowledge button) as a
	// generic scenario-resolution notification, on its own instance so its dismissal does not
	// touch the one-shot opening-briefing guard.
	private void ShowScenarioNotification(string text)
	{
		if (_scenarioNotificationDialog == null)
		{
			PackedScene briefingScene = GD.Load<PackedScene>("res://Scenes/MainGameScreen/briefing_dialog.tscn");
			_scenarioNotificationDialog = (BriefingDialogController)briefingScene.Instantiate();
			_scenarioNotificationDialog.CloseButtonPressed += (s, e) => _scenarioNotificationDialog.Visible = false;
			_mainUILayer.AddChild(_scenarioNotificationDialog);
		}
		_scenarioNotificationDialog.SetBriefing(text);
		_scenarioNotificationDialog.Visible = true;
	}

	private void OnSoldierSelectedForDisplay(object sender, int soldierId)
	{
		if(_soldierScreen == null)
		{
			PackedScene soldierScene = GD.Load<PackedScene>("res://Scenes/SoldierScreen/soldier_screen.tscn");
			_soldierScreen = (SoldierController)soldierScene.Instantiate();
			_mainUILayer.AddChild(_soldierScreen);
			_soldierScreen.SoldierTransferred += OnSoldierTransferred;
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
		OnCloseScreen(_soldierScreen, e);
	}

	private void OnSoldierTransferred(object sender, EventArgs e)
	{
		_chapterScreen?.PopulateCompanyList();
	}

	private void OnRegionDoubleClicked(object sender, Region region)
	{
		if(_regionScreen == null)
		{
			PackedScene regionScene = GD.Load<PackedScene>("res://Scenes/RegionScreen/region_screen.tscn");
			_regionScreen = (RegionScreenController)regionScene.Instantiate();
			_regionScreen.CloseButtonPressed += OnCloseScreen;
			_mainUILayer.AddChild(_regionScreen);
		}
		_regionScreen.SquadDoubleClicked += OnSquadDoubleClicked;
		Control control = (Control)sender;
		_previousScreenStack.Push(control);
		control.Visible = false;
	}

	private void OnSquadDoubleClicked(object sender, Squad squad)
	{
		if (_squadScreen == null)
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

	private void OnOrbitalSquadDoubleClicked(object sender, Squad squad)
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
