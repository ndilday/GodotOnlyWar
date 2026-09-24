using System;
using System.Collections.Generic;
using System.Linq;

using OnlyWar.Domain;
using OnlyWar.Battles.Models;

namespace OnlyWar.Battles
{
    /// <summary>
    /// Battle-scoped owner of morale bookkeeping, input gathering, checks, and morale events.
    ///
    /// <para>The service mutates the live battle state only for morale-owned state: squad morale,
    /// mob coercion commitments, routed roles, and the side intent created when every remaining
    /// squad has routed. It returns the side-routed transition instead of evaluating pursuit, so
    /// the resolver can preserve the existing immediate withdrawal-response boundary.</para>
    /// </summary>
    internal sealed class BattleMoraleService
    {
        private readonly BattleState _state;
        private readonly BattleGridManager _grid;
        private readonly BattleExecutionContext _execution;

        // Starting able strength per squad drives cumulative casualty stress. These are battle
        // lifetime values, unlike the turn-start snapshot below.
        private readonly Dictionary<int, int> _startingAbleCount = [];
        private readonly HashSet<int> _squadStartedWithLeader = [];

        // Turn-start values are replaced immediately before planning, matching the resolver's
        // former SnapshotTurnStartMoraleState call. Routing is intentionally a prior-turn view.
        private readonly Dictionary<int, int> _ableCountAtTurnStart = [];
        private readonly HashSet<int> _routingAtTurnStart = [];

        // Turn-start facts the check triggers compare against. A squad checks only on a turn
        // when one of them changed; see MoraleCheckTrigger.
        private readonly HashSet<int> _leaderAliveAtTurnStart = [];
        private readonly HashSet<int> _synapseCoveredAtTurnStart = [];
        private readonly HashSet<BattleSide> _commandAliveAtTurnStart = [];

        // The turn whose morale pass routed each squad. Every other squad on its side checks at
        // the next pass, so the result never depends on the order squads are checked in.
        private readonly Dictionary<int, int> _routedOnTurn = [];

        // Outcome construction needs routed squads after their live withdrawal role has been
        // cleared by disengagement, so this history remains battle-scoped and monotonic.
        private readonly HashSet<int> _everRoutedSquadIds = [];

        // The turn whose morale check last granted each squad a leader coercion. A leader who
        // has just spent a round coercing cannot do it again at the very next check: if the mob
        // still breaks, it routs. This is tracked here, not read from the squad's committed flag,
        // because the resolver clears that flag in end-of-turn cleanup, before the check runs.
        private readonly Dictionary<int, int> _coercionGrantedOnTurn = [];

        internal BattleMoraleService(
            BattleState state,
            BattleGridManager grid,
            BattleExecutionContext execution,
            IEnumerable<BattleSquad> startingSquads)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _execution = execution ?? throw new ArgumentNullException(nameof(execution));
            if (startingSquads == null) throw new ArgumentNullException(nameof(startingSquads));

            // Use the supplied battle roster, as the resolver did before extraction. In
            // particular, preserve initialization timing and the distinction between a squad
            // that began with a leader and one that merely has a leader later.
            foreach (BattleSquad squad in startingSquads)
            {
                _startingAbleCount[squad.Id] = squad.AbleSoldiers.Count;
                if (squad.Soldiers.Any(soldier => soldier.Soldier.Template.IsSquadLeader))
                {
                    _squadStartedWithLeader.Add(squad.Id);
                }
            }
        }

        /// <summary>
        /// Routed squads that have appeared during this battle. The read-only view is used by
        /// outcome construction after live squads may have been disengaged.
        /// </summary>
        internal IReadOnlyCollection<int> EverRoutedSquadIds => _everRoutedSquadIds;

        internal int StartingAbleCountFor(BattleSquad squad) =>
            _startingAbleCount.GetValueOrDefault(squad.Id, squad.AbleSoldiers.Count);

        internal int TurnStartAbleCountFor(BattleSquad squad) =>
            _ableCountAtTurnStart.GetValueOrDefault(squad.Id, squad.AbleSoldiers.Count);

