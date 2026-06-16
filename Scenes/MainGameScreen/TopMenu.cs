using Godot;
using OnlyWar.Helpers.UI;
using System;

public partial class TopMenu : Control
{
    public event EventHandler SaveButtonPressed;
    private Label _dateLabel;
    private Label _debugLabel;
    private Button _saveButton;

    public override void _Ready()
    {
        _dateLabel = GetNode<Label>("Panel/MarginContainer/CommandRow/DateGroup/DateLabel");
        _debugLabel = GetNode<Label>("Panel/MarginContainer/CommandRow/DebugLabel");
        _saveButton = GetNode<Button>("Panel/MarginContainer/CommandRow/SaveButton");
        IconAtlas.Apply(_saveButton, "save", 116);
        _saveButton.Pressed += () => SaveButtonPressed?.Invoke(this, EventArgs.Empty);
    }

    public void SetDebugText(string newText)
	{
		_debugLabel.Text = newText;
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
