using Godot;
using OnlyWar.Helpers.UI;
using System;

public partial class TopMenu : Control
{
    public event EventHandler SaveButtonPressed;
    public event EventHandler SystemOptionsButtonPressed;
    private Label _screenLabel;
    private Label _dateLabel;
    private Label _debugLabel;
    private Button _saveButton;
    private Button _systemOptionsButton;

    public override void _Ready()
    {
        _screenLabel = GetNode<Label>("Panel/MarginContainer/CommandRow/ResourceGroup/ScreenLabel");
        _dateLabel = GetNode<Label>("Panel/MarginContainer/CommandRow/DateGroup/DateLabel");
        _debugLabel = GetNode<Label>("Panel/MarginContainer/CommandRow/DebugLabel");
        _saveButton = GetNode<Button>("Panel/MarginContainer/CommandRow/SaveButton");
        _systemOptionsButton = GetNode<Button>("Panel/SystemOptionsButton");
        IconAtlas.Apply(_saveButton, "save", 116);
        IconAtlas.ApplyIconButton(_systemOptionsButton, "settings");
        _saveButton.Pressed += () => SaveButtonPressed?.Invoke(this, EventArgs.Empty);
        _systemOptionsButton.Pressed += () => SystemOptionsButtonPressed?.Invoke(this, EventArgs.Empty);
    }

    public void SetDebugText(string newText)
	{
		_debugLabel.Text = newText;
	}

    public void SetScreenText(string newText)
    {
        _screenLabel.Text = newText;
    }

	public void SetDateText(string newText)
	{
		_dateLabel.Text = newText;
	}

	public void SetSaveButtonText(string newText)
	{
        _saveButton.Text = newText;
    }
}