        internal bool SquadStartedWithLeader(BattleSquad squad) =>
            _squadStartedWithLeader.Contains(squad.Id);

        internal void SnapshotTurnStart()
        {
            _ableCountAtTurnStart.Clear();
            _routingAtTurnStart.Clear();
            _leaderAliveAtTurnStart.Clear();
            _synapseCoveredAtTurnStart.Clear();
            _commandAliveAtTurnStart.Clear();
            foreach (BattleSide side in new[] { BattleSide.Attacker, BattleSide.Opposing })
            {
                List<BattleSquad> active = GetActiveSquads(side).ToList();
                if (HasLivingCommand(side))
                {
                    _commandAliveAtTurnStart.Add(side);
                }
                foreach (BattleSquad squad in active)
                {
                    _ableCountAtTurnStart[squad.Id] = squad.AbleSoldiers.Count;
                    if (squad.WithdrawalRole == WithdrawalRole.Routing)
                    {
                        _routingAtTurnStart.Add(squad.Id);
                    }
                    if (squad.SquadLeader != null)
                    {
                        _leaderAliveAtTurnStart.Add(squad.Id);
                    }
                    if (SynapseCoverageEvaluator.IsSynapseCovered(squad, active, _grid))
                    {
                        _synapseCoveredAtTurnStart.Add(squad.Id);
                    }
                }
            }
        }

        /// <summary>
        /// The events that make <paramref name="squad"/> check this turn. None means the squad
        /// rolls nothing, unless it is Shaken, in which case it rolls to rally.
        /// </summary>
        private MoraleCheckTrigger TriggersFor(
            BattleSquad squad,
            bool commandLostThisTurn,
            bool friendlySquadRoutedLastTurn)
        {
            MoraleCheckTrigger triggers = MoraleCheckTrigger.None;
            int currentAble = squad.AbleSoldiers.Count;
            if (currentAble < TurnStartAbleCountFor(squad)
                && currentAble < MoraleConstants.CasualtyCheckStrengthFraction * StartingAbleCountFor(squad))
            {
                triggers |= MoraleCheckTrigger.Casualties;
            }
            if (_leaderAliveAtTurnStart.Contains(squad.Id) && squad.SquadLeader == null)
            {
                triggers |= MoraleCheckTrigger.SquadLeaderLost;
            }
            if (commandLostThisTurn)
            {
                triggers |= MoraleCheckTrigger.BattleLeaderLost;
            }
            // The caller only asks about squads that are checking, so a squad covered at turn
            // start that reaches this point has lost its coverage.
            if (_synapseCoveredAtTurnStart.Contains(squad.Id))
            {
                triggers |= MoraleCheckTrigger.SynapseLost;
            }
            if (friendlySquadRoutedLastTurn)
            {
                triggers |= MoraleCheckTrigger.FriendlySquadRouted;
            }
            return triggers;
        }

        /// <summary>
        /// True while the side has a command-aura provider that is not destroyed. The liveness
        /// rule matches <see cref="CommandAuraEvaluator"/>: a disengaged provider is alive.
        /// </summary>
        private bool HasLivingCommand(BattleSide side) =>
            GetAllSquads(side).Any(squad => squad.SquadProvidesCommandAura
                && squad.Status != BattleSquadStatus.Eliminated
                && squad.AbleSoldiers.Count > 0);

