using OnlyWar.Battles.Models;
using OnlyWar.Battles.Actions;
using OnlyWar.Domain;
using OnlyWar.Domain.Equippables;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Battles
{
    /// <summary>
    /// Battle-scoped owner of withdrawal, pursuit, contact, escape, and rear-guard lifecycle
    /// state. It mutates the live battle state and appends typed events, but does not own battle
    /// history or construct the terminal outcome.
    ///
    /// <para>The resolver remains the phase sequencer. Operations here return a terminal request
    /// when a lifecycle decision ends the battle; the resolver applies that request after the
    /// service has finished clearing live roles, so outcome construction still sees the same
    /// historical squad data.</para>
    /// </summary>
    internal sealed class BattleWithdrawalService
    {
        private readonly BattleState _state;
        private readonly BattleGridManager _grid;
        private readonly GameRulesData _rules;
        private readonly BattleRoundMetrics _roundMetrics;
        private readonly BattleMoraleService _moraleService;

        // Pursuit posture is battle-scoped and refreshed when withdrawal state changes or when
        // contact is reconsidered after a round. It is read by role-constraint preparation.
        private readonly Dictionary<BattleSide, PursuitPosture> _pursuitPostures = [];

        // Each pursuing squad's own posture from the last pursuit evaluation. _pursuitPostures
        // holds the side's summary (the most committed of these), which is what the contact and
        // rear-guard rules read; squad planning reads the squad's own entry.
        private readonly Dictionary<BattleSide, Dictionary<int, PursuitPosture>>
            _squadPursuitPostures = [];

        // Frozen current-turn pursuit pairings. Individual withdrawal escape must distinguish a
        // squad the enemy deliberately chose as its quarry from another member of the same force;
        // force-wide min/max speeds cannot answer that question.
        private readonly Dictionary<int, int> _pursuitTargetsBySquad = [];

        // Geometry is captured for gameplay on every turn. Diagnostic traces are a projection of
        // this state, not its owner: disabling BattleLog must not remove contact evidence.
        private readonly Dictionary<(int PursuerId, int QuarryId), PursuitPairProgressHistory>
            _pursuitProgressHistories = [];
        private readonly Dictionary<int, PursuitAssignmentState> _pursuitAssignmentStates = [];
        private sealed class PursuitAssignmentState
        {
            internal int? QuarryId { get; set; }
            internal bool HasBeenAssigned { get; set; }
            internal int UnproductiveSwitches { get; set; }
            internal bool HadObservedProgress { get; set; }
        }

        private sealed record PursuitGeometryStart(
            int QuarryId,
            int? PreviousQuarryId,
            BattleSquad Pursuer,
            BattleSquad Quarry,
            (float X, float Y) PursuerPosition,
            (float X, float Y) QuarryPosition,
            float Separation,
            HashSet<int> PursuerMemberIds,
            HashSet<int> QuarryMemberIds,
            IReadOnlyDictionary<int, (int X, int Y)> PursuerPositions,
            IReadOnlyDictionary<int, (int X, int Y)> QuarryPositions,
            int PursuerCount,
            int QuarryCount);
        private readonly Dictionary<int, PursuitGeometryStart> _pursuitGeometryStarts = [];

        // Separation at the beginning of the rear-guard hold, used to decide when a masked main
        // body squad has departed far enough behind its guard.
        private readonly Dictionary<int, float> _rearGuardStartingSeparation = [];

        // Set only while the resolver holds a frozen-geometry scope (post-cleanup escape, contact,
        // morale and continuation). The escape pass and the follow-shot projection then share one
        // ranged evaluator memo, one useful-range cache and one pair memo instead of each
        // rebuilding the same pursuer/quarry projections from scratch.
        private BattleAttackOpportunityProjection _frozenGeometryProjection;

        internal BattleWithdrawalService(
            BattleState state,
            BattleGridManager grid,
            GameRulesData rules,
            BattleRoundMetrics roundMetrics,
            BattleMoraleService moraleService)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _roundMetrics = roundMetrics ?? throw new ArgumentNullException(nameof(roundMetrics));
            _moraleService = moraleService
                ?? throw new ArgumentNullException(nameof(moraleService));
        }

        /// <summary>
        /// Opens a window in which no soldier moves, fires, or changes weapon state, so attack
        /// opportunity projections may be shared between the escape pass and the pursuit
        /// response. The caller must dispose the scope before the next turn's actions resolve.
        /// Squads may still disengage or change withdrawal role inside the scope; a disengaged
        /// squad simply drops out of later pair lists, and a role change alters the quarry speed
        /// that keys the pair memo.
        /// </summary>
        internal IDisposable BeginFrozenGeometryScope()
        {
            _frozenGeometryProjection = new BattleAttackOpportunityProjection(
                CreateContactRangedTargetSelector(),
                memoizePairs: true);
            return new FrozenGeometryScope(this);
        }

        private sealed class FrozenGeometryScope : IDisposable
        {
            private BattleWithdrawalService _owner;

            internal FrozenGeometryScope(BattleWithdrawalService owner) => _owner = owner;

            public void Dispose()
            {
                if (_owner == null) return;
                _owner._frozenGeometryProjection = null;
                _owner = null;
            }
        }

        private BattleAttackOpportunityProjection CreateAttackOpportunityProjection() =>
            _frozenGeometryProjection
            ?? new BattleAttackOpportunityProjection(CreateContactRangedTargetSelector());

        /// <summary>
        /// Replaces, rather than appends to, the current turn's deliberate pursuit pairings. A
        /// turn with no pursuit decisions therefore clears the previous turn's quarry choices.
        /// </summary>
        internal void ReplaceCurrentTurnPursuitPairings(
            IEnumerable<KeyValuePair<int, int>> pairings)
        {
            Dictionary<int, int> previous = new(_pursuitTargetsBySquad);
            _pursuitGeometryStarts.Clear();
            _pursuitTargetsBySquad.Clear();
            if (pairings == null)
            {
                MarkUnassignedPursuers();
                return;
            }

            foreach (KeyValuePair<int, int> pairing in pairings)
            {
                _pursuitTargetsBySquad[pairing.Key] = pairing.Value;
            }

            List<BattleSquad> squads = GetActiveSquads(BattleSide.Attacker)
                .Concat(GetActiveSquads(BattleSide.Opposing))
                .ToList();
            foreach (var pair in _pursuitTargetsBySquad.OrderBy(pair => pair.Key))
            {
                BattleSquad pursuer = squads.FirstOrDefault(squad => squad.Id == pair.Key);
                BattleSquad quarry = squads.FirstOrDefault(squad => squad.Id == pair.Value);
                if (pursuer == null || quarry == null) continue;

                PreparePairHistory(pursuer, quarry);
                _pursuitGeometryStarts[pair.Key] = CaptureGeometryStart(
                    pair.Value,
                    previous.TryGetValue(pair.Key, out int old) ? old : null,
                    pursuer,
                    quarry);
            }
            MarkUnassignedPursuers();
        }

        // Called after movement, before cleanup/escape can remove the assigned squads.
        // The history is gameplay state, while the optional records below are its diagnostics.
        internal void LogPursuitProgress(IReadOnlyCollection<IAction> movementActions = null)
        {
            // Closing wrappers retain their concrete moves. Deduplicate in case the executor
            // also supplied those moves directly.
            var moves = (movementActions ?? []).OfType<MoveAction>()
                .Concat((movementActions ?? []).OfType<SquadClosingMoveAction>()
                    .SelectMany(action => action.ResolvedMovementActions).OfType<MoveAction>())
                .Distinct().ToList();
            foreach (var entry in _pursuitGeometryStarts.OrderBy(pair => pair.Key))
            {
                PursuitGeometryStart start = entry.Value;
                HashSet<int> pursuerMemberIds = MemberIds(start.Pursuer);
                HashSet<int> quarryMemberIds = MemberIds(start.Quarry);
                bool membershipStable = start.PursuerMemberIds.SetEquals(pursuerMemberIds)
                    && start.QuarryMemberIds.SetEquals(quarryMemberIds);
                IReadOnlyDictionary<int, (int X, int Y)> pursuerPositions =
                    Positions(start.Pursuer);
                IReadOnlyDictionary<int, (int X, int Y)> quarryPositions =
                    Positions(start.Quarry);
                var pursuerEnd = BattleEngagementFrameBuilder.Centroid(start.Pursuer);
                var quarryEnd = BattleEngagementFrameBuilder.Centroid(start.Quarry);
                float end = MinimumSquadSeparation(start.Pursuer, start.Quarry);
                bool endpointsPlaced = start.PursuerMemberIds.All(
                        memberId => start.PursuerPositions.ContainsKey(memberId))
                    && start.QuarryMemberIds.All(
                        memberId => start.QuarryPositions.ContainsKey(memberId))
                    && pursuerMemberIds.All(memberId => pursuerPositions.ContainsKey(memberId))
                    && quarryMemberIds.All(memberId => quarryPositions.ContainsKey(memberId));
                string validityReason = !membershipStable
                    ? "membership_changed"
                    : !endpointsPlaced
                        ? "unplaced_endpoint"
                        : !IsFinite(start.Separation) || !IsFinite(end)
                            ? "invalid_geometry"
                            : "valid";
                bool validGeometry = validityReason == "valid";
                MoveStats pursuerMoves = MeasureMoves(
                    moves,
                    start.PursuerMemberIds);
                MoveStats quarryMoves = MeasureMoves(
                    moves,
                    start.QuarryMemberIds);
                float pursuerActualDisplacement = ActualDisplacement(
                    start.PursuerPositions,
                    start.Pursuer);
                float quarryActualDisplacement = ActualDisplacement(
                    start.QuarryPositions,
                    start.Quarry);
                float pursuerSpeed = DeclaredTurnSpeed(start.Pursuer);
                float quarrySpeed = DeclaredTurnSpeed(start.Quarry);
                float closing = pursuerSpeed - quarrySpeed;
                PursuitPairProgressHistory history = _pursuitProgressHistories.GetValueOrDefault(
                    (entry.Key, start.QuarryId));
                if (history != null)
                {
                    if (validGeometry)
                    {
                        history.SetMembership(start.Pursuer, start.Quarry);
                        history.Add(new PursuitProgressSample(
                            _state.TurnNumber,
                            start.Separation,
                            end,
                            pursuerActualDisplacement,
                            quarryActualDisplacement,
                            pursuerMoves.PlannedDisplacement,
                            quarryMoves.PlannedDisplacement,
                            pursuerMoves.SuccessfulMoveCount + quarryMoves.SuccessfulMoveCount,
                            pursuerMoves.FailedMoveCount + quarryMoves.FailedMoveCount));
                        if (history.HasObservedClosingProgress
                            && _pursuitAssignmentStates.TryGetValue(
                                entry.Key,
                                out PursuitAssignmentState assignment))
                        {
                            assignment.HadObservedProgress = true;
                            assignment.UnproductiveSwitches = 0;
                        }
                    }
                    else
                    {
                        // A member replacement or an invalid/unplaced endpoint makes the sample
                        // incomparable. Do not let a count-only or stale-nearest sample bridge it.
                        history.ResetForMembership(
                            start.Pursuer,
                            start.Quarry,
                            _state.TurnNumber,
                            validityReason);
                    }
                }

                if (BattleLog.IsEnabled)
                {
                    foreach (MoveAction move in moves.Where(move =>
                        start.PursuerMemberIds.Contains(move.ActorId)
                        || start.QuarryMemberIds.Contains(move.ActorId)))
                    {
                        string role = start.PursuerMemberIds.Contains(move.ActorId)
                            ? "pursuer"
                            : "quarry";
                        BattleLog.Write(new BattleDecisionTrace("PURSUIT_MOVE_RESULT",
                        [
                            BattleDecisionTrace.Field("turn", _state.TurnNumber),
                            BattleDecisionTrace.Field("pursuer", entry.Key),
                            BattleDecisionTrace.Field("quarry", start.QuarryId),
                            BattleDecisionTrace.Field("role", role),
                            BattleDecisionTrace.Field("soldier", move.ActorId),
                            BattleDecisionTrace.Field("origin", move.Origin),
                            BattleDecisionTrace.Field("destination", move.Destination),
                            BattleDecisionTrace.Field("planned_displacement",
                                EngagementExchangeModel.Distance(move.Origin, move.Destination)),
                            BattleDecisionTrace.Field("succeeded", move.Succeeded)
                        ]).Render());
                    }
                    BattleLog.Write(new BattleDecisionTrace("PURSUIT_PROGRESS",
                    [
                        BattleDecisionTrace.Field("turn", _state.TurnNumber),
                        BattleDecisionTrace.Field("pursuer", entry.Key),
                        BattleDecisionTrace.Field("quarry", start.QuarryId),
                        BattleDecisionTrace.Field("previous_quarry", start.PreviousQuarryId),
                        BattleDecisionTrace.Field("assignment_changed", start.PreviousQuarryId != start.QuarryId),
                        BattleDecisionTrace.Field("pursuer_start", start.PursuerPosition),
                        BattleDecisionTrace.Field("pursuer_end", pursuerEnd),
                        BattleDecisionTrace.Field("quarry_start", start.QuarryPosition),
                        BattleDecisionTrace.Field("quarry_end", quarryEnd),
                        BattleDecisionTrace.Field("pursuer_displacement", pursuerActualDisplacement),
                        BattleDecisionTrace.Field("quarry_displacement", quarryActualDisplacement),
                        BattleDecisionTrace.Field("pursuer_planned_displacement",
                            pursuerMoves.PlannedDisplacement),
                        BattleDecisionTrace.Field("quarry_planned_displacement",
                            quarryMoves.PlannedDisplacement),
                        BattleDecisionTrace.Field("failed_moves",
                            pursuerMoves.FailedMoveCount + quarryMoves.FailedMoveCount),
                         BattleDecisionTrace.Field("geometry_valid", validGeometry),
                         BattleDecisionTrace.Field("sample_validity_reason", validityReason),
                         BattleDecisionTrace.Field("separation_start", start.Separation),
                         BattleDecisionTrace.Field("separation_end", validGeometry ? end : null),
                         BattleDecisionTrace.Field("separation_gain",
                             validGeometry ? start.Separation - end : null),
                         BattleDecisionTrace.Field("measured_progress",
                             validGeometry ? start.Separation - end : null),
                        BattleDecisionTrace.Field("pursuer_speed", pursuerSpeed),
                        BattleDecisionTrace.Field("quarry_speed", quarrySpeed),
                        // These values remain diagnostic only. Contact uses the rolling geometry
                        // evidence above and never the declared-speed delta.
                        BattleDecisionTrace.Field("declared_speed_delta", closing),
                        BattleDecisionTrace.Field("declared_speed_not_evidence", true),
                        BattleDecisionTrace.Field("pursuer_count_start", start.PursuerCount),
                        BattleDecisionTrace.Field("pursuer_count_end", pursuerMemberIds.Count),
                        BattleDecisionTrace.Field("quarry_count_start", start.QuarryCount),
                        BattleDecisionTrace.Field("quarry_count_end", quarryMemberIds.Count),
                         BattleDecisionTrace.Field("history_samples", history?.Samples.Count),
                         BattleDecisionTrace.Field("history_window_length",
                             PursuitProgressPolicy.HistoryLength),
                         BattleDecisionTrace.Field("history_assignment_start_turn",
                             history?.AssignmentStartTurn),
                         BattleDecisionTrace.Field("history_window",
                             history == null
                                 ? "none"
                                 : $"{history.Samples.Count}/{PursuitProgressPolicy.HistoryLength}"),
                         BattleDecisionTrace.Field("history_gain", history?.RollingSeparationGain),
                         BattleDecisionTrace.Field("history_tolerance",
                             PursuitProgressPolicy.ProgressTolerance),
                         BattleDecisionTrace.Field("history_sample_valid", history?.LastSampleWasValid),
                         BattleDecisionTrace.Field("history_validity_reason",
                             history?.LastSampleValidityReason),
                         BattleDecisionTrace.Field("history_reset_reason",
                             history?.LastResetReason),
                         BattleDecisionTrace.Field("startup_grace", history?.HasStartupGrace),
                         BattleDecisionTrace.Field("startup_status",
                             history == null
                                 ? "no_history"
                                 : history.HasStartupGrace ? "grace" : "expired"),
                         BattleDecisionTrace.Field("observed_progress_contact_evidence",
                             history?.HasObservedClosingProgress),
                         BattleDecisionTrace.Field("contact_maintenance_evidence_source",
                             "observed_geometric_progress")
                     ]).Render());
                }
            }
            _pursuitGeometryStarts.Clear();
        }

        private PursuitPairProgressHistory PreparePairHistory(
            BattleSquad pursuer,
            BattleSquad quarry)
        {
            (int PursuerId, int QuarryId) key = (pursuer.Id, quarry.Id);
            _pursuitAssignmentStates.TryGetValue(
                pursuer.Id,
                out PursuitAssignmentState assignment);
            assignment ??= new PursuitAssignmentState();

            bool firstAssignment = !assignment.HasBeenAssigned;
            bool assignmentChanged = firstAssignment || assignment.QuarryId != quarry.Id;
            if (assignmentChanged)
            {
                bool previousPairWasProductive = assignment.HadObservedProgress;
                if (!previousPairWasProductive
                    && assignment.QuarryId is int oldQuarryId
                    && _pursuitProgressHistories.TryGetValue(
                        (pursuer.Id, oldQuarryId),
                        out PursuitPairProgressHistory oldHistory))
                {
                    previousPairWasProductive = oldHistory.HasObservedClosingProgress;
                }

                if (!firstAssignment)
                {
                    assignment.UnproductiveSwitches = previousPairWasProductive
                        ? 0
                        : assignment.UnproductiveSwitches + 1;
                }
                assignment.QuarryId = quarry.Id;
                assignment.HasBeenAssigned = true;
                assignment.HadObservedProgress = false;
                InvalidatePursuerPairHistories(pursuer.Id);
            }

            _pursuitAssignmentStates[pursuer.Id] = assignment;
            bool startupAllowed = firstAssignment
                || assignment.UnproductiveSwitches
                    <= PursuitProgressPolicy.MaxUnproductiveAssignmentSwitches;
            PursuitPairProgressHistory history;
            if (!_pursuitProgressHistories.TryGetValue(key, out history))
            {
                history = new PursuitPairProgressHistory(
                    pursuer.Id,
                    quarry.Id,
                    pursuer,
                    quarry,
                    _state.TurnNumber,
                    startupAllowed);
                _pursuitProgressHistories[key] = history;
            }
            else if (!history.MembershipMatches(pursuer, quarry))
            {
                history.ResetForMembership(pursuer, quarry, _state.TurnNumber);
                history.StartupAllowed = startupAllowed;
                assignment.HadObservedProgress = false;
            }
            else
            {
                history.StartupAllowed = startupAllowed;
            }

            return history;
        }

        private PursuitGeometryStart CaptureGeometryStart(
            int quarryId,
            int? previousQuarryId,
            BattleSquad pursuer,
            BattleSquad quarry) =>
            new(
                quarryId,
                previousQuarryId,
                pursuer,
                quarry,
                BattleEngagementFrameBuilder.Centroid(pursuer),
                BattleEngagementFrameBuilder.Centroid(quarry),
                MinimumSquadSeparation(pursuer, quarry),
                MemberIds(pursuer),
                MemberIds(quarry),
                Positions(pursuer),
                Positions(quarry),
                pursuer.AbleSoldiers.Count,
                quarry.AbleSoldiers.Count);

        private void MarkUnassignedPursuers()
        {
            HashSet<int> assignedPursuers = _pursuitTargetsBySquad.Keys.ToHashSet();
            foreach ((int pursuerId, PursuitAssignmentState assignment) in
                _pursuitAssignmentStates)
            {
                if (assignedPursuers.Contains(pursuerId)) continue;
                assignment.QuarryId = null;
                assignment.HadObservedProgress = false;
                InvalidatePursuerPairHistories(pursuerId);
            }
        }

        private void InvalidatePursuerPairHistories(int pursuerId)
        {
            foreach ((int PursuerId, int QuarryId) key in _pursuitProgressHistories.Keys
                .Where(key => key.PursuerId == pursuerId)
                .ToList())
            {
                _pursuitProgressHistories.Remove(key);
            }
        }

        private static HashSet<int> MemberIds(BattleSquad squad) =>
            squad.AbleSoldiers.Select(soldier => soldier.Soldier.Id).ToHashSet();

        private static IReadOnlyDictionary<int, (int X, int Y)> Positions(BattleSquad squad) =>
            squad.AbleSoldiers
                .Where(soldier => soldier.TopLeft.HasValue)
                .ToDictionary(
                    soldier => soldier.Soldier.Id,
                    soldier => (
                        soldier.TopLeft.Value.Item1,
                        soldier.TopLeft.Value.Item2));

        private static float ActualDisplacement(
            IReadOnlyDictionary<int, (int X, int Y)> starts,
            BattleSquad squad)
        {
            List<float> displacement = squad.AbleSoldiers
                .Where(soldier => starts.ContainsKey(soldier.Soldier.Id)
                    && soldier.TopLeft.HasValue)
                .Select(soldier => Distance(
                    starts[soldier.Soldier.Id],
                    (soldier.TopLeft.Value.Item1, soldier.TopLeft.Value.Item2)))
                .ToList();
            return displacement.Count == 0 ? 0 : displacement.Average();
        }

        private readonly record struct MoveStats(
            float PlannedDisplacement,
            int SuccessfulMoveCount,
            int FailedMoveCount);

        private static MoveStats MeasureMoves(
            IReadOnlyCollection<MoveAction> moves,
            IReadOnlySet<int> memberIds)
        {
            float planned = 0;
            int successful = 0;
            int failed = 0;
            foreach (MoveAction move in moves.Where(move => memberIds.Contains(move.ActorId)))
            {
                planned += Distance(move.Origin, move.Destination);
                if (move.Succeeded) successful++;
                else failed++;
            }
            return new MoveStats(planned, successful, failed);
        }

        private static float Distance((int X, int Y) first, (int X, int Y) second)
        {
            int dx = first.X - second.X;
            int dy = first.Y - second.Y;
            return MathF.Sqrt((dx * dx) + (dy * dy));
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        /// <summary>
        /// The side's pursuit posture: the most committed of its squads' postures. BreakOff means
        /// every squad has broken off; Press means at least one squad is pressing.
        /// </summary>
        internal PursuitPosture GetPursuitPosture(BattleSide side) =>
            _pursuitPostures.GetValueOrDefault(side, PursuitPosture.BreakOff);

        /// <summary>
        /// One pursuing squad's own posture. A squad the last evaluation did not cover (it was
        /// routing, or unplaced) falls back to the side's summary posture.
        /// </summary>
        internal PursuitPosture GetSquadPursuitPosture(BattleSide side, int squadId) =>
            _squadPursuitPostures.TryGetValue(side, out Dictionary<int, PursuitPosture> postures)
                && postures.TryGetValue(squadId, out PursuitPosture posture)
                    ? posture
                    : GetPursuitPosture(side);

        /// <summary>
        /// Builds the serial role mask consumed by the planning pass. Cover selection and its
        /// incumbent/heading mutations stay on the lifecycle owner, before any decision workers
        /// begin.
        /// </summary>
        internal void BuildRoleConstraints(
            BattleSide side,
            IReadOnlyCollection<BattleSquad> friendlySquads,
            IReadOnlyCollection<BattleSquad> enemySquads,
            IDictionary<int, EngagementRoleConstraint> constraints,
            ICollection<BattleEvent> events)
        {
            ArgumentNullException.ThrowIfNull(friendlySquads);
            ArgumentNullException.ThrowIfNull(enemySquads);
            ArgumentNullException.ThrowIfNull(constraints);
            ArgumentNullException.ThrowIfNull(events);

            BattleSideState sideState = GetSideState(side);
            List<BattleSquad> active = friendlySquads
                .Where(squad => squad.Status == BattleSquadStatus.Active
                    && squad.AbleSoldiers.Count > 0)
                .OrderBy(squad => squad.Id)
                .ToList();
            foreach (BattleSquad squad in active.Where(squad =>
                squad.WithdrawalRole == WithdrawalRole.Routing
                || squad.MoraleState == MoraleState.Routing))
            {
                constraints[squad.Id] = new EngagementRoleConstraint(
                    EngagementSquadRole.Routing);
            }
            List<BattleSquad> disciplined = active
                .Where(squad => !constraints.ContainsKey(squad.Id))
                .ToList();
            if (disciplined.Count == 0) return;

            if (sideState.Intent == BattleSideIntent.FightingWithdrawal)
            {
                sideState.WithdrawalHeading ??= BattleForcePlanner.SelectWithdrawalHeading(
                    disciplined, enemySquads);
                if (disciplined.Count < 2)
                {
                    sideState.CoveringSquadId = null;
                    foreach (BattleSquad squad in disciplined)
                    {
                        constraints[squad.Id] = new EngagementRoleConstraint(
                            EngagementSquadRole.Bound,
                            sideState.WithdrawalHeading);
                    }
                    return;
                }
                BattleForcePlanner.CoverAssignment cover = BattleForcePlanner.SelectCover(
                    BattleForcePlanner.BuildCoverCandidates(disciplined, enemySquads),
                    sideState.CoveringSquadId);
                sideState.CoveringSquadId = cover.SquadId;
                LogCoverAssignment(side, sideState, cover, events);
                foreach (BattleSquad squad in disciplined)
                {
                    constraints[squad.Id] = new EngagementRoleConstraint(
                        cover.SquadId == squad.Id
                            ? EngagementSquadRole.Cover
                            : EngagementSquadRole.Bound,
                        sideState.WithdrawalHeading);
                }
                return;
            }
            if (sideState.Intent == BattleSideIntent.RearGuardWithdrawal)
            {
                sideState.WithdrawalHeading ??= BattleForcePlanner.SelectWithdrawalHeading(
                    disciplined, enemySquads);
                sideState.CoveringSquadId = null;
                foreach (BattleSquad squad in disciplined)
                {
                    constraints[squad.Id] = new EngagementRoleConstraint(
                        sideState.RearGuardSquadId == squad.Id
                            ? EngagementSquadRole.RearGuard
                            : EngagementSquadRole.Bound,
                        sideState.WithdrawalHeading);
                }
                return;
            }
            if (sideState.Intent == BattleSideIntent.Pursuing)
            {
                float quarryRunSpeed = enemySquads.Count == 0
                    ? 0
                    : enemySquads.Min(squad => squad.GetSquadMove());
                foreach (BattleSquad squad in disciplined)
                {
                    constraints[squad.Id] = new EngagementRoleConstraint(
                        GetSquadPursuitPosture(side, squad.Id) switch
                        {
                            PursuitPosture.BreakOff => EngagementSquadRole.BreakOff,
                            PursuitPosture.Standoff => EngagementSquadRole.Standoff,
                            PursuitPosture.Follow => EngagementSquadRole.Follow,
                            PursuitPosture.Press => EngagementSquadRole.Press,
                            _ => EngagementSquadRole.Pursuit
                        },
                        QuarryRunSpeed: quarryRunSpeed,
                        RoleTargets: enemySquads);
                }
                return;
            }
            foreach (BattleSquad squad in disciplined)
            {
                constraints[squad.Id] = new EngagementRoleConstraint(
                    EngagementSquadRole.Normal);
            }
        }

        internal BattleTerminalRequest EvaluateContinuation(
            BattleForceMetrics attacker,
            BattleForceMetrics opposing,
            List<BattleEvent> events)
        {
            ArgumentNullException.ThrowIfNull(attacker);
            ArgumentNullException.ThrowIfNull(opposing);
            ArgumentNullException.ThrowIfNull(events);

            BattleForceEvaluationResult attackerResult = BattleForceEvaluator.Evaluate(new(
                _state.TurnNumber,
                "first",
                _state.AttackerSide.Aggression,
                attacker,
                opposing,
                IsWithdrawalIntent(_state.AttackerSide.Intent)));
            BattleForceEvaluationResult opposingResult = BattleForceEvaluator.Evaluate(new(
                _state.TurnNumber,
                "second",
                _state.OpposingSide.Aggression,
                opposing,
                attacker,
                IsWithdrawalIntent(_state.OpposingSide.Intent)));

            bool attackerStarts = attackerResult.ShouldWithdraw
                && !IsWithdrawalIntent(_state.AttackerSide.Intent);
            bool opposingStarts = opposingResult.ShouldWithdraw
                && !IsWithdrawalIntent(_state.OpposingSide.Intent);
            if (attackerStarts && opposingStarts)
            {
                DisengageForce(BattleSide.Attacker, events, "Both forces elected to withdraw.");
                DisengageForce(BattleSide.Opposing, events, "Both forces elected to withdraw.");
                return new BattleTerminalRequest(BattleEndReason.MutualDisengagement, null);
            }

            if (attackerStarts)
            {
                BattleTerminalRequest terminal = BeginWithdrawal(BattleSide.Attacker, events);
                if (terminal != null) return terminal;
            }
            if (opposingStarts)
            {
                BattleTerminalRequest terminal = BeginWithdrawal(BattleSide.Opposing, events);
                if (terminal != null) return terminal;
            }

            TryAssignRearGuard(BattleSide.Attacker, events);
            TryAssignRearGuard(BattleSide.Opposing, events);
            return null;
        }

        /// <summary>
        /// Starts a voluntary withdrawal. The rout path uses EvaluatePursuitResponse directly
        /// because morale has already committed the side intent and emitted its ordered event.
        /// </summary>
        internal BattleTerminalRequest BeginWithdrawal(
            BattleSide withdrawingSide,
            List<BattleEvent> events)
        {
            ArgumentNullException.ThrowIfNull(events);
            BattleSide pursuingSide = Opposite(withdrawingSide);
            BattleSideState withdrawing = GetSideState(withdrawingSide);
            withdrawing.Intent = BattleSideIntent.FightingWithdrawal;
            withdrawing.WithdrawalStartedTurn ??= _state.TurnNumber;
            withdrawing.WithdrawalHeading ??= BattleForcePlanner.SelectWithdrawalHeading(
                GetActiveSquads(withdrawingSide),
                GetActiveSquads(pursuingSide));
            events.Add(new BattleEvent(
                BattleEventType.WithdrawalOrdered,
                _state.TurnNumber,
                withdrawingSide,
                null,
                GetActiveSquads(withdrawingSide).Select(squad => squad.Id),
                $"{SideName(withdrawingSide)} ordered a fighting withdrawal."));

            DisengageBurrowers(withdrawingSide, events);
            if (GetActiveSquads(withdrawingSide).Count == 0)
            {
                return CompleteWithdrawal(withdrawingSide, events);
            }

            return EvaluatePursuitResponse(withdrawingSide, events);
        }

        /// <summary>
        /// Re-evaluates how each of the enemy's squads pursues a withdrawing or routing side
        /// (<see cref="BattlePursuitPlanner"/>, one decision per squad from that squad's own
        /// pairs). The side's stored posture is the most committed of its squads' postures, so it
        /// is BreakOff only when every squad breaks off -- which ends the pursuit and returns a
        /// terminal request after the withdrawal has been cleared; callers apply that request
        /// through the resolver's outcome owner.
        /// </summary>
        internal BattleTerminalRequest EvaluatePursuitResponse(
            BattleSide withdrawingSide,
            List<BattleEvent> events)
        {
            ArgumentNullException.ThrowIfNull(events);
            BattleSide pursuingSide = Opposite(withdrawingSide);
            BattleSideState pursuing = GetSideState(pursuingSide);
            BattleForceMetrics withdrawalMetrics = _roundMetrics.BuildMetrics(withdrawingSide);
            // The withdrawer "returns fire" only if some non-routing element still carries a
            // ranged weapon: a fully routed side shoots at no one, and a melee-only force
            // never could. Routing roles are already set when a rout triggers this
            // evaluation, so the flag reads the current turn's reality.
            bool withdrawerReturnsFire = GetActiveSquads(withdrawingSide).Any(
                squad => squad.WithdrawalRole != WithdrawalRole.Routing
                    && squad.AbleSoldiers.Any(
                        soldier => soldier.EquippedRangedWeapons.Count > 0));

            Dictionary<int, PursuitPosture> squadPostures = [];
            foreach (BattleSquad squad in GetActiveSquads(pursuingSide)
                .Where(IsProjectableSquad)
                .Where(squad => squad.WithdrawalRole != WithdrawalRole.Routing
                    && squad.MoraleState != MoraleState.Routing)
                .OrderBy(squad => squad.Id))
            {
                squadPostures[squad.Id] = EvaluateSquadPursuit(
                    squad,
                    pursuingSide,
                    withdrawingSide,
                    pursuing.Aggression,
                    withdrawalMetrics,
                    withdrawerReturnsFire).Posture;
            }
            _squadPursuitPostures[pursuingSide] = squadPostures;
            PursuitPosture summary = squadPostures.Values
                .DefaultIfEmpty(PursuitPosture.BreakOff)
                .Max();

            PursuitPosture? previous = _pursuitPostures.TryGetValue(
                pursuingSide,
                out PursuitPosture stored) ? stored : null;
            _pursuitPostures[pursuingSide] = summary;
            if (summary == PursuitPosture.BreakOff)
            {
                events.Add(new BattleEvent(
                    BattleEventType.PursuitEnded,
                    _state.TurnNumber,
                    pursuingSide,
                    null,
                    null,
                    $"{SideName(pursuingSide)} declined pursuit."));
                return CompleteWithdrawal(withdrawingSide, events);
            }

            StandDownBrokenOffSquads(pursuingSide, squadPostures, events);

            pursuing.Intent = BattleSideIntent.Pursuing;
            if (previous == summary) return null;
            events.Add(new BattleEvent(
                BattleEventType.PursuitStarted,
                _state.TurnNumber,
                pursuingSide,
                null,
                GetActiveSquads(withdrawingSide).Select(squad => squad.Id),
                previous.HasValue
                    ? $"{SideName(pursuingSide)} switched to a {summary} pursuit."
                    : $"{SideName(pursuingSide)} began a {summary} pursuit."));
            return null;
        }

        /// <summary>
        /// A squad that breaks off while the rest of its force keeps up the pursuit has nothing
        /// left to do on the field, so it leaves it. Observed 2026-09-22 (Grist Nine Epsilon): 22
        /// of 33 marine squads broke off and stood idle for hundreds of turns while 11 others kept
        /// the pursuit alive. The squad is disengaged but recorded as stood down, not withdrawn
        /// (<see cref="BattleSideState.StoodDownSquadIds"/>). When EVERY squad breaks off the
        /// pursuit ends instead, through <see cref="CompleteWithdrawal"/>, and none stands down.
        /// </summary>
        private void StandDownBrokenOffSquads(
            BattleSide pursuingSide,
            Dictionary<int, PursuitPosture> squadPostures,
            List<BattleEvent> events)
        {
            BattleSideState pursuing = GetSideState(pursuingSide);
            foreach (BattleSquad squad in GetActiveSquads(pursuingSide)
                .Where(squad => squadPostures.GetValueOrDefault(squad.Id, PursuitPosture.Press)
                    == PursuitPosture.BreakOff)
                .OrderBy(squad => squad.Id)
                .ToList())
            {
                pursuing.StoodDownBattleValue += CurrentBattleValue(squad);
                pursuing.StoodDownSquadIds.Add(squad.Id);
                squadPostures.Remove(squad.Id);
                DisengageSquad(pursuingSide, squad, events, "stood down from the pursuit");
            }
        }

        /// <summary>
        /// One pursuing squad's posture from its own pairs: its fastest way into melee contact, its
        /// earliest useful shot, its equipment doctrine, and the side's aggression.
        /// </summary>
        private BattlePursuitPlanner.Result EvaluateSquadPursuit(
            BattleSquad squad,
            BattleSide pursuingSide,
            BattleSide withdrawingSide,
            OnlyWar.Domain.Orders.Aggression aggression,
            BattleForceMetrics withdrawalMetrics,
            bool withdrawerReturnsFire)
        {
            BattleInterceptionProjection.AggregateResult pressProjection =
                ProjectPressingContact(pursuingSide, withdrawingSide, squad);
            if (BattleLog.IsEnabled)
            {
                List<KeyValuePair<string, string>> projectionFields =
                [
                    BattleDecisionTrace.Field("turn", _state.TurnNumber),
                    BattleDecisionTrace.Field("side", pursuingSide == BattleSide.Attacker
                        ? "first" : "second"),
                    BattleDecisionTrace.Field("squad", squad.Id),
                    BattleDecisionTrace.Field("scope", "pair_aggregate"),
                    BattleDecisionTrace.Field("estimate_kind", "hypothetical_press_to_contact"),
                    BattleDecisionTrace.Field("clock", "movement_and_next_attack_phase")
                ];
                BattleProjectionDiagnosticTrace.AddInterception(
                    projectionFields,
                    "press",
                    pressProjection,
                    "melee_contact_allowance");
                BattleLog.Write(new BattleDecisionTrace(
                    "PRESS_CONTACT_PROJECTION",
                    projectionFields).Render());
            }
            // The planner compares the first attack phase in which contact could be used. A
            // nonzero movement contact happens after this turn's attack phase, so the pure
            // movement clock is kept separately for diagnostics and documentation.
            float pressTurns = pressProjection.EarliestAttackableContactTurns;
            PursuitDoctrine doctrine = BattleEngagementFrameBuilder.ClassifyPursuitDoctrine(squad);
            bool followNeeded = BattlePursuitPlanner.NeedsFollowProjection(
                aggression,
                doctrine,
                withdrawerReturnsFire,
                pressTurns,
                pressProjection.CanReachContactThisTurn);
            float? projectedFollowShotTurns = null;
            BattleAttackOpportunity followTraceOpportunity = null;
            if (followNeeded)
            {
                (projectedFollowShotTurns,
                    BattleAttackOpportunityProjection.AggregateResult followProjection) =
                    ProjectedFollowShotTurns(
                        pursuingSide,
                        withdrawingSide,
                        squad,
                        pressTurns,
                        pressProjection.CanReachContactThisTurn
                            || pressTurns <= BattlePursuitPlanner.MaximumChaseTurns);
                if (followProjection.PairCount > 0)
                {
                    followTraceOpportunity = followProjection.Ranged.IsReachable
                        ? followProjection.Ranged
                        : followProjection.Earliest;
                }
            }
            bool hasFollowPair = followTraceOpportunity != null;
            return BattlePursuitPlanner.Evaluate(new(
                _state.TurnNumber,
                pursuingSide == BattleSide.Attacker,
                aggression,
                squad.AbleSoldiers.Count,
                withdrawalMetrics.AbleSoldierCount,
                PressingMovementSpeed(squad),
                withdrawalMetrics.SlowestMainBodySquadSpeed,
                pressTurns,
                projectedFollowShotTurns,
                withdrawerReturnsFire,
                pressProjection.CanReachContactThisTurn,
                pressProjection.PursuerSquadId,
                pressProjection.QuarrySquadId,
                float.IsPositiveInfinity(pressProjection.EarliestContactTurns)
                    ? null
                    : pressProjection.EarliestContactTurns,
                hasFollowPair ? followTraceOpportunity.PursuerSquadId : null,
                hasFollowPair ? followTraceOpportunity.QuarrySquadId : null,
                hasFollowPair ? followTraceOpportunity.Mode : null,
                hasFollowPair ? followTraceOpportunity.Preparation : null,
                hasFollowPair ? followTraceOpportunity.RequiresMovement : null,
                doctrine,
                squad.Id,
                followNeeded));
        }

        internal BattleTerminalRequest ResolveUnpursuedWithdrawalEscapes(
            List<BattleEvent> events)
        {
            ArgumentNullException.ThrowIfNull(events);
            ResolveUnpursuedWithdrawalEscapes(BattleSide.Attacker, events);
            ResolveUnpursuedWithdrawalEscapes(BattleSide.Opposing, events);
            return null;
        }

        internal BattleTerminalRequest ResolveContactBreaks(List<BattleEvent> events)
        {
            ArgumentNullException.ThrowIfNull(events);
            BattleTerminalRequest terminal = ResolveContactBreak(BattleSide.Attacker, events);
            if (terminal != null) return terminal;
            return ResolveContactBreak(BattleSide.Opposing, events);
        }

        private void ResolveUnpursuedWithdrawalEscapes(
            BattleSide withdrawingSide,
            List<BattleEvent> events)
        {
            if (!IsWithdrawalIntent(GetSideState(withdrawingSide).Intent)) return;

            BattleSide pursuingSide = Opposite(withdrawingSide);
            List<BattleSquad> pursuers = GetActiveSquads(pursuingSide).ToList();
            HashSet<int> pursuedSquadIds = _pursuitTargetsBySquad
                .Where(pair => pursuers.Any(squad => squad.Id == pair.Key))
                .Select(pair => pair.Value)
                .ToHashSet();
            List<BattleSquad> projectablePursuers = pursuers
                .Where(IsProjectableSquad)
                .OrderBy(squad => squad.Id)
                .ToList();
            List<BattleSquad> projectableQuarries = GetActiveSquads(withdrawingSide)
                .Where(IsProjectableSquad)
                .OrderBy(squad => squad.Id)
                .ToList();
            ValueTuple<int, int> headingVector = GetWithdrawalHeadingVector(
                withdrawingSide,
                projectableQuarries,
                projectablePursuers);
            BattleAttackOpportunityProjection opportunityProjection =
                CreateAttackOpportunityProjection();
            foreach (BattleSquad withdrawing in GetActiveSquads(withdrawingSide).ToList())
            {
                if (!IsProjectableSquad(withdrawing)) continue;
                if (pursuedSquadIds.Contains(withdrawing.Id))
                {
                    // A squad some pursuer deliberately chose as its quarry never escapes here
                    // (BattleEscapeRules answers "actively_pursued" before it reads a threat), so
                    // projecting 30-odd pursuers against it is pure cost. Keep the trace line.
                    BattleEscapeRules.Evaluate(new(
                        _state.TurnNumber,
                        withdrawingSide == BattleSide.Attacker,
                        withdrawing.Id,
                        IsPursued: true,
                        []));
                    continue;
                }
                List<BattleAttackOpportunityProjection.PairInput> pairs =
                    projectablePursuers
                    .Select(pursuer => BuildAttackPair(
                        pursuer,
                        withdrawing,
                        headingVector,
                        FollowingMovementSpeed(pursuer),
                        WithdrawalMovementSpeed(withdrawing)))
                    .Where(pair => pair.HasValue)
                    .Select(pair => pair.Value)
                    .ToList();
                // BattleEscapeRules keeps the earliest threat, ties broken by pursuer id, and the
                // pairs are in pursuer id order. Its decision only asks whether that threat is
                // inside the retargeting horizon, and its reason only whether it is at zero turns.
                // So the search narrows as the answer settles:
                //   - once a pursuer can attack at zero turns, no later pursuer can be selected,
                //     and their ranged search is skipped;
                //   - once one can attack within the horizon, the squad remains whatever else is
                //     found, and a later pursuer could only change the reason by shooting NOW, so
                //     only that cheap check is made.
                // The decision is always that of the full search. The reported earliest turn can
                // differ when a later pursuer would have been earlier but not immediate, and in the
                // rare case of a partly reloaded weapon with rounds still loaded (a zero-turn
                // preparation route, not a "shot now") the reason can read enemy_can_retarget
                // where the full search would say inside_attack_range. Both reasons remain.
                List<BattleEscapeRules.Threat> threats = new(pairs.Count);
                BattleAttackOpportunityProjection.RangedSearch search =
                    BattleAttackOpportunityProjection.RangedSearch.Full;
                foreach (BattleAttackOpportunityProjection.PairInput pair in pairs)
                {
                    BattleAttackOpportunityProjection.PairResult projection =
                        opportunityProjection.ProjectPair(pair, search);
                    BattleAttackOpportunity opportunity = projection.Earliest;
                    threats.Add(new BattleEscapeRules.Threat(
                        pair.Pursuer.Id,
                        MinimumSquadSeparation(pair.Pursuer, pair.Quarry),
                        opportunity.DestinationRange
                            ?? BattleContactRules.MeleeContactAllowance,
                        pair.PursuerMoveSpeed,
                        pair.QuarryMoveSpeed,
                        opportunity));
                    if (!opportunity.IsReachable) continue;
                    if (opportunity.ElapsedTurns <= 0)
                    {
                        search = BattleAttackOpportunityProjection.RangedSearch.None;
                    }
                    else if (opportunity.ElapsedTurns <= BattleEscapeRules.RetargetingHorizonTurns
                        && search == BattleAttackOpportunityProjection.RangedSearch.Full)
                    {
                        search = BattleAttackOpportunityProjection.RangedSearch.ImmediateOnly;
                    }
                }
                BattleEscapeRules.Result result = BattleEscapeRules.Evaluate(new(
                    _state.TurnNumber,
                    withdrawingSide == BattleSide.Attacker,
                    withdrawing.Id,
                    pursuedSquadIds.Contains(withdrawing.Id),
                    threats));
                if (!result.Escapes) continue;

                DisengageSquad(
                    withdrawingSide,
                    withdrawing,
                    events,
                    "escaped beyond any timely enemy interception");
            }
        }

        private BattleTerminalRequest ResolveContactBreak(
            BattleSide withdrawingSide,
            List<BattleEvent> events)
        {
            BattleSideState state = GetSideState(withdrawingSide);
            if (!IsWithdrawalIntent(state.Intent)) return null;

            DisengageBurrowers(withdrawingSide, events);
            List<BattleSquad> withdrawing = GetActiveSquads(withdrawingSide).ToList();
            if (withdrawing.Count == 0)
            {
                return CompleteWithdrawal(withdrawingSide, events);
            }

            BattleSide pursuerSide = Opposite(withdrawingSide);
            // Posture is not fixed at declaration (§7: the pursuer re-evaluates every round).
            // Casualties and wounds move both sides' speeds, and the gap the withdrawal opens
            // changes whether pressing or shooting is the better use of the turn — a pursuer
            // that started faster than its quarry and is now not, in particular, needs to stop
            // chasing and start shooting rather than trail it to the turn cap.
            if (GetSideState(pursuerSide).Intent == BattleSideIntent.Pursuing)
            {
                BattleTerminalRequest terminal = EvaluatePursuitResponse(withdrawingSide, events);
                // BreakOff completed the withdrawal and recorded the terminal request.
                if (terminal != null) return terminal;
            }

            List<BattleSquad> pursuers = GetActiveSquads(pursuerSide).ToList();
            PursuitPosture posture = GetPursuitPosture(pursuerSide);
            IReadOnlyList<PursuitPairActivity> pursuitPairs = BuildPursuitPairActivities(
                pursuerSide,
                withdrawingSide);
            BattleContactRules.Result forceResult = BattleContactRules.Evaluate(new(
                _state.TurnNumber,
                withdrawingSide == BattleSide.Attacker,
                pursuers.Count,
                posture == PursuitPosture.BreakOff,
                IsWithdrawalIntent(GetSideState(pursuerSide).Intent),
                pursuitPairs,
                state.RearGuardSquadId.HasValue,
                0,
                GetActiveSquads(withdrawingSide)
                    .Select(SafeSquadMove)
                    .DefaultIfEmpty(0)
                    .Min()));
            if (forceResult.Decision == ContactBreakResult.OrganizedForceDisengages)
            {
                return CompleteWithdrawal(withdrawingSide, events);
            }

            if (state.RearGuardSquadId is int rearGuardId
                && _state.AllAttackerSquads.Values
                    .Concat(_state.AllOpposingSquads.Values)
                    .FirstOrDefault(squad => squad.Id == rearGuardId) is BattleSquad rearGuard
                && rearGuard.Status == BattleSquadStatus.Active)
            {
                foreach (BattleSquad squad in withdrawing.Where(squad => squad.Id != rearGuardId).ToList())
                {
                    float current = MinimumSquadSeparation(squad, rearGuard);
                    float start = _rearGuardStartingSeparation.GetValueOrDefault(squad.Id, current);
                    BattleContactRules.Result masked = BattleContactRules.Evaluate(new(
                        _state.TurnNumber,
                        withdrawingSide == BattleSide.Attacker,
                        pursuers.Count,
                        false,
                        false,
                        pursuitPairs,
                        true,
                        Math.Max(0, current - start),
                        squad.GetSquadMove(),
                        HasImmediateDisengagementCapability: false));
                    if (masked.Decision == ContactBreakResult.SquadDisengages)
                    {
                        DisengageSquad(
                            withdrawingSide,
                            squad,
                            events,
                            "departed behind the rear guard");
                    }
                }
            }

            if (state.RearGuardSquadId.HasValue
                && !GetActiveSquads(withdrawingSide).Any(
                    squad => squad.Id == state.RearGuardSquadId.Value))
            {
                state.RearGuardSquadId = null;
                state.Intent = BattleSideIntent.FightingWithdrawal;
                _rearGuardStartingSeparation.Clear();
            }
            return null;
        }

        private void TryAssignRearGuard(BattleSide withdrawingSide, List<BattleEvent> events)
        {
            BattleSideState state = GetSideState(withdrawingSide);
            BattleSide pursuerSide = Opposite(withdrawingSide);
            if (state.Intent != BattleSideIntent.FightingWithdrawal
                || state.RearGuardSquadId.HasValue
                || GetPursuitPosture(pursuerSide) != PursuitPosture.Press)
            {
                return;
            }

            // Routing squads are removed from the rear-guard candidate set
            // (OnlyWar_TDD.md §6.6).
            List<BattleSquad> squads = GetActiveSquads(withdrawingSide)
                .Where(squad => squad.WithdrawalRole != WithdrawalRole.Routing)
                .OrderBy(squad => squad.Id)
                .ToList();
            if (squads.Count < 2) return;
            // All active friendly squads (including any routing ones) — the propagation and
            // local-outnumber morale terms read the full local picture, not just candidates.
            List<BattleSquad> friendly = GetActiveSquads(withdrawingSide).ToList();
            List<BattleSquad> enemy = GetActiveSquads(pursuerSide).ToList();
            BattleForceMetrics friendlyMetrics = _roundMetrics.BuildMetrics(withdrawingSide);
            BattleForceMetrics enemyMetrics = _roundMetrics.BuildMetrics(pursuerSide);
            float fastestPursuer = enemyMetrics.FastestPursuitSquadSpeed;
            // Rear-guard selection still needs its calibrated force forecast. This is deliberately
            // named as an approximation and never feeds a ranged-shot or escape opportunity.
            float attackReach = ForecastApproximateOneTurnAttackReach(
                pursuerSide,
                withdrawingSide);
            // §8.2 command collapse: force disadvantage feeds the closed-form rout estimate used
            // to price a severed dependent's collapse (see EstimateRoutsIfUncovered).
            float forceDisadvantage = BattleMoraleEvaluator.ComputeForceDisadvantage(
                friendlyMetrics.CurrentBattleValue,
                enemyMetrics.CurrentBattleValue,
                friendlyMetrics.BattleValueLostPreviousTwoRounds,
                enemyMetrics.BattleValueLostPreviousTwoRounds);
            List<WithdrawalForecast.SquadGeometry> geometry = squads.Select(squad =>
            {
                float squadEgo = SquadEgo(squad);
                bool provides = squad.SquadProvidesSynapse;
                // A squad needs coverage iff it neither provides synapse nor clears the Ego gate —
                // the same "independent-willed" definition force generation uses (§9).
                bool depends = !provides && squadEgo < MoraleConstants.RearGuardEgoThreshold;
                bool providesCommand = squad.SquadProvidesCommandAura;
                float commandAura = _moraleService.CommandAuraSupport(squad, withdrawingSide);
                // §4.3/§8.2 second consumer: only a squad CURRENTLY steadied by a living HQ has
                // support to lose in a branch. Synapse dependents are priced by the synapse path
                // (what the branch strips from them is the check skip, not a stress modifier), so
                // the two dependent sets stay disjoint; cross-aura coupling (a Hive Tyrant that
                // is both synapse provider and HQ) is not chased — the §8.2 one-level cap applies
                // to aura interactions too, and each verdict reads the squad's live state for the
                // other aura.
                bool dependsOnCommand = !provides && !depends && !providesCommand && commandAura > 0f;
                return new WithdrawalForecast.SquadGeometry(
                    squad.Id,
                    squad.AbleSoldiers.Count,
                    CurrentBattleValue(squad),
                    MinimumSquadToForceSeparation(squad, enemy),
                    squad.GetSquadMove(),
                    provides,
                    depends,
                    // Precompute the RNG-free rout verdict once per dependent: what §4.2 severance
                    // produces if this squad loses its provider this turn (command aura at its
                    // live value).
                    depends && EstimateRoutsAtOrdinaryMorale(
                        squad, friendly, enemy, forceDisadvantage, commandAura),
                    providesCommand,
                    dependsOnCommand,
                    // The every-HQ-lost branch verdict: support replaced by the loss term, per
                    // the stateless reading in CommandAuraEvaluator.
                    dependsOnCommand && EstimateRoutsAtOrdinaryMorale(
                        squad, friendly, enemy, forceDisadvantage,
                        -MoraleConstants.CommandLossStress));
            }).ToList();
            WithdrawalForecast.Projection baseline = WithdrawalForecast.ProjectOpenGround(
                geometry, fastestPursuer, attackReach);
            float closest = geometry.Min(item => item.CurrentEnemySeparation);
            List<WithdrawalForecast.Candidate> candidates = squads.Select(squad =>
            {
                WithdrawalForecast.SquadGeometry item = geometry.First(value => value.SquadId == squad.Id);
                WithdrawalForecast.Projection projection = WithdrawalForecast.ProjectOpenGround(
                    geometry,
                    fastestPursuer,
                    attackReach,
                    rearGuardSquadId: squad.Id,
                    rearGuardDelayTurns: 1);
                bool exposed = item.CurrentEnemySeparation <= closest + 0.001f;
                bool intercept = item.CurrentEnemySeparation <= fastestPursuer + attackReach;
                float delay = squad.AbleSoldiers.Count + squad.GetAverageArmor()
                    + squad.AbleSoldiers.Sum(soldier => soldier.EquippedRangedWeapons.Count);
                return new WithdrawalForecast.Candidate(
                    squad.Id,
                    exposed,
                    squad.IsInMelee,
                    intercept,
                    !exposed && !intercept,
                    squads.Count - 1,
                    item.CurrentEnemySeparation,
                    delay,
                    projection,
                    SquadEgo: SquadEgo(squad),
                    IsShaken: squad.MoraleState == MoraleState.Shaken,
                    // The live planner holds one squad while its providers withdraw with the main
                    // body, so a covered dependent's coverage always lapses mid-hold. This arm
                    // stays false until composite rear guards (§12); Warriors pass on Ego.
                    WillRemainSynapseCoveredWhileHolding: false);
            }).ToList();
            WithdrawalForecast.Result result = WithdrawalForecast.Evaluate(new(
                _state.TurnNumber,
                withdrawingSide == BattleSide.Attacker,
                baseline,
                candidates));
            if (result.SelectedSquadId is not int selectedId) return;

            state.Intent = BattleSideIntent.RearGuardWithdrawal;
            state.RearGuardSquadId = selectedId;
            state.CoveringSquadId = null;
            BattleSquad guard = squads.First(squad => squad.Id == selectedId);
            guard.WithdrawalRole = WithdrawalRole.RearGuard;
            foreach (BattleSquad squad in squads.Where(squad => squad.Id != selectedId))
            {
                _rearGuardStartingSeparation[squad.Id] = MinimumSquadSeparation(squad, guard);
            }
            events.Add(new BattleEvent(
                BattleEventType.RearGuardAssigned,
                _state.TurnNumber,
                withdrawingSide,
                selectedId,
                squads.Where(squad => squad.Id != selectedId).Select(squad => squad.Id),
                $"{guard.Name} was assigned as rear guard."));
        }

        private bool EstimateRoutsAtOrdinaryMorale(
            BattleSquad squad,
            IReadOnlyList<BattleSquad> friendly,
            IReadOnlyList<BattleSquad> enemy,
            float forceDisadvantage,
            float commandAuraSupport)
        {
            int startingAble = _moraleService.StartingAbleCountFor(squad);
            int turnStartAble = _moraleService.TurnStartAbleCountFor(squad);
            int currentAble = squad.AbleSoldiers.Count;
            float casualtyThisTurn = turnStartAble > 0
                ? Math.Clamp((float)(turnStartAble - currentAble) / turnStartAble, 0f, 1f)
                : 0f;
            float cumulativeCasualty = startingAble > 0
                ? Math.Clamp((float)(startingAble - currentAble) / startingAble, 0f, 1f)
                : 0f;
            bool leaderDead = _moraleService.SquadStartedWithLeader(squad)
                && squad.SquadLeader == null;
            float routingVisible = _moraleService.RoutingVisibleFriendlyFraction(
                squad,
                friendly);
            BattleSide side = _state.AttackerSquads.ContainsKey(squad.Id)
                ? BattleSide.Attacker
                : BattleSide.Opposing;
            float localOutnumber = BattleMoraleEvaluator.ComputeLocalOutnumberRatio(
                squad, friendly, enemy, _grid, MoraleConstants.VisualRange);
            BattleSoldier leader = squad.SquadLeader;
            List<BattleMoraleEvaluator.SoldierMoraleInput> soldiers = squad.AbleSoldiers
                .OrderBy(soldier => soldier.Soldier.Id)
                .Select(soldier => new BattleMoraleEvaluator.SoldierMoraleInput(
                    soldier.Soldier.Id,
                    soldier.Soldier.Ego,
                    leader != null && soldier.Soldier.Id == leader.Soldier.Id))
                .ToList();

            return BattleMoraleEvaluator.EstimateOutcome(
                new BattleMoraleEvaluator.MoraleCheckInput(
                    soldiers,
                    casualtyThisTurn,
                    cumulativeCasualty,
                    leaderDead,
                    routingVisible,
                    localOutnumber,
                    commandAuraSupport,
                    forceDisadvantage,
                    _moraleService.MobSupport(
                        squad,
                        friendly,
                        side,
                        commandAuraSupport))) == MoraleState.Routing;
        }

        private BattleTerminalRequest CompleteWithdrawal(
            BattleSide side,
            List<BattleEvent> events)
        {
            BattleSide holder = Opposite(side);
            // Read intent before DisengageForce overwrites it: a side whose every squad broke
            // (BattleSideIntent.Rout) records the typed Rout end reason, not Withdrawal.
            bool wasRouting = GetSideState(side).Intent == BattleSideIntent.Rout;
            DisengageForce(side, events, $"{SideName(side)} broke contact.");
            GetSideState(holder).Intent = BattleSideIntent.Engaged;
            _pursuitPostures.Remove(holder);
            _squadPursuitPostures.Remove(holder);
            return new BattleTerminalRequest(
                wasRouting ? BattleEndReason.Rout : BattleEndReason.Withdrawal,
                holder);
        }

        private void DisengageBurrowers(BattleSide side, List<BattleEvent> events)
        {
            foreach (BattleSquad squad in GetActiveSquads(side)
                .Where(squad => squad.CanBurrow)
                .ToList())
            {
                DisengageSquad(side, squad, events, "used its burrowing capability to disengage");
            }
        }

        private void DisengageForce(BattleSide side, List<BattleEvent> events, string description)
        {
            foreach (BattleSquad squad in GetActiveSquads(side).ToList())
            {
                DisengageSquad(side, squad, events, description);
            }
            GetSideState(side).Intent = BattleSideIntent.Disengaged;
            events.Add(new BattleEvent(
                BattleEventType.ForceDisengaged,
                _state.TurnNumber,
                side,
                null,
                GetAllSquads(side).Where(squad => squad.Status == BattleSquadStatus.Disengaged)
                    .Select(squad => squad.Id),
                description));
        }

        private void DisengageSquad(
            BattleSide side,
            BattleSquad squad,
            List<BattleEvent> events,
            string reason)
        {
            if (squad.Status != BattleSquadStatus.Active) return;
            foreach (BattleSoldier soldier in squad.AbleSoldiers.ToList())
            {
                _grid.RemoveSoldier(soldier.Soldier.Id);
            }
            _state.DisengageSquad(squad);
            events.Add(new BattleEvent(
                BattleEventType.SquadDisengaged,
                _state.TurnNumber,
                side,
                squad.Id,
                null,
                $"{squad.Name} {reason}."));
        }

        /// <summary>
        /// Materializes contact evidence for the exact pursuer/quarry assignments selected by the
        /// current planning pass. No force-wide speed, separation, or shot projection is allowed to
        /// enter this list. Executed attacks and aims are credited to the pursuer's pair whichever
        /// withdrawing squad they were aimed at, because soldiers choose their own targets.
        /// </summary>
        internal IReadOnlyList<PursuitPairActivity> BuildPursuitPairActivities(
            BattleSide pursuerSide,
            BattleSide quarrySide)
        {
            Dictionary<int, BattleSquad> pursuers = GetActiveSquads(pursuerSide)
                .ToDictionary(squad => squad.Id);
            Dictionary<int, BattleSquad> quarries = GetActiveSquads(quarrySide)
                .ToDictionary(squad => squad.Id);
            HashSet<int> quarryIds = [.. quarries.Keys];
            RangedTargetSelector ranged = CreateContactRangedTargetSelector();
            ValueTuple<int, int> quarryHeading = GetWithdrawalHeadingVector(
                quarrySide,
                quarries.Values.ToList(),
                pursuers.Values.ToList());

            List<PursuitPairActivity> activities = [];
            foreach (KeyValuePair<int, int> pairing in _pursuitTargetsBySquad
                .OrderBy(pairing => pairing.Key))
            {
                if (!pursuers.TryGetValue(pairing.Key, out BattleSquad pursuer)) continue;
                if (quarries.ContainsKey(pairing.Value)) continue;
                // Pairings are replaced at every planning pass and the quarry was active when it
                // was chosen, so an eliminated quarry fell during this pairing's own turn. A
                // quarry that disengaged or withdrew is NOT evidence and still drops out.
                if (FindSquad(pairing.Value)?.Status != BattleSquadStatus.Eliminated) continue;
                if (_pursuitAssignmentStates.TryGetValue(
                        pursuer.Id,
                        out PursuitAssignmentState assignment))
                {
                    // Finishing the quarry is a productive assignment; the pursuer's next
                    // reassignment must not be charged as an unproductive switch.
                    assignment.HadObservedProgress = true;
                    assignment.UnproductiveSwitches = 0;
                }
                activities.Add(new PursuitPairActivity(
                    pursuer.Id,
                    pairing.Value,
                    CurrentSeparation: 0,
                    DeclaredTurnSpeed(pursuer),
                    QuarryWithdrawalSpeed: 0,
                    _roundMetrics.HasPairAttackedRecently(pursuer.Id, pairing.Value),
                    FireCycleProgressedThisTurn: false,
                    FireCommitmentRemainsViable: false,
                    ProjectedCanReachContactThisTurn: false,
                    ProgressValidityReason: "quarry_eliminated",
                    ProgressResetReason: "quarry_eliminated",
                    QuarryEliminated: true));
            }

            return _pursuitTargetsBySquad
                .OrderBy(pairing => pairing.Key)
                .Select(pairing =>
                {
                    if (!pursuers.TryGetValue(pairing.Key, out BattleSquad pursuer)
                        || !quarries.TryGetValue(pairing.Value, out BattleSquad quarry))
                    {
                        return (Pursuer: (BattleSquad)null, Quarry: (BattleSquad)null);
                    }
                    return (Pursuer: pursuer, Quarry: quarry);
                })
                .Where(pair => pair.Pursuer != null && pair.Quarry != null)
                .Select(pair =>
                {
                    bool preparedFireCommitment = HasViablePreparedFireCommitment(
                        pair.Pursuer,
                        pair.Quarry,
                        ranged);
                    // Attack and aim evidence counts against ANY withdrawing squad, not only the
                    // assigned quarry: soldiers pick their own targets, so a pursuer assigned to
                    // the Cover squad may be shooting the squads bounding away behind it. Observed
                    // 2026-09-22 (Grist Nine Epsilon): all 33 pursuers were assigned to the Cover
                    // squad while ~100 aimed and ~10 fired at other orks each turn, and contact
                    // broke as stalled_pursuit with a third of the ork force still under fire.
                    bool viableAimCommitment = HasViableFireCommitment(
                        pair.Pursuer,
                        quarryIds,
                        ranged);
                    bool attackedRecently = quarryIds.Any(quarryId =>
                        _roundMetrics.HasPairAttackedRecently(pair.Pursuer.Id, quarryId));
                    bool fireCycleProgressed = quarryIds.Any(quarryId =>
                        _roundMetrics.HasPairFireCycleProgressedThisRound(
                            pair.Pursuer.Id, quarryId));
                    PursuitPairProgressHistory history = GetCurrentPairHistory(
                        pair.Pursuer,
                        pair.Quarry);
                    BattleInterceptionProjection.PairInput? pressing = BuildPressingPair(
                        pair.Pursuer,
                        pair.Quarry,
                        quarryHeading);
                    bool canReachContactThisTurn = pressing.HasValue
                        && BattleInterceptionProjection.Project(pressing.Value)
                            .CanReachContactThisTurn;
                    PursuitProgressSample? latest = history?.LatestSample;
                    float separation = MinimumSquadSeparation(pair.Pursuer, pair.Quarry);
                    return new PursuitPairActivity(
                        pair.Pursuer.Id,
                        pair.Quarry.Id,
                        separation,
                        DeclaredTurnSpeed(pair.Pursuer),
                        DeclaredTurnSpeed(pair.Quarry),
                        attackedRecently,
                        fireCycleProgressed || preparedFireCommitment,
                        viableAimCommitment || preparedFireCommitment,
                        history?.RollingSeparationGain ?? 0,
                        history?.HasObservedClosingProgress == true,
                        history?.HasStartupGrace == true,
                         canReachContactThisTurn,
                         history?.Samples.Count ?? 0,
                         history?.LastSampleWasValid == true,
                         latest?.ActualPursuerDisplacement ?? 0,
                         latest?.ActualQuarryDisplacement ?? 0,
                         latest?.PlannedPursuerDisplacement ?? 0,
                         latest?.PlannedQuarryDisplacement ?? 0,
                         latest?.FailedMoveCount ?? 0,
                         PursuitProgressPolicy.HistoryLength,
                         history?.LastSampleValidityReason ?? "no_history",
                         history?.LastResetReason ?? "no_history",
                         EffectSeparation: history?.HasObservedClosingProgress == true
                             ? EffectSeparation(pair.Pursuer, pair.Quarry, separation)
                             : null);
                })
                .Concat(activities)
                .OrderBy(activity => activity.PursuerSquadId)
                .ToList();
        }

        /// <summary>
        /// The separation at which a closing pair can act on its quarry: its useful fire range
        /// against that quarry while it is still outside it, and melee contact once it is inside
        /// (a pair inside its useful fire range that is not firing is closing only to reach
        /// contact) or when it has no useful fire at all (dry, or no gun that hurts the quarry).
        /// </summary>
        private static float EffectSeparation(
            BattleSquad pursuer,
            BattleSquad quarry,
            float separation)
        {
            float useful = BattleEngagementFrameBuilder.BuildProfile(pursuer, [quarry])
                .UsefulFireRange;
            return useful > 0 && separation > useful
                ? useful
                : BattleContactRules.MeleeContactAllowance;
        }

        private BattleSquad FindSquad(int squadId) =>
            _state.AllAttackerSquads.TryGetValue(squadId, out BattleSquad attacker)
                ? attacker
                : _state.AllOpposingSquads.GetValueOrDefault(squadId);

        private PursuitPairProgressHistory GetCurrentPairHistory(
            BattleSquad pursuer,
            BattleSquad quarry)
        {
            if (!_pursuitProgressHistories.TryGetValue(
                    (pursuer.Id, quarry.Id),
                    out PursuitPairProgressHistory history))
            {
                return null;
            }

            if (history.MembershipMatches(pursuer, quarry)) return history;

            // Cleanup can remove a body after the post-movement sample was captured. The old
            // nearest-soldier distance is no longer comparable, even when the able count happens
            // to be unchanged because another body replaced it.
            history.ResetForMembership(pursuer, quarry, _state.TurnNumber);
            if (_pursuitAssignmentStates.TryGetValue(
                    pursuer.Id,
                    out PursuitAssignmentState assignment))
            {
                assignment.HadObservedProgress = false;
            }
            return history;
        }

        private RangedTargetSelector CreateContactRangedTargetSelector()
        {
            SquadPlanningServices services = new(
                _grid,
                _state.Soldiers,
                _rules.MeleeWeaponTemplates,
                log: null,
                new BattlePlanningContext());
            return new RangedTargetSelector(new RangedTargetingServices(services));
        }

        private bool HasViableFireCommitment(
            BattleSquad pursuer,
            IReadOnlySet<int> quarrySquadIds,
            RangedTargetSelector ranged)
        {
            return pursuer.AbleSoldiers.Any(soldier =>
                soldier.Aim is ValueTuple<int, RangedWeapon, int> aim
                && _state.Soldiers.TryGetValue(aim.Item1, out BattleSoldier target)
                && target.BattleSquad != null
                && quarrySquadIds.Contains(target.BattleSquad.Id)
                && ranged.IsExistingAimStillViable(soldier));
        }

        /// <summary>
        /// A Ready or Reload action has no target id of its own. It can still be evidence for this
        /// exact pair because the current-turn pairing and the selected Hold policy are both live
        /// on the squad when contact is evaluated. The metrics provide only successful executed
        /// preparations; this method supplies the missing pair-local viability check.
        /// </summary>
        private bool HasViablePreparedFireCommitment(
            BattleSquad pursuer,
            BattleSquad quarry,
            RangedTargetSelector ranged)
        {
            if (pursuer.LastEngagementOptionKind != EngagementOptionKind.Hold)
            {
                return false;
            }

            // Match the fire-window's quarry opening projection. Bound and routing quarries are
            // the only withdrawal roles that contribute an opening speed; a covering or ordinary
            // target contributes no withdrawal opening for this check.
            float quarrySpeed = quarry.WithdrawalRole is WithdrawalRole.Bound or WithdrawalRole.Routing
                ? DeclaredTurnSpeed(quarry)
                : 0;
            foreach (BattleRoundMetrics.RangedPreparationProgress progress in
                _roundMetrics.GetRangedPreparationProgressThisRound(pursuer.Id))
            {
                BattleSoldier shooter = pursuer.AbleSoldiers.FirstOrDefault(
                    soldier => soldier.Soldier.Id == progress.SoldierId);
                if (shooter == null
                    || !shooter.EquippedRangedWeapons.Contains(progress.Weapon))
                {
                    continue;
                }

                if (quarry.AbleSoldiers.Any(target =>
                    ranged.IsWorthwhilePursuitFollowUpShot(
                        shooter,
                        target,
                        progress.Weapon,
                        quarrySpeed,
                        allowPendingPreparation: true)))
                {
                    return true;
                }
            }

            return false;
        }

        private static float DeclaredTurnSpeed(BattleSquad squad) => squad.AbleSoldiers
            .Select(soldier => soldier.CurrentSpeed)
            .DefaultIfEmpty(0)
            .Min();

        /// <summary>
        /// Projects a useful ranged opportunity for every concrete pair of ONE pursuing squad
        /// against the target side's squads. The pursuer's follow movement is the legal jog tier
        /// used by the follow posture; the quarry uses its own withdrawal movement envelope.
        /// Neither speed is borrowed from a different pair, and the ranged evaluator remains the
        /// source of target-defense, ammunition, and readiness decisions.
        ///
        /// <para>The search stops as soon as the pursuit decision's three questions are answered
        /// (<see cref="BattlePursuitPlanner.Evaluate"/> reads only whether a shot exists, whether
        /// one exists now, and whether one comes before contact). When the squad can catch its
        /// quarry, the first shot earlier than contact settles it; when it cannot, a shot now is
        /// looked for first and otherwise any shot at all. The reported turn is then not
        /// necessarily the earliest, which the trace's <c>follow_search</c> field records.</para>
        /// </summary>
        private (float? Turns, BattleAttackOpportunityProjection.AggregateResult Projection)
            ProjectedFollowShotTurns(
                BattleSide pursuingSide,
                BattleSide targetSide,
                BattleSquad pursuer,
                float pressTurns,
                bool canCatch)
        {
            BattleAttackOpportunityProjection.AggregateResult projection;
            string searchKind;
            if (canCatch)
            {
                projection = ProjectAttackOpportunities(
                    pursuingSide,
                    targetSide,
                    FollowingMovementSpeed,
                    WithdrawalMovementSpeed,
                    pursuer,
                    BattleAttackOpportunityProjection.RangedSearch.Full,
                    pressTurns);
                searchKind = "first_before_contact";
            }
            else
            {
                projection = ProjectAttackOpportunities(
                    pursuingSide,
                    targetSide,
                    FollowingMovementSpeed,
                    WithdrawalMovementSpeed,
                    pursuer,
                    BattleAttackOpportunityProjection.RangedSearch.ImmediateOnly);
                searchKind = "shot_now";
                if (!projection.HasRanged)
                {
                    projection = ProjectAttackOpportunities(
                        pursuingSide,
                        targetSide,
                        FollowingMovementSpeed,
                        WithdrawalMovementSpeed,
                        pursuer,
                        BattleAttackOpportunityProjection.RangedSearch.Full,
                        float.PositiveInfinity);
                    searchKind = "any_shot";
                }
            }
            BattleAttackOpportunity ranged = projection.Ranged;
            float? turns = ranged.IsReachable ? ranged.ElapsedTurns : null;
            if (BattleLog.IsEnabled)
            {
                BattleAttackOpportunity earliest = projection.Earliest;
                List<KeyValuePair<string, string>> projectionFields =
                [
                    BattleDecisionTrace.Field("turn", _state.TurnNumber),
                    BattleDecisionTrace.Field("side", pursuingSide == BattleSide.Attacker
                        ? "first" : "second"),
                    BattleDecisionTrace.Field("squad", pursuer.Id),
                    BattleDecisionTrace.Field("scope", "pair_aggregate"),
                    BattleDecisionTrace.Field("follow_search", searchKind),
                    BattleDecisionTrace.Field(
                        "estimate_kind", "hypothetical_useful_attack_opportunity"),
                    BattleDecisionTrace.Field("pursuer_movement_assumption", "follow_jog"),
                    BattleDecisionTrace.Field(
                        "quarry_movement_assumption", "withdrawal_role_envelope"),
                    BattleDecisionTrace.Field("pair_count", projection.PairCount),
                    BattleDecisionTrace.Field("ranged_pursuer", ranged.PursuerSquadId),
                    BattleDecisionTrace.Field("ranged_quarry", ranged.QuarrySquadId),
                    BattleDecisionTrace.Field("ranged_attack_mode", ranged.Mode),
                    BattleDecisionTrace.Field("ranged_attack_kind", ranged.Kind),
                    BattleDecisionTrace.Field("ranged_shooter", ranged.ShooterId),
                    BattleDecisionTrace.Field("ranged_target", ranged.TargetId),
                    BattleDecisionTrace.Field(
                        "ranged_weapon_template", ranged.WeaponTemplateId),
                    BattleDecisionTrace.Field("ranged_preparation", ranged.Preparation),
                    BattleDecisionTrace.Field(
                        "ranged_requires_movement", ranged.RequiresMovement),
                    BattleDecisionTrace.Field("ranged_movement_turns", ranged.MovementTurns),
                    BattleDecisionTrace.Field(
                        "useful_range_model", "sampled_live_ranged_evaluator"),
                    BattleDecisionTrace.Field("useful_range", ranged.DestinationRange),
                    BattleDecisionTrace.Field("useful_attack_turns", turns),
                    BattleDecisionTrace.Field("shot_turns", turns),
                    BattleDecisionTrace.Field("shot_turns_scope", "counterfactual_useful_ranged"),
                    BattleDecisionTrace.Field("ranged_reason", ranged.Reason),
                    BattleDecisionTrace.Field("earliest_pursuer", earliest.PursuerSquadId),
                    BattleDecisionTrace.Field("earliest_quarry", earliest.QuarrySquadId),
                    BattleDecisionTrace.Field("earliest_attack_mode", earliest.Mode),
                    BattleDecisionTrace.Field("earliest_attack_kind", earliest.Kind),
                    BattleDecisionTrace.Field("earliest_shooter", earliest.ShooterId),
                    BattleDecisionTrace.Field("earliest_target", earliest.TargetId),
                    BattleDecisionTrace.Field(
                        "earliest_weapon_template", earliest.WeaponTemplateId),
                    BattleDecisionTrace.Field("earliest_turns",
                        earliest.IsReachable ? earliest.ElapsedTurns : null),
                    BattleDecisionTrace.Field("earliest_reason", earliest.Reason)
                ];
                BattleProjectionDiagnosticTrace.AddOpportunity(
                    projectionFields,
                    "ranged",
                    ranged,
                    "useful_range");
                BattleProjectionDiagnosticTrace.AddOpportunity(
                    projectionFields,
                    "earliest",
                    earliest,
                    "useful_attack");
                BattleLog.Write(new BattleDecisionTrace(
                    "FOLLOW_SHOT_EVAL",
                    projectionFields).Render());
            }
            return (turns, projection);
        }

        private BattleAttackOpportunityProjection.AggregateResult ProjectAttackOpportunities(
            BattleSide pursuingSide,
            BattleSide quarrySide,
            Func<BattleSquad, float> pursuerSpeed,
            Func<BattleSquad, float> quarrySpeed,
            BattleSquad onlyPursuer = null,
            BattleAttackOpportunityProjection.RangedSearch search =
                BattleAttackOpportunityProjection.RangedSearch.Full,
            float acceptBelowTurns = 0)
        {
            List<BattleSquad> pursuers = GetActiveSquads(pursuingSide)
                .Where(IsProjectableSquad)
                .OrderBy(squad => squad.Id)
                .ToList();
            List<BattleSquad> quarries = GetActiveSquads(quarrySide)
                .Where(IsProjectableSquad)
                .OrderBy(squad => squad.Id)
                .ToList();
            // The withdrawal heading is a property of the whole engagement; only the pairs are
            // narrowed to the squad being evaluated.
            ValueTuple<int, int> heading = GetWithdrawalHeadingVector(
                quarrySide,
                quarries,
                pursuers);
            BattleAttackOpportunityProjection projection = CreateAttackOpportunityProjection();
            IEnumerable<BattleAttackOpportunityProjection.PairInput> pairs =
                pursuers
                .Where(pursuer => onlyPursuer == null || pursuer.Id == onlyPursuer.Id)
                .SelectMany(pursuer => quarries
                    .Select(quarry => BuildAttackPair(
                        pursuer,
                        quarry,
                        heading,
                        pursuerSpeed(pursuer),
                        quarrySpeed(quarry)))
                    .Where(pair => pair.HasValue)
                    .Select(pair => pair.Value));
            return projection.EarliestPair(pairs, search, acceptBelowTurns);
        }

        private ValueTuple<int, int> GetWithdrawalHeadingVector(
            BattleSide withdrawingSide,
            IReadOnlyCollection<BattleSquad> quarries,
            IReadOnlyCollection<BattleSquad> pursuers)
        {
            ushort heading = GetSideState(withdrawingSide).WithdrawalHeading
                ?? BattleForcePlanner.SelectWithdrawalHeading(quarries, pursuers);
            return BattleForcePlanner.GetHeadingVector(heading);
        }

        private static BattleAttackOpportunityProjection.PairInput? BuildAttackPair(
            BattleSquad pursuer,
            BattleSquad quarry,
            ValueTuple<int, int> quarryHeading,
            float pursuerSpeed,
            float quarrySpeed)
        {
            if (!IsProjectableSquad(pursuer) || !IsProjectableSquad(quarry)) return null;
            return new BattleAttackOpportunityProjection.PairInput(
                pursuer,
                quarry,
                new BattleInterceptionProjection.Point(
                    quarryHeading.Item1,
                    quarryHeading.Item2),
                Math.Max(0, pursuerSpeed),
                Math.Max(0, quarrySpeed));
        }

        private static float FollowingMovementSpeed(BattleSquad squad) =>
            SafeSquadMove(squad) * SoldierMovementProjector.JogSpeedMultiplier;

        private static float WithdrawalMovementSpeed(BattleSquad squad) =>
            squad.WithdrawalRole is WithdrawalRole.Cover or WithdrawalRole.RearGuard
                ? 0
                : squad.WithdrawalRole is WithdrawalRole.Bound or WithdrawalRole.Routing
                    ? PressingMovementSpeed(squad)
                    : SafeSquadMove(squad);

        /// <summary>
        /// Builds one counterfactual pressing input for every concrete active squad pair. The
        /// nearest placed combat-effective soldier pair supplies the geometry; the movement
        /// speeds remain the two squads' own legal fast-approach capabilities. No selected action
        /// or realized <see cref="BattleSoldier.CurrentSpeed"/> is read here.
        /// </summary>
        private BattleInterceptionProjection.AggregateResult ProjectPressingContact(
            BattleSide pursuingSide,
            BattleSide withdrawingSide,
            BattleSquad onlyPursuer = null)
        {
            List<BattleSquad> pursuers = GetActiveSquads(pursuingSide)
                .Where(IsProjectableSquad)
                .OrderBy(squad => squad.Id)
                .ToList();
            List<BattleSquad> quarries = GetActiveSquads(withdrawingSide)
                .Where(IsProjectableSquad)
                .OrderBy(squad => squad.Id)
                .ToList();
            BattleSideState withdrawing = GetSideState(withdrawingSide);
            ushort heading = withdrawing.WithdrawalHeading
                ?? BattleForcePlanner.SelectWithdrawalHeading(quarries, pursuers);
            ValueTuple<int, int> headingVector = BattleForcePlanner.GetHeadingVector(heading);

            IEnumerable<BattleInterceptionProjection.PairInput> pairs =
                pursuers
                .Where(pursuer => onlyPursuer == null || pursuer.Id == onlyPursuer.Id)
                .SelectMany(pursuer => quarries
                    .Select(quarry => BuildPressingPair(pursuer, quarry, headingVector))
                    .Where(pair => pair.HasValue)
                    .Select(pair => pair.Value));
            return BattleInterceptionProjection.EarliestPair(pairs);
        }

        private static BattleInterceptionProjection.PairInput? BuildPressingPair(
            BattleSquad pursuer,
            BattleSquad quarry,
            ValueTuple<int, int> quarryHeading)
        {
            var nearest = pursuer.AbleSoldiers
                .Where(soldier => soldier.TopLeft.HasValue)
                .SelectMany(pursuerSoldier => quarry.AbleSoldiers
                    .Where(soldier => soldier.TopLeft.HasValue)
                    .Select(quarrySoldier => new
                    {
                        Pursuer = pursuerSoldier,
                        Quarry = quarrySoldier,
                        Distance = BattleInterceptionProjection.Distance(
                            new BattleInterceptionProjection.Point(
                                pursuerSoldier.TopLeft.Value.Item1,
                                pursuerSoldier.TopLeft.Value.Item2),
                            new BattleInterceptionProjection.Point(
                                quarrySoldier.TopLeft.Value.Item1,
                                quarrySoldier.TopLeft.Value.Item2))
                    }))
                .OrderBy(pair => pair.Distance)
                .ThenBy(pair => pair.Pursuer.Soldier.Id)
                .ThenBy(pair => pair.Quarry.Soldier.Id)
                .FirstOrDefault();
            if (nearest == null) return null;

            return new BattleInterceptionProjection.PairInput(
                pursuer.Id,
                quarry.Id,
                new BattleInterceptionProjection.Point(
                    nearest.Pursuer.TopLeft.Value.Item1,
                    nearest.Pursuer.TopLeft.Value.Item2),
                new BattleInterceptionProjection.Point(
                    nearest.Quarry.TopLeft.Value.Item1,
                    nearest.Quarry.TopLeft.Value.Item2),
                PressingMovementSpeed(pursuer),
                PressingMovementSpeed(quarry),
                new BattleInterceptionProjection.Point(quarryHeading.Item1, quarryHeading.Item2));
        }

        private static bool IsProjectableSquad(BattleSquad squad) =>
            squad != null
            && squad.Status == BattleSquadStatus.Active
            && squad.AbleSoldiers.Any(soldier => soldier.TopLeft.HasValue);

        private static float PressingMovementSpeed(BattleSquad squad) =>
            squad.GetSquadMove()
                * (squad.CanRun ? 1f : SoldierMovementProjector.JogSpeedMultiplier);

        private float MinimumSeparation(BattleSide first, BattleSide second)
        {
            List<BattleSquad> secondSquads = GetActiveSquads(second).ToList();
            return GetActiveSquads(first)
                .Select(squad => MinimumSquadToForceSeparation(squad, secondSquads))
                .DefaultIfEmpty(float.MaxValue)
                .Min();
        }

        internal float CurrentMinimumSeparation(BattleSide first, BattleSide second) =>
            MinimumSeparation(first, second);

        private static float MinimumSquadToForceSeparation(
            BattleSquad squad,
            IReadOnlyCollection<BattleSquad> force)
        {
            return force.Select(other => MinimumSquadSeparation(squad, other))
                .DefaultIfEmpty(float.MaxValue)
                .Min();
        }

        private static float MinimumSquadSeparation(BattleSquad first, BattleSquad second)
            => BattleEngagementFrameBuilder.MinimumDistance(first, second);

        /// <summary>
        /// Legacy rear-guard forecast only. The profile-range approximation is retained for that
        /// force-level forecast and is not an executable shot, a pair opportunity, or an escape
        /// threat. Follow and escape use <see cref="BattleAttackOpportunityProjection"/> instead.
        /// </summary>
        private float ForecastApproximateOneTurnAttackReach(
            BattleSide pursuingSide,
            BattleSide targetSide)
        {
            List<BattleSoldier> target = GetActiveSquads(targetSide)
                .SelectMany(squad => squad.AbleSoldiers)
                .ToList();
            if (target.Count == 0) return 0;
            float size = (float)target.Average(soldier => soldier.Soldier.Size);
            float armor = (float)target.Average(
                soldier => soldier.Armor?.Template.ArmorProvided ?? 0);
            float constitution = (float)target.Average(soldier => soldier.Soldier.Constitution);
            float evasion = (float)target.Average(
                soldier => soldier.Soldier.Template.Species.RangedEvasion);
            float ranged = GetActiveSquads(pursuingSide)
                .SelectMany(squad => squad.AbleSoldiers)
                .Select(soldier => BattleModifiersUtil.CalculateOptimalDistance(
                    soldier,
                    size,
                    armor,
                    constitution,
                    evasion))
                .DefaultIfEmpty(0)
                .Max();
            float melee = GetActiveSquads(pursuingSide)
                .SelectMany(squad => squad.AbleSoldiers)
                .Select(soldier => soldier.GetMoveSpeed() + 1)
                .DefaultIfEmpty(0)
                .Max();
            return Math.Max(melee, ranged);
        }

        private void LogCoverAssignment(
            BattleSide side,
            BattleSideState state,
            BattleForcePlanner.CoverAssignment assignment,
            ICollection<BattleEvent> events)
        {
            BattleDecisionTrace trace = new("COVER_ASSIGN",
            [
                BattleDecisionTrace.Field("turn", _state.TurnNumber),
                BattleDecisionTrace.Field("side", side == BattleSide.Attacker ? "first" : "second"),
                BattleDecisionTrace.Field("heading", state.WithdrawalHeading),
                BattleDecisionTrace.Field("selected_squad", assignment.SquadId),
                BattleDecisionTrace.Field("rotated", assignment.Rotated),
                BattleDecisionTrace.Field("reason", assignment.Reason),
                BattleDecisionTrace.Field("candidates", string.Join(",", assignment.Candidates
                    .OrderBy(candidate => candidate.SquadId)
                    .Select(candidate =>
                        $"{candidate.SquadId}:{candidate.NearestEnemyDistance:0.###}:{candidate.RangedCoverEligible}")))
            ]);
            BattleLog.Write(trace.Render());
            if (assignment.SquadId.HasValue
                && (assignment.Rotated || assignment.Reason == "farthest_eligible"))
            {
                events.Add(new BattleEvent(
                    BattleEventType.CoverAssigned,
                    _state.TurnNumber,
                    side,
                    assignment.SquadId,
                    null,
                    $"{SideName(side)} assigned a covering squad."));
            }
        }

        private string SideName(BattleSide side) =>
            GetAllSquads(side).Any(squad => squad.IsPlayerAligned)
                ? "Player force"
                : "Opposing force";

        private IReadOnlyCollection<BattleSquad> GetActiveSquads(BattleSide side) =>
            side == BattleSide.Attacker
                ? _state.ActiveAttackerSquads.Values.ToList()
                : _state.ActiveOpposingSquads.Values.ToList();

        private IReadOnlyCollection<BattleSquad> GetAllSquads(BattleSide side) =>
            side == BattleSide.Attacker
                ? _state.AllAttackerSquads.Values.ToList()
                : _state.AllOpposingSquads.Values.ToList();

        private BattleSideState GetSideState(BattleSide side) =>
            side == BattleSide.Attacker ? _state.AttackerSide : _state.OpposingSide;

        private static BattleSide Opposite(BattleSide side) =>
            side == BattleSide.Attacker ? BattleSide.Opposing : BattleSide.Attacker;

        private static bool IsWithdrawalIntent(BattleSideIntent intent) =>
            intent is BattleSideIntent.FightingWithdrawal
                or BattleSideIntent.RearGuardWithdrawal
                or BattleSideIntent.Rout;

        private static float SquadEgo(BattleSquad squad)
        {
            List<BattleSoldier> able = squad.AbleSoldiers;
            return able.Count > 0 ? able.Average(soldier => soldier.Soldier.Ego) : 0f;
        }

        private static int CurrentBattleValue(BattleSquad squad) => squad.AbleSoldiers
            .Sum(soldier => soldier.EffectiveBattleValue);

        private static float SafeSquadMove(BattleSquad squad) =>
            squad.AbleSoldiers.Count == 0 ? 0 : squad.GetSquadMove();
    }

    /// <summary>Lifecycle request for the resolver's existing BattleHistory outcome owner.</summary>
    internal sealed record BattleTerminalRequest(
        BattleEndReason Reason,
        BattleSide? SideHoldingField);
}
