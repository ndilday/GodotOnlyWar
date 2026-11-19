using System;
using System.Collections.Generic;
using OnlyWar.Domain.Math;
using OnlyWar.Domain.Equippables;

namespace OnlyWar.Battles
{
    /// <summary>
    /// One hit location's range-INDEPENDENT contribution to take-out probability.
    ///
    /// <para><c>Weight</c> is <c>w_loc</c>, the location's share of the stance's hit lottery.
    /// <c>PenetrationThreshold</c> is <c>K_loc = effectiveArmor + requiredPenetratingDamage</c>;
    /// <see cref="RemovalMath.CalculateTakeOutProbabilityOnHit"/> forms
    /// <c>requiredRoll = K_loc / damageCoefficient</c>, and since the numerator carries no range
    /// term the whole range dependence of take-out lives in <c>damageCoefficient(r)</c>. That is
    /// what makes <see cref="PairRemovalTerm.TakeOutProbabilityAt"/> a fixed-size sum of normal
    /// CDFs instead of a hit-location and wound-state traversal.</para>
    ///
    /// <para><c>RequiredRatio</c> is the wound ratio the location must reach to disable, given the
    /// wounds it already carries.</para>
    ///
    /// <para>THE WOUND LADDER (2026-10-06). The resolver's damage-to-wound-ratio map is affine
    /// (<c>ratio = (d - effectiveArmor) * weaponWoundMultiplier * woundMultiplier / constitution</c>,
    /// with <c>effectiveArmor = (wornArmor + naturalArmor) * armorMultiplier</c>),
    /// so the damage at which a hit reaches wound ratio <c>r</c> is
    /// <c>ZeroProgressThreshold + DamagePerRatio * r</c>, with
    /// <c>ZeroProgressThreshold = effectiveArmor</c> and
    /// <c>DamagePerRatio = constitution / (locationWoundMultiplier * weaponWoundMultiplier)</c>.
    /// Any hit that beats <see cref="EffectiveArmor"/> wounds (a Negligible at least). The
    /// location's wound counter holds at most five wounds of a level, six of which become one of
    /// the next, so it is a base-6 number in Negligible units; <see cref="Gap"/> is how many such
    /// units remain before the location reaches its cripple or sever value. Those numbers are all
    /// <see cref="RemovalMath"/> needs to price the hits that soften a target before the one that
    /// takes it out, in the engine's own currency.</para>
    /// </summary>
    internal readonly struct TakeOutLocationTerm
    {
        internal float Weight { get; }
        internal float PenetrationThreshold { get; }
        internal float RequiredRatio { get; }
        /// <summary>The damage at which this location's wound ratio is 0.</summary>
        internal float ZeroProgressThreshold { get; }
        /// <summary>
        /// The damage a hit must beat to wound at all: worn plus natural armor, scaled by the
        /// weapon's armor multiplier.
        /// </summary>
        internal float EffectiveArmor { get; }
        /// <summary>Damage per unit of wound ratio at this location.</summary>
        internal float DamagePerRatio { get; }
        /// <summary>Negligible-wound units left before this location disables.</summary>
        internal float Gap { get; }
        /// <summary>
        /// The location's natural armor as the estimate perceives it, unscaled. The unit-weapon
        /// walk carries it so that rescaling to a weapon can add it to worn armor before the
        /// weapon's armor multiplier applies; it is already inside <see cref="EffectiveArmor"/> of
        /// a rescaled term.
        /// </summary>
        internal float NaturalArmor { get; }

        internal TakeOutLocationTerm(
            float weight,
            float penetrationThreshold,
            float requiredRatio,
            float zeroProgressThreshold,
            float effectiveArmor,
            float damagePerRatio,
            float gap,
            float naturalArmor = 0f)
        {
            Weight = weight;
            PenetrationThreshold = penetrationThreshold;
            RequiredRatio = requiredRatio;
            ZeroProgressThreshold = zeroProgressThreshold;
            EffectiveArmor = effectiveArmor;
            DamagePerRatio = damagePerRatio;
            Gap = gap;
            NaturalArmor = naturalArmor;
        }
    }

