using Godot;
using OnlyWar.Helpers.Recruitment;
using OnlyWar.Helpers.UI;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

public partial class TrainingUnitScreenView : MainScreenView
{
    private enum RecruitmentPage
    {
        Overview,
        Recruitment,
        Aspirants,
        Scouts
    }

    private Label _setupBanner;
    private readonly Dictionary<RecruitmentPage, Button> _navigationButtons = [];
    private readonly Dictionary<RecruitmentPage, Control> _pages = [];
    private RecruitmentPage _activePage;
    private bool _setupMode;
    private bool _suppressDoctrineEvents;

    private Label _overviewWorld;
    private Label _overviewResources;
    private Label _overviewStaff;
    private Label _overviewFunnel;
    private Label _overviewCapacity;
    private VBoxContainer _overviewEvents;

    private OptionButton _policyOption;
    private readonly Dictionary<string, HSlider> _filterSliders = [];
    private readonly Dictionary<string, Label> _filterValues = [];
    private HSlider _geneticSlider;
    private Label _geneticValue;
    private Label _doctrineForecast;
    private Label _staffValidation;
    private Button _confirmDoctrineButton;

    private Label _candidateSummary;
    private VBoxContainer _candidateRows;
    private Label _aspirantSummary;
    private VBoxContainer _aspirantRows;

    private OptionButton _focusOption;
    private RichTextLabel _squadReadinessRichText;
    private VBoxContainer _squadVBox;
    private VBoxContainer _promotionRows;
    private ButtonGroup _squadButtonGroup;
    private IReadOnlyList<ScoutSquadRow> _scoutRows = [];
    private int? _selectedSquadId;

    public event EventHandler<int> SquadButtonPressed;
    public event EventHandler<TrainingFocuses> TrainingFocusSelected;
    public event EventHandler<Variant> LinkClicked;
    public event EventHandler<RecruitmentDoctrineDraft> DoctrineChanged;
    public event EventHandler DoctrineConfirmed;
    public event EventHandler ManageAdministrativeStaffRequested;
    public event EventHandler<int> NeophytePlacementRequested;
    public event EventHandler<int> Phase13PromotionRequested;

    public override void _Ready()
    {
        base._Ready();
        Theme = GD.Load<Theme>("res://Scenes/OnlyWarTheme.tres");
        OnlyWarStyle.ApplyContentPanel(GetNode<Panel>("ContentPanel"));
        OnlyWarStyle.ApplyInsetPanel(GetNode<Panel>("HeaderPanel"));

        _setupBanner = GetNode<Label>("SetupBanner");
        BuildNavigation();
        BuildPages();
        SelectPage(RecruitmentPage.Overview);
    }

    public void RenderLockedState(string message)
    {
        SetSetupMode(false);
        _overviewWorld.Text = "RECRUITMENT LOCKED";
        _overviewResources.Text = message;
        _overviewStaff.Text = string.Empty;
        _overviewFunnel.Text = string.Empty;
        _overviewCapacity.Text = string.Empty;
        ClearContainer(_overviewEvents);
        _navigationButtons[RecruitmentPage.Recruitment].Disabled = true;
        _navigationButtons[RecruitmentPage.Aspirants].Disabled = true;
        SelectPage(RecruitmentPage.Overview);
    }

    public void Render(RecruitmentScreenSnapshot snapshot, int? selectedSquadId)
    {
        _navigationButtons[RecruitmentPage.Recruitment].Disabled = false;
        _navigationButtons[RecruitmentPage.Aspirants].Disabled = false;
        SetSetupMode(!snapshot.IsSetupComplete);
        PopulateOverview(snapshot);
        PopulateDoctrine(snapshot);
        PopulateCandidatesAndAspirants(snapshot.Candidates, snapshot.Aspirants);
        PopulateScoutSquads(snapshot.ScoutSquads, selectedSquadId);
        if (!snapshot.IsSetupComplete)
        {
            ShowRecruitmentView();
        }
    }

