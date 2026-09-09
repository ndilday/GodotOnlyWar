using Godot;
using OnlyWar.Application;
using OnlyWar.Domain.Equippables;
using System;
using System.Collections.Generic;

/// <summary>
/// Shared modal editor for chapter defaults and sparse planetary theater overrides. Every
/// doctrine fact and every write goes through <see cref="ILoadoutScreenApplication"/>; the dialog
/// owns only the staged edit the player has not saved yet.
/// </summary>
public partial class LoadoutDoctrineDialog : Control
{
    private ILoadoutScreenApplication _application;
    private int? _planetId;
    private LoadoutDoctrineScopeView _scope;
    private int? _selectedTemplateId;
    private VBoxContainer _templateList;
    private Label _title;
    private Label _subtitle;
    private Label _selectionTitle;
    private Label _selectionSource;
    private ElementLoadoutEditorView _editor;
    private Button _saveButton;
    private Button _inheritButton;
    private ElementLoadoutEditorView _characterEditor;
    private ScrollContainer _characterScroll;
    private HBoxContainer _modeRow;
    private Button _squadModeButton;
    private Button _characterModeButton;
    private Button _doctrineModeButton;
    private EquipmentLoadoutEditorView _equipmentEditor;
    private PanelContainer _listPanel;
    private VBoxContainer _squadEditorStack;
    private HBoxContainer _footer;
    private int? _editingRoleId;
    // Characters are equipped by role rather than by squad type, so they get their own mode
    // instead of a row in the squad-template list. Chapter scope only: there is no theater tier
    // for characters (see CharacterLoadoutDoctrine).
    private bool _charactersMode;
    private bool _doctrineMode;
    private VBoxContainer _doctrineEditorStack;
    private OptionButton _injuryThresholdButton;
    private CheckButton _requireLeaderButton;
    private SpinBox _minimumStrengthSpinBox;
    private Label _doctrineSummary;
    // The staged operational doctrine the player is editing but has not saved.
    private int _stagedInjuryThresholdIndex;
    private bool _stagedRequireLeader;
    private int _stagedMinimumStrength = 1;
    private bool _isPopulatingDoctrine;

    public event EventHandler DoctrineChanged;

    public void Configure(ILoadoutScreenApplication application)
    {
        _application = application;
    }

    public override void _Ready()
    {
        AddToGroup(DialogController.DialogInputBlockerGroup);
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ZIndex = 80;

        ColorRect scrim = new()
        {
            Color = new Color(0, 0, 0, 0.68f),
            MouseFilter = MouseFilterEnum.Stop,
            AnchorRight = 1,
            AnchorBottom = 1
        };
        AddChild(scrim);

        PanelContainer dialog = new()
        {
            AnchorLeft = 0.16f,
            AnchorTop = 0.075f,
            AnchorRight = 0.84f,
            AnchorBottom = 0.925f
        };
        OnlyWarStyle.ApplyContentPanel(dialog);
        AddChild(dialog);

        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", 18);
        margin.AddThemeConstantOverride("margin_top", 16);
        margin.AddThemeConstantOverride("margin_right", 18);
        margin.AddThemeConstantOverride("margin_bottom", 16);
        dialog.AddChild(margin);

        VBoxContainer outer = new();
        outer.AddThemeConstantOverride("separation", 12);
        margin.AddChild(outer);

        HBoxContainer header = new();
        VBoxContainer heading = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _title = new Label();
        _title.AddThemeFontOverride("font", GetThemeFont("display"));
        _title.AddThemeFontSizeOverride("font_size", 24);
        _subtitle = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _subtitle.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        heading.AddChild(_title);
        heading.AddChild(_subtitle);
        header.AddChild(heading);
        Button close = new() { CustomMinimumSize = new Vector2(40, 36), Text = "X" };
        IconAtlas.ApplyIconButton(close, "close", 40, 28);
        close.Pressed += () => Hide();
        header.AddChild(close);
        outer.AddChild(header);

        _modeRow = new HBoxContainer();
        _modeRow.AddThemeConstantOverride("separation", 8);
        _squadModeButton = new Button { Text = "Squad Types", CustomMinimumSize = new Vector2(150, 34) };
        _characterModeButton = new Button { Text = "Characters", CustomMinimumSize = new Vector2(150, 34) };
        _doctrineModeButton = new Button { Text = "Doctrine", CustomMinimumSize = new Vector2(150, 34) };
        _squadModeButton.Pressed += () => SetMode(false, false);
        _characterModeButton.Pressed += () => SetMode(true, false);
        _doctrineModeButton.Pressed += () => SetMode(false, true);
        _modeRow.AddChild(_squadModeButton);
        _modeRow.AddChild(_characterModeButton);
        _modeRow.AddChild(_doctrineModeButton);
        outer.AddChild(_modeRow);

        HBoxContainer content = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 12);
        outer.AddChild(content);

