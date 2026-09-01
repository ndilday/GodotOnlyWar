using Godot;
using OnlyWar.Builders;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Scenes.MainGameScreen;
using System;
using System.Collections.Generic;
using System.Linq;
using NumericsVector2 = System.Numerics.Vector2;

enum Facing { North, East, South, West}
struct BorderPoint
{
	public Vector2I gridPos;
	public Vector2I mapPoint;
	public Facing orientation;
}

public partial class SectorMap : Node2D
{
    private sealed class LabelDrawInfo
    {
        public string Text { get; init; }
        public Vector2 Position { get; init; }
        public int FontSize { get; init; }
        public SectorLabelBandStyle Style { get; init; }
        public Color Color { get; init; }
    }

    private readonly struct EdgeKey : IEquatable<EdgeKey>
    {
        public readonly Vector2I A;
        public readonly Vector2I B;

        public EdgeKey(Vector2I a, Vector2I b)
        {
            if (ComparePoints(a, b) <= 0)
            {
                A = a;
                B = b;
            }
            else
            {
                A = b;
                B = a;
            }
        }

        public bool Equals(EdgeKey other)
        {
            return A == other.A && B == other.B;
        }

        public override bool Equals(object obj)
        {
            return obj is EdgeKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(A, B);
        }

        private static int ComparePoints(Vector2I left, Vector2I right)
        {
            int xCompare = left.X.CompareTo(right.X);
            return xCompare != 0 ? xCompare : left.Y.CompareTo(right.Y);
        }
    }

    private static readonly Color[] SubsectorPalette =
    [
        Color.Color8(118, 28, 46),
        Color.Color8(22, 102, 106),
        Color.Color8(86, 55, 132),
        Color.Color8(36, 68, 128),
        Color.Color8(78, 112, 62),
        Color.Color8(125, 88, 42)
    ];
    private static readonly Color SubsectorBorderColor = Color.Color8(176, 132, 66);
    private static readonly Color SubsectorBorderGlowColor = Color.Color8(218, 177, 94);
    private static readonly Color SubsectorBorderShadowColor = Color.Color8(7, 6, 5);
    private const float SubsectorGridFillAlpha = 0.08f;
    private const float SubsectorGlassFillAlpha = 0.24f;
    private const float SubsectorInnerStainAlpha = 0.08f;
    private const float PolygonSimplificationToleranceCellFraction = 1.1f;
    private const float PolygonSimplificationMaxBridgeCellFraction = 4.5f;
    private const int PolygonSmoothingPasses = 2;
    private const float SeamSimplificationToleranceCellFraction = 2.4f;
    private const float SeamSimplificationMaxBridgeCellFraction = 8.0f;
    private const int SeamCurveSamplesPerSegment = 10;

    // PROTOTYPE: when true, subsector regions are drawn from a constrained Voronoi
    // tessellation (VoronoiSubsectorMapper) instead of the grid-traced/smoothed polygons.
    private const bool UseVoronoiBorders = true;

    public event EventHandler<int> PlanetClicked;
    public event EventHandler<int> PlanetDoubleClicked;
    public event EventHandler<int> FleetClicked;
    public event EventHandler<int> FleetRightClicked;
    public event EventHandler BackgroundClicked;

    [Export]
    public Godot.Collections.Array<SectorLabelBandStyle> LabelBandStyles { get; set; } = new();

    [Export]
    public bool LabelsVisible { get; set; } = true;

    public Vector2I GridDimensions { get; private set; }
	public Vector2I CellSize { get; private set; }

    public Vector2I HalfCellSize { get; private set; }
    public ushort[] SectorIds { get; private set; }
    public bool[] HasPlanet { get; private set; }
    private Camera2D _camera;
    private Sprite2D _background;
    private readonly List<Node> _fleetSprites = [];
	
	private Dictionary<ushort, List<Vector2I>> _subsectorVertexListMap = [];
    private List<Vector2[]> _subsectorBoundaryPaths = [];
    private Dictionary<ushort, List<Vector2[]>> _voronoiSubsectorLoops = [];
	private Dictionary<ushort, HashSet<ushort>> _subsectorAdjacencyMap = [];
	private Dictionary<ushort, int> _subsectorColorIndexMap = [];
    private List<Subsector> _subsectors = [];
    private readonly List<LabelDrawInfo> _subsectorLabels = [];
    private readonly List<LabelDrawInfo> _planetLabelsBandB = [];
    private readonly List<LabelDrawInfo> _planetLabelsBandC = [];
    private int? _selectedPlanetId;

    public override void _EnterTree()
    {
        EnsureMapMetricsInitialized();
    }

    public bool EnsureMapMetricsInitialized()
    {
        if (GridDimensions != Vector2I.Zero && CellSize != Vector2I.Zero) return true;
        if (!GameDataSingleton.Instance.IsInitialized) return false;

        GridDimensions = new(
            GameDataSingleton.Instance.GameRulesData.SectorGenerationProfile.SectorWidth,
            GameDataSingleton.Instance.GameRulesData.SectorGenerationProfile.SectorHeight);
        CellSize = new(
            PresentationMetrics.SectorMapCellWidth,
            PresentationMetrics.SectorMapCellHeight);
        HalfCellSize = CellSize / 2;
        return true;
    }

    // Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
        _camera = GetNode<Camera2D>("Camera2D");
        _background = GetNodeOrNull<Sprite2D>("Background");
        if (!EnsureMapMetricsInitialized())
        {
            GD.PushError("SectorMap requires initialized game data before the scene is readied.");
            return;
        }

        LayoutBackground();
		SectorIds = new ushort[GridDimensions.X * GridDimensions.Y];
		HasPlanet = new bool[GridDimensions.X * GridDimensions.Y];
		PlacePlanets();
		RefreshFleets();
        _subsectors = SubsectorBuilder.BuildSubsectors(
            GameDataSingleton.Instance.Sector.Planets.Values,
            GridDimensions,
            GameDataSingleton.Instance.GameRulesData.SectorGenerationProfile.MaxSubsectorDiameter);
        foreach(Subsector subsector in _subsectors)
        {
            foreach (Vector2I cell in subsector.Cells)
            {
                SectorIds[GridPositionToIndex(cell)] = subsector.Id;
            }
        }
        CopyGovernanceSeatsFromSectorData();
        _subsectorAdjacencyMap = DetermineSubsectorAdjacency(_subsectors.Select(subsector => subsector.Id));
        _subsectorColorIndexMap = AssignSubsectorColorIndexes(_subsectorAdjacencyMap);
        ValidateSubsectorColoring(_subsectorAdjacencyMap, _subsectorColorIndexMap);
        _subsectorVertexListMap = DetermineSubsectorBorderPoints(_subsectors);
        _subsectorBoundaryPaths = DetermineSubsectorBoundaryPaths();
        if (UseVoronoiBorders)
        {
            Dictionary<ushort, List<Planet>> subsectorPlanetMap =
                _subsectors.ToDictionary(subsector => subsector.Id, subsector => subsector.Planets);
            var voronoiBorders = OnlyWar.Helpers.VoronoiSubsectorMapper.BuildSubsectorLoops(
                subsectorPlanetMap,
                GridDimensions,
                GameDataSingleton.Instance.GameRulesData.SectorGenerationProfile.MaxSubsectorDiameter);
            _voronoiSubsectorLoops = voronoiBorders.Loops;

            // Recolor from the Voronoi adjacency (shared border edges) so that
            // neighboring subsectors never share a palette color.
            EnsureAdjacencyEntries(voronoiBorders.Adjacency, _subsectors.Select(subsector => subsector.Id));
            _subsectorAdjacencyMap = voronoiBorders.Adjacency;
            _subsectorColorIndexMap = AssignSubsectorColorIndexes(_subsectorAdjacencyMap);
            ValidateSubsectorColoring(_subsectorAdjacencyMap, _subsectorColorIndexMap);
        }
        TaskForce centerFleet = GameDataSingleton.Instance.Sector.PlayerForce.Fleet.TaskForces.FirstOrDefault();
        Coordinate? centerPosition = centerFleet?.Planet?.Position ?? centerFleet?.Position;
        if (centerPosition == null)
        {
            centerPosition = GameDataSingleton.Instance.Sector.Planets.Values.First().Position;
        }

