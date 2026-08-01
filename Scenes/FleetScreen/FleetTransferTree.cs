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

    public override void _Ready()
    {
        MouseExited += ResetCursorShape;
    }

    public override void _Notification(int what)
    {
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
        if (TryReadId(item, SquadPrefix, out int squadId))
        {
            dragData = $"{SquadPrefix}{squadId}";
        }
        else if (TryReadId(item, UnitPrefix, out int unitId)
            && TryReadId(item.GetParent(), ShipPrefix, out int sourceShipId))
        {
            dragData = $"{UnitPrefix}{unitId}:{sourceShipId}";
        }
        else
        {
            return default;
        }

        Label preview = new()
        {
            Text = item.GetText(0),
            Modulate = new Color(1f, 1f, 1f, 0.9f)
        };
        SetDragPreview(preview);
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
