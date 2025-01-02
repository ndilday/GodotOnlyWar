using Godot;
using System;

public partial class TopMenu : Control
{
	public void SetDebugText(string newText)
	{
		GetNode<Label>("Panel/DebugLabel").Text = newText;
	}
}
