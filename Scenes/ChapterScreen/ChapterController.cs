using Godot;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Recruitment;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class ChapterController : MainScreenController
{
    private readonly ChapterBrowserNavigator _navigator = new();
    private readonly SoldierTransferService _transferService = new();
    private readonly SoldierDetailBuilder _soldierDetailBuilder = new();
    private readonly SoldierFilterService _filterService = new();
    private List<SoldierTransferOption> _transferOptions = [];
    private List<SoldierFilterCondition> _activeFilter = [];
    private SoldierTransferOption _pendingTransferOption;
    private int? _pendingTransferSoldierId;
    private int? _currentDetailSoldierId;
    private int? _historicalSoldierId;
    private bool _pendingBlackCarapaceSurgery;
    private ConfirmationDialog _transferConfirmationDialog;
    private ConfirmationDialog _recallConfirmationDialog;
    private int? _pendingRecallSoldierId;
    private AcceptDialog _transferBlockedDialog;
    private ChapterFilterDialog _filterDialog;
    private LoadoutDoctrineDialog _loadoutDoctrineDialog;
    private ChapterMusterScreenController _musterScreen;

    public ChapterView ChapterView { get; set; }

    public event EventHandler CampaignChanged;
    public event EventHandler<Squad> SquadLocationRequested;
    public event EventHandler<string> ScreenTitleChanged;

    public override void _Ready()
    {
        base._Ready();
        if (ChapterView == null)
        {
            ChapterView = GetNode<ChapterView>("ChapterView");
        }

        ChapterView.BrowserItemSelected += OnBrowserItemSelected;
        ChapterView.BrowserItemDrillRequested += OnBrowserItemDrillRequested;
        ChapterView.BrowserItemLocationRequested += OnBrowserItemLocationRequested;
        ChapterView.DetailLocationRequested += OnDetailLocationRequested;
        ChapterView.BreadcrumbPressed += OnBreadcrumbPressed;
        ChapterView.TransferTargetSelected += OnTransferTargetSelected;
        ChapterView.FilterButtonPressed += OnFilterButtonPressed;
        ChapterView.ChapterLoadoutsPressed += OnChapterLoadoutsPressed;
        ChapterView.ChapterMusterPressed += (_, _) => OpenMuster(_currentDetailSoldierId);

        _transferConfirmationDialog = new ConfirmationDialog
        {
            Title = "Confirm Transfer"
        };
        _transferConfirmationDialog.Confirmed += OnTransferConfirmed;
        AddChild(_transferConfirmationDialog);

        ChapterView.DetailPrimaryActionPressed += OnDetailPrimaryActionPressed;
        _recallConfirmationDialog = new ConfirmationDialog
        {
            Title = "Confirm Recall"
        };
        _recallConfirmationDialog.Confirmed += OnRecallConfirmed;
        AddChild(_recallConfirmationDialog);

        _transferBlockedDialog = new AcceptDialog
        {
            Title = "Transfer Blocked"
        };
        AddChild(_transferBlockedDialog);

        _filterDialog = new ChapterFilterDialog();
        _filterDialog.FilterApplied += OnFilterApplied;
        _filterDialog.FilterCleared += OnFilterCleared;
        AddChild(_filterDialog);

        _loadoutDoctrineDialog = new LoadoutDoctrineDialog();
        _loadoutDoctrineDialog.DoctrineChanged += (_, _) => CampaignChanged?.Invoke(this, EventArgs.Empty);
        AddChild(_loadoutDoctrineDialog);

        RenderCurrentPath();
    }

    private void EnsureMusterScreen()
    {
        if (_musterScreen != null)
        {
            return;
        }

        PackedScene scene = GD.Load<PackedScene>(
            "res://Scenes/ChapterMusterScreen/chapter_muster_screen.tscn");
        _musterScreen = scene.Instantiate<ChapterMusterScreenController>();
        _musterScreen.Visible = false;
        _musterScreen.CampaignChanged += (_, _) => CampaignChanged?.Invoke(this, EventArgs.Empty);
        _musterScreen.BackRequested += (_, _) => ShowChapterOverview();
        AddChild(_musterScreen);
    }

    private void OpenMuster(int? soldierId)
    {
        EnsureMusterScreen();
        ChapterView.Visible = false;
        _musterScreen.Visible = true;
        _musterScreen.OpenForSoldier(soldierId);
        ScreenTitleChanged?.Invoke(this, "Bulk Transfers");
    }

    private void ShowChapterOverview(bool refresh = true)
    {
        if (_musterScreen != null)
        {
            _musterScreen.Visible = false;
        }
        if (ChapterView != null)
        {
            ChapterView.Visible = true;
        }
        if (refresh)
        {
            RenderCurrentPath();
        }
        ScreenTitleChanged?.Invoke(this, "Chapter Overview");
    }

    public override void _ExitTree()
    {
        if (ChapterView == null)
        {
            return;
        }

        ChapterView.BrowserItemSelected -= OnBrowserItemSelected;
        ChapterView.BrowserItemDrillRequested -= OnBrowserItemDrillRequested;
        ChapterView.BrowserItemLocationRequested -= OnBrowserItemLocationRequested;
        ChapterView.DetailLocationRequested -= OnDetailLocationRequested;
        ChapterView.BreadcrumbPressed -= OnBreadcrumbPressed;
        ChapterView.TransferTargetSelected -= OnTransferTargetSelected;
        ChapterView.FilterButtonPressed -= OnFilterButtonPressed;
        ChapterView.ChapterLoadoutsPressed -= OnChapterLoadoutsPressed;
        ChapterView.DetailPrimaryActionPressed -= OnDetailPrimaryActionPressed;
        if (_transferConfirmationDialog != null)
        {
            _transferConfirmationDialog.Confirmed -= OnTransferConfirmed;
        }
        if (_recallConfirmationDialog != null)
        {
            _recallConfirmationDialog.Confirmed -= OnRecallConfirmed;
        }
        if (_filterDialog != null)
        {
            _filterDialog.FilterApplied -= OnFilterApplied;
            _filterDialog.FilterCleared -= OnFilterCleared;
        }
    }

    public void PopulateCompanyList()
    {
        ShowChapterOverview(refresh: false);
        _historicalSoldierId = null;
        _navigator.ResetToChapter();
        RenderCurrentPath();
    }

    public void DisplaySoldier(int soldierId)
    {
        ShowChapterOverview(refresh: false);
        ISoldier soldier = GetSoldier(soldierId);
        if (soldier == null)
        {
            return;
        }

        if (soldier.AssignedSquad == null)
        {
            _historicalSoldierId = soldierId;
            _activeFilter = [];
            RenderCurrentPath();
            return;
        }

        _historicalSoldierId = null;
        Squad squad = soldier.AssignedSquad;
        _activeFilter = [];
        _navigator.OpenSoldier(FindCompanyId(squad), squad.Id, soldier.Id);
        RenderCurrentPath();
    }

    private void OnBrowserItemSelected(object sender, ChapterBrowserItemEvent item)
    {
        // While a filter is active the left menu shows a flat result list; a click just
        // previews the soldier and never drills, so the results stay put.
        // Outside of filtering, a soldier is a leaf: selecting one from a squad roster (or
        // switching between soldiers once drilled in) opens its own detail so the transfer
        // control is available, matching how filter results behave.
        if (_activeFilter.Count == 0 &&
            item.Level == ChapterBrowserLevel.Soldier &&
            (_navigator.Path.Level == ChapterBrowserLevel.Soldier ||
             _navigator.Path.Level == ChapterBrowserLevel.Squad))
        {
            _navigator.DrillInto(item);
        }
        else
        {
            _navigator.Select(item);
        }
        RenderCurrentPath();
    }

    private void OnBrowserItemDrillRequested(object sender, ChapterBrowserItemEvent item)
    {
        // Drilling changes the browse scope, so the current (scope-bound) filter is retired.
        _activeFilter = [];
        _navigator.DrillInto(item);
        RenderCurrentPath();
    }

    private void OnBrowserItemLocationRequested(object sender, ChapterBrowserItemEvent item)
    {
        Squad squad = GetChapter()?.GetAllSquads().FirstOrDefault(candidate => candidate.Id == item.Id);
        if (SquadLocationNavigation.Resolve(squad) is not null)
        {
            SquadLocationRequested?.Invoke(this, squad);
            return;
        }

        // The campaign can change while a dynamically-created row is being clicked. Refreshing
        // removes a now-invalid affordance instead of leaving a dead navigation control visible.
        RenderCurrentPath();
    }

    private void OnDetailLocationRequested(object sender, int squadId)
    {
        Squad squad = GetChapter()?.GetAllSquads().FirstOrDefault(candidate => candidate.Id == squadId);
        if (SquadLocationNavigation.Resolve(squad) is not null)
        {
            SquadLocationRequested?.Invoke(this, squad);
        }
    }

    private void OnBreadcrumbPressed(object sender, ChapterBrowserLevel level)
    {
        _activeFilter = [];
        _navigator.MoveToBreadcrumb(level);
        RenderCurrentPath();
    }

    private void OnFilterButtonPressed(object sender, EventArgs e)
    {
        if (TryGetChapter() == null)
        {
            return;
        }

        List<ISoldier> scope = GetCurrentScopeMembers().ToList();
        _filterDialog.Populate(
            _filterService.GetAvailableRoles(scope),
            _filterService.GetAvailableHonors(scope,
                GameDataSingleton.Instance.GameRulesData.RatingAwardTiers),
            _activeFilter);
        _filterDialog.PopupCentered();
    }

    private void OnFilterApplied(List<SoldierFilterCondition> conditions)
    {
        _activeFilter = conditions ?? [];
        RenderCurrentPath();
    }

    private void OnFilterCleared()
    {
        _activeFilter = [];
        RenderCurrentPath();
    }

    private void OnTransferTargetSelected(object sender, int index)
    {
        if (index < 0 || index >= _transferOptions.Count)
        {
            return;
        }

        if (TryGetCurrentDetailSoldier() is not PlayerSoldier soldier)
        {
            return;
        }

        SoldierTransferOption option = _transferOptions[index];
        GameDataSingleton.Instance.Sector.PlayerForce.Army.PopulateSquadMap();
        if (_transferService.WouldExceedShipCapacity(
                soldier, option, GameDataSingleton.Instance.Sector.PlayerForce.Army.SquadMap))
        {
            string transferTarget = _transferService.FormatBlockedTransferTarget(
                option,
                GameDataSingleton.Instance.Sector.PlayerForce.Army.SquadMap);
            _transferBlockedDialog.DialogText =
                $"{transferTarget} has no room aboard its ship. Free up space before transferring {soldier.Name} there.";
            _transferBlockedDialog.Title = "Transfer Blocked";
            _transferBlockedDialog.PopupCentered();
            return;
        }

        _pendingTransferOption = option;
        _pendingTransferSoldierId = soldier.Id;
        if (SoldierTransferService.RequiresBlackCarapace(soldier, option))
        {
            GameRulesData rules = GameDataSingleton.Instance.GameRulesData;
            if (option.IsNewSquad
                || option.SoldierTemplate != rules.ChapterTemplates.DevastatorMarine)
            {
                _transferBlockedDialog.Title = "Promotion Blocked";
                _transferBlockedDialog.DialogText =
                    "A campaign-recruited neophyte must receive the Black Carapace "
                    + "before changing roles, and his first Battle-Brother posting "
                    + "must be as a Devastator Marine.";
                _transferBlockedDialog.PopupCentered();
                ClearPendingTransfer();
                return;
            }

            BlackCarapacePlanResult plan = CreateRecruitmentPromotionService()
                .EvaluateBlackCarapace(soldier.Id, option.SquadId);
            if (!plan.Succeeded)
            {
                _transferBlockedDialog.Title = "Promotion Blocked";
                _transferBlockedDialog.DialogText = plan.Message;
                _transferBlockedDialog.PopupCentered();
                ClearPendingTransfer();
                return;
            }

            _pendingBlackCarapaceSurgery = true;
            _transferConfirmationDialog.Title = "Confirm Black Carapace Surgery";
            _transferConfirmationDialog.DialogText =
                $"Commit {soldier.Name} to a one-week Black Carapace procedure? "
                + $"{plan.ApothecaryName} will perform the surgery. His genetic "
                + $"compatibility is {plan.GeneticCompatibility:P0}; failure is fatal. "
                + $"If he survives, he will join {option.DisplayName}.";
            _transferConfirmationDialog.PopupCentered();
            return;
        }

        _pendingBlackCarapaceSurgery = false;
        _transferConfirmationDialog.Title = "Confirm Transfer";
        _transferConfirmationDialog.DialogText =
            $"Transfer {soldier.Template.Name} {soldier.Name} to {_pendingTransferOption.DisplayName}?";
        _transferConfirmationDialog.PopupCentered();
    }

    private void OnTransferConfirmed()
    {
        if (_pendingTransferOption == null || !_pendingTransferSoldierId.HasValue)
        {
            return;
        }

        if (GetSoldier(_pendingTransferSoldierId.Value) is not PlayerSoldier soldier)
        {
            ClearPendingTransfer();
            return;
        }
        if (_pendingBlackCarapaceSurgery)
        {
            RecruitmentPromotionResult result = CreateRecruitmentPromotionService()
                .ScheduleBlackCarapace(soldier.Id, _pendingTransferOption.SquadId);
            if (result.Succeeded)
            {
                CampaignChanged?.Invoke(this, EventArgs.Empty);
                _transferBlockedDialog.Title = "Surgery Scheduled";
            }
            else
            {
                _transferBlockedDialog.Title = "Promotion Blocked";
            }
            _transferBlockedDialog.DialogText = result.Message;
            _transferBlockedDialog.PopupCentered();
            ClearPendingTransfer();
            RenderCurrentPath();
            return;
        }

        // Capture the ordered soldier list of the context we're browsing (filter results
        // or squad roster) before the transfer, so we can advance to the next soldier in
        // that same context rather than following this one to its new home.
        List<int> contextSoldierIds = GetCurrentContextSoldierIds();
        int transferIndex = contextSoldierIds.IndexOf(soldier.Id);
        int originSquadId = soldier.AssignedSquad.Id;

        GameDataSingleton.Instance.Sector.PlayerForce.Army.PopulateSquadMap();
        bool didTransfer = _transferService.ApplyTransfer(
            soldier,
            _pendingTransferOption,
            GameDataSingleton.Instance.Sector.PlayerForce.Army.SquadMap,
            GameDataSingleton.Instance.Date);

        if (didTransfer)
        {
            CampaignChanged?.Invoke(this, EventArgs.Empty);
            // Moving the last member out empties (and disbands) the origin squad — e.g. the
            // final scout leaving a scout squad. If the path we're browsing referenced that
            // squad, it's now stale and the next render would crash, so follow the soldier to
            // his new squad instead of advancing within the vanished context.
            bool originSquadRemoved =
                !GetChapter().GetAllSquads().Any(squad => squad.Id == originSquadId);
            if (originSquadRemoved && _navigator.Path.SquadId == originSquadId)
            {
                NavigateToSoldierSquad(soldier);
            }
            else
            {
                SelectNextInContext(contextSoldierIds, transferIndex);
            }
        }

        ClearPendingTransfer();
        RenderCurrentPath();
    }

    private void ClearPendingTransfer()
    {
        _pendingTransferOption = null;
        _pendingTransferSoldierId = null;
        _pendingBlackCarapaceSurgery = false;
        if (_transferConfirmationDialog != null)
        {
            _transferConfirmationDialog.Title = "Confirm Transfer";
        }
    }

    private void OnChapterLoadoutsPressed(object sender, EventArgs e)
    {
        PlayerForce force = GameDataSingleton.Instance?.Sector?.PlayerForce;
        if (force != null)
        {
            _loadoutDoctrineDialog.OpenChapter(force);
        }
    }

    private static RecruitmentPromotionService CreateRecruitmentPromotionService()
    {
        GameDataSingleton data = GameDataSingleton.Instance;
        return new RecruitmentPromotionService(new GameSession(
            data.GameRulesData,
            data.Sector,
            data.Date,
            StaticRNG.Instance));
    }

    private void RenderCurrentPath()
    {
        if (_historicalSoldierId.HasValue
            && GameDataSingleton.Instance?.Sector?.PlayerForce?.Army?.FallenBrothers
                .TryGetValue(_historicalSoldierId.Value, out PlayerSoldier fallen) == true)
        {
            RenderHistoricalSoldier(fallen);
            return;
        }

        Unit chapter = TryGetChapter();
        if (chapter == null)
        {
            RenderNoChapterData();
            return;
        }

        ChapterView.SetBreadcrumbs(BuildBreadcrumbs(chapter));
        ChapterView.SetFilterActive(_activeFilter.Count);
        _currentDetailSoldierId = null;
        _transferOptions = [];

        if (_activeFilter.Count > 0)
        {
            RenderFilterResults();
            return;
        }

        switch (_navigator.Path.Level)
        {
            case ChapterBrowserLevel.Chapter:
                RenderChapterLevel(chapter);
                break;
            case ChapterBrowserLevel.Company:
                RenderCompanyLevel(GetCompany(_navigator.Path.CompanyId.Value));
                break;
            case ChapterBrowserLevel.Squad:
                RenderSquadLevel(GetSquad(_navigator.Path.SquadId.Value));
                break;
            case ChapterBrowserLevel.Soldier:
                RenderSoldierLevel(GetSoldier(_navigator.Path.SoldierId.Value));
                break;
        }
    }

    private void RenderHistoricalSoldier(PlayerSoldier soldier)
    {
        _currentDetailSoldierId = soldier.Id;
        _transferOptions = [];
        List<ChapterBrowserMenuItem> fallenBrothers = GameDataSingleton.Instance.Sector.PlayerForce.Army
            .FallenBrothers.Values
            .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Id)
            .Select(candidate => new ChapterBrowserMenuItem(
                ChapterBrowserLevel.Soldier,
                candidate.Id,
                GetSoldierIconKey(candidate),
                $"{candidate.Template.Name} {candidate.Name}",
                "Fallen — preserved dossier",
                true,
                candidate.Id == soldier.Id,
                "i"))
            .ToList();
        ChapterView.SetLeftMenu("Fallen Brothers", fallenBrothers);
        ChapterBrowserDetail baseDetail = _soldierDetailBuilder.Build(
            soldier,
            false,
            includeSquadInTitle: true);
        ChapterBrowserDetail detail = baseDetail with
        {
            Subtitle = "Fallen — preserved dossier",
            Cards =
            [
                new ChapterBrowserDetailCard(
                    "archive",
                    "Historical Dossier",
                    "No active posting",
                    "This brother is no longer part of the active order of battle. His name, service record, and campaign history remain preserved here."),
                .. baseDetail.Cards
            ]
        };
        ChapterView.SetDetail(detail);
        ChapterView.SetTransferOptions([]);
    }

    private void RenderNoChapterData()
    {
        _currentDetailSoldierId = null;
        _transferOptions = [];
        ChapterView.SetBreadcrumbs(
        [
            new ChapterBreadcrumbItem(ChapterBrowserLevel.Chapter, "Chapter", "chapter")
        ]);
        ChapterView.SetFilterActive(_activeFilter.Count);
        ChapterView.SetLeftMenu("Companies", []);
        ChapterView.SetDetail(new ChapterBrowserDetail(
            "chapter",
            "No Chapter Data",
            "Chapter data will appear here once a game is loaded.",
            [
                new ChapterBrowserMetric("0", "Soldiers"),
                new ChapterBrowserMetric("0", "Squads"),
                new ChapterBrowserMetric("0", "Wounded")
            ],
            [
                new ChapterBrowserDetailCard("archive", "Awaiting Game Data", "No active chapter", "Open this screen through the main game flow to browse companies, squads, and soldiers.")
            ]));
    }

    private void RenderChapterLevel(Unit chapter)
    {
        Unit selectedCompany = TryGetSelectedCompany();
        Squad selectedSquad = TryGetSelectedSquad();
        List<Squad> orderedSquads = OrderSquads(chapter.Squads).ToList();
        if (selectedCompany == null && selectedSquad == null)
        {
            selectedSquad = orderedSquads.FirstOrDefault();
            selectedCompany = selectedSquad == null ? chapter.ChildUnits.FirstOrDefault() : null;
        }

        List<ChapterBrowserMenuItem> chapterItems = orderedSquads
            .Select(squad => new ChapterBrowserMenuItem(
                ChapterBrowserLevel.Squad,
                squad.Id,
                GetSquadIconKey(squad),
                squad.Name,
                $"{squad.SquadTemplate.Name} - {squad.Members.Count} soldiers",
                true,
                selectedSquad?.Id == squad.Id,
                ">",
                CanNavigate: SquadLocationNavigation.Resolve(squad) is not null))
            .ToList();

        chapterItems.AddRange(chapter.ChildUnits
            .Select(company => new ChapterBrowserMenuItem(
                ChapterBrowserLevel.Company,
                company.Id,
                GetCompanyIconKey(company),
                company.Name,
                $"{FormatCompanySquadCount(company)} squads - {company.GetAllMembers().Count()} soldiers",
                true,
                selectedCompany?.Id == company.Id,
                ">"))
            .ToList());

        ChapterView.SetLeftMenu("Chapter Command", chapterItems);

        ChapterView.SetDetail(BuildChapterDetail(chapter, selectedCompany, selectedSquad));
    }

    private void RenderCompanyLevel(Unit company)
    {
        List<Squad> orderedSquads = OrderSquads(company.Squads).ToList();
        Squad selectedSquad = TryGetSelectedSquad() ?? orderedSquads.FirstOrDefault();

        List<ChapterBrowserMenuItem> squads = orderedSquads
            .Select(squad => new ChapterBrowserMenuItem(
                ChapterBrowserLevel.Squad,
                squad.Id,
                GetSquadIconKey(squad),
                squad.Name,
                $"{squad.SquadTemplate.Name} - {squad.Members.Count} soldiers",
                true,
                selectedSquad?.Id == squad.Id,
                Location: SquadLocationFormatter.Format(squad),
                CanNavigate: SquadLocationNavigation.Resolve(squad) is not null))
            .ToList();

        ChapterView.SetLeftMenu($"{company.Name} Squads", squads);

        ChapterView.SetDetail(BuildCompanyDetail(company, selectedSquad));
    }

    internal static IEnumerable<Squad> OrderSquads(IEnumerable<Squad> squads)
    {
        return squads
            .OrderBy(FleetScreenController.GetSquadTypeOrder)
            .ThenBy(squad => squad.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(squad => squad.Id);
    }

    private void RenderSquadLevel(Squad squad)
    {
        List<ISoldier> orderedMembers =
            OrderByRankAndTenure(squad.Members).ToList();
        ISoldier selectedSoldier = TryGetSelectedSoldier() ?? orderedMembers.FirstOrDefault();

        List<ChapterBrowserMenuItem> soldiers = orderedMembers
            .Select(soldier => new ChapterBrowserMenuItem(
                ChapterBrowserLevel.Soldier,
                soldier.Id,
                GetSoldierIconKey(soldier),
                $"{soldier.Template.Name} {soldier.Name}",
                soldier.IsCombatEffective ? "Available" : "Wounded or impaired",
                true,
                selectedSoldier?.Id == soldier.Id,
                "i"))
            .ToList();

        ChapterView.SetLeftMenu("Battle Brothers", soldiers);

        // A soldier is a leaf, so entering a squad shows the auto-selected top member's own
        // detail (with the transfer control) rather than a squad overview, keeping squad entry
        // consistent with clicking any other member. Empty squads fall back to the overview.
        if (selectedSoldier != null)
        {
            SetSoldierDetail(selectedSoldier);
        }
        else
        {
            ChapterView.SetDetail(BuildSquadDetail(squad, null));
        }
    }

    private void RenderSoldierLevel(ISoldier soldier)
    {
        Squad squad = soldier.AssignedSquad;
        List<ChapterBrowserMenuItem> soldiers =
            OrderByRankAndTenure(squad.Members)
            .Select(squadMember => new ChapterBrowserMenuItem(
                ChapterBrowserLevel.Soldier,
                squadMember.Id,
                GetSoldierIconKey(squadMember),
                $"{squadMember.Template.Name} {squadMember.Name}",
                squadMember.IsCombatEffective ? "Available" : "Wounded or impaired",
                true,
                squadMember.Id == soldier.Id,
                "i"))
            .ToList();

        ChapterView.SetLeftMenu("Battle Brothers", soldiers);
        SetSoldierDetail(soldier);
    }

    private void SetSoldierDetail(ISoldier soldier)
    {
        _currentDetailSoldierId = soldier.Id;
        ChapterBrowserDetail detail =
            _soldierDetailBuilder.Build(soldier, false, includeSquadInTitle: true);

        // A brother assigned to an operation is in the field with someone else's force. The
        // field with someone else's force. Surface that, offer the recall, and withhold the
        // transfer options - SoldierTransferService.ApplyTransfer refuses him anyway (§3.4),
        // so offering them would only produce a silent no-op.
        PlayerSoldier attached = soldier as PlayerSoldier;
        if (attached?.CurrentOrder != null)
        {
            string where = attached.CurrentOrder.Mission?.RegionFaction?.Region?.Name
                ?? "an ongoing operation";
            detail = detail with
            {
                Cards =
                [
                    new ChapterBrowserDetailCard(
                        "target",
                        "Assigned to Operation",
                        where,
                        $"{soldier.Name} is away from {attached.AssignedSquad?.Name} and "
                        + $"serving with the force committed to {where}. He returns when the "
                        + "operation ends, or on recall. Transfers are unavailable while he is "
                        + "in the field."),
                    .. detail.Cards
                ],
                PrimaryActionText = "Recall from operation",
                PrimaryActionIconKey = "archive"
            };
            ChapterView.SetDetail(detail);
            _transferOptions = [];
            ChapterView.SetTransferOptions([]);
            return;
        }

        ChapterView.SetDetail(detail);
        if (soldier is PlayerSoldier playerSoldier)
        {
            _transferOptions = _transferService.GetTransferOptions(
                GameDataSingleton.Instance.Sector.PlayerForce.Army.OrderOfBattle,
                playerSoldier);
            ChapterView.SetTransferOptions(_transferOptions.Select(option => option.DisplayName).ToList());
        }
        else
        {
            _transferOptions = [];
            ChapterView.SetTransferOptions([]);
        }
    }

    // "Recall from operation" on an assigned brother's detail card. Routed through the same
    // confirmation dialog transfers use, so the two destructive-ish actions read alike.
    private void OnDetailPrimaryActionPressed(object sender, EventArgs e)
    {
        if (!_currentDetailSoldierId.HasValue
            || GetSoldier(_currentDetailSoldierId.Value) is not PlayerSoldier soldier
            || soldier.CurrentOrder == null)
        {
            return;
        }
        _pendingRecallSoldierId = soldier.Id;
        _recallConfirmationDialog.DialogText =
            $"Recall {soldier.Template.Name} {soldier.Name} from the operation in "
            + $"{soldier.CurrentOrder.Mission?.RegionFaction?.Region?.Name ?? "the field"}? "
            + $"He rejoins {soldier.AssignedSquad?.Name} immediately.";
        _recallConfirmationDialog.PopupCentered();
    }

    private void OnRecallConfirmed()
    {
        if (!_pendingRecallSoldierId.HasValue) return;
        int soldierId = _pendingRecallSoldierId.Value;
        _pendingRecallSoldierId = null;
        if (GetSoldier(soldierId) is not PlayerSoldier soldier) return;

        OnlyWar.Helpers.Orders.OrderForceService.RemoveCharacter(soldier);
        CampaignChanged?.Invoke(this, EventArgs.Empty);
        RenderCurrentPath();
    }

    // Renders the active filter as a flat, drillable soldier list scoped to the current
    // browse level. Selecting a result previews it; drilling (or navigating) exits filtering.
    private void RenderFilterResults()
    {
        List<ISoldier> results = OrderFilteredSoldiers(_filterService
                .Apply(GetCurrentScopeMembers(), _activeFilter, GameDataSingleton.Instance.Date))
            .ToList();

        int? selectedId = _navigator.SelectedItem?.Level == ChapterBrowserLevel.Soldier
            ? _navigator.SelectedItem.Id
            : null;
        ISoldier selected = results.FirstOrDefault(soldier => soldier.Id == selectedId)
            ?? results.FirstOrDefault();

        List<ChapterBrowserMenuItem> items = results
            .Select(soldier => new ChapterBrowserMenuItem(
                ChapterBrowserLevel.Soldier,
                soldier.Id,
                GetSoldierIconKey(soldier),
                $"{soldier.Template.Name} {soldier.Name}",
                soldier.IsCombatEffective ? "Available" : "Wounded or impaired",
                true,
                selected?.Id == soldier.Id,
                "i"))
            .ToList();

        ChapterView.SetLeftMenu($"Filter Results ({results.Count})", items);

        if (selected == null)
        {
            ChapterView.SetDetail(new ChapterBrowserDetail(
                "archive",
                "No Matches",
                "No battle brothers at this level match the current filter.",
                [],
                [
                    new ChapterBrowserDetailCard("archive", "Adjust Filter", "No results",
                        "Reopen the Filter button to change the conditions, or Clear to resume browsing.")
                ]));
        }
        else
        {
            SetSoldierDetail(selected);
        }
    }

    // Filter results span squads and companies, so tenure is less useful than a predictable
    // alphabetical tie-break. Keep role seniority first, then alphabetize equal-ranked brothers
    // by surname regardless of the order in which their squads appear in the chapter.
    internal static IEnumerable<ISoldier> OrderFilteredSoldiers(IEnumerable<ISoldier> soldiers)
    {
        return soldiers
            .OrderByDescending(soldier => soldier.Template.Rank)
            .ThenByDescending(soldier => soldier.Template.Subrank)
            .ThenBy(GetSurname, StringComparer.OrdinalIgnoreCase)
            .ThenBy(soldier => soldier.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(soldier => soldier.Id);
    }

    private static string GetSurname(ISoldier soldier)
    {
        string name = soldier?.Name?.Trim() ?? "";
        int separatorIndex = name.LastIndexOf(' ');
        return separatorIndex >= 0 ? name[(separatorIndex + 1)..] : name;
    }

    // Soldiers the filter searches, bound to the current breadcrumb level. Presented in the
    // same seniority order the rosters use before the filtered result list applies its own
    // rank/subrank/surname presentation order.
    private IEnumerable<ISoldier> GetCurrentScopeMembers()
    {
        IEnumerable<ISoldier> members = _navigator.Path.Level switch
        {
            ChapterBrowserLevel.Company => GetCompany(_navigator.Path.CompanyId.Value).GetAllMembers(),
            ChapterBrowserLevel.Squad => GetSquad(_navigator.Path.SquadId.Value).Members,
            ChapterBrowserLevel.Soldier => GetSoldier(_navigator.Path.SoldierId.Value).AssignedSquad.Members,
            _ => GetChapter().GetAllMembers()
        };
        return OrderByRankAndTenure(members);
    }

    // Rosters and filter results are presented rank-first (most senior at the top), then by
    // time in rank so the longest-tenured brother within a rank leads. Subrank breaks the tie
    // for roles that share a Rank (e.g. a Veteran Sergeant outranks a Veteran at Rank 5), so a
    // squad leader always sorts above his brothers. SoldierSeniority holds the ordering itself,
    // shared with mission command resolution so the roster and the field agree on who is senior.
    private static IEnumerable<ISoldier> OrderByRankAndTenure(IEnumerable<ISoldier> soldiers)
    {
        return SoldierSeniority.OrderBySeniority(soldiers);
    }

    // The ordered soldier ids currently shown in the left menu, so a transfer can pick the
    // next one in place. Only soldier-bearing contexts (filter results or a squad roster)
    // return ids; chapter/company overviews have no soldier list to advance through.
    private List<int> GetCurrentContextSoldierIds()
    {
        if (_activeFilter.Count > 0)
        {
            return OrderFilteredSoldiers(_filterService
                    .Apply(GetCurrentScopeMembers(), _activeFilter, GameDataSingleton.Instance.Date))
                .Select(soldier => soldier.Id)
                .ToList();
        }

        switch (_navigator.Path.Level)
        {
            case ChapterBrowserLevel.Squad:
                return OrderByRankAndTenure(GetSquad(_navigator.Path.SquadId.Value).Members)
                    .Select(soldier => soldier.Id).ToList();
            case ChapterBrowserLevel.Soldier:
                return OrderByRankAndTenure(
                        GetSoldier(_navigator.Path.SoldierId.Value).AssignedSquad.Members)
                    .Select(soldier => soldier.Id).ToList();
            default:
                return [];
        }
    }

    // After a transfer, select the soldier that follows the transferred one in the pre-transfer
    // context list (or the previous one if it was last), keeping the current browse scope put.
    private void SelectNextInContext(List<int> contextSoldierIds, int transferIndex)
    {
        int? nextId = null;
        if (transferIndex >= 0)
        {
            if (transferIndex + 1 < contextSoldierIds.Count)
            {
                nextId = contextSoldierIds[transferIndex + 1];
            }
            else if (transferIndex - 1 >= 0)
            {
                nextId = contextSoldierIds[transferIndex - 1];
            }
        }

        // At soldier-level browsing the detail is driven by the path; retarget it (or drop back
        // to the squad when nothing remains) so we don't render the transferred soldier's new home.
        if (_activeFilter.Count == 0 && _navigator.Path.Level == ChapterBrowserLevel.Soldier)
        {
            _navigator.Path.SoldierId = nextId;
        }

        _navigator.Select(nextId.HasValue
            ? new ChapterBrowserItemEvent(ChapterBrowserLevel.Soldier, nextId.Value)
            : null);
    }

    // Points the browser at the squad the soldier now belongs to, selecting the soldier itself.
    // Used after a transfer disbands the squad we were viewing, so the stale path can't crash the
    // next render and the player follows the soldier to his new home. Any active filter is cleared
    // since its scope (the disbanded squad) no longer exists.
    private void NavigateToSoldierSquad(ISoldier soldier)
    {
        Squad newSquad = soldier.AssignedSquad;
        _activeFilter = [];
        _navigator.OpenSoldier(FindCompanyId(newSquad), newSquad.Id, soldier.Id);
    }

    // The breadcrumb path models companies as direct children of the chapter, so map a squad back
    // to the company that owns it (a company may nest the squad in a sub-unit, hence GetAllSquads).
    // Returns null for a chapter-level command squad that hangs directly off the order of battle.
    private int? FindCompanyId(Squad squad)
    {
        if (squad == null)
        {
            return null;
        }
        Unit chapter = GetChapter();
        if (chapter.Squads.Contains(squad))
        {
            return null;
        }
        foreach (Unit company in chapter.ChildUnits)
        {
            if (company.GetAllSquads().Contains(squad))
            {
                return company.Id;
            }
        }
        return null;
    }

    private IReadOnlyList<ChapterBreadcrumbItem> BuildBreadcrumbs(Unit chapter)
    {
        List<ChapterBreadcrumbItem> breadcrumbs =
        [
            new ChapterBreadcrumbItem(ChapterBrowserLevel.Chapter, "Chapter", "chapter")
        ];

        if (_navigator.Path.CompanyId.HasValue)
        {
            Unit company = GetCompany(_navigator.Path.CompanyId.Value);
            breadcrumbs.Add(new ChapterBreadcrumbItem(ChapterBrowserLevel.Company, company.Name, GetCompanyIconKey(company)));
        }

        if (_navigator.Path.SquadId.HasValue)
        {
            Squad squad = GetSquad(_navigator.Path.SquadId.Value);
            breadcrumbs.Add(new ChapterBreadcrumbItem(ChapterBrowserLevel.Squad, squad.Name, GetSquadIconKey(squad)));
        }

        if (_navigator.Path.SoldierId.HasValue)
        {
            ISoldier soldier = GetSoldier(_navigator.Path.SoldierId.Value);
            breadcrumbs.Add(new ChapterBreadcrumbItem(ChapterBrowserLevel.Soldier, soldier.Name, GetSoldierIconKey(soldier)));
        }

        return breadcrumbs;
    }

    private ChapterBrowserDetail BuildChapterDetail(Unit chapter, Unit selectedCompany, Squad selectedSquad)
    {
        int soldierCount = chapter.GetAllMembers().Count();
        int squadCount = chapter.GetAllSquads().Count();
        int woundedCount = chapter.GetAllMembers().Count(soldier => !soldier.IsCombatEffective);

        // Scouts are neophytes, not yet full battle brothers, so report them separately
        // from the battle-brother line. Their sergeants are full marines leading them.
        List<Squad> scoutSquads = chapter.GetAllSquads()
            .Where(squad => (squad.SquadTemplate.SquadType & SquadTypes.Scout) > 0)
            .ToList();
        int neophyteCount = scoutSquads.Sum(squad => squad.Members.Count(m => !m.Template.IsSquadLeader));
        int scoutSergeantCount = scoutSquads.Count(squad => squad.SquadLeader != null);
        int battleBrotherCount = soldierCount - neophyteCount - scoutSergeantCount;
        int battleBrotherSquadCount = squadCount - scoutSquads.Count;

        string strengthText = $"{battleBrotherCount} battle brothers across {battleBrotherSquadCount} squads";
        strengthText += scoutSergeantCount > 0
            ? $", and {scoutSergeantCount} Scout Sergeants training {neophyteCount} Neophytes."
            : ".";

        List<ChapterBrowserDetailCard> cards =
        [
            new ChapterBrowserDetailCard("chapter", "Chapter Strength", chapter.Name, strengthText),
            new ChapterBrowserDetailCard("medical", "Recovery", "Apothecarium demand", $"{woundedCount} soldiers are wounded or impaired.")
        ];

        if (selectedCompany != null)
        {
            cards.Insert(0, new ChapterBrowserDetailCard(
                GetCompanyIconKey(selectedCompany),
                $"Selected: {selectedCompany.Name}",
                selectedCompany.UnitTemplate.Name,
                $"{FormatCompanySquadCount(selectedCompany)} squads, {selectedCompany.GetAllMembers().Count()} soldiers."));
        }

        if (selectedSquad != null)
        {
            cards.Insert(0, new ChapterBrowserDetailCard(
                GetSquadIconKey(selectedSquad),
                $"Selected: {selectedSquad.Name}",
                selectedSquad.SquadTemplate.Name,
                $"{selectedSquad.Members.Count} soldiers. Drill in to inspect individual battle brothers."));
        }

        return new ChapterBrowserDetail(
            "chapter",
            chapter.Name,
            "Chapter-level overview. Select command squads or companies for a preview; drill into either to manage their roster.",
            [
                new ChapterBrowserMetric(soldierCount.ToString(), "Soldiers"),
                new ChapterBrowserMetric(squadCount.ToString(), "Squads"),
                new ChapterBrowserMetric(woundedCount.ToString(), "Wounded")
            ],
            cards);
    }

    private ChapterBrowserDetail BuildCompanyDetail(Unit company, Squad selectedSquad)
    {
        int soldierCount = company.GetAllMembers().Count();
        int woundedCount = company.GetAllMembers().Count(soldier => !soldier.IsCombatEffective);

        List<ChapterBrowserDetailCard> cards =
        [
            new ChapterBrowserDetailCard(GetCompanyIconKey(company), "Company Strength", company.UnitTemplate.Name, $"{soldierCount} soldiers across {FormatCompanySquadCount(company)} squads."),
            new ChapterBrowserDetailCard("medical", "Company Recovery", "Readiness impact", $"{woundedCount} soldiers are wounded or impaired."),
            new ChapterBrowserDetailCard("archive", "Company Record", "Chronicle", "Company history and honors can live here as the detail renderer grows.")
        ];

        if (selectedSquad != null)
        {
            cards.Insert(0, new ChapterBrowserDetailCard(
                GetSquadIconKey(selectedSquad),
                $"Selected: {selectedSquad.Name}",
                selectedSquad.SquadTemplate.Name,
                $"{selectedSquad.Members.Count} soldiers. Drill in to inspect individual battle brothers."));
        }

        return new ChapterBrowserDetail(
            GetCompanyIconKey(company),
            company.Name,
            "Company-level overview. Select a squad for a preview; drill into it to manage soldiers.",
            [
                new ChapterBrowserMetric(soldierCount.ToString(), "Soldiers"),
                new ChapterBrowserMetric(FormatCompanySquadCount(company), "Squads"),
                new ChapterBrowserMetric(woundedCount.ToString(), "Wounded")
            ],
            cards);
    }

    private ChapterBrowserDetail BuildSquadDetail(Squad squad, ISoldier selectedSoldier)
    {
        int woundedCount = squad.Members.Count(soldier => !soldier.IsCombatEffective);
        // Headcount stays whole (attachment never touches Squad.Members); this is the
        // "available right now" counterpart the roster needs.
        int assignedCount = squad.Members
            .OfType<PlayerSoldier>()
            .Count(soldier => soldier.CurrentOrder != null);
        string assignedNote = assignedCount == 0
            ? ""
            : $" {assignedCount} assigned to operations elsewhere.";

        List<ChapterBrowserDetailCard> cards =
        [
            new ChapterBrowserDetailCard(GetSquadIconKey(squad), "Squad Composition", squad.SquadTemplate.Name, $"{squad.Members.Count} battle brothers assigned.{assignedNote}"),
            new ChapterBrowserDetailCard("medical", "Casualties", "Current condition", $"{woundedCount} soldiers are wounded or impaired."),
            new ChapterBrowserDetailCard("archive", "Squad Record", "Chronicle", "Squad history, honors, and mission record can expand here.")
        ];

        if (selectedSoldier != null)
        {
            cards.Insert(0, new ChapterBrowserDetailCard(
                GetSoldierIconKey(selectedSoldier),
                $"Selected: {selectedSoldier.Template.Name} {selectedSoldier.Name}",
                selectedSoldier.IsCombatEffective ? "Available" : "Wounded or impaired",
                "Select a soldier for preview; use the detail button to open the existing soldier display flow."));
        }

        return new ChapterBrowserDetail(
            GetSquadIconKey(squad),
            squad.Name,
            $"{SquadLocationFormatter.Format(squad)}. Select individual soldiers to inspect their status.",
            [
                new ChapterBrowserMetric(squad.Members.Count.ToString(), "Soldiers"),
                new ChapterBrowserMetric(woundedCount.ToString(), "Wounded"),
                new ChapterBrowserMetric(squad.SquadTemplate.BattleValue.ToString(), "Battle Value")
            ],
            cards);
    }

    private Unit TryGetSelectedCompany()
    {
        if (_navigator.SelectedItem == null || _navigator.SelectedItem.Level != ChapterBrowserLevel.Company)
        {
            return null;
        }
        return GetChapter().ChildUnits.FirstOrDefault(company => company.Id == _navigator.SelectedItem.Id);
    }

    private Squad TryGetSelectedSquad()
    {
        if (_navigator.SelectedItem == null || _navigator.SelectedItem.Level != ChapterBrowserLevel.Squad)
        {
            return null;
        }
        return GetChapter().GetAllSquads().FirstOrDefault(squad => squad.Id == _navigator.SelectedItem.Id);
    }

    private ISoldier TryGetSelectedSoldier()
    {
        if (_navigator.SelectedItem == null || _navigator.SelectedItem.Level != ChapterBrowserLevel.Soldier)
        {
            return null;
        }
        return GetChapter().GetAllMembers().FirstOrDefault(soldier => soldier.Id == _navigator.SelectedItem.Id);
    }

    private ISoldier TryGetCurrentDetailSoldier()
    {
        if (_currentDetailSoldierId.HasValue)
        {
            return GetSoldier(_currentDetailSoldierId.Value);
        }
        if (_navigator.Path.SoldierId.HasValue)
        {
            return GetSoldier(_navigator.Path.SoldierId.Value);
        }

        return TryGetSelectedSoldier();
    }

    private Unit GetChapter()
    {
        return TryGetChapter();
    }

    private Unit TryGetChapter()
    {
        return GameDataSingleton.Instance?.Sector?.PlayerForce?.Army?.OrderOfBattle;
    }

    private Unit GetCompany(int companyId)
    {
        return GetChapter().ChildUnits.First(company => company.Id == companyId);
    }

    private Squad GetSquad(int squadId)
    {
        return GetChapter().GetAllSquads().First(squad => squad.Id == squadId);
    }

    private ISoldier GetSoldier(int soldierId)
    {
        return GetChapter().GetAllMembers().FirstOrDefault(soldier => soldier.Id == soldierId)
            ?? GameDataSingleton.Instance?.Sector?.PlayerForce?.Army?.FallenBrothers
                .GetValueOrDefault(soldierId);
    }

    // Companies always have a single HQ squad plus a variable number of line
    // squads; surface that as "HQ + N" so the HQ isn't conflated with line strength.
    private static string FormatCompanySquadCount(Unit company)
    {
        int nonHqSquads = company.Squads.Count(
            squad => (squad.SquadTemplate.SquadType & SquadTypes.HQ) == 0);
        bool hasHqSquad = company.Squads.Any(
            squad => (squad.SquadTemplate.SquadType & SquadTypes.HQ) != 0);
        return hasHqSquad ? $"HQ + {nonHqSquads}" : nonHqSquads.ToString();
    }

    private static string GetCompanyIconKey(Unit company)
    {
        return company.UnitTemplate.Name switch
        {
            "Veteran Company" => "elite",
            "Battle Company" => "default",
            "Tactical Company" => "default",
            "Assault Company" => "fast",
            "Devastator Company" => "heavy",
            "Scout Company" => "scout",
            _ => "chapter"
        };
    }

    private static string GetSquadIconKey(Squad squad)
    {
        SquadTypes type = squad.SquadTemplate.SquadType;
        if ((type & SquadTypes.HQ) > 0)
        {
            return "chapter";
        }
        if ((type & SquadTypes.Elite) > 0)
        {
            return "elite";
        }
        if ((type & SquadTypes.Fast) > 0)
        {
            return "fast";
        }
        if ((type & SquadTypes.Heavy) > 0)
        {
            return "heavy";
        }
        if ((type & SquadTypes.Scout) > 0)
        {
            return "scout";
        }

        return "default";
    }

    private static string GetSoldierIconKey(ISoldier soldier) => SoldierDetailBuilder.GetSoldierIconKey(soldier);
}
