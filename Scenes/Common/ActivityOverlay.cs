using Godot;
using System;

public partial class ActivityOverlay : Control
{
    private static readonly string[] SpinnerFrames = { "|", "/", "—", "\\" };
    // Status text can change every battle turn; redrawing it more often than this only flickers.
    private const double StatusPollSeconds = 0.25;

    private Label _operationLabel;
    private Label _messageLabel;
    private Label _statusLabel;
    private Label _spinnerLabel;
    private double _spinnerElapsed;
    private int _spinnerIndex;
    private Func<string> _statusSource;
    private double _statusElapsed;

    public override void _Ready()
    {
        _operationLabel = GetNode<Label>("CenterPanel/MarginContainer/Content/OperationLabel");
        _messageLabel = GetNode<Label>("CenterPanel/MarginContainer/Content/MessageLabel");
        _statusLabel = GetNode<Label>("CenterPanel/MarginContainer/Content/StatusLabel");
        _spinnerLabel = GetNode<Label>("CenterPanel/MarginContainer/Content/ActivityRow/SpinnerLabel");
        OnlyWarStyle.ApplyContentPanel(GetNode<PanelContainer>("CenterPanel"));
        SetProcess(false);
    }

    /// <summary>
    /// Shows the overlay. When <paramref name="statusSource"/> is given, the overlay polls it
    /// a few times a second and shows its text under the message. The source may be written
    /// by another thread; it is only ever read here, on the main thread.
    /// </summary>
    public void ShowBusy(string operation, string message, Func<string> statusSource = null)
    {
        _operationLabel.Text = operation.ToUpperInvariant();
        _messageLabel.Text = message;
        _statusSource = statusSource;
        _statusElapsed = 0;
        RefreshStatus();
        _spinnerIndex = 0;
        _spinnerElapsed = 0;
        _spinnerLabel.Text = SpinnerFrames[_spinnerIndex];
        Visible = true;
        SetProcess(true);
    }

    public void HideBusy()
    {
        Visible = false;
        _statusSource = null;
        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        _statusElapsed += delta;
        if (_statusElapsed >= StatusPollSeconds)
        {
            _statusElapsed = 0;
            RefreshStatus();
        }

        _spinnerElapsed += delta;
        if (_spinnerElapsed < 0.16)
        {
            return;
        }

        _spinnerElapsed = 0;
        _spinnerIndex = (_spinnerIndex + 1) % SpinnerFrames.Length;
        _spinnerLabel.Text = SpinnerFrames[_spinnerIndex];
    }

    private void RefreshStatus()
    {
        string status = _statusSource?.Invoke() ?? string.Empty;
        if (_statusLabel.Text != status)
        {
            _statusLabel.Text = status;
        }
        _statusLabel.Visible = status.Length > 0;
    }
}
