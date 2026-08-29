using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// Ranged target selection and shot estimation: who a soldier shoots, with what, how many
    /// times, and what that shot is expected to remove.
    ///
    /// <para>A leaf of the planning stack. It reads <see cref="SquadPlanningServices"/> and calls
    /// <see cref="RemovalMath"/>; it never touches an <see cref="ActionSink"/>, never consults melee
    /// estimation, and never calls back into engagement scoring. That one-way dependency is why it
    /// could be extracted before the engagement evaluator that sits above it -- measured 2026-08-07
    /// as 21 calls downward from engagement scoring into here, and none upward.</para>
    /// </summary>
    internal sealed class RangedTargetSelector
    {
        // Shot count targets the same take-out confidence melee strike planning uses.
        private const float TargetTakeOutConfidenceThreshold = MeleeMath.TakeOutConfidenceTarget;
        private const int RangedTargetSquadCandidateCount = 3;
        private const float FullBulkMultiplier = SoldierMovementPlanner.FullBulkMultiplier;
        // Shared ranged-candidate cap: rifle, cone, and blast all score against the same top
        // handful of acquired targets (committed target first, then nearest) instead of each
        // rescanning the field independently.
        internal const int RangedCandidateEvaluationCount = 6;
        // TUNABLE (Phase 2 sticky targeting): a soldier keeps engaging the target it already
        // committed to (soldier.TargetId / soldier.Aim) across turns rather than rescanning the whole
        // field every turn, re-acquiring only when that target stops being a viable, worthwhile shot
        // or an un-engaged enemy is about to reach melee. "Worthwhile" reuses the planner's existing
        // floor: positive expected value and better than a one-in-ten chance to hit. Raising this
        // makes soldiers abandon marginal targets (and rescan) sooner.
        internal const float StickyMinimumHitProbability = 0.1f;
        // TUNABLE (Phase 3 fire distribution): base strength of the firing-lane preference that
        // spreads a squad's fire across the enemy frontage instead of piling every rifle onto the
        // single highest-value target. Each candidate target is penalized by this coefficient times
        // the lateral gap (in grid cells, perpendicular to the squad's engagement axis) between the
        // shooter's place in its own line and the target's place in the enemy line, then scaled by
        // the shooter faction's FireDiscipline. 0 disables the lane term and restores pre-Phase-3
        // targeting exactly. Retained after the take-out-probability conversion because it biases
        // target selection only and never changes the returned expected-value score.
        private const float BaseLaneSpreadCoefficient = 1.0f;
        // Fire discipline used when a squad has no faction (test fixtures, stray battle squads).
        private const float DefaultFireDiscipline = 0.5f;
        // The planner's "aim can no longer be improved" ceiling. A held aim is judged at this bonus
        // rather than its current one -- see IsExistingAimStillViable.
        internal const int FullAimBonusTurns = 3;

        private readonly SquadPlanningServices _services;
        // Aliases onto the bundle, named as the planner named them so the moved bodies read
        // unchanged against their original form.
        private readonly BattleGridManager _grid;
        private readonly IReadOnlyDictionary<int, BattleSoldier> _soldierMap;
        private readonly BattlePlanningContext _context;

        internal RangedTargetSelector(SquadPlanningServices services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _grid = _services.Grid;
            _soldierMap = _services.SoldierMap;
            _context = _services.Context;
        }

        private bool IsPlaced(BattleSoldier soldier) => _services.IsPlaced(soldier);

        private static float GetBattleValue(BattleSoldier soldier) =>
            SquadPlanningServices.BattleValueOf(soldier);

        private readonly struct RangedHitEstimateContext
        {
            private readonly float _weaponSkill;
            private readonly float _rangeModifier;
            private readonly float _sizeModifier;
            private readonly float _moveAndAimModifier;
            private readonly float _meleeModifier;
            private readonly float _targetEvasion;

            public RangedHitEstimateContext(
                BattleSoldier soldier,
                BattleSoldier target,
                RangedWeapon weapon,
                float range,
                float moveAndAimModifier,
                bool firingIntoMelee,
                float? targetSpeed = null)
            {
                _weaponSkill = soldier.Soldier.GetTotalSkillValue(weapon.Template.RelatedSkill);
                _rangeModifier = BattleModifiersUtil.CalculateRangeModifier(
                    range, targetSpeed ?? target.CurrentSpeed);
                _sizeModifier = BattleModifiersUtil.CalculateSizeModifier(target.Soldier.Size);
                _moveAndAimModifier = moveAndAimModifier;
                _meleeModifier = firingIntoMelee
                    ? RangedFriendlyFireRules.FiringIntoMeleePenalty
                    : 0;
                _targetEvasion = target.Soldier.Template.Species.RangedEvasion;
            }

            public float CalculatePreRollHitTotal(int numberOfShots)
            {
                // Preserve the original left-to-right floating-point expression exactly. These
                // values guide target and ammunition decisions, so even rounding-level changes can
                // alter a seeded battle at a threshold.
                float rateOfFireModifier = BattleModifiersUtil.CalculateRateOfFireModifier(numberOfShots);
                return _weaponSkill
                    + rateOfFireModifier
                    + _rangeModifier
                    + _sizeModifier
                    + _moveAndAimModifier
                    + _meleeModifier
                    - _targetEvasion;
            }
        }

        internal IReadOnlyList<BattleSoldier> BuildRankedRangedCandidates(
            BattleSoldier soldier,
            ValueTuple<int, int>? movementDirection)
        {
            int committedId = soldier.Aim?.Item1 ?? soldier.TargetId ?? -1;
            List<(BattleSoldier Soldier, float Distance)> ranked = [];
            foreach (BattleSquad squad in GetNearestInRangeEnemySquads(soldier, movementDirection))
            {
                foreach (BattleSoldier enemy in squad.AbleSoldiers)
                {
                    if (enemy == null || !enemy.IsCombatEffective || !IsPlaced(enemy))
                    {
                        continue;
                    }
                    float distance = _grid.GetDistanceBetweenSoldiers(
                        soldier.Soldier.Id, enemy.Soldier.Id);
                    ranked.Add((enemy, distance));
                }
            }
            ranked.Sort((first, second) =>
            {
                bool firstCommitted = first.Soldier.Soldier.Id == committedId;
                bool secondCommitted = second.Soldier.Soldier.Id == committedId;
                if (firstCommitted != secondCommitted)
                {
                    return firstCommitted ? -1 : 1;
                }
                int byDistance = first.Distance.CompareTo(second.Distance);
                return byDistance != 0
                    ? byDistance
                    : first.Soldier.Soldier.Id.CompareTo(second.Soldier.Soldier.Id);
            });
            int count = Math.Min(ranked.Count, RangedCandidateEvaluationCount);
            BattleSoldier[] result = new BattleSoldier[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = ranked[i].Soldier;
            }
            return result;
        }

        // Phase 2 sticky targeting. Replaces the former IsExistingAimStillBest, which reran the full
        // SelectBestRangedTarget scan every turn just to confirm the aim was still globally optimal.
        // Here the aim is kept while it stays viable and worthwhile — a hysteresis band that both
        // preserves the invested aim and skips the scan.
        internal bool IsExistingAimStillViable(BattleSoldier soldier)
        {
            if (soldier.Aim is not ValueTuple<int, RangedWeapon, int> aim
                || !_soldierMap.TryGetValue(aim.Item1, out BattleSoldier target)
                || !target.IsCombatEffective
                || !IsPlaced(target)
                || _grid.GetSoldierSide(aim.Item1) == _grid.GetSoldierSide(soldier.Soldier.Id))
            {
                return false;
            }

            RangedWeapon weapon = aim.Item2;
            if (weapon.LoadedAmmo <= 0 || !soldier.EquippedRangedWeapons.Contains(weapon))
            {
                return false;
            }

            float range = _grid.GetDistanceBetweenSoldiers(soldier.Soldier.Id, aim.Item1);
            if (range > weapon.Template.MaximumRange
                || ShouldInterruptStickyTarget(soldier, target))
            {
                return false;
            }

            // Judge the shot the aim is being HELD FOR, not the one available part-way through it.
            // Aiming exists to turn a marginal shot into a good one, so scoring a half-finished aim
            // at its current bonus condemns exactly the shots worth aiming for: the gate fails at
            // bonus 0, the aim is discarded, the re-acquire path decides aiming still beats
            // shooting and starts a fresh aim at 0, and the soldier loops forever without firing.
            // That is the "sits, aims, never fires" long-range stall — most visible on a Standoff
            // fire-support squad, which is stationary and far away by design. Using the full bonus
            // matches what the >= 3 branch will actually fire with (Accuracy + 3 + 1), so a shot
            // that will be worthwhile once lined up is allowed to mature, while one that is
            // hopeless even fully aimed is still dropped.
            RangedTargetEvaluation evaluation = EvaluateRangedTarget(
                soldier,
                target,
                weapon,
                range,
                weapon.Template.Accuracy + Math.Max(aim.Item3, FullAimBonusTurns) + 1);
            return evaluation.Score > 0 && evaluation.HitProbability > StickyMinimumHitProbability;
        }

        // Evaluates only the target the soldier already committed to (soldier.TargetId), skipping the
        // whole-field SelectBestRangedTarget scan. Returns the shot to take, or null to signal
        // "re-acquire" — the caller then falls back to a full scan. The per-target/weapon scoring
        // mirrors SelectBestRangedTarget's inner loop exactly, so a stuck result is identical to what
        // the scan would have produced for that target; only the target-selection hysteresis differs.
        internal RangedTargetEvaluation EvaluateStickyTarget(
            BattleSoldier soldier,
            float bulkMultiplier,
            ValueTuple<int, int>? movementDirection)
        {
            if (soldier.TargetId is not int committedId
                || !_soldierMap.TryGetValue(committedId, out BattleSoldier target)
                || !target.IsCombatEffective
                || !IsPlaced(target)
                || _grid.GetSoldierSide(committedId) == _grid.GetSoldierSide(soldier.Soldier.Id))
            {
                return null;
            }
            if (HasRestrictedJogFiringArc(movementDirection)
                && !IsWithinJogFiringArc(soldier, target, movementDirection.Value))
            {
                return null;
            }
            if (ShouldInterruptStickyTarget(soldier, target))
            {
                return null;
            }
            // A target that has since broken into a run is not the shot it was committed to: it
            // cannot shoot back, and somebody else on that side is now doing the shooting. Without
            // this, sticky targeting would hold every pursuer on the runner it first acquired and
            // the fleeing-target bias could never take effect. Releasing the commitment only
            // re-opens the choice — the full scan may well re-acquire the same man.
            //
            // Deliberately NOT in ShouldInterruptStickyTarget: that predicate is shared with
            // IsExistingAimStillViable, where a "no" throws away the soldier's accumulated aim.
            // Re-opening a target choice is free; resetting an aim to zero every turn means a
            // standing shooter can never reach the bonus it needs to fire at all.
            if (TargetSelectionWeight(target) < 1f)
            {
                return null;
            }

            float range = _grid.GetDistanceBetweenSoldiers(soldier.Soldier.Id, committedId);
            RangedTargetEvaluation best = null;
            IReadOnlyList<RangedWeapon> sortedWeapons =
                OrderRangedByTemplateId(soldier.EquippedRangedWeapons);
            for (int weaponIndex = 0; weaponIndex < sortedWeapons.Count; weaponIndex++)
            {
                RangedWeapon weapon = sortedWeapons[weaponIndex];
                if (weapon.LoadedAmmo <= 0
                    || weapon.Template.IsTemplateWeapon
                    || range > weapon.Template.MaximumRange)
                {
                    continue;
                }

                float toHitModifier = -weapon.Template.Bulk * bulkMultiplier;
                RangedTargetEvaluation evaluation = EvaluateRangedTarget(
                    soldier,
                    target,
                    weapon,
                    range,
                    toHitModifier);
                if (best == null || evaluation.Score > best.Score)
                {
                    best = evaluation;
                }
            }

            // Re-acquire once the committed target is no longer a worthwhile shot.
            return best != null
                && best.Score > 0
                && best.HitProbability > StickyMinimumHitProbability
                    ? best
                    : null;
        }

        // Emergency re-acquire trigger: an enemy other than the committed target is about to reach
        // melee this soldier while the committed target sits farther away. A soldier already adjacent
        // to an enemy is routed to the melee/charge planner upstream, so this only covers the turn
        // before contact — it stops a soldier from calmly plinking a distant target while a different
        // enemy closes the last stretch into his face.
        private bool ShouldInterruptStickyTarget(BattleSoldier soldier, BattleSoldier committedTarget)
        {
            float nearestRange = _grid.GetNearestEnemy(soldier.Soldier.Id, out int nearestId);
            if (nearestId == -1
                || nearestId == committedTarget.Soldier.Id
                || !_soldierMap.TryGetValue(nearestId, out BattleSoldier nearest))
            {
                return false;
            }

            float committedRange = _grid.GetDistanceBetweenSoldiers(
                soldier.Soldier.Id,
                committedTarget.Soldier.Id);
            return nearestRange < committedRange && nearest.GetMoveSpeed() >= nearestRange;
        }

        /// <summary>
        /// Scores every soldier in the three nearest in-range enemy squads and returns the
        /// target/weapon pair with the greatest expected battle-value swing.
        /// </summary>
        internal RangedTargetEvaluation SelectBestRangedTarget(
            BattleSoldier soldier,
            bool useBulk,
            bool includeExistingAim = false,
            ValueTuple<int, int>? movementDirection = null)
        {
            return SelectBestRangedTarget(
                soldier,
                useBulk ? FullBulkMultiplier : 0,
                includeExistingAim,
                movementDirection);
        }

        // Phase 3 fire distribution. Returns the shooter squad's engagement frame for the turn,
        // computing it once and memoizing per squad. The frame is a pure function of the frozen
        // layout, so every member of the squad shares it.
        private SquadEngagementGeometry GetSquadEngagementGeometry(BattleSquad squad)
        {
            if (squad == null)
            {
                return default;
            }
            if (_context.SquadGeometry.TryGetValue(squad.Id, out SquadEngagementGeometry cached))
            {
                return cached;
            }
            SquadEngagementGeometry geometry = ComputeSquadEngagementGeometry(squad);
            _context.SquadGeometry[squad.Id] = geometry;
            return geometry;
        }

        private SquadEngagementGeometry ComputeSquadEngagementGeometry(BattleSquad squad)
        {
            double sumX = 0;
            double sumY = 0;
            int count = 0;
            bool shooterSide = false;
            bool haveSide = false;
            foreach (BattleSoldier member in squad.AbleSoldiers)
            {
                if (member.TopLeft is not ValueTuple<int, int> position
                    || !_grid.IsSoldierPlaced(member.Soldier.Id))
                {
                    continue;
                }
                sumX += position.Item1;
                sumY += position.Item2;
                count++;
                if (!haveSide)
                {
                    shooterSide = _grid.GetSoldierSide(member.Soldier.Id);
                    haveSide = true;
                }
            }
            if (count == 0 || !haveSide)
            {
                return default;
            }

            double enemyX = 0;
            double enemyY = 0;
            int enemyCount = 0;
            foreach (BattleSoldier enemy in _soldierMap.Values)
            {
                if (!enemy.IsCombatEffective
                    || enemy.TopLeft is not ValueTuple<int, int> enemyPosition
                    || !_grid.IsSoldierPlaced(enemy.Soldier.Id)
                    || _grid.GetSoldierSide(enemy.Soldier.Id) == shooterSide)
                {
                    continue;
                }
                enemyX += enemyPosition.Item1;
                enemyY += enemyPosition.Item2;
                enemyCount++;
            }
            if (enemyCount == 0)
            {
                return default;
            }

            float centroidX = (float)(sumX / count);
            float centroidY = (float)(sumY / count);
            float enemyCentroidX = (float)(enemyX / enemyCount);
            float enemyCentroidY = (float)(enemyY / enemyCount);
            float axisX = enemyCentroidX - centroidX;
            float axisY = enemyCentroidY - centroidY;
            float axisLength = MathF.Sqrt((axisX * axisX) + (axisY * axisY));
            if (axisLength < 1e-4f)
            {
                // Squads occupy the same point (should not happen with living enemies); no axis.
                return default;
            }
            // Perpendicular to the engagement axis is the lateral ("along the frontage") direction.
            float perpX = -axisY / axisLength;
            float perpY = axisX / axisLength;

            float discipline = squad.Faction?.FireDiscipline ?? DefaultFireDiscipline;
            return new SquadEngagementGeometry(
                centroidX,
                centroidY,
                enemyCentroidX,
                enemyCentroidY,
                perpX,
                perpY,
                BaseLaneSpreadCoefficient * discipline);
        }

        // The shooter's own lateral position along its squad frontage — computed once per shooter.
        private static float ShooterLateralOffset(
            in SquadEngagementGeometry geometry,
            BattleSoldier soldier)
        {
            if (!geometry.Valid || soldier.TopLeft is not ValueTuple<int, int> position)
            {
                return 0f;
            }
            return ((position.Item1 - geometry.CentroidX) * geometry.PerpX)
                + ((position.Item2 - geometry.CentroidY) * geometry.PerpY);
        }

        // Penalty applied to a candidate's score so a shooter prefers the enemy in its own lane:
        // the lateral gap between where the shooter sits in its line and where the target sits in the
        // enemy line, scaled by the (discipline-weighted) spread coefficient.
        private static float LaneSpreadPenalty(
            in SquadEngagementGeometry geometry,
            float shooterLateral,
            BattleSoldier target)
        {
            if (!geometry.Valid
                || geometry.SpreadCoefficient <= 0f
                || target.TopLeft is not ValueTuple<int, int> position)
            {
                return 0f;
            }
            float targetLateral = ((position.Item1 - geometry.EnemyCentroidX) * geometry.PerpX)
                + ((position.Item2 - geometry.EnemyCentroidY) * geometry.PerpY);
            return geometry.SpreadCoefficient * MathF.Abs(shooterLateral - targetLateral);
        }

        /// <summary>
        /// TUNABLE: how heavily a fleeing target's expected damage is discounted when choosing whom
        /// to shoot. Bound and Routing squads are running, and a running squad cannot shoot at all,
        /// so in an organized withdrawal every round of return fire comes from the one Cover or
        /// RearGuard squad standing still. A pure expected-damage scorer happily spends the whole
        /// pursuit trading with the runners — the only enemies that cannot hurt it — while the
        /// covering squad fires back unopposed. At 0.5 a fleeing target has to look twice as
        /// valuable before it is preferred, so a badly exposed runner is still taken when it really
        /// is the better shot. 1.0 disables the bias.
        ///
        /// Like the lane-spread penalty it sits beside, this biases *selection* only: the returned
        /// evaluation keeps its true score, so a shot chosen this way still competes honestly
        /// against the template and blast options. Squads carry WithdrawalRole.None whenever nobody
        /// is withdrawing, so this is inert in an ordinary engagement.
        /// </summary>
        private const float FleeingTargetSelectionWeight = 0.5f;

        // The role is whatever the withdrawing side last planned. When that side plans second its
        // roles are a turn stale, which is still a good predictor: cover rotates only when the
        // incumbent becomes the closest squad.
        private static float TargetSelectionWeight(BattleSoldier target) =>
            target?.BattleSquad?.WithdrawalRole is WithdrawalRole.Bound or WithdrawalRole.Routing
                ? FleeingTargetSelectionWeight
                : 1f;

        internal RangedTargetEvaluation SelectBestRangedTarget(
            BattleSoldier soldier,
            float bulkMultiplier,
            bool includeExistingAim = false,
            ValueTuple<int, int>? movementDirection = null)
        {
            IReadOnlyList<RangedWeapon> equippedRanged = soldier?.EquippedRangedWeapons;
            if (equippedRanged == null || equippedRanged.Count == 0)
            {
                return null;
            }
            // The equipped list is tiny and its Template.Id ordering does not depend on the
            // per-target range, so sort it once here instead of rebuilding a LINQ Where/OrderBy
            // pipeline for every candidate target in the innermost loop. Ordering is preserved
            // exactly, keeping seeded tie-breaking stable.
            IReadOnlyList<RangedWeapon> sortedWeapons = OrderRangedByTemplateId(equippedRanged);

            // Phase 3: bias selection toward the enemy in the shooter's own firing lane so the squad
            // spreads its fire. The penalty affects only which target is picked, not the returned
            // evaluation's value (that still competes at its true score against template/blast options).
            SquadEngagementGeometry geometry = GetSquadEngagementGeometry(soldier.BattleSquad);
            float shooterLateral = ShooterLateralOffset(geometry, soldier);

            RangedTargetEvaluation best = null;
            float bestEffectiveScore = float.MinValue;
            foreach (BattleSquad candidateSquad in GetNearestInRangeEnemySquads(
                soldier,
                movementDirection))
            {
                foreach (BattleSoldier target in candidateSquad.AbleSoldiers
                    .Where(IsPlaced)
                    .OrderBy(candidate => candidate.Soldier.Id))
                {
                    float range = _grid.GetDistanceBetweenSoldiers(soldier.Soldier.Id, target.Soldier.Id);
                    float lanePenalty = LaneSpreadPenalty(geometry, shooterLateral, target);
                    for (int weaponIndex = 0; weaponIndex < sortedWeapons.Count; weaponIndex++)
                    {
                        RangedWeapon weapon = sortedWeapons[weaponIndex];
                        if (weapon.LoadedAmmo <= 0
                            || weapon.Template.IsTemplateWeapon
                            || range > weapon.Template.MaximumRange)
                        {
                            continue;
                        }

                        float toHitModifier = -weapon.Template.Bulk * bulkMultiplier;
                        if (includeExistingAim
                            && soldier.Aim?.Item1 == target.Soldier.Id
                            && soldier.Aim?.Item2.Template.Id == weapon.Template.Id)
                        {
                            toHitModifier += weapon.Template.Accuracy + soldier.Aim.Value.Item3 + 1;
                        }

                        RangedTargetEvaluation evaluation = EvaluateRangedTarget(
                            soldier,
                            target,
                            weapon,
                            range,
                            toHitModifier);
                        // Candidate squads, soldiers, and weapons are ordered nearest-first and
                        // deterministically, so an exact tie naturally stays on the closer option.
                        float effectiveScore =
                            (evaluation.Score * TargetSelectionWeight(target)) - lanePenalty;
                        if (best == null || effectiveScore > bestEffectiveScore)
                        {
                            best = evaluation;
                            bestEffectiveScore = effectiveScore;
                        }
                    }
                }
            }

            return best;
        }

        internal TemplateFiringLineEvaluation SelectBestTemplateFiringLine(
            BattleSoldier soldier,
            IEnumerable<BattleSoldier> candidateTargets = null,
            ValueTuple<int, int>? movementDirection = null)
        {
            IReadOnlyList<RangedWeapon> equippedRanged = soldier?.EquippedRangedWeapons;
            if (equippedRanged == null
                || equippedRanged.Count == 0
                || !IsPlaced(soldier))
            {
                return null;
            }
            IReadOnlyList<RangedWeapon> sortedWeapons = OrderRangedByTemplateId(equippedRanged);

            IEnumerable<BattleSoldier> targets = candidateTargets
                ?? GetNearestInRangeEnemySquads(soldier, movementDirection)
                    .SelectMany(candidateSquad => candidateSquad.AbleSoldiers);
            if (candidateTargets != null && HasRestrictedJogFiringArc(movementDirection))
            {
                ValueTuple<int, int> firingDirection = movementDirection.Value;
                targets = targets.Where(target => target != null
                    && IsWithinJogFiringArc(soldier, target, firingDirection));
            }
            bool shooterSide = _grid.GetSoldierSide(soldier.Soldier.Id);
            TemplateFiringLineEvaluation best = null;
            foreach (BattleSoldier target in targets
                .Where(target => target != null
                    && target.IsCombatEffective
                    && IsPlaced(target)
                    && _grid.GetSoldierSide(target.Soldier.Id) != shooterSide)
                .GroupBy(target => target.Soldier.Id)
                .Select(group => group.First())
                .OrderBy(target => target.Soldier.Id))
            {
                float range = _grid.GetDistanceBetweenSoldiers(
                    soldier.Soldier.Id,
                    target.Soldier.Id);
                for (int weaponIndex = 0; weaponIndex < sortedWeapons.Count; weaponIndex++)
                {
                    RangedWeapon weapon = sortedWeapons[weaponIndex];
                    if (!weapon.Template.IsConeWeapon
                        || weapon.LoadedAmmo <= 0
                        || range > weapon.Template.MaximumRange)
                    {
                        continue;
                    }

                    IReadOnlyList<int> victimIds = ConeTemplate.GetVictimIds(
                        _grid,
                        soldier.Soldier.Id,
                        target.Soldier.Id,
                        weapon.Template.MaximumRange,
                        weapon.Template.AreaRadius);
                    float expectedEnemyBattleValueRemoved = 0;
                    float expectedFriendlyBattleValueLost = 0;
                    foreach (int victimId in victimIds)
                    {
                        if (!_soldierMap.TryGetValue(victimId, out BattleSoldier victim))
                        {
                            continue;
                        }
                        if (!victim.IsCombatEffective)
                        {
                            // Incapacitated figures are still physically engulfed by the action,
                            // but their battle value has already been removed from the fight.
                            continue;
                        }

                        float victimRange = _grid.GetDistanceBetweenSoldiers(
                            soldier.Soldier.Id,
                            victimId);
                        float armor = victim.Armor?.Template.ArmorProvided ?? 0;
                        // Phase 5 graded fraction, matching the conventional ranged path so a cone
                        // burst and a rifle shot are quoted in the same currency.
                        float removalFraction = CalculateRangedRemovalFraction(
                            victim, weapon, victimRange, armor);
                        float expectedBattleValueRemoval =
                            removalFraction * GetBattleValue(victim);
                        if (_grid.GetSoldierSide(victimId) == shooterSide)
                        {
                            expectedFriendlyBattleValueLost += expectedBattleValueRemoval;
                        }
                        else
                        {
                            // Undiscounted, matching the conventional ranged path: this burst is
                            // fired now, so when the victim's squad would have reached us is
                            // irrelevant (Phase 3, Design/Reference/BattleLogic.md).
                            expectedEnemyBattleValueRemoved += expectedBattleValueRemoval;
                        }
                    }

                    TemplateFiringLineEvaluation evaluation = new(
                        target,
                        weapon,
                        range,
                        victimIds,
                        expectedEnemyBattleValueRemoved,
                        expectedFriendlyBattleValueLost);
                    // A zero-value burst wastes ammo, and a negative one knowingly trades
                    // more friendly value than it removes. Neither is a viable firing line.
                    if (evaluation.Score > 0 && (best == null || evaluation.Score > best.Score))
                    {
                        best = evaluation;
                    }
                }
            }

            return best;
        }

        internal RangedTargetEvaluation EvaluateRangedTarget(
            BattleSoldier soldier,
            BattleSoldier target,
            RangedWeapon weapon,
            float range,
            float additionalToHitModifier,
            float? targetSpeed = null)
        {
            float evaluatedTargetSpeed = targetSpeed ?? target.CurrentSpeed;
            var cacheKey = (
                soldier.Soldier.Id,
                target.Soldier.Id,
                weapon.Template.Id,
                BitConverter.SingleToInt32Bits(range),
                BitConverter.SingleToInt32Bits(additionalToHitModifier),
                BitConverter.SingleToInt32Bits(evaluatedTargetSpeed),
                (int)weapon.LoadedAmmo);
            if (_context.RangedEvaluations.TryGetValue(cacheKey, out RangedTargetEvaluation cached))
            {
                return cached;
            }

            ValueTuple<float, float, int, float, float> attackEstimate = EstimatePlannedRangedAttack(
                soldier,
                target,
                weapon,
                range,
                additionalToHitModifier,
                evaluatedTargetSpeed);
            float takeOutProbability = Math.Clamp(attackEstimate.Item2, 0, 1);
            // CONTRACT: the expected battle value this shot removes THIS TURN -- hit probability x
            // take-out probability x the target's battle value. It is deliberately undiscounted:
            // Phase 3 (Design/Reference/BattleLogic.md) removed the old
            // 1/(1 + turnsUntilTargetReachesUs) factor, which scaled a fired bolt's worth by when
            // its target would reach us. Arrival time does not affect whether a bolt lands, and the
            // temporal preference it was standing in for is already carried by
            // EngagementFutureDiscount. Distance enters through CalculateRangeModifier, not here.
            // Phase 5: the multiplier is the GRADED removal fraction, not the bare take-out
            // probability -- a hit that softens a target it cannot yet kill has now done something.
            // At lambda = 0 RemovalMath.CombineRemovalFraction is the clamp this line already applied.
            // The burst, not one hit: RemovalMath.ExpectedBurstRemovalFraction integrates the recoil loop
            // ShootAction actually resolves, so a nine-round bolt burst is no longer priced as a
            // single bolt. Unchanged for a one-shot weapon.
            float enemyBattleValueRemoved = RemovalMath.ExpectedBurstRemovalFraction(
                    attackEstimate.Item4,
                    attackEstimate.Item3,
                    weapon.Template.Recoil,
                    RemovalMath.CombineRemovalFraction(attackEstimate.Item2, attackEstimate.Item5))
                * GetBattleValue(target);
            float friendlyBattleValueLost = CalculateExpectedFriendlyStrayCost(
                soldier,
                target,
                weapon,
                range,
                additionalToHitModifier,
                attackEstimate.Item3);

            RangedTargetEvaluation result = new RangedTargetEvaluation(
                target,
                weapon,
                range,
                attackEstimate.Item3,
                attackEstimate.Item1,
                attackEstimate.Item2,
                enemyBattleValueRemoved,
                friendlyBattleValueLost,
                attackEstimate.Item4,
                evaluatedTargetSpeed,
                attackEstimate.Item5);
            _context.RangedEvaluations[cacheKey] = result;
            return result;
        }

        private IReadOnlyList<BattleSquad> GetNearestInRangeEnemySquads(
            BattleSoldier shooter,
            ValueTuple<int, int>? movementDirection = null)
        {
            // The nearest in-range enemy squads are a pure function of the frozen layout, the
            // shooter, and the firing direction, yet SelectBestRangedTarget and
            // SelectBestTemplateFiringLine each request them with the same arguments (and again
            // across planning phases). Memoize per (shooter, direction) for the turn.
            var cacheKey = (shooter.Soldier.Id, movementDirection);
            if (_context.NearestInRangeSquads.TryGetValue(cacheKey, out IReadOnlyList<BattleSquad> cached))
            {
                return cached;
            }

            // Effective range matters for thrown weapons (a grenade's reach scales with
            // the thrower's Strength); every other weapon reads its raw MaximumRange.
            float maximumRange = shooter.EquippedRangedWeapons
                .Where(weapon => weapon.LoadedAmmo > 0)
                .Select(weapon => BattleModifiersUtil.GetEffectiveMaxRange(
                    shooter.Soldier,
                    weapon.Template))
                .DefaultIfEmpty(0)
                .Max();
            IReadOnlyList<BattleSquad> nearest = GetNearestEnemySquadsWithinRange(
                shooter,
                maximumRange,
                movementDirection);
            _context.NearestInRangeSquads[cacheKey] = nearest;
            return nearest;
        }

        internal IReadOnlyList<BattleSquad> GetNearestEnemySquadsWithinRange(
            BattleSoldier shooter,
            float maximumRange,
            ValueTuple<int, int>? movementDirection = null)
        {
            if (maximumRange <= 0 || !IsPlaced(shooter)) return [];

            bool restrictFiringArc = HasRestrictedJogFiringArc(movementDirection);
            ValueTuple<int, int> firingDirection = movementDirection.GetValueOrDefault();

            // Keep only the three best squads while scanning. The previous LINQ pipeline
            // grouped every enemy, allocated a projection for every squad, sorted all of
            // them, and materialized the result on every firing evaluation.
            List<(BattleSquad Squad, float Distance)> candidates =
                new(RangedTargetSquadCandidateCount);
            foreach ((int enemyId, float distance) in
                _grid.GetEnemyDistances(shooter.Soldier.Id))
            {
                if (distance > maximumRange)
                {
                    continue;
                }
                if (!_soldierMap.TryGetValue(enemyId, out BattleSoldier enemy)
                    || !enemy.IsCombatEffective
                    || enemy.BattleSquad == null
                    || (restrictFiringArc && !IsWithinJogFiringArc(
                        shooter,
                        enemy,
                        firingDirection)))
                {
                    continue;
                }

                int existingIndex = -1;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (ReferenceEquals(candidates[i].Squad, enemy.BattleSquad))
                    {
                        existingIndex = i;
                        break;
                    }
                }

                if (existingIndex >= 0)
                {
                    if (distance >= candidates[existingIndex].Distance)
                    {
                        continue;
                    }
                    candidates.RemoveAt(existingIndex);
                }
                else if (candidates.Count == RangedTargetSquadCandidateCount
                    && CompareSquadRange(
                        distance,
                        enemy.BattleSquad.Id,
                        candidates[^1].Distance,
                        candidates[^1].Squad.Id) >= 0)
                {
                    continue;
                }

                int insertionIndex = 0;
                while (insertionIndex < candidates.Count
                    && CompareSquadRange(
                        candidates[insertionIndex].Distance,
                        candidates[insertionIndex].Squad.Id,
                        distance,
                        enemy.BattleSquad.Id) <= 0)
                {
                    insertionIndex++;
                }
                candidates.Insert(insertionIndex, (enemy.BattleSquad, distance));
                if (candidates.Count > RangedTargetSquadCandidateCount)
                {
                    candidates.RemoveAt(candidates.Count - 1);
                }
            }

            if (candidates.Count == 0)
            {
                return [];
            }

            BattleSquad[] result = new BattleSquad[candidates.Count];
            for (int i = 0; i < candidates.Count; i++)
            {
                result[i] = candidates[i].Squad;
            }
            return result;
        }

        private static int CompareSquadRange(
            float leftDistance,
            int leftSquadId,
            float rightDistance,
            int rightSquadId)
        {
            int distanceComparison = leftDistance.CompareTo(rightDistance);
            return distanceComparison != 0
                ? distanceComparison
                : leftSquadId.CompareTo(rightSquadId);
        }

        private float CalculateExpectedFriendlyStrayCost(
            BattleSoldier shooter,
            BattleSoldier nominalTarget,
            RangedWeapon weapon,
            float range,
            float additionalToHitModifier,
            int numberOfShots)
        {
            if (!_grid.IsTargetEngagedWithShootersAllies(
                shooter.Soldier.Id,
                nominalTarget.Soldier.Id))
            {
                return 0;
            }

            List<BattleSoldier> scrumParticipants = _grid
                .GetMeleeScrumParticipants(nominalTarget.Soldier.Id)
                .Where(_soldierMap.ContainsKey)
                .Select(id => _soldierMap[id])
                .ToList();
            bool shooterSide = _grid.GetSoldierSide(shooter.Soldier.Id);
            float expectedFriendlyLossOnStray = scrumParticipants
                .Where(participant => _grid.GetSoldierSide(participant.Soldier.Id) == shooterSide)
                .Sum(participant =>
                {
                    float victimProbability = RangedFriendlyFireRules.CalculateStrayTargetProbability(
                        participant,
                        scrumParticipants);
                    float armor = participant.Armor?.Template.ArmorProvided ?? 0;
                    // Phase 5 graded fraction: the friendly cost of a stray must be priced in the
                    // same currency as the enemy value the shot buys, or the trade is rigged.
                    float removalFraction = CalculateRangedRemovalFraction(
                        participant, weapon, range, armor);
                    return victimProbability
                        * removalFraction
                        * GetBattleValue(participant);
                });

            float preRollHitTotal = CalculateRangedPreRollHitTotal(
                shooter,
                nominalTarget,
                weapon,
                range,
                additionalToHitModifier,
                numberOfShots,
                firingIntoMelee: true);
            return RangedFriendlyFireRules.CalculateNearMissProbability(preRollHitTotal)
                * expectedFriendlyLossOnStray;
        }

        internal RangedTargetEvaluation GetBestWeaponForSituation(
            BattleSoldier soldier,
            BattleSoldier target,
            float range,
            float bulkMultiplier,
            bool useAccuracy,
            float aimMultiplier)
        {
            RangedTargetEvaluation best = null;
            float bestScore = float.MinValue;
            IReadOnlyList<RangedWeapon> orderedWeapons =
                OrderRangedByDamageMultiplierDescending(soldier.EquippedRangedWeapons);
            for (int weaponIndex = 0; weaponIndex < orderedWeapons.Count; weaponIndex++)
            {
                RangedWeapon weapon = orderedWeapons[weaponIndex];
                if (weapon.Template.IsTemplateWeapon
                    || range > weapon.Template.MaximumRange
                    || weapon.LoadedAmmo <= 0)
                {
                    continue;
                }

                float bulkAndAccMod = 0;
                bulkAndAccMod -= weapon.Template.Bulk * bulkMultiplier;
                // base accuracy bonus is the weapon's accuracy plus 1 for aiming making it an all-out attack
                bulkAndAccMod += useAccuracy
                    ? (weapon.Template.Accuracy + 1) * aimMultiplier
                    : 0;
                RangedTargetEvaluation evaluation = EvaluateRangedTarget(
                    soldier,
                    target,
                    weapon,
                    range,
                    bulkAndAccMod);
                // if not likely to break through armor, there's little point
                if (evaluation.HitProbability > 0.1f && evaluation.Score > bestScore)
                {
                    // about a 1/10 chance of hitting
                    best = evaluation;
                    bestScore = evaluation.Score;
                }
            }
            return best;
        }

        // Equipped-weapon lists are tiny (usually a single weapon), yet the innermost targeting
        // loops previously rebuilt a LINQ Where/OrderBy pipeline over them for every candidate
        // target, allocating an enumerator and an ordering buffer each pass. These helpers
        // materialize the deterministic ordering once per planning call; the single-weapon fast
        // path returns the source list without allocating.
        internal static IReadOnlyList<RangedWeapon> OrderRangedByTemplateId(
            IReadOnlyList<RangedWeapon> equipped)
        {
            if (equipped.Count <= 1) return equipped;
            RangedWeapon[] ordered = new RangedWeapon[equipped.Count];
            for (int i = 0; i < equipped.Count; i++) ordered[i] = equipped[i];
            // Template.Id is unique, so this total ordering reproduces the previous OrderBy exactly.
            Array.Sort(ordered, static (first, second) =>
                first.Template.Id.CompareTo(second.Template.Id));
            return ordered;
        }

        private static IReadOnlyList<RangedWeapon> OrderRangedByDamageMultiplierDescending(
            IReadOnlyList<RangedWeapon> equipped)
        {
            if (equipped.Count <= 1) return equipped;
            RangedWeapon[] ordered = new RangedWeapon[equipped.Count];
            for (int i = 0; i < equipped.Count; i++) ordered[i] = equipped[i];
            // Stable insertion sort by descending DamageMultiplier, preserving the original relative
            // order on ties to match LINQ's stable OrderByDescending exactly. Equal keys must not be
            // reordered: the chosen weapon feeds seeded battle resolution.
            for (int i = 1; i < ordered.Length; i++)
            {
                RangedWeapon key = ordered[i];
                float keyMultiplier = key.Template.DamageMultiplier;
                int j = i - 1;
                while (j >= 0 && ordered[j].Template.DamageMultiplier < keyMultiplier)
                {
                    ordered[j + 1] = ordered[j];
                    j--;
                }
                ordered[j + 1] = key;
            }
            return ordered;
        }

        // (HitProbability, TakeOutProbabilityOnHit, ShotsToFire, PreRollHitTotal, WoundProgressOnHit)
        private ValueTuple<float, float, int, float, float> EstimatePlannedRangedAttack(
            BattleSoldier soldier,
            BattleSoldier target,
            RangedWeapon weapon,
            float range,
            float moveAndAimMod,
            float? targetSpeed = null)
        {
            int shotsToFire = Math.Max(
                1,
                Math.Min((int)weapon.Template.RateOfFire, (int)weapon.LoadedAmmo));
            float armor = target.Armor?.Template.ArmorProvided ?? 0;
            (float takeOutProbability, float woundProgress) =
                CalculateRangedHitRemoval(target, weapon, range, armor);
            bool firingIntoMelee = _grid.IsTargetEngagedWithShootersAllies(
                soldier.Soldier.Id,
                target.Soldier.Id);
            RangedHitEstimateContext hitContext = new(
                soldier,
                target,
                weapon,
                range,
                moveAndAimMod,
                firingIntoMelee,
                targetSpeed);
            ValueTuple<float, float, float> estimate = new(0, 0, 0);
            for (int iteration = 0; iteration < 4; iteration++)
            {
                estimate = EstimateHitAndDamage(
                    hitContext,
                    takeOutProbability,
                    shotsToFire);
                int revisedShots = CalculateShotsToFire(
                    weapon,
                    estimate.Item1,
                    estimate.Item2);
                if (revisedShots == shotsToFire)
                {
                    return new ValueTuple<float, float, int, float, float>(
                        estimate.Item1,
                        estimate.Item2,
                        shotsToFire,
                        estimate.Item3,
                        woundProgress);
                }

                shotsToFire = revisedShots;
            }

            // Recalculate once with the final shot count so the returned probability is exactly
            // the one ShootAction will resolve, even if a future rule introduces oscillation.
            estimate = EstimateHitAndDamage(
                hitContext,
                takeOutProbability,
                shotsToFire);
            return new ValueTuple<float, float, int, float, float>(
                estimate.Item1, estimate.Item2, shotsToFire, estimate.Item3, woundProgress);
        }

        internal int CalculateShotsToFire(
            RangedWeapon weapon,
            float toHitAtPlannedRateOfFire,
            float takeOutProbabilityOnHit)
        {
            int minRoF = 1;
            int maxRof = Math.Max(
                1,
                Math.Min((int)weapon.Template.RateOfFire, (int)weapon.LoadedAmmo));
            // assume all machine guns have to fire at at least 1/4 their max
            if(weapon.Template.RateOfFire > 10)
            {
                minRoF = Math.Min(weapon.Template.RateOfFire / 4, maxRof);
            }

            if (toHitAtPlannedRateOfFire < .1f)
            {
                // don't waste ammo on impossible shots
                return minRoF;
            }

            if (takeOutProbabilityOnHit <= 0)
            {
                return minRoF;
            }

            // Fire enough independent shots to reach the same take-out confidence used by melee
            // strike planning. This quantity is now a probability, not a linear damage fraction.
            float perShotTakeOut = Math.Clamp(
                toHitAtPlannedRateOfFire * takeOutProbabilityOnHit,
                0f,
                1f);
            if (perShotTakeOut <= 0f)
            {
                return minRoF;
            }
            int killRof = perShotTakeOut >= 1f
                ? 1
                : (int)Math.Ceiling(
                    Math.Log(1f - TargetTakeOutConfidenceThreshold)
                    / Math.Log(1f - perShotTakeOut));

            return Math.Clamp(killRof, minRoF, maxRof);

        }

        private static ValueTuple<float, float, float> EstimateHitAndDamage(
            RangedHitEstimateContext hitContext,
            float expectedDamage,
            int numberOfShots)
        {
            float preRollHitTotal = hitContext.CalculatePreRollHitTotal(numberOfShots);
            float probability = GaussianCalculator.ApproximateNormalCDF(
                (preRollHitTotal - RemovalMath.HitRollMean) / RemovalMath.HitRollStdDev);
            return new ValueTuple<float, float, float>(
                probability, expectedDamage, preRollHitTotal);
        }

        private static float CalculateRangedPreRollHitTotal(
            BattleSoldier soldier,
            BattleSoldier target,
            RangedWeapon weapon,
            float range,
            float moveAndAimMod,
            int numberOfShots,
            bool firingIntoMelee)
        {
            RangedHitEstimateContext hitContext = new(
                soldier,
                target,
                weapon,
                range,
                moveAndAimMod,
                firingIntoMelee);
            return hitContext.CalculatePreRollHitTotal(numberOfShots);
        }

        // A jogging soldier may only fire into the forward hemisphere of its own movement. Both
        // helpers are pure geometry, shared by the targeting scans and by the planner's move path.
        internal static bool HasRestrictedJogFiringArc(
            ValueTuple<int, int>? movementDirection)
        {
            return movementDirection.HasValue
                && (movementDirection.Value.Item1 != 0
                    || movementDirection.Value.Item2 != 0);
        }

        internal static bool IsWithinJogFiringArc(
            BattleSoldier shooter,
            BattleSoldier target,
            ValueTuple<int, int> movementDirection)
        {
            int targetX = target.TopLeft.Value.Item1 - shooter.TopLeft.Value.Item1;
            int targetY = target.TopLeft.Value.Item2 - shooter.TopLeft.Value.Item2;
            long dotProduct = ((long)movementDirection.Item1 * targetX)
                + ((long)movementDirection.Item2 * targetY);
            return dotProduct >= 0;
        }

        // Phase 5: the graded fraction. Every site that turns a landed hit into expected BATTLE
        // VALUE removed reads this; CalculateShotsToFire and the Phase 4 table's ReferenceTakeOut
        // go straight to RemovalMath for the raw take-out probability instead, because a shot count
        // is a question about kills, not about accumulated wounds.
        internal static float CalculateRangedRemovalFraction(
            BattleSoldier target,
            RangedWeapon weapon,
            float range,
            float armor)
        {
            (float takeOut, float progress) =
                CalculateRangedHitRemoval(target, weapon, range, armor);
            return RemovalMath.CombineRemovalFraction(takeOut, progress);
        }

        // Both halves from ONE hit-location walk, for the path that needs the raw take-out
        // probability (shot count) and the graded fraction (battle value) from the same shot.
        internal static (float TakeOut, float WoundProgress) CalculateRangedHitRemoval(
            BattleSoldier target,
            RangedWeapon weapon,
            float range,
            float armor)
        {
            float damageCoefficient = BattleModifiersUtil.CalculateDamageAtRange(weapon, range);
            if (damageCoefficient <= 0)
            {
                return (0f, 0f);
            }
            return RemovalMath.CalculateRemovalTermsOnHit(
                target,
                damageCoefficient,
                armor * weapon.Template.ArmorMultiplier,
                weapon.Template.WoundMultiplier);
        }
    }
}
