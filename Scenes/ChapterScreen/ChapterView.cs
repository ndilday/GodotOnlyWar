using Godot;
using OnlyWar.Helpers.UI;
using System;
using System.Collections.Generic;

public enum CompanyType
{
    Veteran,
    Tactical,
    ReserveTactical,
    ReserveAssault,
    ReserveDevastator,
    Scout
}

public sealed record ChapterBrowserItemEvent(ChapterBrowserLevel Level, int Id);

public partial class ChapterView : Control
{
    private HBoxContainer _breadcrumbBar;
    private Label _leftTitleLabel;
    private Label _leftHintLabel;
    private VBoxContainer _leftMenuVBox;
    private TextureRect _detailIcon;
    private Label _detailTitleLabel;
    private Label _detailSubtitleLabel;
    private GridContainer _metricGrid;
    private GridContainer _detailCardGrid;
    private Button _closeButton;

    public event EventHandler<ChapterBrowserItemEvent> BrowserItemSelected;
    public event EventHandler<ChapterBrowserItemEvent> BrowserItemDrillRequested;
    public event EventHandler<ChapterBrowserLevel> BreadcrumbPressed;
    public event EventHandler CloseButtonPressed;

    public override void _Ready()
    {
        _breadcrumbBar = GetNode<HBoxContainer>("Content/BreadcrumbBar");
        _leftTitleLabel = GetNode<Label>("Content/MainLayout/LeftMenu/Panel/MarginContainer/MenuStack/Header/TitleLabel");
        _leftHintLabel = GetNode<Label>("Content/MainLayout/LeftMenu/Panel/MarginContainer/MenuStack/Header/HintLabel");
        _leftMenuVBox = GetNode<VBoxContainer>("Content/MainLayout/LeftMenu/Panel/MarginContainer/MenuStack/ScrollContainer/LeftMenuVBox");
        _detailIcon = GetNode<TextureRect>("Content/MainLayout/DetailPanel/Panel/MarginContainer/DetailStack/Hero/DetailIcon");
        _detailTitleLabel = GetNode<Label>("Content/MainLayout/DetailPanel/Panel/MarginContainer/DetailStack/Hero/TitleStack/TitleLabel");
        _detailSubtitleLabel = GetNode<Label>("Content/MainLayout/DetailPanel/Panel/MarginContainer/DetailStack/Hero/TitleStack/SubtitleLabel");
        _metricGrid = GetNode<GridContainer>("Content/MainLayout/DetailPanel/Panel/MarginContainer/DetailStack/Hero/MetricGrid");
        _detailCardGrid = GetNode<GridContainer>("Content/MainLayout/DetailPanel/Panel/MarginContainer/DetailStack/DetailCardGrid");
        _closeButton = GetNode<Button>("TopBar/CloseButton");

        IconAtlas.Apply(_closeButton, "close", 40);
        _closeButton.Text = "";
        _closeButton.Pressed += () => CloseButtonPressed?.Invoke(this, EventArgs.Empty);
    }