        /// <summary>
        /// Evaluates one side from the force metrics captured before either side's morale effects
        /// apply. A non-null result means all remaining squads routed and the resolver must perform
        /// the existing pursuit response immediately before considering the other side.
        /// </summary>
        internal BattleSideRoutedTransition EvaluateSide(
            BattleSide side,
            BattleForceMetrics friendlyMetrics,
            BattleForceMetrics enemyMetrics,
            ICollection<BattleEvent> events)
        {
            ArgumentNullException.ThrowIfNull(friendlyMetrics);
            ArgumentNullException.ThrowIfNull(enemyMetrics);
            ArgumentNullException.ThrowIfNull(events);

            List<BattleSquad> friendly = GetActiveSquads(side)
                .OrderBy(squad => squad.Id)
                .ToList();
            List<BattleSquad> enemy = GetActiveSquads(Opposite(side)).ToList();
            float forceDisadvantage = BattleMoraleEvaluator.ComputeForceDisadvantage(
                friendlyMetrics.CurrentBattleValue,
                enemyMetrics.CurrentBattleValue,
                friendlyMetrics.BattleValueLostPreviousTwoRounds,
                enemyMetrics.BattleValueLostPreviousTwoRounds);
            bool commandLostThisTurn = _commandAliveAtTurnStart.Contains(side)
                && !HasLivingCommand(side);
            // Any friendly squad, at any range: squads are assumed to share a vox net.
            bool friendlySquadRoutedLastTurn = GetAllSquads(side).Any(candidate =>
                _routedOnTurn.TryGetValue(candidate.Id, out int routedOnTurn)
                && routedOnTurn == _state.TurnNumber - 1);

            foreach (BattleSquad squad in friendly)
            {
                BattleMoraleEvaluator.MoraleSkipReason skip =
                    BattleMoraleEvaluator.ShouldCheckMorale(squad, friendly, _grid);
                if (skip != BattleMoraleEvaluator.MoraleSkipReason.Check)
                {
                    // A synapse provider or covered squad is not shaken — hold it Steady. An
                    // already-routing squad is sticky (§6): leave its state untouched.
                    if (skip != BattleMoraleEvaluator.MoraleSkipReason.AlreadyRouting)
                    {
                        squad.MoraleState = MoraleState.Steady;
                    }
                    LogMoraleSkip(side, squad, RenderSkip(skip));
                    continue;
                }

                MoraleCheckTrigger triggers = TriggersFor(
                    squad, commandLostThisTurn, friendlySquadRoutedLastTurn);
                // With nothing to react to, a squad rolls nothing unless it is Shaken. A Shaken
                // squad rolls to rally: the roll can only restore it to Steady, never make it
                // worse, so a rally never routs a squad and never calls on mob coercion.
                bool rally = triggers == MoraleCheckTrigger.None
                    && squad.MoraleState == MoraleState.Shaken;
                if (triggers == MoraleCheckTrigger.None && !rally)
                {
                    LogMoraleSkip(side, squad, "no_trigger");
                    continue;
                }

                int startingAble = StartingAbleCountFor(squad);
                int turnStartAble = TurnStartAbleCountFor(squad);
                int currentAble = squad.AbleSoldiers.Count;
                float casualtyThisTurn = turnStartAble > 0
                    ? Math.Clamp((float)(turnStartAble - currentAble) / turnStartAble, 0f, 1f)
                    : 0f;
                float cumulativeCasualty = startingAble > 0
                    ? Math.Clamp((float)(startingAble - currentAble) / startingAble, 0f, 1f)
                    : 0f;
                bool leaderDead = SquadStartedWithLeader(squad) && squad.SquadLeader == null;
                float routingVisible = RoutingVisibleFriendlyFraction(squad, friendly);
                float localOutnumber = BattleMoraleEvaluator.ComputeLocalOutnumberRatio(
                    squad, friendly, enemy, _grid, MoraleConstants.VisualRange);
                float commandAura = CommandAuraSupport(squad, side);
                float mobSupport = MobSupport(squad, friendly, side, commandAura);
                MoraleState moraleBeforeCheck = squad.MoraleState;

                BattleSoldier leader = squad.SquadLeader;
                List<BattleMoraleEvaluator.SoldierMoraleInput> soldiers = squad.AbleSoldiers
                    .OrderBy(soldier => soldier.Soldier.Id)
                    .Select(soldier => new BattleMoraleEvaluator.SoldierMoraleInput(
                        soldier.Soldier.Id,
                        soldier.Soldier.Ego,
                        leader != null && soldier.Soldier.Id == leader.Soldier.Id))
                    .ToList();

                BattleMoraleEvaluator.MoraleCheckResult result = BattleMoraleEvaluator.Evaluate(
                    new BattleMoraleEvaluator.MoraleCheckInput(
                        soldiers,
                        casualtyThisTurn,
                        cumulativeCasualty,
                        leaderDead,
                        routingVisible,
                        localOutnumber,
                        commandAura,
                        forceDisadvantage,
                        mobSupport),
                    _execution.Random);

                if (rally)
                {
                    if (result.Outcome == MoraleState.Steady)
                    {
                        squad.MoraleState = MoraleState.Steady;
                    }
                    LogMoraleEval(
                        side,
                        squad,
                        result,
                        "rally",
                        squad.MoraleState,
                        casualtyThisTurn,
                        cumulativeCasualty,
                        leaderDead,
                        routingVisible,
                        localOutnumber,
                        commandAura,
                        forceDisadvantage,
                        mobSupport);
                    continue;
                }

                squad.MoraleState = result.Outcome;
                if (FactionCapabilities.HasMobMentality(squad?.Faction)
                    && MathF.Abs(mobSupport) > 0.0001f)
                {
                    events.Add(new BattleEvent(
                        BattleEventType.MobMoraleApplied,
                        _state.TurnNumber,
                        side,
                        squad.Id,
                        null,
                        $"{squad.Name} received mob morale support {mobSupport:+0.##;-0.##;0}.",
                        mobSupport));
                }
                if (result.Outcome == MoraleState.Routing)
                {
                    BattleSoldier leaderForSuppression = squad.SquadLeader;
                    bool coercedLastTurn = _coercionGrantedOnTurn.TryGetValue(
                            squad.Id, out int grantedOnTurn)
                        && grantedOnTurn == _state.TurnNumber - 1;
                    bool canSuppress = FactionCapabilities.HasMobMentality(squad?.Faction)
                        && leaderForSuppression != null
                        && !squad.MobSuppressionPending
                        && !coercedLastTurn;
                    if (canSuppress)
                    {
                        _coercionGrantedOnTurn[squad.Id] = _state.TurnNumber;
                        // The Routing result is ignored, not downgraded to Shaken. Preserve the
                        // state that existed before this check; the cost is represented by the
                        // pending full-round coercion commitment and its recorded attack.
                        squad.MoraleState = moraleBeforeCheck;
                        squad.MobSuppressionPending = true;
                        events.Add(new BattleEvent(
                            BattleEventType.MobLeaderSuppressionCommitted,
                            _state.TurnNumber,
                            side,
                            squad.Id,
                            null,
                            $"{squad.Name}'s leader will spend the next round coercing the mob."));
                    }
                    else
                    {
                        squad.WithdrawalRole = WithdrawalRole.Routing;
                        _everRoutedSquadIds.Add(squad.Id);
                        _routedOnTurn[squad.Id] = _state.TurnNumber;
                        events.Add(new BattleEvent(
                            BattleEventType.SquadRouted,
                            _state.TurnNumber,
                            side,
                            squad.Id,
                            null,
                            $"{squad.Name} broke and routed."));
                    }
                }
                LogMoraleEval(
                    side,
                    squad,
                    result,
                    RenderTriggers(triggers),
                    squad.MoraleState,
                    casualtyThisTurn,
                    cumulativeCasualty,
                    leaderDead,
                    routingVisible,
                    localOutnumber,
                    commandAura,
                    forceDisadvantage,
                    mobSupport);
            }

            // If every remaining active squad on the side is Routing, the side reflects Rout
            // intent so the existing contact-break machinery (which treats Rout as a withdrawal
            // intent) disengages it. Individual routs on an otherwise-fighting side keep the
            // side's intent; those squads flee via the planner's routing path.
            List<BattleSquad> active = GetActiveSquads(side).ToList();
            BattleSideState state = GetSideState(side);
            if (active.Count > 0
                && active.All(squad => squad.WithdrawalRole == WithdrawalRole.Routing)
                && state.Intent != BattleSideIntent.Rout
                && state.Intent != BattleSideIntent.Disengaged)
            {
                state.Intent = BattleSideIntent.Rout;
                state.WithdrawalStartedTurn ??= _state.TurnNumber;
                state.CoveringSquadId = null;
                state.RearGuardSquadId = null;
                BattleSideRoutedTransition transition = new(
                    side,
                    active.Select(squad => squad.Id).ToArray());
                events.Add(new BattleEvent(
                    BattleEventType.WithdrawalOrdered,
                    _state.TurnNumber,
                    side,
                    null,
                    transition.ActiveSquadIds,
                    $"{SideName(side)} broke and routed."));
                return transition;
            }

            return null;
        }