    public void SetSetupMode(bool required)
    {
        _setupMode = required;
        if (_setupBanner != null)
        {
            _setupBanner.Visible = required;
            _setupBanner.Text = required
                ? "MANDATORY FOUNDING DOCTRINE — setup must be completed before continuing"
                : string.Empty;
        }
    }

    public void ShowRecruitmentView()
    {
        SelectPage(RecruitmentPage.Recruitment);
    }

    public void ShowSetupValidation(string text)
    {
        _staffValidation.Text = text ?? string.Empty;
        _staffValidation.Visible = !string.IsNullOrWhiteSpace(text);
    }

    public void UpdateForecast(
        RecruitmentDoctrineDraft doctrine,
        RecruitmentForecast forecast)
    {
        _suppressDoctrineEvents = true;
        SetDoctrineControls(doctrine);
        _suppressDoctrineEvents = false;
        _doctrineForecast.Text = FormatDoctrineForecast(forecast);
    }

    public void PopulateScoutSquads(
        IReadOnlyList<ScoutSquadRow> squads,
        int? selectedSquadId = null)
    {
        _scoutRows = squads ?? [];
        _selectedSquadId = selectedSquadId;
        ClearContainer(_squadVBox);
        foreach (ScoutSquadRow squad in _scoutRows)
        {
            AddSquad(squad, squad.Id == selectedSquadId);
        }

        ScoutSquadRow selected = _scoutRows.FirstOrDefault(
            squad => squad.Id == selectedSquadId);
        if (selected == null)
        {
            _focusOption.Disabled = true;
            _squadReadinessRichText.Text =
                "Select a Scout squad to review its training readiness.";
            ClearContainer(_promotionRows);
            return;
        }

        _focusOption.Disabled = false;
        _focusOption.Select(_focusOption.GetItemIndex((int)selected.Focus));
        _squadReadinessRichText.Text = string.IsNullOrWhiteSpace(selected.ReadinessReport)
            ? "This squad has no neophytes or Scouts to evaluate."
            : selected.ReadinessReport;
        PopulatePromotionRows(selected.PromotionRows);
    }

    private void BuildNavigation()
    {
        HBoxContainer navigation = GetNode<HBoxContainer>("Navigation");
        AddNavigationButton(navigation, RecruitmentPage.Overview, "Overview");
        AddNavigationButton(navigation, RecruitmentPage.Recruitment, "Recruitment");
        AddNavigationButton(navigation, RecruitmentPage.Aspirants, "Aspirants");
        AddNavigationButton(navigation, RecruitmentPage.Scouts, "Neophytes & Scouts");
    }

    private void AddNavigationButton(
        HBoxContainer navigation,
        RecruitmentPage page,
        string text)
    {
        Button button = new()
        {
            Text = text,
            ToggleMode = true,
            CustomMinimumSize = new Vector2(190, 44),
            MouseDefaultCursorShape = CursorShape.PointingHand
        };
        button.Pressed += () => SelectPage(page);
        navigation.AddChild(button);
        _navigationButtons[page] = button;
    }

    private void BuildPages()
    {
        Control host = GetNode<Control>("ContentPanel/PageHost");
        BuildOverviewPage(host);
        BuildRecruitmentPage(host);
        BuildAspirantsPage(host);
        BuildScoutsPage(host);
    }

    private void BuildOverviewPage(Control host)
    {
        VBoxContainer content = CreateScrollablePage(host, RecruitmentPage.Overview);
        content.AddChild(CreateSectionTitle("RECRUITMENT COMMAND"));
        _overviewWorld = CreateBodyLabel();
        content.AddChild(_overviewWorld);

        HSeparator separator = new();
        content.AddChild(separator);
        content.AddChild(CreateSectionTitle("STRATEGIC RESOURCES"));
        _overviewResources = CreateBodyLabel();
        content.AddChild(_overviewResources);
        _overviewStaff = CreateBodyLabel();
        content.AddChild(_overviewStaff);

        content.AddChild(new HSeparator());
        content.AddChild(CreateSectionTitle("WEEKLY RECRUITMENT FUNNEL"));
        _overviewFunnel = CreateBodyLabel();
        content.AddChild(_overviewFunnel);
        _overviewCapacity = CreateBodyLabel();
        content.AddChild(_overviewCapacity);

        content.AddChild(new HSeparator());
        content.AddChild(CreateSectionTitle("RECENT PROGRAM EVENTS"));
        _overviewEvents = new VBoxContainer();
        _overviewEvents.AddThemeConstantOverride("separation", 6);
        content.AddChild(_overviewEvents);
    }

