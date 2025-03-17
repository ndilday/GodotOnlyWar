using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System.Collections.Generic;

namespace OnlyWar.Models.Missions
{
    public class MissionContext
    {
        public Order Order { get; }
        public List<BattleSquad> PlayerSquads { get; }
        public ushort DaysElapsed { get; set; }
        public List<BattleSquad> OpposingForces { get; set; }
        public List<string> Log { get; private set; }

        public List<Mission> MissionsToAdd { get; }
        public List<Mission> MissionsToRemove { get; }
        public float Impact { get; set; }
        public int EnemiesKilled { get; set; }

        public MissionContext(Order order, List<BattleSquad> playerSquads, List<BattleSquad> opposingForces)
        {
            Order = order;
            PlayerSquads = playerSquads;
            OpposingForces = opposingForces;
            DaysElapsed = 0;
            MissionsToAdd = new List<Mission>();
            MissionsToRemove = new List<Mission>();
            Log = new List<string>();
            Impact = 0.0f;
            EnemiesKilled = 0;
        }
    }
}
