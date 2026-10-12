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

        /// <param name="roundPrice">
        /// The price of a round the burst is chosen at (see <see cref="ChooseShotsToFire"/>). Null
        /// prices it as if this shot were the only one on offer. A decision that compares several
        /// plans passes the one price it charges them all
        /// (<see cref="RangedTargetSelector.RoundPrice"/>).
        /// </param>
        internal RangedTargetEvaluation EvaluateRangedTarget(
            BattleSoldier soldier,
            BattleSoldier target,
            RangedWeapon weapon,
            float range,
            float additionalToHitModifier,
            float? targetSpeed = null,
            int? availableAmmo = null,
            float? roundPrice = null)
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
                evaluatedAmmo,
                BitConverter.SingleToInt32Bits(roundPrice ?? float.NaN));
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
                    evaluatedAmmo,
                    roundPrice);
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
                woundProgressOnHit,
                additionalToHitModifier);
            _context.RangedEvaluations[cacheKey] = result;
            return result;
        }

        /// <summary>
        /// The share of the fight's work that falls on one shooter with this weapon: the battle
        /// value of the able enemies the weapon can hurt at all, over the able soldiers on the
        /// shooter's side who carry a reloadable ranged weapon. Enemies the weapon cannot damage
        /// are not its work -- a lasgun facing a Carnifex has nothing to save rounds for. Head
        /// count, not firepower: a heavy weapon is not given a larger share. Once per side and
        /// weapon per planning pass.
        /// </summary>
        internal float WorkPerShooter(BattleSoldier shooter, RangedWeapon weapon)
        {
            if (shooter == null || weapon == null || !_grid.IsSoldierPlaced(shooter.Soldier.Id))
            {
                return 0f;
            }
            bool side = _grid.GetSoldierSide(shooter.Soldier.Id);
            return _context.RangedWorkPerShooter.GetOrAdd(
                (side, weapon.Template.Id),
                _ => CalculateWorkPerShooter(side, weapon));
        }

        private float CalculateWorkPerShooter(bool side, RangedWeapon weapon)
        {
            float enemyValue = 0f;
            int shooters = 0;
            foreach (BattleSoldier soldier in _soldierMap.Values)
            {
                if (!soldier.IsCombatEffective || !_grid.IsSoldierPlaced(soldier.Soldier.Id))
                {
                    continue;
                }
                if (_grid.GetSoldierSide(soldier.Soldier.Id) == side)
                {
                    if (soldier.RangedWeapons.Any(carried => !carried.IsConsumableItem))
                    {
                        shooters++;
                    }
                    continue;
                }
                float armor = soldier.Armor?.Template.ArmorProvided ?? 0;
                if (CalculateRangedRemovalFraction(soldier, weapon, 1f, armor) > 0f)
                {
                    enemyValue += RangedTargetingServices.BattleValueOf(soldier);
                }
            }
            return enemyValue / Math.Max(1, shooters);
        }

        /// <summary>
        /// What this shot offers a firing decision (<see cref="RangedTargetSelector.RoundPrice"/>):
        /// the best value per round over every burst length (the most a round spent on it could
        /// buy), and the burst it fires when rounds are free -- its score and the rounds it spends,
        /// which set the pace. All zero when the shot removes nothing.
        /// </summary>
        internal (float BestValuePerRound, float FreeScore, int FreeRounds) BurstProfile(
            BattleSoldier soldier,
            BattleSoldier target,
            RangedWeapon weapon,
            float range,
            float additionalToHitModifier,
            float? targetSpeed = null,
            int? availableAmmo = null)
        {
            float evaluatedTargetSpeed = targetSpeed ?? target.CurrentSpeed;
            int ammunitionAvailable = availableAmmo ?? weapon.LoadedAmmo;
            float armor = target.Armor?.Template.ArmorProvided ?? 0;
            (float takeOut, float progress) = CalculateRangedHitRemoval(target, weapon, range, armor);
            float removalFractionPerHit = RemovalMath.CombineRemovalFraction(
                Math.Clamp(takeOut, 0f, 1f), progress);
            if (removalFractionPerHit <= 0f)
            {
                return (0f, 0f, 0);
            }
            bool firingIntoMelee = _grid.IsTargetEngagedWithShootersAllies(
                soldier.Soldier.Id,
                target.Soldier.Id);
            RangedHitEstimateContext hitContext = new(
                soldier,
                target,
                weapon,
                range,
                additionalToHitModifier,
                firingIntoMelee,
                evaluatedTargetSpeed);
            (int minShots, int maxShots) = BurstBounds(weapon, ammunitionAvailable);
            float[] scores = ScoreBursts(
                soldier, target, weapon, range, hitContext, firingIntoMelee,
                removalFractionPerHit, minShots, maxShots);
            float best = 0f;
            float freeScore = float.MinValue;
            int freeShots = minShots;
            for (int shots = minShots; shots <= maxShots; shots++)
            {
                float score = scores[shots - minShots];
                best = Math.Max(
                    best,
                    Math.Max(0f, score) / Math.Max(1, weapon.GetAmmunitionUnitsForAttack(shots)));
                // Free rounds: the burst ChooseShotsToFire picks at price 0.
                if (score > freeScore + 1e-7f)
                {
                    freeScore = score;
                    freeShots = shots;
                }
            }
            return (best, freeScore, weapon.GetAmmunitionUnitsForAttack(freeShots));
        }

        private static (int Min, int Max) BurstBounds(RangedWeapon weapon, int ammunitionAvailable)
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
            return (minShots, Math.Max(minShots, maxShots));
        }

        /// <summary>
        /// The score of every burst length from <paramref name="minShots"/> to
        /// <paramref name="maxShots"/>: expected battle value removed by one roll, less the
        /// expected friendly loss of a stray when firing into a melee.
        /// </summary>
        private float[] ScoreBursts(
            BattleSoldier shooter,
            BattleSoldier target,
            RangedWeapon weapon,
            float range,
            in RangedHitEstimateContext hitContext,
            bool firingIntoMelee,
            float removalFractionPerHit,
            int minShots,
            int maxShots)
        {
            float targetValue = RangedTargetingServices.BattleValueOf(target);
            float friendlyLossOnStray = firingIntoMelee
                ? CalculateExpectedFriendlyLossOnStray(shooter, target, weapon, range)
                : 0f;
            float recoil = weapon.Template.Recoil;
            float[] scores = new float[maxShots - minShots + 1];
            for (int shots = minShots; shots <= maxShots; shots++)
            {
                float preRollHitTotal = hitContext.CalculatePreRollHitTotal(shots);
                float removed = targetValue * RemovalMath.ExpectedBurstRemovalFraction(
                    preRollHitTotal, shots, recoil, removalFractionPerHit);
                scores[shots - minShots] = friendlyLossOnStray > 0f
                    ? removed - (RangedFriendlyFireRules.CalculateNearMissProbability(preRollHitTotal)
                        * friendlyLossOnStray)
                    : removed;
            }
            return scores;
        }

        /// <summary>
        /// The burst to fire: the round count with the best net value, where net value is the
        /// burst's expected battle value removed, less friendly strays, less the rounds it spends
        /// at the round price each (<see cref="RangedTargetSelector.RoundPrice"/>).
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
        /// shot: scarcity 0 (rounds that outlast the work) fires everything, and on the last
        /// rounds only the most round-efficient burst is worth firing.</para>
        ///
        /// <para>SEARCH. Every burst length is scored, the round price is set from the best value
        /// per round among them, and the length with the best net value wins. (A climb that
        /// stopped at the first unpaid round was used until 2026-10-06; it is exact only where the
        /// score is concave in the round count, and the deep tail of the hit roll is not.)</para>
        /// </summary>
        private int ChooseShotsToFire(
            BattleSoldier shooter,
            BattleSoldier target,
            RangedWeapon weapon,
            float range,
            in RangedHitEstimateContext hitContext,
            bool firingIntoMelee,
            float removalFractionPerHit,
            int ammunitionAvailable,
            float? roundPrice)
        {
            (int minShots, int maxShots) = BurstBounds(weapon, ammunitionAvailable);
            if (maxShots <= minShots)
            {
                return minShots;
            }

            if (!firingIntoMelee
                && (RangedTargetSelector.IsNeverScarce(weapon) || removalFractionPerHit <= 0f))
            {
                // Free rounds and no friend in the line of fire: every round adds, none costs. A
                // shot that removes nothing prices its rounds at nothing too (a round is worth
                // what it buys), so it also reports the full rate. PairRemovalRateTable keeps this
                // count and rescales the shot to other ranges, so a far-range capture that removes
                // nothing must still report the burst the weapon fires once it can penetrate;
                // otherwise the stored count jumps at the penetration range, and the engagement
                // potential shows a cliff there.
                return maxShots;
            }
            if (removalFractionPerHit <= 0f)
            {
                return minShots;
            }

            // Every burst length, then the best net value at the true best value per round. The
            // climb this replaced (2026-10-06) stopped at the first round that did not pay at the
            // value per round found SO FAR, which is the global answer only while the score is
            // concave. Once per-hit removal came from the engine's wound counter, ordinary shots
            // against armor sat in the convex part -- the log2 rate-of-fire bonus lifting a
            // deep-tail first round -- and the climb fired a round more than its own price allowed
            // (BurstSizeSelectionTests.TheClimb_MatchesTheBruteForceBestNetValue). A burst is a
            // handful of lengths and this evaluation is memoized per shot, so the exact search is
            // affordable.
            float[] scores = ScoreBursts(
                shooter, target, weapon, range, hitContext, firingIntoMelee,
                removalFractionPerHit, minShots, maxShots);
            // The round price. A decision that compares plans passes the one price it charges them
            // all (roundPrice), so every plan's burst is chosen at the price it is then charged.
            // Until 2026-10-11 each plan was priced at its OWN best value per round: an un-aimed
            // shot, whose rounds are worth little, found them cheap and chose a nine-round burst,
            // and the fire-timing comparison then charged those nine rounds at the fully aimed
            // shot's value -- so firing without aim could almost never win. Alone, the shot is its
            // own decision: its best value per round is the value of a round, and its free-rounds
            // burst (the highest score) is the pace.
            float price;
            if (roundPrice is float common)
            {
                price = Math.Max(0f, common);
            }
            else
            {
                float valuePerRound = 0f;
                float freeScore = float.MinValue;
                int freeShots = minShots;
                for (int shots = minShots; shots <= maxShots; shots++)
                {
                    float score = scores[shots - minShots];
                    valuePerRound = Math.Max(
                        valuePerRound,
                        Math.Max(0f, score) / Math.Max(1, weapon.GetAmmunitionUnitsForAttack(shots)));
                    if (score > freeScore + 1e-7f)
                    {
                        freeScore = score;
                        freeShots = shots;
                    }
                }
                float pace = Math.Max(0f, freeScore)
                    / Math.Max(1, weapon.GetAmmunitionUnitsForAttack(freeShots));
                price = RangedTargetSelector.Scarcity(
                        RangedTargetSelector.RoundsCarried(weapon),
                        WorkPerShooter(shooter, weapon),
                        pace)
                    * valuePerRound;
            }
            int chosen = minShots;
            float best = float.MinValue;
            for (int shots = minShots; shots <= maxShots; shots++)
            {
                float net = scores[shots - minShots]
                    - (price * weapon.GetAmmunitionUnitsForAttack(shots));
                if (net > best + 1e-7f)
                {
                    best = net;
                    chosen = shots;
                }
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
            int? availableAmmo = null,
            float? roundPrice = null)
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
                ammunitionAvailable,
                roundPrice);
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