        /// <summary>Morale-owned command-aura input used by the live check and forecasts.</summary>
        internal float CommandAuraSupport(BattleSquad squad, BattleSide side) =>
            CommandAuraEvaluator.ComputeCommandAuraModifier(
                squad,
                GetAllSquads(side),
                _grid,
                _execution.Rules.Skills.Tactics);

        /// <summary>Morale-owned mob-support input used by the live check and forecasts.</summary>
        internal float MobSupport(
            BattleSquad squad,
            IEnumerable<BattleSquad> friendly,
            BattleSide side,
            float commandAura) =>
            MobMoraleSupportEvaluator.ComputeSupport(
                squad,
                friendly,
                GetAllSquads(side),
                _grid,
                _execution.Rules.FactionBehaviorRules,
                StartingAbleCountFor,
                _routingAtTurnStart,
                commandAura);

        internal float RoutingVisibleFriendlyFraction(
            BattleSquad squad,
            IEnumerable<BattleSquad> friendly) =>
            BattleMoraleEvaluator.ComputeRoutingVisibleFriendlyFraction(
                squad,
                friendly,
                _routingAtTurnStart,
                _grid,
                MoraleConstants.VisualRange);

        private void LogMoraleSkip(
            BattleSide side,
            BattleSquad squad,
            string skip)
        {
            if (!BattleLog.IsEnabled) return;
            BattleDecisionTrace trace = new("MORALE_EVAL", new List<KeyValuePair<string, string>>
            {
                BattleDecisionTrace.Field("turn", _state.TurnNumber),
                BattleDecisionTrace.Field("side", side == BattleSide.Attacker ? "first" : "second"),
                BattleDecisionTrace.Field("squad", squad.Id),
                BattleDecisionTrace.Field("skip", skip),
                BattleDecisionTrace.Field("outcome", squad.MoraleState)
            });
            BattleLog.Write(trace.Render());
        }

