using Godot;
using OnlyWar.Helpers;
using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Shared modal editor for chapter defaults and sparse planetary theater overrides.
/// </summary>
public partial class LoadoutDoctrineDialog : Control
{
    private PlayerForce _force;
    private Planet _planet;
    private LoadoutDoctrine _doctrine;
    private SquadTemplate _selectedTemplate;
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
    private PanelContainer _listPanel;
    private VBoxContainer _squadEditorStack;
    private HBoxContainer _footer;
    // Characters mode rows are keyed by SoldierTemplate.Id (CharacterLoadoutDoctrine's own key),
    // but CharacterLoadoutService.SetRoleDefault needs the element itself — this recovers it
    // when a row fires its key back on selection/reset.
    private Dictionary<int, SquadTemplateElement> _characterRoleElements = new();
    // Characters are equipped by role rather than by squad type, so they get their own mode
    // instead of a row in the squad-template list. Chapter scope only: there is no theater tier
    // for characters (see CharacterLoadoutDoctrine).
    private bool _charactersMode;

    public event EventHandler DoctrineChanged;

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
        _squadModeButton.Pressed += () => SetMode(false);
        _characterModeButton.Pressed += () => SetMode(true);
        _modeRow.AddChild(_squadModeButton);
        _modeRow.AddChild(_characterModeButton);
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
        characterScroll.AddChild(_characterEditor);
        editorRoot.AddChild(characterScroll);
        _characterScroll = characterScroll;
        content.AddChild(editorPanel);

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