    public void SetBreadcrumbs(IReadOnlyList<ChapterBreadcrumbItem> breadcrumbs)
    {
        ClearContainer(_breadcrumbBar);

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
            _breadcrumbBar.AddChild(button);
        }
    }

    public void SetLeftMenu(string title, string hint, IReadOnlyList<ChapterBrowserMenuItem> items)
    {
        _leftTitleLabel.Text = title;
        _leftHintLabel.Text = hint;
        ClearContainer(_leftMenuVBox);

        foreach (ChapterBrowserMenuItem item in items)
        {
            _leftMenuVBox.AddChild(CreateMenuRow(item));
        }
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
        foreach (ChapterBrowserDetailCard card in detail.Cards)
        {
            _detailCardGrid.AddChild(CreateDetailCard(card));
        }
    }

    private Control CreateMenuRow(ChapterBrowserMenuItem item)
    {
        PanelContainer row = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0, 58),
            MouseDefaultCursorShape = CursorShape.PointingHand
        };
        row.AddThemeStyleboxOverride("panel", CreateRowStyle(item.IsSelected));
        row.GuiInput += inputEvent =>
        {
            if (inputEvent is InputEventMouseButton mouseButton &&
                mouseButton.ButtonIndex == MouseButton.Left &&
                mouseButton.Pressed)
            {
                if (mouseButton.DoubleClick && item.CanDrill)
                {
                    BrowserItemDrillRequested?.Invoke(this, new ChapterBrowserItemEvent(item.Level, item.Id));
                }
                else
                {
                    BrowserItemSelected?.Invoke(this, new ChapterBrowserItemEvent(item.Level, item.Id));
                }
            }
        };

        HBoxContainer rowContent = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Begin,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        rowContent.AddThemeConstantOverride("separation", 8);
        row.AddChild(rowContent);

        TextureRect icon = new TextureRect
        {
            Texture = IconAtlas.GetIcon(item.IconKey),
            CustomMinimumSize = new Vector2(22, 22),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
        };
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
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        textStack.AddChild(title);

        Label subtitle = new Label
        {
            Text = item.Subtitle,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        subtitle.AddThemeColorOverride("font_color", new Color(0.66f, 0.60f, 0.49f));
        subtitle.AddThemeFontSizeOverride("font_size", 12);
        textStack.AddChild(subtitle);

        Button drillButton = new Button
        {
            Text = item.CanDrill ? ">" : "i",
            CustomMinimumSize = new Vector2(32, 32),
            MouseDefaultCursorShape = CursorShape.PointingHand,
            TooltipText = item.CanDrill ? "Drill into this item" : "Show details"
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
        panel.AddThemeStyleboxOverride("panel", CreateInsetStyle());

        VBoxContainer stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 0);
        panel.AddChild(stack);

        Label value = new Label { Text = metric.Value };
        value.AddThemeColorOverride("font_color", new Color(0.96f, 0.84f, 0.52f));
        value.AddThemeFontSizeOverride("font_size", 18);
        stack.AddChild(value);

        Label label = new Label { Text = metric.Label };
        label.AddThemeColorOverride("font_color", new Color(0.66f, 0.60f, 0.49f));
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
        panel.AddThemeStyleboxOverride("panel", CreateInsetStyle());

        VBoxContainer stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 6);
        panel.AddChild(stack);

        HBoxContainer heading = new HBoxContainer();
        heading.AddThemeConstantOverride("separation", 8);
        stack.AddChild(heading);

        TextureRect icon = new TextureRect
        {
            Texture = IconAtlas.GetIcon(card.IconKey),
            CustomMinimumSize = new Vector2(22, 22),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
        };
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
        subtitle.AddThemeColorOverride("font_color", new Color(0.66f, 0.60f, 0.49f));
        subtitle.AddThemeFontSizeOverride("font_size", 12);
        titleStack.AddChild(subtitle);

        Label body = new Label
        {
            Text = card.Body,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        body.AddThemeFontSizeOverride("font_size", 13);
        stack.AddChild(body);

        return panel;
    }

    private static StyleBoxFlat CreateRowStyle(bool selected)
    {
        return new StyleBoxFlat
        {
            BgColor = selected ? new Color(0.13f, 0.09f, 0.04f, 0.96f) : new Color(0.02f, 0.023f, 0.024f, 0.92f),
            BorderColor = selected ? new Color(0.96f, 0.84f, 0.52f, 0.95f) : new Color(0.33f, 0.28f, 0.18f, 0.72f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 2,
            CornerRadiusTopRight = 2,
            CornerRadiusBottomLeft = 2,
            CornerRadiusBottomRight = 2,
            ContentMarginLeft = 9,
            ContentMarginTop = 7,
            ContentMarginRight = 9,
            ContentMarginBottom = 7
        };
    }

    private static StyleBoxFlat CreateInsetStyle()
    {
        return new StyleBoxFlat
        {
            BgColor = new Color(0.008f, 0.01f, 0.012f, 0.88f),
            BorderColor = new Color(0.33f, 0.28f, 0.18f, 0.72f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 2,
            CornerRadiusTopRight = 2,
            CornerRadiusBottomLeft = 2,
            CornerRadiusBottomRight = 2,
            ContentMarginLeft = 10,
            ContentMarginTop = 8,
            ContentMarginRight = 10,
            ContentMarginBottom = 8
        };
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
