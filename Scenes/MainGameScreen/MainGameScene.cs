using Godot;
using OnlyWar.Application;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Helpers.Storage;
using OnlyWar.Models.Command;
using System;
using System.Collections.Generic;

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
	private PlanetaryOperationsScreenController _planetaryOperationsScreen;
	private bool _planetaryOperationsReturnsToStack;
	private string _planetaryOperationsReturnTitle;
	private Stack<Control> _previousScreenStack;
	private CanvasLayer _mainUILayer;
	private Control _primaryContentHost;
	private Control _modalLayer;
	private MainScreenController _activePrimaryScreen;
	private CommandScreenController _commandScreen;
	private ActivityOverlay _activityOverlay;
	private CampaignApplication _campaignApplication;
	private EndOfTurnDialogController _endOfTurnDialog;
	private BriefingDialogController _scenarioNotificationDialog;
	private PopupMenu _recruitmentPlacementMenu;
	private int _pendingRecruitmentSubjectId;
	private int? _selectedPlanetId;
	private int? _selectedFleetId;
	private bool _isProcessingTurn;

	internal CampaignApplication CampaignApplication => _campaignApplication;

	internal void Configure(CampaignApplication campaignApplication)
	{
		_campaignApplication = campaignApplication
			?? throw new ArgumentNullException(nameof(campaignApplication));
	}

	// The sector map is a scene child, so its own _Ready runs before this scene's. The host must
	// configure the application here, where the parent still runs first.
	public override void _EnterTree()
	{
		if (_campaignApplication == null) return;
		GetNode<SectorMap>("SectorMap").Configure(_campaignApplication);
	}

	public override void _Ready()
	{
		if (_campaignApplication?.HasCampaign != true)
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
		_topMenu.ScreenTitlePressed += OnTopMenuScreenTitlePressed;
		_leftMapTools.MapToolPressed += OnMapToolPressed;
		_systemInspector.OpenSystemPressed += OnInspectorOpenSystemPressed;
		_systemInspector.PlotCoursePressed += OnInspectorPlotCoursePressed;
		_systemInspector.DivideFleetPressed += OnInspectorDivideFleetPressed;
		_systemInspector.MergeFleetPressed += OnInspectorMergeFleetPressed;
		_systemInspector.LandSquadsPressed += OnInspectorOpenFleetPlanetPressed;
		_systemInspector.LoadSquadsPressed += OnInspectorOpenFleetPlanetPressed;
		_systemInspector.AnswerGovernorRequestPressed += OnInspectorAnswerGovernorRequestPressed;
		_bottomMenu.ChapterButtonPressed += OnChapterButtonPressed;
		_bottomMenu.ApothecariumButtonPressed += OnApothecariumButtonPressed;
		_bottomMenu.TrainingUnitButtonPressed += OnTrainingUnitButtonPressed;
		_bottomMenu.FleetButtonPressed += OnFleetButtonPressed;
		_bottomMenu.DiplomacyButtonPressed += OnDiplomacyButtonPressed;
		_bottomMenu.CommandButtonPressed += OnCommandButtonPressed;
		_bottomMenu.EndTurnButtonPressed += OnEndTurnButtonPressed;
		_sectorMap = GetNode<SectorMap>("SectorMap");
		_sectorMap.PlanetClicked += OnPlanetClicked;
		_sectorMap.PlanetDoubleClicked += OnPlanetDoubleClicked;
		_sectorMap.FleetClicked += OnFleetClicked;
		_sectorMap.FleetRightClicked += OnFleetRightClicked;
		_sectorMap.BackgroundClicked += OnMapBackgroundClicked;
		_mainUILayer = GetNode<CanvasLayer>("UILayer");
		_primaryContentHost = GetNode<Control>("UILayer/PrimaryContentHost");
		_modalLayer = GetNode<Control>("UILayer/ModalLayer");
		_activityOverlay = GetNode<ActivityOverlay>("UILayer/ActivityOverlay");
		_systemInspector.Configure(_campaignApplication);
		_previousScreenStack = new Stack<Control>();
		InitializeCampaignControls();
		RefreshTopMenuStatus();

		// The application decides which world the campaign opens on and whether the founding
		// directive is still outstanding; the scene only navigates to the answer.
		MainScreenStartupView startup = _campaignApplication.QueryStartup();
		SelectPlanet(startup.InitialPlanetId);

		// The first-turn directive belongs to the Command Brief. It is acknowledged only after the
		// workspace has been instantiated and rendered successfully; later manual visits keep it
		// visible while the objective remains relevant.
		if (startup.OpeningBriefPending && OpenCommandScreen(autoOpen: true))
		{
			_campaignApplication.AcknowledgeOpeningBrief(_campaignApplication.SessionToken);
		}
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
		CampaignHeaderView header = _campaignApplication.QueryHeader();
		_topMenu.SetDateText(header.DateText);
		_topMenu.SetRequisitionAmount(header.Requisition);
	}

	private void OnTopMenuScreenTitlePressed(object sender, EventArgs e)
	{
		if (_planetaryOperationsScreen?.Visible == true)
		{
			_planetaryOperationsScreen.ShowWorldDossierOverlay();
		}
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
		foreach (Control surface in new Control[] { _squadScreen, _planetaryOperationsScreen })
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
		_chapterScreen.Configure(_campaignApplication, _campaignApplication);
		_chapterScreen.CloseRequested += OnCloseScreen;
		_chapterScreen.CampaignChanged += OnCampaignChanged;
		_chapterScreen.SquadLocationRequested += OnChapterSquadLocationRequested;
		_chapterScreen.ScreenTitleChanged += OnChapterScreenTitleChanged;
		AddPrimaryScreen(_chapterScreen);
	}

	private void OnChapterScreenTitleChanged(object sender, string title)
	{
		_topMenu.SetScreenText(title);
	}

	private void OnCommandButtonPressed(object sender, EventArgs e)
	{
		OpenCommandScreen(autoOpen: false);
	}

	private void EnsureCommandScreen()
	{
		if (_commandScreen != null)
		{
			return;
		}

		PackedScene commandScene = GD.Load<PackedScene>(
			"res://Scenes/CommandScreen/command_screen.tscn");
		_commandScreen = commandScene.Instantiate<CommandScreenController>();
		_commandScreen.Configure(_campaignApplication);
		_commandScreen.CloseRequested += OnCloseScreen;
		_commandScreen.NavigationRequested += OnCommandNavigationRequested;
		_commandScreen.LastTurnReportRequested += OnCommandLastTurnReportRequested;
		AddPrimaryScreen(_commandScreen);
	}

	private bool OpenCommandScreen(bool autoOpen)
	{
		PushVisibleOverlaySurface();
		EnsureCommandScreen();
		if (!autoOpen && ToggleOffActivePrimaryScreen(
			_commandScreen,
			BottomMenu.Destination.Command))
		{
			return false;
		}

		if (autoOpen)
		{
			_commandScreen.SelectBrief();
		}
		else
		{
			_commandScreen.RefreshFromExternalChange();
		}
		ShowPrimaryScreen(
			_commandScreen,
			"Command",
			BottomMenu.Destination.Command);
		return _commandScreen.HasRenderedBrief || !autoOpen;
	}

	private void OnCommandLastTurnReportRequested(object sender, EventArgs e)
	{
		ShowLastTurnReport();
	}

	private void ShowLastTurnReport()
	{
		CreateEndOfTurnDialog();
		_endOfTurnDialog.SetReport(_campaignApplication.QueryLastTurnReport());
		_endOfTurnDialog.Visible = true;
		_endOfTurnDialog.MoveToFront();
	}

	private void OnCommandNavigationRequested(
		object sender,
		CampaignNavigationTarget target)
	{
		if (target == null || !target.IsAvailable)
		{
			return;
		}

		// The application resolves which surface a navigation target actually lands on; the
		// scene only opens it.
		CampaignNavigationRoute route = _campaignApplication.ResolveNavigation(
			target.Kind, target.PrimaryId);
		switch (route.Kind)
		{
			case CampaignNavigationRouteKind.LastTurnReport:
				ShowLastTurnReport();
				return;
			case CampaignNavigationRouteKind.Recruitment:
				PrepareCommandReturnSurface();
				OpenTrainingUnitScreen();
				return;
			case CampaignNavigationRouteKind.Fleet:
				PrepareCommandReturnSurface();
				ShowFleetScreen();
				return;
			case CampaignNavigationRouteKind.SquadOnShip:
				PrepareCommandReturnSurface();
				ShowFleetScreen(route.SquadId);
				return;
			case CampaignNavigationRouteKind.SquadInRegion:
				NavigateFromCommandToRegion(
					route.RegionId.Value, route.PlanetId.Value, route.SquadId);
				return;
			case CampaignNavigationRouteKind.Soldier:
				NavigateFromCommandToSoldier(route.SoldierId.Value);
				return;
			case CampaignNavigationRouteKind.PlanetOperations:
				SelectPlanet(route.PlanetId);
				OpenPlanetaryOperations(route.PlanetId.Value);
				return;
			case CampaignNavigationRouteKind.RegionOperations:
				NavigateFromCommandToRegion(
					route.RegionId.Value, route.PlanetId.Value, selectedSquadId: null);
				return;
			case CampaignNavigationRouteKind.Diplomacy:
				PrepareCommandReturnSurface();
				OnDiplomacyButtonPressed(this, EventArgs.Empty);
				if (route.FocusId.HasValue)
				{
					_diplomacyScreen.FocusRequest(route.FocusId.Value);
				}
				return;
			case CampaignNavigationRouteKind.Apothecarium:
				PrepareCommandReturnSurface();
				OnApothecariumButtonPressed(this, EventArgs.Empty);
				if (route.FocusId.HasValue)
				{
					_apothecariumScreen.FocusSoldier(route.FocusId.Value);
				}
				return;
			case CampaignNavigationRouteKind.SectorMap:
				if (_activePrimaryScreen == _commandScreen)
				{
					_commandScreen.Visible = false;
					_activePrimaryScreen = null;
				}
				_topMenu.SetScreenText("Sector Map");
				_bottomMenu.SetActiveDestination(BottomMenu.Destination.None);
				SetMapWorkspaceVisibility(true);
				return;
		}
	}

	private void PrepareCommandReturnSurface()
	{
		if (_activePrimaryScreen == _commandScreen)
		{
			_commandScreen.Visible = false;
			_activePrimaryScreen = null;
			_previousScreenStack.Push(_commandScreen);
		}
	}

	private void NavigateFromCommandToSoldier(int soldierId)
	{
		EnsureChapterScreen();
		_chapterScreen.DisplaySoldier(soldierId);
		PrepareCommandReturnSurface();
		ShowPrimaryScreen(
			_chapterScreen,
			"Chapter Overview",
			BottomMenu.Destination.Chapter);
	}

	private void NavigateFromCommandToRegion(int regionId, int planetId, int? selectedSquadId)
	{
		_commandScreen.Visible = false;
		if (_activePrimaryScreen == _commandScreen)
		{
			_activePrimaryScreen = null;
		}
		OpenPlanetaryOperationsRegion(regionId, planetId, selectedSquadId, _commandScreen);
	}

	private void OnChapterSquadLocationRequested(object sender, int squadId)
	{
		CampaignNavigationRoute route = _campaignApplication.ResolveSquadLocation(squadId);
		if (route.Kind == CampaignNavigationRouteKind.SquadOnShip)
		{
			ShowFleetScreen(squadId);
			return;
		}

		if (route.Kind == CampaignNavigationRouteKind.SquadInRegion)
		{
			NavigateToLandedSquad(route.RegionId.Value, route.PlanetId.Value, squadId);
		}
	}

	private void NavigateToLandedSquad(int regionId, int planetId, int squadId)
	{
		// Close the Chapter primary screen first so the normal overlay stack can restore its
		// previous surface (or the sector map) underneath the planet and region detail screens.
		if (_activePrimaryScreen == _chapterScreen)
		{
			_chapterScreen.RequestClose();
		}

		OpenPlanetaryOperationsRegion(regionId, planetId, squadId, null);
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
			else if (control == _commandScreen)
			{
				_activePrimaryScreen = _commandScreen;
				_commandScreen.RefreshFromExternalChange();
				_topMenu.SetScreenText("Command");
				_bottomMenu.SetActiveDestination(BottomMenu.Destination.Command);
			}
			else if (control == _trainingUnitScreen)
			{
				_activePrimaryScreen = _trainingUnitScreen;
				_trainingUnitScreen.RefreshFromExternalChange();
				_topMenu.SetScreenText("10th Company");
				_bottomMenu.SetActiveDestination(BottomMenu.Destination.TrainingUnit);
			}
			else if (control == _planetaryOperationsScreen)
			{
				_planetaryOperationsScreen.RefreshFromExternalChange();
				_topMenu.SetScreenText("Sector Map");
				_bottomMenu.SetActiveDestination(BottomMenu.Destination.None);
				SetMapWorkspaceVisibility(true);
			}
			else if (control == _squadScreen)
			{
				_bottomMenu.SetActiveDestination(BottomMenu.Destination.None);
			}
			if (control != _planetaryOperationsScreen)
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
			_apothecariumScreen.Configure(_campaignApplication);
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
			"Apothecarium",
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
			_trainingUnitScreen.Configure(_campaignApplication);
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
		// Which squads can receive a neophyte - and the wording when none can - is a recruitment
		// rule, so the screen asks rather than filtering the order of battle itself.
		NeophytePlacementOptions options =
			_campaignApplication.QueryNeophytePlacementTargets();
		if (!options.IsAvailable)
		{
			_feedbackOverlay.ShowError(options.UnavailableReason);
			return;
		}

		if (_recruitmentPlacementMenu == null)
		{
			_recruitmentPlacementMenu = new PopupMenu();
			_recruitmentPlacementMenu.IdPressed += OnRecruitmentTargetSelected;
			_modalLayer.AddChild(_recruitmentPlacementMenu);
		}
		_recruitmentPlacementMenu.Clear();
		foreach (NeophytePlacementTarget target in options.Targets)
		{
			_recruitmentPlacementMenu.AddItem(target.Label, target.SquadId);
		}
		_pendingRecruitmentSubjectId = subjectId;
		Vector2 mouse = GetGlobalMousePosition();
		_recruitmentPlacementMenu.Position = new Vector2I((int)mouse.X, (int)mouse.Y);
		_recruitmentPlacementMenu.Popup();
	}

	private void OnRecruitmentTargetSelected(long selectedSquadId)
	{
		NeophytePlacementResult result = _campaignApplication.PlaceNeophyte(
			_campaignApplication.SessionToken,
			_pendingRecruitmentSubjectId,
			checked((int)selectedSquadId));
		if (!result.Succeeded)
		{
			_feedbackOverlay.ShowError(result.Message);
			return;
		}

		_trainingUnitScreen.RefreshFromExternalChange();
		RefreshTopMenuStatus();
		_feedbackOverlay.ShowSuccess(result.Message);
	}

	private void OnFleetButtonPressed(object sender, EventArgs e)
	{
		ShowFleetScreen();
	}

	private void ShowFleetScreen(int? focusSquadId = null)
	{
		PushVisibleOverlaySurface();
		if (_fleetScreen == null)
		{
			PackedScene fleetScene = GD.Load<PackedScene>("res://Scenes/FleetScreen/fleet_screen.tscn");
			_fleetScreen = (FleetScreenController)fleetScene.Instantiate();
			_fleetScreen.Configure(_campaignApplication);
			_fleetScreen.CloseRequested += OnCloseScreen;
			_fleetScreen.CampaignChanged += OnCampaignChanged;
			AddPrimaryScreen(_fleetScreen);
		}
		if (focusSquadId == null
			&& ToggleOffActivePrimaryScreen(_fleetScreen, BottomMenu.Destination.Fleet))
		{
			return;
		}
		_fleetScreen.PopulateFleetData(focusSquadId);
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
			_diplomacyScreen.Configure(_campaignApplication);
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
		SelectPlanet(planetId);
	}

	private void OnPlanetDoubleClicked(object sender, int planetId)
	{
		SelectPlanet(planetId);
		OpenPlanetaryOperations(planetId);
	}

	private void OnMapBackgroundClicked(object sender, EventArgs e)
	{
		SelectPlanet(null);
	}

	private void SelectPlanet(int? planetId, int? selectedFleetId = null)
	{
		_selectedPlanetId = planetId;
		_selectedFleetId = selectedFleetId;
		_sectorMap.SetSelectedPlanet(planetId);
		_systemInspector.DisplayPlanet(planetId, selectedFleetId);
	}

	private void RefreshSelectedSystemInspector()
	{
		if (!_selectedPlanetId.HasValue)
		{
			int? fleetContext = _selectedFleetId.HasValue
				? _campaignApplication.QueryFleetContextPlanet(_selectedFleetId.Value)
				: null;
			if (_selectedFleetId.HasValue)
			{
				_systemInspector.DisplayFleetContext(fleetContext, _selectedFleetId);
			}
			else
			{
				_systemInspector.DisplayEmptyState();
			}
			return;
		}

		_systemInspector.DisplayPlanet(_selectedPlanetId, _selectedFleetId);
		if (!_systemInspector.HasSystem)
		{
			SelectPlanet(null);
		}
	}

	private void OpenPlanetaryOperations(int planetId)
	{
		RememberPlanetaryOperationsReturnTitle();
		if (_planetaryOperationsScreen == null)
		{
			PackedScene planetScene = GD.Load<PackedScene>("res://Scenes/PlanetaryOperationsScreen/planetary_operations_screen.tscn");
			_planetaryOperationsScreen = (PlanetaryOperationsScreenController)planetScene.Instantiate();
			_planetaryOperationsScreen.Configure(_campaignApplication);
			_planetaryOperationsScreen.CloseButtonPressed += OnPlanetaryOperationsClosed;
			_planetaryOperationsScreen.SquadDoubleClicked += OnPlanetarySquadDoubleClicked;
			_planetaryOperationsScreen.FleetManagementRequested += OnPlanetaryFleetManagementRequested;
			_planetaryOperationsScreen.RecoveryOperationsRequested += OnPlanetaryRecoveryOperationsRequested;
			_planetaryOperationsScreen.CampaignChanged += OnCampaignChanged;
			_modalLayer.AddChild(_planetaryOperationsScreen);
		}
		_planetaryOperationsReturnsToStack = false;
		_planetaryOperationsScreen.DisplayPlanet(planetId);
		SetPlanetaryOperationsTitle(planetId);
		_planetaryOperationsScreen.Visible = true;
		_planetaryOperationsScreen.MoveToFront();
		GD.Print($"Planet {planetId} Clicked");
	}

	private void OnPlanetaryFleetManagementRequested(object sender, int planetId)
	{
		ShowFleetScreen();
	}

	private void OnPlanetaryOperationsClosed(object sender, EventArgs e)
	{
		if (_planetaryOperationsReturnsToStack)
		{
			_planetaryOperationsReturnsToStack = false;
			_planetaryOperationsReturnTitle = null;
			OnCloseScreen(sender, e);
			return;
		}
		OnDialogClosed(sender, e);
		_topMenu.SetScreenText(_planetaryOperationsReturnTitle ?? "Sector Map");
		_planetaryOperationsReturnTitle = null;
	}

	private void OpenPlanetaryOperationsRegion(
		int regionId, int planetId, int? selectedSquadId, Control returnSurface)
	{
		RememberPlanetaryOperationsReturnTitle();
		if (_planetaryOperationsScreen == null)
		{
			PackedScene operationsScene = GD.Load<PackedScene>("res://Scenes/PlanetaryOperationsScreen/planetary_operations_screen.tscn");
			_planetaryOperationsScreen = (PlanetaryOperationsScreenController)operationsScene.Instantiate();
			_planetaryOperationsScreen.Configure(_campaignApplication);
			_planetaryOperationsScreen.CloseButtonPressed += OnPlanetaryOperationsClosed;
			_planetaryOperationsScreen.SquadDoubleClicked += OnPlanetarySquadDoubleClicked;
			_planetaryOperationsScreen.FleetManagementRequested += OnPlanetaryFleetManagementRequested;
			_planetaryOperationsScreen.RecoveryOperationsRequested += OnPlanetaryRecoveryOperationsRequested;
			_planetaryOperationsScreen.CampaignChanged += OnCampaignChanged;
			_modalLayer.AddChild(_planetaryOperationsScreen);
		}
		_planetaryOperationsScreen.DisplayRegion(regionId, planetId, selectedSquadId);
		SetPlanetaryOperationsTitle(planetId);
		_planetaryOperationsScreen.Visible = true;
		_planetaryOperationsScreen.MoveToFront();
		if (returnSurface != null)
		{
			_planetaryOperationsReturnsToStack = true;
			_previousScreenStack.Push(returnSurface);
			returnSurface.Visible = false;
		}
		else
		{
			_planetaryOperationsReturnsToStack = false;
		}
	}

	private void RememberPlanetaryOperationsReturnTitle()
	{
		if (_planetaryOperationsScreen?.Visible != true)
		{
			_planetaryOperationsReturnTitle = _topMenu.GetScreenText();
		}
	}

	private void SetPlanetaryOperationsTitle(int planetId)
	{
		string name = _campaignApplication.QueryPlanetName(planetId);
		_topMenu.SetScreenText($"PLANETARY OPERATIONS / {name?.ToUpperInvariant()}");
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
		SelectFleetContext(fleetId);
	}

	private void OnFleetRightClicked(object sender, int fleetId)
	{
		SelectFleetContext(fleetId);
		ShowFleetContextMenu(fleetId);
	}

	private void SelectFleetContext(int fleetId)
	{
		_selectedPlanetId = null;
		_selectedFleetId = fleetId;
		_sectorMap.SetSelectedPlanet(null);
		_systemInspector.DisplayFleetContext(
			_campaignApplication.QueryFleetContextPlanet(fleetId), fleetId);
	}

	private void ShowFleetContextMenu(int fleetId)
	{
		// Whether a task force can be re-tasked at all, and which of the three actions it
		// currently offers, is decided by the application.
		FleetActionAvailability actions = _campaignApplication.QueryFleetActions(fleetId);
		if (!actions.IsActionable) return;

		_contextFleetId = fleetId;

		if (_fleetContextMenu == null)
		{
			_fleetContextMenu = new PopupMenu();
			_fleetContextMenu.AddItem("Plot Course", FleetMenuPlotCourse);
			_fleetContextMenu.AddItem("Divide Task Force", FleetMenuDivide);
			_fleetContextMenu.AddItem("Merge Task Force", FleetMenuMerge);
			_fleetContextMenu.IdPressed += OnFleetContextMenuIdPressed;
			_modalLayer.AddChild(_fleetContextMenu);
		}

		_fleetContextMenu.SetItemDisabled(
			_fleetContextMenu.GetItemIndex(FleetMenuDivide), !actions.CanDivide);
		_fleetContextMenu.SetItemDisabled(
			_fleetContextMenu.GetItemIndex(FleetMenuMerge), !actions.CanMerge);

		_fleetContextMenu.Position = (Vector2I)GetViewport().GetMousePosition();
		_fleetContextMenu.ResetSize();
		_fleetContextMenu.Popup();
	}

	private void OnInspectorOpenSystemPressed(object sender, int planetId)
	{
		SelectPlanet(planetId);
		OpenPlanetaryOperations(planetId);
	}

	private void OnInspectorPlotCoursePressed(object sender, int fleetId)
	{
		if (!_campaignApplication.QueryFleetActions(fleetId).CanPlotCourse) return;
		OpenFleetMoveDialog(fleetId);
	}

	private void OnInspectorDivideFleetPressed(object sender, int fleetId)
	{
		if (!_campaignApplication.QueryFleetActions(fleetId).IsActionable) return;
		OpenFleetDivideDialog(fleetId);
	}

	private void OnInspectorMergeFleetPressed(object sender, int fleetId)
	{
		if (!_campaignApplication.QueryFleetActions(fleetId).IsActionable) return;
		OpenFleetMergeDialog(fleetId);
	}

	private void OnInspectorOpenFleetPlanetPressed(object sender, int fleetId)
	{
		FleetLocationView location = _campaignApplication.QueryFleetLocation(fleetId);
		if (!location.IsActionable || !location.PlanetId.HasValue) return;

		SelectPlanet(location.PlanetId, fleetId);
		OpenPlanetaryOperations(location.PlanetId.Value);
	}

	private void OnFleetContextMenuIdPressed(long id)
	{
		switch ((int)id)
		{
			case FleetMenuPlotCourse:
				OpenFleetMoveDialog(_contextFleetId);
				break;
			case FleetMenuDivide:
				OpenFleetDivideDialog(_contextFleetId);
				break;
			case FleetMenuMerge:
				OpenFleetMergeDialog(_contextFleetId);
				break;
		}
	}

	private void OpenFleetMoveDialog(int fleetId)
	{
		if (_fleetMoveDialog == null)
		{
			PackedScene fleetMoveScene = GD.Load<PackedScene>("res://Scenes/FleetScreen/fleet_move_dialog.tscn");
			_fleetMoveDialog = (FleetMoveDialogController)fleetMoveScene.Instantiate();
			_fleetMoveDialog.Configure(_campaignApplication);
			_fleetMoveDialog.CloseButtonPressed += (s, e) => _fleetMoveDialog.Visible = false;
			_fleetMoveDialog.CoursePlotted += OnFleetActionCompleted;
			_modalLayer.AddChild(_fleetMoveDialog);
		}
		_fleetMoveDialog.SetTaskForce(fleetId);
		_fleetMoveDialog.Visible = true;
	}

	private void OpenFleetDivideDialog(int fleetId)
	{
		if (_fleetDivideDialog == null)
		{
			PackedScene fleetDivideScene = GD.Load<PackedScene>("res://Scenes/FleetScreen/fleet_divide_dialog.tscn");
			_fleetDivideDialog = (FleetDivideDialogController)fleetDivideScene.Instantiate();
			_fleetDivideDialog.Configure(_campaignApplication);
			_fleetDivideDialog.CloseButtonPressed += (s, e) => _fleetDivideDialog.Visible = false;
			_fleetDivideDialog.FleetDivided += OnFleetActionCompleted;
			_modalLayer.AddChild(_fleetDivideDialog);
		}
		_fleetDivideDialog.SetTaskForce(fleetId);
		_fleetDivideDialog.Visible = true;
	}

	private void OpenFleetMergeDialog(int fleetId)
	{
		if (_fleetMergeDialog == null)
		{
			PackedScene fleetMergeScene = GD.Load<PackedScene>("res://Scenes/FleetScreen/fleet_merge_dialog.tscn");
			_fleetMergeDialog = (FleetMergeDialogController)fleetMergeScene.Instantiate();
			_fleetMergeDialog.Configure(_campaignApplication);
			_fleetMergeDialog.CloseButtonPressed += (s, e) => _fleetMergeDialog.Visible = false;
			_fleetMergeDialog.FleetsMerged += OnFleetActionCompleted;
			_modalLayer.AddChild(_fleetMergeDialog);
		}
		_fleetMergeDialog.SetTaskForce(fleetId);
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

	private void OnInspectorAnswerGovernorRequestPressed(object sender, int planetId)
	{
		SelectPlanet(planetId);
		OpenPlanetaryOperations(planetId);
		_planetaryOperationsScreen.DisplayGovernorRequest(planetId);
	}

	private bool ProcessTurnCore()
	{
		// Resolution, the turn report and its persistence are one application command; the scene
		// only refreshes what it shows and opens the dialogs.
		ResolveTurnView turn = _campaignApplication.ResolveTurn(
			_campaignApplication.SessionToken);
		if (!turn.Succeeded)
		{
			_feedbackOverlay.ShowError(turn.Message);
			return false;
		}

		RefreshTopMenuStatus();
		_sectorMap.RefreshFleets();
		_sectorMap.RefreshLabels();
		RefreshSelectedSystemInspector();
		CreateEndOfTurnDialog();

		_endOfTurnDialog.SetReport(turn.Report);
		_commandScreen?.RefreshFromExternalChange();
		_endOfTurnDialog.Visible = true;

		// Surface the opening-scenario resolution (win/lapse) if it fired this turn
		// (Design/Reference/OpeningScenario.md).
		if (!string.IsNullOrEmpty(turn.ScenarioNotification))
		{
			ShowScenarioNotification(turn.ScenarioNotification);
		}
		return true;
	}

	// Reuses the briefing dialog scene (a BBCode message + single acknowledge button) as a
	// generic scenario-resolution notification, on its own instance so its dismissal does not
	// touch the Command Brief's automatic-opening acknowledgement.
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
		if (_campaignApplication.RequiresRecruitmentSetup())
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

	private void OnPlanetaryRecoveryOperationsRequested(object sender, int soldierId)
	{
		OnApothecariumButtonPressed(sender, EventArgs.Empty);
		_apothecariumScreen?.FocusSoldier(soldierId);
	}

	private void OnPlanetarySquadDoubleClicked(object sender, int squadId)
	{
		if (_squadScreen == null)
		{
			PackedScene squadScene = GD.Load<PackedScene>("res://Scenes/SquadScreen/squad_screen.tscn");
			_squadScreen = (SquadScreenController)squadScene.Instantiate();
			_squadScreen.Configure(_campaignApplication);
			AddPrimaryScreen(_squadScreen);
			_squadScreen.CloseRequested += OnCloseScreen;
			_squadScreen.CampaignChanged += OnCampaignChanged;
		}
		PlaceMainContentOverlay(_squadScreen);
		_squadScreen.SetSquad(squadId);
		_squadScreen.Visible = true;
		SetMapWorkspaceVisibility(false);
		_bottomMenu.SetActiveDestination(BottomMenu.Destination.None);
		Control control = (Control)sender;
		_previousScreenStack.Push(control);
		control.Visible = false;
	}
}
