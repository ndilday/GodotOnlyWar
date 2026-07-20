using Godot;
using OnlyWar.Helpers.UI;
using System;
using System.Collections.Generic;

// A single squad-roster row: the member's id, rank/name label, a recovery-status clause, and
// flags driving how injured members are visually distinguished (PRD 4.6).
public sealed record SquadMemberRow(
    int SoldierId, string Label, string RecoveryStatus, bool IsInjured, bool IsOutOfAction);

public partial class SquadScreenView : DialogView
{
    private VBoxContainer _squadDetailsVBox;
    private VBoxContainer _squadLoadoutVBox;
    private VBoxContainer _squadMemberVBox;
    private RichTextLabel _defaultName;
    private RichTextLabel _defaultCount;
    private Button _copyLoadoutButton;
    private Button _pasteLoadoutButton;
    private List<WeaponSetSelectionView> _weaponSets;

    public event EventHandler<ValueTuple<string, int>> WeaponSetSelectionWeaponSetCountChanged;
    public event EventHandler CopyLoadout;
    public event EventHandler PasteLoadout;

    public override void _Ready()
    {
        base._Ready();
        _squadDetailsVBox = GetNode<VBoxContainer>("DataPanel/VBoxContainer");
        _squadLoadoutVBox = GetNode<VBoxContainer>("LoadoutPanel/ScrollContainer/VBoxContainer");
        _squadMemberVBox = GetNode<VBoxContainer>("SquadMemberPanel/ScrollContainer/VBoxContainer");
        _defaultName = GetNode<RichTextLabel>("LoadoutPanel/ScrollContainer/VBoxContainer/DefaultHBox/Name");
        _defaultCount = GetNode<RichTextLabel>("LoadoutPanel/ScrollContainer/VBoxContainer/DefaultHBox/Count");
        _copyLoadoutButton = GetNode<Button>("DataPanel/ButtonVBox/CopyLoadoutButton");
        _copyLoadoutButton.Pressed += () => CopyLoadout(this, EventArgs.Empty);
        _pasteLoadoutButton = GetNode<Button>("DataPanel/ButtonVBox/PasteLoadoutButton");
        _pasteLoadoutButton.Pressed += () => PasteLoadout(this, EventArgs.Empty);
        _weaponSets = new List<WeaponSetSelectionView>();
        ApplyThemeStyling();
    }

    public void ClearSquadData()
    {
        var existingLines = _squadDetailsVBox.GetChildren();
        if (existingLines != null)
        {
            foreach (var line in existingLines)
            {
                _squadDetailsVBox.RemoveChild(line);
                line.QueueFree();
            }
        }
    }

    public void ClearSquadLoadout()
    {
        if (_weaponSets.Count > 0)
        {
            foreach (var weaponSetSelectionView in _weaponSets)
            {
                _squadLoadoutVBox.RemoveChild(weaponSetSelectionView);
                weaponSetSelectionView.WeaponSetCountChanged -= OnWeaponSetCountChanged;
                weaponSetSelectionView.QueueFree();
            }
            _weaponSets.Clear();
        }
    }

    public void PopulateSquadData(IReadOnlyList<ValueTuple<string, string>> stringPairs)
    {
        ClearSquadData();
        foreach (ValueTuple<string, string> line in stringPairs)
        {
            AddLine(_squadDetailsVBox, line.Item1, line.Item2);
        }
    }

    public void PopulateSquadLoadout(List<ValueTuple<List<ValueTuple<string, int>>, string, int, int>> weaponSets, ValueTuple<string, int> defaultWeaponSet)
    {
        ClearSquadLoadout();
        PackedScene weaponSetSelectionScene = GD.Load<PackedScene>("res://Scenes/SquadScreen/weapon_set_selection.tscn");
        // add default Weapon set at top, set min and max for it to its current value
        List<string> defaultWeaponSetList = new List<string> { defaultWeaponSet.Item1 };
        _defaultName.Text = defaultWeaponSet.Item1;
        _defaultCount.Text = defaultWeaponSet.Item2.ToString();
        foreach (var weaponSet in weaponSets)
        {

            WeaponSetSelectionView view = (WeaponSetSelectionView)weaponSetSelectionScene.Instantiate();
            _squadLoadoutVBox.AddChild(view);
            _weaponSets.Add(view);
            view.Initialize(weaponSet.Item1, weaponSet.Item2, weaponSet.Item3, weaponSet.Item4);
            view.WeaponSetCountChanged += OnWeaponSetCountChanged;
        }

    }

