using System;
using System.Collections.Generic;
using System.Linq;

using OnlyWar.Models;
using OnlyWar.Models.Battles;

namespace OnlyWar.Helpers.Battles
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

        // Outcome construction needs routed squads after their live withdrawal role has been
        // cleared by disengagement, so this history remains battle-scoped and monotonic.
        private readonly HashSet<int> _everRoutedSquadIds = [];

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
            foreach (BattleSquad squad in GetActiveSquads(BattleSide.Attacker)
                .Concat(GetActiveSquads(BattleSide.Opposing)))
            {
                _ableCountAtTurnStart[squad.Id] = squad.AbleSoldiers.Count;
                if (squad.WithdrawalRole == WithdrawalRole.Routing)
                {
                    _routingAtTurnStart.Add(squad.Id);
                }
            }
        }

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
                    LogMoraleSkip(side, squad, skip);
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
                float mobSupport = MobMoraleSupportEvaluator.ComputeSupport(
                    squad,
                    friendly,
                    GetAllSquads(side),
                    _grid,
                    _execution.Rules.FactionBehaviorRules,
                    commandAura);
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
                    bool canSuppress = FactionCapabilities.HasMobMentality(squad?.Faction)
                        && leaderForSuppression != null
                        && !squad.MobSuppressionPending
                        && !squad.MobSuppressionCommitted;
                    if (canSuppress)
                    {
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
            BattleMoraleEvaluator.MoraleSkipReason skip)
        {
            if (!BattleLog.IsEnabled) return;
            BattleDecisionTrace trace = new("MORALE_EVAL", new List<KeyValuePair<string, string>>
            {
                BattleDecisionTrace.Field("turn", _state.TurnNumber),
                BattleDecisionTrace.Field("side", side == BattleSide.Attacker ? "first" : "second"),
                BattleDecisionTrace.Field("squad", squad.Id),
                BattleDecisionTrace.Field("skip", RenderSkip(skip)),
                BattleDecisionTrace.Field("outcome", squad.MoraleState)
            });
            BattleLog.Write(trace.Render());
        }

        private void LogMoraleEval(
            BattleSide side,
            BattleSquad squad,
            BattleMoraleEvaluator.MoraleCheckResult result,
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
                BattleDecisionTrace.Field("outcome", result.Outcome)
            });
            BattleLog.Write(trace.Render());
        }

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

    /// <summary>Morale's explicit handoff to the existing withdrawal/pursuit lifecycle.</summary>
    internal sealed record BattleSideRoutedTransition(
        BattleSide Side,
        IReadOnlyList<int> ActiveSquadIds);
}
