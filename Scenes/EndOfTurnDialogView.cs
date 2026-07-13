using Godot;
using OnlyWar.Helpers.UI;
using System;
using System.Collections.Generic;

public partial class EndOfTurnDialogView : DialogView
{
    private Label _titleLabel;
    private Label _summaryLabel;
    private VBoxContainer _entryList;
    private Label _emptyHintLabel;

    public event EventHandler<int> EntrySelected;

    public override void _Ready()
    {
        base._Ready();
        _titleLabel = GetNode<Label>("ReportPanel/ReportMargin/Layout/HeaderPanel/HeaderMargin/HeaderStack/TitleLabel");
        _summaryLabel = GetNode<Label>("ReportPanel/ReportMargin/Layout/HeaderPanel/HeaderMargin/HeaderStack/SummaryLabel");
        _entryList = GetNode<VBoxContainer>("ReportPanel/ReportMargin/Layout/ScrollContainer/EntryList");
        _emptyHintLabel = GetNode<Label>("ReportPanel/ReportMargin/Layout/EmptyHintLabel");
        OnlyWarStyle.ApplyContentPanel(GetNode<Panel>("ReportPanel"));
        OnlyWarStyle.ApplyInsetPanel(GetNode<Panel>("ReportPanel/ReportMargin/Layout/HeaderPanel"));
    }

    public void SetReport(IReadOnlyList<EndOfTurnReportEntry> entries)
    {
        ClearEntries();
        int reportCount = entries?.Count ?? 0;
        _titleLabel.Text = "TURN REPORT";
        _summaryLabel.Text = $"{reportCount} report{(reportCount == 1 ? "" : "s")} received by chapter command.";
        _emptyHintLabel.Visible = reportCount == 0;

        if (entries == null)
        {
            return;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            AddEntry(entries[i], i);
        }
    }

    private void AddEntry(EndOfTurnReportEntry entry, int index)
    {
        PanelContainer panel = new();
        if (entry.IsEnemyActivity)
        {
            OnlyWarStyle.ApplyTintedListRow(panel, false, OnlyWarStyle.OpposingAccent);
        }
        else
        {
            OnlyWarStyle.ApplyListRow(panel, false);
        }

        MarginContainer margin = new();
        panel.AddChild(margin);

        VBoxContainer stack = new();
        stack.AddThemeConstantOverride("separation", 5);
        margin.AddChild(stack);

        HBoxContainer titleRow = new();
        titleRow.AddThemeConstantOverride("separation", 10);
        stack.AddChild(titleRow);

        Label title = new()
        {
            Text = entry.Title.ToUpperInvariant(),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        title.AddThemeColorOverride("font_color", entry.CanOpenDebrief ? OnlyWarStyle.Gold : OnlyWarStyle.MutedText);
        titleRow.AddChild(title);

        if (entry.CanOpenDebrief)
        {
            Button openButton = new()
            {
                Text = "DEBRIEF",
                CustomMinimumSize = new Vector2(118, 32),
                TooltipText = "Open the full mission debrief"
            };
            int selectedIndex = index;
            openButton.Pressed += () => EntrySelected?.Invoke(this, selectedIndex);
            titleRow.AddChild(openButton);
        }

        Label subtitle = new()
        {
            Text = entry.Subtitle,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        subtitle.AddThemeColorOverride("font_color", OnlyWarStyle.BodyText);
        stack.AddChild(subtitle);

        Label summary = new()
        {
            Text = entry.Summary,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        summary.AddThemeFontSizeOverride("font_size", 13);
        summary.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        stack.AddChild(summary);

        _entryList.AddChild(panel);
    }

    private void ClearEntries()
    {
        foreach (Node child in _entryList.GetChildren())
        {
            _entryList.RemoveChild(child);
            child.QueueFree();
        }
    }
}