        private void LogMoraleEval(
            BattleSide side,
            BattleSquad squad,
            BattleMoraleEvaluator.MoraleCheckResult result,
            string trigger,
            MoraleState applied,
            float casualtyThisTurn,
            float cumulativeCasualty,
            bool leaderDead,
            float routingVisible,
            float localOutnumber,
            float commandAura,
            float forceDisadvantage,
            float mobSupport)
        {
            if (!BattleLog.IsEnabled) return;
            BattleDecisionTrace trace = new("MORALE_EVAL", new List<KeyValuePair<string, string>>
            {
                BattleDecisionTrace.Field("turn", _state.TurnNumber),
                BattleDecisionTrace.Field("side", side == BattleSide.Attacker ? "first" : "second"),
                BattleDecisionTrace.Field("squad", squad.Id),
                BattleDecisionTrace.Field("skip", "none"),
                BattleDecisionTrace.Field("trigger", trigger),
                BattleDecisionTrace.Field("casualty_this_turn", casualtyThisTurn),
                BattleDecisionTrace.Field("cumulative_casualty", cumulativeCasualty),
                BattleDecisionTrace.Field("leader_dead", leaderDead),
                BattleDecisionTrace.Field("routing_visible", routingVisible),
                BattleDecisionTrace.Field("local_outnumber", localOutnumber),
                // Signed §4.3 aura contribution: positive = support from a living HQ in
                // radius; negative = command-loss stress (every fielded HQ destroyed).
                BattleDecisionTrace.Field("command_aura", commandAura),
                BattleDecisionTrace.Field("mob_support", mobSupport),
                BattleDecisionTrace.Field("force_disadvantage", forceDisadvantage),
                BattleDecisionTrace.Field("shock", result.Shock),
                BattleDecisionTrace.Field("context", result.Context),
                BattleDecisionTrace.Field("stress", result.Stress),
                BattleDecisionTrace.Field("able", result.AbleSoldiers),
                BattleDecisionTrace.Field("fails", result.Fails),
                BattleDecisionTrace.Field("fail_fraction", result.FailFraction),
                BattleDecisionTrace.Field("leader_held", result.LeaderHeld),
                BattleDecisionTrace.Field("rout_threshold", result.RoutThreshold),
                BattleDecisionTrace.Field("shaken_threshold", result.ShakenThreshold),
                BattleDecisionTrace.Field("outcome", result.Outcome),
                // What the squad actually became: a rally never worsens it, and mob coercion
                // ignores a Routing roll.
                BattleDecisionTrace.Field("applied", applied)
            });
            BattleLog.Write(trace.Render());
        }

