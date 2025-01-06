using Godot;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;

public partial class SoldierView : Control
{
	public event EventHandler<int> TransferTargetSelected;
	public event EventHandler CloseButtonPressed;

    private VBoxContainer _soldierDataVBox;
	private RichTextLabel _soldierHistoryRichText;
	private RichTextLabel _soldierAwardsRichText;
	private RichTextLabel _sergeantReportRichText;
	private MenuButton _transferButton;
	private Button _closeButton;


	public override void _Ready()
	{
		_soldierDataVBox = GetNode<VBoxContainer>("DataPanel/VBoxContainer");
		_soldierHistoryRichText = GetNode<RichTextLabel>("HistoryPanel/RichTextLabel");
		_soldierAwardsRichText = GetNode<RichTextLabel>("AwardsPanel/RichTextLabel");
		_sergeantReportRichText = GetNode<RichTextLabel>("RecommendationPanel/RichTextLabel");
		_transferButton = GetNode<MenuButton>("TopMenuPanel/MarginContainer/HBoxContainer/TransferButton");
        _closeButton = GetNode<Button>("TopMenuPanel/CloseButton");
		_closeButton.Pressed += () => CloseButtonPressed?.Invoke(this, EventArgs.Empty);
        _transferButton.GetPopup().IndexPressed += (long index) => TransferTargetSelected?.Invoke(this, (int)index);
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

	public void PopulateSoldierAwards(IReadOnlyList<string> awardEntries)
	{
		// Clear existing text (if any)
		_soldierAwardsRichText.Clear();
		// Add each entry with a newline
		foreach (string entry in awardEntries)
		{
			_soldierAwardsRichText.AddText(entry + "\n");
		}
	}

	public void PopulateSergeantReport(string report)
	{
		// Clear existing text (if any)
		_sergeantReportRichText.Clear();
		// Add the report
		_sergeantReportRichText.AddText(report);
	}

	public void PopulateTransferOptions(List<string> openings)
	{
		_transferButton.GetPopup().Clear();
		foreach (string opening in openings)
		{
			_transferButton.GetPopup().AddItem(opening);
		}
	}
}
