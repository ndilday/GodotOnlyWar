using Godot;
using OnlyWar.Helpers.UI;
using System;

public partial class DestructiveNavigationDialog : Control
{
    private Label _titleLabel;
    private Label _messageLabel;
    private Label _errorLabel;
    private Button _saveButton;
    private Button _discardButton;
    private Button _cancelButton;

    public event EventHandler SaveAndContinueRequested;
    public event EventHandler DiscardAndContinueRequested;
    public event EventHandler Cancelled;

    public override void _Ready()
    {
        _titleLabel = GetNode<Label>("Panel/Margin/Content/Title");
        _messageLabel = GetNode<Label>("Panel/Margin/Content/Message");
        _errorLabel = GetNode<Label>("Panel/Margin/Content/ErrorLabel");
        _saveButton = GetNode<Button>("Panel/Margin/Content/Buttons/SaveButton");
        _discardButton = GetNode<Button>("Panel/Margin/Content/Buttons/DiscardButton");
        _cancelButton = GetNode<Button>("Panel/Margin/Content/Buttons/CancelButton");

        OnlyWarStyle.ApplyContentPanel(GetNode<PanelContainer>("Panel"));
        _saveButton.Pressed += () => SaveAndContinueRequested?.Invoke(this, EventArgs.Empty);
        _discardButton.Pressed += () => DiscardAndContinueRequested?.Invoke(this, EventArgs.Empty);
        _cancelButton.Pressed += Cancel;
        GetNode<Button>("Panel/Margin/Content/HeaderCloseButton").Pressed += Cancel;
    }

    public void ShowFor(string actionName, string detail = null, bool canSave = true)
    {
        _titleLabel.Text = "UNSAVED PROGRESS";
        _messageLabel.Text = string.IsNullOrWhiteSpace(detail)
            ? $"Save before {actionName}? Changes since the last recovery point will otherwise be lost."
            : detail;
        _saveButton.Disabled = !canSave;
        _saveButton.TooltipText = canSave ? string.Empty : "Saving is not available in the current state.";
        SetBusy(false);
        ShowError(null);
        Visible = true;
        MoveToFront();
        (canSave ? _saveButton : _cancelButton).GrabFocus();
    }

    public void SetBusy(bool busy)
    {
        _saveButton.Disabled = busy;
        _discardButton.Disabled = busy;
        _cancelButton.Disabled = busy;
    }

    public void ShowError(string message)
    {
        SetBusy(false);
        _errorLabel.Text = message ?? string.Empty;
        _errorLabel.Visible = !string.IsNullOrWhiteSpace(message);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (Visible
            && @event is InputEventKey keyEvent
            && keyEvent.Pressed
            && !keyEvent.Echo
            && (keyEvent.Keycode == Key.X || keyEvent.PhysicalKeycode == Key.X))
        {
            Cancel();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Cancel()
    {
        Cancelled?.Invoke(this, EventArgs.Empty);
    }
}
