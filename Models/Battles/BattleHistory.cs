using System.Collections.Generic;

namespace OnlyWar.Models.Battles
{
    public class BattleHistory
    {
        public List<BattleTurn> Turns { get; }

        public BattleHistory()
        {
            Turns = new List<BattleTurn>();
        }
    }
}
