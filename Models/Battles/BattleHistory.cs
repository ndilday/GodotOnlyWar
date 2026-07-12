using System.Collections.Generic;

namespace OnlyWar.Models.Battles
{
    public class BattleHistory
    {
        public List<BattleTurn> Turns { get; }
        public int EnemiesKilled { get; set; }
        // Casualties suffered by the second side. Mission battles always pass the mission force as
        // the first side, so this is the kill count used by mission outcome reporting. EnemiesKilled
        // remains the player-chapter career credit and can have the opposite perspective in NPC missions.
        public int FirstSideEnemiesKilled { get; set; }
        // Stable soldier identities confirmed killed during this battle, independent of which
        // aftermath policy is active. Mission objectives can track a specific casualty directly.
        public HashSet<int> KilledSoldierIds { get; }

        public BattleHistory()
        {
            Turns = new List<BattleTurn>();
            EnemiesKilled = 0;
            FirstSideEnemiesKilled = 0;
            KilledSoldierIds = new HashSet<int>();
        }
    }
}
