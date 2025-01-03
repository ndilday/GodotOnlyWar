using Godot;
using System;

public partial class BottomMenu : Control
{
	public event EventHandler CompanyButtonPressed;

	public override void _Ready()
	{
		Button button = GetNode<Button>("./Panel/MarginContainer/HBoxContainer/ChapterButton");
		button.Pressed += () => CompanyButtonPressed?.Invoke(this, EventArgs.Empty);
	}
}
