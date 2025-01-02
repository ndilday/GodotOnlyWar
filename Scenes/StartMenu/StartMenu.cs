using Godot;
using System;

public partial class StartMenu : Control
{
	[Signal]
	public delegate void NewGameEventHandler();
	public void OnNewGameButtonPressed()
	{
		EmitSignal(nameof(NewGameEventHandler));
	}

}
