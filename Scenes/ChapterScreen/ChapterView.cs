using Godot;
using OnlyWar.Helpers.UI;
using System;
using System.Collections.Generic;

public partial class ChapterView : MainScreenView
{
    private const int ChapterIconSize = 48;

    // Pointer travel (in viewport pixels) still treated as a click rather than a drag.
    private const float ClickDragTolerance = 6f;

    private HBoxContainer _breadcrumbBar;
    private HBoxContainer _breadcrumbItems;
    private Button _loadoutsButton;
    private Label _leftTitleLabel;
    private Button _filterButton;
    private VBoxContainer _leftMenuVBox;
    private TextureRect _detailIcon;
    private Label _detailTitleLabel;
    private Label _detailSubtitleLabel;
    private GridContainer _metricGrid;
    private HBoxContainer _detailCardLayout;
    private GridContainer _detailCardGrid;
    private Button _detailActionButton;
    private MenuButton _transferButton;

    public event EventHandler<ChapterBrowserItemEvent> BrowserItemSelected;
    public event EventHandler<ChapterBrowserItemEvent> BrowserItemDrillRequested;
    public event EventHandler<ChapterBrowserLevel> BreadcrumbPressed;
    public event EventHandler DetailPrimaryActionPressed;
    public event EventHandler<int> TransferTargetSelected;
    public event EventHandler FilterButtonPressed;
    public event EventHandler ChapterLoadoutsPressed;

    public override void _Ready()
    {
        base._Ready();
        _breadcrumbBar = GetNode<HBoxContainer>("Content/BreadcrumbBar");
        _breadcrumbItems = GetNode<HBoxContainer>("Content/BreadcrumbBar/BreadcrumbItems");
        _loadoutsButton = GetNode<Button>("Content/MainLayout/DetailPanel/Panel/MarginContainer/DetailStack/Hero/LoadoutsButton");
        _leftTitleLabel = GetNode<Label>("Content/MainLayout/LeftMenu/Panel/MarginContainer/MenuStack/Header/TitleLabel");
        _filterButton = GetNode<Button>("Content/MainLayout/LeftMenu/Panel/MarginContainer/MenuStack/Header/FilterButton");
        _leftMenuVBox = GetNode<VBoxContainer>("Content/MainLayout/LeftMenu/Panel/MarginContainer/MenuStack/ScrollContainer/LeftMenuVBox");
        _detailIcon = GetNode<TextureRect>("Content/MainLayout/DetailPanel/Panel/MarginContainer/DetailStack/Hero/DetailIcon");
        _detailTitleLabel = GetNode<Label>("Content/MainLayout/DetailPanel/Panel/MarginContainer/DetailStack/Hero/TitleStack/TitleLabel");
        _detailSubtitleLabel = GetNode<Label>("Content/MainLayout/DetailPanel/Panel/MarginContainer/DetailStack/Hero/TitleStack/SubtitleLabel");
        _metricGrid = GetNode<GridContainer>("Content/MainLayout/DetailPanel/Panel/MarginContainer/DetailStack/Hero/MetricGrid");
        _detailCardLayout = GetNode<HBoxContainer>("Content/MainLayout/DetailPanel/Panel/MarginContainer/DetailStack/DetailScroll/DetailCardLayout");
        _detailCardGrid = GetNode<GridContainer>("Content/MainLayout/DetailPanel/Panel/MarginContainer/DetailStack/DetailScroll/DetailCardLayout/DetailCardGrid");
        _detailActionButton = GetNode<Button>("Content/MainLayout/DetailPanel/Panel/MarginContainer/DetailStack/DetailActionButton");
        _transferButton = GetNode<MenuButton>("Content/MainLayout/DetailPanel/Panel/MarginContainer/DetailStack/Hero/TransferButton");

        ConfigureHeaderLabel(_leftTitleLabel);
        _filterButton.MouseDefaultCursorShape = CursorShape.PointingHand;
        _filterButton.Pressed += () => FilterButtonPressed?.Invoke(this, EventArgs.Empty);
        IconAtlas.Apply(_loadoutsButton, "armamentarium", 150);
        _loadoutsButton.Pressed += () => ChapterLoadoutsPressed?.Invoke(this, EventArgs.Empty);
        _detailIcon.CustomMinimumSize = new Vector2(ChapterIconSize, ChapterIconSize);
        _detailIcon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        _detailActionButton.Pressed += () => DetailPrimaryActionPressed?.Invoke(this, EventArgs.Empty);
        _transferButton.GetPopup().IndexPressed += index => TransferTargetSelected?.Invoke(this, (int)index);
    }

