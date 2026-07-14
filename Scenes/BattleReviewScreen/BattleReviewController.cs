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
    private Vector2I _mapOffset;
    private Vector2 _mapSize = Vector2.One;
    private bool _hasFramedCamera;

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
        _hasFramedCamera = false;
        ComputeMapBounds();
        DisplayTurn(0);
    }

    // Establishes a single, stable map coordinate system that covers every position
    // across the whole replay, so the drawn origin doesn't shift turn-to-turn (which
    // would make the player's manual pan/zoom jump between rounds).
    private void ComputeMapBounds()
    {
        List<Tuple<int, int>> allPositions = _history.Turns
            .SelectMany(turn => GetBoundaryPositions(turn.State))
            .ToList();

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
        BattleStateSnapshot state = currentTurn.State;
        BattleStateSnapshot previousState = turnIndex > 0 ? _history.Turns[turnIndex - 1].State : null;

        DrawBackground(_mapSize);
        DrawGrid(_mapSize);
        DrawRoundOverlays(previousState, state, currentTurn, _mapOffset);

        foreach (BattleSquadSnapshot squad in state.AttackerSquads.Values.OrderBy(squad => squad.Id))
        {
            DrawSquad(squad, _mapOffset, selectedFormationId == squad.Id);
        }
        foreach (BattleSquadSnapshot squad in state.OpposingSquads.Values.OrderBy(squad => squad.Id))
        {
            DrawSquad(squad, _mapOffset, selectedFormationId == squad.Id);
        }

        // Frame the deployment so every participant is visible, but only on open —
        // afterward the player's manual pan/zoom is preserved across rounds.
        if (!_hasFramedCamera)
        {
            FrameParticipants(GetAllReplayPositions(state, previousState));
            _hasFramedCamera = true;
        }
    }

    private static IReadOnlyList<Tuple<int, int>> GetAllReplayPositions(BattleStateSnapshot currentState, BattleStateSnapshot previousState)
    {
        List<Tuple<int, int>> positions = GetBoundaryPositions(currentState).ToList();
        if (previousState != null)
        {
            positions.AddRange(GetBoundaryPositions(previousState));
        }

        return positions;
    }

    private static IEnumerable<Tuple<int, int>> GetBoundaryPositions(BattleStateSnapshot state)
    {
        foreach (BattleSoldierSnapshot soldier in state.Soldiers.Values)
        {
            yield return new Tuple<int, int>(soldier.MinX, soldier.MinY);
            yield return new Tuple<int, int>(soldier.MaxX, soldier.MaxY);
        }
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

    private void DrawSquad(BattleSquadSnapshot squad, Vector2I topLeftOffset, bool selected)
    {
        List<Vector2> markerPositions = [];
        foreach (BattleSoldierSnapshot soldier in squad.Soldiers)
        {
            // The compact snapshot retains footprint bounds for combat framing. The report marker
            // represents one model, however, so draw one centered marker per soldier (a 4x2
            // Carnifex must not become eight circles).
            Vector2 position = GetSoldierMapPosition(soldier, topLeftOffset);
            markerPositions.Add(position);
            DrawMarker(position, squad.IsPlayerAligned, selected, squad.Id);
        }

        if (markerPositions.Count == 0) return;

        Vector2 centroid = markerPositions.Aggregate(Vector2.Zero, (sum, position) => sum + position) / markerPositions.Count;
        DrawFormationLabel(squad, centroid, selected);
    }

    private void DrawFormationBanner(BattleSquadSnapshot squad, Vector2 centroid, bool selected)
    {
        Color color = selected ? SelectedMarkerColor : squad.IsPlayerAligned ? PlayerMarkerColor : OpposingMarkerColor;
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

    private void DrawFormationLabel(BattleSquadSnapshot squad, Vector2 centroid, bool selected)
    {
        Label label = new()
        {
            Text = $"{squad.Name}  {squad.Soldiers.Count}",
            Position = centroid + new Vector2(10, -28),
            ZIndex = selected ? 6 : 5,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeColorOverride("font_color", selected ? SelectedMarkerColor : squad.IsPlayerAligned ? PlayerMarkerColor : OpposingMarkerColor);
        label.AddThemeFontSizeOverride("font_size", selected ? 14 : 12);
        _view.MapRoot.AddChild(label);
    }

    private void DrawRoundOverlays(BattleStateSnapshot previousState, BattleStateSnapshot currentState, BattleTurn currentTurn, Vector2I topLeftOffset)
    {
        if (previousState == null)
        {
            return;
        }

        DrawCasualtyMarkers(previousState, currentState, topLeftOffset);
        DrawRoutMarkers(previousState, currentState, topLeftOffset);
        DrawActionCallouts(previousState, currentState, currentTurn, topLeftOffset);
    }

    private void DrawCasualtyMarkers(BattleStateSnapshot previousState, BattleStateSnapshot currentState, Vector2I topLeftOffset)
    {
        foreach (BattleSoldierSnapshot soldier in previousState.Soldiers.Values.OrderBy(soldier => soldier.Id))
        {
            if (currentState.Soldiers.ContainsKey(soldier.Id))
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

    private void DrawRoutMarkers(BattleStateSnapshot previousState, BattleStateSnapshot currentState, Vector2I topLeftOffset)
    {
        foreach (BattleSquadSnapshot previousSquad in previousState.AttackerSquads.Values.Concat(previousState.OpposingSquads.Values).OrderBy(squad => squad.Id))
        {
            if (previousSquad.Soldiers.Count == 0)
            {
                continue;
            }

            BattleSquadSnapshot currentSquad = TryGetSquad(currentState, previousSquad.Id);
            if (currentSquad?.Soldiers.Count > 0)
            {
                continue;
            }

            Vector2 centroid = GetSquadCentroid(previousSquad, topLeftOffset);
            DrawCalloutLabel("ROUT", centroid + new Vector2(-22, -42), RoutColor, 13, 12);
            DrawLine(centroid + new Vector2(-20, 14), centroid + new Vector2(18, 34), RoutColor, 2.0f, 8);
            DrawLine(centroid + new Vector2(-6, 18), centroid + new Vector2(32, 38), RoutColor, 2.0f, 8);
        }
    }

    private void DrawActionCallouts(BattleStateSnapshot previousState, BattleStateSnapshot currentState, BattleTurn currentTurn, Vector2I topLeftOffset)
    {
        // Keep the replay faithful to the recorded turn. Large formations can
        // legitimately produce more than 16 actions, especially during an
        // opening volley, so do not truncate the action overlays by count.
        foreach (IAction action in currentTurn.Actions)
        {
            if (action is ShootAction shootAction && TryGetSoldierMapPosition(shootAction.ShooterId, currentState, previousState, topLeftOffset, out Vector2 shooterPosition)
                && TryGetSoldierMapPosition(shootAction.TargetId, currentState, previousState, topLeftOffset, out Vector2 targetPosition))
            {
                DrawDashedLine(shooterPosition, targetPosition, ProjectileColor, 1.15f, 11);
                DrawCalloutLabel($"{Math.Max(1, shootAction.NumberOfShots)} SHOTS", (shooterPosition + targetPosition) / 2.0f + new Vector2(4, -18), ProjectileColor, 11, 12);
                continue;
            }

            if (action is MoveAction && TryGetSoldierMapPosition(action.ActorId, previousState, null, topLeftOffset, out Vector2 from)
                && TryGetSoldierMapPosition(action.ActorId, currentState, null, topLeftOffset, out Vector2 to)
                && from.DistanceTo(to) > 1.0f)
            {
                BattleSoldierSnapshot currentSoldier = currentState.Soldiers.TryGetValue(action.ActorId, out BattleSoldierSnapshot soldier) ? soldier : null;
                string label = currentSoldier?.IsInMelee == true ? "CHARGE" : "MOVE";
                DrawArrowLine(from, to, ChargeColor, 2.2f, 10);
                DrawCalloutLabel(label, (from + to) / 2.0f + new Vector2(4, -18), ChargeColor, 11, 11);
                continue;
            }

            int? targetId = GetActionTargetId(action);
            if (targetId.HasValue
                && TryGetSoldierMapPosition(action.ActorId, currentState, previousState, topLeftOffset, out Vector2 actorPosition)
                && TryGetSoldierMapPosition(targetId.Value, currentState, previousState, topLeftOffset, out Vector2 targetCalloutPosition))
            {
                DrawArrowLine(actorPosition, targetCalloutPosition, ChargeColor, 2.0f, 10);
                DrawCalloutLabel("MELEE", (actorPosition + targetCalloutPosition) / 2.0f + new Vector2(4, -18), ChargeColor, 11, 11);
            }
        }
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
            DrawLine(start + direction * offset, start + direction * Math.Min(offset + dashLength, length), color, width, zIndex);
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

    private Vector2 GetSoldierMapPosition(BattleSoldierSnapshot soldier, Vector2I topLeftOffset)
    {
        return new Vector2(
            (soldier.CenterX - topLeftOffset.X) * _pixelsPerGrid.X + _pixelsPerGrid.X / 2.0f,
            (soldier.CenterY - topLeftOffset.Y) * _pixelsPerGrid.Y + _pixelsPerGrid.Y / 2.0f);
    }

    private bool TryGetSoldierMapPosition(int soldierId, BattleStateSnapshot primaryState, BattleStateSnapshot fallbackState, Vector2I topLeftOffset, out Vector2 position)
    {
        if (primaryState != null && primaryState.Soldiers.TryGetValue(soldierId, out BattleSoldierSnapshot primarySoldier))
        {
            position = GetSoldierMapPosition(primarySoldier, topLeftOffset);
            return true;
        }

        if (fallbackState != null && fallbackState.Soldiers.TryGetValue(soldierId, out BattleSoldierSnapshot fallbackSoldier))
        {
            position = GetSoldierMapPosition(fallbackSoldier, topLeftOffset);
            return true;
        }

        position = Vector2.Zero;
        return false;
    }

    private Vector2 GetSquadCentroid(BattleSquadSnapshot squad, Vector2I topLeftOffset)
    {
        List<Vector2> positions = squad.Soldiers
            .Select(soldier => GetSoldierMapPosition(soldier, topLeftOffset))
            .ToList();

        return positions.Count == 0
            ? Vector2.Zero
            : positions.Aggregate(Vector2.Zero, (sum, position) => sum + position) / positions.Count;
    }

    // Centers on and zooms in as tightly as possible while keeping every participant
    // position visible. The camera uses FixedTopLeft anchoring, so Position is the
    // top-left world corner of the view (not its center).
    private void FrameParticipants(IReadOnlyList<Tuple<int, int>> positions)
    {
        Vector2 contentMin;
        Vector2 contentMax;
        Vector2 halfCell = new Vector2(_pixelsPerGrid.X, _pixelsPerGrid.Y) / 2.0f;
        if (positions.Count == 0)
        {
            contentMin = Vector2.Zero;
            contentMax = _mapSize;
        }
        else
        {
            Vector2I topLeft = GetTopLeftOfPositions(positions);
            Vector2I bottomRight = GetBottomRightOfPositions(positions);
            contentMin = GridToMapPosition(Tuple.Create(topLeft.X, topLeft.Y), _mapOffset) - halfCell;
            contentMax = GridToMapPosition(Tuple.Create(bottomRight.X, bottomRight.Y), _mapOffset) + halfCell;
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
            0.35f,
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

    private static BattleSquadSnapshot TryGetSquad(BattleStateSnapshot state, int squadId)
    {
        if (state.AttackerSquads.TryGetValue(squadId, out BattleSquadSnapshot attackerSquad)) return attackerSquad;
        if (state.OpposingSquads.TryGetValue(squadId, out BattleSquadSnapshot opposingSquad)) return opposingSquad;
        return null;
    }
}
