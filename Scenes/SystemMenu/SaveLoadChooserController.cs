using Godot;
using OnlyWar.Helpers.UI;
using OnlyWar.Helpers.UI.SystemMenu;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

public partial class SaveLoadChooserController : Control
{
    private enum PendingConfirmation
    {
        None,
        Overwrite,
        Delete
    }

    private Label _titleLabel;
    private Label _emptyLabel;
    private Label _validationLabel;
    private VBoxContainer _manualRows;
    private VBoxContainer _autosaveRows;
    private VBoxContainer _saveControls;
    private LineEdit _saveName;
    private Button _saveButton;
    private Button _deleteButton;
    private Button _primaryButton;
    private Control _confirmationOverlay;
    private Label _confirmationTitle;
    private Label _confirmationMessage;
    private Button _confirmationButton;

    private readonly List<SaveSlotViewModel> _entries = new();
    private readonly Dictionary<string, Button> _rowButtons = new(StringComparer.Ordinal);
    private SaveSlotViewModel _selected;
    private SaveChooserMode _mode;
    private PendingConfirmation _pendingConfirmation;

    public event EventHandler CancelRequested;
    public event EventHandler RefreshRequested;
    public event EventHandler<SaveSlotRequestedEventArgs> SaveRequested;
    public event EventHandler<SaveSlotSelectionEventArgs> LoadRequested;
    public event EventHandler<SaveSlotSelectionEventArgs> DeleteRequested;

    public SaveChooserMode Mode => _mode;
    public SaveSlotViewModel SelectedSlot => _selected;

    public override void _Ready()
    {
        _titleLabel = GetNode<Label>("Panel/Margin/Content/Header/Title");
        _emptyLabel = GetNode<Label>("Panel/Margin/Content/Slots/EmptyLabel");
        _validationLabel = GetNode<Label>("Panel/Margin/Content/ValidationLabel");
        _manualRows = GetNode<VBoxContainer>("Panel/Margin/Content/Slots/Scroll/Sections/ManualRows");
        _autosaveRows = GetNode<VBoxContainer>("Panel/Margin/Content/Slots/Scroll/Sections/AutosaveRows");
        _saveControls = GetNode<VBoxContainer>("Panel/Margin/Content/SaveControls");
        _saveName = GetNode<LineEdit>("Panel/Margin/Content/SaveControls/NameRow/SaveName");
        _saveButton = GetNode<Button>("Panel/Margin/Content/SaveControls/NameRow/SaveButton");
        _deleteButton = GetNode<Button>("Panel/Margin/Content/Footer/DeleteButton");
        _primaryButton = GetNode<Button>("Panel/Margin/Content/Footer/PrimaryButton");
        _confirmationOverlay = GetNode<Control>("ConfirmationOverlay");
        _confirmationTitle = GetNode<Label>("ConfirmationOverlay/Panel/Margin/Content/Title");
        _confirmationMessage = GetNode<Label>("ConfirmationOverlay/Panel/Margin/Content/Message");
        _confirmationButton = GetNode<Button>("ConfirmationOverlay/Panel/Margin/Content/Buttons/ConfirmButton");

        OnlyWarStyle.ApplyContentPanel(GetNode<PanelContainer>("Panel"));
        OnlyWarStyle.ApplyInsetPanel(GetNode<PanelContainer>("Panel/Margin/Content/Slots"));
        OnlyWarStyle.ApplyContentPanel(GetNode<PanelContainer>("ConfirmationOverlay/Panel"));

        GetNode<Button>("Panel/Margin/Content/Header/RefreshButton").Pressed += () => RefreshRequested?.Invoke(this, EventArgs.Empty);
        GetNode<Button>("Panel/Margin/Content/Header/CloseButton").Pressed += RequestCancel;
        GetNode<Button>("Panel/Margin/Content/Footer/CancelButton").Pressed += RequestCancel;
        _saveButton.Pressed += RequestSave;
        _deleteButton.Pressed += RequestDelete;
        _primaryButton.Pressed += RequestPrimaryAction;
        _confirmationButton.Pressed += ConfirmPendingAction;
        GetNode<Button>("ConfirmationOverlay/Panel/Margin/Content/Buttons/CancelButton").Pressed += HideConfirmation;
        _saveName.TextChanged += _ => ClearValidation();

        SetMode(SaveChooserMode.Load);
        RenderEntries();
    }

