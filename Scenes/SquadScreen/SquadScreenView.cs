using Godot;
using System;
using System.Collections.Generic;

public partial class SquadScreenView : DialogView
{
    private VBoxContainer _squadDetailsVBox;
    private VBoxContainer _squadLoadoutVBox;
    private RichTextLabel _defaultName;
    private RichTextLabel _defaultCount;
    private List<WeaponSetSelectionView> _weaponSets;

    public event EventHandler<Tuple<string, int>> WeaponSetSelectionWeaponSetCountChanged;

    public override void _Ready()
    {
        base._Ready();
        _squadDetailsVBox = GetNode<VBoxContainer>("DataPanel/VBoxContainer");
        _squadLoadoutVBox = GetNode<VBoxContainer>("LoadoutPanel/ScrollContainer/VBoxContainer");
        _defaultName = GetNode<RichTextLabel>("LoadoutPanel/ScrollContainer/VBoxContainer/DefaultHBox/Name");
        _defaultCount = GetNode<RichTextLabel>("LoadoutPanel/ScrollContainer/VBoxContainer/DefaultHBox/Count");
        _weaponSets = new List<WeaponSetSelectionView>();
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
                _squadDetailsVBox.RemoveChild(weaponSetSelectionView);
                weaponSetSelectionView.WeaponSetCountChanged -= OnWeaponSetCountChanged;
                weaponSetSelectionView.QueueFree();
            }
            _weaponSets.Clear();
        }
    }

    public void PopulateSquadData(IReadOnlyList<Tuple<string, string>> stringPairs)
    {
        ClearSquadData();
        foreach (Tuple<string, string> line in stringPairs)
        {
            AddLine(line.Item1, line.Item2);
        }
    }

    public void PopulateSquadLoadout(List<Tuple<List<string>, string, int, int, int>> weaponSets, Tuple<string, int> defaultWeaponSet)
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
            view.Initialize(weaponSet.Item1, weaponSet.Item2, weaponSet.Item3, weaponSet.Item4, weaponSet.Item5);
            view.WeaponSetCountChanged += OnWeaponSetCountChanged;
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

    private void OnWeaponSetCountChanged(object sender, Tuple<string, int> args)
    {
        WeaponSetSelectionWeaponSetCountChanged?.Invoke(sender, args);
    }

    private void AddLine(string label, string value)
    {
        Panel linePanel = new Panel();
        linePanel.SizeFlagsHorizontal = SizeFlags.Fill;
        linePanel.SizeFlagsVertical = SizeFlags.Fill;
        linePanel.CustomMinimumSize = new Vector2(0, 20);
        Label lineLabel = new Label();
        lineLabel.Text = label;
        lineLabel.AnchorLeft = 0;
        lineLabel.HorizontalAlignment = HorizontalAlignment.Left;
        lineLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        linePanel.AddChild(lineLabel);
        Label lineValue = new Label();
        lineValue.Text = value;
        lineValue.AnchorRight = 1;
        lineValue.HorizontalAlignment = HorizontalAlignment.Right;
        lineValue.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        linePanel.AddChild(lineValue);
        _squadDetailsVBox.AddChild(linePanel);
    }
}