        PanelContainer listPanel = new() { CustomMinimumSize = new Vector2(285, 0) };
        _listPanel = listPanel;
        OnlyWarStyle.ApplyInsetPanel(listPanel);
        ScrollContainer listScroll = new() { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _templateList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _templateList.AddThemeConstantOverride("separation", 6);
        listScroll.AddChild(_templateList);
        listPanel.AddChild(listScroll);
        content.AddChild(listPanel);

        PanelContainer editorPanel = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        OnlyWarStyle.ApplyInsetPanel(editorPanel);
        MarginContainer editorMargin = new();
        editorMargin.AddThemeConstantOverride("margin_left", 14);
        editorMargin.AddThemeConstantOverride("margin_top", 12);
        editorMargin.AddThemeConstantOverride("margin_right", 14);
        editorMargin.AddThemeConstantOverride("margin_bottom", 12);
        editorPanel.AddChild(editorMargin);
        // Both modes render into the same panel, so they share one container and swap visibility.
        VBoxContainer editorRoot = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
        editorMargin.AddChild(editorRoot);
        VBoxContainer editorStack = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
        editorStack.AddThemeConstantOverride("separation", 8);
        editorRoot.AddChild(editorStack);
        _squadEditorStack = editorStack;
        _selectionTitle = new Label();
        _selectionTitle.AddThemeFontOverride("font", GetThemeFont("display"));
        _selectionTitle.AddThemeFontSizeOverride("font_size", 20);
        _selectionSource = new Label();
        _selectionSource.AddThemeColorOverride("font_color", OnlyWarStyle.PlayerAccent);
        editorStack.AddChild(_selectionTitle);
        editorStack.AddChild(_selectionSource);
        ScrollContainer editorScroll = new()
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _editor = new ElementLoadoutEditorView { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        editorScroll.AddChild(_editor);
        editorStack.AddChild(editorScroll);

        ScrollContainer characterScroll = new()
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Visible = false
        };
        _characterEditor = new ElementLoadoutEditorView
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CharacterCaptionText = "CHAPTER STANDARD BY ROLE"
        };
        _characterEditor.CharacterSelectionChanged += OnCharacterRoleSelected;
        _characterEditor.CharacterResetRequested += OnCharacterRoleReset;
        _characterEditor.CharacterCustomizeRequested += OnCharacterRoleCustomize;
        characterScroll.AddChild(_characterEditor);
        editorRoot.AddChild(characterScroll);
        _characterScroll = characterScroll;
        content.AddChild(editorPanel);

        _doctrineEditorStack = BuildOperationalDoctrineEditor();
        editorRoot.AddChild(_doctrineEditorStack);

        HBoxContainer footer = new() { Alignment = BoxContainer.AlignmentMode.End };
        _footer = footer;
        footer.AddThemeConstantOverride("separation", 8);
        _inheritButton = new Button
        {
            Text = "Inherit Chapter",
            CustomMinimumSize = new Vector2(160, 38)
        };
        _inheritButton.Pressed += OnInheritPressed;
        _saveButton = new Button { CustomMinimumSize = new Vector2(190, 38) };
        _saveButton.Pressed += OnSavePressed;
        footer.AddChild(_inheritButton);
        footer.AddChild(_saveButton);
        outer.AddChild(footer);