    /// <summary>
    /// One shooter's expected battle-value removal against one enemy soldier, captured at a
    /// reference range and rescalable to any other range in closed form.
    ///
    /// <para>Hit rescaling is exact up to float rounding. The to-hit total is a sum in which range
    /// enters only through
    /// <see cref="BattleModifiersUtil.CalculateRangeModifier"/> =
    /// <c>k * ln(2/(r+v))</c>, so the total at range <c>r</c> is the reference total plus
    /// <c>k * ln((r0+v0)/(r+v))</c> and
    /// <c>hit(r) = Phi((total0 + k*ln((r0+v0)/(r+v)) - 10.5) / 3)</c> -- one <c>ln</c>, one CDF.</para>
    ///
    /// <para>Take-out rescaling is EXACT for a non-degrading weapon, because
    /// <see cref="BattleModifiersUtil.CalculateDamageAtRange"/> returns a flat
    /// <c>DamageMultiplier</c> in that case and take-out is genuinely range-independent; those
    /// terms carry no location vector at all. A degrading weapon carries the
    /// <see cref="TakeOutLocationTerm"/> vector and re-evaluates the sum at the new damage
    /// coefficient.</para>
    ///
    /// <para>APPROXIMATION, deliberate and documented: the shot count baked into <c>total0</c> (via
    /// the rate-of-fire modifier) is held fixed under rescaling. The live path re-derives shots
    /// from the hit probability, so a rescaled rate can differ from a full re-evaluation for a
    /// burst weapon whose chosen rate of fire would change with range. Rerunning that fixed point
    /// would mean calling the targeting stack, which is exactly what the engagement potential's
    /// per-option projections must not do.</para>
    /// </summary>
    internal sealed class PairRemovalTerm
    {
        internal int ShooterId { get; }
        internal int TargetId { get; }
        internal RangedWeaponTemplate WeaponTemplate { get; }
        /// <summary>total0 -- the pre-roll to-hit total at (ReferenceRange, ReferenceTargetSpeed).</summary>
        internal float ReferenceHitTotal { get; }
        /// <summary>
        /// The shot count total0's rate-of-fire modifier was taken at, and the cap on how many
        /// hits the burst model may credit. Held fixed under rescaling -- the same deliberate
        /// approximation the class summary documents for total0 itself.
        /// </summary>
        internal int ReferenceShotsToFire { get; }
        /// <summary>r0.</summary>
        internal float ReferenceRange { get; }
        /// <summary>v0 -- the relative target speed the reference range modifier was taken at.</summary>
        internal float ReferenceTargetSpeed { get; }
        /// <summary>takeOut0. Also the answer at every range for a non-degrading weapon.</summary>
        internal float ReferenceTakeOut { get; }
        /// <summary>
        /// PHASE 5. <c>E[woundProgress; no takeout]</c> at the reference range -- like
        /// <see cref="ReferenceTakeOut"/>, exact at every range for a non-degrading weapon.
        /// </summary>
        internal float ReferenceWoundProgress { get; }
        internal float TargetBattleValue { get; }
        /// <summary>
        /// PHASE 5. Reach for THIS shooter with THIS weapon (a thrown weapon's reach scales with
        /// the thrower's Strength). Beyond it the shooter cannot fire at all, so the rescaled rate
        /// must be exactly 0 rather than the smooth Gaussian tail -- and it must become positive
        /// again once the potential projects the squads inside reach, which is the gradient that
        /// tells an out-of-range squad closing is worth something.
        /// </summary>
        internal float MaximumEffectiveRange { get; }
        /// <summary>Null for a non-degrading weapon, where take-out does not vary with range.</summary>
        internal IReadOnlyList<TakeOutLocationTerm> TakeOutTerms { get; }
        /// <summary>
        /// The aimed shots the shot planner weighs against firing now
        /// (<see cref="RangedTargetSelector.EvaluateFireTiming"/>), one per number of aim turns, at
        /// the reference range and speed: each one's to-hit total, the burst the planner chooses
        /// for it, and the turns the whole cycle takes (aim turns + the shot). Empty when the
        /// weapon cannot be aimed (a cone or other template weapon). Read only by
        /// <see cref="SustainedRateAt"/>.
        /// </summary>
        internal IReadOnlyList<AimPlan> AimPlans { get; }
        /// <summary>
        /// The ammunition scarcity the planner's round charge uses
        /// (<see cref="RangedTargetSelector.Scarcity"/>: the rounds carried against the shooter's
        /// share of the work), captured with the term.
        /// </summary>
        internal float AmmunitionScarcity { get; }
        /// <summary>
        /// Rounds a shot spends, as the planner counts them: null for the burst itself, 0 for a
        /// weapon whose supply never runs down, 1 for a consumable item.
        /// </summary>
        internal int? FixedRoundsPerShot { get; }
        /// <summary>
        /// The un-aimed shot a standing shooter would fire now, its burst chosen at the same round
        /// price as <see cref="AimPlans"/> (Turns = 1). Null when the weapon cannot be aimed; then
        /// <see cref="SustainedRateAt"/> reads the reference shot, as <see cref="RemovalAt"/> does.
        /// </summary>
        internal AimPlan? StandingShot { get; }

        internal PairRemovalTerm(
            int shooterId,
            int targetId,
            RangedWeaponTemplate weaponTemplate,
            float referenceHitTotal,
            int referenceShotsToFire,
            float referenceRange,
            float referenceTargetSpeed,
            float referenceTakeOut,
            float referenceWoundProgress,
            float targetBattleValue,
            float maximumEffectiveRange,
            IReadOnlyList<TakeOutLocationTerm> takeOutTerms,
            IReadOnlyList<AimPlan> aimPlans = null,
            float ammunitionScarcity = 0,
            int? fixedRoundsPerShot = null,
            AimPlan? standingShot = null)
        {
            StandingShot = standingShot;
            ShooterId = shooterId;
            TargetId = targetId;
            WeaponTemplate = weaponTemplate;
            ReferenceHitTotal = referenceHitTotal;
            ReferenceShotsToFire = referenceShotsToFire;
            ReferenceRange = referenceRange;
            ReferenceTargetSpeed = referenceTargetSpeed;
            ReferenceTakeOut = referenceTakeOut;
            ReferenceWoundProgress = referenceWoundProgress;
            TargetBattleValue = targetBattleValue;
            MaximumEffectiveRange = maximumEffectiveRange;
            TakeOutTerms = takeOutTerms;
            _removalTable = takeOutTerms == null || weaponTemplate == null
                ? null
                : RemovalFractionTable.For(takeOutTerms, weaponTemplate);
            AimPlans = aimPlans ?? Array.Empty<AimPlan>();
            AmmunitionScarcity = Math.Clamp(ammunitionScarcity, 0f, 1f);
            FixedRoundsPerShot = fixedRoundsPerShot;
        }

        /// <summary>
        /// Stands in for an unbounded to-hit total where the range term is +infinity. Far enough
        /// above <see cref="RemovalMath.HitRollMean"/> that every shot in any burst lands,
        /// but finite, so it can be fed to the same arithmetic every other range uses.
        /// </summary>
        private const float CertainHitTotal = 1_000f;

        /// <summary>
        /// The pre-roll to-hit total rescaled to <paramref name="range"/>. Split out from
        /// <see cref="HitProbabilityAt"/> because the burst model needs the TOTAL, not the
        /// first-shot probability: hit k of a burst is its own threshold on the same margin.
        /// </summary>
        internal float HitTotalAt(float range, float targetSpeed)
        {
            float referenceDenominator = ReferenceRange + ReferenceTargetSpeed;
            float denominator = range + targetSpeed;
            if (denominator <= 0f)
            {
                // ln(2/0) is +infinity: point blank against a stationary target never misses.
                return CertainHitTotal;
            }
            if (referenceDenominator <= 0f)
            {
                // No usable reference to shift from; the captured total is all we know.
                return ReferenceHitTotal;
            }
            return ReferenceHitTotal
                + (BattleModifiersUtil.RangeModifierCoefficient
                    * (float)Math.Log(referenceDenominator / denominator));
        }

        internal float HitProbabilityAt(float range, float targetSpeed)
        {
            return GaussianCalculator.ApproximateNormalCDF(
                (HitTotalAt(range, targetSpeed) - RemovalMath.HitRollMean)
                    / RemovalMath.HitRollStdDev);
        }

        internal float TakeOutProbabilityAt(float range)
        {
            if (TakeOutTerms == null)
            {
                return ReferenceTakeOut;
            }
            return RemovalMath.EvaluateTakeOutProbability(
                TakeOutTerms,
                BattleModifiersUtil.CalculateDamageAtRange(WeaponTemplate, range));
        }

