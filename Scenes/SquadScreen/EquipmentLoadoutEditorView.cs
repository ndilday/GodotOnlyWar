using Godot;
using OnlyWar.Helpers.UI;
using OnlyWar.Models.Equippables;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Shared itemized character-loadout editor. It is deliberately a modal control rather than a
/// second doctrine model: callers provide the resolved complete loadout and receive a replacement
/// only after the shared validator accepts it.
/// </summary>
public partial class EquipmentLoadoutEditorView : Control
{
    private EquipmentRulesCatalog _catalog;
    private IReadOnlyList<EquipmentKitTemplate> _presets = [];
    private EquipmentValidationContext _context;
    private EquipmentLoadout _workingLoadout;
    private bool _suppressSignals;

    private Label _title;
    private Label _subtitle;
    private OptionButton _presetPicker;
    private OptionButton _armorPicker;
    private OptionButton _addItemPicker;
    private VBoxContainer _itemsStack;
    private Label _capacityLabel;
    private Label _validationLabel;
    private Button _saveButton;

    public event Action<EquipmentLoadout> SaveRequested;
    public event Action CancelRequested;

    public override void _Ready()
    {
        AddToGroup(DialogController.DialogInputBlockerGroup);
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ZIndex = 120;

        ColorRect scrim = new()
        {
            Color = new Color(0, 0, 0, 0.72f),
            MouseFilter = MouseFilterEnum.Stop,
            AnchorRight = 1,
            AnchorBottom = 1
        };
        AddChild(scrim);

        PanelContainer panel = new()
        {
            AnchorLeft = 0.18f,
            AnchorTop = 0.08f,
            AnchorRight = 0.82f,
            AnchorBottom = 0.92f
        };
        OnlyWarStyle.ApplyContentPanel(panel);
        AddChild(panel);

        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", 18);
        margin.AddThemeConstantOverride("margin_top", 16);
        margin.AddThemeConstantOverride("margin_right", 18);
        margin.AddThemeConstantOverride("margin_bottom", 16);
        panel.AddChild(margin);

        VBoxContainer root = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);

        HBoxContainer header = new();
        VBoxContainer heading = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _title = new Label();
        _title.AddThemeFontOverride("font", GetThemeFont("display"));
        _title.AddThemeFontSizeOverride("font_size", 23);
        _subtitle = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _subtitle.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        heading.AddChild(_title);
        heading.AddChild(_subtitle);
        header.AddChild(heading);
        Button close = new() { Text = "X", CustomMinimumSize = new Vector2(40, 36) };
        IconAtlas.ApplyIconButton(close, "close", 40, 28);
        close.Pressed += () => Close();
        header.AddChild(close);
        root.AddChild(header);

        HBoxContainer presetRow = BuildLabeledRow("PRESET");
        _presetPicker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _presetPicker.ItemSelected += _ => { };
        presetRow.AddChild(_presetPicker);
        Button applyPreset = new() { Text = "Apply Preset", CustomMinimumSize = new Vector2(122, 34) };
        applyPreset.Pressed += ApplySelectedPreset;
        presetRow.AddChild(applyPreset);
        root.AddChild(presetRow);

        HBoxContainer armorRow = BuildLabeledRow("ARMOR");
        _armorPicker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _armorPicker.ItemSelected += OnArmorSelected;
        armorRow.AddChild(_armorPicker);
        root.AddChild(armorRow);

        HBoxContainer addRow = BuildLabeledRow("ADD EQUIPMENT");
        _addItemPicker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _addItemPicker.ItemSelected += _ => { };
        addRow.AddChild(_addItemPicker);
        Button add = new() { Text = "Add", CustomMinimumSize = new Vector2(76, 34) };
        add.Pressed += AddSelectedEquipment;
        addRow.AddChild(add);
        root.AddChild(addRow);

