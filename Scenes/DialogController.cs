using Godot;
using System;

public partial class DialogController : Control
{
	protected PlanetTacticalScreenView _view;

	public event EventHandler CloseButtonPressed;

	public override void _Ready()
	{
		_view = GetNode<PlanetTacticalScreenView>("DialogView");
		_view.CloseButtonPressed += (object sender, EventArgs e) => CloseButtonPressed?.Invoke(this, e);
	}
}
