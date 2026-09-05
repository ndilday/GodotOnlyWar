using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// Estimates the result of one conventional ranged shot or burst.
    ///
    /// <para>This is the single owner of ranged hit assembly, adaptive shot-count selection,
    /// graded removal terms, and the friendly-fire stray cost. It is intentionally decision-only:
    /// it receives the narrow ranged read capability, never the battle RNG, and never an
    /// <see cref="ActionSink"/>.</para>
    /// </summary>
    internal sealed class RangedShotEvaluator
    {
        private const float TargetTakeOutConfidenceThreshold = MeleeMath.TakeOutConfidenceTarget;

        private readonly RangedTargetingServices _services;
        private readonly BattleGridManager _grid;
        private readonly IReadOnlyDictionary<int, BattleSoldier> _soldierMap;
        private readonly BattlePlanningContext _context;

        internal RangedShotEvaluator(RangedTargetingServices services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _grid = services.Grid;
            _soldierMap = services.SoldierMap;
            _context = services.Context;
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

            (float hitProbability,
                float takeOutProbability,
                int shotsToFire,
                float preRollHitTotal,
                float woundProgressOnHit) = EstimatePlannedRangedAttack(
                    soldier,
                    target,
                    weapon,
                    range,
                    additionalToHitModifier,
                    evaluatedTargetSpeed);
            float clampedTakeOutProbability = Math.Clamp(takeOutProbability, 0, 1);

            // This is the expected battle value removed THIS TURN. It is deliberately
            // undiscounted: arrival time belongs to EngagementFutureDiscount, while distance is
            // already represented by the range modifier.
            float enemyBattleValueRemoved = RemovalMath.ExpectedBurstRemovalFraction(
                    preRollHitTotal,
                    shotsToFire,
                    weapon.Template.Recoil,
                    RemovalMath.CombineRemovalFraction(
                        clampedTakeOutProbability,
                        woundProgressOnHit))
                * RangedTargetingServices.BattleValueOf(target);
            float friendlyBattleValueLost = CalculateExpectedFriendlyStrayCost(
                soldier,
                target,
                weapon,
                range,
                additionalToHitModifier,
                shotsToFire);

            RangedTargetEvaluation result = new(
                target,
                weapon,
                range,
                shotsToFire,
                hitProbability,
                takeOutProbability,
                enemyBattleValueRemoved,
                friendlyBattleValueLost,
                preRollHitTotal,
                evaluatedTargetSpeed,
                woundProgressOnHit);
            _context.RangedEvaluations[cacheKey] = result;
            return result;
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
            // Assume all machine guns have to fire at least one quarter of their maximum.
            if (weapon.Template.RateOfFire > 10)
            {
                minRoF = Math.Min(weapon.Template.RateOfFire / 4, maxRof);
            }

            if (toHitAtPlannedRateOfFire < .1f || takeOutProbabilityOnHit <= 0)
            {
                return minRoF;
            }

            // Fire enough independent shots to reach the same take-out confidence used by melee
            // strike planning. This is a kill probability, not a graded wound fraction.
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

        // (HitProbability, TakeOutProbabilityOnHit, ShotsToFire, PreRollHitTotal, WoundProgressOnHit)
        private (float HitProbability, float TakeOutProbabilityOnHit, int ShotsToFire,
            float PreRollHitTotal, float WoundProgressOnHit) EstimatePlannedRangedAttack(
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
            (float takeOutProbability, float woundProgress) = CalculateRangedHitRemoval(
                target,
                weapon,
                range,
                armor);
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
            (float HitProbability, float TakeOutProbabilityOnHit, float PreRollHitTotal) estimate =
                new(0, 0, 0);
            for (int iteration = 0; iteration < 4; iteration++)
            {
                estimate = EstimateHitAndDamage(
                    hitContext,
                    takeOutProbability,
                    shotsToFire);
                int revisedShots = CalculateShotsToFire(
                    weapon,
                    estimate.HitProbability,
                    estimate.TakeOutProbabilityOnHit);
                if (revisedShots == shotsToFire)
                {
                    return (
                        estimate.HitProbability,
                        estimate.TakeOutProbabilityOnHit,
                        shotsToFire,
                        estimate.PreRollHitTotal,
                        woundProgress);
                }
                shotsToFire = revisedShots;
            }

            // Recalculate with the final count so the returned probability is exactly the one the
            // ShootAction will resolve, even if a future rule introduces oscillation.
            estimate = EstimateHitAndDamage(
                hitContext,
                takeOutProbability,
                shotsToFire);
            return (
                estimate.HitProbability,
                estimate.TakeOutProbabilityOnHit,
                shotsToFire,
                estimate.PreRollHitTotal,
                woundProgress);
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
                    float removalFraction = CalculateRangedRemovalFraction(
                        participant,
                        weapon,
                        range,
                        armor);
                    return victimProbability
                        * removalFraction
                        * RangedTargetingServices.BattleValueOf(participant);
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
                    range,
                    targetSpeed ?? target.CurrentSpeed);
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
                // values guide weapon decisions, so threshold-level rounding can alter a seeded
                // battle.
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

        private static (float HitProbability, float TakeOutProbabilityOnHit, float PreRollHitTotal)
            EstimateHitAndDamage(
                RangedHitEstimateContext hitContext,
                float expectedDamage,
                int numberOfShots)
        {
            float preRollHitTotal = hitContext.CalculatePreRollHitTotal(numberOfShots);
            float probability = GaussianCalculator.ApproximateNormalCDF(
                (preRollHitTotal - RemovalMath.HitRollMean) / RemovalMath.HitRollStdDev);
            return (probability, expectedDamage, preRollHitTotal);
        }

        internal static float CalculateRangedPreRollHitTotal(
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

        // The graded fraction is used when a landed hit is translated into expected battle value.
        // Shot-count selection and the Phase 4 table continue to use the raw take-out probability.
        internal static float CalculateRangedRemovalFraction(
            BattleSoldier target,
            RangedWeapon weapon,
            float range,
            float armor)
        {
            (float takeOut, float progress) = CalculateRangedHitRemoval(
                target,
                weapon,
                range,
                armor);
            return RemovalMath.CombineRemovalFraction(takeOut, progress);
        }

        // Both terms come from one hit-location walk so raw kill probability and graded progress
        // remain aligned.
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
