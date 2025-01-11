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
	private RichTextLabel _soldierInjuryRichText;
	private RichTextLabel _sergeantReportRichText;
	private MenuButton _transferButton;
	private Button _closeButton;


	public override void _Ready()
	{
		_soldierDataVBox = GetNode<VBoxContainer>("DataPanel/VBoxContainer");
		_soldierHistoryRichText = GetNode<RichTextLabel>("HistoryPanel/RichTextLabel");
		_soldierAwardsRichText = GetNode<RichTextLabel>("AwardsPanel/RichTextLabel");
		_sergeantReportRichText = GetNode<RichTextLabel>("RecommendationPanel/RichTextLabel");
		_soldierInjuryRichText = GetNode<RichTextLabel>("InjuryPanel/RichTextLabel");
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

	public void PopulateSoldierInjuryReport(string report)
	{
		// Clear existing text (if any)
		_soldierInjuryRichText.Clear();
		// Add the injury summary
		_soldierInjuryRichText.Text = report;
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

	private string GenerateSoldierInjurySummary(ISoldier selectedSoldier)
	{
		string summary = selectedSoldier.Name + "\n";
		byte recoveryTime = 0;
		bool isSevered = false;
		foreach (HitLocation hl in selectedSoldier.Body.HitLocations)
		{
			if (hl.Wounds.WoundTotal != 0)
			{
				if (hl.IsSevered)
				{
					isSevered = true;
				}
				byte woundTime = hl.Wounds.RecoveryTimeLeft();
				if (woundTime > recoveryTime)
				{
					recoveryTime = woundTime;
				}
				summary += hl.ToString() + "\n";
			}
		}
		if (isSevered)
		{
			summary += selectedSoldier.Name +
				" will be unable to perform field duties until receiving cybernetic replacements\n";
		}
		else if (recoveryTime > 0)
		{
			summary += selectedSoldier.Name +
				" requires " + recoveryTime.ToString() + " weeks to be fully fit for duty\n";
		}
		else
		{
			summary += selectedSoldier.Name +
				" is fully fit and ready to serve the Emperor\n";
		}
		return summary;
	}
}
