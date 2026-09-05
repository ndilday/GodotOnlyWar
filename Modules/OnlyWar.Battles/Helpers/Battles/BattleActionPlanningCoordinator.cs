using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Models;
using OnlyWar.Models.Battles;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// Battle-scoped owner of one turn's squad-planning orchestration. It prepares the frozen read
    /// state, creates the shared planning memo and side planners, evaluates every squad decision,
    /// then materializes the already-selected decisions through the existing serial action path.
    ///
    /// <para>The lifecycle service still owns role constraints and pursuit-pairing state. The
    /// resolver obtains the constraints before calling this coordinator and applies the returned
    /// pairings before asking this coordinator to cross the declaration and action-construction
    /// barriers.</para>
    /// </summary>
    internal sealed class BattleActionPlanningCoordinator
    {
        private readonly BattleState _state;
        private readonly BattleGridManager _grid;
        private readonly BattleExecutionContext _execution;

        internal BattleActionPlanningCoordinator(
            BattleState state,
            BattleGridManager grid,
            BattleExecutionContext execution)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _execution = execution ?? throw new ArgumentNullException(nameof(execution));
        }

        /// <summary>
        /// Materializes the lazy injury, equipment, roster, and aggregate views that workers may
        /// read concurrently. This is also used by the pre-turn ambush-aim seeding pass, which
        /// uses the same read-side state as ordinary planning but does not build a turn plan.
        /// </summary>
        internal void PrepareParallelPlanningState()
        {
            foreach (BattleSoldier soldier in _state.Soldiers.Values)
            {
                soldier.PrepareForParallelPlanning();
            }
            foreach (BattleSquad squad in _state.ActiveAttackerSquads.Values
                .Concat(_state.ActiveOpposingSquads.Values))
            {
                squad.PrepareForParallelPlanning();
            }
        }

        /// <summary>
        /// Runs the choice half of a planning pass. Every choice is stored in its indexed result
        /// slot, so parallel completion order cannot affect the subsequent serial barriers.
        /// </summary>
        internal BattlePlanningPassResult PlanDecisions(
            IReadOnlyCollection<BattleSquad> attacker,
            IReadOnlyCollection<BattleSquad> opposing,
            IReadOnlyDictionary<int, EngagementRoleConstraint> constraints,
            ICollection<IAction> shootActions,
            ICollection<IAction> moveActions,
            ICollection<IAction> meleeActions,
            Action<string> log)
        {
            ArgumentNullException.ThrowIfNull(attacker);
            ArgumentNullException.ThrowIfNull(opposing);
            ArgumentNullException.ThrowIfNull(constraints);
            ArgumentNullException.ThrowIfNull(shootActions);
            ArgumentNullException.ThrowIfNull(moveActions);
            ArgumentNullException.ThrowIfNull(meleeActions);

            PrepareParallelPlanningState();

            // One fresh memo for the whole planning pass. Movement, reloads, and casualties do not
            // occur until after this coordinator returns and action execution begins.
            BattlePlanningContext planningContext = new();
            List<BattleSquad> orderedAttacker = attacker
                .OrderBy(squad => squad.Id)
                .ToList();
            List<BattleSquad> orderedOpposing = opposing
                .OrderBy(squad => squad.Id)
                .ToList();
            ActionSink actions = new(shootActions, moveActions, meleeActions);
            Dictionary<BattleSide, BattleSquadPlanner> planners = new()
            {
                [BattleSide.Attacker] = CreateSquadPlanner(
                    BattleSide.Attacker, actions, log, planningContext),
                [BattleSide.Opposing] = CreateSquadPlanner(
                    BattleSide.Opposing, actions, log, planningContext)
            };

            BattleEngagementFrameBuilder.PairedFrame paired =
                BattleEngagementFrameBuilder.Build(orderedAttacker, orderedOpposing, constraints);
            LogScreenEvaluations(
                BattleSide.Attacker, orderedAttacker, orderedOpposing, paired);
            LogScreenEvaluations(
                BattleSide.Opposing, orderedOpposing, orderedAttacker, paired);

            // The force-level horizon is deliberately initialized before workers start. The
            // worker choice path retains its standalone lazy fallback for direct planner tests.
            planners[BattleSide.Attacker].InitializeEngagementHorizon(
                paired.Profiles,
                paired.Frames,
                _execution.MaxPlanningDegreeOfParallelism);

            List<PlanningJob> jobs = [];
            foreach ((BattleSide side, List<BattleSquad> friendly, List<BattleSquad> enemy) in new[]
            {
                (BattleSide.Attacker, orderedAttacker, orderedOpposing),
                (BattleSide.Opposing, orderedOpposing, orderedAttacker)
            })
            {
                foreach (BattleSquad squad in friendly.OrderBy(candidate => candidate.Id))
                {
                    jobs.Add(new PlanningJob(side, planners[side].EngagementPolicy, enemy, squad));
                }
            }

            // Every job has one stable result slot. No worker writes actions, declarations, or
            // lifecycle state; those operations begin only after all choices have been made.
            (BattleSide Side, SquadEngagementDecision Decision)[] decisionResults =
                new (BattleSide, SquadEngagementDecision)[jobs.Count];
            void ChooseAt(int index)
            {
                PlanningJob job = jobs[index];
                EngagementRoleConstraint constraint = constraints.GetValueOrDefault(job.Squad.Id)
                    ?? new EngagementRoleConstraint(EngagementSquadRole.Normal);
                SquadEngagementDecision decision = job.Policy.ChooseEngagementOption(
                    job.Squad,
                    paired.Frames[job.Squad.Id],
                    paired.Profiles,
                    paired.Frames,
                    job.Enemy,
                    constraint.RoleTargets);
                decisionResults[index] = (job.Side, decision);
            }

            if (_execution.MaxPlanningDegreeOfParallelism <= 1 || jobs.Count <= 1)
            {
                for (int index = 0; index < jobs.Count; index++) ChooseAt(index);
            }
            else
            {
                Parallel.For(
                    0,
                    jobs.Count,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = _execution.MaxPlanningDegreeOfParallelism
                    },
                    ChooseAt);
            }

            List<(BattleSide Side, SquadEngagementDecision Decision)> decisions =
                decisionResults.ToList();
            List<KeyValuePair<int, int>> pursuitPairings = decisions
                .Where(entry => entry.Decision.Frame.Role is
                    EngagementSquadRole.Pursuit
                    or EngagementSquadRole.Follow
                    or EngagementSquadRole.Press
                    or EngagementSquadRole.Standoff)
                .Where(entry => entry.Decision.Frame.PrimaryCounterpartSquadId is not null)
                .Select(entry => new KeyValuePair<int, int>(
                    entry.Decision.Squad.Id,
                    entry.Decision.Frame.PrimaryCounterpartSquadId.Value))
                .ToList();

            return new BattlePlanningPassResult(planners, decisions, pursuitPairings);
        }

        /// <summary>
        /// Crosses the two serial planning barriers. The caller applies the coordinator result's
        /// pursuit pairings before declaration so lifecycle state observes the same ordering
        /// as the pre-extraction resolver.
        /// </summary>
        internal void DeclareAndBuildActions(BattlePlanningPassResult pass)
        {
            ArgumentNullException.ThrowIfNull(pass);

            foreach ((BattleSide side, SquadEngagementDecision decision) in pass.Decisions
                .OrderBy(entry => entry.Side)
                .ThenBy(entry => entry.Decision.Squad.Id))
            {
                pass.Planners[side].DeclareEngagementDecision(decision);
            }
            foreach ((BattleSide side, SquadEngagementDecision decision) in pass.Decisions
                .OrderBy(entry => entry.Side)
                .ThenBy(entry => entry.Decision.Squad.Id))
            {
                pass.Planners[side].BuildEngagementActions(decision);
            }
        }

        private BattleSquadPlanner CreateSquadPlanner(
            BattleSide side,
            ActionSink actions,
            Action<string> log,
            BattlePlanningContext planningContext)
        {
            BattleSquadPlanner planner = new(
                _grid,
                _state.Soldiers,
                actions.Shoot,
                actions.Move,
                actions.Melee,
                log,
                _execution.Rules.MeleeWeaponTemplates,
                _execution.Random,
                planningContext,
                _execution.Rules.Skills.Tactics)
            {
                TraceTurnNumber = _state.TurnNumber,
                TraceSideLabel = side == BattleSide.Attacker ? "first" : "second"
            };
            return planner;
        }

        private void LogScreenEvaluations(
            BattleSide side,
            IReadOnlyCollection<BattleSquad> friendly,
            IReadOnlyCollection<BattleSquad> enemy,
            BattleEngagementFrameBuilder.PairedFrame paired)
        {
            if (!BattleLog.IsEnabled) return;
            float forceBv = friendly.Sum(squad =>
                paired.Profiles.GetValueOrDefault(squad.Id)?.TotalAbleBattleValue ?? 0);
            float committed = friendly
                .Where(squad => paired.Frames.GetValueOrDefault(squad.Id)?.ScreenThreatSquadId != null)
                .Sum(squad => paired.Profiles[squad.Id].TotalAbleBattleValue);
            foreach (BattleSquad screener in friendly.OrderBy(squad => squad.Id))
            {
                SquadEngagementFrame frame = paired.Frames[screener.Id];
                BattleSquadCapabilityProfile profile = paired.Profiles[screener.Id];
                foreach (BattleSquad threat in enemy
                    .Where(squad => paired.Profiles[squad.Id].IsContactSeeking)
                    .OrderBy(squad => squad.Id))
                {
                    bool selected = frame.ScreenThreatSquadId == threat.Id;
                    int? protectedId = selected ? frame.ProtectedSquadId : null;
                    float noScreenLoss = Math.Min(
                        paired.Profiles[threat.Id].UsableMeleeBattleValue,
                        protectedId.HasValue
                            ? paired.Profiles[protectedId.Value].TotalAbleBattleValue
                            : 0);
                    float holding = selected
                        ? Math.Min(1f,
                            (profile.UsableMeleeBattleValue + profile.TotalAbleBattleValue * 0.25f)
                            / Math.Max(1, paired.Profiles[threat.Id].UsableMeleeBattleValue))
                        : 0;
                    BattleLog.Write(new BattleDecisionTrace("SCREEN_EVAL", new List<KeyValuePair<string, string>>
                    {
                        BattleDecisionTrace.Field("turn", _state.TurnNumber),
                        BattleDecisionTrace.Field("side", side),
                        BattleDecisionTrace.Field("threat", threat.Id),
                        BattleDecisionTrace.Field("screener", screener.Id),
                        BattleDecisionTrace.Field("protected", protectedId?.ToString() ?? "none"),
                        BattleDecisionTrace.Field("intercept_point", frame.InterposePoint?.ToString() ?? "none"),
                        BattleDecisionTrace.Field("no_screen_loss", noScreenLoss),
                        BattleDecisionTrace.Field("screened_loss", noScreenLoss * (1 - holding)),
                        BattleDecisionTrace.Field("capacity_consumed", selected ? profile.ContactCapacity : 0),
                        BattleDecisionTrace.Field("force_commitment", committed),
                        BattleDecisionTrace.Field("force_cap", forceBv * 0.4f),
                        BattleDecisionTrace.Field("incumbent", selected
                            && screener.LastScreenThreatSquadId == threat.Id),
                        BattleDecisionTrace.Field("selected", selected),
                        BattleDecisionTrace.Field("rejection", selected ? "none" : "not_selected")
                    }).Render());
                }
            }
        }

        private readonly record struct PlanningJob(
            BattleSide Side,
            SquadEngagementPolicy Policy,
            List<BattleSquad> Enemy,
            BattleSquad Squad);
    }

    /// <summary>
    /// The pass-scoped handoff between choice evaluation and serial barriers; models remain live.
    /// </summary>
    internal sealed class BattlePlanningPassResult
    {
        internal IReadOnlyDictionary<BattleSide, BattleSquadPlanner> Planners { get; }
        internal IReadOnlyList<(BattleSide Side, SquadEngagementDecision Decision)> Decisions { get; }
        internal IReadOnlyList<KeyValuePair<int, int>> PursuitPairings { get; }

        internal BattlePlanningPassResult(
            IReadOnlyDictionary<BattleSide, BattleSquadPlanner> planners,
            IReadOnlyList<(BattleSide Side, SquadEngagementDecision Decision)> decisions,
            IReadOnlyList<KeyValuePair<int, int>> pursuitPairings)
        {
            Planners = planners ?? throw new ArgumentNullException(nameof(planners));
            Decisions = decisions ?? throw new ArgumentNullException(nameof(decisions));
            PursuitPairings = pursuitPairings
                ?? throw new ArgumentNullException(nameof(pursuitPairings));
        }
    }
}