        /// <summary>
        /// Expected enemy battle value this shooter removes from this target in one turn at
        /// <paramref name="range"/> -- the same
        /// <c>hit * (takeOut + woundProgress) * targetBV</c> currency
        /// <see cref="BattleSquadPlanner.EvaluateRangedTarget"/> reports, so it is directly
        /// commensurable with `outgoing`. Zero beyond <see cref="MaximumEffectiveRange"/>: the
        /// shooter cannot fire at all there. This is the UN-AIMED shot; see
        /// <see cref="SustainedRateAt"/> for the rate that includes aiming.
        /// </summary>
        internal float RemovalAt(float range, float targetSpeed, float shooterBulkMultiplier)
        {
            if (MaximumEffectiveRange > 0f && range > MaximumEffectiveRange)
            {
                return 0f;
            }
            float hitTotal = HitTotalAt(range, targetSpeed)
                - (WeaponTemplate.Bulk * Math.Max(0f, shooterBulkMultiplier));
            // Same burst model as EvaluateRangedTarget -- the doc comment above promises this is
            // the identical currency, so the two must integrate the recoil loop the same way.
            return RemovalMath.ExpectedBurstRemovalFraction(
                hitTotal,
                ReferenceShotsToFire,
                WeaponTemplate.Recoil,
                RemovalFractionAt(range)) * TargetBattleValue;
        }

        internal float RemovalAt(float range, float targetSpeed) =>
            RemovalAt(range, targetSpeed, 0f);

        internal float RemovalAt(float range) => RemovalAt(range, ReferenceTargetSpeed);

        /// <summary>
        /// Expected battle value a STATIONARY shooter removes per turn, sustained, by the plan the
        /// shot planner itself would choose at this range: fire every turn un-aimed, or one of the
        /// aim-then-fire cycles in <see cref="AimPlans"/>. The choice is the planner's rule
        /// (<see cref="RangedTargetSelector.EvaluateFireTiming"/>): each plan's value per turn net
        /// of the round charge (scarcity x the best value per round on offer x the rounds it
        /// spends), firing now when that is at least the best prepared rate. The rate credited is
        /// the chosen plan's GROSS removal per turn -- what it takes off the enemy; the round
        /// charge decides how the soldier fires, not what a turn of it is worth. Both sides stand
        /// in a standing phase, so no target movement is projected.
        ///
        /// <para>HISTORY. Until 2026-10-06 this was the best of up to three extra aim turns at the
        /// un-aimed burst, which credited Neophytes with a fully aimed ten-round burst they never
        /// fired. From then until 2026-10-09 it was one aim turn (product-owner decision), which the
        /// round charge made wrong in both directions: it puts the charge's preferred plan at four
        /// turns of aim for most soldiers, so Neophyte Hybrids were credited 13.4 battle value a
        /// turn and delivered 9.7, and Shoota Boyz were credited 1.6 and delivered 3.9. The
        /// planner then believed the Hybrids out-shot the Orks several times over, and they crept
        /// forward under fire without shooting (Design/Reference/BattleLogic.md §5.3).</para>
        ///
        /// <para>The plans are captured at the reference range and rescaled, so the choice can
        /// change with range: closer in, a shorter aim or firing every turn usually wins.</para>
        /// </summary>
        internal float SustainedRateAt(float range)
        {
            (float gross, float turns) = SustainedCycleAt(range);
            return gross / turns;
        }

        /// <summary>
        /// The cycle behind <see cref="SustainedRateAt"/>: the gross removal of the shot the
        /// soldier fires and the turns the cycle takes (1 for firing every turn un-aimed).
        ///
        /// <para>THE CYCLE THE SOLDIER REALLY FIRES (2026-10-23). This credited the best cycle on
        /// offer -- usually the full aim -- while the shot planner, once aiming, fires at the first
        /// bonus whose shot now beats every further aim per turn it still takes, which is usually
        /// after one or two aim turns (<see cref="RangedTargetSelector.PlannerCycle"/>). PDF shots
        /// averaged an aim bonus of 0.2-0.7 and the table, and with it the engagement potential,
        /// credited about twice the fire the squads delivered: Hormagaunt broods broke in the
        /// forecast after 13-20% losses that never broke them in battle, and the potential jumped by
        /// 20-30 battle value across a few yards.</para>
        ///
        /// <para>Turns is a float because between two cycle nodes (<see cref="CycleNodeRatio"/>)
        /// the cycle is interpolated, and a cycle of 1 turn next to one of 3 reads as 2 halfway.</para>
        /// </summary>
        internal (float Gross, float Turns) SustainedCycleAt(float range)
        {
            (float gross, float turns, _) = SustainedCycleDetailAt(range);
            return (gross, turns);
        }

        /// <summary>
        /// Rounds this shooter spends per turn on the cycle <see cref="SustainedRateAt"/> credits:
        /// the chosen shot's rounds over the turns the cycle takes. 0 for a supply that never runs
        /// down (<see cref="CountsRounds"/>) and beyond reach.
        /// </summary>
        internal float SustainedRoundsAt(float range)
        {
            if (!CountsRounds || (MaximumEffectiveRange > 0f && range > MaximumEffectiveRange))
            {
                return 0f;
            }
            return SustainedCycleDetailAt(range).RoundsPerTurn;
        }

        /// <summary>
        /// Rounds this shooter spends per turn firing every turn un-aimed (the shot
        /// <see cref="RemovalAt(float, float, float)"/> prices): the reference burst. 0 for a supply
        /// that never runs down and beyond reach.
        /// </summary>
        internal float RoundsPerTurnAt(float range) =>
            !CountsRounds || (MaximumEffectiveRange > 0f && range > MaximumEffectiveRange)
                ? 0f
                : RoundsUsed(ReferenceShotsToFire);

        /// <summary>
        /// Whether the weapon's rounds run down (<see cref="RangedTargetSelector.IsNeverScarce"/>
        /// is false), so the forecast's ammunition budget counts what it fires.
        /// </summary>
        internal bool CountsRounds { get; init; } = true;

        /// <summary>
        /// The rounds the shooter carries and his share of the work, the inputs of
        /// <see cref="RangedTargetSelector.Scarcity"/>, so the scarcity can be set again at every
        /// range. NaN when not captured (a template weapon, a supply that never runs down, a test
        /// fixture); then <see cref="AmmunitionScarcity"/> is used at every range.
        /// </summary>
        internal float ScarcityRounds { get; init; } = float.NaN;

        /// <inheritdoc cref="ScarcityRounds"/>
        internal float ScarcityWork { get; init; } = float.NaN;

        /// <summary>
        /// How many interchangeable shooters this term stands for (2026-11-11, step 2 of
        /// Design/Reference/ForecastCalibration.md section 7). <see cref="PairRemovalRateTable"/> merges the
        /// shooters of one squad that fire the same weapon, with nearly the same to-hit total and
        /// range, at targets of nearly the same profile in one enemy squad, and builds the term
        /// from the first of them in soldier-id order. Every per-shooter reading of the term
        /// (<see cref="RemovalAt(float, float, float)"/>, <see cref="SustainedCycleAt"/>, the
        /// rounds) is ONE shooter's; <see cref="SquadPairRemovalRate"/> multiplies by this count.
        /// </summary>
        internal int Count { get; init; } = 1;