    public void SetMode(SaveChooserMode mode)
    {
        _mode = mode;
        if (!IsNodeReady())
        {
            return;
        }

        bool saving = mode == SaveChooserMode.Save;
        _titleLabel.Text = saving ? "SAVE CAMPAIGN" : "LOAD CAMPAIGN";
        _saveControls.Visible = saving;
        _deleteButton.Visible = true;
        _primaryButton.Visible = !saving;
        _primaryButton.Text = "Load Selected";
        ClearSelection();
        RenderEntries();
    }

    public void SetEntries(IEnumerable<SaveSlotViewModel> entries)
    {
        string selectedId = _selected?.SlotId;
        _entries.Clear();
        if (entries != null)
        {
            _entries.AddRange(entries.Where(entry => entry != null));
        }

        if (IsNodeReady())
        {
            RenderEntries();
            if (!string.IsNullOrWhiteSpace(selectedId))
            {
                SelectSlot(_entries.FirstOrDefault(entry => entry.SlotId == selectedId));
            }
        }
    }

    /// <summary>Convenience alias for integrations that reload the catalog in response to RefreshRequested.</summary>
    public void RefreshEntries(IEnumerable<SaveSlotViewModel> entries) => SetEntries(entries);

    public void ShowChooser(SaveChooserMode mode, IEnumerable<SaveSlotViewModel> entries)
    {
        SetEntries(entries);
        SetMode(mode);
        Visible = true;
        MoveToFront();
    }

    public void SetOperationError(string message)
    {
        _validationLabel.Text = message ?? string.Empty;
        _validationLabel.Visible = !string.IsNullOrWhiteSpace(message);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!Visible || @event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo)
        {
            return;
        }

