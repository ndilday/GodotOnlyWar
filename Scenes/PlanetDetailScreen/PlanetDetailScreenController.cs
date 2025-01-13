using Godot;
using System;

public partial class PlanetDetailScreenController : Control
{
	PlanetDetailScreenView _view;
	public event EventHandler CloseButtonPressed;

	public override void _Ready()
	{
		_view = GetNode<PlanetDetailScreenView>("PlanetDetailScreenView");
		_view.CloseButtonPressed += (object sender, EventArgs e) => CloseButtonPressed?.Invoke(this, e);
	}
}