        Visible = false;
    }

    public void OpenChapter(PlayerForce force)
    {
        Open(force, null, force?.Army?.LoadoutDoctrine);
    }

    public void OpenPlanet(PlayerForce force, Planet planet)
    {
        Open(force, planet, planet?.LoadoutDoctrine);
    }

    private void Open(PlayerForce force, Planet planet, LoadoutDoctrine doctrine)
    {
        _force = force;
        _planet = planet;
        _doctrine = doctrine;
        _saveButton.Text = planet == null ? "Save Chapter Default" : "Save Theater Override";
        _inheritButton.Visible = planet != null;
        // Characters have no theater tier, so the mode switch only appears at chapter scope.
        _modeRow.Visible = planet == null;
        SetMode(_charactersMode && planet == null);
        Visible = true;
    }

    private void SetMode(bool charactersMode)
    {
        _charactersMode = charactersMode;
        OnlyWarStyle.ApplyListRow(_squadModeButton, !charactersMode);
        OnlyWarStyle.ApplyListRow(_characterModeButton, charactersMode);

        _title.Text = _planet == null ? "Chapter Loadouts" : $"{_planet.Name} Theater Loadouts";
        if (charactersMode)
        {
            _subtitle.Text = "Set the chapter-wide kit for each command and specialist role. "
                + "Individuals equipped from their squad screen keep their personal loadout.";
        }
        else
        {
            _subtitle.Text = _planet == null
                ? "Set the chapter-wide baseline for each squad type. Squads with a theater override or custom loadout are unaffected."
                : "Create only the overrides this theater needs. Unmodified squad types continue to inherit chapter doctrine.";
        }

        _listPanel.Visible = !charactersMode;
        _squadEditorStack.Visible = !charactersMode;
        _characterScroll.Visible = charactersMode;
        // Character picks apply on selection; only the squad editor stages an edit to be saved.
        _footer.Visible = !charactersMode;

        if (charactersMode)
        {
            PopulateCharacterRoles();
        }
        else
        {
            PopulateTemplateList();
        }
    }

    // Every character role the chapter actually fields, gathered from the order of battle so a
    // chapter without (say) a Judiciar never shows one. Administrative formations are excluded
    // for the same reason the squad list excludes them: they never deploy as units, so a combat
    // loadout for them is noise. Roles they share with a company HQ (Apothecary, Chaplain,
    // Judiciar) still appear via that HQ; only roles unique to a Chapter office drop out. Grouped
    // by SoldierTemplate.Id (not element) because that's what CharacterLoadoutDoctrine.RoleDefaults
    // is keyed by — the same "Sergeant" role fielded by three squad types is one row, one
    // default, not three.
    private void PopulateCharacterRoles()
    {
        List<SquadTemplateElement> elements = _force?.Army?.OrderOfBattle?.GetAllSquads()
            .Where(squad => squad.IsOperational)
            .SelectMany(squad => squad.SquadTemplate.Elements)
            .Where(element => element.TryGetQuota(CharacterLoadoutService.CommandWeaponGroup, out _))
            .GroupBy(element => element.SoldierTemplate.Id)
            .Select(group => group.First())
            .OrderByDescending(element => element.SoldierTemplate.Rank)
            .ThenBy(element => element.SoldierTemplate.Name, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        _characterRoleElements = elements.ToDictionary(element => element.SoldierTemplate.Id);

        CharacterLoadoutDoctrine doctrine = _force?.Army?.CharacterLoadoutDoctrine;
        _characterEditor.SetData(elements.Select(element =>
        {
            EffectiveCharacterLoadout resolved = CharacterLoadoutService.ResolveRole(element, _force);
            return new CharacterLoadoutRowData(
                element.SoldierTemplate.Id,
                element.SoldierTemplate.Name,
                CharacterLoadoutService.DescribeSource(resolved),
                element.GetMenu(CharacterLoadoutService.CommandWeaponGroup),
                resolved?.WeaponSet,
                doctrine?.RoleDefaults.ContainsKey(element.SoldierTemplate.Id) == true);
        }).ToList(), [], []);
    }

    private void OnCharacterRoleSelected(object sender, (int Key, WeaponSet WeaponSet) change)
    {
        if (_characterRoleElements.TryGetValue(change.Key, out SquadTemplateElement element))
        {
            CharacterLoadoutService.SetRoleDefault(element, change.WeaponSet, _force);
            DoctrineChanged?.Invoke(this, EventArgs.Empty);
        }
        PopulateCharacterRoles();
    }

    private void OnCharacterRoleReset(object sender, int soldierTemplateId)
    {
        if (_force?.Army?.CharacterLoadoutDoctrine.ClearRoleDefault(soldierTemplateId) == true)
        {
            DoctrineChanged?.Invoke(this, EventArgs.Empty);
        }
        PopulateCharacterRoles();
    }

    private void PopulateTemplateList()
    {
        foreach (Node child in _templateList.GetChildren())
        {
            _templateList.RemoveChild(child);
            child.QueueFree();
        }

        List<SquadTemplate> templates = _force?.Army?.OrderOfBattle?.GetAllSquads()
            // A squad type belongs here if it has any pooled group to spend, which is every group
            // other than Command Weapon — not every group with a maximum above 1. Tactical Squad's
            // two groups are both (0,1) and it is still very much a squad type worth a doctrine.
            .Where(squad => squad.IsOperational
                && squad.SquadTemplate.Elements.Any(
                    element => element.Quotas.Any(
                        quota => quota.OptionGroup != CharacterLoadoutService.CommandWeaponGroup)))
            .Select(squad => squad.SquadTemplate)
            .GroupBy(template => template.Id)
            .Select(group => group.First())
            .OrderBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        _selectedTemplate = templates.FirstOrDefault(template => template.Id == _selectedTemplate?.Id)
            ?? templates.FirstOrDefault();
        foreach (SquadTemplate template in templates)
        {
            bool configured = _doctrine?.Loadouts.ContainsKey(template.Id) == true;
            Button button = new()
            {
                Text = _planet != null && !configured ? $"{template.Name}\nInherits chapter" : template.Name,
                Alignment = HorizontalAlignment.Left,
                CustomMinimumSize = new Vector2(0, 48),
                TooltipText = template.Name
            };
            OnlyWarStyle.ApplyListRow(button, template.Id == _selectedTemplate?.Id);
            button.Pressed += () => SelectTemplate(template);
            _templateList.AddChild(button);
        }
        ShowSelectedTemplate();
    }

    private void SelectTemplate(SquadTemplate template)
    {
        _selectedTemplate = template;
        PopulateTemplateList();
    }

    private void ShowSelectedTemplate()
    {
        if (_selectedTemplate == null)
        {
            _selectionTitle.Text = "No operational squad types";
            _selectionSource.Text = "";
            _saveButton.Disabled = true;
            return;
        }

        _saveButton.Disabled = false;
        bool hasLocal = _doctrine.TryGetLoadout(_selectedTemplate.Id, out IReadOnlyList<WeaponSet> loadout);
        if (!hasLocal && _planet != null)
        {
            hasLocal = _force.Army.LoadoutDoctrine.TryGetLoadout(
                _selectedTemplate.Id, out loadout);
        }
        loadout ??= [];
        _selectionTitle.Text = _selectedTemplate.Name;
        _selectionSource.Text = _planet == null
            ? (_doctrine.Loadouts.ContainsKey(_selectedTemplate.Id)
                ? "Explicit chapter default"
                : "Template standard; save to establish a chapter default")
            : (_planet.LoadoutDoctrine.Loadouts.ContainsKey(_selectedTemplate.Id)
                ? "Planetary theater override"
                : "Inherited from chapter doctrine");
        // No live roster at template scope, so capacity is each element's own MaximumNumber
        // rather than an able-bodied count.
        _editor.SetData(
            [],
            ElementLoadoutSections.Build(_selectedTemplate, element => element.MaximumNumber),
            loadout);
        _inheritButton.Disabled = _planet == null
            || !_planet.LoadoutDoctrine.Loadouts.ContainsKey(_selectedTemplate.Id);
    }

    private void OnSavePressed()
    {
        if (_selectedTemplate == null) return;
        _doctrine.SetLoadout(_selectedTemplate.Id, _editor.WorkingLoadout);
        DoctrineChanged?.Invoke(this, EventArgs.Empty);
        PopulateTemplateList();
    }

    private void OnInheritPressed()
    {
        if (_planet == null || _selectedTemplate == null) return;
        if (_planet.LoadoutDoctrine.RemoveLoadout(_selectedTemplate.Id))
        {
            DoctrineChanged?.Invoke(this, EventArgs.Empty);
        }
        PopulateTemplateList();
    }
}
