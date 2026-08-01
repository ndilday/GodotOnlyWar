using Godot;
using OnlyWar.Helpers.UI;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Shared weapon-allocation surface used by individual squads and both doctrine scopes.
/// It edits a working list of explicit special weapon sets; unallocated members implicitly use
/// the squad template's standard weapons.
/// </summary>
public partial class LoadoutEditorView : VBoxContainer
{
    private Label _standardName;
    private Label _standardCount;
    private VBoxContainer _optionStack;
    private SquadTemplate _template;
    private List<WeaponSet> _workingLoadout = [];
    private int _capacity;

    public event EventHandler LoadoutChanged;
    public IReadOnlyList<WeaponSet> WorkingLoadout => _workingLoadout;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 10);

        Label caption = new() { Text = "STANDARD ISSUE" };
        caption.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        caption.AddThemeFontSizeOverride("font_size", 12);
        AddChild(caption);

        PanelContainer standardPanel = new() { CustomMinimumSize = new Vector2(0, 44) };
        OnlyWarStyle.ApplyInsetPanel(standardPanel);
        HBoxContainer standardRow = new();
        standardRow.AddThemeConstantOverride("separation", 12);
        standardPanel.AddChild(standardRow);
        _standardName = new Label
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center
        };
        _standardCount = new Label
        {
            CustomMinimumSize = new Vector2(64, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _standardCount.AddThemeColorOverride("font_color", OnlyWarStyle.Gold);
        standardRow.AddChild(_standardName);
        standardRow.AddChild(_standardCount);
        AddChild(standardPanel);

        Label optionCaption = new() { Text = "SPECIALIST OPTIONS" };
        optionCaption.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        optionCaption.AddThemeFontSizeOverride("font_size", 12);
        AddChild(optionCaption);

        _optionStack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _optionStack.AddThemeConstantOverride("separation", 8);
        AddChild(_optionStack);
    }

    public void SetLoadout(SquadTemplate template, IEnumerable<WeaponSet> loadout, int capacity)
    {
        _template = template;
        _capacity = Math.Max(0, capacity);
        _workingLoadout = loadout?.Take(_capacity).ToList() ?? [];
        Rebuild();
    }

    private void Rebuild()
    {
        if (_standardName == null || _optionStack == null) return;

        foreach (Node child in _optionStack.GetChildren())
        {
            _optionStack.RemoveChild(child);
            child.QueueFree();
        }

        _standardName.Text = _template?.DefaultWeapons?.Name ?? "Standard weapons";
        UpdateStandardCount();
        if (_template?.WeaponOptions == null || _template.WeaponOptions.Count == 0)
        {
            Label empty = new()
            {
                Text = "This squad type has no alternative weapon options.",
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            empty.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
            _optionStack.AddChild(empty);
            return;
        }

        PackedScene selectionScene = GD.Load<PackedScene>("res://Scenes/SquadScreen/weapon_set_selection.tscn");
        foreach (SquadWeaponOption option in _template.WeaponOptions)
        {
            List<(string, int)> choices = option.Options
                .Select(set => (set.Name, _workingLoadout.Count(current => current.Id == set.Id)))
                .ToList();
            WeaponSetSelectionView view = (WeaponSetSelectionView)selectionScene.Instantiate();
            _optionStack.AddChild(view);
            view.Initialize(
                choices,
                option.Name,
                option.MinNumber,
                Math.Min(option.MaxNumber, _capacity));
            view.WeaponSetCountChanged += OnWeaponSetCountChanged;
        }
        UpdateIncreaseAvailability();
    }

    private void OnWeaponSetCountChanged(object sender, (string WeaponSetName, int Count) change)
    {
        WeaponSetSelectionView selection = (WeaponSetSelectionView)sender;
        SquadWeaponOption option = _template.WeaponOptions.First(candidate => candidate.Name == selection.OptionName);
        WeaponSet weaponSet = option.Options.First(candidate => candidate.Name == change.WeaponSetName);
        int currentCount = _workingLoadout.Count(current => current.Id == weaponSet.Id);

        while (currentCount > change.Count)
        {
            _workingLoadout.Remove(_workingLoadout.First(current => current.Id == weaponSet.Id));
            currentCount--;
        }
        while (currentCount < change.Count && _workingLoadout.Count < _capacity)
        {
            _workingLoadout.Add(weaponSet);
            currentCount++;
        }

        UpdateStandardCount();
        UpdateIncreaseAvailability();
        LoadoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateStandardCount()
    {
        if (_standardCount != null)
        {
            _standardCount.Text = Math.Max(0, _capacity - _workingLoadout.Count).ToString();
        }
    }

    private void UpdateIncreaseAvailability()
    {
        bool full = _workingLoadout.Count >= _capacity;
        foreach (WeaponSetSelectionView selection in _optionStack.GetChildren().OfType<WeaponSetSelectionView>())
        {
            selection.DisableIncrease(full);
        }
    }
}
