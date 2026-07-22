using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.Missions
{
    public class MissionDebriefLine
    {
        public string Text { get; }
        public BattleHistory BattleHistory { get; }
        public BattleDebriefReport BattleReport { get; }
        public ushort? Day { get; }
        public string SquadName { get; }
        public bool HasBattle => BattleHistory != null;

        public MissionDebriefLine(
            string text,
            BattleHistory battleHistory = null,
            BattleDebriefReport battleReport = null,
            ushort? day = null,
            string squadName = null)
        {
            Text = text ?? "";
            BattleHistory = battleHistory;
            BattleReport = battleReport;
            Day = day;
            SquadName = squadName;
        }
    }

    public enum BattleCasualtyDisposition
    {
        Dead,
        ReplacementRequired,
        Recovering
    }

    public sealed record BattleCasualtyEntry(
        int SoldierId,
        string Name,
        string Rank,
        string Squad,
        string Company,
        BattleCasualtyDisposition Disposition,
        int RecoveryWeeks);

    public sealed record BattleDebriefReport(
        int PlayerDeaths,
        int OpposingDeaths,
        IReadOnlyList<BattleCasualtyEntry> PlayerCasualties);

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
        public IReadOnlyList<PlayerSoldier> StartingPlayerParticipants { get; }
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
        // Unique enemy bodies killed by this mission. This is the report-facing casualty count.
        public int EnemiesKilled { get; set; }
        // Per-hit/per-attacker credits, which may exceed EnemiesKilled when simultaneous fatal hits
        // land on the same enemy.
        public int EnemyKillCredits { get; set; }

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
        // The generated HQ soldier selected as the assassination objective, and whether that exact
        // soldier was confirmed killed. Bodyguard/interceptor casualties do not satisfy the objective.
        public int? AssassinationTargetSoldierId { get; set; }
        public bool TargetEliminated { get; set; }

        public MissionContext(Order order, List<BattleSquad> playerSquads, List<BattleSquad> opposingForces)
        {
            Order = order;
            MissionSquads = playerSquads;
            StartingPlayerParticipants = playerSquads
                .SelectMany(squad => squad.Soldiers)
                .Select(battleSoldier => battleSoldier.Soldier)
                .OfType<PlayerSoldier>()
                .Distinct()
                .ToList();
            OpposingSquads = opposingForces;
            DaysElapsed = 0;
            MissionsToAdd = new List<Mission>();
            MissionsToRemove = new List<Mission>();
            Log = new List<string>();
            DebriefLines = new List<MissionDebriefLine>();
            Impact = 0.0f;
            EnemiesKilled = 0;
            EnemyKillCredits = 0;
        }

        public void AddLog(string text)
        {
            Log.Add(text);
            DebriefLines.Add(new MissionDebriefLine(
                text,
                day: GetElementDay(),
                squadName: GetElementSquadName()));
        }

        public void AddBattleReport(BattleHistory battleHistory)
        {
            BattleDebriefReport report = BattleDebriefReportBuilder.Build(battleHistory);
            string summary = $"Friendly dead: {report.PlayerDeaths}    Opposing dead: {report.OpposingDeaths}";
            Log.Add(summary);
            DebriefLines.Add(new MissionDebriefLine(
                summary,
                battleHistory,
                report,
                GetElementDay(),
                GetElementSquadName()));
        }

        private string GetElementSquadName() =>
            IsIndependentReconElement() ? MissionSquads[0].Squad?.Name : null;

        private ushort? GetElementDay() =>
            IsIndependentReconElement() ? DaysElapsed : null;

        private bool IsIndependentReconElement() =>
            Order?.Mission?.MissionType == MissionType.Recon && MissionSquads.Count == 1;

        public void RecordBattleOutcome(BattleHistory battleHistory)
        {
            EnemiesKilled += battleHistory.FirstSideEnemyDeaths;
            EnemyKillCredits += battleHistory.FirstSideEnemiesKilled;
            if (AssassinationTargetSoldierId is int targetId
                && battleHistory.KilledSoldierIds.Contains(targetId))
            {
                TargetEliminated = true;
            }

            if (MissionSideWithdrewOrRouted(battleHistory.Outcome))
            {
                ForceWithdrewUnderFire = true;
            }
        }

        public BattleSideProfile CreateMissionBattleProfile(BattleRole role) =>
            new(Order?.LevelOfAggression ?? Aggression.Normal, role);

        public static BattleSideProfile CreateOpposingBattleProfile(
            IEnumerable<BattleSquad> opposingSquads,
            BattleRole role)
        {
            List<Aggression> aggressions = (opposingSquads ?? Enumerable.Empty<BattleSquad>())
                .Select(squad => squad.Squad?.CurrentOrders)
                .Where(order => order != null)
                .Select(order => order.LevelOfAggression)
                .Distinct()
                .OrderBy(aggression => aggression)
                .ToList();
            Aggression aggression = aggressions.Count == 1 ? aggressions[0] : Aggression.Normal;
            return new BattleSideProfile(aggression, role);
        }

        private bool MissionSideWithdrewOrRouted(BattleOutcome outcome)
        {
            if (outcome == null)
            {
                return false;
            }

            HashSet<int> missionSquadIds = MissionSquads.Select(squad => squad.Id).ToHashSet();
            if (outcome.DisengagedSquadIds.Any(missionSquadIds.Contains)
                || outcome.RoutingSquadIds.Any(missionSquadIds.Contains))
            {
                return true;
            }

            if (outcome.EndReason == BattleEndReason.MutualDisengagement)
            {
                return true;
            }

            bool withdrawalEnding = outcome.EndReason is BattleEndReason.Withdrawal
                or BattleEndReason.Rout;
            return withdrawalEnding && outcome.SideHoldingField == BattleSide.Opposing;
        }
    }
}
