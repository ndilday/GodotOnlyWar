using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System.Collections.Generic;

namespace OnlyWar.Models.Missions
{
    public class MissionDebriefLine
    {
        public string Text { get; }
        public BattleHistory BattleHistory { get; }
        public bool HasBattle => BattleHistory != null;

        public MissionDebriefLine(string text, BattleHistory battleHistory = null)
        {
            Text = text ?? "";
            BattleHistory = battleHistory;
        }
    }

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
        public List<MissionDebriefLine> DebriefLines { get; }

        // The enemy faction that detected the intruder, resolved by Region.SelectSpotter when a
        // stealth check fails. It carries the spotter from the detection step to DetectedMissionStep
        // so the intercepting force is raised from the faction that actually caught the scout - which,
        // in a multi-faction region, need not be the mission's anchor RegionFaction. Null until a
        // detection resolves one; flows that never set it fall back to the mission's target faction.
        public RegionFaction Spotter { get; set; }

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
            DebriefLines = new List<MissionDebriefLine>();
            Impact = 0.0f;
            EnemiesKilled = 0;
        }

        public void AddLog(string text)
        {
            Log.Add(text);
            DebriefLines.Add(new MissionDebriefLine(text));
        }

        public void AddBattleLog(string text, BattleHistory battleHistory)
        {
            Log.Add(text);
            DebriefLines.Add(new MissionDebriefLine(text, battleHistory));
        }
    }
}
