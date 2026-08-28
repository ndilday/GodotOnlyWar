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
    public event EventHandler<Region> RegionPressed;
    public event EventHandler<Region> RegionActivated;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(160, 64);
        SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        FocusMode = FocusModeEnum.All;
        ClipText = true;
        TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        Alignment = HorizontalAlignment.Center;
        ClipContents = true;
        _backgroundTexture = new TextureRect
        {
            Texture = GD.Load<Texture2D>(CardBackgroundPath),
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorLeft = 0,
            AnchorTop = 0,
            AnchorRight = 1,
            AnchorBottom = 1,
            OffsetLeft = 3,
            OffsetTop = 3,
            OffsetRight = -3,
            OffsetBottom = -3,
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
            OffsetLeft = 3,
            OffsetTop = 3,
            OffsetRight = -3,
            OffsetBottom = -3,
            Color = new Color(0.035f, 0.03f, 0.02f, 0.38f)
        };
        AddChild(backgroundShade);
        _regionNameLabel = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            AnchorLeft = 0,
            AnchorTop = 0,
            AnchorRight = 1,
            AnchorBottom = 1,
            OffsetLeft = 5,
            OffsetTop = 5,
            OffsetRight = -5,
            OffsetBottom = -38
        };
        _regionNameLabel.AddThemeFontSizeOverride("font_size", 14);
        _regionNameLabel.AddThemeColorOverride("font_color", OnlyWarStyle.BodyText);
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
            OffsetBottom = -8
        };
        _badgeStrip.AddThemeConstantOverride("separation", 2);
        AddChild(_badgeStrip);
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
        if (model.HasPlayerForces)
        {
            AddFactionBadge("map_player", "Chapter forces present", badgeKeys);
        }
        AddThemeColorOverride("font_color", OnlyWarStyle.BodyText);
        AddThemeColorOverride("font_hover_color", OnlyWarStyle.Gold);

        Color border = model.ControlBorderColor;
        // Keep selection visible after the pointer leaves the card and after the workspace
        // rebuilds. Gold is the persistent selection treatment regardless of control state.
        Color normalBorder = selected ? OnlyWarStyle.Gold : border;
        StyleBoxFlat normal = CreateCardStyle(model.TerrainVariant, normalBorder, selected, false);
        StyleBoxFlat hover = CreateCardStyle(model.TerrainVariant, OnlyWarStyle.Gold, true, true);
        AddThemeStyleboxOverride("normal", normal);
        AddThemeStyleboxOverride("hover", hover);
        AddThemeStyleboxOverride("pressed", hover);
        AddThemeStyleboxOverride("focus", hover);
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
        if (string.IsNullOrWhiteSpace(iconKey)
            || !IconAtlas.HasPlanetaryOperationsFactionIcon(iconKey)
            || !badgeKeys.Add(iconKey))
        {
            return;
        }

        TextureRect icon = new()
        {
            Texture = IconAtlas.GetPlanetaryOperationsFactionIcon(iconKey),
            CustomMinimumSize = new Vector2(38, 38),
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
        bool selected,
        bool hover)
    {
        Color terrain = TerrainColors[Math.Abs(terrainVariant) % TerrainColors.Length];
        return new StyleBoxFlat
        {
            BgColor = new Color(terrain.R, terrain.G, terrain.B, hover ? 1f : 0.88f),
            BorderColor = border,
            BorderWidthLeft = selected ? 3 : 2,
            BorderWidthTop = selected ? 3 : 2,
            BorderWidthRight = selected ? 3 : 2,
            BorderWidthBottom = selected ? 3 : 2,
            CornerRadiusTopLeft = 2,
            CornerRadiusTopRight = 2,
            CornerRadiusBottomLeft = 2,
            CornerRadiusBottomRight = 2,
            ContentMarginLeft = 5,
            ContentMarginTop = 4,
            ContentMarginRight = 5,
            ContentMarginBottom = 4,
            ExpandMarginLeft = selected ? 2 : 0,
            ExpandMarginTop = selected ? 2 : 0,
            ExpandMarginRight = selected ? 2 : 0,
            ExpandMarginBottom = selected ? 2 : 0
        };
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
            $"Unassigned Squads: {model.UnassignedSquads}",
            $"Mission Opportunities: {model.MissionOpportunities}"
        ];
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
        _mapRows.AddThemeConstantOverride("separation", 3);
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
            row.AddThemeConstantOverride("separation", 5);
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
        AddBorderLegend(_controlLegend, OnlyWarStyle.MapContested, "CONTESTED");
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
        AddFactionIconLegend(row, "map_orks", "ORKS");
        return row;
    }

    private static void AddBorderLegend(HBoxContainer row, Color color, string label)
    {
        Panel sample = new()
        {
            CustomMinimumSize = new Vector2(16, 14),
            MouseFilter = MouseFilterEnum.Ignore
        };
        sample.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = OnlyWarStyle.WithAlpha(OnlyWarStyle.MapBackground, 0.92f),
            BorderColor = color,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2
        });
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
