using Godot;
using OnlyWar.Helpers.UI;
using OnlyWar.Models.Command;
using OnlyWar.Models.Events;
using System;
using System.Collections.Generic;

public partial class CommandScreenView : MainScreenView
{
    private Button _briefTab;
    private Button _chronicleTab;
    private Button _lastReportButton;
    private VBoxContainer _railStack;
    private VBoxContainer _cardStack;
    private Label _bodyTitle;
    private Label _bodySubtitle;
    private ScrollContainer _bodyScroll;
    private Label _emptyLabel;
    private Button _loadOlderButton;

    public event EventHandler<CommandLens> LensSelected;
    public event EventHandler<CommandBriefCategory?> BriefFilterSelected;
    public event EventHandler<ChronicleFilter> ChronicleFilterSelected;
    public event EventHandler<string> BriefItemActionRequested;
    public event EventHandler<string> FocusedStableKeyChanged;
    public event EventHandler<CampaignNavigationTarget> NavigationRequested;
    public event EventHandler LastTurnReportRequested;
    public event EventHandler LoadOlderRequested;

    public override void _Ready()
    {
        Theme = GD.Load<Theme>("res://Scenes/OnlyWarTheme.tres");
        BuildUi();
    }

    public void SetLens(CommandLens lens)
    {
        if (_briefTab == null) return;
        _briefTab.ButtonPressed = lens == CommandLens.Brief;
        _chronicleTab.ButtonPressed = lens == CommandLens.Chronicle;
        OnlyWarStyle.ApplyAccentButtonRow(_briefTab, lens == CommandLens.Brief, OnlyWarStyle.Gold);
        OnlyWarStyle.ApplyAccentButtonRow(_chronicleTab, lens == CommandLens.Chronicle, OnlyWarStyle.Gold);
    }

    public void SetLastTurnReportState(bool hasReport)
    {
        if (_lastReportButton == null) return;
        _lastReportButton.Disabled = !hasReport;
        _lastReportButton.TooltipText = hasReport
            ? "Open the latest resolved turn report."
            : "No resolved turn report exists for this campaign yet.";
    }

    public void SetBriefFilters(
        IReadOnlyList<(CommandBriefCategory? Category, string Label, int Count)> filters,
        CommandBriefCategory? active)
    {
        ClearContainer(_railStack);
        AddRailHeading("COMMAND MATTERS");
        foreach ((CommandBriefCategory? category, string label, int count) in filters ?? [])
        {
            Button button = CreateRailButton(
                $"{label}  {count}",
                category == active,
                () => BriefFilterSelected?.Invoke(this, category));
            button.TooltipText = category.HasValue
                ? $"Show {label.ToLowerInvariant()} matters."
                : "Show every matter requiring command attention.";
            _railStack.AddChild(button);
        }
    }

    public void SetChronicleFilters(
        IReadOnlyList<(ChronicleFilter Filter, string Label, int Count)> filters,
        ChronicleFilter active)
    {
        ClearContainer(_railStack);
        AddRailHeading("CHRONICLE FILTERS");
        foreach ((ChronicleFilter filter, string label, int count) in filters ?? [])
        {
            Button button = CreateRailButton(
                $"{label}  {count}",
                filter == active,
                () => ChronicleFilterSelected?.Invoke(this, filter));
            button.TooltipText = $"Show {label.ToLowerInvariant()} Chronicle entries.";
            _railStack.AddChild(button);
        }
    }

    public void SetBrief(
        CommandBriefModel model,
        CommandBriefCategory? activeCategory,
        bool isFiltered)
    {
        _bodyTitle.Text = activeCategory.HasValue
            ? GetCategoryTitle(activeCategory.Value)
            : "Command Brief";
        _bodySubtitle.Text = isFiltered
            ? "Live matters drawn from the current campaign state."
            : "What requires command now, and what is already underway.";
        _loadOlderButton.Visible = false;
        ClearContainer(_cardStack);
        IReadOnlyList<CommandBriefItem> items = model?.ForCategory(activeCategory)
            ?? Array.Empty<CommandBriefItem>();
        if (items.Count == 0)
        {
            AddEmpty(
                model?.Items.Count > 0
                    ? "No matters in this category."
                    : "No matters require command. Strategic Situation remains available as campaign facts develop.");
            return;
        }

        foreach (CommandBriefItem item in items)
        {
            _cardStack.AddChild(CreateBriefCard(item));
        }
    }

