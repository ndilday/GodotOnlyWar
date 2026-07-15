using Godot;
using OnlyWar.Helpers.Settings;
using OnlyWar.Helpers.Turns;
using OnlyWar.Helpers.UI;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class EndTurnPreflightDialog : DialogController
{
    private Label _summaryLabel;
    private VBoxContainer _attentionList;
    private Button _proceedButton;
    private Button _cancelButton;
    private EndTurnWarningPreferences _preferences = new();
    private bool _isPopulating;

    public event EventHandler EndTurnAnywayPressed;
    public event EventHandler CancelPressed;
    public event EventHandler<EndTurnWarningPreferences> WarningPreferencesChanged;

    public override void _Ready()
    {
        base._Ready();
        _summaryLabel = GetNode<Label>("DialogView/PreflightPanel/ContentMargin/Layout/SummaryLabel");
        _attentionList = GetNode<VBoxContainer>("DialogView/PreflightPanel/ContentMargin/Layout/AttentionScroll/AttentionList");
        _proceedButton = GetNode<Button>("DialogView/PreflightPanel/ContentMargin/Layout/ActionRow/ProceedButton");
        _cancelButton = GetNode<Button>("DialogView/PreflightPanel/ContentMargin/Layout/ActionRow/CancelButton");

        OnlyWarStyle.ApplyContentPanel(GetNode<Panel>("DialogView/PreflightPanel"));
        _proceedButton.Pressed += OnProceedPressed;
        _cancelButton.Pressed += OnCancelPressed;
        CloseButtonPressed += OnDialogClosePressed;
    }

    public override void _ExitTree()
    {
        if (_proceedButton != null)
        {
            _proceedButton.Pressed -= OnProceedPressed;
        }
        if (_cancelButton != null)
        {
            _cancelButton.Pressed -= OnCancelPressed;
        }
        CloseButtonPressed -= OnDialogClosePressed;
    }

    public void SetData(EndTurnPreflightReport report, EndTurnWarningPreferences preferences)
    {
        if (report == null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        _preferences = (preferences ?? new EndTurnWarningPreferences()).Clone();
        _summaryLabel.Text = report.Items.Count == 1
            ? "One command matter can still be resolved before the turn advances."
            : $"{report.Items.Count} command matters can still be resolved before the turn advances.";

        ClearAttentionItems();
        _isPopulating = true;
        try
        {
            foreach (EndTurnWarningCategory category in Enum.GetValues<EndTurnWarningCategory>())
            {
                IReadOnlyList<EndTurnAttentionItem> items = report.ForCategory(category);
                if (items.Count > 0)
                {
                    AddCategory(category, items);
                }
            }
        }
        finally
        {
            _isPopulating = false;
        }
    }

    private void AddCategory(
        EndTurnWarningCategory category,
        IReadOnlyList<EndTurnAttentionItem> items)
    {
        VBoxContainer section = new();
        section.AddThemeConstantOverride("separation", 6);

        HBoxContainer heading = new();
        heading.AddThemeConstantOverride("separation", 12);
        Label title = new()
        {
            Text = $"{EndTurnPreflight.GetCategoryTitle(category).ToUpperInvariant()} ({items.Count})",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center
        };
        title.AddThemeColorOverride("font_color", OnlyWarStyle.Gold);
        heading.AddChild(title);

        CheckButton preferenceToggle = new()
        {
            Text = EndTurnPreflight.GetPreferenceLabel(category),
            ButtonPressed = _preferences.IsEnabled(category),
            TooltipText = "This is a global preference and applies to every campaign."
        };
        preferenceToggle.Toggled += enabled => OnPreferenceToggled(category, enabled);
        heading.AddChild(preferenceToggle);
        section.AddChild(heading);

        foreach (EndTurnAttentionItem item in items)
        {
            section.AddChild(BuildAttentionRow(item));
        }

        _attentionList.AddChild(section);
    }

    private static PanelContainer BuildAttentionRow(EndTurnAttentionItem item)
    {
        PanelContainer row = new();
        OnlyWarStyle.ApplyListRow(row, false);

        MarginContainer margin = new();
        row.AddChild(margin);

        VBoxContainer text = new();
        text.AddThemeConstantOverride("separation", 3);
        margin.AddChild(text);

        Label title = new()
        {
            Text = item.Title,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        title.AddThemeColorOverride("font_color", OnlyWarStyle.BodyText);
        text.AddChild(title);

        Label detail = new()
        {
            Text = item.Detail,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        detail.AddThemeFontSizeOverride("font_size", 13);
        detail.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        text.AddChild(detail);
        return row;
    }

    private void OnPreferenceToggled(EndTurnWarningCategory category, bool enabled)
    {
        if (_isPopulating)
        {
            return;
        }

        _preferences.SetEnabled(category, enabled);
        WarningPreferencesChanged?.Invoke(this, _preferences.Clone());
    }

    private void OnProceedPressed()
    {
        Visible = false;
        EndTurnAnywayPressed?.Invoke(this, EventArgs.Empty);
    }

    private void OnCancelPressed()
    {
        Visible = false;
        CancelPressed?.Invoke(this, EventArgs.Empty);
    }

    private void OnDialogClosePressed(object sender, EventArgs eventArgs)
    {
        OnCancelPressed();
    }

    private void ClearAttentionItems()
    {
        foreach (Node child in _attentionList.GetChildren())
        {
            _attentionList.RemoveChild(child);
            child.QueueFree();
        }
    }
}
