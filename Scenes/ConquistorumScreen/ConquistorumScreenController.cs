using Godot;
using System;

public partial class ConquistorumScreenController : Control
{
    ConquistorumScreenView _view;
    public event EventHandler CloseButtonPressed;

    public override void _Ready()
    {
        _view = GetNode<ConquistorumScreenView>("ConquistorumScreenView");
        _view.CloseButtonPressed += (object? sender, EventArgs e) => CloseButtonPressed?.Invoke(sender, e);
    }
}
