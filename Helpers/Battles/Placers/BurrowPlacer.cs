using System;
using System.Collections.Generic;

namespace OnlyWar.Helpers.Battles.Placers
{
    /// <summary>
    /// Burrow-arrival placement. After the normal placer has set up both
    /// formations at engagement range, this pulls every burrow-capable squad up
    /// against the nearest enemy so it erupts directly into melee on turn one,
    /// rather than charging in from the open. See
    /// Design/EvasionBurrowAndAmbush.md.
    ///
    /// Implemented as a relocation pass over squads already on the grid: it reuses
    /// the grid's existing nearest-enemy and open-adjacency queries and moves each
    /// burrower individually. A burrower that cannot find an open adjacent spot
    /// simply stays where the normal placer put it (graceful degradation).
    /// </summary>
    public static class BurrowPlacer
    {
        public static void PlaceBurrowers(BattleGridManager grid, IEnumerable<BattleSquad> squads)
        {
            foreach (BattleSquad squad in squads)
            {
                if (!squad.CanBurrow)
                {
                    continue;
                }
                foreach (BattleSoldier soldier in squad.AbleSoldiers)
                {
                    EruptNextToEnemy(grid, soldier);
                }
            }
        }

        private static void EruptNextToEnemy(BattleGridManager grid, BattleSoldier soldier)
        {
            grid.GetNearestEnemy(soldier.Soldier.Id, out int enemyId);
            if (enemyId < 0)
            {
                return;
            }

            Tuple<int, int> currentTopLeft = soldier.TopLeft;
            foreach (Tuple<int, int> enemyCell in grid.GetSoldierPosition(enemyId))
            {
                Tuple<int, int> spot = grid.GetClosestOpenAdjacency(currentTopLeft, enemyCell);
                if (spot == null)
                {
                    continue;
                }
                try
                {
                    // MoveSoldier validates the full footprint is clear (allowing the
                    // soldier's own current cells); on a conflict it throws and we try
                    // the next enemy cell.
                    grid.MoveSoldier(soldier, spot, soldier.Orientation);
                    soldier.TopLeft = spot;
                    return;
                }
                catch (InvalidOperationException)
                {
                    // footprint blocked at this spot; keep looking
                }
            }
        }
    }
}
