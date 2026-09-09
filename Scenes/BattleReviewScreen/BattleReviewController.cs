using Godot;
using OnlyWar.Application;
using OnlyWar.Scenes.MainGameScreen;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

public partial class BattleReviewController : DialogController
{
    private static readonly Color PlayerMarkerColor = OnlyWarStyle.PlayerAccent;
    private static readonly Color OpposingMarkerColor = OnlyWarStyle.OpposingAccent;
    private static readonly Color SelectedMarkerColor = OnlyWarStyle.Gold;
    private static readonly Color GridColor = OnlyWarStyle.MapGrid;
    private static readonly Color BackgroundColor = OnlyWarStyle.MapBackground;
    private static readonly Color CasualtyColor = OnlyWarStyle.Critical;
    private static readonly Color ProjectileColor = OnlyWarStyle.Gold;
    private static readonly Color ChargeColor = new(0.58f, 0.9f, 0.68f, 0.95f);
    private static readonly Color RoutColor = OnlyWarStyle.MedicalWarning;
    private static readonly Color DepartureColor = OnlyWarStyle.MedicalStable;
    private const double SecondsPerRoundAtNormalSpeed = 1.0;

    private readonly float[] _playbackSpeeds = [0.5f, 1.0f, 1.5f, 2.0f];
    private BattleReviewView _view;
    private IMainScreenApplication _application;
    private BattleReplayDisplay _display;
    private Guid _replayId;
    private Texture2D _markerTexture;
    private Vector2 _markerScale;
    private Vector2I _pixelsPerGrid = new(28, 28);
    private int _currentTurnIndex;
    private int? _selectedFormationId;
    private int _playbackSpeedIndex = 1;
    private bool _isPlaying;
    private double _playbackElapsed;
    private Vector2I _mapOffset;
    private Vector2 _mapSize = Vector2.One;
    private bool _cameraFramePending;

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<BattleReviewView>("DialogView");
        _view.PreviousRoundPressed += (_, _) =>
        {
            StopPlayback();
            DisplayTurn(0);
        };
        _view.StepBackPressed += (_, _) =>
        {
            StopPlayback();
            DisplayTurn(_currentTurnIndex - 1);
        };
        _view.PlayPausePressed += (_, _) => TogglePlayback();
        _view.StepForwardPressed += (_, _) =>
        {
            StopPlayback();
            DisplayTurn(_currentTurnIndex + 1);
        };
        _view.NextRoundPressed += (_, _) =>
        {
            StopPlayback();
            DisplayTurn((_display?.Timeline.Count ?? 1) - 1);
        };
        _view.SpeedPressed += (_, _) => CyclePlaybackSpeed();
        _view.FormationSelected += (_, formationId) =>
        {
            _selectedFormationId = formationId;
            DisplayTurn(_currentTurnIndex);
        };
        _view.ReplayPressed += (_, mapPosition) => SelectFormationAt(mapPosition);

        _pixelsPerGrid = new(
            PresentationMetrics.BattleGridCellWidth,
            PresentationMetrics.BattleGridCellHeight);

