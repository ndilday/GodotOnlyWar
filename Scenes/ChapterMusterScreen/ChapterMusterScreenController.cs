using Godot;
using OnlyWar.Helpers;
using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Units;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class ChapterMusterScreenController : MainScreenController
{
    private const int CandidatePageSize = 50;

    // Set MUSTER_PERF_LOG=1 to print open cost to the Godot log: how many times a single open
    // rebuilds the lists, and the node count the screen leaves behind. Container sorting is
    // deferred, so a Stopwatch around the render methods will NOT see the layout cost - compare
    // the frame numbers instead.
    private static readonly bool LogOpenDiagnostics =
        System.Environment.GetEnvironmentVariable("MUSTER_PERF_LOG") == "1";

    private readonly MusterPlanService _plan = new();
    private readonly ChapterMusterViewModelBuilder _builder = new();
    private readonly SoldierFilterService _filters = new();
    private MusterPopulationMode _populationMode = MusterPopulationMode.PromotionEligible;
    private List<SoldierFilterCondition> _activeFilters = [];
    private int? _selectedSoldierId;
    private FormationVacancyViewModel _selectedFormation;
    private string _pendingFormationSelectionKey;
    private int? _scopeCompanyId;
    private IReadOnlyList<MusterCandidateViewModel> _candidates = [];
    private int _candidatePage;

    // Rebuilt by RefreshLists and reused by the selection path, which cannot change it: the set of
    // legal transfers depends on the order of battle and the staged plan, not on what is selected.
    private SoldierTransferContext _transferContext;

    // The candidate rows currently on screen, keyed by soldier, so that changing the selection can
    // restyle the two affected rows instead of rebuilding the page.
    private readonly Dictionary<int, PanelContainer> _renderedCandidateRows = [];
    private readonly Dictionary<string, PanelContainer> _renderedFormationRows = [];
    private int _refreshCount;

    private OptionButton _scopeSelector;
    private OptionButton _populationSelector;
    private Label _candidateCount;
    private Label _candidatePageLabel;
    private Button _candidatePreviousButton;
    private Button _candidateNextButton;
    private VBoxContainer _candidateRows;
    private VBoxContainer _formationRows;
    private VBoxContainer _planRows;
    private ScrollContainer _candidateScroll;
    private ScrollContainer _formationScroll;
    private ScrollContainer _planScroll;
    private Label _previewTitle;
    private Label _previewDetail;
    private Button _stageButton;
    private PlanStatusBadge _planStatus;
    private Label _constraintText;
    private Button _reviewButton;
    private Label _stagedCountLabel;
    private Button _backToChapterButton;
    private Button _undoLastButton;
    private Button _clearAllButton;
    private ChapterFilterDialog _filterDialog;
    private ConfirmationDialog _reviewDialog;
    private ConfirmationDialog _leaveDialog;

    public event EventHandler CampaignChanged;
    public event EventHandler BackRequested;

    public override void _Ready()
    {
        BuildWorkspace();
        BuildDialogs();
    }

    public void RefreshFromExternalChange() => Refresh();

    public void OpenForSoldier(int? soldierId)
    {
        _selectedSoldierId = soldierId;
        int before = _refreshCount;
        Refresh();
        if (!_selectedSoldierId.HasValue && _candidates.Count > 0)
        {
            SelectCandidate(_candidates[0].SoldierId);
        }
        if (LogOpenDiagnostics)
        {
            GD.Print($"[Muster] open: {_refreshCount - before} list rebuild(s), "
                + $"frame {Engine.GetFramesDrawn()}, "
                + $"nodes {Performance.GetMonitor(Performance.Monitor.ObjectNodeCount)}");
        }
    }

    public override void RequestClose()
    {
        if (_plan.Actions.Count == 0)
        {
            base.RequestClose();
            return;
        }
        _leaveDialog.DialogText = $"The Muster has {_plan.Actions.Count} staged change(s).";
        _leaveDialog.PopupCentered();
    }

    private void BuildWorkspace()
    {
        Theme = GD.Load<Theme>("res://Scenes/OnlyWarTheme.tres");
        VBoxContainer root = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 6);

        ColorRect backdrop = new()
        {
            Color = new Color(0.003f, 0.005f, 0.006f, 1f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);
        AddChild(root);

        HBoxContainer columns = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        columns.AddThemeConstantOverride("separation", 8);
        root.AddChild(columns);

        Control candidates = BuildCandidatesPanel();
        candidates.SizeFlagsStretchRatio = 31;
        columns.AddChild(candidates);

        Control formations = BuildFormationsPanel();
        formations.SizeFlagsStretchRatio = 39;
        columns.AddChild(formations);

        Control plan = BuildPlanPanel();
        plan.SizeFlagsStretchRatio = 28;
        columns.AddChild(plan);

        root.AddChild(BuildActionBar());
    }

    private Control BuildCandidatesPanel()
    {
        VBoxContainer stack = PanelStack("TRANSFER CANDIDATES", out PanelContainer panel);
        HBoxContainer scopeRow = new();
        scopeRow.AddThemeConstantOverride("separation", 6);
        Label scopeLabel = new()
        {
            Text = "SCOPE",
            CustomMinimumSize = new Vector2(52, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        scopeLabel.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        scopeRow.AddChild(scopeLabel);
        _scopeSelector = new OptionButton
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = "Limit transfer candidates to the entire Chapter or a company."
        };
        _scopeSelector.ItemSelected += index =>
        {
            _scopeCompanyId = index == 0
                ? null
                : _scopeSelector.GetItemId((int)index) == 0
                    ? null
                    : (int?)_scopeSelector.GetItemId((int)index);
            _selectedSoldierId = null;
            _selectedFormation = null;
            RefreshLists();
        };
        scopeRow.AddChild(_scopeSelector);
        stack.AddChild(scopeRow);

        HBoxContainer controls = new();
        _populationSelector = new OptionButton
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = "Promotion Eligible shows soldiers with a legal higher-rank destination. Any Legal Move also includes lateral transfers and role changes."
        };
        _populationSelector.AddItem("PROMOTION ELIGIBLE");
        _populationSelector.AddItem("ANY LEGAL MOVE");
        _populationSelector.ItemSelected += index =>
        {
            _populationMode = index == 0 ? MusterPopulationMode.PromotionEligible : MusterPopulationMode.AnyLegalMove;
            _selectedSoldierId = null;
            _selectedFormation = null;
            RefreshLists();
        };
        controls.AddChild(_populationSelector);
        Button filter = new() { Text = "FILTER" };
        IconAtlas.Apply(filter, "filter");
        filter.TooltipText = "Add or remove compound candidate filters, including Sergeant Recommended.";
        filter.Pressed += OpenFilter;
        controls.AddChild(filter);
        stack.AddChild(controls);
        _candidateCount = new Label();
        _candidateCount.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        stack.AddChild(_candidateCount);
        HBoxContainer pager = new();
        pager.AddThemeConstantOverride("separation", 6);
        _candidatePreviousButton = new Button
        {
            Text = "‹",
            CustomMinimumSize = new Vector2(34, 30),
            TooltipText = "Show the previous page of Muster candidates."
        };
        _candidatePreviousButton.Pressed += () => SetCandidatePage(_candidatePage - 1);
        pager.AddChild(_candidatePreviousButton);
        _candidatePageLabel = new Label
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        pager.AddChild(_candidatePageLabel);
        _candidateNextButton = new Button
        {
            Text = "›",
            CustomMinimumSize = new Vector2(34, 30),
            TooltipText = "Show the next page of Muster candidates."
        };
        _candidateNextButton.Pressed += () => SetCandidatePage(_candidatePage + 1);
        pager.AddChild(_candidateNextButton);
        pager.AddThemeFontSizeOverride("font_size", 12);
        stack.AddChild(pager);
        _candidateScroll = ExpandScroll();
        _candidateRows = NewRowStack(5);
        _candidateScroll.AddChild(_candidateRows);
        stack.AddChild(_candidateScroll);
        return panel;
    }

    private Control BuildFormationsPanel()
    {
        VBoxContainer stack = PanelStack("FORMATIONS & VACANCIES", out PanelContainer panel);
        HBoxContainer columns = new();
        foreach ((string text, float ratio) in new[] { ("FORMATION", 2.4f), ("TYPE", 1f), ("ROSTER", 1f), ("LOCATION", 1.3f) })
        {
            Label label = new() { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsStretchRatio = ratio };
            label.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
            columns.AddChild(label);
        }
        stack.AddChild(columns);
        _formationScroll = ExpandScroll();
        _formationRows = NewRowStack(3);
        _formationScroll.AddChild(_formationRows);
        stack.AddChild(_formationScroll);

        PanelContainer preview = new();
        OnlyWarStyle.ApplyInsetPanel(preview);
        VBoxContainer previewStack = new();
        preview.AddChild(previewStack);
        _previewTitle = new Label { Text = "Select a candidate and formation" };
        _previewTitle.AddThemeColorOverride("font_color", OnlyWarStyle.Gold);
        previewStack.AddChild(_previewTitle);
        _previewDetail = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        previewStack.AddChild(_previewDetail);
        _stageButton = new Button { Text = "STAGE CHANGE", Disabled = true };
        _stageButton.TooltipText = "Select a candidate and a legal destination before staging a change.";
        _stageButton.Pressed += StageSelected;
        previewStack.AddChild(_stageButton);
        stack.AddChild(preview);
        return panel;
    }

    private Control BuildPlanPanel()
    {
        VBoxContainer stack = PanelStack("PLAN & CONSTRAINTS", out PanelContainer panel);
        HBoxContainer stagedHeader = new();
        Label title = new() { Text = "STAGED CHANGES", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeColorOverride("font_color", OnlyWarStyle.Gold);
        stagedHeader.AddChild(title);
        stack.AddChild(stagedHeader);
        _planScroll = ExpandScroll();
        _planRows = NewRowStack(5);
        _planScroll.AddChild(_planRows);
        stack.AddChild(_planScroll);
        PanelContainer logistics = new();
        OnlyWarStyle.ApplyInsetPanel(logistics);
        VBoxContainer logisticsStack = new();
        logistics.AddChild(logisticsStack);
        Label logisticsTitle = new() { Text = "CONSTRAINT VALIDATION" };
        logisticsTitle.AddThemeColorOverride("font_color", OnlyWarStyle.Gold);
        logisticsStack.AddChild(logisticsTitle);
        _constraintText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        logisticsStack.AddChild(_constraintText);
        stack.AddChild(logistics);
        _planStatus = new PlanStatusBadge();
        stack.AddChild(_planStatus);
        _reviewButton = new Button { Text = "REVIEW & CONFIRM", Disabled = true };
        _reviewButton.Pressed += OpenReview;
        return panel;
    }

    private Control BuildActionBar()
    {
        PanelContainer panel = new();
        OnlyWarStyle.ApplyContentPanel(panel);
        panel.CustomMinimumSize = new Vector2(0, 48);
        HBoxContainer row = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        row.AddThemeConstantOverride("separation", 8);
        panel.AddChild(row);

        _stagedCountLabel = new Label
        {
            Text = "0 CHANGES STAGED",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center
        };
        _stagedCountLabel.AddThemeColorOverride("font_color", OnlyWarStyle.Gold);
        _stagedCountLabel.AddThemeFontSizeOverride("font_size", 16);
        row.AddChild(_stagedCountLabel);

        _backToChapterButton = new Button { Text = "BACK TO CHAPTER" };
        _backToChapterButton.TooltipText = "Return to the main Chapter screen.";
        _backToChapterButton.Pressed += RequestBackToChapter;
        row.AddChild(_backToChapterButton);

        _undoLastButton = new Button { Text = "UNDO LAST CHANGE" };
        _undoLastButton.TooltipText = "Remove the most recently staged transfer.";
        _undoLastButton.Pressed += UndoLastChange;
        row.AddChild(_undoLastButton);

        _clearAllButton = new Button { Text = "CLEAR ALL" };
        _clearAllButton.TooltipText = "Remove every staged transfer from the current plan.";
        _clearAllButton.Pressed += ClearPlan;
        row.AddChild(_clearAllButton);

        _reviewButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(_reviewButton);
        return panel;
    }

    private void BuildDialogs()
    {
        _filterDialog = new ChapterFilterDialog();
        _filterDialog.FilterApplied += conditions => { _activeFilters = conditions ?? []; RefreshLists(); };
        _filterDialog.FilterCleared += () => { _activeFilters = []; RefreshLists(); };
        AddChild(_filterDialog);
        _reviewDialog = new ConfirmationDialog
        {
            Title = "Confirm Bulk Transfer",
            OkButtonText = "CONFIRM CHANGES",
            CancelButtonText = "RETURN TO EDITING"
        };
        ConfigureReviewDialog(_reviewDialog);
        _reviewDialog.Confirmed += CommitPlan;
        AddChild(_reviewDialog);
        _leaveDialog = new ConfirmationDialog { Title = "Staged Muster Plan", OkButtonText = "DISCARD PLAN", CancelButtonText = "KEEP EDITING" };
        _leaveDialog.AddButton("REVIEW PLAN", true, "review");
        _leaveDialog.AddButton("BACK TO CHAPTER", true, "back");
        _leaveDialog.CustomAction += action =>
        {
            if (action == "review")
            {
                _leaveDialog.Hide();
                OpenReview();
            }
            else if (action == "back")
            {
                _leaveDialog.Hide();
                BackRequested?.Invoke(this, EventArgs.Empty);
            }
        };
        _leaveDialog.Confirmed += () => { _plan.Clear(); base.RequestClose(); };
        AddChild(_leaveDialog);
    }

    private static void ConfigureReviewDialog(ConfirmationDialog dialog)
    {
        dialog.Theme = GD.Load<Theme>("res://Scenes/OnlyWarTheme.tres");
        dialog.MinSize = new Vector2I(760, 440);
        dialog.Size = new Vector2I(820, 500);
        dialog.Unresizable = false;
        dialog.DialogAutowrap = true;
        dialog.AddThemeStyleboxOverride("panel", CreateDialogSurfaceStyle());
        dialog.AddThemeColorOverride("title_color", OnlyWarStyle.Gold);

        Label label = dialog.GetLabel();
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        label.AddThemeColorOverride("font_color", OnlyWarStyle.BodyText);
        label.AddThemeFontSizeOverride("font_size", 14);

        Node labelParent = label.GetParent();
        int labelIndex = label.GetIndex();
        labelParent.RemoveChild(label);
        MarginContainer contentMargin = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 300)
        };
        contentMargin.AddThemeConstantOverride("margin_left", 10);
        contentMargin.AddThemeConstantOverride("margin_top", 8);
        contentMargin.AddThemeConstantOverride("margin_right", 10);
        contentMargin.AddThemeConstantOverride("margin_bottom", 8);
        Control scrollFrame = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        ScrollContainer scroll = new()
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        scroll.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        scroll.AddChild(label);
        scrollFrame.AddChild(scroll);
        contentMargin.AddChild(scrollFrame);
        labelParent.AddChild(contentMargin);
        labelParent.MoveChild(contentMargin, labelIndex);

        OnlyWarStyle.ApplyActionButton(dialog.GetOkButton());
        Button cancel = dialog.GetCancelButton();
        if (cancel != null)
        {
            OnlyWarStyle.ApplyActionButton(cancel, OnlyWarStyle.MutedText);
        }
    }

    private static StyleBoxFlat CreateDialogSurfaceStyle() => new()
    {
        BgColor = new Color(0.003f, 0.005f, 0.006f, 0.99f),
        BorderColor = OnlyWarStyle.Gold,
        BorderWidthLeft = 2,
        BorderWidthTop = 2,
        BorderWidthRight = 2,
        BorderWidthBottom = 2,
        CornerRadiusTopLeft = 2,
        CornerRadiusTopRight = 2,
        CornerRadiusBottomLeft = 2,
        CornerRadiusBottomRight = 2,
        ShadowColor = new Color(0, 0, 0, 0.72f),
        ShadowSize = 8
    };

    private void Refresh()
    {
        PlayerForce force = Force;
        if (force?.Army?.OrderOfBattle == null) return;
        Unit chapter = force.Army.OrderOfBattle;
        _scopeSelector.Clear();
        _scopeSelector.AddItem($"ENTIRE CHAPTER · {chapter.GetAllMembers().Count()}", 0);
        foreach (Unit company in chapter.ChildUnits)
        {
            _scopeSelector.AddItem(
                $"{company.Name} · {company.GetAllMembers().Count()}", company.Id);
        }
        int selectedScope = _scopeCompanyId.HasValue
            ? _scopeSelector.GetItemIndex(_scopeCompanyId.Value)
            : 0;
        _scopeSelector.Select(Math.Max(0, selectedScope));
        RefreshLists();
    }

    private void RefreshLists()
    {
        PlayerForce force = Force;
        if (force == null) return;
        _refreshCount++;
        IEnumerable<PlayerSoldier> scope = _scopeCompanyId.HasValue
            ? force.Army.OrderOfBattle.ChildUnits.FirstOrDefault(unit => unit.Id == _scopeCompanyId)?.GetAllMembers().OfType<PlayerSoldier>() ?? []
            : force.Army.PlayerSoldierMap.Values;
        List<PlayerSoldier> scoped = scope.ToList();
        if (_activeFilters.Count > 0)
        {
            scoped = _filters.Apply(scoped, _activeFilters, GameDataSingleton.Instance.Date)
                .OfType<PlayerSoldier>().ToList();
        }
        _transferContext = SoldierTransferContext.Build(force.Army.OrderOfBattle);
        SoldierTransferContext transferContext = _transferContext;
        _candidates = _builder.BuildCandidates(
            force, _plan, _populationMode, scoped, transferContext)
            .Where(candidate => !candidate.IsStaged)
            .ToList();
        bool selectedSoldierIsStaged = _selectedSoldierId.HasValue
            && _plan.IsStaged(_selectedSoldierId.Value);
        if (_selectedSoldierId.HasValue
            && !selectedSoldierIsStaged
            && _candidates.All(candidate => candidate.SoldierId != _selectedSoldierId))
        {
            _selectedSoldierId = null;
        }
        if (_selectedSoldierId.HasValue
            && _candidates.Any(candidate => candidate.SoldierId == _selectedSoldierId))
        {
            int selectedIndex = _candidates
                .Select((candidate, index) => (candidate, index))
                .FirstOrDefault(item => item.candidate.SoldierId == _selectedSoldierId).index;
            _candidatePage = selectedIndex / CandidatePageSize;
        }
        else
        {
            _candidatePage = Math.Clamp(
                _candidatePage,
                0,
                Math.Max(0, (_candidates.Count - 1) / CandidatePageSize));
        }
        _candidateCount.Text = $"{PopulationLabel()} · {_candidates.Count} eligible";
        RenderCandidatePage();
        RenderFormations(transferContext);
        RenderPlan(transferContext);
    }

    private void SetCandidatePage(int page)
    {
        _candidatePage = Math.Clamp(
            page,
            0,
            Math.Max(0, (_candidates.Count - 1) / CandidatePageSize));
        RenderCandidatePage();
    }

    private void RenderCandidatePage()
    {
        int pageCount = Math.Max(1, (_candidates.Count + CandidatePageSize - 1) / CandidatePageSize);
        _candidatePage = Math.Clamp(_candidatePage, 0, pageCount - 1);
        int first = _candidatePage * CandidatePageSize;

        _renderedCandidateRows.Clear();
        VBoxContainer rows = NewRowStack(5);
        foreach (MusterCandidateViewModel candidate in _candidates
            .Skip(first)
            .Take(CandidatePageSize))
        {
            PanelContainer row = CreateCandidateRow(candidate);
            _renderedCandidateRows[candidate.SoldierId] = row;
            rows.AddChild(row);
        }
        SwapRows(_candidateScroll, ref _candidateRows, rows);

        _candidatePageLabel.Text = $"Page {_candidatePage + 1} / {pageCount}";
        _candidatePreviousButton.Disabled = _candidatePage == 0;
        _candidateNextButton.Disabled = _candidatePage >= pageCount - 1;
    }

    private PanelContainer CreateCandidateRow(MusterCandidateViewModel candidate)
    {
        PanelContainer panel = new();
        bool selected = candidate.SoldierId == _selectedSoldierId;
        ApplyCandidateRowStyle(panel, selected);
        panel.CustomMinimumSize = new Vector2(0, 64);
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 7);
        panel.AddChild(row);

        TextureRect icon = RosterRowStyle.CreateIconRect(
            candidate.IsStaged ? "locked" : candidate.SquadIconKey, 42);
        row.AddChild(icon);

        Button button = new()
        {
            Flat = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Modulate = candidate.IsStaged ? new Color(1, 1, 1, 0.62f) : Colors.White,
            TooltipText = candidate.IsStaged
                ? "Already included in the plan; select to focus that staged change."
                : $"Select {candidate.Name} to compare legal destinations."
        };
        MarginContainer textMargin = new();
        textMargin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        textMargin.AddThemeConstantOverride("margin_left", 2);
        textMargin.AddThemeConstantOverride("margin_top", 2);
        textMargin.AddThemeConstantOverride("margin_right", 2);
        textMargin.AddThemeConstantOverride("margin_bottom", 2);
        VBoxContainer textStack = new();
        textStack.AddThemeConstantOverride("separation", 0);
        textStack.MouseFilter = MouseFilterEnum.Ignore;
        textMargin.AddChild(textStack);
        textMargin.MouseFilter = MouseFilterEnum.Ignore;
        button.AddChild(textMargin);

        Label name = new() { Text = candidate.Name, ClipText = true };
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.AddThemeFontSizeOverride("font_size", 16);
        name.AddThemeColorOverride("font_color", OnlyWarStyle.Gold);
        textStack.AddChild(name);

        Label formation = new()
        {
            Text = $"{candidate.Role} · {candidate.Formation}",
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            TooltipText = $"{candidate.Role} · {candidate.Formation}"
        };
        formation.AddThemeFontSizeOverride("font_size", 12);
        formation.AddThemeColorOverride("font_color", OnlyWarStyle.BodyText);
        textStack.AddChild(formation);

        Label location = new()
        {
            Text = candidate.Location,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            TooltipText = candidate.Location
        };
        location.AddThemeFontSizeOverride("font_size", 12);
        location.AddThemeColorOverride("font_color", OnlyWarStyle.BodyText);
        textStack.AddChild(location);

        button.Pressed += () =>
        {
            if (candidate.IsStaged)
            {
                MusterStagedAction action = _plan.Actions.First(entry => entry.SoldierId == candidate.SoldierId);
                _constraintText.Text = $"STAGED: {action.SourceDisplay} → {action.TargetDisplay}";
                return;
            }
            SelectCandidate(candidate.SoldierId);
        };
        row.AddChild(button);
        HonorBadgeView honors = new()
        {
            CustomMinimumSize = new Vector2(154, 0),
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        honors.SetHonors(candidate.Honors);
        row.AddChild(honors);
        return panel;
    }

    /// <summary>
    /// Moves the selection to a candidate without rebuilding the list. Which soldiers are eligible
    /// does not depend on which one is selected, so the only visual changes are the two affected
    /// row styles and the destination panel.
    /// </summary>
    private void SelectCandidate(int soldierId)
    {
        if (_selectedSoldierId == soldierId) return;

        if (_selectedSoldierId.HasValue
            && _renderedCandidateRows.TryGetValue(_selectedSoldierId.Value, out PanelContainer previous))
        {
            ApplyCandidateRowStyle(previous, false);
        }
        _selectedSoldierId = soldierId;
        if (_renderedCandidateRows.TryGetValue(soldierId, out PanelContainer current))
        {
            ApplyCandidateRowStyle(current, true);
        }
        string previousFormationTarget = FormationTargetKey(_selectedFormation);
        _selectedFormation = null;
        _pendingFormationSelectionKey = previousFormationTarget == null
            ? null
            : $"target:{previousFormationTarget}";

        if (_transferContext == null)
        {
            RefreshLists();
            return;
        }
        RenderFormations(_transferContext);
    }

    private void RenderFormations(SoldierTransferContext transferContext)
    {
        PlayerSoldier soldier = SelectedSoldier;
        if (soldier == null)
        {
            _selectedFormation = null;
            _pendingFormationSelectionKey = null;
            _renderedFormationRows.Clear();
            VBoxContainer empty = NewRowStack(4);
            AddMuted(empty, "Select a candidate to show legal destinations.");
            SwapRows(_formationScroll, ref _formationRows, empty);
            RenderPreview();
            return;
        }
        VBoxContainer rows = NewRowStack(4);
        _renderedFormationRows.Clear();
        string selectedFormationKey = FormationSelectionKey(_selectedFormation)
            ?? _pendingFormationSelectionKey;
        FormationVacancyViewModel matchedPendingFormation = null;
        FormationVacancyGroup? currentGroup = null;
        foreach (FormationVacancyViewModel row in _builder.BuildFormations(
            Force, soldier, _plan, transferContext))
        {
            if (currentGroup != row.Group)
            {
                currentGroup = row.Group;
                Label heading = new() { Text = row.GroupLabel };
                heading.AddThemeColorOverride("font_color", OnlyWarStyle.Gold);
                rows.AddChild(heading);
            }
            string formationKey = FormationSelectionKey(row);
            bool selected = selectedFormationKey != null
                && FormationSelectionMatches(selectedFormationKey, row)
                && !row.IsFull;
            if (selected && _pendingFormationSelectionKey != null)
            {
                matchedPendingFormation = row;
            }
            PanelContainer formationPanel = CreateFormationRow(row, selected);
            if (formationKey != null)
            {
                _renderedFormationRows[formationKey] = formationPanel;
            }
            rows.AddChild(formationPanel);
        }
        if (_pendingFormationSelectionKey != null)
        {
            _selectedFormation = matchedPendingFormation;
            _pendingFormationSelectionKey = null;
        }
        SwapRows(_formationScroll, ref _formationRows, rows);
        RenderPreview();
    }

    private PanelContainer CreateFormationRow(FormationVacancyViewModel formation, bool selected)
    {
        PanelContainer panel = new();
        ApplyFormationRowStyle(panel, selected);
        panel.CustomMinimumSize = new Vector2(0, 48);

        Button button = new()
        {
            Flat = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            TooltipText = FormationTooltip(formation),
            MouseDefaultCursorShape = CursorShape.PointingHand
        };
        HBoxContainer content = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        content.AddThemeConstantOverride("separation", 5);
        content.MouseFilter = MouseFilterEnum.Ignore;
        content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        button.AddChild(content);

        HBoxContainer formationCell = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 2.4f
        };
        formationCell.AddThemeConstantOverride("separation", 5);
        TextureRect icon = RosterRowStyle.CreateIconRect(formation.SquadIconKey, 34);
        formationCell.AddChild(icon);

        VBoxContainer formationDetails = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        formationDetails.AddThemeConstantOverride("separation", 0);
        Label formationName = new()
        {
            Text = formation.FormationName,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        formationName.AddThemeFontSizeOverride("font_size", 12);
        formationDetails.AddChild(formationName);
        Label state = new() { Text = formation.StateLabel, ClipText = true };
        state.AddThemeFontSizeOverride("font_size", 10);
        state.AddThemeColorOverride("font_color", FormationStateColor(formation.Group));
        formationDetails.AddChild(state);
        formationCell.AddChild(formationDetails);
        content.AddChild(formationCell);

        AddFormationDivider(content);
        content.AddChild(CreateFormationValue(formation.TypeLabel, 1.0f));
        AddFormationDivider(content);
        content.AddChild(CreateFormationValue(formation.RosterText, 1.0f));
        AddFormationDivider(content);
        content.AddChild(CreateFormationValue(formation.Location, 1.3f));

        button.Pressed += () =>
        {
            string previousKey = FormationSelectionKey(_selectedFormation);
            if (previousKey != null
                && _renderedFormationRows.TryGetValue(previousKey, out PanelContainer previous))
            {
                ApplyFormationRowStyle(previous, false);
            }

            _selectedFormation = formation;
            ApplyFormationRowStyle(panel, true);
            RenderPreview();
        };
        panel.AddChild(button);
        return panel;
    }

    private static Label CreateFormationValue(string text, float stretchRatio)
    {
        Label value = new()
        {
            Text = text,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            SizeFlagsStretchRatio = stretchRatio,
            TooltipText = text
        };
        value.AddThemeFontSizeOverride("font_size", 12);
        value.AddThemeColorOverride("font_color", OnlyWarStyle.BodyText);
        return value;
    }

    private static void AddFormationDivider(HBoxContainer content)
    {
        VSeparator divider = new() { CustomMinimumSize = new Vector2(1, 0) };
        divider.Modulate = OnlyWarStyle.WithAlpha(OnlyWarStyle.MutedText, 0.35f);
        content.AddChild(divider);
    }

    private static void ApplyFormationRowStyle(PanelContainer panel, bool selected)
    {
        StyleBoxFlat style = OnlyWarStyle.GetListRowStyle(selected);
        style.ContentMarginLeft = 6;
        style.ContentMarginTop = 2;
        style.ContentMarginRight = 6;
        style.ContentMarginBottom = 2;
        panel.AddThemeStyleboxOverride("panel", style);
    }

    private static string FormationSelectionKey(FormationVacancyViewModel formation)
    {
        if (formation == null) return null;
        if (!string.IsNullOrEmpty(formation.SelectionKey))
        {
            return formation.SelectionKey;
        }
        if (formation.Option == null) return null;

        SoldierTransferOption option = formation.Option;
        return option.IsNewSquad
            ? $"new:{option.TargetUnit?.Id ?? int.MinValue}:{option.TargetSquadTemplate?.Id ?? int.MinValue}:{option.SoldierTemplate?.Id ?? int.MinValue}"
            : $"squad:{option.SquadId}:{option.SoldierTemplate?.Id ?? int.MinValue}";
    }

    private static string FormationTargetKey(FormationVacancyViewModel formation)
    {
        if (formation == null) return null;
        if (!string.IsNullOrEmpty(formation.SelectionKey))
        {
            return formation.SelectionKey;
        }
        if (formation.Option == null) return null;

        SoldierTransferOption option = formation.Option;
        if (option.IsProvisionalSquad)
        {
            return $"staged:{option.ProvisionalFormationId}";
        }
        if (option.IsNewSquad)
        {
            return $"new:{option.TargetUnit?.Id ?? int.MinValue}:{option.TargetSquadTemplate?.Id ?? int.MinValue}";
        }
        return $"squad:{option.SquadId}";
    }

    private static bool FormationSelectionMatches(
        string selectionKey,
        FormationVacancyViewModel formation)
    {
        if (selectionKey.StartsWith("target:", StringComparison.Ordinal))
        {
            return string.Equals(
                selectionKey["target:".Length..],
                FormationTargetKey(formation),
                StringComparison.Ordinal);
        }
        return string.Equals(selectionKey, FormationSelectionKey(formation), StringComparison.Ordinal);
    }

    private static Color FormationStateColor(FormationVacancyGroup group) => group switch
    {
        FormationVacancyGroup.NeedsLeaders => OnlyWarStyle.Critical,
        FormationVacancyGroup.AvailableNewFormations => OnlyWarStyle.Gold,
        _ => OnlyWarStyle.MutedText
    };

    private void RenderPreview()
    {
        PlayerSoldier soldier = SelectedSoldier;
        if (soldier == null || _selectedFormation == null)
        {
            _previewTitle.Text = "Select a candidate and formation";
            _previewDetail.Text = "The preview will make the resulting role explicit.";
            _stageButton.Disabled = true;
            _stageButton.TooltipText = "Select a candidate and a legal destination before staging a change.";
            return;
        }
        if (_selectedFormation.Option == null)
        {
            _previewTitle.Text = $"{soldier.AssignedSquad.Name}  →  {_selectedFormation.FormationName}";
            _previewDetail.Text = "This formation is already staged in the plan.\nUse the staged change card to edit it.";
            _stageButton.Text = "FORMATION ALREADY STAGED";
            _stageButton.Disabled = true;
            _stageButton.TooltipText = "This provisional formation is already included in the plan.";
            return;
        }
        _previewTitle.Text = $"{soldier.AssignedSquad.Name}  →  {_selectedFormation.FormationName}";
        _previewDetail.Text = $"{soldier.Template.Name}  →  {_selectedFormation.ResultingRole}\n{_selectedFormation.Location}";
        _stageButton.Text = MutationButtonText(soldier, _selectedFormation.Option);
        bool soldierAlreadyStaged = _plan.IsStaged(soldier.Id);
        bool formationIsFull = _selectedFormation.IsFull;
        _stageButton.Disabled = soldierAlreadyStaged || formationIsFull;
        _stageButton.TooltipText = soldierAlreadyStaged
            ? "This soldier already has a staged change. Edit or undo that change before staging another."
            : formationIsFull
                ? "This formation reaches capacity after the staged changes and cannot accept another soldier."
                : $"Add this {MutationDescription(soldier, _selectedFormation.Option)} to the plan without committing it yet.";
    }

    private void StageSelected()
    {
        if (SelectedSoldier == null
            || _selectedFormation?.Option == null
            || _selectedFormation.IsFull)
        {
            return;
        }
        PlayerSoldier stagedSoldier = SelectedSoldier;
        SoldierTransferOption stagedOption = _selectedFormation.Option;
        MusterStagedAction action = _plan.Stage(stagedSoldier, stagedOption);

        int currentIndex = _candidates
            .Select((candidate, index) => (candidate, index))
            .Where(item => item.candidate.SoldierId == stagedSoldier.Id)
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .First();
        _selectedSoldierId = _candidates
            .Skip(currentIndex + 1)
            .Where(candidate => !_plan.IsStaged(candidate.SoldierId))
            .Select(candidate => (int?)candidate.SoldierId)
            .FirstOrDefault();

        _pendingFormationSelectionKey = stagedOption.IsNewSquad
            ? $"staged:{action.ActionId}"
            : FormationSelectionKey(_selectedFormation);
        _selectedFormation = null;
        RefreshLists();
    }

    private void RenderPlan(SoldierTransferContext transferContext)
    {
        VBoxContainer rows = NewRowStack(5);
        foreach ((MusterStagedAction action, int index) in _plan.Actions.Select((action, index) => (action, index)))
        {
            PanelContainer panel = new();
            OnlyWarStyle.ApplyListRow(panel, false);
            VBoxContainer stack = new();
            panel.AddChild(stack);
            HBoxContainer summary = new();
            TextureRect actionIcon = new()
            {
                Texture = IconAtlas.GetIcon(ActionIconKey(action.Kind)),
                CustomMinimumSize = new Vector2(28, 28),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                TooltipText = ActionIconTooltip(action.Kind),
                MouseFilter = MouseFilterEnum.Pass
            };
            summary.AddChild(actionIcon);
            Label text = new() { Text = $"{index + 1}. {Force.Army.PlayerSoldierMap.GetValueOrDefault(action.SoldierId)?.Name}\n{action.SourceDisplay} → {action.TargetDisplay}", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            summary.AddChild(text);
            stack.AddChild(summary);
            HBoxContainer actions = new();
            Button edit = new() { Text = "EDIT" };
            edit.TooltipText = "Remove this entry from the plan and return to its soldier and destination choices.";
            edit.Pressed += () => { _selectedSoldierId = action.SoldierId; _plan.Undo(action.ActionId); RefreshLists(); };
            actions.AddChild(edit);
            Button undo = new() { Text = "UNDO" };
            undo.TooltipText = "Remove only this staged change from the plan.";
            undo.Pressed += () => { _plan.Undo(action.ActionId); RefreshLists(); };
            actions.AddChild(undo);
            stack.AddChild(actions);
            rows.AddChild(panel);
        }
        if (_plan.Actions.Count == 0) AddMuted(rows, "No changes staged.");
        SwapRows(_planScroll, ref _planRows, rows);
        MusterPlanValidation validation = _plan.Validate(Force, transferContext);
        if (_plan.Actions.Count == 0)
        {
            _constraintText.Text = "Stage a personnel action to evaluate logistics.";
            _planStatus.SetStatus(PlanStatusBadge.Status.Staged, "PLAN EMPTY");
            _planStatus.TooltipText = "No changes are staged. Add at least one personnel action to begin a Muster plan.";
        }
        else if (validation.IsValid)
        {
            _constraintText.Text = "No relocation required, or all selected destinations have legal capacity.";
            _planStatus.SetStatus(PlanStatusBadge.Status.Valid, $"VALID · {_plan.Actions.Count} CHANGE(S)");
            _planStatus.TooltipText = "Every staged change is currently legal and all transport requirements are resolved.";
        }
        else
        {
            _constraintText.Text = string.Join("\n", validation.Blockers.Select(blocker => "• " + blocker));
            _planStatus.SetStatus(PlanStatusBadge.Status.Blocked, $"BLOCKED · {validation.Blockers.Count} ISSUE(S)");
            _planStatus.TooltipText = string.Join("\n", validation.Blockers);
        }
        _reviewButton.Disabled = _plan.Actions.Count == 0 || !validation.IsValid;
        _reviewButton.TooltipText = _plan.Actions.Count == 0
            ? "Stage at least one change before review."
            : validation.IsValid
                ? "Review the complete transaction before committing any changes."
                : string.Join("\n", validation.Blockers);
        UpdateActionBar();
    }

    private void UpdateActionBar()
    {
        int count = _plan.Actions.Count;
        _stagedCountLabel.Text = $"{count} CHANGE{(count == 1 ? string.Empty : "S")} STAGED";
        _undoLastButton.Disabled = count == 0;
        _clearAllButton.Disabled = count == 0;
    }

    private void UndoLastChange()
    {
        if (_plan.Actions.Count == 0) return;
        _plan.Undo(_plan.Actions[^1].ActionId);
        RefreshLists();
    }

    private void ClearPlan()
    {
        if (_plan.Actions.Count == 0) return;
        _plan.Clear();
        _selectedFormation = null;
        RefreshLists();
    }

    private void RequestBackToChapter()
    {
        if (_plan.Actions.Count == 0)
        {
            BackRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        _leaveDialog.DialogText =
            $"The Bulk Transfer plan has {_plan.Actions.Count} staged change(s).";
        _leaveDialog.PopupCentered();
    }

    private void OpenFilter()
    {
        List<PlayerSoldier> scope = (_scopeCompanyId.HasValue
            ? Force.Army.OrderOfBattle.ChildUnits.First(unit => unit.Id == _scopeCompanyId).GetAllMembers().OfType<PlayerSoldier>()
            : Force.Army.PlayerSoldierMap.Values).ToList();
        _filterDialog.Populate(_filters.GetAvailableRoles(scope),
            _filters.GetAvailableHonors(scope, GameDataSingleton.Instance.GameRulesData.RatingAwardTiers),
            _activeFilters);
        _filterDialog.PopupCentered();
    }

    private void OpenReview()
    {
        _reviewDialog.Title = "Confirm Bulk Transfer";
        _reviewDialog.DialogText = string.Join("\n\n", _plan.Actions.Select((action, index) =>
            $"{index + 1}. {Force.Army.PlayerSoldierMap.GetValueOrDefault(action.SoldierId)?.Name}\n{action.SourceDisplay}\n→ {action.TargetDisplay}"));
        _reviewDialog.PopupCentered(new Vector2I(860, 500));
    }

    private void CommitPlan()
    {
        MusterCommitResult result = _plan.Commit(Force, GameDataSingleton.Instance.Date);
        if (result.Succeeded) CampaignChanged?.Invoke(this, EventArgs.Empty);
        else _constraintText.Text = string.Join("\n", result.Errors);
        _selectedSoldierId = null;
        _selectedFormation = null;
        Refresh();
    }

    private PlayerForce Force => GameDataSingleton.Instance?.Sector?.PlayerForce;
    private PlayerSoldier SelectedSoldier => _selectedSoldierId.HasValue
        ? Force?.Army?.PlayerSoldierMap.GetValueOrDefault(_selectedSoldierId.Value)
        : null;
    private string PopulationLabel() => _populationMode == MusterPopulationMode.PromotionEligible
        ? "Promotion eligible" : "Any legal move";

    private static string MutationButtonText(PlayerSoldier soldier, SoldierTransferOption option)
    {
        if (option.IsNewSquad) return "STAGE PROMOTION & CREATE FORMATION";
        if (option.SoldierTemplate.Rank > soldier.Template.Rank) return "STAGE PROMOTION & ASSIGNMENT";
        return option.SoldierTemplate == soldier.Template ? "STAGE TRANSFER" : "STAGE TRANSFER & ROLE CHANGE";
    }

    private static string MutationDescription(PlayerSoldier soldier, SoldierTransferOption option)
    {
        if (option.IsNewSquad) return "promotion and new formation";
        if (option.SoldierTemplate.Rank > soldier.Template.Rank) return "promotion and assignment";
        return option.SoldierTemplate == soldier.Template ? "transfer" : "transfer and role change";
    }

    private static string FormationTooltip(FormationVacancyViewModel row)
    {
        List<string> lines = [];
        lines.Add(row.Group switch
        {
            FormationVacancyGroup.EmptyFormations =>
                "Empty lineage: this formation retains its identity and history but currently has no members or location.",
            FormationVacancyGroup.AvailableNewFormations =>
                "New formation: provisional until confirmation; no permanent identity, ordinal, or location is allocated while staged.",
            FormationVacancyGroup.NeedsLeaders =>
                "Needs leader: this formation cannot operate normally until a leader-eligible soldier is assigned.",
            _ => "Understrength: this formation has open roster capacity."
        });
        if (row.Location == "—")
            lines.Add("Location: none. A previous deployment is not inherited.");
        else
            lines.Add($"Location: {row.Location}");
        lines.Add(string.IsNullOrWhiteSpace(row.RosterTooltip)
            ? $"Roster: {row.RosterText}"
            : row.RosterTooltip);
        return string.Join("\n", lines);
    }

    private static string ActionIconKey(MusterMutationKind kind) => kind switch
    {
        MusterMutationKind.PromotionAndCreateFormation => "formation_create",
        MusterMutationKind.FleetRebalance => "fleet_rebalance",
        _ => "route"
    };

    private static string ActionIconTooltip(MusterMutationKind kind) => kind switch
    {
        MusterMutationKind.PromotionAndCreateFormation => "Create new formation",
        MusterMutationKind.FleetRebalance => "Fleet rebalance",
        _ => "Personnel reassignment"
    };

    private static void ApplyCandidateRowStyle(PanelContainer panel, bool selected)
    {
        StyleBoxFlat style = OnlyWarStyle.GetListRowStyle(selected);
        style.ContentMarginLeft = 6;
        style.ContentMarginTop = 2;
        style.ContentMarginRight = 6;
        style.ContentMarginBottom = 2;
        panel.AddThemeStyleboxOverride("panel", style);
    }

    private static VBoxContainer PanelStack(string title, out PanelContainer panel)
    {
        panel = new PanelContainer { CustomMinimumSize = new Vector2(290, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        OnlyWarStyle.ApplyContentPanel(panel);
        VBoxContainer stack = new();
        stack.AddThemeConstantOverride("separation", 7);
        panel.AddChild(stack);
        Label heading = new() { Text = title };
        heading.AddThemeColorOverride("font_color", OnlyWarStyle.Gold);
        heading.AddThemeFontSizeOverride("font_size", 18);
        stack.AddChild(heading);
        return stack;
    }

    private static ScrollContainer ExpandScroll() => new()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
        SizeFlagsVertical = SizeFlags.ExpandFill,
        HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
    };
    private static VBoxContainer NewRowStack(int separation)
    {
        VBoxContainer stack = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        stack.AddThemeConstantOverride("separation", separation);
        return stack;
    }

    /// <summary>
    /// Swaps a freshly built, still-detached row stack in for the one on screen.
    ///
    /// Rows are built detached and attached in a single AddChild because adding them one at a time
    /// to an in-tree container makes every insert queue a container sort and invalidate minimum
    /// sizes up the whole ancestor chain. The outgoing stack is removed from the tree immediately
    /// rather than only QueueFree'd: a queued node stays parented until the end of the frame, so it
    /// would otherwise keep taking part in this frame's layout alongside its replacement.
    /// </summary>
    private static void SwapRows(ScrollContainer scroll, ref VBoxContainer current, VBoxContainer replacement)
    {
        // Clearing and refilling one container used to leave the scroll offset untouched; replacing
        // the container resets it. Restore it deferred, because immediately after AddChild the new
        // rows have not been sorted yet and the scrollbar would clamp the value to zero.
        int offset = scroll.ScrollVertical;
        if (current != null)
        {
            scroll.RemoveChild(current);
            current.QueueFree();
        }
        current = replacement;
        scroll.AddChild(replacement);
        if (offset > 0)
        {
            scroll.SetDeferred(ScrollContainer.PropertyName.ScrollVertical, offset);
        }
    }
    private static void AddMuted(Container container, string text)
    {
        Label label = new() { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        container.AddChild(label);
    }
    private static string IconKey(Unit company) => company.UnitTemplate.Name switch
    {
        "Veteran Company" => "elite", "Assault Company" => "assault",
        "Devastator Company" => "devastator", "Scout Company" => "scout", _ => "chapter"
    };
}
