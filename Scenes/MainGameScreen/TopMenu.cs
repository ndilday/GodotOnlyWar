using Godot;
using System;

public partial class TopMenu : Control
{
    public event EventHandler SaveButtonPressed;

    public override void _Ready()
    {
        GetNode<Button>("Panel/SaveButton").Pressed += () => SaveButtonPressed?.Invoke(this, EventArgs.Empty);
    }

    public void SetDebugText(string newText)
	{
		GetNode<Label>("Panel/DebugLabel").Text = newText;
	}

	public void SetDateText(string newText)
	{
		GetNode<Label>("Panel/DateLabel").Text = newText;
	}

	public void SetSaveButtonText(string newText)
	{
        GetNode<Button>("Panel/SaveButton").Text = newText;
    }
}
