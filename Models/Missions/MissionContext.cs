using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Missions;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
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

        // True when the force both intends to come home and is operating on ground it does not hold.
        //
        // The return policy is what makes this correct rather than accidentally correct: a Hold mission
        // (an advance) keeps the ground it takes and must never try to withdraw from it, and a Static
        // mission (a diversion, a patrol) never left its own region in the first place. The geometric
        // comparison alone was only safe while PrepareAssaultMissionStep had no exfiltration step to
        // reach; under one shared mission shape it would have marched a victorious assault back home.
        public bool MustExfiltrate =>
            MissionReturnPolicies.GetPolicy(Order.Mission.MissionType) == MissionReturnPolicy.Return
            && Order.Mission.RegionFaction.Region != MissionSquads.First().Squad.CurrentRegion;

        // True once the force has spent its operating days and should break off. A force that has to
        // exfiltrate stops a day early so the trip home still lands inside the week; one with no trip
        // home works the full week. This budget must be consulted at EVERY re-entry into an operating
        // step, not just the successful-recon branch: a force that was intercepted and stayed on
        // mission comes back from the engagement into the stealth step (see AmbushedMissionStep /
        // MeetingEngagementMissionStep returnStep), which used to test only the hard week cap. It
        // therefore spent day 7 scouting and could not begin exfiltrating until day 8 - later still
        // when the exfil itself was contested, up to the grace limit.
        public bool OperatingDaysSpent =>
            DaysElapsed >= (MustExfiltrate ? MissionDurationDays - 1 : MissionDurationDays);

        public Order Order { get; }
        public List<BattleSquad> MissionSquads { get; }
        public IReadOnlyList<PlayerSoldier> StartingPlayerParticipants { get; }

        // Battle value of the force when the MISSION began, which is a different baseline from the one
        // BattleForceEvaluator uses inside a battle.
        //
        // The same aggression percentage governs both scales, against two baselines: in-battle
        // disengagement measures against the opening value of THAT battle, and the decision to seek a
        // further battle measures against this one. The distinction is load-bearing rather than
        // pedantic - BattleSquad.StartingBattleValue resets every engagement, so day 2's fight opens
        // reading 100% remaining no matter what day 1 cost, and without a mission-scope baseline the
        // across-days rule simply would not fire.
        public long StartingMissionBattleValue { get; }

        public long CurrentMissionBattleValue => SumBattleValue(MissionSquads);

        // Defender battle value this mission has destroyed, accumulated across every engagement within
        // it. A multi-day assault needs this: RegionFaction.Garrison is not reduced until
        // MissionAftermathProcessor runs at the END of the turn, so without it every day's
        // AssembleDefendingForce call raises a fresh full-strength garrison and the assault re-fights an
        // identical battle it can never win - it can only run out of days or of tolerance.
        public long DefenderBattleValueDestroyed { get; private set; }

        // Disorganized troops overrun after an assault has destroyed the fielded defence. Kept
        // separate because ordinary engagement losses debit organized BV in aftermath.
        public long DisorganizedDefenderBattleValueDestroyed { get; private set; }

        public void RecordDefenderLosses(long battleValueDestroyed)
        {
            if (battleValueDestroyed > 0)
            {
                DefenderBattleValueDestroyed += battleValueDestroyed;
            }
        }

        public void RecordDisorganizedDefenderLosses(long battleValueDestroyed)
        {
            if (battleValueDestroyed > 0)
            {
                DisorganizedDefenderBattleValueDestroyed += battleValueDestroyed;
            }
        }

        // True once losses across the whole mission cross the order's aggression tolerance. This is what
        // stops an assault from seeking another engagement: a squad losing one member per battle never
        // trips the in-battle rule but declines the fifth fight, while a squad mauled in its first battle
        // is finished for the week. Aggression.Aggressive has no threshold and so never stops.
        //
        // Battle VALUE, not body count: losing a sergeant or a heavy weapon crosses the line faster than
        // losing a line trooper, which reads correctly since those losses really do break a squad's
        // effectiveness disproportionately.
        public bool MissionLossesExceedAggressionThreshold
        {
            get
            {
                if (StartingMissionBattleValue <= 0) return false;
                double? threshold = BattleForceEvaluator.GetEligibilityThreshold(
                    Order?.LevelOfAggression ?? Aggression.Normal);
                if (threshold == null) return false;
                double remaining = (double)CurrentMissionBattleValue / StartingMissionBattleValue;
                return remaining < threshold.Value;
            }
        }
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
        // The force completed the dedicated exfiltration step and reached its staging area. Kept
        // separate from ForceBrokeContact because an undetected return is not an evasion.
        public bool ForceReturnedToBase { get; set; }
        // Exfiltration grace expired while combat-capable troops remained. They begin the next turn
        // openly deployed in the mission region, using the same regional posture as an assault force.
        public bool ForceRemainedInTargetRegion { get; set; }
        // The force could not break contact and was lost behind enemy lines (assumed dead / gone to ground).
        public bool ForceLostContact { get; set; }
        // An embedded engagement left the force combat-ineffective and ended the mission under fire.
        public bool ForceWithdrewUnderFire { get; set; }
        // Set when this force ceased to be a viable participant in a reciprocal assault. NPC
        // survivor accounting uses it to return a failed counterattack to its staging region
        // instead of treating every surviving invader as though it secured the target.
        public bool ReciprocalAssaultDefeated { get; set; }
        // The force could not reach its objective before acting (failed to infiltrate / too many casualties).
        public bool ObjectiveAborted { get; set; }
        // The operation found nothing worthwhile to engage (a raid/ambush that turned up no target).
        public bool NoViableTarget { get; set; }
        // An assassination force reached and identified its target.
        public bool TargetLocated { get; set; }
        // The level of enemy works a sabotage mission actually knocked down, and the level that was
        // standing before it did, as measured by MissionAftermathProcessor when it applies the
        // damage. Not derivable from Impact: the charges are capped by the mission's size and then
        // by however much of the position was really there. The report renders the pair as a band
        // change rather than a raw figure, so it needs the starting level as well as the loss.
        public double SabotageDamageDealt { get; set; }
        public double SabotageDefenseLevelBefore { get; set; }
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
            StartingMissionBattleValue = SumBattleValue(playerSquads);
            DaysElapsed = 0;
            MissionsToAdd = new List<Mission>();
            MissionsToRemove = new List<Mission>();
            Log = new List<string>();
            DebriefLines = new List<MissionDebriefLine>();
            Impact = 0.0f;
            EnemiesKilled = 0;
            EnemyKillCredits = 0;
        }

        private static long SumBattleValue(IEnumerable<BattleSquad> squads) =>
            squads?
                .SelectMany(squad => squad.AbleSoldiers)
                .Sum(battleSoldier => (long)battleSoldier.Soldier.Template.BattleValue)
            ?? 0L;

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

        /// <summary>
        /// Records this context's side of a shared reciprocal-assault battle. A tactical withdrawal
        /// is deliberately not terminal here: mission-level cumulative losses decide whether the
        /// force can reform and contest the ground again tomorrow.
        /// </summary>
        public void RecordReciprocalAssaultOutcome(
            BattleHistory battleHistory,
            BattleSide missionSide,
            int enemyDeaths)
        {
            EnemiesKilled += Math.Max(0, enemyDeaths);
            // The resolver only tracks per-hit kill credit for its first side. Unique enemy deaths
            // are the stable symmetric quantity available to both linked mission reports.
            EnemyKillCredits += missionSide == BattleSide.Attacker
                ? Math.Max(0, battleHistory?.FirstSideEnemiesKilled ?? 0)
                : Math.Max(0, enemyDeaths);
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
