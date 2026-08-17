using Godot;
using OnlyWar.Helpers;
using OnlyWar.Helpers.UI;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// One character-equipped slot. Key is a soldier id when editing a live squad's personal
/// loadouts, or a soldier-template id when editing a chapter-wide role default — callers
/// interpret it, this view only echoes it back on the change events.
/// </summary>
public sealed record CharacterLoadoutRowData(
    int Key,
    string Title,
    string Detail,
    IReadOnlyList<WeaponSet> Options,
    WeaponSet Selected,
    bool CanReset);

/// <summary>
/// One pooled (MaximumAllowed &gt; 1) quota group on an element — "up to 4 heavies" — sharing
/// the element's standard-issue capacity with any sibling groups on the same element.
/// </summary>
public sealed record CountGroupData(
    string OptionGroup,
    IReadOnlyList<WeaponSet> Menu,
    int MinimumRequired,
    int MaximumAllowed);

/// <summary>
/// One element's count-based section: a standard-issue readout shared by every quota group on
/// the element, each group rendered as its own spinbox row-set. Capacity is that element's own
/// body count, supplied by the caller — a live squad's able-bodied roster for that element, or
/// the template's MaximumNumber when there is no roster yet.
/// </summary>
public sealed record ElementCountSectionData(
    string StandardIssueName,
    int Capacity,
    IReadOnlyList<CountGroupData> Groups);

/// <summary>
/// Builds <see cref="ElementCountSectionData"/> from a template's elements, one section per
/// element that has a pooled quota group. Shared by the squad screen (live roster capacity) and
/// the doctrine dialog (template MaximumNumber capacity) so the per-element capacity scoping —
/// never the whole squad's or the whole template's — lives in exactly one place.
/// </summary>
public static class ElementLoadoutSections
{
    public static List<ElementCountSectionData> Build(
        SquadTemplate template, Func<SquadTemplateElement, int> capacityForElement)
    {
        List<ElementCountSectionData> sections = [];
        if (template?.Elements == null) return sections;

        foreach (SquadTemplateElement element in template.Elements)
        {
            // An explicit PersonalEquipmentRole owns the element's complete composition and
            // never enters the pooled count editor. Legacy fixtures without that relation retain
            // the old Command Weapon split until their rules rows are migrated.
            List<CountGroupData> groups = element.PersonalEquipmentRole != null
                ? []
                : element.Quotas
                .Where(quota => quota.OptionGroup != CharacterLoadoutService.CommandWeaponGroup)
                .Select(quota => new CountGroupData(
                    quota.OptionGroup,
                    element.GetMenu(quota.OptionGroup),
                    quota.MinimumRequired,
                    quota.MaximumAllowed))
                .ToList();
            // Elements with no pooled quota (fixed-loadout troopers, or Command-Weapon-only slots
            // handled by the character rows instead) contribute no section at all.
            if (groups.Count == 0) continue;

            string standardName = element.DefaultWeapons?.Name ?? template.DefaultWeapons?.Name ?? "Standard weapons";
            sections.Add(new ElementCountSectionData(standardName, capacityForElement(element), groups));
        }
        return sections;
    }
}

/// <summary>
/// Unified squad-loadout editor, driven by a SquadTemplateElement's quota groups rather than a
/// squad-level or soldier-level menu. The Command Weapon group is one named body's personal pick
/// (a sergeant, a captain, a specialist) and renders as a dropdown row; every other group is a
/// headcount drawn from the element's bodies and renders as the standard-issue/spinbox section.
/// Replaces the former LoadoutEditorView/CharacterLoadoutEditorView split, which tracked
/// "character vs squad" template flags that no longer exist.
/// </summary>
public partial class ElementLoadoutEditorView : VBoxContainer
{
    private VBoxContainer _characterStack;
    private VBoxContainer _countStack;
    private List<CharacterLoadoutRowData> _characterRows = [];
    private List<ElementCountSectionData> _countSections = [];
    private List<WeaponSet> _workingLoadout = [];

    /// <summary>Raised whenever a count-group spinbox edits WorkingLoadout.</summary>
    public event EventHandler LoadoutChanged;
    /// <summary>Raised with a character row's key and the newly chosen set.</summary>
    public event EventHandler<(int Key, WeaponSet WeaponSet)> CharacterSelectionChanged;
    /// <summary>Raised with a character row's key when the player clears its override.</summary>
    public event EventHandler<int> CharacterResetRequested;
    /// <summary>Raised when the itemized editor should open for a character row.</summary>
    public event EventHandler<int> CharacterCustomizeRequested;

