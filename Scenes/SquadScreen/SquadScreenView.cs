using Godot;
using System;
using System.Collections.Generic;

public partial class SquadScreenView : DialogView
{
    private VBoxContainer _squadDetailsVBox;

    public override void _Ready()
    {
        base._Ready();
        _squadDetailsVBox = GetNode<VBoxContainer>("DataPanel/VBoxContainer");
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
}
