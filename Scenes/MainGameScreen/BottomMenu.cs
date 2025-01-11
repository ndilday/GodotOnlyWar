using Godot;
using System;

public partial class BottomMenu : Control
{
	public event EventHandler ChapterButtonPressed;
	public event EventHandler ConquistorumButtonPressed;
	public event EventHandler EndTurnButtonPressed;

	public override void _Ready()
	{
		Button button = GetNode<Button>("./Panel/MarginContainer/HBoxContainer/ChapterButton");
		button.Pressed += () => ChapterButtonPressed?.Invoke(this, EventArgs.Empty);
		Button endTurnButton = GetNode<Button>("./Panel/MarginContainer/HBoxContainer/EndTurnButton");
		endTurnButton.Pressed += () => EndTurnButtonPressed?.Invoke(this, EventArgs.Empty);
		Button conquistorumButton = GetNode<Button>("./Panel/MarginContainer/HBoxContainer/ConquistorumButton");
		conquistorumButton.Pressed += () => ConquistorumButtonPressed?.Invoke(this, EventArgs.Empty);
	}
}