    public IReadOnlyList<WeaponSet> WorkingLoadout => _workingLoadout;

    /// <summary>Heading over the character rows; callers use this to distinguish "this squad's
    /// people" from "this chapter's roles".</summary>
    public string CharacterCaptionText { get; set; } = "COMMAND & SPECIALISTS";

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 14);

        _characterStack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _characterStack.AddThemeConstantOverride("separation", 6);
        AddChild(_characterStack);

        _countStack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _countStack.AddThemeConstantOverride("separation", 14);
        AddChild(_countStack);
    }

    public void SetData(
        IReadOnlyList<CharacterLoadoutRowData> characterRows,
        IReadOnlyList<ElementCountSectionData> countSections,
        IEnumerable<WeaponSet> loadout)
    {
        _characterRows = characterRows?.ToList() ?? [];
        _countSections = countSections?.ToList() ?? [];
        _workingLoadout = loadout?.ToList() ?? [];
        Rebuild();
    }

    private void Rebuild()
    {
        if (_characterStack == null || _countStack == null) return;

        ClearChildren(_characterStack);
        ClearChildren(_countStack);

        if (_characterRows.Count == 0 && _countSections.Count == 0)
        {
            Label empty = new()
            {
                Text = "No configurable loadout here.",
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            empty.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
            _countStack.AddChild(empty);
            return;
        }

        if (_characterRows.Count > 0)
        {
            Label caption = new() { Text = CharacterCaptionText };
            caption.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
            caption.AddThemeFontSizeOverride("font_size", 12);
            _characterStack.AddChild(caption);
            foreach (CharacterLoadoutRowData row in _characterRows)
            {
                _characterStack.AddChild(BuildCharacterRow(row));
            }
        }

        foreach (ElementCountSectionData section in _countSections)
        {
            BuildCountSection(section);
        }
    }

    private PanelContainer BuildCharacterRow(CharacterLoadoutRowData row)
    {
        PanelContainer panel = new() { CustomMinimumSize = new Vector2(0, 56) };
        OnlyWarStyle.ApplyInsetPanel(panel);

        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_top", 6);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_bottom", 6);
        panel.AddChild(margin);

        HBoxContainer content = new();
        content.AddThemeConstantOverride("separation", 12);
        margin.AddChild(content);

        VBoxContainer identity = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        Label title = new() { Text = row.Title };
        Label detail = new()
        {
            Text = row.Detail ?? "",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        detail.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        detail.AddThemeFontSizeOverride("font_size", 12);
        identity.AddChild(title);
        identity.AddChild(detail);
        content.AddChild(identity);

        int key = row.Key;
        if (row.Options.Count > 0)
        {
            OptionButton picker = new() { CustomMinimumSize = new Vector2(280, 34) };
            for (int i = 0; i < row.Options.Count; i++)
            {
                picker.AddItem(row.Options[i].Name, i);
            }
            int selectedIndex = row.Selected == null
                ? -1
                : row.Options.ToList().FindIndex(option => option.Id == row.Selected.Id);
            picker.Selected = selectedIndex;
            picker.Disabled = row.Options.Count <= 1;
            // Capture the row's own key and options; the handler outlives this loop iteration.
            IReadOnlyList<WeaponSet> options = row.Options;
            picker.ItemSelected += index =>
            {
                if (index < 0 || index >= options.Count) return;
                CharacterSelectionChanged?.Invoke(this, (key, options[(int)index]));
            };
            content.AddChild(picker);
        }
        else
        {
            Label itemized = new()
            {
                Text = "Itemized loadout",
                CustomMinimumSize = new Vector2(180, 34),
                VerticalAlignment = VerticalAlignment.Center
            };
            itemized.AddThemeColorOverride("font_color", OnlyWarStyle.PlayerAccent);
            content.AddChild(itemized);
        }

        Button customize = new()
        {
            Text = "Customize",
            CustomMinimumSize = new Vector2(104, 34),
            TooltipText = "Compose the complete armor, weapons, gear, and ammunition loadout"
        };
        customize.Pressed += () => CharacterCustomizeRequested?.Invoke(this, key);
        content.AddChild(customize);

        Button reset = new()
        {
            Text = "Inherit",
            CustomMinimumSize = new Vector2(84, 34),
            TooltipText = "Clear this override and follow the broader standard",
            Disabled = !row.CanReset
        };
        reset.Pressed += () => CharacterResetRequested?.Invoke(this, key);
        content.AddChild(reset);

        return panel;
    }

    // Parents its own section rather than returning one for the caller to add. That ordering is
    // load-bearing: weapon_set_selection.tscn resolves its child nodes in _Ready, which Godot only
    // runs once a node enters the scene tree, so the instantiated selection views below must be
    // added under something already in the tree before Initialize touches them.
    private void BuildCountSection(ElementCountSectionData section)
    {
        VBoxContainer stack = new();
        stack.AddThemeConstantOverride("separation", 10);
        _countStack.AddChild(stack);

        Label standardCaption = new() { Text = "STANDARD ISSUE" };
        standardCaption.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        standardCaption.AddThemeFontSizeOverride("font_size", 12);
        stack.AddChild(standardCaption);

        PanelContainer standardPanel = new() { CustomMinimumSize = new Vector2(0, 44) };
        OnlyWarStyle.ApplyInsetPanel(standardPanel);
        HBoxContainer standardRow = new();
        standardRow.AddThemeConstantOverride("separation", 12);
        standardPanel.AddChild(standardRow);
        Label standardName = new()
        {
            Text = section.StandardIssueName,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center
        };
        Label standardCount = new()
        {
            CustomMinimumSize = new Vector2(64, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        standardCount.AddThemeColorOverride("font_color", OnlyWarStyle.Gold);
        standardRow.AddChild(standardName);
        standardRow.AddChild(standardCount);
        stack.AddChild(standardPanel);

        // Every group on this element draws from the same body pool, so "how many are still
        // standard issue" is capacity minus the union of all groups' picks, not any one group's.
        HashSet<int> sectionSetIds = section.Groups
            .SelectMany(group => group.Menu)
            .Select(set => set.Id)
            .ToHashSet();
        List<WeaponSetSelectionView> groupViews = [];

        void UpdateStandardCount()
        {
            int used = _workingLoadout.Count(set => sectionSetIds.Contains(set.Id));
            standardCount.Text = Math.Max(0, section.Capacity - used).ToString();
        }
        void UpdateIncreaseAvailability()
        {
            int used = _workingLoadout.Count(set => sectionSetIds.Contains(set.Id));
            bool full = used >= section.Capacity;
            foreach (WeaponSetSelectionView view in groupViews)
            {
                view.DisableIncrease(full);
            }
        }
        void OnGroupCountChanged(CountGroupData group, string weaponSetName, int newCount)
        {
            WeaponSet weaponSet = group.Menu.First(set => set.Name == weaponSetName);
            int currentCount = _workingLoadout.Count(current => current.Id == weaponSet.Id);
            int used = _workingLoadout.Count(set => sectionSetIds.Contains(set.Id));

            while (currentCount > newCount)
            {
                _workingLoadout.Remove(_workingLoadout.First(current => current.Id == weaponSet.Id));
                currentCount--;
                used--;
            }
            while (currentCount < newCount && used < section.Capacity)
            {
                _workingLoadout.Add(weaponSet);
                currentCount++;
                used++;
            }

            UpdateStandardCount();
            UpdateIncreaseAvailability();
            LoadoutChanged?.Invoke(this, EventArgs.Empty);
        }

        UpdateStandardCount();
        if (section.Groups.Count == 0) return;

        Label optionCaption = new() { Text = "SPECIALIST OPTIONS" };
        optionCaption.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        optionCaption.AddThemeFontSizeOverride("font_size", 12);
        stack.AddChild(optionCaption);

        VBoxContainer optionStack = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        optionStack.AddThemeConstantOverride("separation", 8);
        stack.AddChild(optionStack);

        PackedScene selectionScene = GD.Load<PackedScene>("res://Scenes/SquadScreen/weapon_set_selection.tscn");
        foreach (CountGroupData group in section.Groups)
        {
            List<(string, int)> choices = group.Menu
                .Select(set => (set.Name, _workingLoadout.Count(current => current.Id == set.Id)))
                .ToList();
            WeaponSetSelectionView view = (WeaponSetSelectionView)selectionScene.Instantiate();
            optionStack.AddChild(view);
            view.Initialize(
                choices,
                group.OptionGroup,
                group.MinimumRequired,
                Math.Min(group.MaximumAllowed, section.Capacity));
            view.WeaponSetCountChanged += (_, change) =>
                OnGroupCountChanged(group, change.Item1, change.Item2);
            groupViews.Add(view);
        }
        UpdateIncreaseAvailability();
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
