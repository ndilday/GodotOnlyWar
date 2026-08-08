using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// Emits the melee half of a turn: strikes for soldiers already in contact, the point-blank
    /// shoot-instead-of-stab decision, charge movement, and the squad-level charge resolution that
    /// runs against the live post-movement grid.
    ///
    /// <para>Holds an <see cref="ActionSink"/> as well as a <see cref="SquadPlanningServices"/>,
    /// and unlike every scorer in this stack it MUTATES: it reserves grid squares, sets soldier
    /// speed and facing, and draws from the seeded RNG for the fractional attack. Order matters --
    /// <see cref="ResolveSquadChargeIntent"/> executes each move as it goes so later chargers see
    /// squares already taken -- so this is safe only on the resolver's serial action-building
    /// phase, never inside its parallel posture scan.</para>
    ///
    /// <para>What a melee is WORTH lives in <see cref="MeleeStrikeEstimator"/>; this class asks it
    /// rather than re-deriving anything.</para>
    /// </summary>
    internal sealed class MeleeActionBuilder
    {
        private readonly SquadPlanningServices _services;
        private readonly ActionSink _actions;
        private readonly RangedTargetSelector _ranged;
        private readonly MeleeStrikeEstimator _melee;
        private readonly SoldierMovementPlanner _movement;
        // Equip/reload housekeeping a soldier may do while closing. Cross-cutting rather than melee,
        // so it stays on the planner and arrives here as a callback.
        private readonly Action<BattleSoldier> _addRunUtility;

        private readonly BattleGridManager _grid;
        private readonly IReadOnlyDictionary<int, BattleSoldier> _soldierMap;
        private readonly IReadOnlyDictionary<int, MeleeWeaponTemplate> _meleeWeaponTemplates;
        private readonly IRNG _random;
        private readonly Action<string> _log;

        internal MeleeActionBuilder(
            SquadPlanningServices services,
            ActionSink actions,
            RangedTargetSelector ranged,
            MeleeStrikeEstimator melee,
            SoldierMovementPlanner movement,
            Action<BattleSoldier> addRunUtility)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _actions = actions ?? throw new ArgumentNullException(nameof(actions));
            _ranged = ranged ?? throw new ArgumentNullException(nameof(ranged));
            _melee = melee ?? throw new ArgumentNullException(nameof(melee));
            _movement = movement ?? throw new ArgumentNullException(nameof(movement));
            _addRunUtility = addRunUtility ?? throw new ArgumentNullException(nameof(addRunUtility));
            _grid = _services.Grid;
            _soldierMap = _services.SoldierMap;
            _meleeWeaponTemplates = _services.MeleeWeaponTemplates;
            _random = _services.Random;
            _log = _services.Log;
        }

        private bool IsPlaced(BattleSoldier soldier) => _services.IsPlaced(soldier);

        internal void AddMeleeActionsToBag(BattleSoldier soldier)
        {
            soldier.TargetId = null;
            soldier.CurrentSpeed = 0;
            // He has stopped and turned to fight, so he defends with skill and parry again even
            // if his squad declared a Run this turn.
            soldier.IsRunning = false;
            List<BattleSoldier> adjacentEnemies = _grid.GetAdjacentEnemies(soldier.Soldier.Id)
                .Select(enemyId => _soldierMap[enemyId])
                .Where(enemy => enemy.IsCombatEffective)
                .OrderBy(enemy => enemy.Soldier.Id)
                .ToList();
            if (adjacentEnemies.Count == 0)
            {
                throw new InvalidOperationException("Attempting to melee with no adjacent enemy");
            }

            IReadOnlyList<MeleeWeapon> projectedMeleeLoadout =
                _melee.GetProjectedMeleeLoadout(soldier);
            MeleeWeapon projectedPrimary = projectedMeleeLoadout.FirstOrDefault();
            MeleeWeapon projectedSecondary =
                MeleeStrikeEstimator.GetSecondaryMeleeWeapon(projectedMeleeLoadout);
            List<MeleeWeapon> plannedMeleeWeapons = BuildPlannedWeaponSequence(
                soldier,
                projectedPrimary,
                projectedSecondary);
            List<PlannedMeleeStrike> projectedStrikePlans = _melee.BuildStrikePlan(
                soldier,
                adjacentEnemies,
                plannedMeleeWeapons,
                didMove: false);

            if (TryAddGunAndBladeActions(soldier, projectedStrikePlans))
            {
                return;
            }

            float meleeScore = _melee.EstimateProjectedMeleeBattleValue(
                soldier,
                projectedStrikePlans,
                plannedMeleeWeapons);

            RangedTargetEvaluation pointBlankShot = SelectBestPointBlankRangedTarget(
                soldier,
                adjacentEnemies);
            TemplateFiringLineEvaluation pointBlankTemplate = _ranged.SelectBestTemplateFiringLine(
                soldier,
                adjacentEnemies);
            float bestRangedScore = Math.Max(
                pointBlankShot?.Score ?? float.MinValue,
                pointBlankTemplate?.Score ?? float.MinValue);
            float forfeitedParryRisk = pointBlankShot == null && pointBlankTemplate == null
                ? 0
                : _melee.EstimateForfeitedParryRisk(
                    soldier,
                    adjacentEnemies,
                    projectedMeleeLoadout);
            float pointBlankScore = bestRangedScore - forfeitedParryRisk;

            if (pointBlankTemplate != null
                && pointBlankTemplate.Score >= (pointBlankShot?.Score ?? float.MinValue)
                && pointBlankScore > meleeScore)
            {
                soldier.TargetId = pointBlankTemplate.Target.Soldier.Id;
                _actions.Shoot.Add(new AreaAttackAction(
                    soldier.Soldier.Id,
                    pointBlankTemplate.Target.Soldier.Id,
                    pointBlankTemplate.Weapon.Template.Id,
                    _grid,
                    _random));
                return;
            }

            if (pointBlankShot != null && pointBlankScore > meleeScore)
            {
                soldier.TargetId = pointBlankShot.Target.Soldier.Id;
                _actions.Shoot.Add(new ShootAction(
                    soldier.Soldier.Id,
                    pointBlankShot.Target.Soldier.Id,
                    pointBlankShot.Weapon.Template.Id,
                    pointBlankShot.Range,
                    pointBlankShot.ShotsToFire,
                    useBulk: true,
                    grid: _grid,
                    random: _random));
                return;
            }

            // Preserve the existing action economy: choosing a melee weapon that is not yet in
            // hand spends this turn readying it; an already-ready (or unarmed default) loadout
            // attacks using the exact strike plan that was scored above.
            MeleeWeapon meleeWeaponToReady = MeleeStrikeEstimator.GetFirstUsableMeleeWeapon(soldier);
            if (soldier.EquippedMeleeWeapons.Count == 0 && meleeWeaponToReady != null)
            {
                _actions.Shoot.Add(new ReadyMeleeWeaponAction(soldier, meleeWeaponToReady));
            }
            else if (projectedStrikePlans.Count > 0)
            {
                _actions.Melee.Add(new MeleeAttackAction(
                    soldier,
                    projectedStrikePlans,
                    didMove: false,
                    log: _log,
                    random: _random,
                    meleeWeaponTemplates: _meleeWeaponTemplates));
            }
        }

        // A soldier gripping both a one-handed gun and a one-handed melee weapon does not choose
        // between them: the strike costs him nothing, so he always makes it, and the sidearm shot
        // at his strike target joins it whenever its own net value is positive. The evaluation's
        // stray-shot term prices in the scrum he is standing in -- himself and his brothers
        // included -- so a non-positive score means the trigger pull is expected to cost his side
        // more than it removes from the enemy.
        private bool TryAddGunAndBladeActions(
            BattleSoldier soldier,
            List<PlannedMeleeStrike> strikePlans)
        {
            if (strikePlans.Count == 0
                || !soldier.EquippedMeleeWeapons.Any(
                    weapon => weapon.Template.Location == EquipLocation.OneHand))
            {
                return false;
            }
            RangedWeapon sidearm = RangedTargetSelector
                .OrderRangedByTemplateId(soldier.EquippedRangedWeapons)
                .FirstOrDefault(weapon => weapon.Template.Location == EquipLocation.OneHand
                    && !weapon.Template.IsTemplateWeapon
                    && !weapon.Template.IsBlastWeapon
                    && weapon.LoadedAmmo > 0);
            if (sidearm == null)
            {
                return false;
            }

            _actions.Melee.Add(new MeleeAttackAction(
                soldier,
                strikePlans,
                didMove: false,
                log: _log,
                random: _random,
                meleeWeaponTemplates: _meleeWeaponTemplates));

            BattleSoldier strikeTarget = _soldierMap[strikePlans[0].TargetId];
            float range = _grid.GetDistanceBetweenSoldiers(
                soldier.Soldier.Id,
                strikeTarget.Soldier.Id);
            if (range > sidearm.Template.MaximumRange)
            {
                return true;
            }
            RangedTargetEvaluation sidearmShot = _ranged.EvaluateRangedTarget(
                soldier,
                strikeTarget,
                sidearm,
                range,
                additionalToHitModifier: -sidearm.Template.Bulk);
            if (sidearmShot.Score > 0)
            {
                soldier.TargetId = strikeTarget.Soldier.Id;
                _actions.Shoot.Add(new ShootAction(
                    soldier.Soldier.Id,
                    strikeTarget.Soldier.Id,
                    sidearm.Template.Id,
                    range,
                    sidearmShot.ShotsToFire,
                    useBulk: true,
                    grid: _grid,
                    random: _random));
            }
            return true;
        }

        private RangedTargetEvaluation SelectBestPointBlankRangedTarget(
            BattleSoldier soldier,
            IReadOnlyList<BattleSoldier> adjacentEnemies)
        {
            RangedTargetEvaluation best = null;
            IReadOnlyList<RangedWeapon> sortedWeapons =
                RangedTargetSelector.OrderRangedByTemplateId(soldier.EquippedRangedWeapons);
            foreach (BattleSoldier target in adjacentEnemies.OrderBy(enemy => enemy.Soldier.Id))
            {
                float range = _grid.GetDistanceBetweenSoldiers(
                    soldier.Soldier.Id,
                    target.Soldier.Id);
                for (int weaponIndex = 0; weaponIndex < sortedWeapons.Count; weaponIndex++)
                {
                    RangedWeapon weapon = sortedWeapons[weaponIndex];
                    if (weapon.LoadedAmmo <= 0
                        || weapon.Template.IsTemplateWeapon
                        || range > weapon.Template.MaximumRange)
                    {
                        continue;
                    }

                    RangedTargetEvaluation evaluation = _ranged.EvaluateRangedTarget(
                        soldier,
                        target,
                        weapon,
                        range,
                        additionalToHitModifier: -weapon.Template.Bulk);
                    if (best == null || evaluation.Score > best.Score)
                    {
                        best = evaluation;
                    }
                }
            }

            return best;
        }

        internal void AddChargeActionsToBag(BattleSoldier soldier)
        {
            soldier.TargetId = null;
            if (_grid.IsAdjacentToEnemy(soldier.Soldier.Id))
            {
                // determine what sort of manuver to make
                AddMeleeActionsToBag(soldier);
            }
            else
            {
                // get stuck in
                // move adjacent to nearest enemy
                // TODO: handle when someone else in the same squad wants to use the same spot
                // TODO: probably by letting the one with the lower id have it, and the higher id has to
                float distance = _grid.GetNearestEnemy(soldier.Soldier.Id, out int closestEnemyId);
                float moveSpeed = SoldierMovementPlanner.GetMovementBudget(
                    soldier, SquadMovementTier.InMelee);
                ValueTuple<int, int> enemyPosition = _grid.GetSoldierPosition(closestEnemyId)[0];
                if (distance > moveSpeed + 1)
                {
                    ValueTuple<int, int> moveVector = new ValueTuple<int, int>(enemyPosition.Item1 - soldier.TopLeft.Value.Item1, enemyPosition.Item2 - soldier.TopLeft.Value.Item2);
                    // we can't make it to an enemy in one move
                    // soldier can't get there in one move, advance as far as possible
                    _movement.AddMoveAction(soldier, moveSpeed, moveVector, SquadMovementTier.InMelee);
                    _addRunUtility(soldier);
                }
                else
                {
                    ValueTuple<int, int> newPos = _grid.GetClosestOpenAdjacency(soldier.TopLeft.Value, enemyPosition);
                    BattleSquad oppSquad = _soldierMap[closestEnemyId].BattleSquad;
                    if (newPos == soldier.TopLeft.Value)
                    {
                        // find the next closest
                        // okay, this is one of those times where I made something because it made me feel smart,
                        // but it's probably unreadable so I should change it later
                        // basically, foreach soldier in the squad of the closest enemy, except the closest enemy (who we already checked)
                        // get their locations, and then sort it according to distance square
                        // PROTIP: SQRT is a relatively expensive operation, so sort by distance squares when it's about comparative, not absolute, distance
                        var map = oppSquad.AbleSoldiers
                            .Where(s => s.Soldier.Id != closestEnemyId)
                            .Select(s => new ValueTuple<int, ValueTuple<int, int>>(s.Soldier.Id, _grid.GetSoldierPosition(s.Soldier.Id)[0]))
                            .Select(t => new ValueTuple<int, ValueTuple<int, int>, ValueTuple<int, int>>(t.Item1, t.Item2, new ValueTuple<int, int>(t.Item2.Item1 - soldier.TopLeft.Value.Item1, t.Item2.Item2 - soldier.TopLeft.Value.Item2)))
                            .Select(u => new ValueTuple<int, ValueTuple<int, int>, int>(u.Item1, u.Item2, (u.Item3.Item1 * u.Item3.Item1 + u.Item3.Item2 * u.Item3.Item2)))
                            .OrderBy(u => u.Item3);
                        foreach (ValueTuple<int, ValueTuple<int, int>, int> soldierData in map)
                        {
                            newPos = _grid.GetClosestOpenAdjacency(soldier.TopLeft.Value, soldierData.Item2);
                            if (newPos != soldier.TopLeft.Value)
                            {
                                AddChargeActionsHelper(soldier, soldierData.Item1, soldier.TopLeft.Value, (float)Math.Sqrt(soldierData.Item3), oppSquad, newPos);
                                break;
                            }
                        }
                        if (newPos == soldier.TopLeft.Value)
                        {
                            // we weren't able to find an enemy to get near, guess we try to find someone to shoot, instead?
                            //Debug.Log("ISoldier in squad engaged in melee couldn't find anyone to attack");
                            ValueTuple<int, int> line = new ValueTuple<int, int>((short)(enemyPosition.Item1 - soldier.TopLeft.Value.Item1),
                                                                               (short)(enemyPosition.Item2 - soldier.TopLeft.Value.Item2));
                            // soldier can't get there in one move, advance as far as possible
                            _movement.AddMoveAction(soldier, moveSpeed, line, SquadMovementTier.InMelee);
                            _addRunUtility(soldier);
                        }
                    }
                    else
                    {
                        AddChargeActionsHelper(soldier, closestEnemyId, soldier.TopLeft.Value, distance, oppSquad, newPos);
                    }
                }
            }
        }

        internal IReadOnlyList<IAction> ResolveSquadChargeIntent(
            BattleSquad chargingSquad,
            BattleSquad targetSquad,
            BattleState state)
        {
            List<IAction> resolvedMovement = [];
            if (chargingSquad.Status != BattleSquadStatus.Active
                || targetSquad.Status != BattleSquadStatus.Active)
            {
                return resolvedMovement;
            }

            // Resolve in stable soldier order against the live post-movement grid. Each successful
            // placement immediately occupies its cells, so later members naturally select another
            // defender or another open adjacency instead of dog-piling one reserved square.
            List<BattleSoldier> initialTargets = targetSquad.AbleSoldiers
                .Where(IsPlaced)
                .ToList();
            foreach (BattleSoldier charger in chargingSquad.AbleSoldiers
                .Where(IsPlaced)
                .Select(soldier => new
                {
                    Soldier = soldier,
                    Distance = initialTargets
                        .Select(target => _grid.GetDistanceBetweenSoldiers(
                            soldier.Soldier.Id, target.Soldier.Id))
                        .DefaultIfEmpty(float.MaxValue)
                        .Min()
                })
                .OrderByDescending(candidate => candidate.Distance)
                .ThenBy(candidate => candidate.Soldier.Soldier.Id)
                .Select(candidate => candidate.Soldier))
            {
                List<BattleSoldier> targets = targetSquad.AbleSoldiers
                    .Where(IsPlaced)
                    .OrderBy(target => target.Soldier.Id)
                    .ToList();
                if (targets.Count == 0) break;

                List<BattleSoldier> adjacent = targets
                    .Where(target => _grid.GetDistanceBetweenSoldiers(
                        charger.Soldier.Id, target.Soldier.Id)
                        <= BattleContactRules.MeleeContactAllowance)
                    .ToList();
                if (adjacent.Count > 0)
                {
                    PrepareChargerForMelee(charger);
                    MeleeAttackAction attack = CreateMeleeAttackAction(
                        charger, adjacent, didMove: false);
                    if (attack != null) _actions.Melee.Add(attack);
                    continue;
                }

                float budget = SoldierMovementPlanner.GetMovementBudget(
                    charger, SquadMovementTier.InMelee);
                var approaches = targets
                    .Select(target =>
                    {
                        ValueTuple<int, int> position = _grid.GetSoldierPosition(
                            target.Soldier.Id)[0];
                        ValueTuple<int, int> adjacency = _grid.GetClosestOpenAdjacency(
                            charger.TopLeft.Value, position);
                        float distance = adjacency == charger.TopLeft.Value
                            ? float.MaxValue
                            : GridDistance(charger.TopLeft.Value, adjacency);
                        return new { Target = target, Position = position, Adjacency = adjacency, Distance = distance };
                    })
                    .OrderBy(candidate => candidate.Distance)
                    .ThenBy(candidate => candidate.Target.Soldier.Id)
                    .ToList();
                var reachable = approaches.FirstOrDefault(candidate =>
                    candidate.Distance <= budget + 0.0001f);
                BattleSoldier pursuedTarget = reachable?.Target
                    ?? targets.OrderBy(target => _grid.GetDistanceBetweenSoldiers(
                            charger.Soldier.Id, target.Soldier.Id))
                        .ThenBy(target => target.Soldier.Id)
                        .First();
                ValueTuple<int, int> pursuedPosition = _grid.GetSoldierPosition(
                    pursuedTarget.Soldier.Id)[0];
                ValueTuple<int, int> line;
                ValueTuple<int, int> destination;
                if (reachable != null)
                {
                    destination = reachable.Adjacency;
                    line = (
                        destination.Item1 - charger.TopLeft.Value.Item1,
                        destination.Item2 - charger.TopLeft.Value.Item2);
                }
                else
                {
                    line = (
                        pursuedPosition.Item1 - charger.TopLeft.Value.Item1,
                        pursuedPosition.Item2 - charger.TopLeft.Value.Item2);
                    ValueTuple<int, int> desired = _movement.CalculateMovementAlongLine(line, budget);
                    destination = (
                        charger.TopLeft.Value.Item1 + desired.Item1,
                        charger.TopLeft.Value.Item2 + desired.Item2);
                }

                ushort orientation = _movement.CalculateOrientationFromVector(
                    line, charger, SquadMovementTier.InMelee);
                destination = _movement.FindBestLocation(
                    charger,
                    charger.TopLeft.Value,
                    destination,
                    budget,
                    orientation);
                MoveAction move = new(
                    charger,
                    _grid,
                    charger.TopLeft.Value,
                    destination,
                    orientation,
                    budget);
                charger.CurrentSpeed = SoldierMovementPlanner.GetTierSpeed(
                    charger, SquadMovementTier.InMelee);
                move.Execute(state);
                if (move.Succeeded) resolvedMovement.Add(move);

                if (move.Succeeded
                    && pursuedTarget.IsCombatEffective
                    && IsPlaced(pursuedTarget)
                    && _grid.GetDistanceBetweenSoldiers(
                        charger.Soldier.Id, pursuedTarget.Soldier.Id)
                        <= BattleContactRules.MeleeContactAllowance)
                {
                    PrepareChargerForMelee(charger);
                    MeleeAttackAction attack = CreateMeleeAttackAction(
                        charger, [pursuedTarget], didMove: true, isCharge: true);
                    if (attack != null) _actions.Melee.Add(attack);
                }
            }
            return resolvedMovement;
        }

        private static float GridDistance(
            ValueTuple<int, int> first,
            ValueTuple<int, int> second)
        {
            int dx = first.Item1 - second.Item1;
            int dy = first.Item2 - second.Item2;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static void PrepareChargerForMelee(BattleSoldier soldier)
        {
            soldier.CurrentSpeed = 0;
            soldier.LeftoverMovement = 0;
            soldier.IsRunning = false;
        }

        private void AddChargeActionsHelper(BattleSoldier soldier, int closestEnemyId, ValueTuple<int, int> currentPosition, float distance, BattleSquad oppSquad, ValueTuple<int, int> newPos)
        {
            ValueTuple<int, int> move = new ValueTuple<int, int>(newPos.Item1 - currentPosition.Item1, newPos.Item2 - currentPosition.Item2);
            float moveSpeed = SoldierMovementPlanner.GetMovementBudget(
                soldier, SquadMovementTier.InMelee);
            if (distance > moveSpeed + 1)
            {
                // we can't make it to an enemy in one move
                // soldier can't get there in one move, advance as far as possible

                ValueTuple<int, int> realMove = _movement.CalculateMovementAlongLine(move, moveSpeed);
                _movement.AddMoveAction(soldier, moveSpeed, realMove, SquadMovementTier.InMelee);
                _addRunUtility(soldier);
            }
            else
            {
                //Debug.Log(soldier.Soldier.Name + " charging " + moveSpeed.ToString("F0"));
                soldier.CurrentSpeed = SoldierMovementPlanner.GetTierSpeed(
                    soldier, SquadMovementTier.InMelee);
                _grid.ReserveSpace(newPos);
                ushort orientation = _movement.CalculateOrientationFromVector(
                    move, soldier, SquadMovementTier.InMelee);
                _actions.Move.Add(new MoveAction(
                    soldier,
                    _grid,
                    currentPosition,
                    newPos,
                    orientation,
                    moveSpeed));
                MeleeWeapon meleeWeaponToReady =
                    MeleeStrikeEstimator.GetFirstUsableMeleeWeapon(soldier);
                if (soldier.EquippedMeleeWeapons.Count == 0 && meleeWeaponToReady != null)
                {
                    _actions.Shoot.Add(new ReadyMeleeWeaponAction(soldier, meleeWeaponToReady));
                }
                else
                {
                    BattleSoldier target = oppSquad.AbleSoldiers.Single(s => s.Soldier.Id == closestEnemyId);
                    MeleeAttackAction action = CreateMeleeAttackAction(
                        soldier,
                        [target],
                        didMove: true,
                        isCharge: true);
                    if (action != null)
                    {
                        _actions.Melee.Add(action);
                    }
                }
            }
        }

        private MeleeAttackAction CreateMeleeAttackAction(
            BattleSoldier soldier,
            IEnumerable<BattleSoldier> candidateTargets,
            bool didMove,
            bool isCharge = false)
        {
            List<BattleSoldier> targets = candidateTargets
                .Where(target => target != null && target.IsCombatEffective)
                .GroupBy(target => target.Soldier.Id)
                .Select(group => group.First())
                .OrderBy(target => target.Soldier.Id)
                .ToList();
            if (targets.Count == 0)
            {
                return null;
            }

            MeleeWeapon primaryWeapon = soldier.GetPrimaryMeleeWeapon(
                MeleeAttackAction.GetUnarmedWeapon(soldier));
            MeleeWeapon secondaryWeapon = soldier.GetSecondaryMeleeWeapon();
            List<MeleeWeapon> plannedWeapons = BuildPlannedWeaponSequence(soldier, primaryWeapon, secondaryWeapon);
            if (plannedWeapons.Count == 0)
            {
                return null;
            }

            List<PlannedMeleeStrike> strikePlans = _melee.BuildStrikePlan(
                soldier, targets, plannedWeapons, didMove);
            if (strikePlans.Count == 0)
            {
                return null;
            }

            LogMeleeAttack(soldier, strikePlans, targets, didMove, isCharge);
            return new MeleeAttackAction(
                soldier,
                strikePlans,
                didMove,
                _log,
                _random,
                _meleeWeaponTemplates,
                isCharge);
        }

        /// <summary>
        /// Per-soldier melee trace, the counterpart of the ACTION record on the ranged side.
        ///
        /// <para>Melee attacks never pass through <see cref="PlannedSoldierAction"/> -- they are
        /// built here and dropped straight into the melee bag -- so without this the melee half of
        /// every turn is invisible in a log that records the ranged half in full. The strike list is
        /// the interesting part: <see cref="MeleeStrikeEstimator.BuildStrikePlan"/> spreads a
        /// soldier's attacks across targets, moving on once cumulative take-out confidence clears
        /// the threshold, so which enemies a soldier split its blows between is a decision, not a
        /// detail.</para>
        /// </summary>
        private void LogMeleeAttack(
            BattleSoldier soldier,
            IReadOnlyList<PlannedMeleeStrike> strikePlans,
            IReadOnlyList<BattleSoldier> candidateTargets,
            bool didMove,
            bool isCharge)
        {
            if (_log == null) return;
            string line = new BattleDecisionTrace("MELEE", new List<KeyValuePair<string, string>>
            {
                BattleDecisionTrace.Field("soldier", soldier.Soldier.Id),
                BattleDecisionTrace.Field("name", soldier.Soldier.Name),
                BattleDecisionTrace.Field("squad", soldier.BattleSquad?.Id),
                BattleDecisionTrace.Field("charge", isCharge),
                BattleDecisionTrace.Field("did_move", didMove),
                BattleDecisionTrace.Field("candidates", candidateTargets.Count),
                BattleDecisionTrace.Field("strikes", strikePlans.Count),
                // weapon>target per strike, in swing order. Semicolon-separated: spaces are the
                // record format's field separator.
                BattleDecisionTrace.Field(
                    "plan",
                    string.Join(
                        ";",
                        strikePlans.Select(strike =>
                            $"{strike.WeaponName}>{strike.TargetName}")))
            }).Render();
            lock (_log)
            {
                _log(line);
            }
        }

        // The RNG-drawing sibling of MeleeStrikeEstimator.BuildProjectedWeaponSequence: this one
        // rolls the fractional attack, because it is building the strikes that will actually
        // resolve rather than scoring a hypothetical.
        private List<MeleeWeapon> BuildPlannedWeaponSequence(BattleSoldier soldier, MeleeWeapon primaryWeapon, MeleeWeapon secondaryWeapon)
        {
            int primaryAttackCount = DetermineAttackCount(soldier, primaryWeapon);
            List<MeleeWeapon> plannedWeapons = [];
            for (int i = 0; i < primaryAttackCount; i++)
            {
                plannedWeapons.Add(primaryWeapon);
            }

            if (secondaryWeapon != null)
            {
                plannedWeapons.Add(secondaryWeapon);
            }

            return plannedWeapons;
        }

        private int DetermineAttackCount(BattleSoldier soldier, MeleeWeapon weapon)
        {
            float attackCount = MeleeMath.CalculateBaseAttackCount(
                soldier.Soldier.AttackSpeed,
                weapon?.Template.AttackSpeedMultiplier
                    ?? MeleeWeaponTemplate.DefaultAttackSpeedMultiplier);
            int guaranteedAttacks = (int)Math.Floor(attackCount);
            float fractionalAttack = attackCount - guaranteedAttacks;
            if (_random.GetLinearDouble() < fractionalAttack)
            {
                guaranteedAttacks++;
            }

            return Math.Max(0, guaranteedAttacks);
        }
    }
}