    public void SetBreadcrumbs(IReadOnlyList<ChapterBreadcrumbItem> breadcrumbs)
    {
        ClearContainer(_breadcrumbItems);

        foreach (ChapterBreadcrumbItem breadcrumb in breadcrumbs)
        {
            Button button = new Button
            {
                Text = breadcrumb.Text,
                MouseDefaultCursorShape = CursorShape.PointingHand,
                CustomMinimumSize = new Vector2(0, 36)
            };
            IconAtlas.Apply(button, breadcrumb.IconKey);
            button.Pressed += () => BreadcrumbPressed?.Invoke(this, breadcrumb.Level);
            _breadcrumbItems.AddChild(button);
        }
    }

    public void SetLeftMenu(string title, IReadOnlyList<ChapterBrowserMenuItem> items)
    {
        _leftTitleLabel.Text = title;
        _leftTitleLabel.TooltipText = title;
        ClearContainer(_leftMenuVBox);

        foreach (ChapterBrowserMenuItem item in items)
        {
            _leftMenuVBox.AddChild(CreateMenuRow(item));
        }

        if (items.Count == 0)
        {
            Label emptyLabel = new Label
            {
                Text = "No records at this level.",
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            emptyLabel.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
            _leftMenuVBox.AddChild(emptyLabel);
        }
    }

    // Reflects the active filter on the Filter button so the player can see (and clear) it
    // from the left-menu header. A zero count renders as the plain "Filter" idle label.
    public void SetFilterActive(int conditionCount)
    {
        _filterButton.Text = conditionCount > 0 ? $"Filter ({conditionCount})" : "Filter";
    }

    public void SetDetail(ChapterBrowserDetail detail)
    {
        _detailIcon.Texture = IconAtlas.GetIcon(detail.IconKey);
        _detailTitleLabel.Text = detail.Title;
        _detailSubtitleLabel.Text = detail.Subtitle;

        ClearContainer(_metricGrid);
        foreach (ChapterBrowserMetric metric in detail.Metrics)
        {
            _metricGrid.AddChild(CreateMetricPanel(metric));
        }

        ClearContainer(_detailCardGrid);
        foreach (Node child in _detailCardLayout.GetChildren())
        {
            if (child != _detailCardGrid)
            {
                _detailCardLayout.RemoveChild(child);
                child.QueueFree();
            }
        }

        bool hasFullHeightCard = false;
        foreach (ChapterBrowserDetailCard card in detail.Cards)
        {
            if (card.FullHeight)
            {
                hasFullHeightCard = true;
                _detailCardLayout.AddChild(CreateDetailCard(card));
            }
            else
            {
                _detailCardGrid.AddChild(CreateDetailCard(card));
            }
        }

        // Full-height cards claim the right third of the panel; the grid narrows to two columns.
        _detailCardGrid.Columns = hasFullHeightCard ? 2 : 3;
        _detailCardGrid.SizeFlagsStretchRatio = 2f;

        bool hasAction = !string.IsNullOrWhiteSpace(detail.PrimaryActionText);
        _detailActionButton.Visible = hasAction;
        _detailActionButton.Disabled = !hasAction;
        _transferButton.Visible = false;
        _transferButton.Disabled = true;
        _transferButton.GetPopup().Clear();
        if (hasAction)
        {
            _detailActionButton.Text = detail.PrimaryActionText;
            IconAtlas.Apply(_detailActionButton, detail.PrimaryActionIconKey ?? "archive");
        }
    }

    public void SetTransferOptions(IReadOnlyList<string> options)
    {
        _transferButton.GetPopup().Clear();
        foreach (string option in options)
        {
            _transferButton.GetPopup().AddItem(option);
        }

        bool hasOptions = options.Count > 0;
        _transferButton.Visible = hasOptions;
        _transferButton.Disabled = !hasOptions;
        _transferButton.Text = hasOptions ? "Transfer" : "No Transfers Available";
        if (hasOptions)
        {
            IconAtlas.Apply(_transferButton, "chapter");
        }
    }

    private Control CreateMenuRow(ChapterBrowserMenuItem item)
    {
        PanelContainer row = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0, 58),
            MouseDefaultCursorShape = CursorShape.PointingHand
        };
        OnlyWarStyle.ApplyListRow(row, item.IsSelected);
        // A press only arms the row; the selection fires on release, and only if the pointer
        // stayed put. Otherwise drag-scrolling the menu would select whichever row the drag
        // happened to start on.
        bool pressArmed = false;
        Vector2 pressPosition = Vector2.Zero;
        row.GuiInput += inputEvent =>
        {
            if (inputEvent is not InputEventMouseButton mouseButton ||
                mouseButton.ButtonIndex != MouseButton.Left)
            {
                return;
            }

            if (mouseButton.Pressed)
            {
                if (mouseButton.DoubleClick && item.CanDrill)
                {
                    pressArmed = false;
                    BrowserItemDrillRequested?.Invoke(this, new ChapterBrowserItemEvent(item.Level, item.Id));
                    return;
                }

                pressArmed = true;
                // Viewport space, not row-local: the row itself moves under the cursor while
                // the scroll container is being dragged, which would hide the motion.
                pressPosition = mouseButton.GlobalPosition;
                return;
            }

            if (!pressArmed)
            {
                return;
            }

            pressArmed = false;
            if (mouseButton.GlobalPosition.DistanceTo(pressPosition) <= ClickDragTolerance)
            {
                BrowserItemSelected?.Invoke(this, new ChapterBrowserItemEvent(item.Level, item.Id));
            }
        };

