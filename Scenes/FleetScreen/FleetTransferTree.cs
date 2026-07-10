using Godot;
using System;

public partial class FleetTransferTree : Tree
{
    private const string SquadPrefix = "Squad:";
    private const string ShipPrefix = "Ship:";

    public Func<int, int, bool> CanTransferSquadToShip { get; set; }
    public Action<int, int> TransferSquadToShip { get; set; }
    private bool _isDraggingSquad;

    public override void _Ready()
    {
        MouseExited += ResetCursorShape;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationDragEnd && _isDraggingSquad)
        {
            _isDraggingSquad = false;
            ResetCursorShape();
            CallDeferred(MethodName.ResetCursorShape);
        }
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        TreeItem item = GetItemAtPosition(atPosition);
        if (!TryReadId(item, SquadPrefix, out int squadId))
        {
            return default;
        }

        Label preview = new()
        {
            Text = item.GetText(0),
            Modulate = new Color(1f, 1f, 1f, 0.9f)
        };
        SetDragPreview(preview);
        _isDraggingSquad = true;
        MouseDefaultCursorShape = CursorShape.Drag;
        return Variant.From($"{SquadPrefix}{squadId}");
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        bool canDrop = TryReadDraggedSquadId(data, out int squadId)
            && TryReadId(GetItemAtPosition(atPosition), ShipPrefix, out int shipId)
            && CanTransferSquadToShip?.Invoke(squadId, shipId) == true;

        MouseDefaultCursorShape = canDrop ? CursorShape.CanDrop : CursorShape.Forbidden;
        return canDrop;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (TryReadDraggedSquadId(data, out int squadId)
            && TryReadId(GetItemAtPosition(atPosition), ShipPrefix, out int shipId)
            && CanTransferSquadToShip?.Invoke(squadId, shipId) == true)
        {
            TransferSquadToShip?.Invoke(squadId, shipId);
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
