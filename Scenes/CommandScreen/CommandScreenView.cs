using Godot;
using OnlyWar.Application;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class CommandScreenView : MainScreenView
{
    private const int RecentLegacyRows = 8;

    private Button _briefTab;
    private Button _chronicleTab;
    private Button _mandatesTab;
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
    public event EventHandler<MandateOptionView> SwearMandateRequested;
    public event EventHandler<ActiveMandateView> AbandonMandateRequested;
    public event EventHandler<MandateBoardView> RenounceChapterWorldRequested;

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
        _mandatesTab.ButtonPressed = lens == CommandLens.Mandates;
        OnlyWarStyle.ApplyAccentButtonRow(_briefTab, lens == CommandLens.Brief, OnlyWarStyle.Gold);
        OnlyWarStyle.ApplyAccentButtonRow(_chronicleTab, lens == CommandLens.Chronicle, OnlyWarStyle.Gold);
        OnlyWarStyle.ApplyAccentButtonRow(_mandatesTab, lens == CommandLens.Mandates, OnlyWarStyle.Gold);
    }

    public void SetMandatesAvailable(bool available)
    {
        if (_mandatesTab != null) _mandatesTab.Visible = available;
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
        bool hasAnyEntries,
        MandateBoardView legacy = null)
    {
        _bodyTitle.Text = "Chapter Chronicle";
        _bodySubtitle.Text = hasAnyEntries
            ? "Frozen accounts of the events that mattered to the Chapter."
            : "A curated history begins with defining events as the campaign unfolds.";
        ClearContainer(_cardStack);
        if (legacy?.HasCampaign == true)
        {
            _cardStack.AddChild(CreateLegacyPanel(legacy));
        }
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

    // Scrolls a Chronicle card into view once the new page has its layout. A card panel takes no
    // focus, so FocusStableKey cannot do this.
    public void ScrollToChronicleEntry(long entryId)
    {
        if (_bodyScroll == null || _cardStack == null) return;
        Control card = _cardStack.GetNodeOrNull<Control>($"chronicle_{entryId}");
        if (card == null) return;
        Callable.From(() =>
        {
            if (IsInstanceValid(card) && IsInstanceValid(_bodyScroll))
            {
                _bodyScroll.EnsureControlVisible(card);
            }
        }).CallDeferred();
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
        _mandatesTab = CreateHeaderButton("MANDATES", () => LensSelected?.Invoke(this, CommandLens.Mandates));
        _mandatesTab.TooltipText = "The Chapter's sworn mandate, the mandates it can swear, and its past vows.";
        row.AddChild(_briefTab);
        row.AddChild(_chronicleTab);
        row.AddChild(_mandatesTab);
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

                Button relatedButton = CreateLinkButton(related.Label, $"Open {related.Label}.");
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
        importance.AddThemeColorOverride("font_color", entry.Importance == ChronicleImportance.Defining
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
                Button linkButton = CreateLinkButton(link.Label, $"Open {link.Label}.");
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

    // ---- Mandates lens (TDD §6.15 *Application*) --------------------------------------------------

    public void SetMandateRail(MandateBoardView board)
    {
        ClearContainer(_railStack);
        AddRailHeading("CHAPTER LEGACY");
        Label total = new() { Text = FormatSigned(board?.ChapterLegacy ?? 0) };
        total.AddThemeFontSizeOverride("font_size", 28);
        total.AddThemeFontOverride("font", GetThemeFont("display"));
        total.AddThemeColorOverride("font_color", LegacyColor(board?.ChapterLegacy ?? 0));
        _railStack.AddChild(total);
        _railStack.AddChild(MutedLabel(
            "Kept vows and duty raise the Chapter's legacy; broken vows, lost worlds and fallen "
            + "brothers lower it. The Chronicle shows its history."));

        AddRailHeading("CHAPTER WORLD");
        _railStack.AddChild(MutedLabel(board?.ChapterWorldName ?? "The Chapter has no Chapter World."));
        if (board?.CanRenounceChapterWorld == true)
        {
            Button renounce = new()
            {
                Text = "RENOUNCE",
                CustomMinimumSize = new Vector2(0, 34),
                FocusMode = Control.FocusModeEnum.All,
                TooltipText = $"Give up {board.ChapterWorldName} as the Chapter World."
            };
            OnlyWarStyle.ApplyActionButton(renounce, OnlyWarStyle.Critical);
            renounce.Pressed += () => RenounceChapterWorldRequested?.Invoke(this, board);
            _railStack.AddChild(renounce);
        }
    }

    public void SetMandates(MandateBoardView board)
    {
        _bodyTitle.Text = "Mandates";
        _loadOlderButton.Visible = false;
        ClearContainer(_cardStack);
        if (board == null || !board.HasCampaign)
        {
            _bodySubtitle.Text = string.Empty;
            AddEmpty("No campaign is loaded.");
            return;
        }

        if (board.Active != null)
        {
            _bodySubtitle.Text = "The Chapter holds one mandate at a time. Keep it, or set it aside to choose another.";
            _cardStack.AddChild(CreateActiveMandateCard(board.Active));
        }
        else
        {
            _bodySubtitle.Text = board.Options.Count > 0
                ? "The Chapter has sworn no mandate. Choose the goal it will pursue next."
                : "The Chapter has sworn no mandate, and none is eligible now.";
            foreach (IGrouping<string, MandateOptionView> family in board.Options.GroupBy(option => option.Family))
            {
                _cardStack.AddChild(SectionHeading(family.Key.ToUpperInvariant()));
                foreach (MandateOptionView option in family)
                {
                    _cardStack.AddChild(CreateMandateOptionCard(option));
                }
            }
            if (board.Options.Count == 0)
            {
                AddEmpty("No mandate is eligible now. The list changes as the sector changes.");
            }
        }

        if (board.History.Count > 0)
        {
            _cardStack.AddChild(SectionHeading("PAST VOWS"));
            foreach (MandateHistoryView vow in board.History)
            {
                _cardStack.AddChild(CreateHistoryRow(vow));
            }
        }

        if (board.RecentEntries.Count > 0)
        {
            _cardStack.AddChild(SectionHeading("RECENT LEGACY CHANGES"));
            foreach (LegacyEntryView entry in board.RecentEntries.Take(RecentLegacyRows))
            {
                _cardStack.AddChild(CreateLegacyEntryRow(entry));
            }
        }
    }

    private Control CreateActiveMandateCard(ActiveMandateView active)
    {
        (PanelContainer panel, VBoxContainer stack) = CreateCard(
            $"mandate_{active.Id}",
            active.WeeksLeft is <= 4 ? OnlyWarEventTone.Critical : OnlyWarEventTone.Warning);
        stack.AddChild(CardHeading(active.Family.ToUpperInvariant(), active.Title, active.DeadlineLabel));
        stack.AddChild(BodyText(active.Progress));
        stack.AddChild(MutedLabel(
            $"Kept: +{active.LegacyValue} legacy. Broken or abandoned: {active.FailureCost} legacy."));
        AddLinks(stack, active.Links, $"mandate_{active.Id}");
        Button abandon = new()
        {
            Text = active.IsPromisedWorld ? "GIVE UP" : "ABANDON",
            CustomMinimumSize = new Vector2(160, 34),
            FocusMode = Control.FocusModeEnum.All,
            TooltipText = active.AbandonWarning
        };
        OnlyWarStyle.ApplyActionButton(abandon, OnlyWarStyle.Critical);
        abandon.Pressed += () => AbandonMandateRequested?.Invoke(this, active);
        stack.AddChild(ActionRow(abandon));
        return panel;
    }

    private Control CreateMandateOptionCard(MandateOptionView option)
    {
        (PanelContainer panel, VBoxContainer stack) = CreateCard(
            $"option_{option.Key.Replace('/', '_')}", OnlyWarEventTone.Normal);
        stack.AddChild(CardHeading(null, option.Title, option.DeadlineLabel));
        stack.AddChild(BodyText(option.Goal));
        stack.AddChild(MutedLabel(
            $"Kept: +{option.LegacyValue} legacy. Broken or abandoned: {option.FailureCost} legacy."));
        AddLinks(stack, option.Links, $"option_{option.Key.Replace('/', '_')}");
        Button swear = new()
        {
            Text = "SWEAR",
            CustomMinimumSize = new Vector2(160, 34),
            FocusMode = Control.FocusModeEnum.All,
            TooltipText = $"Swear to {option.Title.ToLowerInvariant()}."
        };
        IconAtlas.Apply(swear, "chapter", 160);
        swear.Pressed += () => SwearMandateRequested?.Invoke(this, option);
        stack.AddChild(ActionRow(swear));
        return panel;
    }

    private Control CreateHistoryRow(MandateHistoryView vow)
    {
        PanelContainer panel = new() { Name = $"vow_{vow.Id}" };
        OnlyWarStyle.ApplyListRow(panel, false);
        MarginContainer margin = Margins(10, 6);
        panel.AddChild(margin);
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 10);
        margin.AddChild(row);
        Label result = new() { Text = vow.Result.ToUpperInvariant(), CustomMinimumSize = new Vector2(96, 0) };
        result.AddThemeColorOverride("font_color", vow.Result == "Kept" ? OnlyWarStyle.MedicalStable : OnlyWarStyle.Critical);
        row.AddChild(result);
        Label title = new()
        {
            Text = vow.Title,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        title.AddThemeColorOverride("font_color", OnlyWarStyle.BodyText);
        row.AddChild(title);
        Label dates = MutedLabel($"{vow.SwornLabel} to {vow.ResolvedLabel}");
        dates.AutowrapMode = TextServer.AutowrapMode.Off;
        row.AddChild(dates);
        Label amount = new() { Text = FormatSigned(vow.LegacyAmount), CustomMinimumSize = new Vector2(56, 0), HorizontalAlignment = HorizontalAlignment.Right };
        amount.AddThemeColorOverride("font_color", LegacyColor(vow.LegacyAmount));
        row.AddChild(amount);
        return panel;
    }

    // ---- Legacy header of the Chronicle (§6.3) --------------------------------------------------
    // A header, not an entry: no card frame, the graph beside the per-master table, and a rule and
    // an "ENTRIES" label between it and the Chronicle's own entries (user report, 2026-11-19).

    private Control CreateLegacyPanel(MandateBoardView board)
    {
        VBoxContainer header = new() { Name = "legacy_header", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        header.AddThemeConstantOverride("separation", 6);

        HBoxContainer titleRow = new();
        titleRow.AddThemeConstantOverride("separation", 10);
        Label title = new() { Text = "LEGACY" };
        title.AddThemeFontSizeOverride("font_size", 18);
        title.AddThemeFontOverride("font", GetThemeFont("display"));
        title.AddThemeColorOverride("font_color", OnlyWarStyle.Gold);
        titleRow.AddChild(title);
        Label total = ColoredNumber(board.ChapterLegacy);
        total.AddThemeFontSizeOverride("font_size", 18);
        titleRow.AddChild(total);
        header.AddChild(titleRow);

        if (board.LegacySeries.Count == 0)
        {
            header.AddChild(MutedLabel("No legacy has been gained or lost yet."));
        }
        else
        {
            HBoxContainer body = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            body.AddThemeConstantOverride("separation", 18);
            LegacyGraph graph = new()
            {
                CustomMinimumSize = new Vector2(260, 140),
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            graph.SetSeries(board.LegacySeries);
            body.AddChild(graph);
            if (board.Masters.Count > 0)
            {
                body.AddChild(CreateMasterTable(board.Masters));
            }
            header.AddChild(body);
        }

        header.AddChild(new HSeparator());
        header.AddChild(SectionHeading("ENTRIES"));
        return header;
    }

    private static Control CreateMasterTable(IReadOnlyList<LegacyMasterView> masters)
    {
        GridContainer table = new() { Columns = 5, SizeFlagsVertical = SizeFlags.ShrinkBegin };
        table.AddThemeConstantOverride("h_separation", 14);
        table.AddThemeConstantOverride("v_separation", 3);
        foreach (string column in new[] { "Chapter Master", "In office", "Gained", "Lost", "Total" })
        {
            table.AddChild(SectionHeading(column.ToUpperInvariant()));
        }
        foreach (LegacyMasterView master in masters)
        {
            Label name = new() { Text = master.Name };
            name.AddThemeColorOverride("font_color", OnlyWarStyle.BodyText);
            table.AddChild(name);
            Label tenure = MutedLabel(master.TenureLabel);
            tenure.AutowrapMode = TextServer.AutowrapMode.Off;
            table.AddChild(tenure);
            table.AddChild(ColoredNumber(master.Gained));
            table.AddChild(ColoredNumber(master.Lost));
            table.AddChild(ColoredNumber(master.Total));
        }
        return table;
    }

    private Control CreateLegacyEntryRow(LegacyEntryView entry)
    {
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 10);
        Label amount = ColoredNumber(entry.Amount);
        amount.CustomMinimumSize = new Vector2(48, 0);
        row.AddChild(amount);
        Label text = new()
        {
            Text = entry.Description,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        text.AddThemeColorOverride("font_color", OnlyWarStyle.BodyText);
        row.AddChild(text);
        Label date = MutedLabel(entry.DateLabel);
        date.AutowrapMode = TextServer.AutowrapMode.Off;
        row.AddChild(date);
        return row;
    }

    // ---- Small builders -------------------------------------------------------------------------

    private static (PanelContainer Panel, VBoxContainer Stack) CreateCard(string name, OnlyWarEventTone tone)
    {
        PanelContainer panel = new() { Name = name };
        OnlyWarStyle.ApplyEventPanel(panel, tone);
        MarginContainer margin = Margins(12, 9);
        panel.AddChild(margin);
        VBoxContainer stack = new();
        stack.AddThemeConstantOverride("separation", 5);
        margin.AddChild(stack);
        return (panel, stack);
    }

    private static MarginContainer Margins(int horizontal, int vertical)
    {
        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", horizontal);
        margin.AddThemeConstantOverride("margin_top", vertical);
        margin.AddThemeConstantOverride("margin_right", horizontal);
        margin.AddThemeConstantOverride("margin_bottom", vertical);
        return margin;
    }

    private static HBoxContainer CardHeading(string kicker, string title, string status)
    {
        HBoxContainer heading = new();
        heading.AddThemeConstantOverride("separation", 10);
        if (!string.IsNullOrWhiteSpace(kicker))
        {
            Label kickerLabel = new() { Text = kicker };
            kickerLabel.AddThemeColorOverride("font_color", OnlyWarStyle.Gold);
            heading.AddChild(kickerLabel);
        }
        Label titleLabel = new()
        {
            Text = title,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        titleLabel.AddThemeFontSizeOverride("font_size", 16);
        titleLabel.AddThemeColorOverride("font_color", OnlyWarStyle.BodyText);
        heading.AddChild(titleLabel);
        if (!string.IsNullOrWhiteSpace(status))
        {
            Label statusLabel = new() { Text = status };
            statusLabel.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
            heading.AddChild(statusLabel);
        }
        return heading;
    }

    private static Label BodyText(string text)
    {
        Label label = new()
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        label.AddThemeColorOverride("font_color", OnlyWarStyle.BodyText);
        return label;
    }

    private static Label MutedLabel(string text)
    {
        Label label = new()
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        label.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        return label;
    }

    private static Label SectionHeading(string text)
    {
        Label label = new() { Text = text };
        label.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        label.AddThemeFontSizeOverride("font_size", 12);
        return label;
    }

    private static Label ColoredNumber(int value)
    {
        Label label = new() { Text = FormatSigned(value) };
        label.AddThemeColorOverride("font_color", LegacyColor(value));
        return label;
    }

    private static HBoxContainer ActionRow(Button button)
    {
        HBoxContainer row = new() { Alignment = BoxContainer.AlignmentMode.End };
        row.AddChild(button);
        return row;
    }

    private void AddLinks(VBoxContainer stack, IReadOnlyList<MandateLinkView> links, string focusKey)
    {
        if (links == null || links.Count == 0) return;
        HFlowContainer flow = new();
        flow.AddThemeConstantOverride("separation", 5);
        foreach (MandateLinkView link in links)
        {
            Button button = CreateLinkButton(link.Label, $"{link.Label}.");
            button.Pressed += () =>
            {
                FocusedStableKeyChanged?.Invoke(this, focusKey);
                NavigationRequested?.Invoke(this, link.Target);
            };
            flow.AddChild(button);
        }
        stack.AddChild(flow);
    }

    // A link to a world or other record: text only, centred, with the action-button frame. An icon
    // here pushed the text off centre (user report, 2026-11-19).
    private static Button CreateLinkButton(string label, string tooltip)
    {
        Button button = new()
        {
            Text = label,
            Alignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(120, 30),
            FocusMode = Control.FocusModeEnum.All,
            TooltipText = tooltip
        };
        OnlyWarStyle.ApplyActionButton(button);
        return button;
    }

    private static string FormatSigned(int value) => value > 0 ? $"+{value}" : value.ToString();

    private static Color LegacyColor(int value) => value > 0
        ? OnlyWarStyle.MedicalStable
        : value < 0 ? OnlyWarStyle.Critical : OnlyWarStyle.MutedText;

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
