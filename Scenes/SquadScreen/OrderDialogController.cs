using Godot;
using System;

public partial class OrderDialogController : Control
{
    private OrderDialogView _view;

    public override void _Ready()
    {
        _view = GetNode<OrderDialogView>("OrderDialogView");
    }
}