    public void SetChronicle(
        IReadOnlyList<ChronicleEntryViewModel> entries,
        ChronicleFilter activeFilter,
        bool hasOlderPage,
        bool hasAnyEntries)
    {
        _bodyTitle.Text = "Chapter Chronicle";
        _bodySubtitle.Text = hasAnyEntries
            ? "Frozen accounts of the events that mattered to the Chapter."
            : "A curated history begins with defining events as the campaign unfolds.";
        ClearContainer(_cardStack);
        foreach (ChronicleEntryViewModel entry in entries ?? [])
        {
            _cardStack.AddChild(CreateChronicleCard(entry));
        }

        if ((entries?.Count ?? 0) == 0)
        {
            AddEmpty(hasAnyEntries
                ? $"No {GetFilterLabel(activeFilter).ToLowerInvariant()} entries are recorded."
                : "Defining events will appear here as the campaign unfolds.");
        }

        _loadOlderButton.Visible = hasOlderPage;
        _loadOlderButton.Disabled = false;
    }

    public int GetScrollOffset() => _bodyScroll?.ScrollVertical ?? 0;

    public void SetScrollOffset(int offset)
    {
        if (_bodyScroll == null) return;
        _bodyScroll.ScrollVertical = Math.Max(0, offset);
    }

    public void FocusStableKey(string stableKey)
    {
        // Dynamic card controls are intentionally not indexed by position. The next refresh can
        // safely omit a resolved key; Godot's normal focus order then lands on the first available
        // action instead of a stale control.
        if (string.IsNullOrWhiteSpace(stableKey)) return;
        string controlName = stableKey.Replace('/', '_');
        foreach (Node child in _cardStack.GetChildren())
        {
            if (child is Control control && control.Name == controlName)
            {
                control.GrabFocus();
                return;
            }
        }
    }