        if ((keyEvent.Keycode == Key.X || keyEvent.PhysicalKeycode == Key.X)
            && GetViewport().GuiGetFocusOwner() != _saveName)
        {
            if (_confirmationOverlay.Visible)
            {
                HideConfirmation();
            }
            else
            {
                RequestCancel();
            }
            GetViewport().SetInputAsHandled();
        }
    }

    private void RenderEntries()
    {
        if (!IsNodeReady())
        {
            return;
        }

        ClearRows(_manualRows);
        ClearRows(_autosaveRows);
        _rowButtons.Clear();

        foreach (SaveSlotViewModel entry in _entries
                     .OrderByDescending(entry => entry.Kind == SaveSlotKind.Manual)
                     .ThenByDescending(entry => entry.LastWriteTime))
        {
            VBoxContainer target = entry.Kind == SaveSlotKind.Manual ? _manualRows : _autosaveRows;
            Button row = CreateRow(entry);
            target.AddChild(row);
            _rowButtons[entry.SlotId] = row;
        }

        _emptyLabel.Visible = _entries.Count == 0;
        GetNode<Label>("Panel/Margin/Content/Slots/Scroll/Sections/ManualHeader").Visible = _manualRows.GetChildCount() > 0;
        GetNode<Label>("Panel/Margin/Content/Slots/Scroll/Sections/AutosaveHeader").Visible = _autosaveRows.GetChildCount() > 0;
        ClearSelection();
    }

    private Button CreateRow(SaveSlotViewModel entry)
    {
        string state = entry.IsCompatible
            ? "READY"
            : "UNAVAILABLE: " + (string.IsNullOrWhiteSpace(entry.StateDescription) ? "This save cannot be opened." : entry.StateDescription);
        string chapter = string.IsNullOrWhiteSpace(entry.ChapterName) ? "Unknown chapter" : entry.ChapterName;
        string displayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? "Unnamed save" : entry.DisplayName;
        string campaignDate = string.IsNullOrWhiteSpace(entry.CampaignDate)
            ? "Unknown campaign date"
            : entry.CampaignDate;
        string lastWrite = entry.LastWriteTime == default
            ? "Unknown write time"
            : entry.LastWriteTime.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

        Button button = new()
        {
            Text = $"{displayName}  —  {chapter}\nCampaign: {campaignDate}     Written: {lastWrite}\n{state}",
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, 72),
            FocusMode = FocusModeEnum.All,
            TooltipText = entry.IsCompatible ? entry.FilePath : state
        };
        button.AddThemeColorOverride("font_color", entry.IsCompatible ? OnlyWarStyle.BodyText : OnlyWarStyle.MutedText);
        OnlyWarStyle.ApplyListRow(button, false, entry.IsCompatible);
        if (entry.IsCompatible)
        {
            button.Pressed += () => SelectSlot(entry);
        }
        else
        {
            button.Disabled = true;
        }
        return button;
    }

    private void SelectSlot(SaveSlotViewModel entry)
    {
        _selected = entry;
        foreach ((string slotId, Button button) in _rowButtons)
        {
            SaveSlotViewModel rowEntry = _entries.FirstOrDefault(candidate => candidate.SlotId == slotId);
            OnlyWarStyle.ApplyListRow(button, entry != null && slotId == entry.SlotId, rowEntry?.IsCompatible == true);
        }

        if (_mode == SaveChooserMode.Save && entry?.Kind == SaveSlotKind.Manual)
        {
            _saveName.Text = entry.DisplayName;
            _saveName.CaretColumn = _saveName.Text.Length;
        }

        _deleteButton.Disabled = entry?.Kind != SaveSlotKind.Manual;
        _primaryButton.Disabled = _mode != SaveChooserMode.Load || entry?.IsCompatible != true;
        ClearValidation();
    }

    private void ClearSelection()
    {
        SelectSlot(null);
        if (_saveName != null && _mode == SaveChooserMode.Save)
        {
            _saveName.Text = string.Empty;
        }
    }

    private void RequestSave()
    {
        string name = _saveName.Text.Trim();
        if (name.Length == 0)
        {
            SetOperationError("Enter a name for this manual save.");
            _saveName.GrabFocus();
            return;
        }

        SaveSlotViewModel overwriteTarget = _entries.FirstOrDefault(entry =>
            entry.Kind == SaveSlotKind.Manual
            && string.Equals(entry.DisplayName, name, StringComparison.CurrentCultureIgnoreCase));
        if (overwriteTarget == null)
        {
            SaveRequested?.Invoke(this, new SaveSlotRequestedEventArgs(name, null));
            return;
        }

        _selected = overwriteTarget;
        _pendingConfirmation = PendingConfirmation.Overwrite;
        _confirmationTitle.Text = "OVERWRITE MANUAL SAVE?";
        _confirmationMessage.Text = $"Replace ‘{overwriteTarget.DisplayName}’? The previous contents of this slot cannot be recovered.";
        _confirmationButton.Text = "Overwrite";
        _confirmationOverlay.Visible = true;
        _confirmationButton.GrabFocus();
    }

    private void RequestDelete()
    {
        if (_selected?.Kind != SaveSlotKind.Manual)
        {
            return;
        }

        _pendingConfirmation = PendingConfirmation.Delete;
        _confirmationTitle.Text = "DELETE MANUAL SAVE?";
        _confirmationMessage.Text = $"Delete ‘{_selected.DisplayName}’? This cannot be undone.";
        _confirmationButton.Text = "Delete";
        _confirmationOverlay.Visible = true;
        _confirmationButton.GrabFocus();
    }

    private void RequestPrimaryAction()
    {
        if (_mode == SaveChooserMode.Load && _selected?.IsCompatible == true)
        {
            LoadRequested?.Invoke(this, new SaveSlotSelectionEventArgs(_selected));
        }
    }

    private void ConfirmPendingAction()
    {
        PendingConfirmation action = _pendingConfirmation;
        SaveSlotViewModel selected = _selected;
        HideConfirmation();
        if (action == PendingConfirmation.Overwrite && selected != null)
        {
            SaveRequested?.Invoke(this, new SaveSlotRequestedEventArgs(_saveName.Text.Trim(), selected));
        }
        else if (action == PendingConfirmation.Delete && selected?.Kind == SaveSlotKind.Manual)
        {
            DeleteRequested?.Invoke(this, new SaveSlotSelectionEventArgs(selected));
        }
    }

    private void HideConfirmation()
    {
        _pendingConfirmation = PendingConfirmation.None;
        _confirmationOverlay.Visible = false;
    }

    private void RequestCancel()
    {
        if (_confirmationOverlay.Visible)
        {
            HideConfirmation();
            return;
        }
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ClearValidation()
    {
        _validationLabel.Text = string.Empty;
        _validationLabel.Visible = false;
    }

    private static void ClearRows(VBoxContainer container)
    {
        foreach (Node child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }
}