    private void BuildRecruitmentPage(Control host)
    {
        VBoxContainer content = CreateScrollablePage(host, RecruitmentPage.Recruitment);
        content.AddChild(CreateSectionTitle("FOUNDING DOCTRINE"));
        Label explanation = CreateBodyLabel();
        explanation.Text =
            "Set the standards used by the Master of Recruitment. Changes remain a "
            + "forecast until confirmed.";
        content.AddChild(explanation);

        HBoxContainer policyRow = new();
        policyRow.AddChild(CreateFieldLabel("Recruitment policy", 240));
        _policyOption = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _policyOption.AddItem("Voluntary Presentation", (int)RecruitmentPolicy.VoluntaryPresentation);
        _policyOption.AddItem("Planetary Tithe", (int)RecruitmentPolicy.PlanetaryTithe);
        _policyOption.ItemSelected += index => RaiseDoctrineChanged();
        policyRow.AddChild(_policyOption);
        content.AddChild(policyRow);

        content.AddChild(CreateSectionTitle("ATTRIBUTE FILTERS"));
        AddFilterRow(content, "Strength", "strength");
        AddFilterRow(content, "Constitution", "constitution");
        AddFilterRow(content, "Intelligence", "intelligence");
        AddFilterRow(content, "Dexterity", "dexterity");
        AddFilterRow(content, "Ego", "ego");

        HBoxContainer geneticRow = new();
        geneticRow.AddChild(CreateFieldLabel("Genetic compatibility", 240));
        _geneticSlider = new HSlider
        {
            MinValue = 0,
            MaxValue = 100,
            Step = 1,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _geneticSlider.ValueChanged += value =>
        {
            _geneticValue.Text = $"{value:0}%";
            RaiseDoctrineChanged();
        };
        geneticRow.AddChild(_geneticSlider);
        _geneticValue = CreateFieldLabel("90%", 80);
        _geneticValue.HorizontalAlignment = HorizontalAlignment.Right;
        geneticRow.AddChild(_geneticValue);
        content.AddChild(geneticRow);

        content.AddChild(new HSeparator());
        content.AddChild(CreateSectionTitle("LIVE FORECAST"));
        _doctrineForecast = CreateBodyLabel();
        content.AddChild(_doctrineForecast);
        _staffValidation = CreateBodyLabel();
        _staffValidation.AddThemeColorOverride("font_color", new Color(0.96f, 0.45f, 0.34f));
        _staffValidation.Visible = false;
        content.AddChild(_staffValidation);
        Button manageStaffButton = new()
        {
            Text = "Manage 10th Company HQ",
            CustomMinimumSize = new Vector2(320, 44),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            TooltipText =
                "Open the Chapter screen to reassign eligible recruitment staff.",
            MouseDefaultCursorShape = CursorShape.PointingHand
        };
        manageStaffButton.Pressed += () =>
            ManageAdministrativeStaffRequested?.Invoke(this, EventArgs.Empty);
        content.AddChild(manageStaffButton);
        _confirmDoctrineButton = new Button
        {
            Text = "Confirm Recruitment Doctrine",
            CustomMinimumSize = new Vector2(320, 48),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            MouseDefaultCursorShape = CursorShape.PointingHand
        };
        _confirmDoctrineButton.Pressed += () =>
            DoctrineConfirmed?.Invoke(this, EventArgs.Empty);
        content.AddChild(_confirmDoctrineButton);
    }

    private void AddFilterRow(VBoxContainer content, string label, string key)
    {
        HBoxContainer row = new();
        row.AddChild(CreateFieldLabel(label, 240));
        HSlider slider = new()
        {
            MinValue = RecruitmentRules.MinimumAttributeFilterHalfSteps,
            MaxValue = RecruitmentRules.MaximumAttributeFilterHalfSteps,
            Step = 1,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        Label valueLabel = CreateFieldLabel("0σ", 80);
        valueLabel.HorizontalAlignment = HorizontalAlignment.Right;
        slider.ValueChanged += value =>
        {
            valueLabel.Text = FormatSigma((int)value);
            RaiseDoctrineChanged();
        };
        row.AddChild(slider);
        row.AddChild(valueLabel);
        content.AddChild(row);
        _filterSliders[key] = slider;
        _filterValues[key] = valueLabel;
    }

    private void BuildAspirantsPage(Control host)
    {
        VBoxContainer content = CreateScrollablePage(host, RecruitmentPage.Aspirants);
        content.AddChild(CreateSectionTitle("QUALIFIED CANDIDATES"));
        _candidateSummary = CreateBodyLabel();
        content.AddChild(_candidateSummary);
        _candidateRows = new VBoxContainer();
        content.AddChild(_candidateRows);
        content.AddChild(new HSeparator());
        content.AddChild(CreateSectionTitle("ASPIRANT COHORT"));
        _aspirantSummary = CreateBodyLabel();
        content.AddChild(_aspirantSummary);
        _aspirantRows = new VBoxContainer();
        content.AddChild(_aspirantRows);
    }

    private void BuildScoutsPage(Control host)
    {
        Control page = CreatePage(host, RecruitmentPage.Scouts);
        HSplitContainer split = new()
        {
            AnchorsPreset = (int)LayoutPreset.FullRect
        };
        split.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        page.AddChild(split);

        VBoxContainer left = new()
        {
            CustomMinimumSize = new Vector2(380, 0)
        };
        left.AddChild(CreateSectionTitle("10TH COMPANY SQUADS"));
        ScrollContainer squadScroll = new()
        {
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _squadVBox = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _squadVBox.AddThemeConstantOverride("separation", 7);
        squadScroll.AddChild(_squadVBox);
        left.AddChild(squadScroll);
        split.AddChild(left);

        VBoxContainer right = new();
        right.AddChild(CreateSectionTitle("READINESS & ADVANCEMENT"));
        HBoxContainer focusRow = new();
        focusRow.AddChild(CreateFieldLabel("Training focus", 150));
        _focusOption = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        PopulateFocusOptions();
        _focusOption.ItemSelected += OnFocusOptionSelected;
        _focusOption.Disabled = true;
        focusRow.AddChild(_focusOption);
        right.AddChild(focusRow);
        _squadReadinessRichText = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            CustomMinimumSize = new Vector2(0, 180),
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _squadReadinessRichText.MetaClicked += meta =>
            LinkClicked?.Invoke(this, meta);
        right.AddChild(_squadReadinessRichText);
        right.AddChild(CreateSectionTitle("BLACK CARAPACE CANDIDATES"));
        _promotionRows = new VBoxContainer();
        right.AddChild(_promotionRows);
        split.AddChild(right);
        _squadButtonGroup = new ButtonGroup();
    }

    private void PopulateOverview(RecruitmentScreenSnapshot snapshot)
    {
        _overviewWorld.Text =
            $"Home World: {snapshot.HomeWorldName}\n"
            + $"Public Chapter population: {snapshot.ChapterPopulation:N0}";
        _overviewResources.Text =
            $"Requisition: {snapshot.Requisition:N0}    "
            + $"Gene-seed stockpile: {snapshot.Geneseed:N0}    "
            + $"Weekly operating cost: {snapshot.Forecast.WeeklyRequisitionCost:N0}";
        RecruitmentStaffSummary staff = snapshot.Staff;
        _overviewStaff.Text =
            $"10th Company HQ staff — Scout Sergeants: {staff.ScoutSergeants}, "
            + $"Apothecaries: {staff.Apothecaries}, Chaplains/Judiciars: {staff.Chaplains}";
        _overviewFunnel.Text =
            $"Eligible male cohort: {RecruitmentRateFormatter.FormatWeekly(snapshot.Forecast.EligibleMaleCohort)}\n"
            + $"Screened: {RecruitmentRateFormatter.FormatWeekly(snapshot.Forecast.ExpectedScreenedCandidates)} "
            + $"({snapshot.Forecast.ScreeningCoverage:P0} coverage)\n"
            + $"Qualified: {RecruitmentRateFormatter.FormatWeekly(snapshot.Forecast.ExpectedQualifiedCandidates)}\n"
            + $"Expected Phase 12 survivors: {RecruitmentRateFormatter.FormatWeekly(snapshot.Forecast.ExpectedPhase12Survivors)}\n"
            + $"Expected Battle Brothers: {RecruitmentRateFormatter.FormatWeekly(snapshot.Forecast.ExpectedPhase13BattleBrothers)}";
        _overviewCapacity.Text =
            $"Screening capacity: {snapshot.Forecast.ScreeningCapacity:N0}/week    "
            + $"Aspirant capacity: {snapshot.Forecast.AspirantTrainingCapacity:N0}    "
            + $"Waitlist: {snapshot.Forecast.QualifiedCandidateWaitlist:N0}    "
            + $"Unscreened backlog: {snapshot.Forecast.UnscreenedBacklog:N0}";
        ClearContainer(_overviewEvents);
        if (snapshot.RecentEvents.Count == 0)
        {
            Label empty = CreateBodyLabel();
            empty.Text = "No recruitment events have been recorded.";
            _overviewEvents.AddChild(empty);
        }
        else
        {
            foreach (string eventText in snapshot.RecentEvents)
            {
                Label eventLabel = CreateBodyLabel();
                eventLabel.Text = eventText;
                _overviewEvents.AddChild(eventLabel);
            }
        }
    }

    private void PopulateDoctrine(RecruitmentScreenSnapshot snapshot)
    {
        _suppressDoctrineEvents = true;
        SetDoctrineControls(snapshot.Doctrine);
        _suppressDoctrineEvents = false;
        _doctrineForecast.Text = FormatDoctrineForecast(snapshot.Forecast);
        _confirmDoctrineButton.Text = snapshot.IsSetupComplete
            ? "Apply Recruitment Doctrine"
            : "Establish Recruitment Program";
        if (!snapshot.Staff.IsComplete)
        {
            ShowSetupValidation(
                "Staffing incomplete: assign a Scout Sergeant, Apothecary, and "
                + "Chaplain or Judiciar to the 10th Company HQ on the Chapter screen.");
        }
        else
        {
            ShowSetupValidation(string.Empty);
        }
    }

    private void SetDoctrineControls(RecruitmentDoctrineDraft doctrine)
    {
        _policyOption.Select(
            _policyOption.GetItemIndex((int)doctrine.Policy));
        SetFilter("strength", doctrine.StrengthHalfSigmaSteps);
        SetFilter("constitution", doctrine.ConstitutionHalfSigmaSteps);
        SetFilter("intelligence", doctrine.IntelligenceHalfSigmaSteps);
        SetFilter("dexterity", doctrine.DexterityHalfSigmaSteps);
        SetFilter("ego", doctrine.EgoHalfSigmaSteps);
        _geneticSlider.Value = doctrine.MinimumGeneticCompatibility * 100;
        _geneticValue.Text = $"{_geneticSlider.Value:0}%";
    }

    private void SetFilter(string key, int value)
    {
        _filterSliders[key].Value = value;
        _filterValues[key].Text = FormatSigma(value);
    }

    private void RaiseDoctrineChanged()
    {
        if (_suppressDoctrineEvents)
        {
            return;
        }

        RecruitmentDoctrineDraft doctrine = new(
            (RecruitmentPolicy)_policyOption.GetSelectedId(),
            (int)_filterSliders["strength"].Value,
            (int)_filterSliders["constitution"].Value,
            (int)_filterSliders["intelligence"].Value,
            (int)_filterSliders["dexterity"].Value,
            (int)_filterSliders["ego"].Value,
            (float)(_geneticSlider.Value / 100.0));
        DoctrineChanged?.Invoke(this, doctrine);
    }

    private void PopulateCandidatesAndAspirants(
        IReadOnlyList<RecruitmentCandidateRow> candidates,
        IReadOnlyList<RecruitmentAspirantRow> aspirants)
    {
        _candidateSummary.Text = candidates.Count == 0
            ? "No candidates are currently waiting for admission."
            : $"{candidates.Count:N0} candidates await Phase 0 capacity.";
        ClearContainer(_candidateRows);
        foreach (RecruitmentCandidateRow candidate in candidates)
        {
            _candidateRows.AddChild(CreateDataRow(
                candidate.Designation,
                $"{candidate.Age}    Genetic match {candidate.GeneticCompatibility:P0}"));
        }

        _aspirantSummary.Text = aspirants.Count == 0
            ? "No aspirants are currently in development."
            : $"{aspirants.Count:N0} aspirants are in the implantation and training program.";
        ClearContainer(_aspirantRows);
        foreach (RecruitmentAspirantRow aspirant in aspirants)
        {
            HBoxContainer row = CreateDataRow(
                aspirant.Designation,
                $"{aspirant.Phase}    {aspirant.Age}    "
                + $"Training {aspirant.TrainingProgress:P0}");
            if (aspirant.CanBecomeNeophyte)
            {
                Button placeButton = new()
                {
                    Text = "Assign as Neophyte",
                    TooltipText =
                        "Choose an eligible Home World Scout squad and Chapter name.",
                    MouseDefaultCursorShape = CursorShape.PointingHand
                };
                int aspirantId = aspirant.Id;
                placeButton.Pressed += () =>
                    NeophytePlacementRequested?.Invoke(this, aspirantId);
                row.AddChild(placeButton);
            }
            _aspirantRows.AddChild(row);
        }
    }

    private void PopulatePromotionRows(IReadOnlyList<ScoutPromotionRow> rows)
    {
        ClearContainer(_promotionRows);
        IReadOnlyList<ScoutPromotionRow> ready = rows
            .Where(row => row.IsReady)
            .ToList();
        if (ready.Count == 0)
        {
            Label empty = CreateBodyLabel();
            empty.Text = "No Scouts in this squad are ready for the Black Carapace.";
            _promotionRows.AddChild(empty);
            return;
        }

        foreach (ScoutPromotionRow scout in ready)
        {
            HBoxContainer row = CreateDataRow(
                scout.Name,
                "Ready for one-week Phase 13 procedure");
            Button promoteButton = new()
            {
                Text = "Begin Phase 13",
                TooltipText =
                    "Select a Devastator posting and reserve an Apothecary procedure.",
                MouseDefaultCursorShape = CursorShape.PointingHand
            };
            int soldierId = scout.SoldierId;
            promoteButton.Pressed += () =>
                Phase13PromotionRequested?.Invoke(this, soldierId);
            row.AddChild(promoteButton);
            _promotionRows.AddChild(row);
        }
    }

    private void SelectPage(RecruitmentPage page)
    {
        if (!_pages.ContainsKey(page)
            || (_navigationButtons.TryGetValue(page, out Button nav) && nav.Disabled))
        {
            return;
        }

        _activePage = page;
        foreach ((RecruitmentPage key, Control value) in _pages)
        {
            value.Visible = key == page;
        }
        foreach ((RecruitmentPage key, Button value) in _navigationButtons)
        {
            value.ButtonPressed = key == page;
        }
    }

    private VBoxContainer CreateScrollablePage(Control host, RecruitmentPage page)
    {
        Control root = CreatePage(host, page);
        ScrollContainer scroll = new();
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddChild(scroll);
        VBoxContainer content = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        content.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(content);
        return content;
    }

    private Control CreatePage(Control host, RecruitmentPage page)
    {
        MarginContainer root = new()
        {
            Visible = false
        };
        root.AddThemeConstantOverride("margin_left", 18);
        root.AddThemeConstantOverride("margin_top", 16);
        root.AddThemeConstantOverride("margin_right", 18);
        root.AddThemeConstantOverride("margin_bottom", 16);
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        host.AddChild(root);
        _pages[page] = root;
        return root;
    }

    private void AddSquad(ScoutSquadRow squad, bool selected)
    {
        Button squadButton = new()
        {
            Text = squad.Label,
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, 52),
            TooltipText = squad.Label,
            MouseDefaultCursorShape = CursorShape.PointingHand,
            ToggleMode = true,
            ButtonGroup = _squadButtonGroup,
            ButtonPressed = selected
        };
        squadButton.Pressed += () =>
            SquadButtonPressed?.Invoke(this, squad.Id);
        squadButton.Toggled += isPressed =>
            OnlyWarStyle.ApplyListRow(squadButton, isPressed);
        OnlyWarStyle.ApplyListRow(squadButton, selected);
        IconAtlas.Apply(squadButton, "scout");
        _squadVBox.AddChild(squadButton);
    }

    private void PopulateFocusOptions()
    {
        _focusOption.Clear();
        _focusOption.AddItem("Balanced", (int)TrainingFocuses.None);
        _focusOption.AddItem("Physical", (int)TrainingFocuses.Physical);
        _focusOption.AddItem("Vehicles", (int)TrainingFocuses.Vehicles);
        _focusOption.AddItem("Melee", (int)TrainingFocuses.Melee);
        _focusOption.AddItem("Ranged", (int)TrainingFocuses.Ranged);
    }

    private void OnFocusOptionSelected(long index)
    {
        TrainingFocusSelected?.Invoke(
            this,
            (TrainingFocuses)_focusOption.GetItemId((int)index));
    }

    private static HBoxContainer CreateDataRow(string title, string detail)
    {
        HBoxContainer row = new()
        {
            CustomMinimumSize = new Vector2(0, 42)
        };
        Label titleLabel = CreateFieldLabel(title, 260);
        titleLabel.AddThemeColorOverride(
            "font_color", new Color(0.96f, 0.84f, 0.52f));
        row.AddChild(titleLabel);
        Label detailLabel = CreateBodyLabel();
        detailLabel.Text = detail;
        detailLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(detailLabel);
        return row;
    }

    private static Label CreateSectionTitle(string text)
    {
        Label label = new()
        {
            Text = text,
            CustomMinimumSize = new Vector2(0, 32)
        };
        label.AddThemeColorOverride(
            "font_color", new Color(0.96f, 0.84f, 0.52f));
        label.AddThemeFontSizeOverride("font_size", 20);
        return label;
    }

    private static Label CreateBodyLabel()
    {
        return new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
    }

    private static Label CreateFieldLabel(string text, float width)
    {
        return new Label
        {
            Text = text,
            CustomMinimumSize = new Vector2(width, 34),
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static string FormatSigma(int halfSteps)
    {
        double sigma = halfSteps * RecruitmentRules.AttributeFilterStepSigma;
        return $"{sigma.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture)}σ";
    }

    private static string FormatDoctrineForecast(RecruitmentForecast forecast)
    {
        return
            $"Screening coverage: {forecast.ScreeningCoverage:P0}    "
            + $"Public compliance: {forecast.PublicCompliance:P0}    "
            + $"Attribute pass rate: {forecast.AttributePassRate:P2}\n"
            + $"Qualified candidates: "
            + $"{RecruitmentRateFormatter.FormatWeekly(forecast.ExpectedQualifiedCandidates)}    "
            + $"Phase 12 survivors: "
            + $"{RecruitmentRateFormatter.FormatWeekly(forecast.ExpectedPhase12Survivors)}    "
            + $"Battle Brothers: "
            + $"{RecruitmentRateFormatter.FormatWeekly(forecast.ExpectedPhase13BattleBrothers)}\n"
            + $"Weekly Requisition: {forecast.WeeklyRequisitionCost:N0}    "
            + $"Expected overflow: {forecast.ExpectedCandidateOverflow:0.##}";
    }

    private static void ClearContainer(Container container)
    {
        foreach (Node child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }
}