        /// <summary>
        /// The target soldiers the <see cref="Count"/> shooters aim at, and how many aim at each,
        /// for the focus-fire waste in <see cref="SquadPairRemovalRate.SustainedRateAtRange"/>.
        /// Null means all of them aim at <see cref="TargetId"/>.
        /// </summary>
        internal IReadOnlyList<(int TargetId, int Shooters)> TargetShares { get; init; }

        /// <summary>
        /// A solved cycle at one exact range. Immutable, so a reference to it can be published to
        /// <see cref="_cycleCache"/> and read from other planning threads without a lock.
        /// </summary>
        private sealed record CycleEntry(float Range, float Gross, int Turns, int Shots);

        private const int CycleCacheSlots = 256;

        /// <summary>
        /// MEMO OF <see cref="ComputeSustainedCycleDetailAt"/> (2026-11-10). Choosing the burst again
        /// at every range scores every burst length for every aim plan -- for a ten-round weapon with
        /// four aim plans, fifty burst integrals and up to 275 normal CDFs -- and a CPU profile of an
        /// eight-squad battle put more than a quarter of all planning time in the CDF, most of it
        /// here. The forecast asks the same ranges again and again (the rate and the rounds at one
        /// range, every lane option sampled on the same separations), and the answer depends only
        /// on this term's fields and the range, so it is kept, keyed by the exact range bits.
        /// Direct-mapped and lossy: a collision just recomputes, and a hit is identical to the bit
        /// (at exact ranges the eight-squad duel repeated its 441-turn battle to the last digit).
        /// Burst searches per turn on that battle (8 Neophyte squads v 3 PDF): about 107,000 with no
        /// memo; 34,700 with 64 slots at exact ranges; 19,200 with 64 slots on the one-yard
        /// <see cref="CycleRangeGrid"/>; 11,000 with 256 slots on the grid.
        /// <para>It also reads two process-wide switches
        /// (<see cref="RechooseBurstByRange"/>, <see cref="RangedTargetSelector.ScarcityMargin"/>),
        /// which the diagnostics set once before any battle; a term built before a switch changes
        /// would keep its old answers.</para>
        /// </summary>
        private readonly CycleEntry[] _cycleCache = new CycleEntry[CycleCacheSlots];

        /// <summary>
        /// The range step, in yards, that <see cref="SustainedCycleDetailAt"/> solves on (2026-11-10;
        /// 0 solves at the exact range, as before). Every term is asked at its own reference range
        /// plus the lane's shift, so the ranges asked are continuous and two thirds of the memo's
        /// misses were ranges never asked before. Rounded to this step, a term's cycle is solved
        /// once per yard. Reach is still tested at the exact range, so a soldier never fires past
        /// his weapon's range because of the rounding. Process-wide so the diagnostics can compare
        /// (DUEL_CYCLE_GRID=0). Duels on the grid matched the exact ranges within one battle in a
        /// table: PDF v Hormagaunts 29/71 (same), Tactical v Sluggas 58/42 (62/38), 4 Brood Brother
        /// squads v Warriors at 195 yards 50/50 (62/38 on 8 seeds), Mobs v Tactical 100/0 (same),
        /// Tactical v 3 PDF 100/0 (same).
        /// </summary>
        internal static float CycleRangeGrid = 1f;

        /// <summary>
        /// STEP 3 of Design/Reference/ForecastCalibration.md section 7 (2026-11-11): the ratio between the ranges
        /// of neighbouring cycle nodes. The burst-and-aim search
        /// (<see cref="ComputeSustainedCycleDetailAt"/>) runs only at nodes
        /// <c>(ReferenceRange + 1) x ratio^k - 1</c>, and the rate, the cycle's turns and its rounds
        /// per turn are interpolated linearly in ln(range + 1) between them. 0 (or 1 and less)
        /// solves on <see cref="CycleRangeGrid"/>, as before. Process-wide so the diagnostics can
        /// compare (DUEL_CYCLE_NODE_RATIO).
        ///
        /// <para>WHY A RATIO AND NOT 50-YARD BANDS. The to-hit total moves with
        /// <c>2.47 x ln(range)</c>, so a fixed step in yards is far too coarse close in and finer than
        /// needed far out. At 1.25 neighbouring nodes differ by 0.55 to-hit points (a hit chance
        /// changes by at most 0.07): about 7 yards apart at 25 yards, 50 at 200, 200 at 800.</para>
        ///
        /// <para>WHY THE PLANNER'S SEARCH AND NOT A DOCTRINE BURST. The faction's FireDiscipline only
        /// sets how a squad spreads its fire across lanes; the shot planner picks each burst by its
        /// net value at the round price. A doctrine burst would make the forecast predict a fire the
        /// soldiers do not fire, which is the error the burst-by-range search fixed on 2026-11-08.
        /// So the search stays and runs less often.</para>
        ///
        /// <para>The nodes are anchored on the term's own reference range, so the cycle at that range
        /// is the search's own answer.</para>
        ///
        /// <para>OFF BY DEFAULT (2026-11-11). At 1.25 the searches on an 8 v 3 squad duel fell from
        /// 6,790 to 1,917 a turn, but no duel got faster, so the search is no longer where planning
        /// time goes; and the outcomes moved: 10 Mobs v Tactical went from 100/0 (every earlier run)
        /// to 75/25, and Brood Brother and Tactical v PDF stalls rose. Kept behind the switch for
        /// comparison.</para>
        /// </summary>
        internal static float CycleNodeRatio = 0f;

        private (float Gross, float Turns, float RoundsPerTurn) SustainedCycleDetailAt(float range)
        {
            if (MaximumEffectiveRange > 0f && range > MaximumEffectiveRange)
            {
                return (0f, 1f, 0f);
            }
            float ratio = CycleNodeRatio;
            if (ratio <= 1f)
            {
                return WithRounds(CachedCycleAt(range, true));
            }
            float logRatio = MathF.Log(ratio);
            float referenceLog = MathF.Log(ReferenceRange + 1f);
            float position = (MathF.Log(Math.Max(0f, range) + 1f) - referenceLog) / logRatio;
            float node = MathF.Floor(position);
            float fraction = position - node;
            int lowestNode = (int)MathF.Floor(-referenceLog / logRatio);
            (float Gross, float Turns, float RoundsPerTurn) lower =
                CycleAtNode((int)node, lowestNode, referenceLog, logRatio);
            if (fraction < 1e-6f)
            {
                return lower;
            }
            (float Gross, float Turns, float RoundsPerTurn) upper =
                CycleAtNode((int)node + 1, lowestNode, referenceLog, logRatio);
            float lowerRate = lower.Gross / lower.Turns;
            float rate = lowerRate + (fraction * ((upper.Gross / upper.Turns) - lowerRate));
            float turns = lower.Turns + (fraction * (upper.Turns - lower.Turns));
            float rounds = lower.RoundsPerTurn + (fraction * (upper.RoundsPerTurn - lower.RoundsPerTurn));
            return (rate * turns, turns, rounds);
        }

