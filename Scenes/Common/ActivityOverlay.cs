using Godot;
using OnlyWar.Helpers.UI;

public partial class ActivityOverlay : Control
{
    private static readonly string[] SpinnerFrames = { "|", "/", "—", "\\" };

    private Label _operationLabel;
    private Label _messageLabel;
    private Label _spinnerLabel;
    private double _spinnerElapsed;
    private int _spinnerIndex;

    public override void _Ready()
    {
        _operationLabel = GetNode<Label>("CenterPanel/MarginContainer/Content/OperationLabel");
        _messageLabel = GetNode<Label>("CenterPanel/MarginContainer/Content/MessageLabel");
        _spinnerLabel = GetNode<Label>("CenterPanel/MarginContainer/Content/ActivityRow/SpinnerLabel");
        OnlyWarStyle.ApplyContentPanel(GetNode<PanelContainer>("CenterPanel"));
        SetProcess(false);
    }

    public void ShowBusy(string operation, string message)
    {
        _operationLabel.Text = operation.ToUpperInvariant();
        _messageLabel.Text = message;
        _spinnerIndex = 0;
        _spinnerElapsed = 0;
        _spinnerLabel.Text = SpinnerFrames[_spinnerIndex];
        Visible = true;
        SetProcess(true);
    }

    public void HideBusy()
    {
        Visible = false;
        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        _spinnerElapsed += delta;
        if (_spinnerElapsed < 0.16)
        {
            return;
        }

        _spinnerElapsed = 0;
        _spinnerIndex = (_spinnerIndex + 1) % SpinnerFrames.Length;
        _spinnerLabel.Text = SpinnerFrames[_spinnerIndex];
    }
}