        _equipmentEditor = new EquipmentLoadoutEditorView();
        _equipmentEditor.SaveRequested += OnEquipmentLoadoutSaved;
        AddChild(_equipmentEditor);

        Visible = false;
    }

    public void OpenChapter() => Open(null);

    public void OpenPlanet(int planetId) => Open(planetId);

    private void Open(int? planetId)
    {
        _planetId = planetId;
        _scope = _application?.QueryDoctrineScope(planetId);
        if (_scope == null || !_scope.IsAvailable) return;

        _saveButton.Text = _scope.SaveButtonText;
        _inheritButton.Visible = planetId != null;
        // Characters have no theater tier, so the mode switch only appears at chapter scope.
        _modeRow.Visible = planetId == null;
        SetMode(_charactersMode && planetId == null, _doctrineMode && planetId == null);
        Visible = true;
    }

    private void SetMode(bool charactersMode, bool doctrineMode)
    {
        if (_scope == null) return;
        _charactersMode = charactersMode;
        _doctrineMode = doctrineMode && _planetId == null;
        _saveButton.Text = _doctrineMode ? "Save Operational Doctrine" : _scope.SaveButtonText;
        OnlyWarStyle.ApplyListRow(_squadModeButton, !charactersMode);
        OnlyWarStyle.ApplyListRow(_characterModeButton, charactersMode);
        OnlyWarStyle.ApplyListRow(_doctrineModeButton, _doctrineMode);

        _title.Text = _scope.Title;
        _subtitle.Text = _doctrineMode
            ? _scope.DoctrineModeSubtitle
            : charactersMode ? _scope.CharacterModeSubtitle : _scope.SquadModeSubtitle;

        _listPanel.Visible = !charactersMode && !_doctrineMode;
        _squadEditorStack.Visible = !charactersMode && !_doctrineMode;
        _characterScroll.Visible = charactersMode && !_doctrineMode;
        _doctrineEditorStack.Visible = _doctrineMode;
        // Character picks apply on selection; squad and Doctrine modes stage an edit to be saved.
        _footer.Visible = !charactersMode;
        _inheritButton.Visible = !_doctrineMode && _planetId != null;
        if (_doctrineMode)
        {
            LoadStagedDoctrine();
        }
        else if (charactersMode)
        {
            PopulateCharacterRoles();
        }
        else
        {
            PopulateTemplateList();
        }
    }

    private VBoxContainer BuildOperationalDoctrineEditor()
    {
        VBoxContainer stack = new()
        {
            Visible = false,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        stack.AddThemeConstantOverride("separation", 12);

        Label heading = new()
        {
            Text = "UNFIT FOR DUTY",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        heading.AddThemeFontOverride("font", GetThemeFont("display"));
        heading.AddThemeFontSizeOverride("font_size", 20);
        stack.AddChild(heading);

        Label explanation = new()
        {
            Text = "The threshold is inclusive and uses the soldier's worst active wound band. "
                + "Incapacitated removes only the extra wound restriction; incapacitated soldiers, "
                + "procedure reservations, untreated severances, and fewer than two functioning arms "
                + "remain unavailable.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        explanation.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        stack.AddChild(explanation);

        _injuryThresholdButton = new OptionButton
        {
            TooltipText = "Withhold soldiers at or above this inclusive worst-wound band."
        };
        _injuryThresholdButton.ItemSelected += index =>
        {
            if (_isPopulatingDoctrine) return;
            _stagedInjuryThresholdIndex = (int)index;
            RefreshDoctrineSummary();
        };
        stack.AddChild(LabeledControl("Injury threshold", _injuryThresholdButton));

        _requireLeaderButton = new CheckButton
        {
            Text = "Require a duty-ready squad leader",
            TooltipText = "A squad without its required duty-ready leader cannot deploy."
        };
        _requireLeaderButton.Toggled += enabled =>
        {
            if (_isPopulatingDoctrine) return;
            _stagedRequireLeader = enabled;
            RefreshDoctrineSummary();
        };
        stack.AddChild(_requireLeaderButton);

        _minimumStrengthSpinBox = new SpinBox
        {
            MinValue = 1,
            MaxValue = 100,
            Step = 1,
            AllowLesser = false,
            TooltipText = "A squad needs this many duty-ready members. The leader counts toward the total."
        };
        _minimumStrengthSpinBox.ValueChanged += value =>
        {
            if (_isPopulatingDoctrine) return;
            _stagedMinimumStrength = (int)value;
            RefreshDoctrineSummary();
        };
        stack.AddChild(LabeledControl("Minimum squad strength", _minimumStrengthSpinBox));

        _doctrineSummary = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _doctrineSummary.AddThemeColorOverride("font_color", OnlyWarStyle.PlayerAccent);
        stack.AddChild(_doctrineSummary);
        return stack;
    }

    private static HBoxContainer LabeledControl(string labelText, Control control)
    {
        HBoxContainer row = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 12);
        Label label = new()
        {
            Text = labelText,
            CustomMinimumSize = new Vector2(220, 0),
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        row.AddChild(label);
        row.AddChild(control);
        return row;
    }

    private void LoadStagedDoctrine()
    {
        OperationalDoctrineView doctrine = _application?.QueryOperationalDoctrine();
        if (doctrine == null) return;

        _isPopulatingDoctrine = true;
        _injuryThresholdButton.Clear();
        foreach (string label in doctrine.InjuryThresholdLabels)
        {
            _injuryThresholdButton.AddItem(label);
        }
        _stagedInjuryThresholdIndex = doctrine.InjuryThresholdIndex;
        _stagedRequireLeader = doctrine.RequireDutyReadySquadLeader;
        _stagedMinimumStrength = doctrine.MinimumDutyReadySquadStrength;
        _injuryThresholdButton.Select(_stagedInjuryThresholdIndex);
        _requireLeaderButton.ButtonPressed = _stagedRequireLeader;
        _minimumStrengthSpinBox.Value = _stagedMinimumStrength;
        _isPopulatingDoctrine = false;
        RefreshDoctrineSummary();
    }

    private void RefreshDoctrineSummary()
    {
        if (_doctrineSummary == null || _application == null) return;
        _doctrineSummary.Text = _application.DescribeOperationalDoctrineConsequence(
            _stagedInjuryThresholdIndex, _stagedRequireLeader, _stagedMinimumStrength);
    }

    private void PopulateCharacterRoles()
    {
        _characterEditor.SetData(_application?.QueryCharacterRoles() ?? [], [], []);
    }

    private void OnCharacterRoleSelected(object sender, (int Key, WeaponSet WeaponSet) change)
    {
        if (_application?.SetCharacterRoleWeaponSet(
            _application.SessionToken, change.Key, change.WeaponSet)?.Succeeded == true)
        {
            DoctrineChanged?.Invoke(this, EventArgs.Empty);
        }
        PopulateCharacterRoles();
    }

    private void OnCharacterRoleReset(object sender, int roleId)
    {
        if (_application?.ResetCharacterRole(_application.SessionToken, roleId)?.Succeeded == true)
        {
            DoctrineChanged?.Invoke(this, EventArgs.Empty);
        }
        PopulateCharacterRoles();
    }

    private void OnCharacterRoleCustomize(object sender, int roleId)
    {
        EquipmentEditorView editor = _application?.QueryRoleEquipmentEditor(roleId);
        if (editor?.IsAvailable != true) return;

        _editingRoleId = roleId;
        _equipmentEditor.Open(
            editor.Title,
            editor.Subtitle,
            editor.Catalog,
            editor.Loadout,
            editor.Context,
            editor.Catalog.EquipmentKits.Values);
    }

    private void OnEquipmentLoadoutSaved(EquipmentLoadout loadout)
    {
        if (!_editingRoleId.HasValue || _application == null) return;

        LoadoutCommandResult result = _application.SaveRoleEquipment(
            _application.SessionToken, _editingRoleId.Value, loadout);
        _editingRoleId = null;
        if (!result.Succeeded)
        {
            GD.PushWarning(result.Message);
            return;
        }

        DoctrineChanged?.Invoke(this, EventArgs.Empty);
        PopulateCharacterRoles();
    }

    private void PopulateTemplateList()
    {
        foreach (Node child in _templateList.GetChildren())
        {
            _templateList.RemoveChild(child);
            child.QueueFree();
        }

        _scope = _application?.QueryDoctrineScope(_planetId) ?? _scope;
        IReadOnlyList<LoadoutTemplateOption> templates = _scope?.Templates ?? [];
        bool selectionStillListed = false;
        foreach (LoadoutTemplateOption template in templates)
        {
            selectionStillListed |= template.TemplateId == _selectedTemplateId;
        }
        if (!selectionStillListed)
        {
            _selectedTemplateId = templates.Count > 0 ? templates[0].TemplateId : null;
        }

        foreach (LoadoutTemplateOption template in templates)
        {
            Button button = new()
            {
                Text = template.Label,
                Alignment = HorizontalAlignment.Left,
                CustomMinimumSize = new Vector2(0, 48),
                TooltipText = template.Label
            };
            OnlyWarStyle.ApplyListRow(button, template.TemplateId == _selectedTemplateId);
            int templateId = template.TemplateId;
            button.Pressed += () => SelectTemplate(templateId);
            _templateList.AddChild(button);
        }
        ShowSelectedTemplate();
    }

    private void SelectTemplate(int templateId)
    {
        _selectedTemplateId = templateId;
        PopulateTemplateList();
    }

    private void ShowSelectedTemplate()
    {
        LoadoutTemplateDetailView detail = _selectedTemplateId.HasValue
            ? _application?.QueryTemplateLoadout(_planetId, _selectedTemplateId.Value)
            : null;
        if (detail?.Exists != true)
        {
            _selectionTitle.Text = LoadoutTemplateDetailView.Missing.Name;
            _selectionSource.Text = "";
            _saveButton.Disabled = true;
            return;
        }

        _saveButton.Disabled = false;
        _selectionTitle.Text = detail.Name;
        _selectionSource.Text = detail.SourceText;
        _editor.SetData([], detail.CountSections, detail.Loadout);
        _inheritButton.Disabled = !detail.CanInherit;
    }

    private void OnSavePressed()
    {
        if (_application == null) return;
        if (_doctrineMode)
        {
            if (_application.SaveOperationalDoctrine(
                _application.SessionToken,
                _stagedInjuryThresholdIndex,
                _stagedRequireLeader,
                _stagedMinimumStrength).Succeeded)
            {
                DoctrineChanged?.Invoke(this, EventArgs.Empty);
            }
            LoadStagedDoctrine();
            return;
        }

        if (!_selectedTemplateId.HasValue) return;
        if (_application.SaveTemplateLoadout(
            _application.SessionToken,
            _planetId,
            _selectedTemplateId.Value,
            _editor.WorkingLoadout).Succeeded)
        {
            DoctrineChanged?.Invoke(this, EventArgs.Empty);
        }
        PopulateTemplateList();
    }

    private void OnInheritPressed()
    {
        if (_application == null || !_planetId.HasValue || !_selectedTemplateId.HasValue) return;
        if (_application.InheritTemplateLoadout(
            _application.SessionToken, _planetId.Value, _selectedTemplateId.Value).Succeeded)
        {
            DoctrineChanged?.Invoke(this, EventArgs.Empty);
        }
        PopulateTemplateList();
    }
}