    // Injured members are visually distinguished and carry their expected recovery time
    // (PRD 4.6): out-of-action brothers in crimson, lighter wounds in amber, healthy in the
    // default parchment.
    private static readonly Color OutOfActionColor = new Color("d05a5a");
    private static readonly Color InjuredColor = new Color("d9a441");

    public void PopulateSquadMembers(IReadOnlyList<SquadMemberRow> members)
    {
        ClearSquadMembers();
        if (members == null || members.Count == 0)
        {
            _squadMemberVBox.AddChild(CreateMemberRow("No members assigned.", OnlyWarStyle.MutedText));
        }
        else
        {
            foreach (var member in members)
            {
                string text = member.IsInjured
                    ? $"{member.Label} - {member.RecoveryStatus}"
                    : member.Label;
                Color textColor = OnlyWarStyle.BodyText;
                if (member.IsOutOfAction)
                {
                    textColor = OutOfActionColor;
                }
                else if (member.IsInjured)
                {
                    textColor = InjuredColor;
                }
                _squadMemberVBox.AddChild(CreateMemberRow(text, textColor));
            }
        }
    }

    private void ClearSquadMembers()
    {
        var children = _squadMemberVBox.GetChildren();
        foreach (var child in children)
        {
            _squadMemberVBox.RemoveChild(child);
            child.QueueFree();
        }
    }

    public void SetDefaultWeaponSetCount(int count)
    {
        _defaultCount.Text = count.ToString();
    }

    public void DisableCountIncreases(bool disable)
    {
        foreach(var weaponSet in _weaponSets)
        {
            weaponSet.DisableInrease(disable);
        }
    }

    public void DisablePasteLoadout(bool disable)
    {
        _pasteLoadoutButton.Disabled = disable;
    }

    private void OnWeaponSetCountChanged(object sender, ValueTuple<string, int> args)
    {
        WeaponSetSelectionWeaponSetCountChanged?.Invoke(sender, args);
    }

    private void ApplyThemeStyling()
    {
        ApplyContentPanel("DataPanel");
        ApplyContentPanel("SquadMemberPanel");
        ApplyContentPanel("LoadoutPanel");
        ApplyInsetPanel("DataPanel/Header");
        ApplyInsetPanel("SquadMemberPanel/Header");
        ApplyInsetPanel("LoadoutPanel/Panel");

        _squadDetailsVBox.AddThemeConstantOverride("separation", 6);
        _squadLoadoutVBox.AddThemeConstantOverride("separation", 6);
        _squadMemberVBox.AddThemeConstantOverride("separation", 6);
    }

    private void ApplyContentPanel(string path)
    {
        Panel panel = GetNodeOrNull<Panel>(path);
        if (panel != null)
        {
            OnlyWarStyle.ApplyContentPanel(panel);
        }
    }

    private void ApplyInsetPanel(string path)
    {
        Panel panel = GetNodeOrNull<Panel>(path);
        if (panel != null)
        {
            OnlyWarStyle.ApplyInsetPanel(panel);
        }
    }

    private void AddLine(VBoxContainer container, string label, string value)
    {
        PanelContainer linePanel = new()
        {
            CustomMinimumSize = new Vector2(0, 36),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        OnlyWarStyle.ApplyInsetPanel(linePanel);

        HBoxContainer row = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        row.AddThemeConstantOverride("separation", 8);
        linePanel.AddChild(row);

        Label lineLabel = new()
        {
            Text = label,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(140, 0)
        };
        lineLabel.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        row.AddChild(lineLabel);

        Label lineValue = new()
        {
            Text = value,
            HorizontalAlignment = HorizontalAlignment.Right,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(140, 0)
        };
        row.AddChild(lineValue);

        container.AddChild(linePanel);
    }

    private static Control CreateMemberRow(string text, Color textColor)
    {
        PanelContainer rowPanel = new()
        {
            CustomMinimumSize = new Vector2(0, 34),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        OnlyWarStyle.ApplyInsetPanel(rowPanel);

        Label label = new()
        {
            Text = text,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        label.AddThemeColorOverride("font_color", textColor);
        rowPanel.AddChild(label);
        return rowPanel;
    }
}
