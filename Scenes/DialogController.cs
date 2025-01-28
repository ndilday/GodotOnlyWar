using Godot;
using System;

public partial class DialogController : Control
{
	public event EventHandler CloseButtonPressed;

	public override void _Ready()
	{
		foreach(Node child in GetChildren())
		{
			if(child is DialogView)
			{
				DialogView view = (DialogView)child;
				view.CloseButtonPressed += (object sender, EventArgs e) => CloseButtonPressed?.Invoke(this, e);
				break;
			}
		}
		
	}
}
