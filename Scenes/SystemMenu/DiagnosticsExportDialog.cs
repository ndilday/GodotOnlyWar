using Godot;
using OnlyWar.Helpers.UI;
using System;
using System.Globalization;

public sealed class DiagnosticsExportRequestedEventArgs : EventArgs
{
    public DiagnosticsExportRequestedEventArgs(string destinationPath, bool includeCurrentCampaign)
    {
        DestinationPath = destinationPath;
        IncludeCurrentCampaign = includeCurrentCampaign;
    }

    public string DestinationPath { get; }
    public bool IncludeCurrentCampaign { get; }
}

public partial class DiagnosticsExportDialog : Control
{
    private LineEdit _destination;
    private CheckBox _includeCampaign;
    private Label _statusLabel;
    private Button _exportButton;
    private Button _chooseButton;
    private FileDialog _fileDialog;

    public event EventHandler CancelRequested;
    public event EventHandler<DiagnosticsExportRequestedEventArgs> ExportRequested;

    public override void _Ready()
    {
        _destination = GetNode<LineEdit>("Panel/Margin/Content/DestinationRow/Destination");
        _includeCampaign = GetNode<CheckBox>("Panel/Margin/Content/IncludeCampaign");
        _statusLabel = GetNode<Label>("Panel/Margin/Content/StatusLabel");
        _exportButton = GetNode<Button>("Panel/Margin/Content/Buttons/ExportButton");
        _chooseButton = GetNode<Button>("Panel/Margin/Content/DestinationRow/ChooseButton");
        _fileDialog = GetNode<FileDialog>("FileDialog");

        OnlyWarStyle.ApplyContentPanel(GetNode<PanelContainer>("Panel"));
        _fileDialog.Access = FileDialog.AccessEnum.Filesystem;
        _fileDialog.FileMode = FileDialog.FileModeEnum.SaveFile;
        _fileDialog.Filters = new[] { "*.zip ; ZIP archives" };
        _fileDialog.UseNativeDialog = true;

        GetNode<Button>("Panel/Margin/Content/Header/CloseButton").Pressed += RequestCancel;
        GetNode<Button>("Panel/Margin/Content/Buttons/CancelButton").Pressed += RequestCancel;
        _chooseButton.Pressed += ChooseDestination;
        _exportButton.Pressed += RequestExport;
        _destination.TextChanged += _ => ClearStatus();
        _fileDialog.FileSelected += OnFileSelected;
    }

    public void ShowDialog(string suggestedFileName = null)
    {
        _includeCampaign.ButtonPressed = false;
        _destination.Text = string.Empty;
        _fileDialog.CurrentFile = string.IsNullOrWhiteSpace(suggestedFileName)
            ? "onlywar-diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".zip"
            : suggestedFileName;
        SetBusy(false);
        ClearStatus();
        Visible = true;
        MoveToFront();
        _chooseButton.GrabFocus();
    }

    public void SetBusy(bool busy)
    {
        _exportButton.Disabled = busy;
        _chooseButton.Disabled = busy;
        _includeCampaign.Disabled = busy;
        GetNode<Button>("Panel/Margin/Content/Buttons/CancelButton").Disabled = busy;
        if (busy)
        {
            SetStatus("Building diagnostic bundle…", false);
        }
    }

    public void ShowExportResult(bool successful, string message)
    {
        SetBusy(false);
        SetStatus(message, !successful);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (Visible
            && @event is InputEventKey keyEvent
            && keyEvent.Pressed
            && !keyEvent.Echo
            && (keyEvent.Keycode == Key.X || keyEvent.PhysicalKeycode == Key.X)
            && GetViewport().GuiGetFocusOwner() is not (LineEdit or TextEdit))
        {
            RequestCancel();
            GetViewport().SetInputAsHandled();
        }
    }

    private void ChooseDestination()
    {
        _fileDialog.PopupCentered(new Vector2I(900, 620));
    }

    private void OnFileSelected(string path)
    {
        _destination.Text = path;
        _destination.CaretColumn = path.Length;
    }

    private void RequestExport()
    {
        if (string.IsNullOrWhiteSpace(_destination.Text))
        {
            SetStatus("Choose where the diagnostic bundle should be written.", true);
            return;
        }

        ExportRequested?.Invoke(this, new DiagnosticsExportRequestedEventArgs(
            _destination.Text.Trim(),
            _includeCampaign.ButtonPressed));
    }

    private void RequestCancel()
    {
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SetStatus(string message, bool error)
    {
        _statusLabel.Text = message ?? string.Empty;
        _statusLabel.AddThemeColorOverride("font_color", error ? OnlyWarStyle.Critical : OnlyWarStyle.BodyText);
        _statusLabel.Visible = !string.IsNullOrWhiteSpace(message);
    }

    private void ClearStatus()
    {
        _statusLabel.Text = string.Empty;
        _statusLabel.Visible = false;
    }
}