        /// <summary>
        /// Solved cycle nodes, indexed by node number from the lowest node at or below range 0.
        /// Created on first use; entries are immutable records, so planning threads share them
        /// without a lock (two threads that race on one node compute the same answer). A node past
        /// the array (only when the term has no reach) falls back to the hashed memo.
        /// </summary>
        private CycleEntry[] _cycleNodes;

        private const float UnboundedNodeRange = 3000f;

        private (float Gross, float Turns, float RoundsPerTurn) CycleAtNode(
            int node, int lowestNode, float referenceLog, float logRatio)
        {
            CycleEntry[] nodes = _cycleNodes;
            if (nodes == null)
            {
                float reach = MaximumEffectiveRange > 0f ? MaximumEffectiveRange : UnboundedNodeRange;
                int highestNode = (int)MathF.Ceiling((MathF.Log(reach + 1f) - referenceLog) / logRatio) + 1;
                nodes = new CycleEntry[Math.Max(2, highestNode - lowestNode + 2)];
                _cycleNodes = nodes;
            }
            int index = node - lowestNode;
            float nodeRange = NodeRange(node, referenceLog, logRatio);
            if (index < 0 || index >= nodes.Length)
            {
                return WithRounds(CachedCycleAt(nodeRange, false));
            }
            CycleEntry entry = System.Threading.Volatile.Read(ref nodes[index]);
            if (entry == null)
            {
                (float gross, int turns, int shots) = ComputeSustainedCycleDetailAt(nodeRange);
                entry = new CycleEntry(nodeRange, gross, turns, shots);
                System.Threading.Volatile.Write(ref nodes[index], entry);
            }
            return WithRounds((entry.Gross, entry.Turns, entry.Shots));
        }

        private float NodeRange(float node, float referenceLog, float logRatio)
        {
            float nodeRange = Math.Max(0f, MathF.Exp(referenceLog + (node * logRatio)) - 1f);
            return MaximumEffectiveRange > 0f && nodeRange > MaximumEffectiveRange
                ? MaximumEffectiveRange
                : nodeRange;
        }

        private (float Gross, float Turns, float RoundsPerTurn) WithRounds(
            (float Gross, int Turns, int Shots) cycle)
        {
            float turns = Math.Max(1, cycle.Turns);
            return (cycle.Gross, turns, CountsRounds ? RoundsUsed(cycle.Shots) / turns : 0f);
        }

        /// <summary>
        /// Solved cycles on the <see cref="CycleRangeGrid"/>, indexed by grid step (2026-11-11). The
        /// hashed memo above kept 256 ranges per term, and the forecast asks many more than that
        /// over a battle, so it solved the same yard again after every collision: the cycle search
        /// still made 592,000 burst calls a turn on an 8 v 8 squad duel. One slot per yard up to
        /// the weapon's reach (or <see cref="UnboundedNodeRange"/>) solves each yard once per term.
        /// A hit is the same computation the hashed memo stored, so results are identical to the
        /// bit. Created on first use; entries are immutable, so threads share them without a lock.
        /// </summary>
        private CycleEntry[] _gridCycles;

        private float _gridCyclesStep;

        private int GridCycleSlots(float grid)
        {
            float reach = MaximumEffectiveRange > 0f ? MaximumEffectiveRange : UnboundedNodeRange;
            return (int)MathF.Ceiling(reach / grid) + 2;
        }

        private (float Gross, int Turns, int Shots) GridCycleAt(int step, float range)
        {
            CycleEntry[] cycles = System.Threading.Volatile.Read(ref _gridCycles);
            if (cycles == null || _gridCyclesStep != CycleRangeGrid)
            {
                cycles = new CycleEntry[GridCycleSlots(CycleRangeGrid)];
                _gridCyclesStep = CycleRangeGrid;
                System.Threading.Volatile.Write(ref _gridCycles, cycles);
            }
            CycleEntry entry = System.Threading.Volatile.Read(ref cycles[step]);
            if (entry == null)
            {
                (float gross, int turns, int shots) = ComputeSustainedCycleDetailAt(range);
                entry = new CycleEntry(range, gross, turns, shots);
                System.Threading.Volatile.Write(ref cycles[step], entry);
            }
            return (entry.Gross, entry.Turns, entry.Shots);
        }

        private (float Gross, int Turns, int Shots) CachedCycleAt(float range, bool onGrid)
        {
            if (MaximumEffectiveRange > 0f && range > MaximumEffectiveRange)
            {
                return (0f, 1, 0);
            }
            if (onGrid && CycleRangeGrid > 0f)
            {
                float step = MathF.Round(range / CycleRangeGrid);
                range = step * CycleRangeGrid;
                if (MaximumEffectiveRange > 0f && range > MaximumEffectiveRange)
                {
                    range = MaximumEffectiveRange;
                }
                if (step >= 0f && step < GridCycleSlots(CycleRangeGrid))
                {
                    return GridCycleAt((int)step, range);
                }
            }
            uint bits = unchecked((uint)BitConverter.SingleToInt32Bits(range) * 2654435761u);
            int slot = (int)(bits >> 24);
            CycleEntry cached = System.Threading.Volatile.Read(ref _cycleCache[slot]);
            if (cached != null && cached.Range.Equals(range))
            {
                return (cached.Gross, cached.Turns, cached.Shots);
            }
            (float gross, int turns, int shots) = ComputeSustainedCycleDetailAt(range);
            System.Threading.Volatile.Write(ref _cycleCache[slot], new CycleEntry(range, gross, turns, shots));
            return (gross, turns, shots);
        }

