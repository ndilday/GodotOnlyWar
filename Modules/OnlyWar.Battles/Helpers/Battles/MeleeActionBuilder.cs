using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Battles.Actions;
using OnlyWar.Battles.Models;
using OnlyWar.Domain.Equippables;

namespace OnlyWar.Battles
{
    /// <summary>
    /// Emits the melee half of a turn: strikes for soldiers already in contact, the point-blank
    /// shoot-instead-of-stab decision, and the squad-level closing move that runs against the live
    /// post-movement grid.
    ///
    /// <para>Strikes and closing moves are built in DIFFERENT phases and must not be confused.
    /// <see cref="AddMeleeActionsToBag"/> runs at planning time against turn-start geometry;
    /// <see cref="ResolveSquadClosingMove"/> runs after the attack phase and builds movement only.
    /// A closing soldier who reaches contact is marked, and strikes on the next turn as a charge.
    /// See TDD §6.6.</para>
    ///
    /// <para>Holds an <see cref="ActionSink"/> as well as a <see cref="SquadPlanningServices"/>,
    /// and unlike every scorer in this stack it MUTATES: it reserves grid squares, sets soldier
    /// speed and facing, and draws from the seeded RNG for the fractional attack. Order matters --
    /// <see cref="ResolveSquadClosingMove"/> executes each move as it goes so later chargers see
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
        // so it arrives through the narrow run-utility builder operation.
        private readonly Action<BattleSoldier> _addRunUtility;

        private readonly BattleGridManager _grid;
        private readonly IReadOnlyDictionary<int, BattleSoldier> _soldierMap;
        private readonly IReadOnlyDictionary<int, MeleeWeaponTemplate> _meleeWeaponTemplates;
        private readonly IRNG _random;
        private readonly Action<string> _log;

        internal MeleeActionBuilder(
            SquadPlanningServices services,
            ActionSink actions,
            IRNG random,
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
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _log = _services.Log;
        }

        private bool IsPlaced(BattleSoldier soldier) => _services.IsPlaced(soldier);

        /// <summary>
        /// Exactly the precondition <see cref="AddMeleeActionsToBag"/> asserts. Call this before
        /// routing a soldier there; the bag throws rather than returning empty, deliberately, so
        /// every caller must agree with it about what counts as an enemy in contact.
        /// </summary>
        internal bool HasCombatEffectiveAdjacentEnemy(BattleSoldier soldier) =>
            _grid.GetAdjacentEnemies(soldier.Soldier.Id)
                .Any(enemyId => _soldierMap.TryGetValue(enemyId, out BattleSoldier enemy)
                    && enemy.IsCombatEffective);

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
            // A soldier who closed into contact on last turn's closing pass is finishing that
            // charge now: nothing happened between his arrival and this swing, so he swings
            // off-balance and without his parry (PRD §4.14).
            bool isCharge = soldier.ChargedIntoContactLastTurn;
            List<PlannedMeleeStrike> projectedStrikePlans = _melee.BuildStrikePlan(
                soldier,
                adjacentEnemies,
                plannedMeleeWeapons,
                didMove: isCharge);

            if (TryAddGunAndBladeActions(soldier, projectedStrikePlans, adjacentEnemies))
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
                AddMeleeAttack(soldier, projectedStrikePlans, adjacentEnemies, isCharge);
            }
        }

        private void AddMeleeAttack(
            BattleSoldier soldier,
            List<PlannedMeleeStrike> strikePlans,
            IReadOnlyList<BattleSoldier> candidateTargets,
            bool isCharge)
        {
            LogMeleeAttack(soldier, strikePlans, candidateTargets, isCharge, isCharge);
            _actions.Melee.Add(new MeleeAttackAction(
                soldier,
                strikePlans,
                didMove: isCharge,
                log: _log,
                random: _random,
                meleeWeaponTemplates: _meleeWeaponTemplates,
                isCharge: isCharge));
        }

        // A soldier gripping both a one-handed gun and a one-handed melee weapon does not choose
        // between them: the strike costs him nothing, so he always makes it, and the sidearm shot
        // at his strike target joins it whenever its own net value is positive. The evaluation's
        // stray-shot term prices in the scrum he is standing in -- himself and his brothers
        // included -- so a non-positive score means the trigger pull is expected to cost his side
        // more than it removes from the enemy.
        private bool TryAddGunAndBladeActions(
            BattleSoldier soldier,
            List<PlannedMeleeStrike> strikePlans,
            IReadOnlyList<BattleSoldier> adjacentEnemies)
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

            AddMeleeAttack(soldier, strikePlans, adjacentEnemies, soldier.ChargedIntoContactLastTurn);

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

        internal IReadOnlyList<IAction> ResolveSquadClosingMove(
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

                // Already in contact at turn start: he fought in this turn's attack phase and has
                // nowhere to go. Gate on the same predicate the attack phase used, so the two
                // agree about who counted as engaged.
                if (HasCombatEffectiveAdjacentEnemy(charger))
                {
                    PrepareChargerForMelee(charger);
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

                SoldierMovementProjection projection = _movement.ProjectMove(
                    charger,
                    budget,
                    line,
                    SquadMovementTier.InMelee,
                    targetPointOverride: destination);
                MoveAction move = _movement.CreateImmediateChargeMove(charger, projection);
                move.Execute(state);
                if (move.Succeeded) resolvedMovement.Add(move);

                // Reaching contact does NOT produce an attack here. This pass runs after the attack
                // phase, so the blow belongs to the next turn; mark the charge instead and let the
                // attack phase spend it. See BattleSoldier.ChargedIntoContactLastTurn.
                if (move.Succeeded && HasCombatEffectiveAdjacentEnemy(charger))
                {
                    PrepareChargerForMelee(charger);
                    charger.ChargedIntoContactLastTurn = true;
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
