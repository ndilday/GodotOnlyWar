using Godot;
using OnlyWar.Helpers.UI;

public partial class TransientFeedbackOverlay : Control
{
    private PanelContainer _panel;
    private Label _messageLabel;
    private Timer _timer;

    public override void _Ready()
    {
        _panel = GetNode<PanelContainer>("Panel");
        _messageLabel = GetNode<Label>("Panel/Margin/Message");
        _timer = GetNode<Timer>("Timer");
        _timer.Timeout += HideFeedback;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void ShowSuccess(string message, double seconds = 3.0)
    {
        ShowFeedback(message, OnlyWarEventTone.Normal, seconds);
    }

    public void ShowWarning(string message, double seconds = 4.5)
    {
        ShowFeedback(message, OnlyWarEventTone.Warning, seconds);
    }

    public void ShowError(string message, double seconds = 6.0)
    {
        ShowFeedback(message, OnlyWarEventTone.Critical, seconds);
    }

    public void HideFeedback()
    {
        _timer.Stop();
        Visible = false;
    }

    private void ShowFeedback(string message, OnlyWarEventTone tone, double seconds)
    {
        _messageLabel.Text = message ?? string.Empty;
        _messageLabel.AddThemeColorOverride("font_color", tone == OnlyWarEventTone.Critical
            ? OnlyWarStyle.Critical
            : OnlyWarStyle.BodyText);
        OnlyWarStyle.ApplyEventPanel(_panel, tone);
        Visible = true;
        MoveToFront();
        _timer.Start(System.Math.Max(0.5, seconds));
    }
}

