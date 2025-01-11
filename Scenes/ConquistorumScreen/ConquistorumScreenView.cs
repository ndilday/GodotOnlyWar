using Godot;
using System;

public partial class ConquistorumScreenView : Control
{
    private Button _closeButton;
    private VBoxContainer _squadVBox;
    private ButtonGroup _squadButtonGroup;

    public event EventHandler CloseButtonPressed;
    public event EventHandler<int> SquadButtonPressed;

    public override void _Ready()
    {
        _closeButton = GetNode<Button>("CloseButton");
        _closeButton.Pressed += () => CloseButtonPressed?.Invoke(this, EventArgs.Empty);
        _squadVBox = GetNode<VBoxContainer>("InjuryReportPanel/ScrollContainer/VBoxContainer");
        _squadButtonGroup = new ButtonGroup();
    }
}
