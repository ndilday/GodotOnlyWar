using Godot;
using System;
using System.Collections.Generic;

public partial class SquadScreenView : DialogView
{
    private VBoxContainer _squadDetailsVBox;
    private VBoxContainer _squadLoadoutVBox;

    public override void _Ready()
    {
        base._Ready();
        _squadDetailsVBox = GetNode<VBoxContainer>("DataPanel/VBoxContainer");
        _squadLoadoutVBox = GetNode<VBoxContainer>("LoadoutPanel/VBoxContainer");
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
        var existingLines = _squadLoadoutVBox.GetChildren();
        if (existingLines != null)
        {
            foreach (var line in existingLines)
            {
                _squadLoadoutVBox.RemoveChild(line);
                line.QueueFree();
            }
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

    public void PopulateSquadLoadout(List<Tuple<List<string>, int, int>> weaponSets)
    {
        ClearSquadLoadout();
        PackedScene weaponSetSelectionScene = GD.Load<PackedScene>("res://Scenes/ChapterScreen/weapon_set_selection.tscn");
        foreach (var weaponSet in weaponSets)
        {
            
            WeaponSetSelectionView view = (WeaponSetSelectionView)weaponSetSelectionScene.Instantiate();
            _squadLoadoutVBox.AddChild(view);
            view.Initialize(weaponSet.Item1, weaponSet.Item2, weaponSet.Item3);
        }

    }
}