        HBoxContainer rowContent = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Begin,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        rowContent.AddThemeConstantOverride("separation", 8);
        row.AddChild(rowContent);

        TextureRect icon = CreateIconRect(item.IconKey, ChapterIconSize);
        rowContent.AddChild(icon);

        VBoxContainer textStack = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        textStack.AddThemeConstantOverride("separation", 0);
        rowContent.AddChild(textStack);

        Label title = new Label
        {
            Text = item.Title,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            TooltipText = item.Title
        };
        textStack.AddChild(title);

        Label subtitle = new Label
        {
            Text = item.Subtitle,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            TooltipText = item.Subtitle
        };
        subtitle.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        subtitle.AddThemeFontSizeOverride("font_size", 12);
        textStack.AddChild(subtitle);

        if (!string.IsNullOrWhiteSpace(item.Location))
        {
            Label location = new Label
            {
                Text = item.Location,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                TooltipText = item.Location
            };
            location.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
            location.AddThemeFontSizeOverride("font_size", 12);
            textStack.AddChild(location);
        }

        Button drillButton = new Button
        {
            Text = item.CanDrill ? item.DrillText : "i",
            CustomMinimumSize = new Vector2(32, 32),
            MouseDefaultCursorShape = CursorShape.PointingHand,
            TooltipText = item.CanDrill ? "Drill into this item" : "Show details",
            Disabled = !item.CanDrill && item.Level != ChapterBrowserLevel.Soldier
        };
        drillButton.Pressed += () =>
        {
            if (item.CanDrill)
            {
                BrowserItemDrillRequested?.Invoke(this, new ChapterBrowserItemEvent(item.Level, item.Id));
            }
            else
            {
                BrowserItemSelected?.Invoke(this, new ChapterBrowserItemEvent(item.Level, item.Id));
            }
        };
        rowContent.AddChild(drillButton);

        return row;
    }

    private Control CreateMetricPanel(ChapterBrowserMetric metric)
    {
        PanelContainer panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(110, 58)
        };
        OnlyWarStyle.ApplyInsetPanel(panel);

        VBoxContainer stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 0);
        panel.AddChild(stack);

        Label value = new Label { Text = metric.Value };
        value.AddThemeColorOverride("font_color", OnlyWarStyle.Gold);
        value.AddThemeFontSizeOverride("font_size", 18);
        stack.AddChild(value);

        Label label = new Label { Text = metric.Label };
        label.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        label.AddThemeFontSizeOverride("font_size", 12);
        stack.AddChild(label);

        return panel;
    }

    private Control CreateDetailCard(ChapterBrowserDetailCard card)
    {
        PanelContainer panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0, 112),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        if (card.FullHeight)
        {
            panel.SizeFlagsVertical = SizeFlags.ExpandFill;
            panel.SizeFlagsStretchRatio = 1f;
        }
        OnlyWarStyle.ApplyInsetPanel(panel);

        VBoxContainer stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 6);
        panel.AddChild(stack);

        HBoxContainer heading = new HBoxContainer();
        heading.AddThemeConstantOverride("separation", 8);
        stack.AddChild(heading);

        TextureRect icon = CreateIconRect(card.IconKey, ChapterIconSize);
        heading.AddChild(icon);

        VBoxContainer titleStack = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        titleStack.AddThemeConstantOverride("separation", 0);
        heading.AddChild(titleStack);

        Label title = new Label
        {
            Text = card.Title,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        titleStack.AddChild(title);

        Label subtitle = new Label
        {
            Text = card.Subtitle,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        subtitle.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        subtitle.AddThemeFontSizeOverride("font_size", 12);
        titleStack.AddChild(subtitle);

        Label body = new Label
        {
            Text = card.Body,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        body.AddThemeFontSizeOverride("font_size", 13);

        if (card.Scrollable)
        {
            body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            ScrollContainer bodyScroll = new ScrollContainer
            {
                CustomMinimumSize = new Vector2(0, 48),
                SizeFlagsVertical = SizeFlags.ExpandFill,
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
            };
            bodyScroll.AddChild(body);
            stack.AddChild(bodyScroll);
        }
        else
        {
            stack.AddChild(body);
        }

        return panel;
    }

    private static TextureRect CreateIconRect(string iconKey, int size)
    {
        return new TextureRect
        {
            Texture = IconAtlas.GetIcon(iconKey),
            CustomMinimumSize = new Vector2(size, size),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
    }

    private static void ConfigureHeaderLabel(Label label)
    {
        label.ClipText = true;
        label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
    }

    private static void ClearContainer(Container container)
    {
        foreach (Node child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }
}
