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

        // --- Structured mission-outcome signals (PRD 5.3 "Mission Field Experience & Records") ---
        // Set by the individual mission steps at the point each event resolves, so downstream consumers
        // (MissionOutcomeClassifier -> the career-log recorder and the end-of-turn report) classify how
        // the mission went from these facts rather than by string-matching Log lines - the wording of a
        // step's log line can change freely without silently breaking classification. Each flag is
        // monotonic: a step sets it true when the event happens and nothing clears it. The force's
        // terminal disposition is derived from the first four by MissionOutcomeClassifier (which applies
        // a worst-fate-wins priority); the last two capture orthogonal objective facts.

        // The strike force slipped back out after being detected (evaded the interceptors / exfiltrated).
        public bool ForceBrokeContact { get; set; }
        // The force could not break contact and was lost behind enemy lines (assumed dead / gone to ground).
        public bool ForceLostContact { get; set; }
        // An embedded engagement left the force combat-ineffective and ended the mission under fire.
        public bool ForceWithdrewUnderFire { get; set; }
        // The force could not reach its objective before acting (failed to infiltrate / too many casualties).
        public bool ObjectiveAborted { get; set; }
        // The operation found nothing worthwhile to engage (a raid/ambush that turned up no target).
        public bool NoViableTarget { get; set; }
        // An assassination force reached and identified its target.
        public bool TargetLocated { get; set; }

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