        private (float Gross, int Turns, int Shots) ComputeSustainedCycleDetailAt(float range)
        {
            if (MaximumEffectiveRange > 0f && range > MaximumEffectiveRange)
            {
                return (0f, 1, 0);
            }
            float removalFraction = RemovalFractionAt(range);
            float shift = HitTotalAt(range, ReferenceTargetSpeed) - ReferenceHitTotal;
            AimPlan standing = StandingShot
                ?? new AimPlan(ReferenceHitTotal, ReferenceShotsToFire, 1);
            if (AimPlans.Count == 0 || !RechooseBurstByRange)
            {
                return FixedBurstCycle(standing, shift, removalFraction);
            }

            // THE BURST IS CHOSEN AGAIN AT EVERY RANGE (2026-11-08). The plans' bursts were the
            // shot planner's choice at the reference range, held fixed under rescaling. Far out,
            // where a round buys little, the planner fires one round; the table then priced the
            // same soldier at 183 yards as firing one round there too, where the planner fires
            // ten: a Brood Brother squad built at 732 yards from Warriors was credited 0.13 battle
            // value a turn at 183 yards, against 1.33 for the same squad built at 190 yards, and
            // every Close continuation lost. Each plan now picks its burst at this range by the
            // planner's own rule (RangedShotEvaluator.ChooseShotsToFire): every length scored, the
            // best net at the round price, the price being the scarcity times the best value per
            // round on offer here. A plan's to-hit total carries the rate-of-fire modifier of its
            // own burst, so that modifier is swapped for each length.
            (int minShots, int maxShots) = BurstBounds();
            int planCount = AimPlans.Count + 1;
            int lengths = maxShots - minShots + 1;
            Span<float> scores = stackalloc float[planCount * lengths];
            float valuePerRound = 0f;
            for (int plan = 0; plan < planCount; plan++)
            {
                AimPlan aim = plan == 0 ? standing : AimPlans[plan - 1];
                float baseTotal = aim.HitTotal + shift
                    - BattleModifiersUtil.CalculateRateOfFireModifier(Math.Max(1, aim.ShotsToFire));
                for (int shots = minShots; shots <= maxShots; shots++)
                {
                    float score = RemovalMath.ExpectedBurstRemovalFraction(
                        baseTotal + BattleModifiersUtil.CalculateRateOfFireModifier(shots),
                        shots,
                        WeaponTemplate.Recoil,
                        removalFraction) * TargetBattleValue;
                    scores[(plan * lengths) + shots - minShots] = score;
                    valuePerRound = Math.Max(valuePerRound, PerRound(score, shots));
                }
            }

            // The scarcity too is set again here, by RangedTargetSelector.RoundPrice's rule: the work
            // over the value a round buys at the pace of the fastest plan fired with free rounds.
            // Captured at 732 yards, where a lasgun hit removes almost nothing, the work looked
            // endless, scarcity was 1, every round cost as much as the best round, and no burst
            // longer than one ever paid at any range.
            float scarcity = AmmunitionScarcity;
            if (!float.IsNaN(ScarcityRounds) && !float.IsNaN(ScarcityWork))
            {
                float fastestRate = float.MinValue;
                float fastValue = 0f;
                for (int plan = 0; plan < planCount; plan++)
                {
                    float freeScore = float.MinValue;
                    int freeShots = minShots;
                    for (int shots = minShots; shots <= maxShots; shots++)
                    {
                        float score = scores[(plan * lengths) + shots - minShots];
                        if (score > freeScore + 1e-7f)
                        {
                            freeScore = score;
                            freeShots = shots;
                        }
                    }
                    int planTurns = plan == 0 ? 1 : AimPlans[plan - 1].Turns;
                    float rate = freeScore / Math.Max(1, planTurns);
                    if (rate > fastestRate)
                    {
                        fastestRate = rate;
                        fastValue = Math.Max(0f, freeScore) / Math.Max(1, RoundsUsed(freeShots));
                    }
                }
                scarcity = RangedTargetSelector.Scarcity(ScarcityRounds, ScarcityWork, fastValue);
            }
            float price = scarcity * valuePerRound;
            Span<float> gross = stackalloc float[planCount];
            Span<float> net = stackalloc float[planCount];
            Span<int> chosenShots = stackalloc int[planCount];
            for (int plan = 0; plan < planCount; plan++)
            {
                float best = float.MinValue;
                for (int shots = minShots; shots <= maxShots; shots++)
                {
                    float score = scores[(plan * lengths) + shots - minShots];
                    float value = score - (price * RoundsUsed(shots));
                    if (value > best + 1e-7f)
                    {
                        best = value;
                        gross[plan] = score;
                        chosenShots[plan] = shots;
                    }
                }
                net[plan] = best;
            }

            Span<float> aimNet = stackalloc float[AimPlans.Count];
            Span<int> turns = stackalloc int[AimPlans.Count];
            for (int index = 0; index < AimPlans.Count; index++)
            {
                aimNet[index] = net[index + 1];
                turns[index] = AimPlans[index].Turns;
            }
            int fired = RangedTargetSelector.PlannerCycle(net[0], aimNet, turns);
            return fired < 0
                ? (gross[0], 1, chosenShots[0])
                : (gross[fired + 1], AimPlans[fired].Turns, chosenShots[fired + 1]);
        }

        /// <summary>
        /// Whether <see cref="SustainedCycleDetailAt"/> chooses each plan's burst again at the range
        /// asked (true since 2026-11-08), or keeps the bursts chosen at the reference range, as
        /// before. Process-wide; a static field so the diagnostics can compare (DUEL_BURST_BY_RANGE=0).
        /// </summary>
        internal static bool RechooseBurstByRange = true;

        /// <summary>
        /// The burst lengths the shot planner considers (RangedShotEvaluator.BurstBounds): one to
        /// the rate of fire, and at least a quarter of it for a weapon firing more than ten. A
        /// weapon whose supply never runs down fires its full rate, as the planner fires it.
        /// </summary>
        private (int Min, int Max) BurstBounds()
        {
            int maxShots = Math.Max(1, (int)WeaponTemplate.RateOfFire);
            int minShots = WeaponTemplate.RateOfFire > 10
                ? Math.Min(WeaponTemplate.RateOfFire / 4, maxShots)
                : 1;
            return FixedRoundsPerShot == 0 ? (maxShots, maxShots) : (minShots, Math.Max(minShots, maxShots));
        }

