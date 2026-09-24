using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Battles.Models;
using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Math;

namespace OnlyWar.Battles
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
            float? targetSpeed = null,
            int? availableAmmo = null)
        {
            float evaluatedTargetSpeed = targetSpeed ?? target.CurrentSpeed;
            int evaluatedAmmo = availableAmmo ?? weapon.LoadedAmmo;
            var cacheKey = (
                soldier.Soldier.Id,
                target.Soldier.Id,
                weapon.Template.Id,
                BitConverter.SingleToInt32Bits(range),
                BitConverter.SingleToInt32Bits(additionalToHitModifier),
                BitConverter.SingleToInt32Bits(evaluatedTargetSpeed),
                evaluatedAmmo);
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
                    evaluatedTargetSpeed,
                    evaluatedAmmo);
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

        /// <summary>
        /// The burst to fire: the round count with the best net value, where net value is the
        /// burst's expected battle value removed, less friendly strays, less the rounds it spends
        /// at <c>scarcity × (value of a round)</c> each.
        ///
        /// <para>WHY. This used to fire enough rounds to reach 75% take-out confidence, treating
        /// each round as an independent chance to kill. <see cref="Actions.ShootAction"/> does
        /// not resolve a burst that way: it makes ONE roll, and hit k needs the margin to clear
        /// <c>1 + (k-1) × recoil</c>. For a boltgun the fifth hit needs a margin above 7 against a
        /// deviation of 3, so rounds five to nine almost never land; all they buy is the
        /// <c>log2(n)</c> to-hit bonus on the first round. The old rule could not see that, and
        /// marines emptied their magazines on nine-round bursts. The score below is the one
        /// <see cref="RemovalMath.ExpectedBurstRemovalFraction"/> already gives every consumer.</para>
        ///
        /// <para>Without a price on rounds the answer is always the full rate of fire -- another
        /// round never lowers the expected removal -- so the round cost is what shortens a burst.
        /// It is priced exactly as <see cref="RangedTargetSelector.EvaluateFireTiming"/> prices a
        /// shot: scarcity 0 (free rounds) fires everything, and on the last magazine only the
        /// most round-efficient burst is worth firing.</para>
        ///
        /// <para>SEARCH. Climb from the minimum and stop at the first round whose marginal value
        /// does not cover its cost. That is exact when the score is concave in the round count --
        /// the bonus step <c>log2((n+1)/n)</c> shrinks and each later hit needs a higher margin --
        /// and then the best value per round is the first burst's, so the price is known before
        /// the climb starts. It is only approximate in the deep tail of the hit roll, where the
        /// score is convex and the climb can stop early on a shot hardly worth taking. A short
        /// burst costs a few CDF calls, fewer than the fixed-point iteration this replaced.</para>
        /// </summary>
        private int ChooseShotsToFire(
            BattleSoldier shooter,
            BattleSoldier target,
            RangedWeapon weapon,
            float range,
            in RangedHitEstimateContext hitContext,
            bool firingIntoMelee,
            float removalFractionPerHit,
            int ammunitionAvailable)
        {
            int maxShots = Math.Max(
                1,
                Math.Min((int)weapon.Template.RateOfFire, Math.Max(0, ammunitionAvailable)));
            int minShots = 1;
            // Assume all machine guns have to fire at least one quarter of their maximum.
            if (weapon.Template.RateOfFire > 10)
            {
                minShots = Math.Min(weapon.Template.RateOfFire / 4, maxShots);
            }
            if (maxShots <= minShots)
            {
                return minShots;
            }

            float scarcity = RangedTargetSelector.AmmunitionScarcity(weapon);
            if (scarcity <= 0f && !firingIntoMelee)
            {
                // Free rounds and no friend in the line of fire: every round adds, none costs.
                // Checked before the zero-removal case on purpose. PairRemovalRateTable keeps this
                // count and rescales the shot to other ranges, so a far-range capture that removes
                // nothing must still report the burst the weapon fires once it can penetrate;
                // otherwise the stored count jumps from the minimum to the full rate at the
                // penetration range, and the engagement potential shows a cliff there.
                return maxShots;
            }
            if (removalFractionPerHit <= 0f)
            {
                return minShots;
            }

            float targetValue = RangedTargetingServices.BattleValueOf(target);
            float friendlyLossOnStray = firingIntoMelee
                ? CalculateExpectedFriendlyLossOnStray(shooter, target, weapon, range)
                : 0f;
            float recoil = weapon.Template.Recoil;
            RangedHitEstimateContext context = hitContext;
            float Score(int shots)
            {
                float preRollHitTotal = context.CalculatePreRollHitTotal(shots);
                float removed = targetValue * RemovalMath.ExpectedBurstRemovalFraction(
                    preRollHitTotal, shots, recoil, removalFractionPerHit);
                return friendlyLossOnStray > 0f
                    ? removed - (RangedFriendlyFireRules.CalculateNearMissProbability(preRollHitTotal)
                        * friendlyLossOnStray)
                    : removed;
            }

            int chosen = minShots;
            float score = Score(minShots);
            float valuePerRound = Math.Max(0f, score)
                / Math.Max(1, weapon.GetAmmunitionUnitsForAttack(minShots));
            while (chosen < maxShots)
            {
                int nextShots = chosen + 1;
                float nextScore = Score(nextShots);
                float roundCost = scarcity * valuePerRound
                    * (weapon.GetAmmunitionUnitsForAttack(nextShots)
                        - weapon.GetAmmunitionUnitsForAttack(chosen));
                if (nextScore - score <= roundCost)
                {
                    break;
                }
                chosen = nextShots;
                score = nextScore;
                valuePerRound = Math.Max(
                    valuePerRound,
                    Math.Max(0f, score) / Math.Max(1, weapon.GetAmmunitionUnitsForAttack(chosen)));
            }
            return chosen;
        }

        // (HitProbability, TakeOutProbabilityOnHit, ShotsToFire, PreRollHitTotal, WoundProgressOnHit)
        private (float HitProbability, float TakeOutProbabilityOnHit, int ShotsToFire,
            float PreRollHitTotal, float WoundProgressOnHit) EstimatePlannedRangedAttack(
            BattleSoldier soldier,
            BattleSoldier target,
            RangedWeapon weapon,
            float range,
            float moveAndAimMod,
            float? targetSpeed = null,
            int? availableAmmo = null)
        {
            int ammunitionAvailable = availableAmmo ?? weapon.LoadedAmmo;
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
            int shotsToFire = ChooseShotsToFire(
                soldier,
                target,
                weapon,
                range,
                hitContext,
                firingIntoMelee,
                RemovalMath.CombineRemovalFraction(
                    Math.Clamp(takeOutProbability, 0f, 1f),
                    woundProgress),
                ammunitionAvailable);
            (float HitProbability, float TakeOutProbabilityOnHit, float PreRollHitTotal) estimate =
                EstimateHitAndDamage(hitContext, takeOutProbability, shotsToFire);
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

            float preRollHitTotal = CalculateRangedPreRollHitTotal(
                shooter,
                nominalTarget,
                weapon,
                range,
                additionalToHitModifier,
                numberOfShots,
                firingIntoMelee: true);
            return RangedFriendlyFireRules.CalculateNearMissProbability(preRollHitTotal)
                * CalculateExpectedFriendlyLossOnStray(shooter, nominalTarget, weapon, range);
        }

        /// <summary>
        /// Expected friendly battle value lost if a shot at a target in a melee scrum strays: the
        /// stray-victim lottery over the scrum's friendly members, times what one hit removes.
        /// Independent of the burst, so burst selection prices every round count against it once.
        /// </summary>
        private float CalculateExpectedFriendlyLossOnStray(
            BattleSoldier shooter,
            BattleSoldier nominalTarget,
            RangedWeapon weapon,
            float range)
        {
            List<BattleSoldier> scrumParticipants = _grid
                .GetMeleeScrumParticipants(nominalTarget.Soldier.Id)
                .Where(_soldierMap.ContainsKey)
                .Select(id => _soldierMap[id])
                .ToList();
            bool shooterSide = _grid.GetSoldierSide(shooter.Soldier.Id);
            return scrumParticipants
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

        // The graded fraction is used when a landed hit is translated into expected battle value,
        // including by shot-count selection. The Phase 4 table still carries the raw take-out
        // probability alongside it.
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
