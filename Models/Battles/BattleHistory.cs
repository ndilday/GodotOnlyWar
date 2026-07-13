using System.Collections.Generic;

namespace OnlyWar.Models.Battles
{
    public class BattleHistory
    {
        public List<BattleTurn> Turns { get; }
        // Player-career credit. Multiple fatal hits on one enemy can legitimately produce multiple
        // credits when the shots resolve simultaneously.
        public int EnemiesKilled { get; set; }
        // Per-hit kill credits against the second side. Mission battles pass the mission force as the
        // first side, so this is the mission force's credit total and may exceed actual deaths.
        public int FirstSideEnemiesKilled { get; set; }
        // Unique soldiers killed on the second side. Unlike FirstSideEnemiesKilled, this is a body
        // count and cannot exceed the second side's starting soldier count.
        public int FirstSideEnemyDeaths { get; set; }
        // Stable soldier identities confirmed killed during this battle, independent of which
        // aftermath policy is active. Mission objectives can track a specific casualty directly.
        public HashSet<int> KilledSoldierIds { get; }
        // Closing facts (casualties, who held the field) rendered after the per-turn log. Populated
        // once by BattleTurnResolver at the end of the battle, via BattleSummaryBuilder.
        public List<string> ClosingSummary { get; }

        public BattleHistory()
        {
            Turns = new List<BattleTurn>();
            EnemiesKilled = 0;
            FirstSideEnemiesKilled = 0;
            FirstSideEnemyDeaths = 0;
            KilledSoldierIds = new HashSet<int>();
            ClosingSummary = new List<string>();
        }
    }
}