        private (float Gross, int Turns, int Shots) FixedBurstCycle(
            AimPlan standing, float shift, float removalFraction)
        {
            float shootNow = RemovalMath.ExpectedBurstRemovalFraction(
                standing.HitTotal + shift,
                standing.ShotsToFire,
                WeaponTemplate.Recoil,
                removalFraction) * TargetBattleValue;
            if (AimPlans.Count == 0)
            {
                return (shootNow, 1, standing.ShotsToFire);
            }

            Span<float> gross = stackalloc float[AimPlans.Count];
            float valuePerRound = PerRound(shootNow, standing.ShotsToFire);
            for (int index = 0; index < AimPlans.Count; index++)
            {
                AimPlan plan = AimPlans[index];
                gross[index] = RemovalMath.ExpectedBurstRemovalFraction(
                    plan.HitTotal + shift,
                    plan.ShotsToFire,
                    WeaponTemplate.Recoil,
                    removalFraction) * TargetBattleValue;
                valuePerRound = Math.Max(valuePerRound, PerRound(gross[index], plan.ShotsToFire));
            }

            float shootNowNet = shootNow
                - AmmunitionScarcity * valuePerRound * RoundsUsed(standing.ShotsToFire);
            Span<float> net = stackalloc float[AimPlans.Count];
            Span<int> turns = stackalloc int[AimPlans.Count];
            for (int index = 0; index < AimPlans.Count; index++)
            {
                AimPlan plan = AimPlans[index];
                net[index] = gross[index]
                    - AmmunitionScarcity * valuePerRound * RoundsUsed(plan.ShotsToFire);
                turns[index] = plan.Turns;
            }
            int fired = RangedTargetSelector.PlannerCycle(shootNowNet, net, turns);
            return fired < 0
                ? (shootNow, 1, standing.ShotsToFire)
                : (gross[fired], AimPlans[fired].Turns, AimPlans[fired].ShotsToFire);
        }

        private int RoundsUsed(int shots) => FixedRoundsPerShot ?? Math.Max(1, shots);

        private float PerRound(float value, int shots)
        {
            int rounds = RoundsUsed(shots);
            return rounds == 0 ? 0f : Math.Max(0f, value) / rounds;
        }

        private float RemovalFractionAt(float range) =>
            _removalTable == null
                ? RemovalMath.CombineRemovalFraction(ReferenceTakeOut, ReferenceWoundProgress)
                : _removalTable.At(range);

        /// <summary>
        /// <see cref="TakeOutTerms"/> against <see cref="WeaponTemplate"/> on 50-yard nodes; null
        /// when the term carries no location vector (then the reference values are used).
        /// </summary>
        private readonly RemovalFractionTable _removalTable;
    }

    /// <summary>
    /// One aim-then-fire cycle the shot planner weighs: the aimed shot's to-hit total at the
    /// reference range, the burst the planner chooses for it, and the turns the cycle takes.
    /// </summary>
    internal readonly record struct AimPlan(float HitTotal, int ShotsToFire, int Turns);

    /// <summary>
    /// One (shooter squad, target squad) cell of the Phase 4 removal-rate table
    /// (Design/Reference/BattleLogic.md).
    ///
    /// <para>SEMANTICS. The rate is the SUM over the shooter squad's able, placed, ranged-armed
    /// soldiers of each soldier's single best target's expected removal -- the same
    /// per-soldier-argmax aggregation `outgoing` uses in <c>EvaluateImmediateActionValue</c>, and
    /// deliberately NOT an average, because the engagement scorer wants a squad-level battle value
    /// per turn. A soldier contributes to exactly one cell: the one containing the enemy squad its
    /// best target belongs to. A pair with no shooter aimed into it is absent from the table,
    /// meaning rate 0.</para>
    ///
    /// <para>The rate is captured for the STATIONARY, un-aimed, no-bulk reference posture. The
    /// consumer rescales the cached terms for target motion and the enemy's projected bulk. (The
    /// removed bounded policy rollout also applied an explicit per-policy outgoing retention
    /// here.) <see cref="SustainedRateAtRange"/> is the one reading that includes aiming.</para>
    ///
    /// <para>No cap is applied. <c>EngagementExchangeModel.EvaluateOutgoingExchangeRate</c>,
    /// <c>EvaluateSustainedOutgoingRate</c> and <c>EvaluateIncomingExchangeRate</c> clamp to the
    /// defender's <c>TotalAbleBattleValue</c>, and `outgoing` caps per target soldier; leaving the
    /// raw sum here keeps the choice with the consumer.</para>
    ///
    /// <para>PHASE 5 RESOLVED the pair-weights question, asymmetrically. The OUTGOING exchange
    /// rate reads a cell directly with NO <c>PairWeights</c> factor, because this table is
    /// already target-selected and multiplying by a normalized allocation would divide the squad's
    /// fire twice; the INCOMING rate keeps <c>PairWeights</c>, applied to the enemy's whole-squad
    /// rate, because there the allocation is the real question. See
    /// <c>EngagementExchangeModel.EvaluateOutgoingExchangeRate</c>.</para>
    /// </summary>
    internal sealed class SquadPairRemovalRate
    {
        internal int ShooterSquadId { get; }
        internal int TargetSquadId { get; }
        /// <summary>
        /// Representative shooter-to-target distance at capture: the mean of the terms' own
        /// reference ranges. Rescaling shifts every term by <c>range - ReferencePairRange</c>, so
        /// per-soldier geometry offsets within the two squads survive, and
        /// <c>RateAtRange(ReferencePairRange) == ReferenceRate</c> exactly.
        /// </summary>
        internal float ReferencePairRange { get; }
        internal float ReferenceRate { get; }
        internal IReadOnlyList<PairRemovalTerm> Terms { get; }

        internal SquadPairRemovalRate(
            int shooterSquadId,
            int targetSquadId,
            IReadOnlyList<PairRemovalTerm> terms)
        {
            ShooterSquadId = shooterSquadId;
            TargetSquadId = targetSquadId;
            Terms = terms ?? Array.Empty<PairRemovalTerm>();
            float rangeTotal = 0f;
            float rateTotal = 0f;
            int shooters = 0;
            for (int index = 0; index < Terms.Count; index++)
            {
                int count = Terms[index].Count;
                shooters += count;
                rangeTotal += count * Terms[index].ReferenceRange;
                rateTotal += count * Terms[index].RemovalAt(Terms[index].ReferenceRange);
            }
            ShooterCount = shooters;
            ReferencePairRange = shooters == 0 ? 0f : rangeTotal / shooters;
            ReferenceRate = rateTotal;

            // Shooters that share a target soldier, for the focus-fire waste in
            // SustainedRateAtRange: one share per (term, target soldier) with its shooter count.
            List<(int Term, int Group, int Shooters)> shares = [];
            Dictionary<int, int> groups = [];
            List<int> sizes = [];
            for (int index = 0; index < Terms.Count; index++)
            {
                PairRemovalTerm term = Terms[index];
                IReadOnlyList<(int TargetId, int Shooters)> targets =
                    term.TargetShares ?? [(term.TargetId, term.Count)];
                foreach ((int targetId, int count) in targets)
                {
                    if (!groups.TryGetValue(targetId, out int group))
                    {
                        group = groups.Count;
                        groups[targetId] = group;
                        sizes.Add(0);
                    }
                    shares.Add((index, group, count));
                    sizes[group] += count;
                }
            }
            _shares = shares.ToArray();
            _targetGroupSizes = sizes.ToArray();
        }