        ScrollContainer scroll = new()
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _itemsStack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _itemsStack.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_itemsStack);
        root.AddChild(scroll);

        _capacityLabel = new Label();
        _capacityLabel.AddThemeColorOverride("font_color", OnlyWarStyle.Gold);
        root.AddChild(_capacityLabel);
        _validationLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _validationLabel.AddThemeColorOverride("font_color", OnlyWarStyle.MedicalWarning);
        root.AddChild(_validationLabel);

        HBoxContainer footer = new() { Alignment = BoxContainer.AlignmentMode.End };
        footer.AddThemeConstantOverride("separation", 8);
        Button cancel = new() { Text = "Cancel", CustomMinimumSize = new Vector2(100, 38) };
        cancel.Pressed += () =>
        {
            Close();
            CancelRequested?.Invoke();
        };
        _saveButton = new Button { Text = "Save Loadout", CustomMinimumSize = new Vector2(150, 38) };
        _saveButton.Pressed += Save;
        footer.AddChild(cancel);
        footer.AddChild(_saveButton);
        root.AddChild(footer);

        Visible = false;
    }

    public void Open(
        string title,
        string subtitle,
        EquipmentRulesCatalog catalog,
        EquipmentLoadout initialLoadout,
        EquipmentValidationContext context,
        IEnumerable<EquipmentKitTemplate> presets = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _context = context ?? new EquipmentValidationContext();
        _workingLoadout = initialLoadout ?? new EquipmentLoadout();
        _presets = (presets ?? catalog.EquipmentKits.Values)
            .Where(preset => preset != null)
            .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _title.Text = title ?? "Equipment loadout";
        _subtitle.Text = subtitle ?? "Compose a complete carried loadout. Worn armor does not consume carry capacity.";
        PopulatePickers();
        RebuildItems();
        Visible = true;
    }

    public void Close() => Visible = false;

    private HBoxContainer BuildLabeledRow(string labelText)
    {
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 8);
        Label label = new()
        {
            Text = labelText,
            CustomMinimumSize = new Vector2(118, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        label.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        label.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(label);
        return row;
    }

    private void PopulatePickers()
    {
        _suppressSignals = true;
        _presetPicker.Clear();
        foreach (EquipmentKitTemplate preset in _presets)
        {
            _presetPicker.AddItem(preset.Name, preset.Id);
        }

        _armorPicker.Clear();
        _armorPicker.AddItem("(No armor)", 0);
        foreach (EquipmentTemplate armor in _catalog.EquipmentTemplates.Values
                     .Where(equipment => equipment.ArmorProfile != null)
                     .OrderBy(equipment => equipment.Name, StringComparer.OrdinalIgnoreCase))
        {
            _armorPicker.AddItem(armor.Name, armor.Id);
        }
        SelectById(_armorPicker, _workingLoadout.Armor?.Id ?? 0);

        _addItemPicker.Clear();
        _addItemPicker.AddItem("Choose an item…", 0);
        foreach (EquipmentTemplate equipment in GetSelectableEquipment()
                     .OrderBy(equipment => equipment.Name, StringComparer.OrdinalIgnoreCase))
        {
            _addItemPicker.AddItem(equipment.Name, equipment.Id);
        }
        _suppressSignals = false;
    }

    private IEnumerable<EquipmentTemplate> GetSelectableEquipment() =>
        _catalog.EquipmentTemplates.Values.Where(equipment =>
            equipment.ArmorProfile == null
            && (equipment.RangedProfile != null
                || equipment.MeleeProfile != null
                || equipment.GearProfile != null
                || equipment.AmmunitionProfile != null));

    private void ApplySelectedPreset()
    {
        if (_presetPicker.Selected < 0 || _presetPicker.Selected >= _presets.Count) return;
        _workingLoadout = _presets[_presetPicker.Selected].ToLoadout();
        PopulatePickers();
        RebuildItems();
    }

    private void OnArmorSelected(long _)
    {
        if (_suppressSignals) return;
        int armorId = _armorPicker.GetSelectedId();
        EquipmentTemplate armor = armorId == 0
            ? null
            : _catalog.EquipmentTemplates.GetValueOrDefault(armorId);
        _workingLoadout = new EquipmentLoadout(armor, _workingLoadout.Items);
        RefreshValidation();
    }

    private void AddSelectedEquipment()
    {
        int equipmentId = _addItemPicker.GetSelectedId();
        if (equipmentId == 0 || !_catalog.EquipmentTemplates.TryGetValue(equipmentId, out EquipmentTemplate equipment))
        {
            return;
        }

        List<EquipmentLoadoutEntry> entries = _workingLoadout.Items.ToList();
        int existingIndex = entries.FindIndex(entry =>
            entry.Equipment.Id == equipmentId && entry.InitialReadyOrder == null);
        if (existingIndex >= 0)
        {
            EquipmentLoadoutEntry existing = entries[existingIndex];
            if (existing.Quantity >= equipment.MaximumQuantity) return;
            entries[existingIndex] = new EquipmentLoadoutEntry(
                equipment,
                existing.Quantity + 1,
                existing.InitialReadyOrder);
        }
        else
        {
            entries.Add(new EquipmentLoadoutEntry(equipment, 1));
        }
        _workingLoadout = new EquipmentLoadout(_workingLoadout.Armor, entries);
        RebuildItems();
    }

    private void RebuildItems()
    {
        if (_itemsStack == null) return;
        ClearChildren(_itemsStack);

        Label caption = new() { Text = "CARRIED EQUIPMENT" };
        caption.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        caption.AddThemeFontSizeOverride("font_size", 12);
        _itemsStack.AddChild(caption);

        if (_workingLoadout.Items.Count == 0)
        {
            Label empty = new() { Text = "No carried items. Add a weapon, gear item, or ammunition package above." };
            empty.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
            _itemsStack.AddChild(empty);
        }

        foreach (EquipmentLoadoutEntry entry in _workingLoadout.Items.ToList())
        {
            _itemsStack.AddChild(BuildItemRow(entry));
        }
        RefreshValidation();
    }

    private PanelContainer BuildItemRow(EquipmentLoadoutEntry entry)
    {
        PanelContainer panel = new() { CustomMinimumSize = new Vector2(0, 48) };
        OnlyWarStyle.ApplyInsetPanel(panel);
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 8);
        panel.AddChild(row);

        Label name = new()
        {
            Text = entry.Equipment.Name,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.AddChild(name);

        SpinBox quantity = new()
        {
            MinValue = 1,
            MaxValue = entry.Equipment.MaximumQuantity,
            Step = 1,
            Value = entry.Quantity,
            CustomMinimumSize = new Vector2(82, 34),
            TooltipText = "Carried quantity"
        };
        quantity.ValueChanged += value =>
        {
            int newQuantity = Math.Clamp((int)value, 1, entry.Equipment.MaximumQuantity);
            ReplaceEntry(entry, new EquipmentLoadoutEntry(
                entry.Equipment,
                newQuantity,
                entry.InitialReadyOrder));
        };
        row.AddChild(quantity);

        CheckButton readied = new()
        {
            Text = "Ready",
            ButtonPressed = entry.InitialReadyOrder.HasValue,
            TooltipText = "Prefer this weapon when the mission starts"
        };
        row.AddChild(readied);

        SpinBox readyOrder = new()
        {
            MinValue = 0,
            MaxValue = 20,
            Step = 1,
            Value = entry.InitialReadyOrder ?? 0,
            CustomMinimumSize = new Vector2(68, 34),
            TooltipText = "Initial ready order"
        };
        readyOrder.GetLineEdit().Editable = entry.InitialReadyOrder.HasValue;
        readied.Toggled += pressed =>
        {
            readyOrder.GetLineEdit().Editable = pressed;
            ReplaceEntry(entry, new EquipmentLoadoutEntry(
                entry.Equipment,
                entry.Quantity,
                pressed ? (int)readyOrder.Value : null));
        };
        readyOrder.ValueChanged += value =>
        {
            if (!readied.ButtonPressed) return;
            ReplaceEntry(entry, new EquipmentLoadoutEntry(
                entry.Equipment,
                entry.Quantity,
                (int)value));
        };
        row.AddChild(readyOrder);

        Button remove = new() { Text = "Remove", CustomMinimumSize = new Vector2(84, 34) };
        remove.Pressed += () =>
        {
            List<EquipmentLoadoutEntry> entries = _workingLoadout.Items.ToList();
            entries.Remove(entry);
            _workingLoadout = new EquipmentLoadout(_workingLoadout.Armor, entries);
            RebuildItems();
        };
        row.AddChild(remove);
        return panel;
    }

    private void ReplaceEntry(EquipmentLoadoutEntry original, EquipmentLoadoutEntry replacement)
    {
        List<EquipmentLoadoutEntry> entries = _workingLoadout.Items.ToList();
        int index = entries.IndexOf(original);
        if (index < 0) return;
        entries[index] = replacement;
        _workingLoadout = new EquipmentLoadout(_workingLoadout.Armor, entries);
        RefreshValidation();
    }

    private void RefreshValidation()
    {
        if (_workingLoadout == null || _capacityLabel == null) return;
        float used = EquipmentLoadoutValidator.GetUsedCapacity(_workingLoadout);
        float available = EquipmentLoadoutValidator.GetAvailableCapacity(_workingLoadout, _context);
        EquipmentValidationResult validation = EquipmentLoadoutValidator.Validate(
            _workingLoadout,
            _context);
        _capacityLabel.Text = $"Load {used:0.##}/{available:0.##} capacity · "
            + $"{_workingLoadout.Items.Sum(entry => entry.Quantity)} carried item(s)";
        _validationLabel.Text = validation.IsValid
            ? "Loadout valid. Save to replace the complete resolved composition."
            : string.Join("\n", validation.Issues.Select(issue => "• " + issue.Message));
        _validationLabel.AddThemeColorOverride(
            "font_color",
            validation.IsValid ? OnlyWarStyle.PlayerAccent : OnlyWarStyle.MedicalWarning);
        _saveButton.Disabled = !validation.IsValid;
    }

    private void Save()
    {
        EquipmentValidationResult validation = EquipmentLoadoutValidator.Validate(
            _workingLoadout,
            _context);
        if (!validation.IsValid) return;
        Close();
        SaveRequested?.Invoke(_workingLoadout);
    }

    private static void SelectById(OptionButton picker, int id)
    {
        for (int index = 0; index < picker.ItemCount; index++)
        {
            if (picker.GetItemId(index) == id)
            {
                picker.Selected = index;
                return;
            }
        }
        picker.Selected = 0;
    }

    private static void ClearChildren(Node parent)
    {
        foreach (Node child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }
}
