using System.Collections.Generic;

namespace OnlyWar.Models.Battles
{
    public class BattleHistory
    {
        public List<BattleTurn> Turns { get; }
        public int EnemiesKilled { get; set; }

        public BattleHistory()
        {
            Turns = new List<BattleTurn>();
            EnemiesKilled = 0;
        }
    }
}
