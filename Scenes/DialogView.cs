using Godot;
using System;

public partial class DialogView : Control
{
    private Button _closeButton;

    public event EventHandler CloseButtonPressed;

    public override void _Ready()
    {
        _closeButton = GetNode<Button>("CloseButton");
        _closeButton.Pressed += () => CloseButtonPressed?.Invoke(this, EventArgs.Empty);
    }
}
