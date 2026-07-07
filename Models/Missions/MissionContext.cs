using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System.Collections.Generic;

namespace OnlyWar.Models.Missions
{
    public class MissionContext
    {
        // A strategic turn is one week, so a mission plays out over at most this many days. Looping
        // steps (recon stealth/detect/evade, exfiltration) must honor this cap: previously only a
        // *successful* recon checked the day count, so a scout stuck failing stealth against a
        // heavily-garrisoned region could loop far past the week (DaysElapsed observed climbing to
        // 20+). Exfiltration gets a small grace beyond the week to break contact before it is lost.
        public const int MissionDurationDays = 7;
        public const int ExfiltrationGraceDays = 3;

        public Order Order { get; }
        public List<BattleSquad> MissionSquads { get; }
        public ushort DaysElapsed { get; set; }
        public List<BattleSquad> OpposingSquads { get; set; }
        public List<string> Log { get; private set; }

        public List<Mission> MissionsToAdd { get; }
        public List<Mission> MissionsToRemove { get; }
        public float Impact { get; set; }
        public int EnemiesKilled { get; set; }

        public MissionContext(Order order, List<BattleSquad> playerSquads, List<BattleSquad> opposingForces)
        {
            Order = order;
            MissionSquads = playerSquads;
            OpposingSquads = opposingForces;
            DaysElapsed = 0;
            MissionsToAdd = new List<Mission>();
            MissionsToRemove = new List<Mission>();
            Log = new List<string>();
            Impact = 0.0f;
            EnemiesKilled = 0;
        }
    }
}