        _markerTexture = GD.Load<Texture2D>("res://Assets/UICircle.png");
        if (_markerTexture != null)
        {
            Vector2 targetSize = new(_pixelsPerGrid.X * 0.62f, _pixelsPerGrid.Y * 0.62f);
            _markerScale = targetSize / _markerTexture.GetSize();
        }
    }

    public void Configure(IMainScreenApplication application) =>
        _application = application ?? throw new ArgumentNullException(nameof(application));

    public override void _Process(double delta)
    {
        base._Process(delta);

        if (_cameraFramePending)
        {
            FrameInitialDeployment();
            _cameraFramePending = false;
        }

        if (!_isPlaying || _display == null || _display.Timeline.Count == 0)
        {
            return;
        }

        if (_currentTurnIndex >= _display.Timeline.Count - 1)
        {
            StopPlayback();
            return;
        }

        _playbackElapsed += delta * _playbackSpeeds[_playbackSpeedIndex];
        if (_playbackElapsed < SecondsPerRoundAtNormalSpeed)
        {
            return;
        }

        _playbackElapsed = 0;
        DisplayTurn(_currentTurnIndex + 1);
    }

    public void LoadNewReplay(Guid replayId)
    {
        StopPlayback();
        _replayId = replayId;
        _display = null;
        _selectedFormationId = null;
        _cameraFramePending = true;
        DisplayTurn(0);
    }

    private void DisplayTurn(int requestedTurnIndex)
    {
        if (_application == null || _replayId == Guid.Empty)
        {
            return;
        }

        BattleReplayDisplay display = _application.QueryBattleReplay(
            new BattleReplayQuery(_replayId, requestedTurnIndex, _selectedFormationId));
        if (display == null || display.Timeline.Count == 0)
        {
            return;
        }

        _display = display;
        _currentTurnIndex = display.CurrentTurnIndex;
        _selectedFormationId = display.SelectedFormationId;
        ComputeMapBounds(display.MapGeometry);
        _view.SetDisplay(display);
        _view.SetPlaybackButtons(
            _currentTurnIndex > 0,
            _currentTurnIndex < display.Timeline.Count - 1,
            _isPlaying,
            GetSpeedLabel(),
            display.Timeline.Count > 1);
        DrawBattlefield(display);

        if (_currentTurnIndex >= display.Timeline.Count - 1 && _isPlaying)
        {
            StopPlayback();
        }
    }

    private void TogglePlayback()
    {
        if (_display == null || _display.Timeline.Count == 0)
        {
            return;
        }

        if (_isPlaying)
        {
            StopPlayback();
            return;
        }

        if (_currentTurnIndex >= _display.Timeline.Count - 1)
        {
            DisplayTurn(0);
        }

        _isPlaying = _currentTurnIndex < _display.Timeline.Count - 1;
        _playbackElapsed = 0;
        RefreshPlaybackButtons();
    }

    private void StopPlayback()
    {
        if (!_isPlaying && _playbackElapsed == 0)
        {
            return;
        }

        _isPlaying = false;
        _playbackElapsed = 0;
        RefreshPlaybackButtons();
    }

    private void CyclePlaybackSpeed()
    {
        _playbackSpeedIndex = (_playbackSpeedIndex + 1) % _playbackSpeeds.Length;
        RefreshPlaybackButtons();
    }

    private void RefreshPlaybackButtons()
    {
        if (_view == null || _display == null || _display.Timeline.Count == 0)
        {
            return;
        }

        _view.SetPlaybackButtons(
            _currentTurnIndex > 0,
            _currentTurnIndex < _display.Timeline.Count - 1,
            _isPlaying,
            GetSpeedLabel(),
            _display.Timeline.Count > 1);
    }

    private string GetSpeedLabel() =>
        $"{_playbackSpeeds[_playbackSpeedIndex].ToString("0.##", CultureInfo.InvariantCulture)}x";

    private void ComputeMapBounds(BattleReplayMapGeometry geometry)
    {
        IReadOnlyList<BattleReplayMapPoint> allPositions = geometry?.StableBounds
            ?? Array.Empty<BattleReplayMapPoint>();
        if (allPositions.Count == 0)
        {
            _mapOffset = Vector2I.Zero;
            _mapSize = new Vector2(_pixelsPerGrid.X, _pixelsPerGrid.Y);
            return;
        }

        Vector2I topLeft = GetTopLeftOfPositions(allPositions) - Vector2I.One;
        Vector2I bottomRight = GetBottomRightOfPositions(allPositions) + Vector2I.One;
        _mapOffset = topLeft;
        _mapSize = new Vector2(
            Math.Max(1, bottomRight.X - topLeft.X + 1) * _pixelsPerGrid.X,
            Math.Max(1, bottomRight.Y - topLeft.Y + 1) * _pixelsPerGrid.Y);
    }

    private void DrawBattlefield(BattleReplayDisplay display)
    {
        ClearMap();
        DrawBackground(_mapSize);
        DrawGrid(_mapSize);

        foreach (BattleReplayMapSoldier casualty in display.MapFrame.Casualties)
        {
            Vector2 position = MapPointToPosition(
                new BattleReplayMapPoint(casualty.CenterX, casualty.CenterY));
            float radius = Math.Min(_pixelsPerGrid.X, _pixelsPerGrid.Y) * 0.34f;
            DrawLine(position + new Vector2(-radius, -radius),
                position + new Vector2(radius, radius), CasualtyColor, 2.2f, 9);
            DrawLine(position + new Vector2(-radius, radius),
                position + new Vector2(radius, -radius), CasualtyColor, 2.2f, 9);
            DrawCalloutLabel("CAS", position + new Vector2(8, 8), CasualtyColor, 10, 10);
        }

        foreach (BattleReplayMapTransition transition in display.MapFrame.Transitions)
        {
            if (transition.Kind == BattleReplayMapTransitionKind.Casualty) continue;
            Vector2 centroid = MapPointToPosition(transition.Center);
            Color color = transition.Kind == BattleReplayMapTransitionKind.Departure
                ? DepartureColor
                : RoutColor;
            string label = transition.Kind == BattleReplayMapTransitionKind.Departure
                ? "DEPART"
                : "ROUT";
            DrawCalloutLabel(label, centroid + new Vector2(-22, -42), color, 13, 12);
            DrawLine(centroid + new Vector2(-20, 14),
                centroid + new Vector2(18, 34), color, 2.0f, 8);
            DrawLine(centroid + new Vector2(-6, 18),
                centroid + new Vector2(32, 38), color, 2.0f, 8);
        }

        foreach (BattleReplayMapAction action in display.MapFrame.Actions)
        {
            Vector2 from = MapPointToPosition(action.From);
            Vector2 to = MapPointToPosition(action.To);
            if (action.Kind == BattleReplayMapActionKind.Ranged)
            {
                DrawDashedLine(from, to, ProjectileColor, 1.15f, 11);
                DrawCalloutLabel(action.Label, (from + to) / 2.0f + new Vector2(4, -18),
                    ProjectileColor, 11, 12);
            }
            else
            {
                DrawArrowLine(from, to, ChargeColor, 2.0f, 10);
                DrawCalloutLabel(action.Label, (from + to) / 2.0f + new Vector2(4, -18),
                    ChargeColor, 11, 11);
            }
        }

        foreach (BattleReplayMapFormation formation in display.MapFrame.Formations)
        {
            DrawFormation(formation, display.SelectedFormationId == formation.FormationId);
        }
    }

    private void FrameInitialDeployment()
    {
        if (_display == null)
        {
            return;
        }

        FrameParticipants(_display.MapGeometry.InitialDeployment);
    }

    private void DrawBackground(Vector2 mapSize)
    {
        ColorRect background = new()
        {
            Color = BackgroundColor,
            Size = mapSize + new Vector2(4000, 4000),
            Position = new Vector2(-2000, -2000),
            ZIndex = -10
        };
        _view.MapRoot.AddChild(background);
    }

    private void DrawGrid(Vector2 mapSize)
    {
        for (int x = 0; x <= mapSize.X; x += _pixelsPerGrid.X)
        {
            DrawLine(new Vector2(x, 0), new Vector2(x, mapSize.Y), GridColor, 1.0f, -8);
        }
        for (int y = 0; y <= mapSize.Y; y += _pixelsPerGrid.Y)
        {
            DrawLine(new Vector2(0, y), new Vector2(mapSize.X, y), GridColor, 1.0f, -8);
        }
    }

    private void DrawLine(Vector2 start, Vector2 end, Color color, float width, int zIndex)
    {
        Line2D line = new()
        {
            DefaultColor = color,
            Width = width,
            ZIndex = zIndex
        };
        line.AddPoint(start);
        line.AddPoint(end);
        _view.MapRoot.AddChild(line);
    }

    private void DrawFormation(BattleReplayMapFormation formation, bool selected)
    {
        List<Vector2> markerPositions = [];
        foreach (BattleReplayMapSoldier soldier in formation.Soldiers)
        {
            Vector2 position = MapPointToPosition(
                new BattleReplayMapPoint(soldier.CenterX, soldier.CenterY));
            markerPositions.Add(position);
            DrawMarker(position, formation.IsPlayerForce, selected, formation.FormationId);
        }

        if (markerPositions.Count == 0) return;
        Vector2 centroid = markerPositions.Aggregate(
            Vector2.Zero,
            (sum, position) => sum + position) / markerPositions.Count;
        DrawFormationLabel(formation, centroid, selected);
    }

    private void SelectFormationAt(Vector2 mapPosition)
    {
        if (_display == null || _display.MapFrame.Formations.Count == 0)
        {
            return;
        }

        float hitRadius = Math.Max(
            Math.Min(_pixelsPerGrid.X, _pixelsPerGrid.Y) * 0.45f,
            8.0f / Math.Max(_view.ReplayCamera.Zoom.X, 0.01f));
        float hitRadiusSquared = hitRadius * hitRadius;
        BattleReplayMapFormation closestFormation = _display.MapFrame.Formations
            .SelectMany(formation => formation.Soldiers.Select(soldier => new
            {
                Formation = formation,
                DistanceSquared = mapPosition.DistanceSquaredTo(MapPointToPosition(
                    new BattleReplayMapPoint(soldier.CenterX, soldier.CenterY)))
            }))
            .Where(candidate => candidate.DistanceSquared <= hitRadiusSquared)
            .OrderBy(candidate => candidate.DistanceSquared)
            .Select(candidate => candidate.Formation)
            .FirstOrDefault();

        if (closestFormation == null) return;
        _selectedFormationId = closestFormation.FormationId;
        DisplayTurn(_currentTurnIndex);
    }

    private void DrawMarker(Vector2 position, bool isPlayerForce, bool selected, int formationId)
    {
        ClickableSprite2D sprite = new()
        {
            Texture = _markerTexture,
            Position = position,
            Scale = selected ? _markerScale * 1.28f : _markerScale,
            Modulate = selected
                ? SelectedMarkerColor
                : isPlayerForce ? PlayerMarkerColor : OpposingMarkerColor,
            ZIndex = selected ? 4 : 2
        };
        sprite.Pressed += (_, _) =>
        {
            _selectedFormationId = formationId;
            DisplayTurn(_currentTurnIndex);
        };
        _view.MapRoot.AddChild(sprite);

        if (!selected) return;

        Line2D ring = new()
        {
            DefaultColor = SelectedMarkerColor,
            Width = 2.0f,
            ZIndex = 3
        };
        float radius = Math.Min(_pixelsPerGrid.X, _pixelsPerGrid.Y) * 0.48f;
        for (int i = 0; i <= 24; i++)
        {
            float angle = Mathf.Tau * i / 24.0f;
            ring.AddPoint(position + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
        }
        _view.MapRoot.AddChild(ring);
    }

    private void DrawFormationLabel(
        BattleReplayMapFormation formation,
        Vector2 centroid,
        bool selected)
    {
        Label label = new()
        {
            Text = $"{formation.Name}  {formation.Soldiers.Count}",
            Position = centroid + new Vector2(10, -28),
            ZIndex = selected ? 6 : 5,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeColorOverride(
            "font_color",
            selected
                ? SelectedMarkerColor
                : formation.IsPlayerForce ? PlayerMarkerColor : OpposingMarkerColor);
        label.AddThemeFontSizeOverride("font_size", selected ? 14 : 12);
        _view.MapRoot.AddChild(label);
    }

    private void DrawDashedLine(Vector2 start, Vector2 end, Color color, float width, int zIndex)
    {
        Vector2 delta = end - start;
        float length = delta.Length();
        if (length <= 1.0f) return;
        Vector2 direction = delta / length;
        const float dashLength = 9.0f;
        const float gapLength = 7.0f;
        for (float offset = 0; offset < length; offset += dashLength + gapLength)
        {
            DrawLine(
                start + direction * offset,
                start + direction * Math.Min(offset + dashLength, length),
                color,
                width,
                zIndex);
        }
    }

    private void DrawArrowLine(Vector2 start, Vector2 end, Color color, float width, int zIndex)
    {
        DrawLine(start, end, color, width, zIndex);
        Vector2 direction = end - start;
        if (direction.LengthSquared() <= 1.0f) return;

        direction = direction.Normalized();
        Vector2 perpendicular = new(-direction.Y, direction.X);
        float arrowLength = Math.Min(_pixelsPerGrid.X, _pixelsPerGrid.Y) * 0.32f;
        Polygon2D arrowHead = new()
        {
            Color = color,
            Polygon =
            [
                end,
                end - direction * arrowLength + perpendicular * arrowLength * 0.55f,
                end - direction * arrowLength - perpendicular * arrowLength * 0.55f
            ],
            ZIndex = zIndex + 1
        };
        _view.MapRoot.AddChild(arrowHead);
    }

    private void DrawCalloutLabel(string text, Vector2 position, Color color, int fontSize, int zIndex)
    {
        Label label = new()
        {
            Text = text,
            Position = position,
            ZIndex = zIndex,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_shadow_color", BackgroundColor);
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        _view.MapRoot.AddChild(label);
    }

    private Vector2 MapPointToPosition(BattleReplayMapPoint point)
    {
        Vector2 adjustedPosition = new(point.X - _mapOffset.X, point.Y - _mapOffset.Y);
        return new Vector2(
            adjustedPosition.X * _pixelsPerGrid.X + _pixelsPerGrid.X / 2.0f,
            adjustedPosition.Y * _pixelsPerGrid.Y + _pixelsPerGrid.Y / 2.0f);
    }

    private void FrameParticipants(IReadOnlyList<BattleReplayMapPoint> positions)
    {
        Vector2 contentMin;
        Vector2 contentMax;
        Vector2 halfCell = new Vector2(_pixelsPerGrid.X, _pixelsPerGrid.Y) / 2.0f;
        if (positions == null || positions.Count == 0)
        {
            contentMin = Vector2.Zero;
            contentMax = _mapSize;
        }
        else
        {
            Vector2I topLeft = GetTopLeftOfPositions(positions);
            Vector2I bottomRight = GetBottomRightOfPositions(positions);
            contentMin = MapPointToPosition(new BattleReplayMapPoint(topLeft.X, topLeft.Y)) - halfCell;
            contentMax = MapPointToPosition(new BattleReplayMapPoint(bottomRight.X, bottomRight.Y)) + halfCell;
        }

        Vector2 contentSize = contentMax - contentMin;
        Vector2 contentCenter = (contentMin + contentMax) / 2.0f;
        Vector2 viewportSize = _view.ReplayCamera.GetViewportRect().Size;
        if (viewportSize.X <= 1.0f || viewportSize.Y <= 1.0f)
        {
            viewportSize = new Vector2(900.0f, 560.0f);
        }

        const float framingPadding = 0.92f;
        float zoom = Math.Clamp(
            Math.Min(
                viewportSize.X / Math.Max(contentSize.X, 1.0f),
                viewportSize.Y / Math.Max(contentSize.Y, 1.0f)) * framingPadding,
            0.05f,
            3.0f);
        _view.ReplayCamera.Zoom = new Vector2(zoom, zoom);
        _view.ReplayCamera.Position = contentCenter - viewportSize / (2.0f * zoom);
    }

    private void ClearMap()
    {
        foreach (Node child in _view.MapRoot.GetChildren())
        {
            _view.MapRoot.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static Vector2I GetTopLeftOfPositions(IReadOnlyList<BattleReplayMapPoint> positions) =>
        new(
            (int)Math.Floor(positions.Min(position => position.X)),
            (int)Math.Floor(positions.Min(position => position.Y)));

    private static Vector2I GetBottomRightOfPositions(IReadOnlyList<BattleReplayMapPoint> positions) =>
        new(
            (int)Math.Ceiling(positions.Max(position => position.X)),
            (int)Math.Ceiling(positions.Max(position => position.Y)));
}
