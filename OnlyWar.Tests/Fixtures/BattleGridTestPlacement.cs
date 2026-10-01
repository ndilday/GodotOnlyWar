using System;
using System.Collections.Generic;

using OnlyWar.Battles;

namespace OnlyWar.Tests.Fixtures;

/// <summary>
/// Places test soldiers on a battle grid in the same footprint convention the battle placer and
/// MoveAction use (<see cref="BattleOrientation.GetFootprint"/>), so a test soldier's TopLeft and
/// the cells it occupies on the grid always agree.
/// </summary>
internal static class BattleGridTestPlacement
{
    /// <summary>The cells the soldier covers at its current TopLeft and Orientation.</summary>
    internal static List<ValueTuple<int, int>> GridFootprint(this BattleSoldier soldier) =>
        BattleOrientation.GetFootprint(soldier, soldier.TopLeft.Value, soldier.Orientation);

    /// <summary>Sets the soldier's TopLeft and Orientation and places its footprint.</summary>
    internal static BattleSoldier PlaceAt(
        this BattleGridManager grid,
        BattleSoldier soldier,
        bool side,
        ValueTuple<int, int> topLeft,
        ushort orientation = 0)
    {
        soldier.TopLeft = topLeft;
        soldier.Orientation = orientation;
        grid.PlaceSoldier(soldier, side, soldier.GridFootprint());
        return soldier;
    }
}