    private void BuildUi()
    {
        PanelContainer shell = new()
        {
            AnchorRight = 1,
            AnchorBottom = 1,
            OffsetLeft = 12,
            OffsetTop = 12,
            OffsetRight = -12,
            OffsetBottom = -12,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        OnlyWarStyle.ApplyContentPanel(shell);
        AddChild(shell);

        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        shell.AddChild(margin);

        VBoxContainer root = new();
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);
        root.AddChild(BuildHeader());

        HBoxContainer body = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        body.AddThemeConstantOverride("separation", 12);
        root.AddChild(body);

        PanelContainer rail = new()
        {
            CustomMinimumSize = new Vector2(248, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        OnlyWarStyle.ApplyInsetPanel(rail);
        body.AddChild(rail);
        MarginContainer railMargin = new();
        railMargin.AddThemeConstantOverride("margin_left", 10);
        railMargin.AddThemeConstantOverride("margin_top", 10);
        railMargin.AddThemeConstantOverride("margin_right", 10);
        railMargin.AddThemeConstantOverride("margin_bottom", 10);
        rail.AddChild(railMargin);
        _railStack = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _railStack.AddThemeConstantOverride("separation", 5);
        railMargin.AddChild(_railStack);

        PanelContainer mainPanel = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        OnlyWarStyle.ApplyInsetPanel(mainPanel);
        body.AddChild(mainPanel);
        MarginContainer mainMargin = new();
        mainMargin.AddThemeConstantOverride("margin_left", 12);
        mainMargin.AddThemeConstantOverride("margin_top", 10);
        mainMargin.AddThemeConstantOverride("margin_right", 12);
        mainMargin.AddThemeConstantOverride("margin_bottom", 10);
        mainPanel.AddChild(mainMargin);
        VBoxContainer mainStack = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
        mainStack.AddThemeConstantOverride("separation", 7);
        mainMargin.AddChild(mainStack);
        _bodyTitle = new Label { Text = "Command Brief" };
        _bodyTitle.AddThemeFontSizeOverride("font_size", 22);
        _bodyTitle.AddThemeFontOverride("font", GetThemeFont("display"));
        mainStack.AddChild(_bodyTitle);
        _bodySubtitle = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _bodySubtitle.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        mainStack.AddChild(_bodySubtitle);
        _bodyScroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        mainStack.AddChild(_bodyScroll);
        _cardStack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _cardStack.AddThemeConstantOverride("separation", 8);
        _bodyScroll.AddChild(_cardStack);
        _loadOlderButton = new Button
        {
            Text = "LOAD OLDER",
            Visible = false,
            CustomMinimumSize = new Vector2(0, 36),
            FocusMode = Control.FocusModeEnum.All,
            TooltipText = "Load the next bounded Chronicle page."
        };
        IconAtlas.Apply(_loadOlderButton, "archive", 124);
        _loadOlderButton.Pressed += () => LoadOlderRequested?.Invoke(this, EventArgs.Empty);
        mainStack.AddChild(_loadOlderButton);
    }

    private Control BuildHeader()
    {
        PanelContainer header = new();
        OnlyWarStyle.ApplyInsetPanel(header);
        HBoxContainer row = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        row.AddThemeConstantOverride("separation", 8);
        header.AddChild(row);
        Label title = new() { Text = "COMMAND", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 20);
        title.AddThemeFontOverride("font", GetThemeFont("display"));
        row.AddChild(title);
        _briefTab = CreateHeaderButton("COMMAND BRIEF", () => LensSelected?.Invoke(this, CommandLens.Brief));
        _chronicleTab = CreateHeaderButton("CHAPTER CHRONICLE", () => LensSelected?.Invoke(this, CommandLens.Chronicle));
        row.AddChild(_briefTab);
        row.AddChild(_chronicleTab);
        _lastReportButton = new Button
        {
            Text = "LAST TURN REPORT",
            CustomMinimumSize = new Vector2(170, 36),
            FocusMode = Control.FocusModeEnum.All
        };
        IconAtlas.Apply(_lastReportButton, "archive", 170);
        _lastReportButton.Pressed += () => LastTurnReportRequested?.Invoke(this, EventArgs.Empty);
        row.AddChild(_lastReportButton);
        return header;
    }

    private static Button CreateHeaderButton(string text, Action pressed)
    {
        Button button = new()
        {
            Text = text,
            ToggleMode = true,
            CustomMinimumSize = new Vector2(150, 36),
            FocusMode = Control.FocusModeEnum.All,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand
        };
        button.Pressed += pressed;
        return button;
    }

    private Control CreateBriefCard(CommandBriefItem item)
    {
        PanelContainer panel = new() { Name = item.StableKey.Replace('/', '_') };
        OnlyWarStyle.ApplyEventPanel(panel, item.Priority switch
        {
            CommandBriefPriority.Critical => OnlyWarEventTone.Critical,
            CommandBriefPriority.Actionable => OnlyWarEventTone.Warning,
            _ => OnlyWarEventTone.Normal
        });
        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 9);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 9);
        panel.AddChild(margin);
        VBoxContainer stack = new();
        stack.AddThemeConstantOverride("separation", 5);
        margin.AddChild(stack);
        HBoxContainer heading = new();
        heading.AddThemeConstantOverride("separation", 10);
        stack.AddChild(heading);
        Label priority = new() { Text = item.Priority.ToString().ToUpperInvariant() };
        priority.AddThemeColorOverride("font_color", item.Priority == CommandBriefPriority.Critical
            ? OnlyWarStyle.Critical
            : item.Priority == CommandBriefPriority.Actionable ? OnlyWarStyle.Gold : OnlyWarStyle.MutedText);
        heading.AddChild(priority);
        Label title = new() { Text = item.Title, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 16);
        title.AddThemeColorOverride("font_color", OnlyWarStyle.BodyText);
        heading.AddChild(title);
        if (!string.IsNullOrWhiteSpace(item.DeadlineOrStatus))
        {
            Label status = new() { Text = item.DeadlineOrStatus };
            status.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
            heading.AddChild(status);
        }
        RichTextLabel summary = new()
        {
            Text = item.Summary,
            BbcodeEnabled = true,
            FitContent = true,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        summary.AddThemeColorOverride("default_color", OnlyWarStyle.BodyText);
        stack.AddChild(summary);
        if (item.RelatedLinks?.Count > 0)
        {
            HFlowContainer relatedLinks = new();
            relatedLinks.AddThemeConstantOverride("separation", 5);
            foreach (CommandBriefRelatedLink related in item.RelatedLinks)
            {
                if (related.Target?.IsAvailable != true)
                {
                    Label unavailable = new()
                    {
                        Text = $"{related.Label} (unavailable)",
                        TooltipText = related.Target?.Fallback ?? "This link is unavailable."
                    };
                    unavailable.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
                    relatedLinks.AddChild(unavailable);
                    continue;
                }

                Button relatedButton = new()
                {
                    Text = related.Label,
                    CustomMinimumSize = new Vector2(0, 30),
                    FocusMode = Control.FocusModeEnum.All,
                    TooltipText = $"Open {related.Label}."
                };
                IconAtlas.Apply(relatedButton, "map_pin", 0);
                relatedButton.Pressed += () =>
                {
                    FocusedStableKeyChanged?.Invoke(this, item.StableKey);
                    NavigationRequested?.Invoke(this, related.Target);
                };
                relatedLinks.AddChild(relatedButton);
            }
            stack.AddChild(relatedLinks);
        }
        Button action = new()
        {
            Text = item.ActionLabel,
            CustomMinimumSize = new Vector2(160, 34),
            SizeFlagsHorizontal = SizeFlags.ShrinkEnd,
            FocusMode = Control.FocusModeEnum.All,
            Disabled = item.PrimaryTarget == null || !item.PrimaryTarget.IsAvailable,
            TooltipText = item.PrimaryTarget?.IsAvailable == true
                ? $"{item.ActionLabel}: {item.PrimaryTarget.DisplayNameSnapshot ?? item.Title}"
                : item.PrimaryTarget?.Fallback ?? "This action is unavailable."
        };
        IconAtlas.Apply(action, item.IconKey, 160);
        action.Pressed += () => BriefItemActionRequested?.Invoke(this, item.StableKey);
        heading = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        heading.AddChild(action);
        stack.AddChild(heading);
        return panel;
    }

    private Control CreateChronicleCard(ChronicleEntryViewModel entry)
    {
        PanelContainer panel = new() { Name = $"chronicle_{entry.EntryId}" };
        OnlyWarStyle.ApplyListRow(panel, false);
        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        panel.AddChild(margin);
        VBoxContainer stack = new();
        stack.AddThemeConstantOverride("separation", 6);
        margin.AddChild(stack);
        HBoxContainer heading = new();
        heading.AddThemeConstantOverride("separation", 9);
        stack.AddChild(heading);
        Label date = new() { Text = entry.DateLabel };
        date.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        heading.AddChild(date);
        Label title = new() { Text = entry.Title, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 17);
        title.AddThemeColorOverride("font_color", OnlyWarStyle.Gold);
        heading.AddChild(title);
        Label importance = new() { Text = entry.Importance.ToString().ToUpperInvariant() };
        importance.AddThemeColorOverride("font_color", entry.Importance == CampaignEventImportance.Defining
            ? OnlyWarStyle.Gold : OnlyWarStyle.MutedText);
        heading.AddChild(importance);
        RichTextLabel body = new()
        {
            Text = entry.Body,
            BbcodeEnabled = true,
            FitContent = true,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        body.AddThemeColorOverride("default_color", OnlyWarStyle.BodyText);
        stack.AddChild(body);
        if (!string.IsNullOrWhiteSpace(entry.RelatedBattleLabel))
        {
            Label related = new() { Text = $"Battle context: {entry.RelatedBattleLabel}" };
            related.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
            stack.AddChild(related);
        }
        if (entry.Links?.Count > 0)
        {
            HFlowContainer links = new();
            links.AddThemeConstantOverride("separation", 5);
            foreach (ChronicleEntityLink link in entry.Links)
            {
                if (!link.IsAvailable)
                {
                    Label unavailable = new() { Text = $"{link.Label} (unavailable)" };
                    unavailable.TooltipText = link.Target.Fallback;
                    unavailable.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
                    links.AddChild(unavailable);
                    continue;
                }
                Button linkButton = new()
                {
                    Text = link.Label,
                    CustomMinimumSize = new Vector2(0, 30),
                    FocusMode = Control.FocusModeEnum.All,
                    TooltipText = $"Open {link.Label}."
                };
                IconAtlas.Apply(linkButton, "map_pin", 0);
                linkButton.Pressed += () =>
                {
                    FocusedStableKeyChanged?.Invoke(this, $"chronicle_{entry.EntryId}");
                    NavigationRequested?.Invoke(this, link.Target);
                };
                links.AddChild(linkButton);
            }
            stack.AddChild(links);
        }
        return panel;
    }

    private void AddRailHeading(string text)
    {
        Label heading = new() { Text = text };
        heading.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        heading.AddThemeFontSizeOverride("font_size", 12);
        _railStack.AddChild(heading);
    }

    private Button CreateRailButton(string text, bool selected, Action pressed)
    {
        Button button = new()
        {
            Text = text,
            ToggleMode = true,
            ButtonPressed = selected,
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, 34),
            FocusMode = Control.FocusModeEnum.All,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand
        };
        OnlyWarStyle.ApplyListRow(button, selected);
        button.Pressed += pressed;
        return button;
    }

    private void AddEmpty(string text)
    {
        _emptyLabel = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 64)
        };
        _emptyLabel.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        _cardStack.AddChild(_emptyLabel);
    }

    private static string GetCategoryTitle(CommandBriefCategory category) => category switch
    {
        CommandBriefCategory.RequiresOrders => "Requires Orders",
        CommandBriefCategory.PetitionsAndOpportunities => "Petitions & Opportunities",
        CommandBriefCategory.OperationsUnderway => "Operations Underway",
        CommandBriefCategory.RecoveryAndReinforcement => "Recovery & Reinforcement",
        CommandBriefCategory.StrategicSituation => "Strategic Situation",
        CommandBriefCategory.Mandates => "Mandates",
        _ => "Command Brief"
    };

    private static string GetFilterLabel(ChronicleFilter filter) => filter switch
    {
        ChronicleFilter.Defining => "Defining",
        ChronicleFilter.Battles => "Battles",
        ChronicleFilter.Brothers => "Brothers",
        ChronicleFilter.Worlds => "Worlds",
        ChronicleFilter.Chapter => "Chapter",
        _ => "All"
    };

    private static void ClearContainer(Container container)
    {
        if (container == null) return;
        foreach (Node child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }
}
