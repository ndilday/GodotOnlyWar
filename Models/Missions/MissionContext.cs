using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System.Collections.Generic;

namespace OnlyWar.Models.Missions
{
    public class MissionContext
    {
        public Region Region { get; }
        public MissionType MissionType { get; }
        public Aggression Aggression { get; }
        public List<BattleSquad> PlayerSquads { get; }
        public ushort DaysElapsed { get; set; }
        public List<BattleSquad> OpposingForces { get; set; }
        public List<string> Log { get; private set; }

        public List<SpecialMission> MissionsToAdd { get; }
        public List<SpecialMission> MissionsToRemove { get; }
        public float Impact { get; set; }
        public int EnemiesKilled { get; set; }

        public MissionContext(Region region, MissionType missionType, Aggression aggression, List<BattleSquad> playerSquads, List<BattleSquad> opposingForces)
        {
            Region = region;
            MissionType = missionType;
            Aggression = aggression;
            PlayerSquads = playerSquads;
            OpposingForces = opposingForces;
            DaysElapsed = 0;
            MissionsToAdd = new List<SpecialMission>();
            MissionsToRemove = new List<SpecialMission>();
            Log = new List<string>();
            Impact = 0.0f;
            EnemiesKilled = 0;
        }
    }
}
