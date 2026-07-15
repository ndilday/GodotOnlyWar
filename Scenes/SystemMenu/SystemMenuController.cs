using Godot;
using OnlyWar.Helpers.UI;
using System;

public enum SystemMenuContext
{
    Campaign,
    TitleScreen
}

public sealed class WarningPreferencesChangedEventArgs : EventArgs
{
    public WarningPreferencesChangedEventArgs(bool idleSquads, bool actionableTaskForces, bool specialMissionOpportunities)
    {
        WarnIdleDeployableSquads = idleSquads;
        WarnActionableTaskForces = actionableTaskForces;
        WarnSpecialMissionOpportunities = specialMissionOpportunities;
    }

    public bool WarnIdleDeployableSquads { get; }
    public bool WarnActionableTaskForces { get; }
    public bool WarnSpecialMissionOpportunities { get; }
}

public partial class SystemMenuController : Control
{
    private Label _lastSaveLabel;
    private Label _titleLabel;
    private Label _subtitleLabel;
    private Label _escapeHint;
    private Button _resumeButton;
    private Button _titleButton;
    private Button _saveButton;
    private Button _loadButton;
    private CheckBox _idleSquads;
    private CheckBox _taskForces;
    private CheckBox _specialMissions;
    private bool _settingPreferences;
    private SystemMenuContext _context;

    public event EventHandler ResumeRequested;
    public event EventHandler CloseRequested;
    public event EventHandler SaveRequested;
    public event EventHandler LoadRequested;
    public event EventHandler ReturnToTitleRequested;
    public event EventHandler QuitRequested;
    public event EventHandler ExportDiagnosticsRequested;
    public event EventHandler<WarningPreferencesChangedEventArgs> WarningPreferencesChanged;

    public override void _Ready()
    {
        _lastSaveLabel = GetNode<Label>("Panel/Margin/Content/LastSaveLabel");
        _titleLabel = GetNode<Label>("Panel/Margin/Content/Title");
        _subtitleLabel = GetNode<Label>("Panel/Margin/Content/Subtitle");
        _escapeHint = GetNode<Label>("Panel/Margin/Content/EscapeHint");
        _resumeButton = GetNode<Button>("Panel/Margin/Content/Actions/ResumeButton");
        _titleButton = GetNode<Button>("Panel/Margin/Content/Actions/TitleButton");
        _saveButton = GetNode<Button>("Panel/Margin/Content/Actions/SaveButton");
        _loadButton = GetNode<Button>("Panel/Margin/Content/Actions/LoadButton");
        _idleSquads = GetNode<CheckBox>("Panel/Margin/Content/WarningPanel/Margin/Warnings/IdleSquads");
        _taskForces = GetNode<CheckBox>("Panel/Margin/Content/WarningPanel/Margin/Warnings/TaskForces");
        _specialMissions = GetNode<CheckBox>("Panel/Margin/Content/WarningPanel/Margin/Warnings/SpecialMissions");

        OnlyWarStyle.ApplyContentPanel(GetNode<PanelContainer>("Panel"));
        OnlyWarStyle.ApplyInsetPanel(GetNode<PanelContainer>("Panel/Margin/Content/WarningPanel"));

        _resumeButton.Pressed += RequestClose;
        _saveButton.Pressed += () => SaveRequested?.Invoke(this, EventArgs.Empty);
        _loadButton.Pressed += () => LoadRequested?.Invoke(this, EventArgs.Empty);
        _titleButton.Pressed += () => ReturnToTitleRequested?.Invoke(this, EventArgs.Empty);
        GetNode<Button>("Panel/Margin/Content/Actions/QuitButton").Pressed += () => QuitRequested?.Invoke(this, EventArgs.Empty);
        GetNode<Button>("Panel/Margin/Content/ExportButton").Pressed += () => ExportDiagnosticsRequested?.Invoke(this, EventArgs.Empty);
        _idleSquads.Toggled += OnWarningToggled;
        _taskForces.Toggled += OnWarningToggled;
        _specialMissions.Toggled += OnWarningToggled;
        SetContext(SystemMenuContext.Campaign);
    }

    public void ShowMenu()
    {
        Visible = true;
        MoveToFront();
        _resumeButton.GrabFocus();
    }

    public void CloseMenu()
    {
        Visible = false;
    }

    public void SetLastSaveStatus(string status)
    {
        _lastSaveLabel.Text = string.IsNullOrWhiteSpace(status) ? "No recovery point created this session." : status;
    }

    public void SetSaveAvailability(bool canSave, bool canLoad)
    {
        _saveButton.Disabled = !canSave;
        _loadButton.Disabled = !canLoad;
    }

    public void SetContext(SystemMenuContext context)
    {
        _context = context;
        if (!IsNodeReady())
        {
            return;
        }

        bool campaign = context == SystemMenuContext.Campaign;
        _titleLabel.Text = campaign ? "SYSTEM MENU" : "OPTIONS";
        _subtitleLabel.Text = campaign
            ? "Campaign control and release support"
            : "Global preferences and release support";
        _resumeButton.Text = campaign ? "Resume" : "Close";
        _escapeHint.Text = campaign ? "ESC — Resume" : "ESC — Close";
        _saveButton.Visible = campaign;
        _loadButton.Visible = campaign;
        _titleButton.Visible = campaign;
        _lastSaveLabel.Visible = campaign;
    }

    public void SetWarningPreferences(bool idleSquads, bool actionableTaskForces, bool specialMissionOpportunities)
    {
        _settingPreferences = true;
        _idleSquads.ButtonPressed = idleSquads;
        _taskForces.ButtonPressed = actionableTaskForces;
        _specialMissions.ButtonPressed = specialMissionOpportunities;
        _settingPreferences = false;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!Visible || @event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo)
        {
            return;
        }

        if (keyEvent.Keycode == Key.Escape)
        {
            RequestClose();
            GetViewport().SetInputAsHandled();
        }
    }

    private void RequestClose()
    {
        if (_context == SystemMenuContext.Campaign)
        {
            ResumeRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnWarningToggled(bool _)
    {
        if (_settingPreferences)
        {
            return;
        }

        WarningPreferencesChanged?.Invoke(this, new WarningPreferencesChangedEventArgs(
            _idleSquads.ButtonPressed,
            _taskForces.ButtonPressed,
            _specialMissions.ButtonPressed));
    }
}