        Vector2I gridPosition = new Vector2I(centerPosition.Value.X, centerPosition.Value.Y);
        Vector2I mapPosition = CalculateMapPosition(gridPosition);
        RebuildLabelLayouts();
        _camera.ZoomTo(1, mapPosition);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton
            {
                ButtonIndex: MouseButton.Left,
                Pressed: true
            }
            || !IsVisibleInTree()
            || IsPointerOverMapObject())
        {
            return;
        }

        BackgroundClicked?.Invoke(this, EventArgs.Empty);
        GetViewport().SetInputAsHandled();
    }

    private bool IsPointerOverMapObject()
    {
        return GetChildren().OfType<ClickableSprite2D>()
            .Any(sprite => sprite.IsVisibleInTree()
                && sprite.IsPixelOpaque(sprite.GetLocalMousePosition()));
    }

    private void CopyGovernanceSeatsFromSectorData()
    {
        IReadOnlyList<Subsector> sectorSubsectors = GameDataSingleton.Instance.Sector.Subsectors;
        foreach (Subsector subsector in _subsectors)
        {
            Subsector source = sectorSubsectors.FirstOrDefault(candidate => candidate.Id == subsector.Id);
            if (source != null)
            {
                subsector.SetGovernanceSeat(source.GovernanceSeat);
            }
        }
    }

    private void LayoutBackground()
    {
        if (_background?.Texture == null) return;

        Vector2 mapSize = new(GridDimensions.X * CellSize.X, GridDimensions.Y * CellSize.Y);
        Vector2 backgroundSize = mapSize + Vector2.One * (2 * _camera.MapBorderPixels);

        _background.Position = mapSize / 2.0f;
        _background.Scale = backgroundSize / _background.Texture.GetSize();
    }

	public override void _Draw()
	{
		base._Draw();
        if (!GameDataSingleton.Instance.IsInitialized) return;

        if (UseVoronoiBorders && _voronoiSubsectorLoops.Count > 0)
        {
            DrawVoronoiSubsectors();
            DrawLabels();
            DrawSelectedSystemOverlay();
            return;
        }

		foreach (var kvp in _subsectorVertexListMap.OrderBy(kvp => kvp.Key))
		{
            Vector2[] polygonPoints = kvp.Value.Select(vector => new Vector2(vector.X, vector.Y)).ToArray();
            Vector2[] smoothedPolygonPoints = BuildSmoothedPolygon(polygonPoints);
            Color baseColor = GetSubsectorColor(kvp.Key);

            DrawSubsectorFill(kvp.Key, polygonPoints, smoothedPolygonPoints, baseColor);
        }
        DrawSubsectorBoundaries();
        DrawLabels();
        DrawSelectedSystemOverlay();
	}

    public void SetSelectedPlanet(int? planetId)
    {
        _selectedPlanetId = planetId;
        QueueRedraw();
    }

    public void SetLabelsVisible(bool visible)
    {
        if (LabelsVisible == visible) return;
        LabelsVisible = visible;
        QueueRedraw();
    }

    public void ToggleLabelVisibility()
    {
        SetLabelsVisible(!LabelsVisible);
    }

    /// <summary>
    /// Rebuilds the turn-varying planet priorities. Subsector geometry and its placements are
    /// left untouched because they only change when the map is rebuilt or loaded.
    /// </summary>
    public void RefreshLabels()
    {
        if (!GameDataSingleton.Instance.IsInitialized || _subsectors.Count == 0) return;

        BuildPlanetLabelLayouts();
        QueueRedraw();
    }

    public void OnCameraZoomChanged(float zoom)
    {
        QueueRedraw();
    }

    private void RebuildLabelLayouts()
    {
        EnsureLabelStyles();
        BuildSubsectorLabelLayouts();
        BuildPlanetLabelLayouts();
    }

    private void EnsureLabelStyles()
    {
        LabelBandStyles ??= new Godot.Collections.Array<SectorLabelBandStyle>();
        SectorLabelBandStyle[] defaults = CreateDefaultLabelStyles();
        while (LabelBandStyles.Count < defaults.Length)
        {
            LabelBandStyles.Add(defaults[LabelBandStyles.Count]);
        }

        for (int i = 0; i < defaults.Length; i++)
        {
            if (LabelBandStyles[i] == null)
            {
                LabelBandStyles[i] = defaults[i];
            }
        }
    }

    private static SectorLabelBandStyle[] CreateDefaultLabelStyles()
    {
        return
        [
            new SectorLabelBandStyle
            {
                Font = CreateFontVariation("res://Assets/Fonts/caslon-antique.regular.ttf", 1.0f),
                WorldFontSize = 34.0f,
                MinZoom = 0.33f,
                MaxZoom = 1.1f,
                FontColor = new Color(0.980f, 0.906f, 0.725f, 1.0f),
                OutlineColor = new Color(0.0f, 0.0f, 0.0f, 0.82f),
                OutlineWidth = 2,
                ShadowColor = new Color(0.0f, 0.0f, 0.0f, 0.70f),
                ShadowSize = 1,
                ShadowOffset = new Vector2(1.0f, 1.0f),
                LetterSpacing = 1.0f
            },
            new SectorLabelBandStyle
            {
                Font = CreateFontVariation("res://Assets/Fonts/eb-garamond.12-regular-all-smallcaps.ttf", 1.25f),
                WorldFontSize = 10.0f,
                MinZoom = 1.1f,
                MaxZoom = 3.5f,
                FontColor = Colors.White,
                OutlineColor = new Color(0.0f, 0.0f, 0.0f, 0.82f),
                OutlineWidth = 1,
                ShadowColor = new Color(0.0f, 0.0f, 0.0f, 0.70f),
                ShadowSize = 1,
                ShadowOffset = new Vector2(1.0f, 1.0f),
                LetterSpacing = 1.25f
            },
            new SectorLabelBandStyle
            {
                Font = CreateFontVariation("res://Assets/Fonts/eb-garamond.12-regular-all-smallcaps.ttf", 1.0f),
                WorldFontSize = 3.5f,
                MinZoom = 3.5f,
                MaxZoom = 10.0f,
                FontColor = Colors.White,
                OutlineColor = new Color(0.0f, 0.0f, 0.0f, 0.82f),
                OutlineWidth = 1,
                ShadowColor = new Color(0.0f, 0.0f, 0.0f, 0.70f),
                ShadowSize = 1,
                ShadowOffset = new Vector2(1.0f, 1.0f),
                LetterSpacing = 1.0f
            }
        ];
    }

    private static FontVariation CreateFontVariation(string path, float letterSpacing)
    {
        FontVariation variation = new();
        FontFile baseFont = GD.Load<FontFile>(path);
        if (baseFont != null)
        {
            variation.SetBaseFont(baseFont);
        }

        variation.SpacingGlyph = Mathf.RoundToInt(letterSpacing);
        return variation;
    }

    private SectorLabelBandStyle GetLabelStyle(SectorMapLabelBand band)
    {
        EnsureLabelStyles();
        return LabelBandStyles[(int)band];
    }

    private void BuildSubsectorLabelLayouts()
    {
        _subsectorLabels.Clear();
        if (_subsectors.Count == 0) return;

        SectorLabelBandStyle style = GetLabelStyle(SectorMapLabelBand.A);
        List<SectorMapLabelCandidate> candidates = [];
        Dictionary<int, (string Text, int FontSize, float Scale)> metadata = [];

        foreach (Subsector subsector in _subsectors.OrderBy(subsector => subsector.Id))
        {
            string sourceText = string.IsNullOrWhiteSpace(subsector.Name)
                ? $"Subsector {subsector.Id}"
                : subsector.Name;
            (Vector2 anchor, float inscribedWidth) = GetSubsectorLabelGeometry(subsector);
            IReadOnlyList<IReadOnlyList<NumericsVector2>> allowedRegions =
                GetSubsectorLabelRegions(subsector);
            float maxWidth = inscribedWidth * 0.86f;
            string text = sourceText;
            (NumericsVector2 extent, int fontSize, float scale) = MeasureSubsectorLabel(
                style,
                text,
                maxWidth,
                ToNumerics(anchor),
                allowedRegions);

            // Keep short or comfortably fitting names on one line. The stacked form is
            // reserved for derived capital names that would otherwise be squeezed too far.
            if (HasSubsectorSuffix(sourceText) && (extent == NumericsVector2.Zero || scale < 0.90f))
            {
                text = FormatSubsectorLabel(sourceText);
                (extent, fontSize, scale) = MeasureSubsectorLabel(
                    style,
                    text,
                    maxWidth,
                    ToNumerics(anchor),
                    allowedRegions);
            }

            if (extent == NumericsVector2.Zero) continue;

            candidates.Add(new SectorMapLabelCandidate(
                subsector.Id,
                ToNumerics(anchor),
                0,
                extent,
                scale,
                allowedRegions));
            metadata[subsector.Id] = (text, fontSize, scale);
        }

        SectorMapLabelBounds mapBounds = GetLabelMapBounds();
        foreach (SectorMapLabelPlacement placement in SectorMapLabelLayout.Place(candidates, mapBounds))
        {
            if (!metadata.TryGetValue(placement.Id, out var data)) continue;

            _subsectorLabels.Add(new LabelDrawInfo
            {
                Text = data.Text,
                Position = ToGodot(placement.Position),
                FontSize = data.FontSize,
                Style = style,
                Color = style.FontColor
            });
        }
    }

    private void BuildPlanetLabelLayouts()
    {
        _planetLabelsBandB.Clear();
        _planetLabelsBandC.Clear();
        if (!GameDataSingleton.Instance.IsInitialized) return;

        BuildPlanetLabelLayout(GetLabelStyle(SectorMapLabelBand.B), _planetLabelsBandB);
        BuildPlanetLabelLayout(GetLabelStyle(SectorMapLabelBand.C), _planetLabelsBandC);
    }

    private void BuildPlanetLabelLayout(
        SectorLabelBandStyle style,
        List<LabelDrawInfo> output)
    {
        List<SectorMapLabelCandidate> candidates = [];
        Dictionary<int, (Planet Planet, int FontSize, float Scale)> metadata = [];
        IEnumerable<Planet> planets = GameDataSingleton.Instance.Sector.Planets.Values
            .Where(planet => !string.IsNullOrWhiteSpace(planet.Name))
            .OrderBy(planet => planet.Id);

        foreach (Planet planet in planets)
        {
            SectorMapPlanetLabelPriority priority = GetPlanetLabelPriority(planet);
            (NumericsVector2 extent, int fontSize, float scale) = MeasureLabel(style, planet.Name, 0);
            if (extent == NumericsVector2.Zero) continue;

            candidates.Add(new SectorMapLabelCandidate(
                planet.Id,
                ToNumerics(CalculateMapPosition(new Vector2I(planet.Position.X, planet.Position.Y))),
                priority.Rank,
                extent,
                scale));
            metadata[planet.Id] = (planet, fontSize, scale);
        }

        foreach (SectorMapLabelPlacement placement in SectorMapLabelLayout.Place(
            candidates,
            GetLabelMapBounds()))
        {
            if (!metadata.TryGetValue(placement.Id, out var data)) continue;

            output.Add(new LabelDrawInfo
            {
                Text = data.Planet.Name,
                Position = ToGodot(placement.Position),
                FontSize = data.FontSize,
                Style = style,
                Color = GetPlanetLabelColor(data.Planet, style)
            });
        }
    }

    private SectorMapPlanetLabelPriority GetPlanetLabelPriority(Planet planet)
    {
        bool hasActiveRequest = false;
        RequestSeverity severity = RequestSeverity.Concerned;
        foreach (IRequest request in GameDataSingleton.Instance.Sector.PlayerForce?.Requests ?? [])
        {
            if (request.TargetPlanet != planet
                || request.Status is not (RequestStatus.Open or RequestStatus.InProgress))
            {
                continue;
            }

            hasActiveRequest = true;
            severity = (RequestSeverity)Math.Max((int)severity, (int)request.Severity);
        }

        bool hasActiveMission = planet.Regions
            .Where(region => region != null)
            .Any(region => region.SpecialMissions.Count > 0);
        bool hasActiveOrder = GameDataSingleton.Instance.Sector.Orders.Values
            .Any(order => order.Mission?.RegionFaction?.Region?.Planet == planet);
        bool isGovernanceSeat = _subsectors.Any(subsector =>
            subsector.GovernanceSeat?.Id == planet.Id);

        return new SectorMapPlanetLabelPriority(
            planet.Id,
            hasActiveRequest || hasActiveMission || hasActiveOrder,
            severity,
            isGovernanceSeat,
            planet.Importance);
    }

    private static Color GetPlanetLabelColor(Planet planet, SectorLabelBandStyle style)
    {
        Faction controller = planet.GetControllingFaction();
        if (controller == null) return style.FontColor;

        System.Drawing.Color color = controller.Color;
        return new Color(color.R / 255.0f, color.G / 255.0f, color.B / 255.0f, 1.0f);
    }

    private (Vector2 Anchor, float InscribedWidth) GetSubsectorLabelGeometry(Subsector subsector)
    {
        if (subsector.Cells.Count > 0)
        {
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            Vector2 sum = Vector2.Zero;
            foreach (Vector2I cell in subsector.Cells)
            {
                Vector2 center = CalculateMapPosition(cell);
                sum += center;
                minX = Mathf.Min(minX, center.X - HalfCellSize.X);
                minY = Mathf.Min(minY, center.Y - HalfCellSize.Y);
                maxX = Mathf.Max(maxX, center.X + HalfCellSize.X);
                maxY = Mathf.Max(maxY, center.Y + HalfCellSize.Y);
            }

            return (sum / subsector.Cells.Count, Mathf.Max(CellSize.X, (maxX - minX) * 0.82f));
        }

        if (subsector.Planets.Count > 0)
        {
            Vector2 sum = subsector.Planets
                .Select(planet => CalculateMapPosition(new Vector2I(planet.Position.X, planet.Position.Y)))
                .Aggregate(Vector2.Zero, (current, point) => current + point);
            return (sum / subsector.Planets.Count, CellSize.X * 0.82f);
        }

        return (Vector2.Zero, 0);
    }

    private IReadOnlyList<IReadOnlyList<NumericsVector2>> GetSubsectorLabelRegions(Subsector subsector)
    {
        if (!_voronoiSubsectorLoops.TryGetValue(subsector.Id, out List<Vector2[]> loops))
            return [];

        return loops
            .Where(loop => loop.Length >= 3)
            .Select(loop => (IReadOnlyList<NumericsVector2>)loop
                .Select(GridToPixel)
                .Select(ToNumerics)
                .ToArray())
            .ToList();
    }

    private (NumericsVector2 Extent, int FontSize, float Scale) MeasureLabel(
        SectorLabelBandStyle style,
        string text,
        float maxWidth,
        float fontScaleLimit = 1.0f)
    {
        if (string.IsNullOrEmpty(text)) return (NumericsVector2.Zero, 0, 1.0f);

        Font font = GetLabelFont(style);
        int baseFontSize = Mathf.Max(1, Mathf.RoundToInt(style.WorldFontSize));
        Godot.Vector2 measured = MeasureText(font, text, baseFontSize);
        float scale = Mathf.Clamp(fontScaleLimit, 0.05f, 1.0f);
        if (maxWidth > 0 && measured.X * scale > maxWidth)
        {
            scale = maxWidth / measured.X;
        }

        int fontSize = Mathf.Max(1, Mathf.RoundToInt(baseFontSize * scale));
        measured = MeasureText(font, text, fontSize);
        return (
            new NumericsVector2(measured.X, measured.Y),
            fontSize,
            scale);
    }

    private static Godot.Vector2 MeasureText(Font font, string text, int fontSize)
    {
        string[] lines = text.Split('\n');
        float width = 0.0f;
        foreach (string line in lines)
        {
            width = Mathf.Max(
                width,
                font.GetStringSize(line, HorizontalAlignment.Left, -1, fontSize).X);
        }

        return new Godot.Vector2(width, font.GetHeight(fontSize) * lines.Length);
    }

    private static string FormatSubsectorLabel(string name)
    {
        const string suffix = " Subsector";
        if (!HasSubsectorSuffix(name)) return name;

        string capitalName = name[..^suffix.Length].TrimEnd();
        return capitalName.Length == 0 ? name : $"{capitalName}\nSubsector";
    }

    private static bool HasSubsectorSuffix(string name) =>
        name.EndsWith(" Subsector", StringComparison.OrdinalIgnoreCase);

    private (NumericsVector2 Extent, int FontSize, float Scale) MeasureSubsectorLabel(
        SectorLabelBandStyle style,
        string text,
        float maxWidth,
        NumericsVector2 anchor,
        IReadOnlyList<IReadOnlyList<NumericsVector2>> allowedRegions)
    {
        (NumericsVector2 baseExtent, int baseFontSize, float baseScale) = MeasureLabel(style, text, maxWidth);
        if (baseExtent == NumericsVector2.Zero || allowedRegions.Count == 0)
            return (baseExtent, baseFontSize, baseScale);

        SectorMapLabelBounds mapBounds = GetLabelMapBounds();
        for (float scale = baseScale; scale >= 0.20f; scale -= 0.05f)
        {
            (NumericsVector2 extent, int fontSize, float actualScale) =
                MeasureLabel(style, text, maxWidth, scale);
            SectorMapLabelCandidate candidate = new(
                0,
                anchor,
                0,
                extent,
                actualScale,
                allowedRegions);
            if (SectorMapLabelLayout.Place([candidate], mapBounds).Count > 0)
                return (extent, fontSize, actualScale);
        }

        return (NumericsVector2.Zero, 0, 1.0f);
    }

    private Font GetLabelFont(SectorLabelBandStyle style)
    {
        if (style?.Font == null) return ThemeDB.FallbackFont;

        // FontVariation owns tracking, so the inspector value remains live even when a
        // scene supplies a shared variation resource for two planet bands.
        style.Font.SpacingGlyph = Mathf.RoundToInt(style.LetterSpacing);
        return style.Font;
    }

    private SectorMapLabelBounds GetLabelMapBounds()
    {
        float border = _camera?.MapBorderPixels ?? 100.0f;
        return new SectorMapLabelBounds(
            -border,
            -border,
            GridDimensions.X * CellSize.X + border,
            GridDimensions.Y * CellSize.Y + border);
    }

    private void DrawLabels()
    {
        if (!LabelsVisible) return;

        float zoom = _camera?.Zoom.X ?? 1.0f;
        SectorLabelBandStyle subsectorStyle = GetLabelStyle(SectorMapLabelBand.A);
        SectorLabelBandStyle bandBStyle = GetLabelStyle(SectorMapLabelBand.B);
        SectorLabelBandStyle bandCStyle = GetLabelStyle(SectorMapLabelBand.C);

        DrawLabelList(_subsectorLabels, GetSubsectorLabelOpacity(zoom, subsectorStyle, bandBStyle));
        DrawLabelList(_planetLabelsBandB, GetBandOpacity(zoom, bandBStyle));
        DrawLabelList(_planetLabelsBandC, GetBandOpacity(zoom, bandCStyle, keepVisibleAtMax: true));
    }

    private void DrawLabelList(IEnumerable<LabelDrawInfo> labels, float opacity)
    {
        if (opacity <= 0.001f) return;

        foreach (LabelDrawInfo label in labels)
        {
            Font font = GetLabelFont(label.Style);
            float alpha = Mathf.Clamp(opacity * label.Style.Opacity * label.Color.A, 0.0f, 1.0f);
            if (alpha <= 0.001f) continue;

            Color textColor = new(label.Color.R, label.Color.G, label.Color.B, alpha);
            Color outlineColor = WithAlpha(label.Style.OutlineColor, label.Style.OutlineColor.A * alpha);
            Color shadowColor = WithAlpha(label.Style.ShadowColor, label.Style.ShadowColor.A * alpha);
            float lineHeight = font.GetHeight(label.FontSize);
            float textWidth = MeasureText(font, label.Text, label.FontSize).X;
            string[] lines = label.Text.Split('\n');
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex];
                float lineWidth = font.GetStringSize(
                    line,
                    HorizontalAlignment.Left,
                    -1,
                    label.FontSize).X;
                Vector2 baseline = label.Position + new Vector2(
                    (textWidth - lineWidth) / 2.0f,
                    font.GetAscent(label.FontSize) + lineHeight * lineIndex);

                if (label.Style.ShadowSize > 0)
                {
                    DrawStringOutline(
                        font,
                        baseline + label.Style.ShadowOffset,
                        line,
                        HorizontalAlignment.Left,
                        -1,
                        label.FontSize,
                        label.Style.ShadowSize,
                        shadowColor);
                }

                if (label.Style.OutlineWidth > 0)
                {
                    DrawStringOutline(
                        font,
                        baseline,
                        line,
                        HorizontalAlignment.Left,
                        -1,
                        label.FontSize,
                        label.Style.OutlineWidth,
                        outlineColor);
                }

                DrawString(font, baseline, line, HorizontalAlignment.Left, -1, label.FontSize, textColor);
            }
        }
    }

    private static float GetSubsectorLabelOpacity(
        float zoom,
        SectorLabelBandStyle subsectorStyle,
        SectorLabelBandStyle planetStyle)
    {
        float firstBoundary = subsectorStyle.MaxZoom;
        float secondBoundary = planetStyle.MaxZoom;
        float firstFade = Mathf.Max(0.05f, (secondBoundary - firstBoundary) * 0.12f);
        float dimmedAlpha = 0.28f;

        if (zoom <= firstBoundary - firstFade) return 1.0f;
        if (zoom < firstBoundary)
        {
            return Mathf.Lerp(1.0f, dimmedAlpha, Smooth01((zoom - (firstBoundary - firstFade)) / firstFade));
        }
        if (zoom < secondBoundary - firstFade)
        {
            return dimmedAlpha;
        }
        if (zoom < secondBoundary)
        {
            return Mathf.Lerp(dimmedAlpha, 0.0f, Smooth01((zoom - (secondBoundary - firstFade)) / firstFade));
        }
        return 0.0f;
    }

    private static float GetBandOpacity(
        float zoom,
        SectorLabelBandStyle style,
        bool keepVisibleAtMax = false)
    {
        float transition = Mathf.Max(0.05f, (style.MaxZoom - style.MinZoom) * 0.12f);
        if (zoom <= style.MinZoom - transition) return 0.0f;
        if (zoom < style.MinZoom)
        {
            return Smooth01((zoom - (style.MinZoom - transition)) / transition);
        }
        if (keepVisibleAtMax && zoom >= style.MinZoom) return 1.0f;
        if (zoom < style.MaxZoom - transition) return 1.0f;
        if (zoom < style.MaxZoom)
        {
            return 1.0f - Smooth01((zoom - (style.MaxZoom - transition)) / transition);
        }
        return 0.0f;
    }

    private static float Smooth01(float value)
    {
        value = Mathf.Clamp(value, 0.0f, 1.0f);
        return value * value * (3.0f - 2.0f * value);
    }

    private static NumericsVector2 ToNumerics(Vector2 vector) => new(vector.X, vector.Y);

    private static Vector2 ToGodot(NumericsVector2 vector) => new(vector.X, vector.Y);

    public void ZoomIn()
    {
        _camera.ZoomIn(null);
    }

    public void ZoomOut()
    {
        _camera.ZoomOut(null);
    }

    public void CenterOnSelectedPlanet()
    {
        if (!_selectedPlanetId.HasValue) return;
        if (!GameDataSingleton.Instance.Sector.Planets.TryGetValue(_selectedPlanetId.Value, out Planet planet)) return;

        Vector2I gridPosition = new(planet.Position.X, planet.Position.Y);
        _camera.ZoomTo(_camera.Zoom.X, CalculateMapPosition(gridPosition));
    }

    private Dictionary<ushort, HashSet<ushort>> DetermineSubsectorAdjacency(IEnumerable<ushort> subsectorIds)
    {
        Dictionary<ushort, HashSet<ushort>> adjacencyMap = subsectorIds
            .Distinct()
            .ToDictionary(id => id, _ => new HashSet<ushort>());

        for (int y = 0; y < GridDimensions.Y; y++)
        {
            for (int x = 0; x < GridDimensions.X; x++)
            {
                Vector2I cell = new(x, y);
                ushort currentId = SectorIds[GridPositionToIndex(cell)];
                if (currentId == 0) continue;

                AddSubsectorAdjacency(adjacencyMap, currentId, cell + Vector2I.Right);
                AddSubsectorAdjacency(adjacencyMap, currentId, cell + Vector2I.Down);
            }
        }

        return adjacencyMap;
    }

    private void AddSubsectorAdjacency(Dictionary<ushort, HashSet<ushort>> adjacencyMap, ushort currentId, Vector2I neighborCell)
    {
        if (!IsWithinBounds(neighborCell)) return;

        ushort neighborId = SectorIds[GridPositionToIndex(neighborCell)];
        if (neighborId == 0 || neighborId == currentId) return;

        adjacencyMap[currentId].Add(neighborId);
        adjacencyMap[neighborId].Add(currentId);
    }

    private Dictionary<ushort, int> AssignSubsectorColorIndexes(Dictionary<ushort, HashSet<ushort>> adjacencyMap)
    {
        Dictionary<ushort, int> colorIndexMap = [];

        if (TryAssignSubsectorColorIndexes(adjacencyMap, colorIndexMap))
        {
            return colorIndexMap;
        }

        GD.PushWarning("Subsector graph coloring failed; falling back to deterministic id-based colors.");
        foreach (ushort subsectorId in adjacencyMap.Keys.OrderBy(id => id))
        {
            colorIndexMap[subsectorId] = subsectorId % SubsectorPalette.Length;
        }
        return colorIndexMap;
    }

    private bool TryAssignSubsectorColorIndexes(Dictionary<ushort, HashSet<ushort>> adjacencyMap, Dictionary<ushort, int> colorIndexMap)
    {
        if (colorIndexMap.Count == adjacencyMap.Count) return true;

        ushort subsectorId = SelectNextSubsectorToColor(adjacencyMap, colorIndexMap);
        HashSet<int> usedNeighborColors = adjacencyMap[subsectorId]
            .Where(colorIndexMap.ContainsKey)
            .Select(neighborId => colorIndexMap[neighborId])
            .ToHashSet();

        for (int i = 0; i < SubsectorPalette.Length; i++)
        {
            if (usedNeighborColors.Contains(i)) continue;

            colorIndexMap[subsectorId] = i;
            if (TryAssignSubsectorColorIndexes(adjacencyMap, colorIndexMap))
            {
                return true;
            }
            colorIndexMap.Remove(subsectorId);
        }

        return false;
    }

    private ushort SelectNextSubsectorToColor(Dictionary<ushort, HashSet<ushort>> adjacencyMap, Dictionary<ushort, int> colorIndexMap)
    {
        return adjacencyMap.Keys
            .Where(id => !colorIndexMap.ContainsKey(id))
            .OrderByDescending(id => adjacencyMap[id]
                .Where(colorIndexMap.ContainsKey)
                .Select(neighborId => colorIndexMap[neighborId])
                .Distinct()
                .Count())
            .ThenByDescending(id => adjacencyMap[id].Count)
            .ThenBy(id => id)
            .First();
    }

    private void ValidateSubsectorColoring(Dictionary<ushort, HashSet<ushort>> adjacencyMap, Dictionary<ushort, int> colorIndexMap)
    {
        foreach (var kvp in adjacencyMap)
        {
            foreach (ushort neighborId in kvp.Value)
            {
                if (kvp.Key >= neighborId) continue;
                if (colorIndexMap[kvp.Key] != colorIndexMap[neighborId]) continue;

                GD.PushWarning($"Adjacent subsectors {kvp.Key} and {neighborId} share a color.");
            }
        }
    }

    private static void EnsureAdjacencyEntries(Dictionary<ushort, HashSet<ushort>> adjacencyMap, IEnumerable<ushort> subsectorIds)
    {
        foreach (ushort subsectorId in subsectorIds)
        {
            if (!adjacencyMap.ContainsKey(subsectorId))
            {
                adjacencyMap[subsectorId] = [];
            }
        }
    }

    private Color GetSubsectorColor(ushort subsectorId)
    {
        if (!_subsectorColorIndexMap.TryGetValue(subsectorId, out int colorIndex))
        {
            colorIndex = subsectorId % SubsectorPalette.Length;
        }

        return SubsectorPalette[colorIndex];
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        return new Color(color.R, color.G, color.B, alpha);
    }

    private void DrawSubsectorFill(ushort subsectorId, Vector2[] polygonPoints, Vector2[] smoothedPolygonPoints, Color baseColor)
    {
        DrawColoredPolygon(polygonPoints, WithAlpha(baseColor, SubsectorGridFillAlpha));
        DrawColoredPolygon(smoothedPolygonPoints, WithAlpha(baseColor, SubsectorGlassFillAlpha));

        Vector2 centroid = CalculateCentroid(smoothedPolygonPoints);
        float inset = 0.78f + 0.08f * Noise01(subsectorId, 17);
        Vector2[] innerStainPoints = ScalePolygon(smoothedPolygonPoints, centroid, inset);
        Color stainColor = TintSubsectorColor(baseColor, 1.18f);
        DrawColoredPolygon(innerStainPoints, WithAlpha(stainColor, SubsectorInnerStainAlpha));

        float secondInset = 0.48f + 0.08f * Noise01(subsectorId, 29);
        Vector2 offset = new(
            (Noise01(subsectorId, 31) - 0.5f) * CellSize.X * 1.6f,
            (Noise01(subsectorId, 37) - 0.5f) * CellSize.Y * 1.6f);
        Vector2[] shadowStainPoints = ScalePolygon(smoothedPolygonPoints, centroid + offset, secondInset);
        Color shadowColor = TintSubsectorColor(baseColor, 0.72f);
        DrawColoredPolygon(shadowStainPoints, WithAlpha(shadowColor, SubsectorInnerStainAlpha * 0.8f));
    }

    private Vector2 GridToPixel(Vector2 gridPoint)
    {
        return new Vector2(
            gridPoint.X * CellSize.X + HalfCellSize.X,
            gridPoint.Y * CellSize.Y + HalfCellSize.Y);
    }

    private void DrawVoronoiSubsectors()
    {
        // Fills first, so the border strokes sit cleanly on top of every region.
        foreach (var kvp in _voronoiSubsectorLoops.OrderBy(entry => entry.Key))
        {
            Color baseColor = GetSubsectorColor(kvp.Key);
            foreach (Vector2[] loop in kvp.Value)
            {
                if (loop.Length < 3) continue;
                Vector2[] pixelLoop = loop.Select(GridToPixel).ToArray();
                DrawColoredPolygon(pixelLoop, WithAlpha(baseColor, SubsectorGlassFillAlpha));
            }
        }

        foreach (var kvp in _voronoiSubsectorLoops.OrderBy(entry => entry.Key))
        {
            foreach (Vector2[] loop in kvp.Value)
            {
                if (loop.Length < 2) continue;
                Vector2[] pixelLoop = loop.Select(GridToPixel).ToArray();
                Vector2[] closedLoop = ClosePolygon(pixelLoop);
                DrawPolyline(closedLoop, WithAlpha(SubsectorBorderShadowColor, 0.82f), 4.2f, true);
                DrawPolyline(closedLoop, WithAlpha(SubsectorBorderGlowColor, 0.20f), 2.8f, true);
                DrawPolyline(closedLoop, WithAlpha(SubsectorBorderColor, 0.88f), 1.35f, true);
            }
        }
    }

    private void DrawSubsectorBoundaries()
    {
        foreach (Vector2[] boundaryPath in _subsectorBoundaryPaths)
        {
            if (boundaryPath.Length < 2) continue;

            DrawPolyline(boundaryPath, WithAlpha(SubsectorBorderShadowColor, 0.82f), 4.2f, true);
            DrawPolyline(boundaryPath, WithAlpha(SubsectorBorderGlowColor, 0.20f), 2.8f, true);
            DrawPolyline(boundaryPath, WithAlpha(SubsectorBorderColor, 0.88f), 1.35f, true);
        }
    }

    private static Color TintSubsectorColor(Color color, float multiplier)
    {
        return new Color(
            Mathf.Clamp(color.R * multiplier, 0.0f, 1.0f),
            Mathf.Clamp(color.G * multiplier, 0.0f, 1.0f),
            Mathf.Clamp(color.B * multiplier, 0.0f, 1.0f),
            color.A);
    }

    private static Vector2 CalculateCentroid(Vector2[] polygonPoints)
    {
        if (polygonPoints.Length == 0) return Vector2.Zero;

        Vector2 sum = Vector2.Zero;
        foreach (Vector2 point in polygonPoints)
        {
            sum += point;
        }

        return sum / polygonPoints.Length;
    }

    private static Vector2[] ScalePolygon(Vector2[] polygonPoints, Vector2 center, float scale)
    {
        Vector2[] scaledPoints = new Vector2[polygonPoints.Length];
        for (int i = 0; i < polygonPoints.Length; i++)
        {
            scaledPoints[i] = center + (polygonPoints[i] - center) * scale;
        }

        return scaledPoints;
    }

    private static Vector2[] ClosePolygon(Vector2[] polygonPoints)
    {
        if (polygonPoints.Length == 0) return polygonPoints;

        Vector2[] closedPolygonPoints = new Vector2[polygonPoints.Length + 1];
        polygonPoints.CopyTo(closedPolygonPoints, 0);
        closedPolygonPoints[^1] = polygonPoints[0];
        return closedPolygonPoints;
    }

    private Vector2[] BuildSmoothedPolygon(Vector2[] polygonPoints)
    {
        if (polygonPoints.Length < 3) return polygonPoints;

        Vector2[] simplifiedPoints = SimplifyClosedPolygon(
            polygonPoints,
            PolygonSimplificationToleranceCellFraction,
            PolygonSimplificationMaxBridgeCellFraction);
        if (simplifiedPoints.Length < 3) return polygonPoints;

        return SmoothClosedPolygon(simplifiedPoints, PolygonSmoothingPasses);
    }

    private Vector2[] SimplifyClosedPolygon(Vector2[] polygonPoints, float toleranceCellFraction, float maxBridgeCellFraction)
    {
        List<Vector2> simplifiedPoints = polygonPoints.ToList();
        float cellSize = Mathf.Min(CellSize.X, CellSize.Y);
        float tolerance = cellSize * toleranceCellFraction;
        float maxBridgeLengthSquared = Mathf.Pow(cellSize * maxBridgeCellFraction, 2);

        for (int pass = 0; pass < 4 && simplifiedPoints.Count > 3; pass++)
        {
            bool removedAny = false;

            for (int i = 0; i < simplifiedPoints.Count && simplifiedPoints.Count > 3; i++)
            {
                Vector2 previous = simplifiedPoints[(i - 1 + simplifiedPoints.Count) % simplifiedPoints.Count];
                Vector2 current = simplifiedPoints[i];
                Vector2 next = simplifiedPoints[(i + 1) % simplifiedPoints.Count];
                float bridgeLengthSquared = previous.DistanceSquaredTo(next);
                float distanceFromBridge = DistanceToSegment(current, previous, next);

                if (distanceFromBridge <= 0.1f || (distanceFromBridge <= tolerance && bridgeLengthSquared <= maxBridgeLengthSquared))
                {
                    simplifiedPoints.RemoveAt(i);
                    removedAny = true;
                    i--;
                }
            }

            if (!removedAny) break;
        }

        return simplifiedPoints.ToArray();
    }

    private static Vector2[] SmoothClosedPolygon(Vector2[] polygonPoints, int passes)
    {
        Vector2[] smoothedPoints = polygonPoints;

        for (int pass = 0; pass < passes && smoothedPoints.Length >= 3; pass++)
        {
            Vector2[] nextPoints = new Vector2[smoothedPoints.Length * 2];

            for (int i = 0; i < smoothedPoints.Length; i++)
            {
                Vector2 current = smoothedPoints[i];
                Vector2 next = smoothedPoints[(i + 1) % smoothedPoints.Length];
                nextPoints[i * 2] = current.Lerp(next, 0.25f);
                nextPoints[i * 2 + 1] = current.Lerp(next, 0.75f);
            }

            smoothedPoints = nextPoints;
        }

        return smoothedPoints;
    }

    private Vector2[] BuildSmoothedBoundaryPath(Vector2[] boundaryPoints)
    {
        if (boundaryPoints.Length < 3) return boundaryPoints;

        bool isClosed = boundaryPoints[0] == boundaryPoints[^1];
        Vector2[] points = isClosed ? boundaryPoints.Take(boundaryPoints.Length - 1).ToArray() : boundaryPoints;
        Vector2[] simplifiedPoints = isClosed
            ? SimplifyClosedPolygon(points, SeamSimplificationToleranceCellFraction, SeamSimplificationMaxBridgeCellFraction)
            : SimplifyOpenPolyline(points, SeamSimplificationToleranceCellFraction, SeamSimplificationMaxBridgeCellFraction);

        if (simplifiedPoints.Length < 2) return boundaryPoints;

        Vector2[] smoothedPoints = isClosed
            ? BuildClosedCatmullRomCurve(simplifiedPoints, SeamCurveSamplesPerSegment)
            : BuildOpenCatmullRomCurve(simplifiedPoints, SeamCurveSamplesPerSegment);

        return isClosed ? ClosePolygon(smoothedPoints) : smoothedPoints;
    }

    private Vector2[] SimplifyOpenPolyline(Vector2[] polylinePoints, float toleranceCellFraction, float maxBridgeCellFraction)
    {
        if (polylinePoints.Length < 3) return polylinePoints;

        float cellSize = Mathf.Min(CellSize.X, CellSize.Y);
        float tolerance = cellSize * toleranceCellFraction;
        Vector2[] simplifiedPoints = SimplifyPolylineDouglasPeucker(polylinePoints, tolerance);
        if (simplifiedPoints.Length < 3) return simplifiedPoints;

        List<Vector2> locallySmoothedPoints = simplifiedPoints.ToList();
        float maxBridgeLengthSquared = Mathf.Pow(cellSize * maxBridgeCellFraction, 2);

        for (int pass = 0; pass < 2 && locallySmoothedPoints.Count > 2; pass++)
        {
            bool removedAny = false;
            for (int i = 1; i < locallySmoothedPoints.Count - 1 && locallySmoothedPoints.Count > 2; i++)
            {
                Vector2 previous = locallySmoothedPoints[i - 1];
                Vector2 current = locallySmoothedPoints[i];
                Vector2 next = locallySmoothedPoints[i + 1];
                float bridgeLengthSquared = previous.DistanceSquaredTo(next);
                float distanceFromBridge = DistanceToSegment(current, previous, next);

                if (distanceFromBridge <= 0.1f || (distanceFromBridge <= tolerance && bridgeLengthSquared <= maxBridgeLengthSquared))
                {
                    locallySmoothedPoints.RemoveAt(i);
                    removedAny = true;
                    i--;
                }
            }

            if (!removedAny) break;
        }

        return locallySmoothedPoints.ToArray();
    }

    private static Vector2[] SimplifyPolylineDouglasPeucker(Vector2[] points, float tolerance)
    {
        if (points.Length < 3) return points;

        bool[] keep = new bool[points.Length];
        keep[0] = true;
        keep[^1] = true;
        MarkDouglasPeuckerPoints(points, 0, points.Length - 1, tolerance, keep);

        List<Vector2> simplifiedPoints = [];
        for (int i = 0; i < points.Length; i++)
        {
            if (keep[i])
            {
                simplifiedPoints.Add(points[i]);
            }
        }

        return simplifiedPoints.ToArray();
    }

    private static void MarkDouglasPeuckerPoints(Vector2[] points, int startIndex, int endIndex, float tolerance, bool[] keep)
    {
        if (endIndex <= startIndex + 1) return;

        float maxDistance = 0.0f;
        int farthestIndex = -1;
        Vector2 start = points[startIndex];
        Vector2 end = points[endIndex];

        for (int i = startIndex + 1; i < endIndex; i++)
        {
            float distance = DistanceToSegment(points[i], start, end);
            if (distance > maxDistance)
            {
                maxDistance = distance;
                farthestIndex = i;
            }
        }

        if (farthestIndex == -1 || maxDistance <= tolerance) return;

        keep[farthestIndex] = true;
        MarkDouglasPeuckerPoints(points, startIndex, farthestIndex, tolerance, keep);
        MarkDouglasPeuckerPoints(points, farthestIndex, endIndex, tolerance, keep);
    }

    private static Vector2[] BuildOpenCatmullRomCurve(Vector2[] points, int samplesPerSegment)
    {
        if (points.Length < 3) return points;

        List<Vector2> curvePoints = [points[0]];
        for (int i = 0; i < points.Length - 1; i++)
        {
            Vector2 p0 = points[Math.Max(i - 1, 0)];
            Vector2 p1 = points[i];
            Vector2 p2 = points[i + 1];
            Vector2 p3 = points[Math.Min(i + 2, points.Length - 1)];
            for (int sample = 1; sample <= samplesPerSegment; sample++)
            {
                float t = sample / (float)samplesPerSegment;
                curvePoints.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }

        return curvePoints.ToArray();
    }

    private static Vector2[] BuildClosedCatmullRomCurve(Vector2[] points, int samplesPerSegment)
    {
        if (points.Length < 3) return points;

        List<Vector2> curvePoints = [];
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 p0 = points[(i - 1 + points.Length) % points.Length];
            Vector2 p1 = points[i];
            Vector2 p2 = points[(i + 1) % points.Length];
            Vector2 p3 = points[(i + 2) % points.Length];
            for (int sample = 0; sample < samplesPerSegment; sample++)
            {
                float t = sample / (float)samplesPerSegment;
                curvePoints.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }

        return curvePoints.ToArray();
    }

    private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (
            (2.0f * p1)
            + (-p0 + p2) * t
            + (2.0f * p0 - 5.0f * p1 + 4.0f * p2 - p3) * t2
            + (-p0 + 3.0f * p1 - 3.0f * p2 + p3) * t3);
    }

    private static float Noise01(ushort seed, int salt)
    {
        uint value = (uint)seed * 73856093u
            ^ (uint)(salt + 1) * 19349663u;

        value ^= value >> 16;
        value *= 2246822519u;
        value ^= value >> 13;
        value *= 3266489917u;
        value ^= value >> 16;

        return (value & 0x00FFFFFF) / 16777215.0f;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        Vector2 segment = end - start;
        float segmentLengthSquared = segment.LengthSquared();
        if (segmentLengthSquared <= 0.001f) return point.DistanceTo(start);

        float t = Mathf.Clamp((point - start).Dot(segment) / segmentLengthSquared, 0.0f, 1.0f);
        return point.DistanceTo(start + segment * t);
    }

    private void DrawSelectedSystemOverlay()
    {
        if (!_selectedPlanetId.HasValue) return;
        if (!GameDataSingleton.Instance.Sector.Planets.TryGetValue(_selectedPlanetId.Value, out Planet planet)) return;

        Vector2 center = CalculateMapPosition(new Vector2I(planet.Position.X, planet.Position.Y));
        float baseRadius = Mathf.Min(CellSize.X, CellSize.Y) * 0.42f;
        Color ringColor = Color.Color8(99, 199, 215);
        DrawArc(center, baseRadius, 0, Mathf.Tau, 96, WithAlpha(ringColor, 0.72f), 2.0f, true);
        DrawArc(center, baseRadius * 1.45f, 0, Mathf.Tau, 96, WithAlpha(ringColor, 0.28f), 1.2f, true);
        DrawArc(center, baseRadius * 1.9f, 0, Mathf.Tau, 96, WithAlpha(ringColor, 0.16f), 1.0f, true);

        List<TaskForce> orbitingFleets = GameDataSingleton.Instance.Sector.Fleets.Values
            .Where(fleet => fleet.Planet == planet && fleet.TravelPhase == FleetTravelPhase.InOrbit)
            .OrderBy(fleet => fleet.Id)
            .ToList();

        // The fleet's ship sprite is placed up-and-to-the-right of the planet by
        // half a cell (see PlaceFleets), so anchor the highlight there rather than
        // on an orbit angle, keeping the marker on the actual fleet icon.
        Vector2 fleetAnchor = center + new Vector2(HalfCellSize.X, -HalfCellSize.Y);

        for (int i = 0; i < orbitingFleets.Count; i++)
        {
            // Fan multiple orbiting fleets horizontally so their markers don't fully overlap.
            float fanOffset = (i - (orbitingFleets.Count - 1) / 2.0f) * 7.0f;
            Vector2 fleetPosition = fleetAnchor + new Vector2(fanOffset, 0.0f);
            bool isPlayerFleet = orbitingFleets[i].Faction == GameDataSingleton.Instance.Sector.PlayerForce.Faction;
            Color fleetColor = isPlayerFleet ? Color.Color8(99, 199, 215) : Color.Color8(204, 83, 71);
            DrawCircle(fleetPosition, 5.0f, WithAlpha(fleetColor, 0.85f), true, -1.0f, true);
            DrawArc(fleetPosition, 8.0f, 0, Mathf.Tau, 24, WithAlpha(fleetColor, 0.55f), 1.0f, true);
        }
    }

    public new void SetProcessInput(bool enable)
    {
        base.SetProcessInput(enable);
        _camera.SetProcessInput(enable);
    }

    public Vector2I CalculateMapPosition(Vector2I gridPosition)
    {
        return gridPosition * CellSize + HalfCellSize;
    }

    public Vector2I CalculateGridCoordinates(Vector2I mapPosition)
    {
        return (mapPosition / CellSize);
    }

    public int GridPositionToIndex(Vector2I cell)
    {
        return (GridDimensions.X * cell.Y + cell.X);
    }

    public Vector2I IndexToGridPosition(int index)
    {
        int x = index % GridDimensions.X;
        int y = index / GridDimensions.X;
        return new Vector2I(x, y);
    }

    private void PlacePlanets()
	{
		var starTexture = (Texture2D)GD.Load("res://Assets/UICircle.png");
		Vector2 starTextureScale = new Vector2(0.05f, 0.05f);
		foreach(var kvp in GameDataSingleton.Instance.Sector.Planets)
		{
			Vector2I gridPosition = new(kvp.Value.Position.X, kvp.Value.Position.Y);
			int index = GridPositionToIndex(gridPosition);
			HasPlanet[index] = true;
            Faction controller = kvp.Value.GetControllingFaction();
            var color = controller?.Color ?? System.Drawing.Color.Gray;
            ClickableSprite2D planet = DrawTexture(starTexture, starTextureScale, gridPosition, new Color(color.R, color.G, color.B, color.A));
            planet.Pressed += (object sender, EventArgs e) => PlanetClicked?.Invoke(planet, kvp.Key);
            planet.DoublePressed += (object sender, EventArgs e) => PlanetDoubleClicked?.Invoke(planet, kvp.Key);
		}
	}

	public void RefreshFleets()
	{
		foreach (Node fleetSprite in _fleetSprites)
		{
			RemoveChild(fleetSprite);
			fleetSprite.QueueFree();
		}
		_fleetSprites.Clear();
		PlaceFleets();
	}

	private void PlaceFleets()
	{
		var shipTexture = GD.Load<AtlasTexture>(("res://Assets/shipAtlasTexture.tres"));
		Vector2 shipTextureScale = new Vector2(0.2f, 0.2f);
		foreach(var taskForceKvp in GameDataSingleton.Instance.Sector.Fleets)
		{
            TaskForce taskForce = taskForceKvp.Value;
			if (!IsFleetVisibleOnMap(taskForce)) continue;

            // Determine the position for the fleet's sprite
            Vector2I gridPosition;
            bool isRealspaceTransit = taskForce.TravelPhase == FleetTravelPhase.OutboundSystemTransit
                || taskForce.TravelPhase == FleetTravelPhase.InboundSystemTransit;
            if (taskForce.Planet != null)
            {
                // Fleet is in orbit around a planet

                // Assuming you have a way to get the planet's position
                // You'll need to implement GetPlanetSpritePosition or similar
                gridPosition = new(taskForce.Planet.Position.X, taskForce.Planet.Position.Y);
            }
            else if (isRealspaceTransit && GetTransitAnchorPosition(taskForce) is Coordinate transitPosition)
            {
                gridPosition = new Vector2I(transitPosition.X, transitPosition.Y);
            }
            else
            {
                // Fleet is in space, use its map coordinates
                gridPosition = new Vector2I(taskForce.Position.Value.X, taskForce.Position.Value.Y);
            }

			// Make sure you are the owner of the new node, or it will not save properly
			Vector2I fleetOffset = isRealspaceTransit
				? new Vector2I(1, 1)
				: new Vector2I(1, -1);
			ClickableSprite2D fleet = DrawTexture(shipTexture, shipTextureScale, gridPosition, Color.Color8(255, 255, 255), 2, fleetOffset);
            fleet.Pressed += (object sender, EventArgs e) => FleetClicked?.Invoke(fleet, taskForceKvp.Key);
            fleet.RightPressed += (object sender, EventArgs e) => FleetRightClicked?.Invoke(fleet, taskForceKvp.Key);
			_fleetSprites.Add(fleet);
        }
    }

	private static Coordinate? GetTransitAnchorPosition(TaskForce taskForce)
	{
		return taskForce.TravelPhase switch
		{
			FleetTravelPhase.OutboundSystemTransit => taskForce.Origin?.Position ?? taskForce.Position,
			FleetTravelPhase.InboundSystemTransit => taskForce.Destination?.Position ?? taskForce.Position,
			_ => taskForce.Position
		};
	}

	private static bool IsFleetVisibleOnMap(TaskForce taskForce)
	{
		return taskForce.TravelPhase != FleetTravelPhase.InWarp;
	}

    private void Fleet_Pressed(object sender, EventArgs e)
    {
        throw new NotImplementedException();
    }

    private List<Vector2[]> DetermineSubsectorBoundaryPaths()
    {
        Dictionary<(ushort, ushort), HashSet<EdgeKey>> edgeSetsBySubsectorPair = [];

        for (int y = 0; y < GridDimensions.Y; y++)
        {
            for (int x = 0; x < GridDimensions.X; x++)
            {
                Vector2I cell = new(x, y);
                ushort currentId = SectorIds[GridPositionToIndex(cell)];
                if (currentId == 0) continue;

                AddBoundaryEdgeIfNeeded(edgeSetsBySubsectorPair, cell, Vector2I.Up);
                AddBoundaryEdgeIfNeeded(edgeSetsBySubsectorPair, cell, Vector2I.Right);
                AddBoundaryEdgeIfNeeded(edgeSetsBySubsectorPair, cell, Vector2I.Down);
                AddBoundaryEdgeIfNeeded(edgeSetsBySubsectorPair, cell, Vector2I.Left);
            }
        }

        List<Vector2[]> boundaryPaths = [];
        foreach (var kvp in edgeSetsBySubsectorPair.OrderBy(kvp => kvp.Key.Item1).ThenBy(kvp => kvp.Key.Item2))
        {
            Dictionary<Vector2I, HashSet<Vector2I>> adjacencyMap = BuildBoundaryAdjacencyMap(kvp.Value);
            boundaryPaths.AddRange(ChainBoundaryEdges(adjacencyMap, kvp.Value)
                .Select(BuildSmoothedBoundaryPath)
                .Where(path => path.Length >= 2));
        }

        return boundaryPaths;
    }

    private void AddBoundaryEdgeIfNeeded(
        Dictionary<(ushort, ushort), HashSet<EdgeKey>> edgeSetsBySubsectorPair,
        Vector2I cell,
        Vector2I direction)
    {
        ushort currentId = SectorIds[GridPositionToIndex(cell)];
        Vector2I neighborCell = cell + direction;
        ushort neighborId = IsWithinBounds(neighborCell) ? SectorIds[GridPositionToIndex(neighborCell)] : (ushort)0;
        if (neighborId == currentId) return;

        (Vector2I start, Vector2I end) = GetCellEdgeMapPoints(cell, direction);
        EdgeKey edgeKey = new(start, end);
        (ushort, ushort) subsectorPair = currentId < neighborId
            ? (currentId, neighborId)
            : (neighborId, currentId);

        if (!edgeSetsBySubsectorPair.TryGetValue(subsectorPair, out HashSet<EdgeKey> edgeSet))
        {
            edgeSet = [];
            edgeSetsBySubsectorPair[subsectorPair] = edgeSet;
        }

        edgeSet.Add(edgeKey);
    }

    private static Dictionary<Vector2I, HashSet<Vector2I>> BuildBoundaryAdjacencyMap(HashSet<EdgeKey> edgeSet)
    {
        Dictionary<Vector2I, HashSet<Vector2I>> adjacencyMap = [];
        foreach (EdgeKey edgeKey in edgeSet)
        {
            AddBoundaryAdjacency(adjacencyMap, edgeKey);
        }

        return adjacencyMap;
    }

    private static void AddBoundaryAdjacency(Dictionary<Vector2I, HashSet<Vector2I>> adjacencyMap, EdgeKey edgeKey)
    {
        if (!adjacencyMap.TryGetValue(edgeKey.A, out HashSet<Vector2I> aNeighbors))
        {
            aNeighbors = [];
            adjacencyMap[edgeKey.A] = aNeighbors;
        }
        if (!adjacencyMap.TryGetValue(edgeKey.B, out HashSet<Vector2I> bNeighbors))
        {
            bNeighbors = [];
            adjacencyMap[edgeKey.B] = bNeighbors;
        }

        aNeighbors.Add(edgeKey.B);
        bNeighbors.Add(edgeKey.A);
    }

    private (Vector2I Start, Vector2I End) GetCellEdgeMapPoints(Vector2I cell, Vector2I direction)
    {
        Vector2I cellCenterPosition = CalculateMapPosition(cell);
        Vector2I topLeft = cellCenterPosition - HalfCellSize;
        Vector2I topRight = topLeft + new Vector2I(CellSize.X, 0);
        Vector2I bottomRight = topLeft + CellSize;
        Vector2I bottomLeft = topLeft + new Vector2I(0, CellSize.Y);

        if (direction == Vector2I.Up) return (topLeft, topRight);
        if (direction == Vector2I.Right) return (topRight, bottomRight);
        if (direction == Vector2I.Down) return (bottomLeft, bottomRight);
        return (topLeft, bottomLeft);
    }

    private static List<Vector2[]> ChainBoundaryEdges(
        Dictionary<Vector2I, HashSet<Vector2I>> adjacencyMap,
        HashSet<EdgeKey> edgeSet)
    {
        List<Vector2[]> paths = [];
        HashSet<EdgeKey> visitedEdges = [];

        foreach (EdgeKey edge in edgeSet.OrderBy(edge => edge.A.X).ThenBy(edge => edge.A.Y).ThenBy(edge => edge.B.X).ThenBy(edge => edge.B.Y))
        {
            if (visitedEdges.Contains(edge)) continue;

            Vector2I start = adjacencyMap[edge.A].Count != 2 ? edge.A : edge.B;
            Vector2I current = start;
            Vector2I next = start == edge.A ? edge.B : edge.A;
            List<Vector2I> path = [start];

            while (true)
            {
                EdgeKey currentEdge = new(current, next);
                if (!visitedEdges.Add(currentEdge)) break;

                path.Add(next);
                if (next == start) break;
                if (adjacencyMap[next].Count != 2) break;

                Vector2I previous = current;
                current = next;
                Vector2I? nextCandidate = null;
                foreach (Vector2I candidate in adjacencyMap[current])
                {
                    if (candidate == previous) continue;
                    if (visitedEdges.Contains(new EdgeKey(current, candidate))) continue;

                    nextCandidate = candidate;
                    break;
                }

                if (!nextCandidate.HasValue) break;
                next = nextCandidate.Value;
            }

            if (path.Count >= 2)
            {
                paths.Add(path.Select(point => new Vector2(point.X, point.Y)).ToArray());
            }
        }

        return paths;
    }

    private Dictionary<ushort, List<Vector2I>> DetermineSubsectorBorderPoints(IEnumerable<Subsector> subsectors)
    {
        Dictionary<ushort, List<Vector2I>> subsectorVertexListMap = [];
        foreach(Subsector subsector in subsectors)
        {
            List<Vector2I> vertexList = [];
            subsectorVertexListMap[subsector.Id] = vertexList;
            // the first cell should be the top left of the subsector
            Vector2I gridPosition = subsector.Cells[0];
            Vector2I cellCenterPosition = CalculateMapPosition(gridPosition);
            Vector2I topLeft = cellCenterPosition - HalfCellSize;
            Vector2I topRight = new Vector2I(topLeft.X + CellSize.X, topLeft.Y);
            BorderPoint currentPoint = new BorderPoint
            {
                gridPos = gridPosition,
                mapPoint = topRight,
                orientation = Facing.East
            };
            vertexList.Add(topRight);
            while (currentPoint.mapPoint != topLeft)
            {
                currentPoint = GetNextPoint(currentPoint, subsector.Id);
                vertexList.Add(currentPoint.mapPoint);
            }
        }

        return subsectorVertexListMap;
    }

    private BorderPoint GetNextPoint(BorderPoint currentPoint, ushort subsectorId)
    {
        BorderPoint left, straight, right;
        switch (currentPoint.orientation)
        {
            case Facing.North:
                // at a top-left
                // left is (-1, -1)
                // straight is (0, -1)
                // right is (c, 0)
                left = new BorderPoint
                {
                    gridPos = currentPoint.gridPos + new Vector2I(-1, -1),
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2I.Left,
                    orientation = Facing.West
                };
                straight = new BorderPoint
                {
                    gridPos = currentPoint.gridPos + new Vector2I(0, -1),
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2I.Up,
                    orientation = Facing.North
                };
                right = new BorderPoint
                {
                    gridPos = currentPoint.gridPos,
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2I.Right,
                    orientation = Facing.East
                };
                break;
            case Facing.East:
                // at a top-right
                // left is (1, -1)
                // straight is (1, 0)
                // right is (0, c)
                left = new BorderPoint
                {
                    gridPos = currentPoint.gridPos + new Vector2I(1, -1),
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2I.Up,
                    orientation = Facing.North
                };
                straight = new BorderPoint
                {
                    gridPos = currentPoint.gridPos + new Vector2I(1, 0),
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2I.Right,
                    orientation = Facing.East
                };
                right = new BorderPoint
                {
                    gridPos = currentPoint.gridPos,
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2I.Down,
                    orientation = Facing.South
                };
                break;
            case Facing.South:
                // at a bottom-right
                // left is (1, 1)
                // straight is (0, 1)
                // right is (-c, 0)
                left = new BorderPoint
                {
                    gridPos = currentPoint.gridPos + new Vector2I(1, 1),
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2I.Right,
                    orientation = Facing.East
                };
                straight = new BorderPoint
                {
                    gridPos = currentPoint.gridPos + new Vector2I(0, 1),
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2I.Down,
                    orientation = Facing.South
                };
                right = new BorderPoint
                {
                    gridPos = currentPoint.gridPos,
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2I.Left,
                    orientation = Facing.West
                };
                break;
            default:
            case Facing.West:
                // at a bottom-left
                // left is (-1, 1)
                // straight is (-1, 0)
                // right is (0, -c)
                left = new BorderPoint
                {
                    gridPos = currentPoint.gridPos + new Vector2I(-1, 1),
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2I.Down,
                    orientation = Facing.South
                };
                straight = new BorderPoint
                {
                    gridPos = currentPoint.gridPos + new Vector2I(-1, 0),
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2I.Left,
                    orientation = Facing.West
                };
                right = new BorderPoint
                {
                    gridPos = currentPoint.gridPos,
                    mapPoint = currentPoint.mapPoint + CellSize * Vector2I.Up,
                    orientation = Facing.North
                };
                break;
        }
        // if left is in bounds and part of this sector, it's next
        if (IsWithinBounds(left.gridPos) && SectorIds[GridPositionToIndex(left.gridPos)] == subsectorId)
        {
            return left;
        }
        else if (IsWithinBounds(straight.gridPos) && SectorIds[GridPositionToIndex(straight.gridPos)] == subsectorId)
        {
            return straight;
        }
        else
        {
            return right;
        }
    }

    private bool IsWithinBounds(Vector2I cellCoordinates)
    {
        return (cellCoordinates.X >= 0 && cellCoordinates.X < GridDimensions.X && cellCoordinates.Y >= 0 && cellCoordinates.Y < GridDimensions.Y);
    }

    private ClickableSprite2D DrawTexture(Texture2D texture, Vector2 scale, Vector2I gridPosition, Color color, int zIndex = 1, bool offset=false)
	{
		Vector2I spriteOffset = offset ? new Vector2I(1, -1) : Vector2I.Zero;
		return DrawTexture(texture, scale, gridPosition, color, zIndex, spriteOffset);
	}

    private ClickableSprite2D DrawTexture(Texture2D texture, Vector2 scale, Vector2I gridPosition, Color color, int zIndex, Vector2I offset)
	{
		ClickableSprite2D newSprite = new ClickableSprite2D();
		this.AddChild(newSprite);
        newSprite.Owner = this;
		Vector2I mapPosition = CalculateMapPosition(gridPosition);
		if(offset != Vector2I.Zero)
		{
			mapPosition += HalfCellSize * offset;
		}
        newSprite.GlobalPosition = mapPosition;
        newSprite.Texture = texture;
        newSprite.Modulate = color;
        newSprite.Scale = scale;
		newSprite.ZIndex = zIndex;
        return newSprite;
	}

}
