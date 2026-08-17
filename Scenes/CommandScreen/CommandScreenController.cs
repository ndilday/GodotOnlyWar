using Godot;
using OnlyWar.Helpers.Command;
using OnlyWar.Helpers.Storage;
using OnlyWar.Models;
using OnlyWar.Models.Command;
using OnlyWar.Models.Events;
using OnlyWar.Models.Reports;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class CommandScreenController : MainScreenController
{
    private CommandScreenView _view;
    private readonly CommandBriefBuilder _briefBuilder = new();
    private CommandLens _lens = CommandLens.Brief;
    private CommandBriefCategory? _briefFilter;
    private ChronicleFilter _chronicleFilter = ChronicleFilter.All;
    private readonly Dictionary<ChronicleFilter, int> _chroniclePages = [];
    private readonly Dictionary<CommandLens, int> _scrollOffsets = [];
    private string _focusedStableKey;

    public bool HasRenderedBrief { get; private set; }

    public event EventHandler<CampaignNavigationTarget> NavigationRequested;
    public event EventHandler LastTurnReportRequested;

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
        RefreshFromExternalChange();
    }

    public override void _ExitTree()
    {
        if (_view == null) return;
        _view.LensSelected -= OnLensSelected;
        _view.BriefFilterSelected -= OnBriefFilterSelected;
        _view.ChronicleFilterSelected -= OnChronicleFilterSelected;
        _view.BriefItemActionRequested -= OnBriefItemActionRequested;
        _view.FocusedStableKeyChanged -= OnFocusedStableKeyChanged;
        _view.NavigationRequested -= OnNavigationRequested;
        _view.LastTurnReportRequested -= OnLastTurnReportRequested;
        _view.LoadOlderRequested -= OnLoadOlderRequested;
    }

    public void RefreshFromExternalChange()
    {
        if (_view == null || !GameDataSingleton.Instance.IsInitialized) return;
        _view.SetLens(_lens);
        _view.SetLastTurnReportState(
            GameDataSingleton.Instance.Sector.PlayerForce.LastTurnReportSnapshot != null);
        if (_lens == CommandLens.Brief)
        {
            RenderBrief();
            HasRenderedBrief = true;
        }
        else
        {
            RenderChronicle();
        }
        RestoreScrollOffset();
    }

    public void SelectBrief()
    {
        _lens = CommandLens.Brief;
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
        PlayerForce force = GameDataSingleton.Instance.Sector.PlayerForce;
        IReadOnlyList<ChronicleFilter> available = ChapterChronicleBrowser.GetAvailableFilters(
            force.ChapterChronicle,
            force.CampaignEventLedger);
        List<(ChronicleFilter Filter, string Label, int Count)> filters = available
            .Select(filter =>
            {
                int count = CountChronicleEntries(force, filter);
                return (filter, GetChronicleFilterLabel(filter), count);
            })
            .ToList();
        if (!available.Contains(_chronicleFilter)) _chronicleFilter = ChronicleFilter.All;
        _view.SetChronicleFilters(filters, _chronicleFilter);
        int page = GetChroniclePage();
        IReadOnlyList<ChronicleEntryViewModel> entries = ChapterChronicleBrowser.GetPage(
            force.ChapterChronicle,
            force.CampaignEventLedger,
            GameDataSingleton.Instance.Sector,
            _chronicleFilter,
            page);
        _view.SetChronicle(
            entries,
            _chronicleFilter,
            ChapterChronicleBrowser.HasPage(
                force.ChapterChronicle,
                force.CampaignEventLedger,
                _chronicleFilter,
                page + 1),
            force.ChapterChronicle.Entries.Count > 0);
    }

    private CommandBriefModel BuildBrief() => _briefBuilder.Build(
        GameDataSingleton.Instance.Date,
        GameDataSingleton.Instance.Sector,
        GameDataSingleton.Instance.GameRulesData,
        GameDataSingleton.Instance.Sector.PlayerForce.LastTurnReportSnapshot,
        GameDataSingleton.Instance.Sector.PlayerForce.CampaignEventLedger.GetEventsInWeekRange(
            GameDataSingleton.Instance.Date.GetTotalWeeks(),
            GameDataSingleton.Instance.Date.GetTotalWeeks()));

    private int GetChroniclePage() =>
        _chroniclePages.GetValueOrDefault(_chronicleFilter, 0);

    private static int CountChronicleEntries(PlayerForce force, ChronicleFilter filter) =>
        ChapterChronicleBrowser.Count(
            force.ChapterChronicle,
            force.CampaignEventLedger,
            filter);

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

    private static string GetChronicleFilterLabel(ChronicleFilter filter) => filter switch
    {
        ChronicleFilter.Defining => "Defining",
        ChronicleFilter.Battles => "Battles",
        ChronicleFilter.Brothers => "Brothers",
        ChronicleFilter.Worlds => "Worlds",
        ChronicleFilter.Chapter => "Chapter",
        _ => "All"
    };
}
