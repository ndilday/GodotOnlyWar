using Godot;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Scenes.MainGameScreen;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class BattleReviewController : DialogController
{
    private static readonly Color PlayerMarkerColor = OnlyWarStyle.PlayerAccent;
    private static readonly Color OpposingMarkerColor = OnlyWarStyle.OpposingAccent;
    private static readonly Color SelectedMarkerColor = OnlyWarStyle.Gold;
    private static readonly Color GridColor = OnlyWarStyle.MapGrid;
    private static readonly Color BackgroundColor = OnlyWarStyle.MapBackground;

    private readonly BattleReplaySummaryBuilder _summaryBuilder = new();
    private BattleReviewView _view;
    private BattleHistory _history;
    private Texture2D _markerTexture;
    private Vector2 _markerScale;
    private Vector2I _pixelsPerGrid = new(28, 28);
    private int _currentTurnIndex;
    private int? _selectedFormationId;

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<BattleReviewView>("DialogView");
        _view.PreviousRoundPressed += (_, _) => DisplayTurn(0);
        _view.StepBackPressed += (_, _) => DisplayTurn(_currentTurnIndex - 1);
        _view.PlayPausePressed += (_, _) => DisplayTurn(_currentTurnIndex + 1);
        _view.StepForwardPressed += (_, _) => DisplayTurn(_currentTurnIndex + 1);
        _view.NextRoundPressed += (_, _) => DisplayTurn((_history?.Turns.Count ?? 1) - 1);
        _view.FormationSelected += (_, formationId) =>
        {
            _selectedFormationId = formationId;
            DisplayTurn(_currentTurnIndex);
        };
        _view.TimelineTurnSelected += (_, turnIndex) => DisplayTurn(turnIndex);

        if (GameDataSingleton.Instance?.IsInitialized == true)
        {
            _pixelsPerGrid = new(
                GameDataSingleton.Instance.GameRulesData.BattleCellSize.X,
                GameDataSingleton.Instance.GameRulesData.BattleCellSize.Y);
        }

        _markerTexture = GD.Load<Texture2D>("res://Assets/UICircle.png");
        if (_markerTexture != null)
        {
            Vector2 targetSize = new(_pixelsPerGrid.X * 0.62f, _pixelsPerGrid.Y * 0.62f);
            _markerScale = targetSize / _markerTexture.GetSize();
        }
    }

    public void LoadNewHistory(BattleHistory history)
    {
        _history = history;
        _selectedFormationId = null;
        DisplayTurn(0);
    }

    private void DisplayTurn(int requestedTurnIndex)
    {
        if (_history == null || _history.Turns.Count == 0) return;

        _currentTurnIndex = Math.Clamp(requestedTurnIndex, 0, _history.Turns.Count - 1);
        BattleReplayDisplay display = _summaryBuilder.Build(_history, _currentTurnIndex, _selectedFormationId);
        _selectedFormationId = display.SelectedFormationId;
        _view.SetDisplay(display);
        DrawBattlefield(_history.Turns[_currentTurnIndex].State, display.SelectedFormationId);
    }

    private void DrawBattlefield(BattleState state, int? selectedFormationId)
    {
        ClearMap();

        IReadOnlyList<Tuple<int, int>> allPositions = state.SoldierPositionsMap.Values.SelectMany(positions => positions).ToList();
        if (allPositions.Count == 0) return;

        Vector2I topLeft = GetTopLeftOfPositions(allPositions) - Vector2I.One;
        Vector2I bottomRight = GetBottomRightOfPositions(allPositions) + Vector2I.One;
        Vector2 mapSize = new(
            Math.Max(1, bottomRight.X - topLeft.X + 1) * _pixelsPerGrid.X,
            Math.Max(1, bottomRight.Y - topLeft.Y + 1) * _pixelsPerGrid.Y);

        DrawBackground(mapSize);
        DrawGrid(mapSize);

        foreach (BattleSquad squad in state.PlayerSquads.Values.OrderBy(squad => squad.Id))
        {
            DrawSquad(squad, topLeft, selectedFormationId == squad.Id);
        }
        foreach (BattleSquad squad in state.OpposingSquads.Values.OrderBy(squad => squad.Id))
        {
            DrawSquad(squad, topLeft, selectedFormationId == squad.Id);
        }

        CenterCamera(mapSize);
    }

    private void DrawBackground(Vector2 mapSize)
    {
        ColorRect background = new()
        {
            Color = BackgroundColor,
            Size = mapSize,
            Position = Vector2.Zero,
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

    private void DrawSquad(BattleSquad squad, Vector2I topLeftOffset, bool selected)
    {
        List<Vector2> markerPositions = [];
        foreach (BattleSoldier soldier in squad.AbleSoldiers)
        {
            foreach (Tuple<int, int> location in soldier.PositionList)
            {
                Vector2 position = GridToMapPosition(location, topLeftOffset);
                markerPositions.Add(position);
                DrawMarker(position, squad.IsPlayerSquad, selected, squad.Id);
            }
        }

        if (markerPositions.Count == 0) return;

        Vector2 centroid = markerPositions.Aggregate(Vector2.Zero, (sum, position) => sum + position) / markerPositions.Count;
        DrawFormationLabel(squad, centroid, selected);
    }

    private void DrawMarker(Vector2 position, bool isPlayerForce, bool selected, int formationId)
    {
        ClickableSprite2D sprite = new()
        {
            Texture = _markerTexture,
            Position = position,
            Scale = selected ? _markerScale * 1.28f : _markerScale,
            Modulate = selected ? SelectedMarkerColor : isPlayerForce ? PlayerMarkerColor : OpposingMarkerColor,
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

    private void DrawFormationLabel(BattleSquad squad, Vector2 centroid, bool selected)
    {
        Label label = new()
        {
            Text = $"{squad.Name}  {squad.AbleSoldiers.Count}",
            Position = centroid + new Vector2(10, -28),
            ZIndex = selected ? 6 : 5,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeColorOverride("font_color", selected ? SelectedMarkerColor : squad.IsPlayerSquad ? PlayerMarkerColor : OpposingMarkerColor);
        label.AddThemeFontSizeOverride("font_size", selected ? 14 : 12);
        _view.MapRoot.AddChild(label);
    }

    private Vector2 GridToMapPosition(Tuple<int, int> gridPosition, Vector2I topLeftOffset)
    {
        Vector2I adjustedPosition = new(gridPosition.Item1 - topLeftOffset.X, gridPosition.Item2 - topLeftOffset.Y);
        return new Vector2(
            adjustedPosition.X * _pixelsPerGrid.X + _pixelsPerGrid.X / 2.0f,
            adjustedPosition.Y * _pixelsPerGrid.Y + _pixelsPerGrid.Y / 2.0f);
    }

    private void CenterCamera(Vector2 mapSize)
    {
        _view.ReplayCamera.Position = mapSize / 2.0f;
        float zoom = Math.Clamp(Math.Min(900.0f / Math.Max(mapSize.X, 1), 560.0f / Math.Max(mapSize.Y, 1)), 0.45f, 2.0f);
        _view.ReplayCamera.Zoom = new Vector2(zoom, zoom);
    }

    private void ClearMap()
    {
        foreach (Node child in _view.MapRoot.GetChildren())
        {
            _view.MapRoot.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static Vector2I GetTopLeftOfPositions(IReadOnlyList<Tuple<int, int>> positions)
    {
        return new Vector2I(
            positions.Min(position => position.Item1),
            positions.Min(position => position.Item2));
    }

    private static Vector2I GetBottomRightOfPositions(IReadOnlyList<Tuple<int, int>> positions)
    {
        return new Vector2I(
            positions.Max(position => position.Item1),
            positions.Max(position => position.Item2));
    }
}
