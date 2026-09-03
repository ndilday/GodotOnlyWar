using Godot;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.PlanetaryOperations;
using OnlyWar.Helpers.UI;
using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class RegionMapCardView : Button
{
    private const string CardBackgroundPath =
        "res://Assets/UI/PlanetaryOperations/region_card_ash_wastes.png";

    private static readonly Color[] TerrainColors =
    [
        Color.Color8(32, 38, 37),
        Color.Color8(42, 35, 31),
        Color.Color8(31, 35, 42),
        Color.Color8(45, 41, 30),
        Color.Color8(35, 30, 38),
        Color.Color8(29, 42, 39)
    ];

    private Region _region;
    private TextureRect _backgroundTexture;
    private Label _regionNameLabel;
    private HBoxContainer _badgeStrip;
    private Label _strengthLabel;
    private Label _ordersLabel;
    private bool _drawContestedBorder;
    private bool _selected;
    private Color _controlBorderColor;
    public event EventHandler<Region> RegionPressed;
    public event EventHandler<Region> RegionActivated;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(160, 78);
        SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        FocusMode = FocusModeEnum.All;
        ClipText = true;
        TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        Alignment = HorizontalAlignment.Center;
        // The selected treatment intentionally extends beyond the control border.
        ClipContents = false;
        _backgroundTexture = new TextureRect
        {
            Texture = GD.Load<Texture2D>(CardBackgroundPath),
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorLeft = 0,
            AnchorTop = 0,
            AnchorRight = 1,
            AnchorBottom = 1,
            OffsetLeft = 2,
            OffsetTop = 2,
            OffsetRight = -2,
            OffsetBottom = -2,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            Modulate = new Color(1f, 1f, 1f, 0.42f)
        };
        AddChild(_backgroundTexture);
        ColorRect backgroundShade = new()
        {
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorLeft = 0,
            AnchorTop = 0,
            AnchorRight = 1,
            AnchorBottom = 1,
            OffsetLeft = 2,
            OffsetTop = 2,
            OffsetRight = -2,
            OffsetBottom = -2,
            Color = new Color(0.025f, 0.023f, 0.018f, 0.43f)
        };
        AddChild(backgroundShade);
        _regionNameLabel = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming,
            MaxLinesVisible = 2,
            AnchorLeft = 0,
            AnchorTop = 0,
            AnchorRight = 1,
            AnchorBottom = 0,
            OffsetLeft = 5,
            OffsetTop = 3,
            OffsetRight = -5,
            OffsetBottom = 42
        };
        _regionNameLabel.AddThemeFontSizeOverride("font_size", 16);
        _regionNameLabel.AddThemeColorOverride("font_color", OnlyWarStyle.Gold);
        _regionNameLabel.AddThemeColorOverride("font_hover_color", OnlyWarStyle.Gold);
        AddChild(_regionNameLabel);
        _badgeStrip = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.Center,
            AnchorLeft = 0,
            AnchorTop = 1,
            AnchorRight = 1,
            AnchorBottom = 1,
            OffsetLeft = 5,
            OffsetTop = -46,
            OffsetRight = -5,
            OffsetBottom = -20
        };
        _badgeStrip.AddThemeConstantOverride("separation", 4);
        AddChild(_badgeStrip);

        PanelContainer footerBand = new()
        {
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorLeft = 0,
            AnchorTop = 1,
            AnchorRight = 1,
            AnchorBottom = 1,
            OffsetLeft = 2,
            OffsetTop = -19,
            OffsetRight = -2,
            OffsetBottom = -2
        };
        footerBand.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.012f, 0.014f, 0.013f, 0.86f),
            BorderColor = OnlyWarStyle.WithAlpha(OnlyWarStyle.Gold, 0.30f),
            BorderWidthTop = 1,
            ContentMarginLeft = 4,
            ContentMarginRight = 4
        });
        HBoxContainer footer = new()
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        footer.AddThemeConstantOverride("separation", 3);
        _strengthLabel = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        _strengthLabel.AddThemeFontSizeOverride("font_size", 12);
        _strengthLabel.AddThemeColorOverride("font_color", OnlyWarStyle.BodyText);
        footer.AddChild(_strengthLabel);
        _ordersLabel = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ShrinkEnd,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        _ordersLabel.AddThemeFontSizeOverride("font_size", 12);
        _ordersLabel.AddThemeColorOverride("font_color", OnlyWarStyle.Gold);
        footer.AddChild(_ordersLabel);
        footerBand.AddChild(footer);
        AddChild(footerBand);

        Resized += QueueRedraw;
        Pressed += () => RegionPressed?.Invoke(this, _region);
        GuiInput += OnGuiInput;
    }

    public void Configure(
        RegionMapCardViewModel model,
        bool selected)
    {
        _region = model.Region;
        TooltipText = BuildTooltip(model);
        Text = string.Empty;
        _regionNameLabel.Text = model.Name.ToUpperInvariant();
        Icon = null;
        ClearBadges();
        HashSet<string> badgeKeys = [];
        foreach (RegionPresencePresentation presence in model.Presences)
        {
            AddFactionBadge(
                presence.IconKey,
                $"Disclosed presence: {presence.FactionName}",
                badgeKeys);
        }
        if (model.FactionActivity is string factionActivity)
        {
            AddFactionBadge(
                model.FactionActivityIconKey ?? "map_faction",
                factionActivity,
                badgeKeys);
        }
        if (model.HasPlayerForces)
        {
            AddFactionBadge("map_player", "Chapter forces present", badgeKeys);
        }
        _strengthLabel.Text = model.PlayerSquads == 0
            ? "—"
            : $"{model.PlayerSquads} SQ · {model.PlayerEffectiveStrength}/{model.PlayerFullStrength}";
        _strengthLabel.HorizontalAlignment = model.PlayerSquads == 0 && model.ActiveOrders == 0
            ? HorizontalAlignment.Center
            : HorizontalAlignment.Left;
        _ordersLabel.Text = model.ActiveOrders > 0
            ? $"▶ {model.ActiveOrders}"
            : model.PlayerSquads > 0 ? "—" : string.Empty;
        AddThemeColorOverride("font_color", OnlyWarStyle.BodyText);
        AddThemeColorOverride("font_hover_color", OnlyWarStyle.Gold);

        Color border = model.ControlBorderColor;
        _drawContestedBorder = model.Control == RegionControlState.Contested;
        _selected = selected;
        _controlBorderColor = border;
        // Selection is drawn separately as cool-white corner brackets, leaving the control
        // border intact. That keeps selection visually distinct from Imperial gold and the
        // orange contested treatment instead of visually reclassifying the region.
        StyleBoxFlat normal = CreateCardStyle(
            model.TerrainVariant, border, false, _drawContestedBorder);
        StyleBoxFlat hover = CreateCardStyle(
            model.TerrainVariant, border, true, _drawContestedBorder);
        AddThemeStyleboxOverride("normal", normal);
        AddThemeStyleboxOverride("hover", hover);
        AddThemeStyleboxOverride("pressed", hover);
        AddThemeStyleboxOverride("focus", hover);
        QueueRedraw();
    }

    private void ClearBadges()
    {
        foreach (Node child in _badgeStrip.GetChildren())
        {
            _badgeStrip.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void AddFactionBadge(string iconKey, string tooltip, HashSet<string> badgeKeys)
    {
        if (string.IsNullOrWhiteSpace(iconKey))
        {
            GD.PushError($"Missing required planetary-operations faction art for {tooltip}");
            return;
        }
        if (!badgeKeys.Add(iconKey))
        {
            return;
        }
        if (!IconAtlas.HasPlanetaryOperationsFactionIcon(iconKey))
        {
            GD.PushError($"Missing required planetary-operations faction art: {iconKey}");
            return;
        }

        TextureRect icon = new()
        {
            Texture = IconAtlas.GetPlanetaryOperationsFactionIcon(iconKey),
            CustomMinimumSize = new Vector2(26, 26),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            TooltipText = tooltip,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _badgeStrip.AddChild(icon);
    }

    private static StyleBoxFlat CreateCardStyle(
        int terrainVariant,
        Color border,
        bool hover,
        bool contested)
    {
        Color terrain = TerrainColors[Math.Abs(terrainVariant) % TerrainColors.Length];
        return new StyleBoxFlat
        {
            BgColor = new Color(terrain.R, terrain.G, terrain.B, hover ? 1f : 0.88f),
            BorderColor = contested ? Colors.Transparent : border,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 2,
            CornerRadiusTopRight = 2,
            CornerRadiusBottomLeft = 2,
            CornerRadiusBottomRight = 2,
            ContentMarginLeft = 5,
            ContentMarginTop = 4,
            ContentMarginRight = 5,
            ContentMarginBottom = 4
        };
    }

    public override void _Draw()
    {
        if (_selected)
        {
            DrawSelectionCorners();
        }

        if (!_drawContestedBorder || Size.X < 4 || Size.Y < 4) return;

        const float inset = 1.5f;
        Vector2 topLeft = new(inset, inset);
        Vector2 topRight = new(Size.X - inset, inset);
        Vector2 bottomRight = new(Size.X - inset, Size.Y - inset);
        Vector2 bottomLeft = new(inset, Size.Y - inset);
        DrawDashedEdge(topLeft, topRight, _controlBorderColor);
        DrawDashedEdge(topRight, bottomRight, _controlBorderColor);
        DrawDashedEdge(bottomRight, bottomLeft, _controlBorderColor);
        DrawDashedEdge(bottomLeft, topLeft, _controlBorderColor);
    }

    private void DrawSelectionCorners()
    {
        const float offset = 3f;
        const float armLength = 20f;
        Color selectionColor = Color.Color8(232, 242, 241);

        Vector2 topLeft = new(-offset, -offset);
        Vector2 topRight = new(Size.X + offset, -offset);
        Vector2 bottomRight = new(Size.X + offset, Size.Y + offset);
        Vector2 bottomLeft = new(-offset, Size.Y + offset);

        // The broad pass supplies a restrained halo; the narrow, near-white pass makes the
        // brackets unmistakable without competing with faction-colored control borders.
        DrawSelectionCorner(topLeft, Vector2.Right, Vector2.Down, armLength,
            OnlyWarStyle.WithAlpha(selectionColor, 0.24f), 6f);
        DrawSelectionCorner(topRight, Vector2.Left, Vector2.Down, armLength,
            OnlyWarStyle.WithAlpha(selectionColor, 0.24f), 6f);
        DrawSelectionCorner(bottomRight, Vector2.Left, Vector2.Up, armLength,
            OnlyWarStyle.WithAlpha(selectionColor, 0.24f), 6f);
        DrawSelectionCorner(bottomLeft, Vector2.Right, Vector2.Up, armLength,
            OnlyWarStyle.WithAlpha(selectionColor, 0.24f), 6f);

        DrawSelectionCorner(topLeft, Vector2.Right, Vector2.Down, armLength,
            OnlyWarStyle.WithAlpha(selectionColor, 0.96f), 2.5f);
        DrawSelectionCorner(topRight, Vector2.Left, Vector2.Down, armLength,
            OnlyWarStyle.WithAlpha(selectionColor, 0.96f), 2.5f);
        DrawSelectionCorner(bottomRight, Vector2.Left, Vector2.Up, armLength,
            OnlyWarStyle.WithAlpha(selectionColor, 0.96f), 2.5f);
        DrawSelectionCorner(bottomLeft, Vector2.Right, Vector2.Up, armLength,
            OnlyWarStyle.WithAlpha(selectionColor, 0.96f), 2.5f);
    }

    private void DrawSelectionCorner(
        Vector2 corner,
        Vector2 horizontalDirection,
        Vector2 verticalDirection,
        float armLength,
        Color color,
        float width)
    {
        DrawLine(corner, corner + horizontalDirection * armLength, color, width, true);
        DrawLine(corner, corner + verticalDirection * armLength, color, width, true);
    }

    private void DrawDashedEdge(Vector2 start, Vector2 end, Color color)
    {
        const float dashLength = 8f;
        const float gapLength = 5f;
        float length = start.DistanceTo(end);
        if (length <= 0f) return;
        Vector2 direction = (end - start) / length;
        for (float offset = 0; offset < length; offset += dashLength + gapLength)
        {
            DrawLine(
                start + direction * offset,
                start + direction * Math.Min(offset + dashLength, length),
                color,
                2f,
                true);
        }
    }

    private void OnGuiInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseButton mouse
            && mouse.ButtonIndex == MouseButton.Left
            && mouse.Pressed
            && mouse.DoubleClick)
        {
            RegionActivated?.Invoke(this, _region);
            AcceptEvent();
        }
    }

    internal static string BuildTooltip(RegionMapCardViewModel model)
    {
        List<string> rows =
        [
            model.Name,
            $"Control: {ControlLabel(model)}",
            $"Surface Squads: {model.PlayerSquads}",
            $"Duty-Ready Strength: {model.PlayerEffectiveStrength}/{model.PlayerFullStrength}",
            $"Active Orders: {model.ActiveOrders}",
            $"Unassigned Squads: {model.UnassignedSquads}",
            $"Mission Opportunities: {model.MissionOpportunities}"
        ];
        if (model.FactionActivity is string factionActivity)
        {
            rows.Add($"Faction Activity: {factionActivity}");
        }
        rows.AddRange(model.PublicEnemyForces.Select(enemy =>
            $"{enemy.FactionName}: {enemy.ForceEstimate}"));
        return string.Join("\n", rows);
    }

    private static string ControlLabel(RegionMapCardViewModel model) => model.Control switch
    {
        RegionControlState.Contested => "Contested",
        _ => string.IsNullOrWhiteSpace(model.ControlFactionName)
            ? "Contested"
            : model.ControlFactionName
    };
}

