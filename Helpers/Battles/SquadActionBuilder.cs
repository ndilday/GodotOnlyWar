using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// Owns the serial half of squad planning: declaration, movement commitment, and materializing
    /// the already-selected root descriptors into executable actions.
    ///
    /// <para>Choice has completed before this builder is called. The builder therefore does not
    /// score another option or ask the targeting code for a replacement action. Its writes to the
    /// live grid, soldiers, and action sink are intentionally confined to the serial planning
    /// barriers.</para>
    /// </summary>
    internal sealed class SquadActionBuilder
    {
        private const float WalkSpeedMultiplier = SoldierMovementPlanner.WalkSpeedMultiplier;
        private const float JogSpeedMultiplier = SoldierMovementPlanner.JogSpeedMultiplier;
        private const int RoutLineLength = 1_000;

        private readonly ActionSink _actions;
        private readonly BattleGridManager _grid;
        private readonly IReadOnlyDictionary<int, BattleSoldier> _soldierMap;
        private readonly IRNG _random;
        private readonly Action<string> _log;
        private readonly SoldierMovementPlanner _movement;
        private readonly MeleeStrikeEstimator _melee;
        private readonly MeleeActionBuilder _meleeBuilder;
        private readonly SquadRunUtilityActionBuilder _runUtility;
        private readonly SquadEngagementPolicy _policy;

        internal SquadActionBuilder(
            SquadPlanningServices services,
            ActionSink actions,
            IRNG random,
            SoldierMovementPlanner movement,
            MeleeStrikeEstimator melee,
            MeleeActionBuilder meleeBuilder,
            SquadRunUtilityActionBuilder runUtility,
            SquadEngagementPolicy policy)
        {
            ArgumentNullException.ThrowIfNull(services);
            _actions = actions ?? throw new ArgumentNullException(nameof(actions));
            _grid = services.Grid;
            _soldierMap = services.SoldierMap;
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _log = services.Log;
            _movement = movement ?? throw new ArgumentNullException(nameof(movement));
            _melee = melee ?? throw new ArgumentNullException(nameof(melee));
            _meleeBuilder = meleeBuilder
                ?? throw new ArgumentNullException(nameof(meleeBuilder));
            _runUtility = runUtility ?? throw new ArgumentNullException(nameof(runUtility));
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        internal void DeclareEngagementDecision(SquadEngagementDecision decision)
        {
            BattleSquad squad = decision.Squad;
            squad.MovementTier = decision.Chosen.Tier;
            squad.WithdrawalRole = decision.Frame.Role switch
            {
                EngagementSquadRole.Cover => WithdrawalRole.Cover,
                EngagementSquadRole.RearGuard => WithdrawalRole.RearGuard,
                EngagementSquadRole.Bound => WithdrawalRole.Bound,
                EngagementSquadRole.Routing => WithdrawalRole.Routing,
                _ => WithdrawalRole.None
            };
            squad.LastEngagementOptionKind = decision.Chosen.Kind;
            squad.LastScreenThreatSquadId = decision.Frame.ScreenThreatSquadId;
            squad.LastProtectedSquadId = decision.Frame.ProtectedSquadId;
            ApplyDeclaredMovementState(squad);

            // A declaration receives only the speed its feasible projection actually covers. This
            // prevents a blocked or zero-distance move from receiving free ranged evasion.
            float tierReference = squad.GetSquadMove() * (decision.Chosen.Tier switch
            {
                SquadMovementTier.Walk => WalkSpeedMultiplier,
                SquadMovementTier.Jog => JogSpeedMultiplier,
                SquadMovementTier.Run when squad.CanRun => 1f,
                SquadMovementTier.Run => JogSpeedMultiplier,
                SquadMovementTier.InMelee => 1f,
                _ => 0f
            });
            float fraction = tierReference <= 0
                ? 0
                : Math.Clamp(decision.Chosen.FeasibleSpeed / tierReference, 0, 1);
            foreach (BattleSoldier soldier in squad.AbleSoldiers)
            {
                soldier.CurrentSpeed *= fraction;
                if (soldier.CurrentSpeed <= 0) soldier.IsRunning = false;
            }
        }

        internal void BuildEngagementActions(SquadEngagementDecision decision)
        {
            BattleSquad squad = decision.Squad;
            EngagementOptionKind kind = decision.Chosen.Kind;
            BattleSquad primary = _policy.ResolvePrimary(
                decision.Frame,
                decision.RoleTargets,
                _soldierMap.Values.Select(soldier => soldier.BattleSquad)
                    .Where(candidate => candidate != null)
                    .DistinctBy(candidate => candidate.Id)
                    .ToList());
            // Four roles return before an action builder, so retain the existing trace point in
            // the materialization path. The policy owns the decision trace construction.
            _policy.LogEngagementOptions(decision);
            if (decision.Frame.Role == EngagementSquadRole.BreakOff) return;
            if (decision.Frame.Role == EngagementSquadRole.Routing)
            {
                PrepareRoutingActions(squad);
                return;
            }
            if (decision.Frame.Role == EngagementSquadRole.Bound)
            {
                PrepareBoundActions(squad, decision.Frame.FixedHeading ?? 0);
                return;
            }
            // CloseToContact is also the ordinary semantic "run until contact is possible" option
            // for distant squads. Only convert it into a deferred charge when contact is actually
            // reachable this turn; otherwise preserve the selected moving root action (reload,
            // ready, and similar run-legal utility) while making a normal directed move.
            if (squad.IsInMelee
                || kind == EngagementOptionKind.CloseToContact
                    && decision.Chosen.Tier == SquadMovementTier.InMelee)
            {
                if (primary == null) return;
                foreach (BattleSoldier soldier in squad.AbleSoldiers
                    .OrderBy(member => member.Soldier.Id))
                {
                    MeleeWeapon meleeWeaponToReady =
                        MeleeStrikeEstimator.GetFirstUsableMeleeWeapon(soldier);
                    if (soldier.EquippedMeleeWeapons.Count == 0 && meleeWeaponToReady != null)
                    {
                        _actions.Shoot.Add(new ReadyMeleeWeaponAction(
                            soldier,
                            meleeWeaponToReady));
                    }
                }
                _actions.Move.Add(new SquadChargeIntentAction(
                    squad,
                    primary,
                    state => _meleeBuilder.ResolveSquadChargeIntent(squad, primary, state)));
                return;
            }
            if (kind == EngagementOptionKind.Hold)
            {
                ExecutePlannedRootActions(decision);
            }
            else
            {
                PrepareDirectedMovingActions(squad, decision, primary);
            }
        }

        internal void PrepareMeleeActions(BattleSquad squad)
        {
            squad.MovementTier = SquadMovementTier.InMelee;
            ApplyDeclaredMovementState(squad);
            // it doesn't really matter what the soldiers want to do, it's time to flee or fight
            // TODO: evaluate running vs fighting
            foreach (BattleSoldier soldier in squad.AbleSoldiers)
            {
                if (_grid.IsAdjacentToEnemy(soldier.Soldier.Id))
                {
                    _meleeBuilder.AddMeleeActionsToBag(soldier);
                }
                else
                {
                    _meleeBuilder.AddChargeActionsToBag(soldier);
                }
            }
        }

        internal void PrepareBoundActions(BattleSquad squad, ushort withdrawalHeading)
        {
            squad.WithdrawalRole = WithdrawalRole.Bound;
            squad.MovementTier = SquadMovementTier.Run;
            ApplyDeclaredMovementState(squad);
            SquadMovementTier movementTier = squad.MovementTier;
            ValueTuple<int, int> direction = BattleForcePlanner.GetHeadingVector(withdrawalHeading);
            ValueTuple<int, int> movementLine =
                new(direction.Item1 * 10_000, direction.Item2 * 10_000);
            foreach (BattleSoldier soldier in squad.AbleSoldiers.OrderBy(s => s.Soldier.Id))
            {
                // A bound soldier caught in melee decides for himself whether to break contact.
                // Running is not free: he turns his back, so he defends with foot speed alone
                // (BattleSoldier.IsRunning). Withdrawal is an ordered movement, not a rout, so
                // unlike PrepareRoutingActions he is allowed the choice rather than pinned.
                if (_grid.IsAdjacentToEnemy(soldier.Soldier.Id)
                    && _melee.DecideMeleeDisengagement(soldier).Choice
                        == MeleeDisengagementChoice.StandAndFight)
                {
                    _meleeBuilder.AddMeleeActionsToBag(soldier);
                    continue;
                }

                _movement.AddMoveAction(
                    soldier,
                    GetMovementBudget(soldier, movementTier),
                    movementLine,
                    movementTier);
                _runUtility.AddPermittedRunUtilityActionToBag(soldier);
            }
        }

        internal void PrepareRoutingActions(BattleSquad squad)
        {
            squad.WithdrawalRole = WithdrawalRole.Routing;
            squad.MovementTier = SquadMovementTier.Run;
            ApplyDeclaredMovementState(squad);
            SquadMovementTier movementTier = squad.MovementTier;
            ValueTuple<int, int>? routLine = CalculateSquadRoutLine(squad);
            foreach (BattleSoldier soldier in squad.AbleSoldiers.OrderBy(s => s.Soldier.Id))
            {
                if (_grid.IsAdjacentToEnemy(soldier.Soldier.Id))
                {
                    // Pinned in melee — he fights because he cannot flee, not because he wants to.
                    _meleeBuilder.AddMeleeActionsToBag(soldier);
                    continue;
                }

                // No enemy this squad can locate: nothing to run from, so nobody moves.
                if (routLine == null) continue;
                _movement.AddMoveAction(
                    soldier,
                    GetMovementBudget(soldier, movementTier),
                    routLine.Value,
                    movementTier);
                // Deliberately no run-utility action: routing permits no voluntary actions.
            }
        }

        private void PrepareDirectedMovingActions(
            BattleSquad squad,
            SquadEngagementDecision decision,
            BattleSquad primary)
        {
            Dictionary<int, PlannedSoldierAction> actions = (decision.Chosen.RootActions ?? [])
                .ToDictionary(action => action.SoldierId);
            SquadMovementTier movementTier = decision.Chosen.Tier == SquadMovementTier.Run
                && !squad.CanRun
                    ? SquadMovementTier.Jog
                    : decision.Chosen.Tier;
            foreach (BattleSoldier soldier in squad.AbleSoldiers.OrderBy(member => member.Soldier.Id))
            {
                ValueTuple<int, int> line = _policy.MovementLineFor(
                    soldier,
                    decision.Chosen.Kind,
                    decision.Frame,
                    primary,
                    decision.Chosen.IntendedDestination);
                _movement.AddMoveAction(
                    soldier,
                    GetMovementBudget(soldier, movementTier),
                    line,
                    movementTier);
                if (actions.TryGetValue(soldier.Soldier.Id, out PlannedSoldierAction action))
                {
                    ExecutePlannedRootAction(action);
                }
            }
        }

        private void ExecutePlannedRootActions(SquadEngagementDecision decision)
        {
            foreach (PlannedSoldierAction action in (decision.Chosen.RootActions ?? [])
                .OrderBy(candidate => candidate.SoldierId))
            {
                ExecutePlannedRootAction(action);
            }
        }

        private void ExecutePlannedRootAction(PlannedSoldierAction plan)
        {
            if (!_soldierMap.TryGetValue(plan.SoldierId, out BattleSoldier soldier)) return;
            BattleSoldier target = plan.TargetId.HasValue
                && _soldierMap.TryGetValue(plan.TargetId.Value, out BattleSoldier foundTarget)
                    ? foundTarget
                    : null;
            RangedWeapon weapon = plan.WeaponTemplateId.HasValue
                ? soldier.EquippedRangedWeapons
                    .Concat(soldier.RangedWeapons)
                    .FirstOrDefault(candidate =>
                        candidate.Template.Id == plan.WeaponTemplateId.Value)
                : null;
            switch (plan.Kind)
            {
                case PlannedSoldierActionKind.Shoot when target != null && weapon != null:
                    soldier.TargetId = target.Soldier.Id;
                    _actions.Shoot.Add(new ShootAction(
                        soldier.Soldier.Id,
                        target.Soldier.Id,
                        weapon.Template.Id,
                        plan.Range,
                        plan.ShotsToFire,
                        plan.BulkMultiplier,
                        plan.AimMultiplier,
                        _grid,
                        _random));
                    break;
                case PlannedSoldierActionKind.Aim when target != null && weapon != null:
                    soldier.TargetId = target.Soldier.Id;
                    _actions.Shoot.Add(new AimAction(soldier, target, weapon, _log));
                    break;
                case PlannedSoldierActionKind.Reload when weapon != null:
                    _actions.Shoot.Add(new ReloadRangedWeaponAction(soldier, weapon));
                    break;
                case PlannedSoldierActionKind.Ready when weapon != null:
                    _actions.Shoot.Add(new ReadyRangedWeaponAction(soldier, weapon));
                    break;
                case PlannedSoldierActionKind.AreaAttack when target != null && weapon != null:
                    soldier.TargetId = target.Soldier.Id;
                    _actions.Shoot.Add(new AreaAttackAction(
                        soldier.Soldier.Id,
                        target.Soldier.Id,
                        weapon.Template.Id,
                        _grid,
                        _random));
                    break;
                case PlannedSoldierActionKind.BlastAttack when target != null && weapon != null:
                    soldier.TargetId = target.Soldier.Id;
                    _actions.Shoot.Add(new BlastAttackAction(
                        soldier.Soldier.Id,
                        target.Soldier.Id,
                        weapon.Template.Id,
                        plan.Range,
                        plan.BulkMultiplier,
                        _grid,
                        _random));
                    EmitPlanDiagnostic(plan);
                    break;
            }
            LogSoldierAction(soldier, plan, target, weapon);
        }

        private void LogSoldierAction(
            BattleSoldier soldier,
            PlannedSoldierAction plan,
            BattleSoldier target,
            RangedWeapon weapon)
        {
            if (_log == null || plan.Kind == PlannedSoldierActionKind.None) return;
            List<KeyValuePair<string, string>> fields =
            [
                BattleDecisionTrace.Field("soldier", soldier.Soldier.Id),
                BattleDecisionTrace.Field("name", soldier.Soldier.Name),
                BattleDecisionTrace.Field("squad", soldier.BattleSquad?.Id),
                BattleDecisionTrace.Field("action", plan.Kind),
                BattleDecisionTrace.Field("weapon", weapon?.Template.Name ?? "none"),
                BattleDecisionTrace.Field("target", target?.Soldier.Name ?? "none"),
                BattleDecisionTrace.Field("target_id", target?.Soldier.Id),
                BattleDecisionTrace.Field("range", plan.Range),
                BattleDecisionTrace.Field("shots", plan.ShotsToFire),
                BattleDecisionTrace.Field("enemy_bv", plan.ExpectedEnemyBattleValueRemoved),
                BattleDecisionTrace.Field("friendly_bv", plan.ExpectedFriendlyBattleValueLost),
                BattleDecisionTrace.Field("readiness", plan.ReadinessValue)
            ];
            string line = new BattleDecisionTrace("ACTION", fields).Render();
            lock (_log)
            {
                _log(line);
            }
        }

        private void EmitPlanDiagnostic(PlannedSoldierAction plan)
        {
            if (_log == null || plan.Diagnostic == null) return;
            lock (_log)
            {
                _log(plan.Diagnostic);
            }
        }

        private ValueTuple<int, int>? CalculateSquadRoutLine(BattleSquad squad)
        {
            float nearestDistance = float.MaxValue;
            ValueTuple<int, int>? threat = null;
            foreach (BattleSoldier soldier in squad.AbleSoldiers.OrderBy(s => s.Soldier.Id))
            {
                if (!soldier.TopLeft.HasValue) continue;
                float distance = _grid.GetNearestEnemy(
                    soldier.Soldier.Id,
                    out int closestEnemyId);
                if (closestEnemyId == -1 || distance >= nearestDistance) continue;
                nearestDistance = distance;
                threat = _grid.GetSoldierPosition(closestEnemyId)[0];
            }
            if (threat == null) return null;

            (float centroidX, float centroidY) = BattleEngagementFrameBuilder.Centroid(squad);
            float dx = centroidX - threat.Value.Item1;
            float dy = centroidY - threat.Value.Item2;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (length <= 0.0001f) return new ValueTuple<int, int>(0, RoutLineLength);
            return new ValueTuple<int, int>(
                (int)Math.Round(dx / length * RoutLineLength),
                (int)Math.Round(dy / length * RoutLineLength));
        }

        private void ApplyDeclaredMovementState(BattleSquad squad)
        {
            if (squad.MovementTier == SquadMovementTier.Run && !squad.CanRun)
            {
                squad.MovementTier = SquadMovementTier.Jog;
            }
            foreach (BattleSoldier soldier in squad.AbleSoldiers)
            {
                // Only the Run tier strips a soldier's melee guard (see BattleSoldier.IsRunning).
                // A soldier who subsequently stops to fight clears the flag in AddMeleeActionsToBag,
                // so the declaration here is a default, not a verdict.
                soldier.IsRunning = squad.MovementTier == SquadMovementTier.Run
                    && soldier.CanRun;
                switch (squad.MovementTier)
                {
                    case SquadMovementTier.Stationary:
                        soldier.CurrentSpeed = 0;
                        soldier.LeftoverMovement = 0;
                        break;
                    case SquadMovementTier.Walk:
                        soldier.CurrentSpeed = soldier.GetMoveSpeed() * WalkSpeedMultiplier;
                        break;
                    case SquadMovementTier.Jog:
                        soldier.CurrentSpeed = soldier.GetMoveSpeed() * JogSpeedMultiplier;
                        soldier.Aim = null;
                        break;
                    case SquadMovementTier.Run:
                        soldier.CurrentSpeed = soldier.GetMoveSpeed();
                        soldier.Aim = null;
                        break;
                    case SquadMovementTier.InMelee:
                        bool isAdjacentToEnemy = _grid.IsAdjacentToEnemy(soldier.Soldier.Id);
                        soldier.CurrentSpeed = isAdjacentToEnemy ? 0 : soldier.GetMoveSpeed();
                        if (isAdjacentToEnemy)
                        {
                            // Carry-over represents an interrupted continuous move. Once a
                            // soldier settles into direct melee, that move has ended; retaining
                            // its bank here can produce an oversized charge after contact breaks.
                            soldier.LeftoverMovement = 0;
                        }
                        soldier.Aim = null;
                        break;
                }
            }
        }

        private static float GetMovementBudget(BattleSoldier soldier, SquadMovementTier tier) =>
            SoldierMovementPlanner.GetMovementBudget(soldier, tier);
    }
}
