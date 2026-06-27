using Godot;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
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
    private const double SecondsPerRoundAtNormalSpeed = 1.0;

    private readonly BattleReplaySummaryBuilder _summaryBuilder = new();
    private readonly float[] _playbackSpeeds = [0.5f, 1.0f, 1.5f, 2.0f];
    private BattleReviewView _view;
    private BattleHistory _history;
    private Texture2D _markerTexture;
    private Vector2 _markerScale;
    private Vector2I _pixelsPerGrid = new(28, 28);
    private int _currentTurnIndex;
    private int? _selectedFormationId;
    private int _playbackSpeedIndex = 1;
    private bool _isPlaying;
    private double _playbackElapsed;

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
            DisplayTurn((_history?.Turns.Count ?? 1) - 1);
        };
        _view.SpeedPressed += (_, _) => CyclePlaybackSpeed();
        _view.FormationSelected += (_, formationId) =>
        {
            _selectedFormationId = formationId;
            DisplayTurn(_currentTurnIndex);
        };
        _view.TimelineTurnSelected += (_, turnIndex) =>
        {
            StopPlayback();
            DisplayTurn(turnIndex);
        };

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

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (!_isPlaying || _history == null || _history.Turns.Count == 0)
        {
            return;
        }

        if (_currentTurnIndex >= _history.Turns.Count - 1)
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

    public void LoadNewHistory(BattleHistory history)
    {
        StopPlayback();
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
        _view.SetPlaybackButtons(
            _currentTurnIndex > 0,
            _currentTurnIndex < _history.Turns.Count - 1,
            _isPlaying,
            GetSpeedLabel(),
            _history.Turns.Count > 1);
        DrawBattlefield(_currentTurnIndex, display.SelectedFormationId);

        if (_currentTurnIndex >= _history.Turns.Count - 1 && _isPlaying)
        {
            StopPlayback();
        }
    }

    private void TogglePlayback()
    {
        if (_history == null || _history.Turns.Count == 0)
        {
            return;
        }

        if (_isPlaying)
        {
            StopPlayback();
            return;
        }

        if (_currentTurnIndex >= _history.Turns.Count - 1)
        {
            DisplayTurn(0);
        }

        _isPlaying = _currentTurnIndex < _history.Turns.Count - 1;
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
        if (_view == null || _history == null || _history.Turns.Count == 0)
        {
            return;
        }

        _view.SetPlaybackButtons(
            _currentTurnIndex > 0,
            _currentTurnIndex < _history.Turns.Count - 1,
            _isPlaying,
            GetSpeedLabel(),
            _history.Turns.Count > 1);
    }

    private string GetSpeedLabel()
    {
        return $"{_playbackSpeeds[_playbackSpeedIndex].ToString("0.##", CultureInfo.InvariantCulture)}x";
    }

    private void DrawBattlefield(int turnIndex, int? selectedFormationId)
    {
        ClearMap();

        BattleTurn currentTurn = _history.Turns[turnIndex];
        BattleState state = currentTurn.State;
        BattleState previousState = turnIndex > 0 ? _history.Turns[turnIndex - 1].State : null;
        IReadOnlyList<Tuple<int, int>> allPositions = GetAllReplayPositions(state, previousState);
        if (allPositions.Count == 0) return;

        Vector2I topLeft = GetTopLeftOfPositions(allPositions) - Vector2I.One;
        Vector2I bottomRight = GetBottomRightOfPositions(allPositions) + Vector2I.One;
        Vector2 mapSize = new(
            Math.Max(1, bottomRight.X - topLeft.X + 1) * _pixelsPerGrid.X,
            Math.Max(1, bottomRight.Y - topLeft.Y + 1) * _pixelsPerGrid.Y);

        DrawBackground(mapSize);
        DrawGrid(mapSize);
        DrawRoundOverlays(previousState, state, currentTurn, topLeft);

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

    private static IReadOnlyList<Tuple<int, int>> GetAllReplayPositions(BattleState currentState, BattleState previousState)
    {
        List<Tuple<int, int>> positions = currentState.SoldierPositionsMap.Values.SelectMany(value => value).ToList();
        if (previousState != null)
        {
            positions.AddRange(previousState.SoldierPositionsMap.Values.SelectMany(value => value));
        }

        return positions;
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
        DrawFormationBanner(squad, centroid, selected);
        DrawFormationLabel(squad, centroid, selected);
    }

    private void DrawFormationBanner(BattleSquad squad, Vector2 centroid, bool selected)
    {
        Color color = selected ? SelectedMarkerColor : squad.IsPlayerSquad ? PlayerMarkerColor : OpposingMarkerColor;
        Vector2 mastBase = centroid + new Vector2(-18, -16);
        Vector2 mastTop = mastBase + new Vector2(0, -26);
        DrawLine(mastBase, mastTop, color, selected ? 2.5f : 2.0f, 6);

        Polygon2D flag = new()
        {
            Color = color,
            Polygon =
            [
                mastTop,
                mastTop + new Vector2(38, 5),
                mastTop + new Vector2(38, 19),
                mastTop + new Vector2(0, 14)
            ],
            ZIndex = selected ? 8 : 7
        };
        _view.MapRoot.AddChild(flag);
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

    private void DrawRoundOverlays(BattleState previousState, BattleState currentState, BattleTurn currentTurn, Vector2I topLeftOffset)
    {
        if (previousState == null)
        {
            return;
        }

        DrawCasualtyMarkers(previousState, currentState, topLeftOffset);
        DrawRoutMarkers(previousState, currentState, topLeftOffset);
        DrawActionCallouts(previousState, currentState, currentTurn, topLeftOffset);
    }

    private void DrawCasualtyMarkers(BattleState previousState, BattleState currentState, Vector2I topLeftOffset)
    {
        foreach (BattleSoldier soldier in previousState.Soldiers.Values.OrderBy(soldier => soldier.Soldier.Id))
        {
            if (currentState.Soldiers.ContainsKey(soldier.Soldier.Id))
            {
                continue;
            }

            Vector2 position = GetSoldierMapPosition(soldier, topLeftOffset);
            float radius = Math.Min(_pixelsPerGrid.X, _pixelsPerGrid.Y) * 0.34f;
            DrawLine(position + new Vector2(-radius, -radius), position + new Vector2(radius, radius), CasualtyColor, 2.2f, 9);
            DrawLine(position + new Vector2(-radius, radius), position + new Vector2(radius, -radius), CasualtyColor, 2.2f, 9);
            DrawCalloutLabel("CAS", position + new Vector2(8, 8), CasualtyColor, 10, 10);
        }
    }

    private void DrawRoutMarkers(BattleState previousState, BattleState currentState, Vector2I topLeftOffset)
    {
        foreach (BattleSquad previousSquad in previousState.PlayerSquads.Values.Concat(previousState.OpposingSquads.Values).OrderBy(squad => squad.Id))
        {
            if (previousSquad.AbleSoldiers.Count == 0)
            {
                continue;
            }

            BattleSquad currentSquad = TryGetSquad(currentState, previousSquad.Id);
            if (currentSquad?.AbleSoldiers.Count > 0)
            {
                continue;
            }

            Vector2 centroid = GetSquadCentroid(previousSquad, topLeftOffset);
            DrawCalloutLabel("ROUT", centroid + new Vector2(-22, -42), RoutColor, 13, 12);
            DrawLine(centroid + new Vector2(-20, 14), centroid + new Vector2(18, 34), RoutColor, 2.0f, 8);
            DrawLine(centroid + new Vector2(-6, 18), centroid + new Vector2(32, 38), RoutColor, 2.0f, 8);
        }
    }

    private void DrawActionCallouts(BattleState previousState, BattleState currentState, BattleTurn currentTurn, Vector2I topLeftOffset)
    {
        int drawn = 0;
        foreach (IAction action in currentTurn.Actions)
        {
            if (drawn >= 16)
            {
                return;
            }

            if (action is ShootAction shootAction && TryGetSoldierMapPosition(shootAction.ShooterId, currentState, previousState, topLeftOffset, out Vector2 shooterPosition)
                && TryGetSoldierMapPosition(shootAction.TargetId, currentState, previousState, topLeftOffset, out Vector2 targetPosition))
            {
                DrawArrowLine(shooterPosition, targetPosition, ProjectileColor, 2.4f, 11);
                DrawCalloutLabel($"{Math.Max(1, shootAction.NumberOfShots)} SHOTS", (shooterPosition + targetPosition) / 2.0f + new Vector2(4, -18), ProjectileColor, 11, 12);
                drawn++;
                continue;
            }

            if (action is MoveAction && TryGetSoldierMapPosition(action.ActorId, previousState, null, topLeftOffset, out Vector2 from)
                && TryGetSoldierMapPosition(action.ActorId, currentState, null, topLeftOffset, out Vector2 to)
                && from.DistanceTo(to) > 1.0f)
            {
                BattleSoldier currentSoldier = currentState.Soldiers.TryGetValue(action.ActorId, out BattleSoldier soldier) ? soldier : null;
                string label = currentSoldier?.IsInMelee == true ? "CHARGE" : "MOVE";
                DrawArrowLine(from, to, ChargeColor, 2.2f, 10);
                DrawCalloutLabel(label, (from + to) / 2.0f + new Vector2(4, -18), ChargeColor, 11, 11);
                drawn++;
                continue;
            }

            int? targetId = GetActionTargetId(action);
            if (targetId.HasValue
                && TryGetSoldierMapPosition(action.ActorId, currentState, previousState, topLeftOffset, out Vector2 actorPosition)
                && TryGetSoldierMapPosition(targetId.Value, currentState, previousState, topLeftOffset, out Vector2 targetCalloutPosition))
            {
                DrawArrowLine(actorPosition, targetCalloutPosition, ChargeColor, 2.0f, 10);
                DrawCalloutLabel("MELEE", (actorPosition + targetCalloutPosition) / 2.0f + new Vector2(4, -18), ChargeColor, 11, 11);
                drawn++;
            }
        }
    }

    private static int? GetActionTargetId(IAction action)
    {
        return action switch
        {
            ShootAction shootAction => shootAction.TargetId,
            MeleeAttackAction meleeAttackAction => meleeAttackAction.WoundResolutions.FirstOrDefault()?.Suffererer?.Soldier?.Id,
            _ => null
        };
    }

    private void DrawArrowLine(Vector2 start, Vector2 end, Color color, float width, int zIndex)
    {
        DrawLine(start, end, color, width, zIndex);
        Vector2 direction = end - start;
        if (direction.LengthSquared() <= 1.0f)
        {
            return;
        }

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

    private Vector2 GridToMapPosition(Tuple<int, int> gridPosition, Vector2I topLeftOffset)
    {
        Vector2I adjustedPosition = new(gridPosition.Item1 - topLeftOffset.X, gridPosition.Item2 - topLeftOffset.Y);
        return new Vector2(
            adjustedPosition.X * _pixelsPerGrid.X + _pixelsPerGrid.X / 2.0f,
            adjustedPosition.Y * _pixelsPerGrid.Y + _pixelsPerGrid.Y / 2.0f);
    }

    private Vector2 GetSoldierMapPosition(BattleSoldier soldier, Vector2I topLeftOffset)
    {
        IReadOnlyList<Tuple<int, int>> positions = soldier.PositionList;
        if (positions.Count == 0)
        {
            return GridToMapPosition(soldier.TopLeft, topLeftOffset);
        }

        return positions
            .Select(position => GridToMapPosition(position, topLeftOffset))
            .Aggregate(Vector2.Zero, (sum, position) => sum + position) / positions.Count;
    }

    private bool TryGetSoldierMapPosition(int soldierId, BattleState primaryState, BattleState fallbackState, Vector2I topLeftOffset, out Vector2 position)
    {
        if (primaryState != null && primaryState.Soldiers.TryGetValue(soldierId, out BattleSoldier primarySoldier))
        {
            position = GetSoldierMapPosition(primarySoldier, topLeftOffset);
            return true;
        }

        if (fallbackState != null && fallbackState.Soldiers.TryGetValue(soldierId, out BattleSoldier fallbackSoldier))
        {
            position = GetSoldierMapPosition(fallbackSoldier, topLeftOffset);
            return true;
        }

        position = Vector2.Zero;
        return false;
    }

    private Vector2 GetSquadCentroid(BattleSquad squad, Vector2I topLeftOffset)
    {
        List<Vector2> positions = squad.AbleSoldiers
            .SelectMany(soldier => soldier.PositionList)
            .Select(position => GridToMapPosition(position, topLeftOffset))
            .ToList();

        return positions.Count == 0
            ? Vector2.Zero
            : positions.Aggregate(Vector2.Zero, (sum, position) => sum + position) / positions.Count;
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

    private static BattleSquad TryGetSquad(BattleState state, int squadId)
    {
        if (state.PlayerSquads.TryGetValue(squadId, out BattleSquad playerSquad)) return playerSquad;
        if (state.OpposingSquads.TryGetValue(squadId, out BattleSquad opposingSquad)) return opposingSquad;
        return null;
    }
}
