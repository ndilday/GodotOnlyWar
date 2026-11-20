using Godot;
using OnlyWar.Application;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class CommandScreenController : MainScreenController
{
    private CommandScreenView _view;
    private ICommandScreenApplication _command;
    private IMandateScreenApplication _mandates;
    private Guid _sessionToken;
    private ConfirmationDialog _confirmDialog;
    private AcceptDialog _messageDialog;
    private Func<MandateCommandResult> _pendingCommand;
    private CommandLens _lens = CommandLens.Brief;
    private CommandBriefCategory? _briefFilter;
    private ChronicleFilter _chronicleFilter = ChronicleFilter.All;
    private readonly Dictionary<ChronicleFilter, int> _chroniclePages = [];
    private readonly Dictionary<CommandLens, int> _scrollOffsets = [];
    private string _focusedStableKey;

    public bool HasRenderedBrief { get; private set; }

    public event EventHandler<CampaignNavigationTarget> NavigationRequested;
    public event EventHandler LastTurnReportRequested;
    public event EventHandler CampaignChanged;

    public void Configure(ICommandScreenApplication command, IMandateScreenApplication mandates = null)
    {
        if (_command != null) _command.SessionChanged -= OnSessionChanged;
        _command = command ?? throw new ArgumentNullException(nameof(command));
        _mandates = mandates;
        _command.SessionChanged += OnSessionChanged;
        _sessionToken = command.SessionToken;
    }

    // A replaced campaign has its own brief and chronicle; drop the previous filters and focus.
    private void OnSessionChanged(object sender, EventArgs args)
    {
        _sessionToken = _command.SessionToken;
        _briefFilter = null;
        _chronicleFilter = ChronicleFilter.All;
        _chroniclePages.Clear();
        _scrollOffsets.Clear();
        _focusedStableKey = null;
        HasRenderedBrief = false;
        if (_view != null) RefreshFromExternalChange();
    }

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<CommandScreenView>("CommandScreenView");
        _view.LensSelected += OnLensSelected;
        _view.BriefFilterSelected += OnBriefFilterSelected;
        _view.ChronicleFilterSelected += OnChronicleFilterSelected;
        _view.BriefItemActionRequested += OnBriefItemActionRequested;
        _view.FocusedStableKeyChanged += OnFocusedStableKeyChanged;
        _view.NavigationRequested += OnNavigationRequested;
        _view.LastTurnReportRequested += OnLastTurnReportRequested;
        _view.LoadOlderRequested += OnLoadOlderRequested;
        _view.SwearMandateRequested += OnSwearMandateRequested;
        _view.AbandonMandateRequested += OnAbandonMandateRequested;
        _view.RenounceChapterWorldRequested += OnRenounceChapterWorldRequested;
        _confirmDialog = new ConfirmationDialog { DialogAutowrap = true, MinSize = new Vector2I(460, 0) };
        _confirmDialog.Confirmed += OnPendingCommandConfirmed;
        AddChild(_confirmDialog);
        _messageDialog = new AcceptDialog { DialogAutowrap = true, MinSize = new Vector2I(420, 0) };
        AddChild(_messageDialog);
        RefreshFromExternalChange();
    }

    public override void _ExitTree()
    {
        if (_command != null) _command.SessionChanged -= OnSessionChanged;
        if (_view == null) return;
        _view.LensSelected -= OnLensSelected;
        _view.BriefFilterSelected -= OnBriefFilterSelected;
        _view.ChronicleFilterSelected -= OnChronicleFilterSelected;
        _view.BriefItemActionRequested -= OnBriefItemActionRequested;
        _view.FocusedStableKeyChanged -= OnFocusedStableKeyChanged;
        _view.NavigationRequested -= OnNavigationRequested;
        _view.LastTurnReportRequested -= OnLastTurnReportRequested;
        _view.LoadOlderRequested -= OnLoadOlderRequested;
        _view.SwearMandateRequested -= OnSwearMandateRequested;
        _view.AbandonMandateRequested -= OnAbandonMandateRequested;
        _view.RenounceChapterWorldRequested -= OnRenounceChapterWorldRequested;
        if (_confirmDialog != null) _confirmDialog.Confirmed -= OnPendingCommandConfirmed;
    }

    public override void RefreshFromExternalChange()
    {
        if (_view == null || _command == null || !_command.HasCampaign) return;
        if (_lens == CommandLens.Mandates && _mandates == null) _lens = CommandLens.Brief;
        _view.SetLens(_lens);
        _view.SetMandatesAvailable(_mandates != null);
        _view.SetLastTurnReportState(_command.HasLastTurnReport);
        switch (_lens)
        {
            case CommandLens.Brief:
                RenderBrief();
                HasRenderedBrief = true;
                break;
            case CommandLens.Mandates:
                RenderMandates();
                break;
            default:
                RenderChronicle();
                break;
        }
        RestoreScrollOffset();
    }

    public void SelectBrief() => SelectLens(CommandLens.Brief);

    public void SelectLens(CommandLens lens)
    {
        SaveScrollOffset();
        _lens = lens;
        RefreshFromExternalChange();
    }

    private void OnLensSelected(object sender, CommandLens lens)
    {
        SaveScrollOffset();
        _lens = lens;
        RefreshFromExternalChange();
        RestoreScrollOffset();
    }

    private void OnBriefFilterSelected(object sender, CommandBriefCategory? category)
    {
        SaveScrollOffset();
        _briefFilter = category;
        RenderBrief();
        RestoreScrollOffset();
    }

    private void OnChronicleFilterSelected(object sender, ChronicleFilter filter)
    {
        SaveScrollOffset();
        _chronicleFilter = filter;
        RenderChronicle();
        RestoreScrollOffset();
    }

    // ---- Mandates: swear, abandon, renounce the Chapter World (TDD §6.15) ------------------------

    private void OnSwearMandateRequested(object sender, MandateOptionView option)
    {
        if (_mandates == null || option == null) return;
        Guid token = _mandates.SessionToken;
        AskToConfirm(
            "Swear the Mandate",
            $"Swear to {LowerFirst(option.Title)}?\n\n{option.Goal}\n\nDeadline: {option.DeadlineLabel}. "
                + $"Kept, the vow is worth {option.LegacyValue} legacy; broken or abandoned, it costs "
                + $"{option.FailureCost}. The Chapter can hold only one mandate at a time.",
            "Swear",
            () => _mandates.SwearMandate(token, option.Key));
    }

    private void OnAbandonMandateRequested(object sender, ActiveMandateView active)
    {
        if (_mandates == null || active == null) return;
        Guid token = _mandates.SessionToken;
        AskToConfirm(
            active.IsPromisedWorld ? "Give Up the Promised World" : "Abandon the Mandate",
            active.AbandonWarning,
            active.IsPromisedWorld ? "Give Up" : "Abandon",
            () => _mandates.AbandonMandate(token));
    }

    private void OnRenounceChapterWorldRequested(object sender, MandateBoardView board)
    {
        if (_mandates == null || board?.CanRenounceChapterWorld != true) return;
        Guid token = _mandates.SessionToken;
        AskToConfirm(
            "Renounce the Chapter World",
            board.RenounceWarning,
            "Renounce",
            () => _mandates.RenounceChapterWorld(token));
    }

    private void AskToConfirm(string title, string message, string confirmLabel, Func<MandateCommandResult> command)
    {
        _pendingCommand = command;
        _confirmDialog.Title = title;
        _confirmDialog.DialogText = message;
        _confirmDialog.OkButtonText = confirmLabel;
        _confirmDialog.PopupCentered();
    }

    private void OnPendingCommandConfirmed()
    {
        Func<MandateCommandResult> command = _pendingCommand;
        _pendingCommand = null;
        if (command == null) return;
        MandateCommandResult result = command();
        if (result.Succeeded)
        {
            CampaignChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _messageDialog.Title = "Mandates";
            _messageDialog.DialogText = result.Message;
            _messageDialog.PopupCentered();
        }
        RefreshFromExternalChange();
    }

    private void RenderMandates()
    {
        MandateBoardView board = _mandates?.QueryMandates() ?? MandateBoardView.Empty;
        _view.SetMandateRail(board);
        _view.SetMandates(board);
    }

    private static string LowerFirst(string text) =>
        string.IsNullOrEmpty(text) ? text : char.ToLowerInvariant(text[0]) + text[1..];

    private void OnBriefItemActionRequested(object sender, string stableKey)
    {
        CommandBriefModel model = BuildBrief();
        CommandBriefItem item = model.Items.FirstOrDefault(candidate => candidate.StableKey == stableKey);
        if (item == null) return;
        _focusedStableKey = stableKey;
        OnNavigationRequested(this, item.PrimaryTarget);
    }

    private void OnNavigationRequested(object sender, CampaignNavigationTarget target)
    {
        if (target == null || !target.IsAvailable) return;
        SaveScrollOffset();
        NavigationRequested?.Invoke(this, target);
    }

    private void OnFocusedStableKeyChanged(object sender, string stableKey)
    {
        _focusedStableKey = stableKey;
    }

    private void OnLastTurnReportRequested(object sender, EventArgs e) =>
        LastTurnReportRequested?.Invoke(this, EventArgs.Empty);

    private void OnLoadOlderRequested(object sender, EventArgs e)
    {
        _chroniclePages[_chronicleFilter] = GetChroniclePage() + 1;
        RenderChronicle();
        RestoreScrollOffset();
    }

    private void RenderBrief()
    {
        CommandBriefModel model = BuildBrief();
        if (_briefFilter.HasValue && !model.AvailableCategories.Contains(_briefFilter.Value))
        {
            _briefFilter = null;
        }
        List<(CommandBriefCategory? Category, string Label, int Count)> filters =
        [
            (null, "All", model.Items.Count)
        ];
        filters.AddRange(model.AvailableCategories.Select(category =>
            ((CommandBriefCategory?)category, GetCategoryLabel(category), model.ForCategory(category).Count)));
        _view.SetBriefFilters(filters, _briefFilter);
        _view.SetBrief(model, _briefFilter, _briefFilter.HasValue);
    }

    private void RenderChronicle()
    {
        ChronicleView chronicle = _command.QueryChronicle(_chronicleFilter, GetChroniclePage());
        _chronicleFilter = chronicle.Filter;
        _view.SetChronicleFilters(
            chronicle.Filters
                .Select(option => (option.Filter, option.Label, option.Count)).ToList(),
            _chronicleFilter);
        // The legacy graph and the per-master table head the whole Chronicle and its Chapter
        // filter (TDD §6.15 *Application*).
        MandateBoardView legacy = _mandates != null
            && _chronicleFilter is ChronicleFilter.All or ChronicleFilter.Chapter
                ? _mandates.QueryMandates()
                : null;
        _view.SetChronicle(
            chronicle.Entries,
            _chronicleFilter,
            chronicle.HasOlder,
            chronicle.HasAnyEntries,
            legacy);
    }

    private CommandBriefModel BuildBrief() => _command.QueryBrief();

    private int GetChroniclePage() =>
        _chroniclePages.GetValueOrDefault(_chronicleFilter, 0);

    private void SaveScrollOffset()
    {
        if (_view != null) _scrollOffsets[_lens] = _view.GetScrollOffset();
    }

    private void RestoreScrollOffset()
    {
        _view?.SetScrollOffset(_scrollOffsets.GetValueOrDefault(_lens));
        _view?.FocusStableKey(_focusedStableKey);
    }

    private static string GetCategoryLabel(CommandBriefCategory category) => category switch
    {
        CommandBriefCategory.RequiresOrders => "Requires Orders",
        CommandBriefCategory.PetitionsAndOpportunities => "Petitions & Opportunities",
        CommandBriefCategory.OperationsUnderway => "Operations Underway",
        CommandBriefCategory.RecoveryAndReinforcement => "Recovery & Reinforcement",
        CommandBriefCategory.StrategicSituation => "Strategic Situation",
        CommandBriefCategory.Mandates => "Mandates",
        _ => category.ToString()
    };
}
