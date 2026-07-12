using System.Collections.Generic;

namespace OnlyWar.Models.Battles
{
    public class BattleHistory
    {
        public List<BattleTurn> Turns { get; }
        public int EnemiesKilled { get; set; }
        // Stable soldier identities confirmed killed during this battle, independent of which
        // aftermath policy is active. Mission objectives can track a specific casualty directly.
        public HashSet<int> KilledSoldierIds { get; }

        public BattleHistory()
        {
            Turns = new List<BattleTurn>();
            EnemiesKilled = 0;
            KilledSoldierIds = new HashSet<int>();
        }
    }
}
