using Godot;
using System;

public partial class FleetTransferTree : Tree
{
    private const string SquadPrefix = "Squad:";
    private const string UnitPrefix = "Unit:";
    private const string ShipPrefix = "Ship:";

    public Func<int, int, bool> CanTransferSquadToShip { get; set; }
    public Action<int, int> TransferSquadToShip { get; set; }
    public Func<int, int, int, bool> CanTransferUnitToShip { get; set; }
    public Action<int, int, int> TransferUnitToShip { get; set; }
    private bool _isDraggingTransfer;
    private TreeItem _pendingFoldItem;

    public override void _Ready()
    {
        MouseExited += ResetCursorShape;
    }

    /// <summary>
    /// Tree folds a non-selectable row (a company) on mouse-down, which happens before the
    /// viewport can promote the press into a drag. Dragging a company therefore read as a
    /// click: the row folded and everything beneath it shifted out from under the cursor.
    /// Hold the fold until release, and abandon it once the press becomes a drag.
    /// </summary>
    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mouseButton
            || mouseButton.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        if (mouseButton.Pressed)
        {
            TreeItem item = GetItemAtPosition(mouseButton.Position);
            if (item == null || item.GetChildCount() == 0 || item.IsSelectable(0))
            {
                return;
            }

            _pendingFoldItem = item;
            // Accepting the event keeps Tree's own handler from folding the row, without
            // clearing the viewport's mouse focus, so drag detection still works.
            AcceptEvent();
        }
        else if (_pendingFoldItem != null)
        {
            TreeItem item = _pendingFoldItem;
            _pendingFoldItem = null;
            if (IsInstanceValid(item) && item == GetItemAtPosition(mouseButton.Position))
            {
                item.Collapsed = !item.Collapsed;
            }

            AcceptEvent();
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationDragBegin)
        {
            // The press became a drag; the release never reaches _GuiInput, so drop the fold.
            _pendingFoldItem = null;
        }

        if (what == NotificationDragEnd && _isDraggingTransfer)
        {
            _isDraggingTransfer = false;
            ResetCursorShape();
            CallDeferred(MethodName.ResetCursorShape);
        }
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        TreeItem item = GetItemAtPosition(atPosition);
        string dragData;
        string previewText;
        if (TryReadId(item, SquadPrefix, out int squadId))
        {
            dragData = $"{SquadPrefix}{squadId}";
            previewText = item.GetText(0);
        }
        else if (TryReadId(item, UnitPrefix, out int unitId)
            && TryReadId(item.GetParent(), ShipPrefix, out int sourceShipId))
        {
            dragData = $"{UnitPrefix}{unitId}:{sourceShipId}";
            previewText = GetUnitDragPreviewText(item);
        }
        else
        {
            return default;
        }

        SetDragPreview(CreateDragPreview(previewText));
        _isDraggingTransfer = true;
        MouseDefaultCursorShape = CursorShape.Drag;
        return Variant.From(dragData);
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        bool canDrop = false;
        if (TryReadId(GetItemAtPosition(atPosition), ShipPrefix, out int shipId))
        {
            canDrop = TryReadDraggedSquadId(data, out int squadId)
                ? CanTransferSquadToShip?.Invoke(squadId, shipId) == true
                : TryReadDraggedUnit(data, out int unitId, out int sourceShipId)
                    && CanTransferUnitToShip?.Invoke(unitId, sourceShipId, shipId) == true;
        }

        MouseDefaultCursorShape = canDrop ? CursorShape.CanDrop : CursorShape.Forbidden;
        return canDrop;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (TryReadId(GetItemAtPosition(atPosition), ShipPrefix, out int shipId))
        {
            if (TryReadDraggedSquadId(data, out int squadId)
                && CanTransferSquadToShip?.Invoke(squadId, shipId) == true)
            {
                TransferSquadToShip?.Invoke(squadId, shipId);
            }
            else if (TryReadDraggedUnit(data, out int unitId, out int sourceShipId)
                && CanTransferUnitToShip?.Invoke(unitId, sourceShipId, shipId) == true)
            {
                TransferUnitToShip?.Invoke(unitId, sourceShipId, shipId);
            }
        }

        ResetCursorShape();
    }

    private void ResetCursorShape()
    {
        MouseDefaultCursorShape = CursorShape.Arrow;
    }

    private static string GetUnitDragPreviewText(TreeItem unitItem)
    {
        int squadCount = unitItem.GetChildCount();
        if (squadCount == 1)
        {
            return unitItem.GetFirstChild()?.GetText(0) ?? "1 squad";
        }

        return $"{squadCount} squads";
    }

    private static Control CreateDragPreview(string text)
    {
        float width = Mathf.Max(96f, (text?.Length ?? 0) * 8f + 24f);
        PanelContainer badge = new()
        {
            CustomMinimumSize = new Vector2(width, 32f),
            MouseFilter = MouseFilterEnum.Ignore,
            Position = new Vector2(18f, 18f)
        };

        StyleBoxFlat background = new()
        {
            BgColor = new Color("26354aee"),
            BorderColor = new Color("8fb7e8"),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomRight = 4,
            CornerRadiusBottomLeft = 4,
            ContentMarginLeft = 10f,
            ContentMarginTop = 5f,
            ContentMarginRight = 10f,
            ContentMarginBottom = 5f
        };
        badge.AddThemeStyleboxOverride("panel", background);

        Label label = new()
        {
            Text = text,
            MouseFilter = MouseFilterEnum.Ignore,
            VerticalAlignment = VerticalAlignment.Center
        };
        label.AddThemeColorOverride("font_color", Colors.White);
        badge.AddChild(label);
        return badge;
    }

    private static bool TryReadDraggedSquadId(Variant data, out int squadId)
    {
        return TryReadId(data.AsString(), SquadPrefix, out squadId);
    }

    private static bool TryReadDraggedUnit(Variant data, out int unitId, out int sourceShipId)
    {
        unitId = 0;
        sourceShipId = 0;
        string metadata = data.AsString();
        if (string.IsNullOrEmpty(metadata)
            || !metadata.StartsWith(UnitPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        string[] ids = metadata[UnitPrefix.Length..].Split(':');
        return ids.Length == 2
            && int.TryParse(ids[0], out unitId)
            && int.TryParse(ids[1], out sourceShipId);
    }

    private static bool TryReadId(TreeItem item, string prefix, out int id)
    {
        id = 0;
        if (item == null)
        {
            return false;
        }

        return TryReadId(item.GetMetadata(0).AsString(), prefix, out id);
    }

    private static bool TryReadId(string metadata, string prefix, out int id)
    {
        id = 0;
        return !string.IsNullOrEmpty(metadata)
            && metadata.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(metadata[prefix.Length..], out id);
    }
}