public partial class RegionControlLegendSample : Control
{
    public Color BorderColor { get; init; }
    public bool Dashed { get; init; }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(16, 14);
        MouseFilter = MouseFilterEnum.Ignore;
        QueueRedraw();
    }

    public override void _Draw()
    {
        Rect2 rect = new(new Vector2(1, 1), Size - new Vector2(2, 2));
        DrawRect(rect, OnlyWarStyle.WithAlpha(OnlyWarStyle.MapBackground, 0.92f), true);
        if (!Dashed)
        {
            DrawRect(rect, BorderColor, false, 2f, true);
            return;
        }

        DrawDashedEdge(rect.Position, rect.Position + new Vector2(rect.Size.X, 0));
        DrawDashedEdge(rect.Position + new Vector2(rect.Size.X, 0), rect.End);
        DrawDashedEdge(rect.End, rect.Position + new Vector2(0, rect.Size.Y));
        DrawDashedEdge(rect.Position + new Vector2(0, rect.Size.Y), rect.Position);
    }

    private void DrawDashedEdge(Vector2 start, Vector2 end)
    {
        const float dashLength = 4f;
        const float gapLength = 3f;
        float length = start.DistanceTo(end);
        if (length <= 0f) return;
        Vector2 direction = (end - start) / length;
        for (float offset = 0; offset < length; offset += dashLength + gapLength)
        {
            DrawLine(
                start + direction * offset,
                start + direction * Math.Min(offset + dashLength, length),
                BorderColor,
                2f,
                true);
        }
    }
}

