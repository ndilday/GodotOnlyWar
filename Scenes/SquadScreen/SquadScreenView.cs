using Godot;
using OnlyWar.Helpers.UI;
using OnlyWar.Models.Equippables;
using System;
using System.Collections.Generic;

public partial class SquadScreenView : MainScreenView
{
    private Label _title;
    private Label _subtitle;
    private Label _source;
    private Button _returnToDoctrine;
    private ElementLoadoutEditorView _editor;
    private EquipmentLoadoutEditorView _equipmentEditor;

    public event EventHandler LoadoutChanged;
    public event EventHandler ReturnToDoctrinePressed;
    public event EventHandler ClosePressed;
    public event EventHandler<(int SoldierId, WeaponSet WeaponSet)> CharacterLoadoutSelected;
    public event EventHandler<int> CharacterLoadoutReset;
    public event EventHandler<int> CharacterCustomizeRequested;
    public event Action<EquipmentLoadout> EquipmentLoadoutSaveRequested;

    public IReadOnlyList<WeaponSet> WorkingLoadout => _editor.WorkingLoadout;

    public override void _Ready()
    {
        base._Ready();
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }

        ColorRect scrim = new()
        {
            Color = new Color(0, 0, 0, 0.62f),
            MouseFilter = MouseFilterEnum.Stop,
            AnchorRight = 1,
            AnchorBottom = 1
        };
        AddChild(scrim);

        PanelContainer dialog = new()
        {
            AnchorLeft = 0.29f,
            AnchorTop = 0.10f,
            AnchorRight = 0.71f,
            AnchorBottom = 0.90f,
            OffsetLeft = 0,
            OffsetTop = 0,
            OffsetRight = 0,
            OffsetBottom = 0
        };
        OnlyWarStyle.ApplyContentPanel(dialog);
        AddChild(dialog);

        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", 18);
        margin.AddThemeConstantOverride("margin_top", 16);
        margin.AddThemeConstantOverride("margin_right", 18);
        margin.AddThemeConstantOverride("margin_bottom", 16);
        dialog.AddChild(margin);

        VBoxContainer stack = new();
        stack.AddThemeConstantOverride("separation", 10);
        margin.AddChild(stack);

        HBoxContainer header = new();
        header.AddThemeConstantOverride("separation", 12);
        stack.AddChild(header);

        VBoxContainer titleStack = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _title = new Label();
        _title.AddThemeFontOverride("font", GetThemeFont("display"));
        _title.AddThemeFontSizeOverride("font_size", 22);
        _subtitle = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _subtitle.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        titleStack.AddChild(_title);
        titleStack.AddChild(_subtitle);
        header.AddChild(titleStack);

        Button close = new() { CustomMinimumSize = new Vector2(40, 36), Text = "X" };
        IconAtlas.ApplyIconButton(close, "close", 40, 28);
        close.TooltipText = "Close";
        close.Pressed += () => ClosePressed?.Invoke(this, EventArgs.Empty);
        header.AddChild(close);

        PanelContainer sourcePanel = new() { CustomMinimumSize = new Vector2(0, 38) };
        OnlyWarStyle.ApplyInsetPanel(sourcePanel);
        _source = new Label { VerticalAlignment = VerticalAlignment.Center };
        _source.AddThemeColorOverride("font_color", OnlyWarStyle.PlayerAccent);
        sourcePanel.AddChild(_source);
        stack.AddChild(sourcePanel);

        ScrollContainer scroll = new()
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        VBoxContainer editorStack = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        editorStack.AddThemeConstantOverride("separation", 14);
        scroll.AddChild(editorStack);

        // Character rows render above the pooled count sections: in an HQ squad they are the
        // whole roster, and in a mixed squad they are the part the player most likely came here
        // to change.
        _editor = new ElementLoadoutEditorView { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _editor.LoadoutChanged += (_, _) => LoadoutChanged?.Invoke(this, EventArgs.Empty);
        _editor.CharacterSelectionChanged += (_, change) =>
            CharacterLoadoutSelected?.Invoke(this, (change.Key, change.WeaponSet));
        _editor.CharacterResetRequested += (_, soldierId) =>
            CharacterLoadoutReset?.Invoke(this, soldierId);
        _editor.CharacterCustomizeRequested += (_, soldierId) =>
            CharacterCustomizeRequested?.Invoke(this, soldierId);
        editorStack.AddChild(_editor);
        stack.AddChild(scroll);

        HBoxContainer footer = new() { Alignment = BoxContainer.AlignmentMode.End };
        footer.AddThemeConstantOverride("separation", 8);
        _returnToDoctrine = new Button
        {
            Text = "Return to Doctrine",
            CustomMinimumSize = new Vector2(170, 38)
        };
        IconAtlas.Apply(_returnToDoctrine, "chapter", 170);
        _returnToDoctrine.Pressed += () => ReturnToDoctrinePressed?.Invoke(this, EventArgs.Empty);
        Button done = new() { Text = "Done", CustomMinimumSize = new Vector2(110, 38) };
        done.Pressed += () => ClosePressed?.Invoke(this, EventArgs.Empty);
        footer.AddChild(_returnToDoctrine);
        footer.AddChild(done);
        stack.AddChild(footer);

        _equipmentEditor = new EquipmentLoadoutEditorView();
        _equipmentEditor.SaveRequested += loadout => EquipmentLoadoutSaveRequested?.Invoke(loadout);
        AddChild(_equipmentEditor);
    }

    public void Display(
        string title,
        string subtitle,
        string source,
        IEnumerable<WeaponSet> loadout,
        bool isCustom,
        IReadOnlyList<CharacterLoadoutRowData> characterRows,
        IReadOnlyList<ElementCountSectionData> countSections)
    {
        _title.Text = title;
        _subtitle.Text = subtitle;
        _source.Text = source;
        _returnToDoctrine.Visible = isCustom;
        _editor.SetData(characterRows, countSections, loadout);
    }

    public void SetDoctrineState(string source, bool isCustom)
    {
        _source.Text = source;
        _returnToDoctrine.Visible = isCustom;
    }

    public void OpenEquipmentEditor(
        string title,
        string subtitle,
        EquipmentRulesCatalog catalog,
        EquipmentLoadout loadout,
        EquipmentValidationContext context)
    {
        _equipmentEditor.Open(title, subtitle, catalog, loadout, context, catalog.EquipmentKits.Values);
    }
}
