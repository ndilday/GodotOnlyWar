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
    private LoadoutEditorView _editor;
    private Button _saveButton;
    private Button _inheritButton;

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

        HBoxContainer content = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 12);
        outer.AddChild(content);

        PanelContainer listPanel = new() { CustomMinimumSize = new Vector2(285, 0) };
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
        VBoxContainer editorStack = new();
        editorStack.AddThemeConstantOverride("separation", 8);
        editorMargin.AddChild(editorStack);
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
        _editor = new LoadoutEditorView { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        editorScroll.AddChild(_editor);
        editorStack.AddChild(editorScroll);
        content.AddChild(editorPanel);

        HBoxContainer footer = new() { Alignment = BoxContainer.AlignmentMode.End };
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
        _title.Text = planet == null ? "Chapter Loadouts" : $"{planet.Name} Theater Loadouts";
        _subtitle.Text = planet == null
            ? "Set the chapter-wide baseline for each squad type. Squads with a theater override or custom loadout are unaffected."
            : "Create only the overrides this theater needs. Unmodified squad types continue to inherit chapter doctrine.";
        _saveButton.Text = planet == null ? "Save Chapter Default" : "Save Theater Override";
        _inheritButton.Visible = planet != null;
        PopulateTemplateList();
        Visible = true;
    }

    private void PopulateTemplateList()
    {
        foreach (Node child in _templateList.GetChildren())
        {
            _templateList.RemoveChild(child);
            child.QueueFree();
        }

        List<SquadTemplate> templates = _force?.Army?.OrderOfBattle?.GetAllSquads()
            .Where(squad => squad.IsOperational
                && squad.SquadTemplate.WeaponOptions?.Any() == true)
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
        int maximumSize = _selectedTemplate.Elements.Sum(element => (int)element.MaximumNumber);
        _selectionTitle.Text = _selectedTemplate.Name;
        _selectionSource.Text = _planet == null
            ? (_doctrine.Loadouts.ContainsKey(_selectedTemplate.Id)
                ? "Explicit chapter default"
                : "Template standard; save to establish a chapter default")
            : (_planet.LoadoutDoctrine.Loadouts.ContainsKey(_selectedTemplate.Id)
                ? "Planetary theater override"
                : "Inherited from chapter doctrine");
        _editor.SetLoadout(_selectedTemplate, loadout, maximumSize);
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
