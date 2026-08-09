using Godot;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Helpers.Storage;
using OnlyWar.Helpers.Turns;
using OnlyWar.Helpers.Recruitment;
using OnlyWar.Helpers.Simulation;
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
	private SquadScreenController _squadScreen;
	private PlanetTacticalScreenController _planetTacticalScreen;
	private RegionScreenController _regionScreen;
	private Stack<Control> _previousScreenStack;
	private CanvasLayer _mainUILayer;
	private Control _primaryContentHost;
	private Control _modalLayer;
	private MainScreenController _activePrimaryScreen;
	private ActivityOverlay _activityOverlay;
	private TurnController _turnController;
	private EndOfTurnDialogController _endOfTurnDialog;
	private BriefingDialogController _briefingDialog;
	private BriefingDialogController _scenarioNotificationDialog;
	private PopupMenu _recruitmentPlacementMenu;
	private int _pendingRecruitmentSubjectId;
	private CampaignScenario _pendingBriefingScenario;
	private int? _selectedPlanetId;
	private int? _selectedFleetId;
	private bool _isProcessingTurn;
	public override void _Ready()
	{
		// The engine log seams (BattleLog/GameLog) are wired to the Godot console by the
		// GodotLogBridge autoload, which runs before any scene so generation logging is captured too.

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
		_topMenu.SystemOptionsButtonPressed += OnSystemOptionsButtonPressed;
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
		_bottomMenu.ArchiveButtonPressed += OnArchiveButtonPressed;
		_bottomMenu.EndTurnButtonPressed += OnEndTurnButtonPressed;
		_sectorMap = GetNode<SectorMap>("SectorMap");
		_sectorMap.PlanetClicked += OnPlanetClicked;
		_sectorMap.PlanetDoubleClicked += OnPlanetDoubleClicked;
		_sectorMap.FleetClicked += OnFleetClicked;
		_sectorMap.FleetRightClicked += OnFleetRightClicked;
		_mainUILayer = GetNode<CanvasLayer>("UILayer");
		_primaryContentHost = GetNode<Control>("UILayer/PrimaryContentHost");
		_modalLayer = GetNode<Control>("UILayer/ModalLayer");
		_activityOverlay = GetNode<ActivityOverlay>("UILayer/ActivityOverlay");
		_turnController = new TurnController();
		_previousScreenStack = new Stack<Control>();
		InitializeCampaignControls();
		RefreshTopMenuStatus();
		// Start with the world the chapter fleet is orbiting selected (the promised world at game
		// start), mirroring the camera's initial centring in SectorMap. Fall back to the first
		// planet if there's no fleet/orbit
		Planet initialPlanet =
			GameDataSingleton.Instance.Sector.PlayerForce.Fleet.TaskForces.FirstOrDefault()?.Planet
			?? GameDataSingleton.Instance.Sector.Planets.Values.FirstOrDefault();
		_sectorMap.SetSelectedPlanet(initialPlanet?.Id);
		_systemInspector.DisplayPlanet(initialPlanet);

		// One-shot opening briefing (Design/Reference/OpeningScenario.md): show on first entry after a
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
			_modalLayer.AddChild(_briefingDialog);
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
			MarkCampaignChanged();
			_pendingBriefingScenario = null;
		}
		_briefingDialog.Visible = false;
	}

	public override void _Input(InputEvent @event)
	{
		if (HandleGlobalCampaignInput(@event))
		{
			GetViewport().SetInputAsHandled();
			return;
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

	private void SetMapWorkspaceVisibility(bool isVisible)
	{
		_sectorMap.Visible = isVisible;
		_sectorMap.SetProcessInput(isVisible);
		_topMenu.Visible = true;
		_leftMapTools.Visible = isVisible;
		_systemInspector.Visible = isVisible;
		_bottomMenu.Visible = true;
		RefreshTopMenuStatus();
	}

	private void ShowPrimaryScreen(
		MainScreenController screen,
		string title,
		BottomMenu.Destination destination)
	{
		if (_activePrimaryScreen != null && _activePrimaryScreen != screen)
		{
			_activePrimaryScreen.Visible = false;
		}

		_activePrimaryScreen = screen;
		screen.Visible = true;
		_topMenu.SetScreenText(title);
		_bottomMenu.SetActiveDestination(destination);
		SetMapWorkspaceVisibility(false);
	}

	private bool ToggleOffActivePrimaryScreen(
		MainScreenController screen,
		BottomMenu.Destination destination)
	{
		if (_activePrimaryScreen != screen || !screen.Visible)
		{
			return false;
		}

		screen.RequestClose();
		// A screen may veto navigation (the mandatory recruitment setup does this). Restore
		// the pressed state that Godot toggled before dispatching the button event.
		if (screen.Visible)
		{
			_bottomMenu.SetActiveDestination(destination);
		}
		return true;
	}

	private void AddPrimaryScreen(MainScreenController screen)
	{
		_primaryContentHost.AddChild(screen);
		screen.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		screen.Visible = false;
	}

	private void RefreshTopMenuStatus()
	{
		_topMenu.SetDateText(GameDataSingleton.Instance.Date.ToString());
		_topMenu.SetRequisitionAmount(GameDataSingleton.Instance.Sector.PlayerForce.Army.Requisition);
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

	// The bottom menu stays clickable while a map-overlay surface (planet, region,
	// or squad detail) is open. Push the visible surface before opening a
	// bottom-menu screen so closing it returns there, instead of restoring the
	// sector map underneath the still-visible surface.
	private void PushVisibleOverlaySurface()
	{
		foreach (Control surface in new Control[] { _squadScreen, _regionScreen, _planetTacticalScreen })
		{
			if (surface?.Visible == true)
			{
				_previousScreenStack.Push(surface);
				surface.Visible = false;
			}
		}
	}

	private void OnChapterButtonPressed(object sender, EventArgs e)
	{
		PushVisibleOverlaySurface();
		EnsureChapterScreen();
		if (ToggleOffActivePrimaryScreen(_chapterScreen, BottomMenu.Destination.Chapter))
		{
			return;
		}
		_chapterScreen.PopulateCompanyList();
		ShowPrimaryScreen(
			_chapterScreen,
			"Chapter Overview",
			BottomMenu.Destination.Chapter);
	}

	private void EnsureChapterScreen()
	{
		if (_chapterScreen != null)
		{
			return;
		}

		PackedScene chapterScene = GD.Load<PackedScene>("res://Scenes/ChapterScreen/chapter_screen.tscn");
		_chapterScreen = (ChapterController)chapterScene.Instantiate();
		_chapterScreen.CloseRequested += OnCloseScreen;
		_chapterScreen.CampaignChanged += OnCampaignChanged;
		AddPrimaryScreen(_chapterScreen);
	}

	private void OnCloseScreen(object sender, EventArgs e)
	{
		Control closingScreen = (Control)sender;
		closingScreen.Visible = false;
		if (closingScreen == _activePrimaryScreen)
		{
			_activePrimaryScreen = null;
		}

		if(_previousScreenStack.Count > 0)
		{
			Control control = _previousScreenStack.Pop();
			control.Visible = true;
			if (control == _chapterScreen)
			{
				_activePrimaryScreen = _chapterScreen;
				_topMenu.SetScreenText("Chapter Overview");
				_bottomMenu.SetActiveDestination(BottomMenu.Destination.Chapter);
			}
			else if (control == _trainingUnitScreen)
			{
				_activePrimaryScreen = _trainingUnitScreen;
				_trainingUnitScreen.RefreshFromExternalChange();
				_topMenu.SetScreenText("10th Company");
				_bottomMenu.SetActiveDestination(BottomMenu.Destination.TrainingUnit);
			}
			else if (control == _planetTacticalScreen)
			{
				_planetTacticalScreen.RefreshFromExternalChange();
				_topMenu.SetScreenText("Sector Map");
				_bottomMenu.SetActiveDestination(BottomMenu.Destination.None);
				SetMapWorkspaceVisibility(true);
			}
			else if (control == _regionScreen)
			{
				_regionScreen.RefreshFromExternalChange();
				_topMenu.SetScreenText("Sector Map");
				_bottomMenu.SetActiveDestination(BottomMenu.Destination.None);
				SetMapWorkspaceVisibility(true);
			}
			else if (control == _squadScreen)
			{
				_bottomMenu.SetActiveDestination(BottomMenu.Destination.None);
			}
			if (control != _planetTacticalScreen && control != _regionScreen)
			{
				SetMapWorkspaceVisibility(false);
			}
		}
		else
		{
			_topMenu.SetScreenText("Sector Map");
			_bottomMenu.SetActiveDestination(BottomMenu.Destination.None);
			SetMapWorkspaceVisibility(true);
		}
		RefreshTopMenuStatus();
	}

	private static void OnDialogClosed(object sender, EventArgs e)
	{
		if (sender is Control dialog)
		{
			dialog.Visible = false;
		}
	}

	private void OnApothecariumButtonPressed(object sender, EventArgs e)
	{
		PushVisibleOverlaySurface();
		// open the Apothecarium screen
		if (_apothecariumScreen == null)
		{
			PackedScene apothecariumScene = GD.Load<PackedScene>("res://Scenes/ApothecariumScreen/apothecarium_screen.tscn");
			_apothecariumScreen = (ApothecariumScreenController)apothecariumScene.Instantiate();
			_apothecariumScreen.CloseRequested += OnCloseScreen;
			_apothecariumScreen.CampaignChanged += OnCampaignChanged;
			AddPrimaryScreen(_apothecariumScreen);
		}
		if (ToggleOffActivePrimaryScreen(
			_apothecariumScreen,
			BottomMenu.Destination.Apothecarium))
		{
			return;
		}
		_apothecariumScreen.RefreshFromExternalChange();
		ShowPrimaryScreen(
			_apothecariumScreen,
			"Apothecarion",
			BottomMenu.Destination.Apothecarium);
	}

	private void OnTrainingUnitButtonPressed(object sender, EventArgs e)
	{
		PushVisibleOverlaySurface();
		OpenTrainingUnitScreen(toggleIfActive: true);
	}

	private void OpenTrainingUnitScreen(bool mandatorySetup = false, bool toggleIfActive = false)
	{
		if (_trainingUnitScreen == null)
		{
			PackedScene trainingUnitScene = GD.Load<PackedScene>("res://Scenes/TrainingUnitScreen/training_unit_screen.tscn");
			_trainingUnitScreen = (TrainingUnitScreenController)trainingUnitScene.Instantiate();
			_trainingUnitScreen.CloseRequested += OnCloseScreen;
			_trainingUnitScreen.SoldierLinkClicked += OnSoldierSelectedForDisplay;
			_trainingUnitScreen.CampaignChanged += OnCampaignChanged;
			_trainingUnitScreen.NeophytePlacementRequested += OnNeophytePlacementRequested;
			_trainingUnitScreen.Phase13PromotionRequested += OnPhase13PromotionRequested;
			_trainingUnitScreen.ManageAdministrativeStaffRequested +=
				OnManageRecruitmentStaffRequested;
			AddPrimaryScreen(_trainingUnitScreen);
		}
		if (toggleIfActive && ToggleOffActivePrimaryScreen(
			_trainingUnitScreen,
			BottomMenu.Destination.TrainingUnit))
		{
			return;
		}
		_trainingUnitScreen.RefreshFromExternalChange();
		ShowPrimaryScreen(
			_trainingUnitScreen,
			"10th Company",
			BottomMenu.Destination.TrainingUnit);
		if (mandatorySetup)
		{
			_trainingUnitScreen.OpenMandatorySetup();
		}
	}

	private void OnManageRecruitmentStaffRequested(object sender, EventArgs e)
	{
		EnsureChapterScreen();
		_chapterScreen.PopulateCompanyList();
		ShowPrimaryScreen(
			_chapterScreen,
			"Chapter Overview",
			BottomMenu.Destination.Chapter);
		Control recruitmentScreen = (Control)sender;
		_previousScreenStack.Push(recruitmentScreen);
		recruitmentScreen.Visible = false;
	}

	private void OnNeophytePlacementRequested(object sender, int aspirantId)
	{
		ShowRecruitmentPlacementMenu(aspirantId);
	}

	private void OnPhase13PromotionRequested(object sender, int soldierId)
	{
		OnSoldierSelectedForDisplay(sender, soldierId);
	}

	private void ShowRecruitmentPlacementMenu(int subjectId)
	{
		PlayerForce force = GameDataSingleton.Instance.Sector.PlayerForce;
		if (force?.RecruitmentProgram == null)
		{
			_feedbackOverlay.ShowError("The Chapter has no active recruitment program.");
			return;
		}

		ChapterGenerationTemplates templates =
			GameDataSingleton.Instance.GameRulesData.ChapterTemplates;
		SquadTemplate targetTemplate = templates.ScoutSquad;
		List<Squad> targets = force.Army.OrderOfBattle.GetAllSquads()
			.Where(squad => squad.IsOperational)
			.Where(squad => squad.SquadTemplate == targetTemplate)
			.Where(squad =>
				(squad.CurrentRegion?.Planet
					?? squad.BoardedLocation?.Fleet?.Planet)?.Id
				== force.RecruitmentProgram.HomeWorldPlanetId)
			.OrderBy(squad => squad.ParentUnit?.Name)
			.ThenBy(squad => squad.Name)
			.ToList();
		if (targets.Count == 0)
		{
			_feedbackOverlay.ShowError(
				$"No {targetTemplate.Name} is available on or in orbit of the Home World.");
			return;
		}

		if (_recruitmentPlacementMenu == null)
		{
			_recruitmentPlacementMenu = new PopupMenu();
			_recruitmentPlacementMenu.IdPressed += OnRecruitmentTargetSelected;
			_modalLayer.AddChild(_recruitmentPlacementMenu);
		}
		_recruitmentPlacementMenu.Clear();
		foreach (Squad squad in targets)
		{
			_recruitmentPlacementMenu.AddItem(
				$"{squad.Name} - {squad.ParentUnit?.Name} ({SquadLocationFormatter.Format(squad)})",
				squad.Id);
		}
		_pendingRecruitmentSubjectId = subjectId;
		Vector2 mouse = GetGlobalMousePosition();
		_recruitmentPlacementMenu.Position = new Vector2I((int)mouse.X, (int)mouse.Y);
		_recruitmentPlacementMenu.Popup();
	}

	private void OnRecruitmentTargetSelected(long selectedSquadId)
	{
		GameDataSingleton data = GameDataSingleton.Instance;
		GameSession session = new(
			data.GameRulesData,
			data.Sector,
			data.Date,
			StaticRNG.Instance);
		RecruitmentPromotionService service = new(session);
		RecruitmentPromotionResult result = service.PromoteAspirantToNeophyte(
			_pendingRecruitmentSubjectId, checked((int)selectedSquadId));
		if (!result.Succeeded)
		{
			_feedbackOverlay.ShowError(result.Message);
			return;
		}

		MarkCampaignChanged();
		_trainingUnitScreen.RefreshFromExternalChange();
		RefreshTopMenuStatus();
		_feedbackOverlay.ShowSuccess(result.Message);
	}

	private void OnFleetButtonPressed(object sender, EventArgs e)
	{
		PushVisibleOverlaySurface();
		if (_fleetScreen == null)
		{
			PackedScene fleetScene = GD.Load<PackedScene>("res://Scenes/FleetScreen/fleet_screen.tscn");
			_fleetScreen = (FleetScreenController)fleetScene.Instantiate();
			_fleetScreen.CloseRequested += OnCloseScreen;
			_fleetScreen.CampaignChanged += OnCampaignChanged;
			AddPrimaryScreen(_fleetScreen);
		}
		if (ToggleOffActivePrimaryScreen(_fleetScreen, BottomMenu.Destination.Fleet))
		{
			return;
		}
		_fleetScreen.PopulateFleetData();
		ShowPrimaryScreen(
			_fleetScreen,
			"Classis",
			BottomMenu.Destination.Fleet);
	}

	private void OnDiplomacyButtonPressed(object sender, EventArgs e)
	{
		PushVisibleOverlaySurface();
		if (_diplomacyScreen == null)
		{
			PackedScene diplomacyScene = GD.Load<PackedScene>("res://Scenes/DiplomacyScreen/diplomacy_screen.tscn");
			_diplomacyScreen = (DiplomacyScreenController)diplomacyScene.Instantiate();
			_diplomacyScreen.CloseRequested += OnCloseScreen;
			AddPrimaryScreen(_diplomacyScreen);
		}
		if (ToggleOffActivePrimaryScreen(
			_diplomacyScreen,
			BottomMenu.Destination.Diplomacy))
		{
			return;
		}
		_diplomacyScreen.PopulateRequestData();
		ShowPrimaryScreen(
			_diplomacyScreen,
			"Diplomacy",
			BottomMenu.Destination.Diplomacy);
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

	private void LoadPlanetTacticalScreen(Planet planet)
	{
		if (_planetTacticalScreen == null)
		{
			PackedScene planetScene = GD.Load<PackedScene>("res://Scenes/PlanetDetailScreen/planet_tactical_screen.tscn");
			_planetTacticalScreen = (PlanetTacticalScreenController)planetScene.Instantiate();

			_planetTacticalScreen.CloseButtonPressed += OnDialogClosed;
			_planetTacticalScreen.OrbitalSquadDoubleClicked += OnOrbitalSquadDoubleClicked;
			_planetTacticalScreen.RegionDoubleClicked += OnRegionDoubleClicked;
			_planetTacticalScreen.CampaignChanged += OnCampaignChanged;
			_modalLayer.AddChild(_planetTacticalScreen);
		}
		_planetTacticalScreen.PopulatePlanetData(planet);
		_planetTacticalScreen.Visible = true;
		_planetTacticalScreen.MoveToFront();
		GD.Print($"Planet {planet.Id} Clicked");
	}

	private void PlaceMainContentOverlay(Control overlay)
	{
		overlay.AnchorLeft = 0f;
		overlay.AnchorTop = 0f;
		overlay.AnchorRight = 1f;
		overlay.AnchorBottom = 1f;
		overlay.OffsetLeft = 0f;
		overlay.OffsetTop = 0f;
		overlay.OffsetRight = 0f;
		overlay.OffsetBottom = 0f;
		overlay.ClipContents = true;
		_primaryContentHost.MoveChild(overlay, _primaryContentHost.GetChildCount() - 1);
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
			_modalLayer.AddChild(_fleetContextMenu);
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
			_modalLayer.AddChild(_fleetMoveDialog);
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
			_modalLayer.AddChild(_fleetDivideDialog);
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
			_modalLayer.AddChild(_fleetMergeDialog);
		}
		_fleetMergeDialog.SetTaskForce(taskForce);
		_fleetMergeDialog.Visible = true;
	}

	private void OnFleetActionCompleted(object sender, EventArgs e)
	{
		MarkCampaignChanged();
		((Control)sender).Visible = false;
		_sectorMap.RefreshFleets();
		RefreshSelectedSystemInspector();
	}

	private void OnEndTurnButtonPressed(object sender, EventArgs e)
	{
		RequestEndTurn();
	}

	private void OnArchiveButtonPressed(object sender, EventArgs e)
	{
		if (_endOfTurnDialog == null)
		{
			CreateEndOfTurnDialog();
			_endOfTurnDialog.SetSnapshot(
				GameDataSingleton.Instance.Sector?.PlayerForce?.LastTurnReportSnapshot);
		}

		_endOfTurnDialog.Visible = true;
		_endOfTurnDialog.MoveToFront();
	}

	private void ProcessTurnCore()
	{
		// handle squad orders
		TurnResolutionResult turnResult =
			_turnController.ProcessTurn(GameDataSingleton.Instance.Sector);
		RefreshTopMenuStatus();
		_sectorMap.RefreshFleets();
		_sectorMap.RefreshLabels();
		RefreshSelectedSystemInspector();
		CreateEndOfTurnDialog();

		// handle ship movement

		// display end of turn dialog
		_endOfTurnDialog.AddData(GameDataSingleton.Instance.Date, turnResult);
		// Only replace the persisted report after resolution and report construction both succeed.
		// A failed turn therefore leaves the previous report available to the protected pre-turn
		// save and to any later manual save.
		GameDataSingleton.Instance.Sector.PlayerForce.LastTurnReportSnapshot =
			_endOfTurnDialog.LastReportSnapshot;
		_endOfTurnDialog.Visible = true;

		// Surface the opening-scenario resolution (win/lapse) if it fired this turn
		// (Design/Reference/OpeningScenario.md).
		if (!string.IsNullOrEmpty(turnResult.ScenarioNotification))
		{
			ShowScenarioNotification(turnResult.ScenarioNotification);
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
			_scenarioNotificationDialog.CloseButtonPressed += OnScenarioNotificationClosed;
			_modalLayer.AddChild(_scenarioNotificationDialog);
		}
		_scenarioNotificationDialog.SetBriefing(text);
		_scenarioNotificationDialog.Visible = true;
	}

	private void OnScenarioNotificationClosed(object sender, EventArgs e)
	{
		_scenarioNotificationDialog.Visible = false;
		if (GameDataSingleton.Instance.Sector?.PlayerForce?.RecruitmentProgram
			is { IsSetupComplete: false })
		{
			if (_endOfTurnDialog != null)
			{
				_endOfTurnDialog.Visible = false;
			}
			OpenTrainingUnitScreen(mandatorySetup: true);
		}
	}

	private void CreateEndOfTurnDialog()
	{
		if (_endOfTurnDialog != null)
		{
			return;
		}

		PackedScene endOfTurnScene = GD.Load<PackedScene>("res://Scenes/EndOfTurnDialog.tscn");
		_endOfTurnDialog = (EndOfTurnDialogController)endOfTurnScene.Instantiate();
		_endOfTurnDialog.CloseButtonPressed += OnDialogClosed;
		_modalLayer.AddChild(_endOfTurnDialog);
	}

	private void OnSoldierSelectedForDisplay(object sender, int soldierId)
	{
		EnsureChapterScreen();
		_chapterScreen.DisplaySoldier(soldierId);
		ShowPrimaryScreen(
			_chapterScreen,
			"Chapter Overview",
			BottomMenu.Destination.Chapter);
		Control control = (Control)sender;
		_previousScreenStack.Push(control);
		control.Visible = false;
	}

	private void OnRegionDoubleClicked(object sender, Region region)
	{
		if(_regionScreen == null)
		{
			PackedScene regionScene = GD.Load<PackedScene>("res://Scenes/RegionScreen/region_screen.tscn");
			_regionScreen = (RegionScreenController)regionScene.Instantiate();
			_regionScreen.CloseButtonPressed += OnCloseScreen;
			_regionScreen.SquadDoubleClicked += OnSquadDoubleClicked;
			_regionScreen.AdjacentRegionChangeRequested += OnAdjacentRegionChangeRequested;
			_regionScreen.CampaignChanged += OnCampaignChanged;
			_modalLayer.AddChild(_regionScreen);
		}
		_regionScreen.DisplayRegion(region);
		_regionScreen.Visible = true;
		_regionScreen.MoveToFront();
		Control control = (Control)sender;
		_previousScreenStack.Push(control);
		control.Visible = false;
	}

	private void OnAdjacentRegionChangeRequested(object sender, Region region)
	{
		_regionScreen?.DisplayRegion(region);
	}

	private void OnSquadDoubleClicked(object sender, Squad squad)
	{
		if (_squadScreen == null)
		{
			PackedScene squadScene = GD.Load<PackedScene>("res://Scenes/SquadScreen/squad_screen.tscn");
			_squadScreen = (SquadScreenController)squadScene.Instantiate();
			AddPrimaryScreen(_squadScreen);
			_squadScreen.CloseRequested += OnCloseScreen;
			_squadScreen.CampaignChanged += OnCampaignChanged;
		}
		PlaceMainContentOverlay(_squadScreen);
		_squadScreen.SetSquad(squad);
		_squadScreen.Visible = true;
		SetMapWorkspaceVisibility(false);
		_bottomMenu.SetActiveDestination(BottomMenu.Destination.None);
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
			AddPrimaryScreen(_squadScreen);
			_squadScreen.CloseRequested += OnCloseScreen;
			_squadScreen.CampaignChanged += OnCampaignChanged;
		}
		PlaceMainContentOverlay(_squadScreen);
		_squadScreen.SetSquad(squad);
		_squadScreen.Visible = true;
		SetMapWorkspaceVisibility(false);
		_bottomMenu.SetActiveDestination(BottomMenu.Destination.None);
		Control control = (Control)sender;
		_previousScreenStack.Push(control);
		control.Visible = false;
	}
}
