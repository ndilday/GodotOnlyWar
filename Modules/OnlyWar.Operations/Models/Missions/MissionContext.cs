using OnlyWar.Models.Recruitment;
using OnlyWar.Battles.Abstractions;
using OnlyWar.Operations.Contracts;
using OnlyWar.Helpers.Readiness;
using OnlyWar.Helpers.Missions;
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
        public IBattleReplay BattleHistory { get; }
        public BattleDebriefReport BattleReport { get; }
        public ushort? Day { get; }
        public string SquadName { get; }
        // A reloaded report retains the compact BattleReport but not the full replay graph. Both
        // forms should render as battle content; only the history-backed form can open a replay.
        public bool HasBattle => BattleHistory != null || BattleReport != null;

        public MissionDebriefLine(
            string text,
            IBattleReplay battleHistory = null,
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

    // Why an ambush never got to spring. Both failure points end in the same meeting engagement, and
    // without this the debrief showed only "Force accepted engagement with ..." - indistinguishable
    // from a mission that was never an ambush at all.
    public enum AmbushSpoilStage
    {
        // Either not an ambush, or the ambush was sprung as intended.
        NotSpoiled = 0,
        // Spotted while moving into position: the ambush was never set (PositionAmbushMissionStep).
        DuringSetup,
        // Set successfully, then discovered before the enemy walked in (PerformAmbushMissionStep).
        BeforeSpringing
    }

    public enum MissionAvailabilityStatus
    {
        Ready = 0,
        NoDutyReadyParticipants,
        SquadStructuralBlocker
    }

    public sealed record MissionSquadReadinessIssue(
        Squad CampaignSquad,
        MissionAvailabilityStatus Status,
        IReadOnlyList<SquadReadinessBlocker> Blockers,
        string Message)
    {
        public int SquadId => CampaignSquad?.Id ?? 0;
        public string SquadName => CampaignSquad?.Name ?? "Unknown squad";
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

        // True when the force both intends to come home and is operating on ground it does not hold.
        //
        // The return policy is what makes this correct rather than accidentally correct: a Hold mission
        // (an advance) keeps the ground it takes and must never try to withdraw from it, and a Static
        // mission (a diversion, a patrol) never left its own region in the first place. The geometric
        // comparison alone was only safe while PrepareAssaultMissionStep had no exfiltration step to
        // reach; under one shared mission shape it would have marched a victorious assault back home.
        public bool MustExfiltrate =>
            MissionReturnPolicies.GetPolicy(Order.Mission.MissionType) == MissionReturnPolicy.Return
            && Order.Mission.RegionFaction.Region != CurrentForceRegion;

        private Region CurrentForceRegion => MissionSquads
            .Select(squad => squad.CampaignCharacter?.EffectiveRegion
                ?? squad.CampaignSquad?.CurrentRegion)
            .FirstOrDefault(region => region != null);

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
        public long? StrategicInvasionForceId => Order?.StrategicInvasionForceId;
        public ChapterOperationalDoctrine OperationalDoctrine { get; }
        public MissionAvailabilityStatus AvailabilityStatus { get; private set; }
        public IReadOnlyList<SquadReadinessBlocker> AvailabilityBlockers { get; private set; }
        public List<MissionSquadReadinessIssue> ReadinessIssues { get; } = [];

        public List<OperationalMissionElement> MissionSquads { get; }
        /// <summary>
        /// Neutral participant projection used at the engagement boundary. Tactical state remains
        /// opaque while mission policy passes these contract values to the resolver.
        /// </summary>
        public IReadOnlyList<EngagementParticipant> MissionParticipants =>
            (MissionSquads ?? [])
                .Where(squad => squad?.AbleMembers.Count > 0)
                .Select(squad => squad.ToEngagementParticipant())
                .ToList();

        public IReadOnlyList<EngagementParticipant> OpposingParticipants =>
            (OpposingSquads ?? [])
                .Where(squad => squad?.AbleMembers.Count > 0)
                .Select(squad => squad.ToEngagementParticipant())
                .ToList();

        public IReadOnlyList<PlayerSoldier> StartingPlayerParticipants { get; }

        // Battle value of the force when the MISSION began, which is a different baseline from the one
        // the tactical resolver uses inside a battle.
        //
        // The same aggression percentage governs both scales, against two baselines: in-battle
        // disengagement measures against the opening value of THAT battle, and the decision to seek a
        // further battle measures against this one. The distinction is load-bearing rather than
        // pedantic - the tactical state resets its engagement baseline every fight, so day 2's fight opens
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
                double? threshold = GetMissionLossThreshold(
                    Order?.LevelOfAggression ?? Aggression.Normal);
                if (threshold == null) return false;
                double remaining = (double)CurrentMissionBattleValue / StartingMissionBattleValue;
                return remaining < threshold.Value;
            }
        }
        public ushort DaysElapsed { get; set; }
        public List<OperationalMissionElement> OpposingSquads { get; set; }
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

        // What this mission cost the Chapter, accumulated across every engagement in it
        // (Design/Reference/CasualtyRealism.md §2.3). Counted against the force that STARTED the
        // mission, so a brother can appear in exactly one of the two totals and only once.
        public int FriendlyDeaths { get; private set; }
        public int FriendlyIncapacitated { get; private set; }
        // All confirmed battlefield deaths in this mission, including non-player soldiers. The
        // The invasion campaign uses the stable commander soldier identity for deterministic tactical leader
        // death without changing the ordinary aftermath policy.
        public HashSet<int> KilledSoldierIds { get; } = [];

        /// <summary>
        /// What the Apothecary attached to this order did over the operation
        /// (Design/Reference/CasualtyRealism.md §2.6, Phase 2b). Null when the order had none.
        ///
        /// This exists because of SpecialistAttachment.md §8 trap 3: field-care reporting must remain
        /// visible whether an attached specialist is withheld or materialized as a one-person battle
        /// element. <see cref="StartingPlayerParticipants"/> is an engagement participant set, so it
        /// is not the source for order-wide field-care reporting.
        ///
        /// Order-wide, not element-wide: one order can produce several MissionContexts, and all of
        /// them carry the same report. The treatment itself ran exactly once.
        /// </summary>
        public Helpers.Medical.FieldCareReport FieldCare { get; set; }
        private readonly HashSet<int> _friendlyDeadIds = [];
        private readonly HashSet<int> _friendlyIncapacitatedIds = [];

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
        // Set by the ambush steps when a stealth check fails and the ambush degrades into a
        // straight meeting engagement, so the debrief can say which of the two happened. Like the
        // flags above it is only ever set forward: a setup that was spotted can never later be
        // discovered in position, since it never reaches PerformAmbushMissionStep.
        public AmbushSpoilStage AmbushSpoiled { get; set; }
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

        public MissionContext(
            Order order,
            List<OperationalMissionElement> playerSquads,
            List<OperationalMissionElement> opposingForces,
            ChapterOperationalDoctrine operationalDoctrine = null,
            RecruitmentProgram recruitmentProgram = null)
        {
            Order = order;
            MissionSquads = playerSquads;
            OperationalDoctrine = operationalDoctrine;
            RecruitmentProgram = recruitmentProgram;
            RefreshDutyReadyParticipants();
            StartingPlayerParticipants = playerSquads
                .SelectMany(squad => squad.AbleMembers)
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
            AvailabilityStatus = MissionAvailabilityStatus.Ready;
            AvailabilityBlockers = Array.Empty<SquadReadinessBlocker>();
        }

        /// <summary>
        /// Re-evaluates player participants at a mission-stage/engagement boundary. The elements
        /// remain mission-owned so opaque tactical state survives; only the set allowed into the
        /// next engagement changes.
        /// </summary>
        internal RecruitmentProgram RecruitmentProgram { get; }

        public void RefreshDutyReadyParticipants(
            IReadinessDecisions readiness = null,
            IEngagementElementFactory engagementElements = null)
        {
            if (readiness == null)
            {
                return;
            }
            foreach (OperationalMissionElement squad in MissionSquads
                .Where(squad => squad?.IsPlayerSquad == true
                    && (squad.FrozenParticipantIds != null || OperationalDoctrine != null)))
            {
                if (squad.CampaignCharacter != null)
                {
                    squad.RefreshEngagementParticipants(
                        readiness.EvaluateSoldier(
                            squad.CampaignCharacter,
                            OperationalDoctrine,
                            RecruitmentProgram).IsDutyReady
                            ? new[] { squad.CampaignCharacter }
                            : Array.Empty<ISoldier>());
                    engagementElements?.Update(squad);
                    continue;
                }

                SquadReadinessSnapshot snapshot = readiness.EvaluateSquad(
                    squad.CampaignSquad,
                    program: RecruitmentProgram,
                    doctrine: OperationalDoctrine);
                squad.RefreshEngagementParticipants(
                    snapshot.StructuralBlockers.Count == 0
                        ? readiness.GetDutyReadyMembers(
                                squad.CampaignSquad,
                                OperationalDoctrine,
                                RecruitmentProgram)
                            .Where(member => member is not PlayerSoldier player
                                || player.IndividualPosting == null)
                        : Array.Empty<ISoldier>());
                engagementElements?.Update(squad);
            }
        }

        public void MarkAvailabilityBlocked(
            MissionAvailabilityStatus status,
            IEnumerable<SquadReadinessBlocker> blockers = null)
        {
            AvailabilityStatus = status;
            AvailabilityBlockers = (blockers ?? Array.Empty<SquadReadinessBlocker>())
                .Where(blocker => blocker != SquadReadinessBlocker.None)
                .Distinct()
                .ToList();
        }

        public void RecordReadinessIssue(MissionSquadReadinessIssue issue)
        {
            if (issue == null || issue.CampaignSquad == null) return;
            if (ReadinessIssues.Any(existing => existing.SquadId == issue.SquadId)) return;
            ReadinessIssues.Add(issue);
        }

        private static long SumBattleValue(IEnumerable<OperationalMissionElement> squads) =>
            squads?
                .SelectMany(squad => squad.AbleMembers)
                .Sum(soldier => (long)(soldier.Template?.BattleValue ?? 0))
            ?? 0L;

        private static double? GetMissionLossThreshold(Aggression aggression) => aggression switch
        {
            Aggression.Avoid => 0.90,
            Aggression.Cautious => 0.75,
            Aggression.Normal => 0.50,
            Aggression.Attritional => 0.25,
            Aggression.Aggressive => null,
            _ => 0.50
        };

        public void AddLog(string text)
        {
            Log.Add(text);
            DebriefLines.Add(new MissionDebriefLine(
                text,
                day: GetElementDay(),
                squadName: GetElementSquadName()));
        }

        /// <summary>
        /// Records the detached result returned by the engagement boundary. The replay is retained
        /// only for the current host review workflow; mission policy consumes the value facts on the
        /// result and does not need to know the tactical resolver's implementation type.
        /// </summary>
        public void AddBattleReport(EngagementResult engagement)
        {
            if (engagement == null) return;
            string summary = engagement.Summary ?? "Engagement resolved.";
            Log.Add(summary);
            DebriefLines.Add(new MissionDebriefLine(
                summary,
                engagement.Replay,
                engagement.Report,
                GetElementDay(),
                GetElementSquadName()));
        }

        private string GetElementSquadName() =>
            IsIndependentReconElement() ? MissionSquads[0].Name : null;

        private ushort? GetElementDay() =>
            IsIndependentReconElement() ? DaysElapsed : null;

        private bool IsIndependentReconElement() =>
            Order?.Mission?.MissionType == MissionType.Recon && MissionSquads.Count == 1;

        /// <summary>Folds a detached tactical result into mission-level outcome facts.</summary>
        public void RecordBattleOutcome(EngagementResult engagement)
        {
            if (engagement == null) return;
            KilledSoldierIds.UnionWith(engagement.KilledIds);
            EnemiesKilled += engagement.EnemiesKilled;
            EnemyKillCredits += engagement.FirstSideEnemiesKilled;
            if (AssassinationTargetSoldierId is int targetId
                && engagement.KilledIds.Contains(targetId))
            {
                TargetEliminated = true;
            }

            RecordFriendlyCasualties(engagement.KilledIds, engagement.IncapacitatedIds);
            if (MissionSideWithdrewOrRouted(engagement.Outcome))
            {
                ForceWithdrewUnderFire = true;
            }
        }

        /// <summary>
        /// Folds one engagement's Chapter casualties into the mission totals. Restricted to the
        /// brothers this mission started with, so a shared battle (a reciprocal assault) never
        /// bills one mission for the other's losses, and deduplicated by soldier id because a
        /// brother who goes down on day 2 is still down on day 3 and must not be counted twice.
        /// Incapacitation is a real state change from a prior day's tally only in one direction:
        /// a man already counted as incapacitated who later dies moves to the dead total.
        /// </summary>
        private void RecordFriendlyCasualties(
            IEnumerable<int> killedSoldierIds,
            IEnumerable<int> incapacitatedSoldierIds)
        {
            HashSet<int> killed = (killedSoldierIds ?? Enumerable.Empty<int>()).ToHashSet();
            HashSet<int> incapacitated =
                (incapacitatedSoldierIds ?? Enumerable.Empty<int>()).ToHashSet();
            foreach (PlayerSoldier participant in StartingPlayerParticipants)
            {
                int id = participant.Id;
                if (killed.Contains(id))
                {
                    _friendlyIncapacitatedIds.Remove(id);
                    _friendlyDeadIds.Add(id);
                }
                else if (incapacitated.Contains(id)
                    && !_friendlyDeadIds.Contains(id))
                {
                    _friendlyIncapacitatedIds.Add(id);
                }
            }
            FriendlyDeaths = _friendlyDeadIds.Count;
            FriendlyIncapacitated = _friendlyIncapacitatedIds.Count;
        }

        /// <summary>
        /// Records this context's side of a shared reciprocal-assault battle. A tactical withdrawal
        /// is deliberately not terminal here: mission-level cumulative losses decide whether the
        /// force can reform and contest the ground again tomorrow.
        /// </summary>
        public void RecordReciprocalAssaultOutcome(
            EngagementResult engagement,
            EngagementSide missionSide,
            int enemyDeaths)
        {
            if (engagement == null) return;
            EnemiesKilled += Math.Max(0, enemyDeaths);
            EnemyKillCredits += missionSide == EngagementSide.First
                ? Math.Max(0, engagement.FirstSideEnemiesKilled)
                : Math.Max(0, enemyDeaths);
            RecordFriendlyCasualties(engagement.KilledIds, engagement.IncapacitatedIds);
        }

        public EngagementSideProfile CreateMissionEngagementProfile(EngagementRole role) =>
            new(Order?.LevelOfAggression ?? Aggression.Normal, role);

        public static EngagementSideProfile CreateOpposingEngagementProfile(
            IEnumerable<OperationalMissionElement> opposingSquads,
            EngagementRole role)
        {
            List<Aggression> aggressions = (opposingSquads ?? Enumerable.Empty<OperationalMissionElement>())
                .Select(squad => squad.CampaignCharacter?.CurrentOrder ?? squad.CampaignSquad?.CurrentOrders)
                .Where(order => order != null)
                .Select(order => order.LevelOfAggression)
                .Distinct()
                .OrderBy(aggression => aggression)
                .ToList();
            Aggression aggression = aggressions.Count == 1 ? aggressions[0] : Aggression.Normal;
            return new EngagementSideProfile(aggression, role);
        }

        private bool MissionSideWithdrewOrRouted(EngagementOutcome outcome)
        {
            if (outcome == null) return false;
            if (outcome.EndReason == EngagementEndReason.Annihilation
                && outcome.SideHoldingField == EngagementSide.First)
            {
                return false;
            }

            HashSet<int> missionSquadIds = MissionSquads.Select(squad => squad.Id).ToHashSet();
            if (outcome.DisengagedIds.Any(missionSquadIds.Contains)
                || outcome.RoutingIds.Any(missionSquadIds.Contains))
            {
                return true;
            }

            if (outcome.EndReason == EngagementEndReason.MutualDisengagement)
            {
                return true;
            }

            bool withdrawalEnding = outcome.EndReason is EngagementEndReason.Withdrawal
                or EngagementEndReason.Rout;
            return withdrawalEnding && outcome.SideHoldingField == EngagementSide.Second;
        }
    }
}