public partial class PlanetRegionMapView : PanelContainer
{
    private VBoxContainer _mapRows;
    private HBoxContainer _controlLegend;
    private HBoxContainer _presenceLegend;
    private readonly List<RegionMapCardView> _cards = [];
    private readonly Dictionary<int, RegionMapCardView> _cardsByRegionId = [];
    private Region _selectedRegion;

    public event EventHandler<Region> RegionSelected;
    public event EventHandler<Region> RegionActivated;
    public event EventHandler BackgroundPressed;

    public ulong PersistentInstanceId => GetInstanceId();

    public override void _Ready()
    {
        Theme = GD.Load<Theme>("res://Scenes/OnlyWarTheme.tres");
        OnlyWarStyle.ApplyContentPanel(this);
        ClipContents = true;
        GuiInput += OnGuiInput;
        VBoxContainer stack = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
        stack.AddThemeConstantOverride("separation", 5);
        AddChild(stack);

        _mapRows = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.Center
        };
        _mapRows.AddThemeConstantOverride("separation", 7);
        stack.AddChild(_mapRows);

        VBoxContainer legends = new();
        legends.AddThemeConstantOverride("separation", 2);
        _controlLegend = new HBoxContainer();
        _controlLegend.AddThemeConstantOverride("separation", 4);
        ConfigureControlLegend([]);
        _presenceLegend = BuildPresenceLegend();
        legends.AddChild(_controlLegend);
        legends.AddChild(_presenceLegend);
        stack.AddChild(legends);
    }

    public void Display(PlanetRegionMapViewModel model, Region selectedRegion)
    {
        _selectedRegion = selectedRegion;
        ConfigureControlLegend(model.Rows.SelectMany(row => row).ToList());
        EnsureCardGeometry(model.Rows.Select(row => row.Count).ToList());

        _cardsByRegionId.Clear();
        int cursor = 0;
        foreach (IReadOnlyList<RegionMapCardViewModel> row in model.Rows)
        {
            foreach (RegionMapCardViewModel cardModel in row)
            {
                RegionMapCardView card = _cards[cursor++];
                card.Visible = true;
                card.Configure(
                    cardModel,
                    cardModel.Region?.Id == selectedRegion?.Id);
                _cardsByRegionId[cardModel.Region.Id] = card;
            }
        }
        while (cursor < _cards.Count)
        {
            _cards[cursor++].Visible = false;
        }
        ConfigureAdjacencyFocus(model.Rows.SelectMany(row => row).ToList());
    }

    public void FocusSelectedRegion()
    {
        if (_selectedRegion != null
            && _cardsByRegionId.TryGetValue(_selectedRegion.Id, out RegionMapCardView card))
        {
            card.GrabFocus();
        }
    }

    private void EnsureCardGeometry(IReadOnlyList<int> rowCounts)
    {
        if (_cards.Count > 0) return;
        int cardIndex = 0;
        foreach (int count in rowCounts)
        {
            HBoxContainer row = new()
            {
                Alignment = BoxContainer.AlignmentMode.Center,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill
            };
            row.AddThemeConstantOverride("separation", 7);
            _mapRows.AddChild(row);
            for (int index = 0; index < count; index++)
            {
                RegionMapCardView card = new() { Name = $"RegionCard{cardIndex}" };
                card.RegionPressed += (_, region) => RegionSelected?.Invoke(this, region);
                card.RegionActivated += (_, region) => RegionActivated?.Invoke(this, region);
                row.AddChild(card);
                _cards.Add(card);
                cardIndex++;
            }
        }
    }

    private void ConfigureAdjacencyFocus(
        IReadOnlyList<RegionMapCardViewModel> cardModels)
    {
        foreach (RegionMapCardViewModel model in cardModels)
        {
            if (!_cardsByRegionId.TryGetValue(model.Region.Id, out RegionMapCardView card)) continue;
            List<Region> neighbours = model.Region.GetAdjacentRegions();
            int rowKey = PlanetRegionMapViewModelBuilder.GetVisualRowKey(model.Region);
            Region north = neighbours
                .Where(region => PlanetRegionMapViewModelBuilder.GetVisualRowKey(region) < rowKey)
                .OrderByDescending(region => PlanetRegionMapViewModelBuilder.GetVisualRowKey(region))
                .ThenBy(region => Math.Abs(region.Coordinates.X - model.Region.Coordinates.X))
                .FirstOrDefault();
            Region south = neighbours
                .Where(region => PlanetRegionMapViewModelBuilder.GetVisualRowKey(region) > rowKey)
                .OrderBy(region => PlanetRegionMapViewModelBuilder.GetVisualRowKey(region))
                .ThenBy(region => Math.Abs(region.Coordinates.X - model.Region.Coordinates.X))
                .FirstOrDefault();
            Region west = neighbours.Where(region => region.Coordinates.X < model.Region.Coordinates.X)
                .OrderByDescending(region => region.Coordinates.X)
                .ThenBy(region => Math.Abs(PlanetRegionMapViewModelBuilder.GetVisualRowKey(region) - rowKey))
                .FirstOrDefault();
            Region east = neighbours.Where(region => region.Coordinates.X > model.Region.Coordinates.X)
                .OrderBy(region => region.Coordinates.X)
                .ThenBy(region => Math.Abs(PlanetRegionMapViewModelBuilder.GetVisualRowKey(region) - rowKey))
                .FirstOrDefault();
            card.FocusNeighborTop = CardPath(north);
            card.FocusNeighborBottom = CardPath(south);
            card.FocusNeighborLeft = CardPath(west);
            card.FocusNeighborRight = CardPath(east);
        }
    }

    private NodePath CardPath(Region region) =>
        region != null && _cardsByRegionId.TryGetValue(region.Id, out RegionMapCardView card)
            ? card.GetPath()
            : new NodePath();

    private void OnGuiInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseButton mouse
            && mouse.ButtonIndex == MouseButton.Left
            && mouse.Pressed
            && _mapRows.GetGlobalRect().HasPoint(mouse.GlobalPosition)
            && !_cards.Any(card =>
                card.Visible && card.GetGlobalRect().HasPoint(mouse.GlobalPosition)))
        {
            BackgroundPressed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ConfigureControlLegend(
        IReadOnlyList<RegionMapCardViewModel> cardModels)
    {
        foreach (Node child in _controlLegend.GetChildren())
        {
            _controlLegend.RemoveChild(child);
            child.QueueFree();
        }

        _controlLegend.AddChild(LegendText("CONTROL BORDER:"));
        AddBorderLegend(_controlLegend, OnlyWarStyle.Gold, "IMPERIAL");
        foreach (RegionMapCardViewModel card in cardModels
            .Where(card => card.Control == RegionControlState.Enemy
                && card.ControlFactionId.HasValue
                && !string.IsNullOrWhiteSpace(card.ControlFactionName))
            .GroupBy(card => card.ControlFactionId.Value)
            .Select(group => group.First())
            .OrderBy(card => card.ControlFactionName)
            .ThenBy(card => card.ControlFactionId))
        {
            AddBorderLegend(
                _controlLegend,
                card.ControlBorderColor,
                card.ControlFactionName.ToUpperInvariant());
        }
        AddBorderLegend(_controlLegend, OnlyWarStyle.MapContested, "CONTESTED", dashed: true);
    }

    private static HBoxContainer BuildPresenceLegend()
    {
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 4);
        row.AddChild(LegendText("ICONS:"));
        AddFactionIconLegend(row, "map_imperial", "IMPERIAL");
        AddFactionIconLegend(row, "map_player", "ASTARTES");
        AddFactionIconLegend(row, "map_genestealer_cult", "CULT");
        AddFactionIconLegend(row, "map_tyranids", "TYRANIDS");
        return row;
    }

    private static void AddBorderLegend(
        HBoxContainer row,
        Color color,
        string label,
        bool dashed = false)
    {
        RegionControlLegendSample sample = new()
        {
            BorderColor = color,
            Dashed = dashed
        };
        row.AddChild(sample);
        row.AddChild(LegendText(label));
    }

    private static void AddFactionIconLegend(HBoxContainer row, string iconKey, string label)
    {
        row.AddChild(CreateFactionLegendIcon(iconKey, label));
        row.AddChild(LegendText(label));
    }

    private static TextureRect CreateFactionLegendIcon(string iconKey, string tooltip)
    {
        return new TextureRect
        {
            Texture = IconAtlas.GetPlanetaryOperationsFactionIcon(iconKey),
            CustomMinimumSize = new Vector2(17, 17),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            TooltipText = tooltip,
            MouseFilter = MouseFilterEnum.Ignore
        };
    }

    private static Label LegendLabel(string text)
    {
        Label label = LegendText(text);
        label.AddThemeColorOverride("font_color", OnlyWarStyle.Gold);
        return label;
    }

    private static Label LegendText(string text)
    {
        Label label = new() { Text = text };
        label.AddThemeFontSizeOverride("font_size", 9);
        label.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        return label;
    }

}
