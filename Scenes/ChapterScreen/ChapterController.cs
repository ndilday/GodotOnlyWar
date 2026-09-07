using Godot;
using OnlyWar.Application;
using OnlyWar.Helpers;
using OnlyWar.Helpers.UI;
using System;
using System.Collections.Generic;

public partial class ChapterController : MainScreenController
{
    private readonly ChapterBrowserNavigator _navigator = new();
    private IChapterScreenApplication _application;
    private ILoadoutScreenApplication _loadoutApplication;
    private List<SoldierFilterCondition> _activeFilter = [];
    private IReadOnlyList<string> _transferOptions = [];
    private IReadOnlyList<int> _contextSoldierIds = [];
    private int? _pendingTransferOptionIndex;
    private int? _pendingTransferSoldierId;
    private int? _currentDetailSoldierId;
    private int? _currentDetailSquadId;
    private int? _historicalSoldierId;
    private ConfirmationDialog _transferConfirmationDialog;
    private ConfirmationDialog _recallConfirmationDialog;
    private int? _pendingRecallSoldierId;
    private AcceptDialog _transferBlockedDialog;
    private ChapterFilterDialog _filterDialog;
    private LoadoutDoctrineDialog _loadoutDoctrineDialog;
    private ChapterMusterScreenController _musterScreen;

    public ChapterView ChapterView { get; set; }

    public event EventHandler CampaignChanged;
    public event EventHandler<int> SquadLocationRequested;
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
        _loadoutDoctrineDialog.Configure(_loadoutApplication);
        _loadoutDoctrineDialog.DoctrineChanged += (_, _) => CampaignChanged?.Invoke(this, EventArgs.Empty);
        AddChild(_loadoutDoctrineDialog);