        private static string RenderTriggers(MoraleCheckTrigger triggers) =>
            string.Join("+", Enum.GetValues<MoraleCheckTrigger>()
                .Where(flag => flag != MoraleCheckTrigger.None && triggers.HasFlag(flag))
                .Select(flag => flag switch
                {
                    MoraleCheckTrigger.Casualties => "casualties",
                    MoraleCheckTrigger.SquadLeaderLost => "squad_leader_lost",
                    MoraleCheckTrigger.BattleLeaderLost => "battle_leader_lost",
                    MoraleCheckTrigger.SynapseLost => "synapse_lost",
                    MoraleCheckTrigger.FriendlySquadRouted => "friendly_squad_routed",
                    _ => flag.ToString()
                }));

        private static string RenderSkip(BattleMoraleEvaluator.MoraleSkipReason skip) => skip switch
        {
            BattleMoraleEvaluator.MoraleSkipReason.NoAbleSoldiers => "no_able_soldiers",
            BattleMoraleEvaluator.MoraleSkipReason.AlreadyRouting => "already_routing",
            BattleMoraleEvaluator.MoraleSkipReason.ProvidesSynapse => "provides_synapse",
            BattleMoraleEvaluator.MoraleSkipReason.SynapseCovered => "synapse_covered",
            _ => "none"
        };

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

        private static string SideName(BattleSide side) =>
            side == BattleSide.Attacker ? "First side" : "Second side";
    }

    /// <summary>
    /// The events that make a squad take a morale check at the end of a turn. A squad with none
    /// of them rolls nothing, except that a Shaken squad rolls to rally.
    /// </summary>
    [Flags]
    internal enum MoraleCheckTrigger
    {
        None = 0,
        /// <summary>The squad lost able soldiers this turn, below
        /// <see cref="MoraleConstants.CasualtyCheckStrengthFraction"/> of its starting strength.</summary>
        Casualties = 1,
        /// <summary>The squad's leader was alive at the start of the turn and is not now.</summary>
        SquadLeaderLost = 2,
        /// <summary>The side's last command-aura provider was destroyed this turn.</summary>
        BattleLeaderLost = 4,
        /// <summary>The squad was synapse-covered at the start of the turn and is not now.</summary>
        SynapseLost = 8,
        /// <summary>A friendly squad, at any range, routed in the previous turn's morale pass.</summary>
        FriendlySquadRouted = 16
    }

    /// <summary>Morale's explicit handoff to the existing withdrawal/pursuit lifecycle.</summary>
    internal sealed record BattleSideRoutedTransition(
        BattleSide Side,
        IReadOnlyList<int> ActiveSquadIds);
}