        /// <summary>The shooters the terms stand for (the sum of <see cref="PairRemovalTerm.Count"/>).</summary>
        internal int ShooterCount { get; }

        private readonly (int Term, int Group, int Shooters)[] _shares;
        private readonly int[] _targetGroupSizes;

        // Cap on the per-turn chance that squadmates take a shared target out before this shooter
        // fires, so an aim at a target several others also hold still counts for something.
        private const float MaximumFocusLossPerTurn = 0.9f;

        /// <summary>
        /// Battle value this squad removes from the paired enemy squad per turn at a projected
        /// centroid separation. Closed-form arithmetic over the cached terms: per term one
        /// <c>ln</c>, one normal CDF, and (degrading weapons only) a fixed-size CDF sum. Nothing
        /// here touches the grid, the targeting stack, or wound state, which is what lets the
        /// engagement potential call it for every option x enemy squad x (current, projected)
        /// separation. (It was first sized for a 3-policy x 2-ply x N-squad lookahead, since
        /// removed.)
        /// </summary>
        internal float RateAtRange(float range)
        {
            return RateAtRangeCore(range, true, 0f, 0f);
        }

        internal float RateAtRange(
            float range,
            float targetSpeed,
            float shooterBulkMultiplier)
        {
            return RateAtRangeCore(range, false, targetSpeed, shooterBulkMultiplier);
        }

        private float RateAtRangeCore(
            float range,
            bool useReferenceTargetSpeed,
            float targetSpeed,
            float shooterBulkMultiplier)
        {
            if (Terms.Count == 0)
            {
                return 0f;
            }
            float shift = range - ReferencePairRange;
            float total = 0f;
            for (int index = 0; index < Terms.Count; index++)
            {
                PairRemovalTerm term = Terms[index];
                total += term.Count * term.RemovalAt(
                    Math.Max(0f, term.ReferenceRange + shift),
                    useReferenceTargetSpeed ? term.ReferenceTargetSpeed : targetSpeed,
                    shooterBulkMultiplier);
            }
            return total;
        }

        /// <summary>
        /// <see cref="RateAtRange(float)"/> for a squad that stands and may aim: each term at
        /// <see cref="PairRemovalTerm.SustainedRateAt"/>.
        ///
        /// <para>The STANDING phases of <see cref="EngagementExchangeForecast"/> read it: a squad
        /// that stands can aim and then fire. Read at the un-aimed rate, a sniper rifle (Accuracy
        /// 9) scores almost nothing at long range, and sniper scouts once ran at 'Eavy Nobz for 39
        /// turns without a shot (Grist Nine Epsilon, 2026-09-23).</para>
        ///
        /// <para>The forecast once priced a moving option at this rate on arrival without charging
        /// for the aim that moving throws away, so every step toward the enemy looked like a gain
        /// (a rifle squad in its band left Hold to run at an approaching melee squad). It charges
        /// for it now: the approach phase fires nothing, the move forfeits any stored aim through
        /// the readiness term, and the immediate term fires nothing on a run.</para>
        ///
        /// <para>FOCUS-FIRE WASTE (2026-10-23). Squadmates often hold aims on the same soldier --
        /// up to 15 PDF soldiers on one Hormagaunt -- and when one shot drops him every other aim on
        /// him is lost: 113 of 503 PDF aim turns at 150 yards went that way. A term whose target is
        /// shared is credited its cycle only if the target survives the cycle's aim turns:
        /// <c>gross x (1 - q)^(turns - 1) / turns</c>, with <c>q</c> the other terms' removal on
        /// that soldier per turn over his battle value, capped at
        /// <see cref="MaximumFocusLossPerTurn"/>. Other squads firing at the same soldier are not
        /// counted.</para>
        /// </summary>
        /// <summary>
        /// Rounds this squad spends per turn on the cycles <see cref="SustainedRateAtRange"/>
        /// credits, at a projected separation (each term's <see cref="PairRemovalTerm.SustainedRoundsAt"/>).
        /// </summary>
        internal float SustainedRoundsAtRange(float range)
        {
            float shift = range - ReferencePairRange;
            float total = 0f;
            for (int index = 0; index < Terms.Count; index++)
            {
                total += Terms[index].Count
                    * Terms[index].SustainedRoundsAt(Math.Max(0f, Terms[index].ReferenceRange + shift));
            }
            return total;
        }

        /// <summary>
        /// Rounds this squad spends per turn firing un-aimed every turn, as
        /// <see cref="RateAtRange(float, float, float)"/> prices it.
        /// </summary>
        internal float RoundsPerTurnAtRange(float range)
        {
            float shift = range - ReferencePairRange;
            float total = 0f;
            for (int index = 0; index < Terms.Count; index++)
            {
                total += Terms[index].Count
                    * Terms[index].RoundsPerTurnAt(Math.Max(0f, Terms[index].ReferenceRange + shift));
            }
            return total;
        }

        internal float SustainedRateAtRange(float range)
        {
            if (Terms.Count == 0)
            {
                return 0f;
            }
            float shift = range - ReferencePairRange;
            int count = Terms.Count;
            Span<float> gross = count <= 128 ? stackalloc float[count] : new float[count];
            Span<float> turns = count <= 128 ? stackalloc float[count] : new float[count];
            Span<float> groupRate = _targetGroupSizes.Length <= 128
                ? stackalloc float[_targetGroupSizes.Length]
                : new float[_targetGroupSizes.Length];
            groupRate.Clear();
            for (int index = 0; index < count; index++)
            {
                PairRemovalTerm term = Terms[index];
                (gross[index], turns[index]) =
                    term.SustainedCycleAt(Math.Max(0f, term.ReferenceRange + shift));
            }
            foreach ((int termIndex, int group, int shooters) in _shares)
            {
                groupRate[group] += shooters * gross[termIndex] / turns[termIndex];
            }
            float total = 0f;
            foreach ((int termIndex, int group, int shooters) in _shares)
            {
                float rate = gross[termIndex] / turns[termIndex];
                if (turns[termIndex] > 1f && _targetGroupSizes[group] > 1)
                {
                    // The other shooters on this soldier: everyone on him but this one.
                    float others = groupRate[group] - rate;
                    float loss = Math.Clamp(
                        others / Math.Max(1f, Terms[termIndex].TargetBattleValue),
                        0f,
                        MaximumFocusLossPerTurn);
                    rate *= MathF.Pow(1f - loss, turns[termIndex] - 1);
                }
                total += shooters * rate;
            }
            return total;
        }
    }
}