        RenderCurrentPath();
    }

    public void Configure(
        IChapterScreenApplication application, ILoadoutScreenApplication loadoutApplication)
    {
        if (_application != null)
        {
            _application.SessionChanged -= OnSessionChanged;
        }

        _application = application;
        _loadoutApplication = loadoutApplication;
        _loadoutDoctrineDialog?.Configure(loadoutApplication);
        _musterScreen?.Configure(application as IMusterScreenApplication);
        if (_application != null)
        {
            _application.SessionChanged += OnSessionChanged;
        }
        RenderCurrentPath();
    }

    private void OnSessionChanged(object sender, EventArgs e)
    {
        // Every id the browser is holding belongs to the replaced campaign.
        _navigator.ResetToChapter();
        _activeFilter = [];
        _historicalSoldierId = null;
        ClearPendingTransfer();
        _pendingRecallSoldierId = null;
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
        _musterScreen.Configure(_application as IMusterScreenApplication);
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
        if (_application != null)
        {
            _application.SessionChanged -= OnSessionChanged;
        }
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
        if (_application == null) return;

        _activeFilter = [];
        // A soldier the order of battle no longer holds is a fallen brother: his preserved
        // dossier is its own browse mode, so ask the application where he lives before pathing.
        int? squadId = _application
            .QueryChapterBrowser(new ChapterBrowserQuery(
                null, null, soldierId, null, null, [], null))
            .DetailSoldierSquadId;
        if (squadId == null)
        {
            _historicalSoldierId = soldierId;
            RenderCurrentPath();
            return;
        }

        _historicalSoldierId = null;
        _navigator.OpenSoldier(
            _application.FindCompanyForSquad(squadId.Value), squadId.Value, soldierId);
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
        if (_application?.CanNavigateToSquad(item.Id) == true)
        {
            SquadLocationRequested?.Invoke(this, item.Id);
            return;
        }

        // The campaign can change while a dynamically-created row is being clicked. Refreshing
        // removes a now-invalid affordance instead of leaving a dead navigation control visible.
        RenderCurrentPath();
    }

    private void OnDetailLocationRequested(object sender, int squadId)
    {
        if (_application?.CanNavigateToSquad(squadId) == true)
        {
            SquadLocationRequested?.Invoke(this, squadId);
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
        if (_application?.HasChapter != true)
        {
            return;
        }

        ChapterFilterOptions options = _application.QueryFilterOptions(BuildQuery());
        _filterDialog.Populate(options.Roles, options.Honors, _activeFilter);
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
        if (_application == null
            || index < 0 || index >= _transferOptions.Count
            || !_currentDetailSoldierId.HasValue)
        {
            return;
        }

        ChapterPrompt prompt = _application.DescribeTransfer(_currentDetailSoldierId.Value, index);
        if (prompt.Kind == ChapterPromptKind.None) return;
        if (prompt.Kind == ChapterPromptKind.Blocked)
        {
            ShowAcknowledgement(prompt);
            ClearPendingTransfer();
            return;
        }

        _pendingTransferOptionIndex = index;
        _pendingTransferSoldierId = _currentDetailSoldierId;
        _transferConfirmationDialog.Title = prompt.Title;
        _transferConfirmationDialog.DialogText = prompt.Message;
        _transferConfirmationDialog.PopupCentered();
    }

    private void OnTransferConfirmed()
    {
        if (_application == null
            || !_pendingTransferOptionIndex.HasValue
            || !_pendingTransferSoldierId.HasValue)
        {
            return;
        }

        // Capture the ordered soldier list of the context we're browsing (filter results or
        // squad roster) before the transfer, so we can advance to the next soldier in that same
        // context rather than following this one to its new home.
        IReadOnlyList<int> contextSoldierIds = _contextSoldierIds;
        int soldierId = _pendingTransferSoldierId.Value;
        int transferIndex = IndexOf(contextSoldierIds, soldierId);
        int originSquadId = _currentDetailSquadId ?? 0;

        ChapterTransferResult result = _application.ConfirmTransfer(
            _application.SessionToken,
            soldierId,
            _pendingTransferOptionIndex.Value,
            contextSoldierIds);
        ClearPendingTransfer();

        if (result.Acknowledgement.Kind != ChapterPromptKind.None)
        {
            ShowAcknowledgement(result.Acknowledgement);
        }
        if (result.Succeeded)
        {
            CampaignChanged?.Invoke(this, EventArgs.Empty);
        }

        if (result.DidTransfer)
        {
            if (result.OriginSquadRemoved
                && _navigator.Path.SquadId == originSquadId
                && result.NewSquadId.HasValue)
            {
                // The squad we were browsing no longer exists; follow the soldier to his new
                // home rather than advancing within a vanished context.
                _activeFilter = [];
                _navigator.OpenSoldier(
                    result.NewCompanyId, result.NewSquadId.Value, soldierId);
            }
            else
            {
                SelectNextInContext(contextSoldierIds, transferIndex);
            }
        }

        RenderCurrentPath();
    }

    private void ClearPendingTransfer()
    {
        _pendingTransferOptionIndex = null;
        _pendingTransferSoldierId = null;
        if (_transferConfirmationDialog != null)
        {
            _transferConfirmationDialog.Title = "Confirm Transfer";
        }
    }

    private void ShowAcknowledgement(ChapterPrompt prompt)
    {
        _transferBlockedDialog.Title = prompt.Title;
        _transferBlockedDialog.DialogText = prompt.Message;
        _transferBlockedDialog.PopupCentered();
    }

    private void OnChapterLoadoutsPressed(object sender, EventArgs e)
    {
        _loadoutDoctrineDialog.OpenChapter();
    }

    // "Recall from operation" on an assigned brother's detail card. Routed through the same
    // confirmation dialog transfers use, so the two destructive-ish actions read alike.
    private void OnDetailPrimaryActionPressed(object sender, EventArgs e)
    {
        if (_application == null || !_currentDetailSoldierId.HasValue) return;

        ChapterPrompt prompt = _application.DescribeRecall(_currentDetailSoldierId.Value);
        if (prompt.Kind != ChapterPromptKind.Confirm) return;

        _pendingRecallSoldierId = _currentDetailSoldierId;
        _recallConfirmationDialog.Title = prompt.Title;
        _recallConfirmationDialog.DialogText = prompt.Message;
        _recallConfirmationDialog.PopupCentered();
    }

    private void OnRecallConfirmed()
    {
        if (_application == null || !_pendingRecallSoldierId.HasValue) return;
        int soldierId = _pendingRecallSoldierId.Value;
        _pendingRecallSoldierId = null;

        if (_application.ConfirmRecall(_application.SessionToken, soldierId).Succeeded)
        {
            CampaignChanged?.Invoke(this, EventArgs.Empty);
        }
        RenderCurrentPath();
    }

    // After a transfer, select the soldier that follows the transferred one in the pre-transfer
    // context list (or the previous one if it was last), keeping the current browse scope put.
    private void SelectNextInContext(IReadOnlyList<int> contextSoldierIds, int transferIndex)
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

    private ChapterBrowserQuery BuildQuery() => new(
        _navigator.Path.CompanyId,
        _navigator.Path.SquadId,
        _navigator.Path.SoldierId,
        _navigator.SelectedItem?.Level,
        _navigator.SelectedItem?.Id,
        _activeFilter,
        _historicalSoldierId);

    private void RenderCurrentPath()
    {
        if (ChapterView == null || _application == null) return;

        ChapterBrowserView view = _application.QueryChapterBrowser(BuildQuery());
        ChapterView.SetBreadcrumbs(view.Breadcrumbs);
        ChapterView.SetFilterActive(_activeFilter.Count);
        ChapterView.SetLeftMenu(view.LeftMenuTitle, view.LeftMenu);
        ChapterView.SetDetail(view.Detail);
        ChapterView.SetTransferOptions(view.TransferOptions);
        _transferOptions = view.TransferOptions;
        _contextSoldierIds = view.ContextSoldierIds;
        _currentDetailSoldierId = view.DetailSoldierId;
        _currentDetailSquadId = view.DetailSoldierSquadId;
    }

    private static int IndexOf(IReadOnlyList<int> ids, int value)
    {
        for (int index = 0; index < ids.Count; index++)
        {
            if (ids[index] == value) return index;
        }
        return -1;
    }
}
