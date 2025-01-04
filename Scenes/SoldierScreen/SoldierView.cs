using Godot;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;

public partial class SoldierView : Control
{
	private VBoxContainer _soldierDataVBox;
	private RichTextLabel _soldierHistoryRichText;

	public override void _Ready()
	{
		_soldierDataVBox = GetNode<VBoxContainer>("DataPanel/VBoxContainer");
        _soldierHistoryRichText = GetNode<RichTextLabel>("HistoryPanel/RichTextLabel");
    }
	public void PopulateSoldierData(IReadOnlyList<Tuple<string, string>> stringPairs)
	{
		var existingLines = _soldierDataVBox.GetChildren();
		if (existingLines != null)
		{
			foreach (var line in existingLines)
			{
				_soldierDataVBox.RemoveChild(line);
				line.QueueFree();
			}
		}
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
		lineLabel.HorizontalAlignment = HorizontalAlignment.Left;
        lineLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        linePanel.AddChild(lineLabel);
		Label lineValue = new Label();
		lineValue.Text = value;
		lineValue.HorizontalAlignment = HorizontalAlignment.Right;
        lineValue.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        linePanel.AddChild(lineValue);
		_soldierDataVBox.AddChild(linePanel);
	}

	public void PopulateSoldierHistory(IReadOnlyList<string> historyEntries)
	{
        // Clear existing text (if any)
        _soldierHistoryRichText.Clear();

        // Add each entry with a newline
        foreach (string entry in historyEntries)
        {
            _soldierHistoryRichText.AddText(entry + "\n");
        }
    }

	
}
