using Godot;
using OnlyWar.Helpers.UI;
using System;
using System.Globalization;

public partial class TopMenu : Control
{
    public event EventHandler SystemOptionsButtonPressed;
    private Label _screenLabel;
    private Label _requisitionAmountLabel;
    private Label _dateLabel;
    private Label _debugLabel;
    private Button _systemOptionsButton;

    public override void _Ready()
    {
        _screenLabel = GetNode<Label>("Panel/MarginContainer/CommandRow/CenterSection/ScreenLabel");
        _requisitionAmountLabel = GetNode<Label>("Panel/MarginContainer/CommandRow/LeftSection/RequisitionAmountLabel");
        _dateLabel = GetNode<Label>("Panel/MarginContainer/CommandRow/RightSection/DateGroup/DateLabel");
        _debugLabel = GetNodeOrNull<Label>("Panel/MarginContainer/CommandRow/DebugLabel");
        _systemOptionsButton = GetNode<Button>("Panel/MarginContainer/CommandRow/RightSection/SystemOptionsButton");
        IconAtlas.ApplyIconButton(_systemOptionsButton, "settings");
        _systemOptionsButton.Pressed += () => SystemOptionsButtonPressed?.Invoke(this, EventArgs.Empty);
    }

    public void SetDebugText(string newText)
	{
		if (_debugLabel != null)
        {
            _debugLabel.Text = newText;
        }
	}

    public void SetScreenText(string newText)
    {
        _screenLabel.Text = newText;
    }

    public void SetRequisitionAmount(int amount)
    {
        _requisitionAmountLabel.Text = amount.ToString("N0", CultureInfo.InvariantCulture);
    }

	public void SetDateText(string newText)
	{
		_dateLabel.Text = newText;
	}

}
