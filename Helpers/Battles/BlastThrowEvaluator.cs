using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models.Equippables;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// Grenade scoring: picks the aim point whose expected blast is worth the throw, integrating
    /// enemy AND friendly (self included) battle value over the full delivery scatter distribution
    /// rather than over a perfect impact. See OnlyWar_TDD.md §6.6 and OnlyWar_PRD.md §4.14.
    ///
    /// <para>Holds no planning state of its own -- it reads the planner's
    /// <see cref="SquadPlanningServices"/>, and emits nothing, so it never touches an
    /// <see cref="ActionSink"/>. The one thing it cannot reach is the planner's enemy-acquisition
    /// scan, so that arrives as <see cref="_findEnemiesWithinRange"/>: blast normally scores
    /// against the shared ranged candidate list, and only falls back to its own scan when no
    /// candidates were supplied.</para>
    /// </summary>
    internal sealed class BlastThrowEvaluator
    {
        // TUNABLE: the grenade is a sidearm, not the main gun. A blast throw must beat the
        // soldier's best conventional action (rifle shot or cone burst) by more than this
        // expected-battle-value margin before it is chosen. Retained after the take-out-
        // probability conversion: focused tests still separate lone targets, clusters, and
        // danger-close throws at this margin.
        internal const float OverConventionalScoreMargin = 0.25f;
        // Blast planning integrates enemy AND friendly value over the delivery scatter
        // distribution (not just the on-target impact), so a throw that only frags the
        // squad when it misses is no longer scored as free. See EvaluateThrow and
        // OnlyWar_TDD.md §6.6.
        private const float BlastDeliveryRollMean = 10.5f;
        private const float BlastDeliveryRollStdDev = 3.0f;
        // Deterministic quadrature nodes over the delivery roll's standard normal, and the
        // number of angular samples a scattered node spreads across. Fixed at compile time,
        // so blast scoring stays reproducible without drawing from the battle RNG.
        private const int BlastScatterAngleSamples = 8;
        // Soldiers farther than AreaRadius + this many cells from the aim point cannot be
        // caught by any scatter node we integrate, so the gather stops there.
        private const int BlastScatterMaxGatherCells = 12;
        private static readonly (float Z, float Weight)[] BlastDeliveryQuadrature =
            BuildStandardNormalQuadrature();

        private readonly SquadPlanningServices _services;
        // The planner's nearest-enemies-within-range scan: (shooter, range, movementDirection).
        // Only consulted on the no-candidates fallback path.
        private readonly Func<BattleSoldier, float, ValueTuple<int, int>?, IEnumerable<BattleSoldier>>
            _findEnemiesWithinRange;

        private BattleGridManager Grid => _services.Grid;
        private IReadOnlyDictionary<int, BattleSoldier> SoldierMap => _services.SoldierMap;

        internal BlastThrowEvaluator(
            SquadPlanningServices services,
            Func<BattleSoldier, float, ValueTuple<int, int>?, IEnumerable<BattleSoldier>>
                findEnemiesWithinRange)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _findEnemiesWithinRange = findEnemiesWithinRange
                ?? throw new ArgumentNullException(nameof(findEnemiesWithinRange));
        }

        /// <summary>
        /// Scores grenade aim points and returns the best throw, or null when none removes more
        /// value than it costs in expectation. Each candidate enemy's cell is an aim point;
        /// <see cref="EvaluateThrow"/> integrates expected enemy and friendly (self included)
        /// battle value over the full delivery scatter distribution and the per-victim damage roll,
        /// so a throw that only frags the squad when it misses is priced accordingly. When
        /// <paramref name="candidateTargets"/> is supplied the throw is scored against the shared
        /// acquired candidates (rifle/cone/blast agreeing on targets); otherwise it falls back to
        /// its own nearest-in-range scan.
        /// </summary>
        internal TemplateFiringLineEvaluation SelectBestThrow(
            BattleSoldier soldier,
            ValueTuple<int, int>? movementDirection = null,
            float bulkMultiplier = 0,
            IReadOnlyList<BattleSoldier> candidateTargets = null)
        {
            if (soldier == null || !_services.IsPlaced(soldier))
            {
                return null;
            }

            List<RangedWeapon> blastWeapons = GetLoadedBlastWeapons(soldier);
            if (blastWeapons.Count == 0)
            {
                return null;
            }

            float maximumEffectiveRange = blastWeapons.Max(weapon =>
                BattleModifiersUtil.GetEffectiveMaxRange(soldier.Soldier, weapon.Template));
            bool shooterSide = Grid.GetSoldierSide(soldier.Soldier.Id);
            IEnumerable<BattleSoldier> targets = candidateTargets
                ?? _findEnemiesWithinRange(soldier, maximumEffectiveRange, movementDirection);
            TemplateFiringLineEvaluation best = null;
            foreach (BattleSoldier target in targets
                .Where(target => target != null
                    && target.IsCombatEffective
                    && _services.IsPlaced(target)
                    && Grid.GetSoldierSide(target.Soldier.Id) != shooterSide)
                .GroupBy(target => target.Soldier.Id)
                .Select(group => group.First())
                .OrderBy(target => target.Soldier.Id))
            {
                float range = Grid.GetDistanceBetweenSoldiers(
                    soldier.Soldier.Id,
                    target.Soldier.Id);
                foreach (RangedWeapon weapon in blastWeapons
                    .Where(weapon => range <= BattleModifiersUtil.GetEffectiveMaxRange(
                        soldier.Soldier,
                        weapon.Template)))
                {
                    BlastThrowOutcome outcome = EvaluateThrow(
                        soldier, target, weapon, range, bulkMultiplier);
                    TemplateFiringLineEvaluation evaluation = new(
                        target,
                        weapon,
                        range,
                        outcome.NominalVictimIds,
                        outcome.EnemyBattleValueRemoved,
                        outcome.FriendlyBattleValueLost);
                    // A throw that trades away as much friendly value (self included) as
                    // it removes is never worth the grenade.
                    if (evaluation.Score > 0 && (best == null || evaluation.Score > best.Score))
                    {
                        best = evaluation;
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// Emits a per-turn planning trace breaking down why a soldier chose to throw a grenade
        /// over its best conventional ranged action: the throw's to-hit/delivery math and
        /// enemy/friendly battle-value split, alongside the alternative rifle shot and template
        /// (cone) line it beat. The throw's to-hit and delivery confidence are recomputed here
        /// (mirroring <see cref="SelectBestThrow"/>) because they are local to that scan and
        /// not carried on the returned evaluation; this only runs when a throw is actually
        /// selected and a log sink is attached, so the no-logging hot path is untouched.
        ///
        /// <para>Returns the line rather than writing it. The caller plans a root action for every
        /// candidate posture, only one of which is materialized, so the string rides on
        /// <see cref="PlannedSoldierAction.Diagnostic"/> and is emitted once the throw is known to
        /// be the one taken.</para>
        /// </summary>
        internal string FormatGrenadeSelection(
            BattleSoldier soldier,
            TemplateFiringLineEvaluation blastThrow,
            RangedTargetEvaluation conventionalShot,
            TemplateFiringLineEvaluation conventionalTemplate,
            float bestConventionalScore,
            float bulkMultiplier)
        {
            if (_services.Log == null) return null;

            RangedWeaponTemplate weapon = blastThrow.Weapon.Template;
            float range = blastThrow.Range;
            float skill = soldier.Soldier.GetTotalSkillValue(weapon.RelatedSkill);
            float rangeModifier = BattleModifiersUtil.CalculateBlastRangeModifier(
                soldier.Soldier, weapon, range);
            float bulkPenalty = weapon.Bulk * bulkMultiplier;
            float toHit = skill + rangeModifier - bulkPenalty;
            float deliveryConfidence = GaussianCalculator.ApproximateNormalCDF(
                (toHit - BlastDeliveryRollMean) / BlastDeliveryRollStdDev);

            // float.MinValue is the "no alternative existed" sentinel the caller's Math.Max produces;
            // rendering it as a number would read as a real score of -3.4e38.
            bool hasConventional = conventionalShot != null || conventionalTemplate != null;
            float? bestConventional = hasConventional ? bestConventionalScore : null;
            float? margin = hasConventional ? blastThrow.Score - bestConventionalScore : null;

            bool shooterSide = Grid.GetSoldierSide(soldier.Soldier.Id);
            List<string> caughtEnemies = [];
            List<string> caughtFriendlies = [];
            foreach (int victimId in blastThrow.VictimIds)
            {
                if (!SoldierMap.TryGetValue(victimId, out BattleSoldier victim)) continue;
                string label = victim.Soldier.Name;
                if (victimId == soldier.Soldier.Id) label += " (self)";
                if (Grid.GetSoldierSide(victimId) == shooterSide) caughtFriendlies.Add(label);
                else caughtEnemies.Add(label);
            }

            return new BattleDecisionTrace("GRENADE_CHOICE",
            [
                BattleDecisionTrace.Field("soldier", soldier.Soldier.Id),
                BattleDecisionTrace.Field("name", soldier.Soldier.Name),
                BattleDecisionTrace.Field("weapon", weapon.Name),
                BattleDecisionTrace.Field("target", blastThrow.Target.Soldier.Name),
                BattleDecisionTrace.Field("range", range),
                BattleDecisionTrace.Field("score", blastThrow.Score),
                BattleDecisionTrace.Field("enemy_bv", blastThrow.ExpectedEnemyBattleValueRemoved),
                BattleDecisionTrace.Field("friendly_bv", blastThrow.ExpectedFriendlyBattleValueLost),
                BattleDecisionTrace.Field("to_hit", toHit),
                BattleDecisionTrace.Field("skill", skill),
                BattleDecisionTrace.Field("range_mod", rangeModifier),
                BattleDecisionTrace.Field("bulk_penalty", bulkPenalty),
                BattleDecisionTrace.Field("delivery", deliveryConfidence),
                // Semicolon-separated: the record format reserves spaces for field boundaries.
                BattleDecisionTrace.Field(
                    "caught_enemies",
                    caughtEnemies.Count == 0 ? "none" : string.Join(";", caughtEnemies)),
                BattleDecisionTrace.Field(
                    "caught_friendlies",
                    caughtFriendlies.Count == 0 ? "none" : string.Join(";", caughtFriendlies)),
                // The alternatives the throw beat. Without these the score above is unfalsifiable:
                // a throw looks arbitrary until you can see what firing instead was worth.
                BattleDecisionTrace.Field(
                    "alt_shot_weapon", conventionalShot?.Weapon.Template.Name),
                BattleDecisionTrace.Field(
                    "alt_shot_target", conventionalShot?.Target.Soldier.Name),
                BattleDecisionTrace.Field("alt_shot_shots", conventionalShot?.ShotsToFire),
                BattleDecisionTrace.Field("alt_shot_hit", conventionalShot?.HitProbability),
                BattleDecisionTrace.Field(
                    "alt_shot_takeout", conventionalShot?.TakeOutProbabilityOnHit),
                BattleDecisionTrace.Field("alt_shot_score", conventionalShot?.Score),
                BattleDecisionTrace.Field(
                    "alt_template_weapon", conventionalTemplate?.Weapon.Template.Name),
                BattleDecisionTrace.Field("alt_template_score", conventionalTemplate?.Score),
                BattleDecisionTrace.Field("best_conventional", bestConventional),
                BattleDecisionTrace.Field("margin", margin),
                BattleDecisionTrace.Field(
                    "margin_threshold", OverConventionalScoreMargin)
            ]).Render();
        }

        /// <summary>
        /// Blast weapons ride on the belt (<see cref="BattleSoldier.RangedWeapons"/>)
        /// without occupying a hand, so both lists are candidates.
        /// </summary>
        private static List<RangedWeapon> GetLoadedBlastWeapons(BattleSoldier soldier)
        {
            return soldier.EquippedRangedWeapons
                .Concat(soldier.RangedWeapons)
                .Where(weapon => weapon.Template.IsBlastWeapon && weapon.LoadedAmmo > 0)
                .GroupBy(weapon => weapon.Template.Id)
                .Select(group => group.First())
                .OrderBy(weapon => weapon.Template.Id)
                .ToList();
        }

        // Midpoint quadrature over the delivery roll's standard normal on [-3, 3], weights
        // renormalized to sum to 1. Compile-time constant, so blast scoring is deterministic
        // and never touches the battle RNG.
        private static (float Z, float Weight)[] BuildStandardNormalQuadrature()
        {
            const float lo = -3f;
            const float hi = 3f;
            const float stepSize = 0.5f;
            List<(float Z, float Weight)> nodes = [];
            float total = 0f;
            for (float z = lo; z <= hi + 1e-4f; z += stepSize)
            {
                float weight = RemovalMath.NormalPdf(z);
                nodes.Add((z, weight));
                total += weight;
            }
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i] = (nodes[i].Z, nodes[i].Weight / total);
            }
            return nodes.ToArray();
        }

        private readonly struct BlastNearbySoldier
        {
            public readonly float OffsetX;
            public readonly float OffsetY;
            public readonly bool Friendly;
            public readonly BattleSoldier Target;
            public readonly float BattleValue;
            public readonly RangedWeapon Weapon;

            public BlastNearbySoldier(
                float offsetX,
                float offsetY,
                bool friendly,
                BattleSoldier target,
                float battleValue,
                RangedWeapon weapon)
            {
                OffsetX = offsetX;
                OffsetY = offsetY;
                Friendly = friendly;
                Target = target;
                BattleValue = battleValue;
                Weapon = weapon;
            }
        }

        private readonly struct BlastThrowOutcome
        {
            public readonly float EnemyBattleValueRemoved;
            public readonly float FriendlyBattleValueLost;
            public readonly IReadOnlyList<int> NominalVictimIds;

            public BlastThrowOutcome(
                float enemyBattleValueRemoved,
                float friendlyBattleValueLost,
                IReadOnlyList<int> nominalVictimIds)
            {
                EnemyBattleValueRemoved = enemyBattleValueRemoved;
                FriendlyBattleValueLost = friendlyBattleValueLost;
                NominalVictimIds = nominalVictimIds;
            }
        }

        // Scores a single grenade aim point (the target's cell) by integrating expected enemy and
        // friendly battle value over BOTH the delivery scatter distribution and the per-victim
        // damage roll. A throw that only catches the squad when it scatters is no longer free:
        // every miss node lands the template somewhere and pays its friendly cost. Neither enemy
        // nor friendly value carries an arrival-time discount -- the grenade detonates this turn
        // (Phase 3, Design/Reference/BattleLogic.md), matching the conventional ranged
        // path. Replaces the former perfect-impact-times-confidence estimate. See
        // OnlyWar_TDD.md §6.6.
        private BlastThrowOutcome EvaluateThrow(
            BattleSoldier soldier,
            BattleSoldier target,
            RangedWeapon weapon,
            float range,
            float bulkMultiplier)
        {
            float skill = soldier.Soldier.GetTotalSkillValue(weapon.Template.RelatedSkill);
            float modifier = BattleModifiersUtil.CalculateBlastRangeModifier(
                    soldier.Soldier, weapon.Template, range)
                - (weapon.Template.Bulk * bulkMultiplier);
            // deliveryRoll = mean + stdDev * z, so margin(z) = (skill + modifier - mean) - stdDev * z.
            float baseMargin = skill + modifier - BlastDeliveryRollMean;
            float areaRadius = weapon.Template.AreaRadius;
            float radiusSquared = areaRadius * areaRadius;

            ValueTuple<int, int> aimCell = BlastTemplate.ResolveImpactCell(
                Grid, soldier.Soldier.Id, target.Soldier.Id, margin: 0f, directionRoll: 0.0);

            float gatherRadius = areaRadius + BlastScatterMaxGatherCells;
            float gatherRadiusSquared = gatherRadius * gatherRadius;
            bool shooterSide = Grid.GetSoldierSide(soldier.Soldier.Id);

            // One direct scan of the (small) field collects every soldier a scatter node could
            // reach, plus their offset from the aim cell — cheaper and more precise than a per-node
            // disc query, and it yields the nominal (on-target) victim list for logging in one pass.
            List<BlastNearbySoldier> nearby = [];
            List<int> nominalVictims = [];
            foreach (BattleSoldier candidate in SoldierMap.Values)
            {
                if (!candidate.IsCombatEffective || !_services.IsPlaced(candidate))
                {
                    continue;
                }
                IList<ValueTuple<int, int>> footprint =
                    Grid.GetSoldierPosition(candidate.Soldier.Id);
                if (footprint == null || footprint.Count == 0)
                {
                    continue;
                }
                // Represent the soldier by whichever footprint cell sits closest to the aim
                // point, mirroring how BlastTemplate.GetVictims credits a figure's nearest cell.
                float offsetX = 0f;
                float offsetY = 0f;
                float distanceSquared = float.MaxValue;
                foreach (ValueTuple<int, int> cell in footprint)
                {
                    float cellX = cell.Item1 - aimCell.Item1;
                    float cellY = cell.Item2 - aimCell.Item2;
                    float cellDistanceSquared = (cellX * cellX) + (cellY * cellY);
                    if (cellDistanceSquared < distanceSquared)
                    {
                        distanceSquared = cellDistanceSquared;
                        offsetX = cellX;
                        offsetY = cellY;
                    }
                }
                if (distanceSquared > gatherRadiusSquared)
                {
                    continue;
                }
                bool friendly = Grid.GetSoldierSide(candidate.Soldier.Id) == shooterSide;
                nearby.Add(new BlastNearbySoldier(
                    offsetX,
                    offsetY,
                    friendly,
                    candidate,
                    SquadPlanningServices.BattleValueOf(candidate),
                    weapon));
                if (distanceSquared <= radiusSquared)
                {
                    nominalVictims.Add(candidate.Soldier.Id);
                }
            }

            float enemyBattleValueRemoved = 0f;
            float friendlyBattleValueLost = 0f;
            foreach ((float z, float weight) in BlastDeliveryQuadrature)
            {
                float margin = baseMargin - (BlastDeliveryRollStdDev * z);
                if (margin >= 0f)
                {
                    // On-target node: the whole weight lands on the aim cell.
                    AccumulateBlastNode(
                        nearby, 0f, 0f, areaRadius, radiusSquared, weight,
                        ref enemyBattleValueRemoved, ref friendlyBattleValueLost);
                    continue;
                }
                // Scattered node: the impact deviates |margin| * ScatterDistancePerPoint cells in a
                // uniformly random direction, so split the node weight across the angular samples.
                float scatterDistance = -margin * BlastTemplate.ScatterDistancePerPoint;
                float angleWeight = weight / BlastScatterAngleSamples;
                for (int angleIndex = 0; angleIndex < BlastScatterAngleSamples; angleIndex++)
                {
                    double angle = (2.0 * Math.PI * angleIndex) / BlastScatterAngleSamples;
                    float impactX = (float)(scatterDistance * Math.Cos(angle));
                    float impactY = (float)(scatterDistance * Math.Sin(angle));
                    AccumulateBlastNode(
                        nearby, impactX, impactY, areaRadius, radiusSquared, angleWeight,
                        ref enemyBattleValueRemoved, ref friendlyBattleValueLost);
                }
            }

            return new BlastThrowOutcome(
                enemyBattleValueRemoved, friendlyBattleValueLost, nominalVictims);
        }

        // Adds one integration node's contribution: every gathered soldier within the template of
        // an impact at (impactX, impactY) relative to the aim cell, weighted by the node weight.
        private static void AccumulateBlastNode(
            List<BlastNearbySoldier> nearby,
            float impactX,
            float impactY,
            float areaRadius,
            float radiusSquared,
            float weight,
            ref float enemyBattleValueRemoved,
            ref float friendlyBattleValueLost)
        {
            for (int i = 0; i < nearby.Count; i++)
            {
                BlastNearbySoldier victim = nearby[i];
                float dx = victim.OffsetX - impactX;
                float dy = victim.OffsetY - impactY;
                float distanceSquared = (dx * dx) + (dy * dy);
                if (distanceSquared > radiusSquared)
                {
                    continue;
                }
                float falloff = 1f - (MathF.Sqrt(distanceSquared) / areaRadius);
                float armor = victim.Target.Armor?.Template.ArmorProvided ?? 0f;
                // Phase 5 graded fraction, applied to BOTH the enemy and the friendly half below --
                // Phase 3 showed that pricing one side of a blast differently from the other is
                // pure accounting asymmetry, not caution.
                float removalFraction = RemovalMath.CalculateRemovalFractionOnHit(
                    victim.Target,
                    victim.Weapon.Template.DamageMultiplier * falloff * falloff,
                    armor * victim.Weapon.Template.ArmorMultiplier,
                    victim.Weapon.Template.WoundMultiplier);
                float removed = weight * removalFraction * victim.BattleValue;
                if (victim.Friendly)
                {
                    friendlyBattleValueLost += removed;
                }
                else
                {
                    enemyBattleValueRemoved += removed;
                }
            }
        }
    }
}
