using Godot;
using OnlyWar.Helpers.UI;
using System;
using System.Collections.Generic;

public partial class SoldierView : Control
{
    private const int SoldierIconSize = 48;

    private TextureRect _detailIcon;
    private Label _detailTitleLabel;
    private Label _detailSubtitleLabel;
    private GridContainer _metricGrid;
    private GridContainer _detailCardGrid;
    private MenuButton _transferButton;
    private Button _closeButton;

    public event EventHandler<int> TransferTargetSelected;
    public event EventHandler CloseButtonPressed;

    public override void _Ready()
    {
        ClearContainer(this);
        BuildLayout();

        _closeButton.Pressed += () => CloseButtonPressed?.Invoke(this, EventArgs.Empty);
        _transferButton.GetPopup().IndexPressed += index => TransferTargetSelected?.Invoke(this, (int)index);
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

    private void BuildLayout()
    {
        Panel backgroundPanel = new();
        ApplyFullRect(backgroundPanel);
        AddChild(backgroundPanel);

        Panel topBar = new()
        {
            CustomMinimumSize = new Vector2(0, 56)
        };
        topBar.AnchorRight = 1;
        topBar.OffsetBottom = 56;
        AddChild(topBar);

        Label title = new()
        {
            Text = "SOLDIER RECORD",
            VerticalAlignment = VerticalAlignment.Center
        };
        title.AnchorRight = 1;
        title.AnchorBottom = 1;
        title.OffsetLeft = 14;
        title.OffsetRight = -58;
        topBar.AddChild(title);

        _closeButton = new Button
        {
            Text = "",
            CustomMinimumSize = new Vector2(40, 40)
        };
        IconAtlas.Apply(_closeButton, "close", 40);
        _closeButton.AnchorLeft = 1;
        _closeButton.AnchorRight = 1;
        _closeButton.OffsetLeft = -46;
        _closeButton.OffsetTop = 8;
        _closeButton.OffsetRight = -6;
        _closeButton.OffsetBottom = 48;
        topBar.AddChild(_closeButton);

        MarginContainer content = new();
        ApplyFullRect(content);
        content.OffsetTop = 68;
        content.OffsetLeft = 16;
        content.OffsetRight = -16;
        content.OffsetBottom = -16;
        AddChild(content);

        VBoxContainer detailStack = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        detailStack.AddThemeConstantOverride("separation", 10);
        content.AddChild(detailStack);

        HBoxContainer hero = new()
        {
            CustomMinimumSize = new Vector2(0, 94)
        };
        hero.AddThemeConstantOverride("separation", 12);
        detailStack.AddChild(hero);

        _detailIcon = new TextureRect
        {
            CustomMinimumSize = new Vector2(SoldierIconSize, SoldierIconSize),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
        };
        hero.AddChild(_detailIcon);

        VBoxContainer titleStack = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        titleStack.AddThemeConstantOverride("separation", 4);
        hero.AddChild(titleStack);

        _detailTitleLabel = new Label
        {
            Text = "Soldier",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        titleStack.AddChild(_detailTitleLabel);

        _detailSubtitleLabel = new Label
        {
            Text = "Record",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        titleStack.AddChild(_detailSubtitleLabel);

        _metricGrid = new GridContainer
        {
            CustomMinimumSize = new Vector2(340, 0),
            Columns = 3
        };
        hero.AddChild(_metricGrid);

        ScrollContainer detailScroll = new()
        {
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        detailStack.AddChild(detailScroll);

        _detailCardGrid = new GridContainer
        {
            Columns = 3,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _detailCardGrid.AddThemeConstantOverride("h_separation", 8);
        _detailCardGrid.AddThemeConstantOverride("v_separation", 8);
        detailScroll.AddChild(_detailCardGrid);

        _transferButton = new MenuButton
        {
            Text = "Transfer",
            Visible = false,
            Disabled = true,
            CustomMinimumSize = new Vector2(0, 36)
        };
        detailStack.AddChild(_transferButton);
    }

    private static Control CreateMetricPanel(ChapterBrowserMetric metric)
    {
        PanelContainer panel = new()
        {
            CustomMinimumSize = new Vector2(110, 58)
        };
        panel.AddThemeStyleboxOverride("panel", CreateInsetStyle());

        VBoxContainer stack = new();
        stack.AddThemeConstantOverride("separation", 0);
        panel.AddChild(stack);

        Label value = new() { Text = metric.Value };
        value.AddThemeColorOverride("font_color", new Color(0.96f, 0.84f, 0.52f));
        value.AddThemeFontSizeOverride("font_size", 18);
        stack.AddChild(value);

        Label label = new() { Text = metric.Label };
        label.AddThemeColorOverride("font_color", new Color(0.66f, 0.60f, 0.49f));
        label.AddThemeFontSizeOverride("font_size", 12);
        stack.AddChild(label);

        return panel;
    }

    private static Control CreateDetailCard(ChapterBrowserDetailCard card)
    {
        PanelContainer panel = new()
        {
            CustomMinimumSize = new Vector2(0, 112),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        panel.AddThemeStyleboxOverride("panel", CreateInsetStyle());

        VBoxContainer stack = new();
        stack.AddThemeConstantOverride("separation", 6);
        panel.AddChild(stack);

        HBoxContainer heading = new();
        heading.AddThemeConstantOverride("separation", 8);
        stack.AddChild(heading);

        TextureRect icon = new()
        {
            Texture = IconAtlas.GetIcon(card.IconKey),
            CustomMinimumSize = new Vector2(SoldierIconSize, SoldierIconSize),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        heading.AddChild(icon);

        VBoxContainer titleStack = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        titleStack.AddThemeConstantOverride("separation", 0);
        heading.AddChild(titleStack);

        Label title = new()
        {
            Text = card.Title,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        titleStack.AddChild(title);

        Label subtitle = new()
        {
            Text = card.Subtitle,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        subtitle.AddThemeColorOverride("font_color", new Color(0.66f, 0.60f, 0.49f));
        subtitle.AddThemeFontSizeOverride("font_size", 12);
        titleStack.AddChild(subtitle);

        Label body = new()
        {
            Text = card.Body,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        body.AddThemeFontSizeOverride("font_size", 13);
        stack.AddChild(body);

        return panel;
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

    private static void ApplyFullRect(Control control)
    {
        control.AnchorRight = 1;
        control.AnchorBottom = 1;
        control.GrowHorizontal = GrowDirection.Both;
        control.GrowVertical = GrowDirection.Both;
    }

    private static void ClearContainer(Node container)
    {
        foreach (Node child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }
}
