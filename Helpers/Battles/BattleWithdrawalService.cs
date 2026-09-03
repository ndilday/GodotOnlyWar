using OnlyWar.Models.Battles;
using OnlyWar.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Battles
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

        // Frozen current-turn pursuit pairings. Individual withdrawal escape must distinguish a
        // squad the enemy deliberately chose as its quarry from another member of the same force;
        // force-wide min/max speeds cannot answer that question.
        private readonly Dictionary<int, int> _pursuitTargetsBySquad = [];

        // Separation at the beginning of the rear-guard hold, used to decide when a masked main
        // body squad has departed far enough behind its guard.
        private readonly Dictionary<int, float> _rearGuardStartingSeparation = [];

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
        /// Replaces, rather than appends to, the current turn's deliberate pursuit pairings. A
        /// turn with no pursuit decisions therefore clears the previous turn's quarry choices.
        /// </summary>
        internal void ReplaceCurrentTurnPursuitPairings(
            IEnumerable<KeyValuePair<int, int>> pairings)
        {
            _pursuitTargetsBySquad.Clear();
            if (pairings == null) return;

            foreach (KeyValuePair<int, int> pairing in pairings)
            {
                _pursuitTargetsBySquad[pairing.Key] = pairing.Value;
            }
        }

        internal PursuitPosture GetPursuitPosture(BattleSide side) =>
            _pursuitPostures.GetValueOrDefault(side, PursuitPosture.BreakOff);

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
                PursuitPosture forcePosture = GetPursuitPosture(side);
                float quarryRunSpeed = enemySquads.Count == 0
                    ? 0
                    : enemySquads.Min(squad => squad.GetSquadMove());
                foreach (BattleSquad squad in disciplined)
                {
                    constraints[squad.Id] = new EngagementRoleConstraint(
                        forcePosture switch
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
        /// Re-evaluates the enemy's force-level pursuit posture toward a withdrawing or routing
        /// side. BreakOff returns a terminal request after the withdrawal has been cleared;
        /// callers apply that request through the resolver's outcome owner.
        /// </summary>
        internal BattleTerminalRequest EvaluatePursuitResponse(
            BattleSide withdrawingSide,
            List<BattleEvent> events)
        {
            ArgumentNullException.ThrowIfNull(events);
            BattleSide pursuingSide = Opposite(withdrawingSide);
            BattleSideState pursuing = GetSideState(pursuingSide);
            BattleForceMetrics pursuitMetrics = _roundMetrics.BuildMetrics(pursuingSide);
            BattleForceMetrics withdrawalMetrics = _roundMetrics.BuildMetrics(withdrawingSide);
            float separation = MinimumSeparation(pursuingSide, withdrawingSide);
            float speedAdvantage = Math.Max(
                0.01f,
                pursuitMetrics.FastestPursuitSquadSpeed - withdrawalMetrics.SlowestMainBodySquadSpeed);
            float pressTurns = separation / speedAdvantage;
            // The withdrawer "returns fire" only if some non-routing element still carries a
            // ranged weapon: a fully routed side shoots at no one, and a melee-only force
            // never could. Routing roles are already set when a rout triggers this
            // evaluation, so the flag reads the current turn's reality.
            bool withdrawerReturnsFire = GetActiveSquads(withdrawingSide).Any(
                squad => squad.WithdrawalRole != WithdrawalRole.Routing
                    && squad.AbleSoldiers.Any(
                        soldier => soldier.EquippedRangedWeapons.Count > 0));
            // Whether the pursuit can put someone in melee THIS turn, independent of whether it
            // can close over time. A pursuer already in contact, or one Run-and-charge away, has a
            // catch available right now, so the "cannot close" override must not talk it out of
            // taking it. The reach test is net of the quarry's own withdrawal — see
            // BattleContactRules.CanReachMeleeThisTurn for why measuring it against the pursuer's
            // raw move instead pinned this flag true for the whole of a stern chase.
            bool pursuerCanReachMeleeThisTurn =
                GetActiveSquads(pursuingSide).Any(squad => squad.IsInMelee)
                || BattleContactRules.CanReachMeleeThisTurn(
                    separation,
                    pursuitMetrics.FastestPursuitSquadSpeed,
                    withdrawalMetrics.SlowestMainBodySquadSpeed);
            float? projectedFollowShotTurns = ProjectedFollowShotTurns(
                pursuingSide,
                withdrawingSide,
                separation,
                withdrawalMetrics.SlowestMainBodySquadSpeed);
            BattlePursuitPlanner.Result result = BattlePursuitPlanner.Evaluate(new(
                _state.TurnNumber,
                pursuingSide == BattleSide.Attacker,
                pursuing.Aggression,
                pursuitMetrics.AbleSoldierCount,
                withdrawalMetrics.AbleSoldierCount,
                pursuitMetrics.FastestPursuitSquadSpeed,
                withdrawalMetrics.SlowestMainBodySquadSpeed,
                pressTurns,
                projectedFollowShotTurns,
                withdrawerReturnsFire,
                pursuerCanReachMeleeThisTurn));
            PursuitPosture? previous = _pursuitPostures.TryGetValue(
                pursuingSide,
                out PursuitPosture stored) ? stored : null;
            _pursuitPostures[pursuingSide] = result.Posture;
            if (result.Posture == PursuitPosture.BreakOff)
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

            pursuing.Intent = BattleSideIntent.Pursuing;
            if (previous == result.Posture) return null;
            events.Add(new BattleEvent(
                BattleEventType.PursuitStarted,
                _state.TurnNumber,
                pursuingSide,
                null,
                GetActiveSquads(withdrawingSide).Select(squad => squad.Id),
                previous.HasValue
                    ? $"{SideName(pursuingSide)} switched to a {result.Posture} pursuit."
                    : $"{SideName(pursuingSide)} began a {result.Posture} pursuit."));
            return null;
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
            foreach (BattleSquad withdrawing in GetActiveSquads(withdrawingSide).ToList())
            {
                List<BattleEscapeRules.Threat> threats = pursuers.Select(pursuer =>
                    new BattleEscapeRules.Threat(
                        pursuer.Id,
                        MinimumSquadSeparation(pursuer, withdrawing),
                        MaximumUsefulAttackRange(pursuer, withdrawing),
                        SafeSquadMove(pursuer),
                        SafeSquadMove(withdrawing)))
                    .ToList();
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
            BattleForceMetrics pursuerMetrics = _roundMetrics.BuildMetrics(pursuerSide);
            BattleForceMetrics withdrawalMetrics = _roundMetrics.BuildMetrics(withdrawingSide);
            float separation = MinimumSeparation(pursuerSide, withdrawingSide);
            float attackReach = MaximumOneTurnAttackReach(pursuerSide, withdrawingSide);
            float declaredPursuitSpeed = FastestDeclaredPursuitSpeed(pursuers);
            // Keep contact alive while the same projection used by BattlePursuitPlanner says a
            // worthwhile shot is available now. The squad planner may spend several stationary
            // turns converting that opportunity into a fully aimed ShootAction.
            bool pursuersHaveReasonableShot = ProjectedFollowShotTurns(
                pursuerSide,
                withdrawingSide,
                separation,
                withdrawalMetrics.SlowestMainBodySquadSpeed) == 0f;
            BattleContactRules.Result forceResult = BattleContactRules.Evaluate(new(
                _state.TurnNumber,
                withdrawingSide == BattleSide.Attacker,
                pursuers.Count,
                posture == PursuitPosture.BreakOff,
                IsWithdrawalIntent(GetSideState(pursuerSide).Intent),
                separation,
                attackReach,
                declaredPursuitSpeed,
                withdrawalMetrics.SlowestMainBodySquadSpeed,
                state.RearGuardSquadId.HasValue,
                0,
                withdrawalMetrics.SlowestMainBodySquadSpeed,
                PursuersAttackedRecently: pursuerMetrics.HasViableDamagingActionRecently,
                PursuersHaveReasonableShot: pursuersHaveReasonableShot));
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
                        separation,
                        attackReach,
                        declaredPursuitSpeed,
                        squad.GetSquadMove(),
                        true,
                        Math.Max(0, current - start),
                        squad.GetSquadMove(),
                        PursuersAttackedRecently: pursuerMetrics.HasViableDamagingActionRecently,
                        PursuersHaveReasonableShot: pursuersHaveReasonableShot));
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
            float attackReach = MaximumOneTurnAttackReach(pursuerSide, withdrawingSide);
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
                    MobMoraleSupportEvaluator.ComputeSupport(
                        squad,
                        friendly,
                        GetAllSquads(side),
                        _grid,
                        _rules.FactionBehaviorRules,
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

        private static float FastestDeclaredPursuitSpeed(
            IReadOnlyCollection<BattleSquad> pursuers)
        {
            return pursuers
                .Where(squad => squad.LastEngagementOptionKind is
                    EngagementOptionKind.StepForward
                    or EngagementOptionKind.JogToward
                    or EngagementOptionKind.RunToward
                    or EngagementOptionKind.CloseToContact)
                .Select(squad => squad.AbleSoldiers
                    .Select(soldier => soldier.CurrentSpeed)
                    .DefaultIfEmpty(0)
                    .Min())
                .DefaultIfEmpty(0)
                .Max();
        }

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
        {
            return first.AbleSoldiers.SelectMany(a => second.AbleSoldiers.Select(b =>
            {
                float dx = a.TopLeft.Value.Item1 - b.TopLeft.Value.Item1;
                float dy = a.TopLeft.Value.Item2 - b.TopLeft.Value.Item2;
                return MathF.Sqrt((dx * dx) + (dy * dy));
            })).DefaultIfEmpty(float.MaxValue).Min();
        }

        private static (float Size, float Armor, float Constitution, float Evasion) TargetProfile(
            IReadOnlyCollection<BattleSoldier> soldiers)
        {
            if (soldiers.Count == 0) return (0, 0, 0, 0);
            return (
                (float)soldiers.Average(soldier => soldier.Soldier.Size),
                (float)soldiers.Average(soldier => soldier.Armor?.Template.ArmorProvided ?? 0),
                (float)soldiers.Average(soldier => soldier.Soldier.Constitution),
                (float)soldiers.Average(soldier => soldier.Soldier.Template.Species.RangedEvasion));
        }

        private (float Size, float Armor, float Constitution, float Evasion) ForceTargetProfile(
            BattleSide side)
        {
            List<BattleSoldier> soldiers = GetActiveSquads(side)
                .SelectMany(squad => squad.AbleSoldiers)
                .ToList();
            return TargetProfile(soldiers);
        }

        private static float MaximumUsefulAttackRange(
            BattleSquad pursuer,
            BattleSquad target)
        {
            (float size, float armor, float constitution, float evasion) =
                TargetProfile(target.AbleSoldiers);
            if (size <= 0) return 0;
            float ranged = pursuer.AbleSoldiers
                .Select(soldier => BattleModifiersUtil.CalculateOptimalDistance(
                    soldier, size, armor, constitution, evasion))
                .DefaultIfEmpty(0)
                .Max();
            // A melee-only pursuer still has an attack envelope once relative movement brings it
            // beside the quarry. The projected turns consume movement; this is the final contact
            // allowance, not an extra turn of free movement.
            return Math.Max(BattleContactRules.MeleeContactAllowance, ranged);
        }

        private float WorthwhileRangedReach(BattleSide side, BattleSide targetSide)
        {
            (float size, float armor, float constitution, float evasion) =
                ForceTargetProfile(targetSide);
            if (size <= 0) return 0;
            return GetActiveSquads(side).SelectMany(squad => squad.AbleSoldiers)
                .Select(soldier => BattleModifiersUtil.CalculateOptimalDistance(
                    soldier, size, armor, constitution, evasion))
                .DefaultIfEmpty(0)
                .Max();
        }

        private float MaximumOneTurnAttackReach(BattleSide side, BattleSide targetSide)
        {
            float meleeReach = GetActiveSquads(side).SelectMany(squad => squad.AbleSoldiers)
                .Select(soldier => soldier.GetMoveSpeed() + 1)
                .DefaultIfEmpty(0)
                .Max();
            return Math.Max(meleeReach, WorthwhileRangedReach(side, targetSide));
        }

        private float? ProjectedFollowShotTurns(
            BattleSide side,
            BattleSide targetSide,
            float separation,
            float quarrySpeed)
        {
            float reach = WorthwhileRangedReach(side, targetSide);
            if (reach <= 0) return null;
            if (separation <= reach) return 0;
            float jogSpeed = _roundMetrics.BuildMetrics(side).FastestPursuitSquadSpeed
                * SoldierMovementPlanner.JogSpeedMultiplier;
            float closingRate = jogSpeed - Math.Max(0, quarrySpeed);
            return closingRate <= 0 ? null : (separation - reach) / closingRate;
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
