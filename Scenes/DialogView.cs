using Godot;
using OnlyWar.Helpers.UI;
using System;

public partial class DialogView : Control
{
    private Button _closeButton;

    public event EventHandler CloseButtonPressed;

    public override void _Ready()
    {
        _closeButton = GetNode<Button>("CloseButton");
        Control modalScrim = GetNodeOrNull<Control>("ModalScrim");
        if (modalScrim != null)
        {
            MoveChild(modalScrim, 0);
        }
        // A few dialogs intentionally turn the inherited control into a labelled acknowledgement
        // action. Only iconize the stock dismissal affordance.
        if (string.IsNullOrWhiteSpace(_closeButton.Text) || _closeButton.Text == "X")
        {
            IconAtlas.ApplyIconButton(_closeButton, "close", 40, 28);
            _closeButton.TooltipText = "Close";
        }
        _closeButton.ZIndex = 100;
        _closeButton.MouseFilter = MouseFilterEnum.Stop;
        MoveChild(_closeButton, GetChildCount() - 1);
        _closeButton.Pressed += () => CloseButtonPressed?.Invoke(this, EventArgs.Empty);
    }
}
