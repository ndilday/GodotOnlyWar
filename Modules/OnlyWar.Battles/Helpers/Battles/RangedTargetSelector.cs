using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Battles.Models;
using OnlyWar.Domain.Equippables;

namespace OnlyWar.Battles
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
        // spreads a squad's fire across the target squad instead of piling every rifle onto one
        // man. The shooter's place in its own line and the target's place in its squad's line are
        // both normalized to 0..1 (see SquadLaneFrame), and a candidate's selection score is
        // reduced by this fraction of itself per unit of lane mismatch, scaled by the shooter
        // faction's FireDiscipline. At 0.5 a fully disciplined shooter needs a target in the
        // opposite lane to be twice as good before it crosses over, while a near-tie between two
        // similar men is always settled by lane. 0 disables the lane term. It biases selection
        // only and never changes the returned expected-value score.
        //
        // The penalty is RELATIVE to the shot's own score by design. Until 2026-09-22 it was an
        // absolute 1 BV per grid cell measured against the centroid of the whole enemy force, and
        // shot scores are 2-4 BV: once a withdrawal scattered the enemy squads hundreds of cells
        // apart, the lateral term dwarfed the shot and every shooter picked whichever enemy lay
        // nearest the one line toward that centroid. Grist Nine Epsilon turn 200: 161 marines
        // aimed at one ork in a 13-man squad, and every aim was lost each time it died.
        private const float BaseLaneSpreadFraction = 0.5f;
        // Fire discipline used when a squad has no faction (test fixtures, stray battle squads).
        private const float DefaultFireDiscipline = 0.5f;
        // The planner's "aim can no longer be improved" ceiling. A held aim is judged at this bonus
        // rather than its current one -- see IsExistingAimStillViable.
        internal const int FullAimBonusTurns = 3;
        // Shared with the pursuit fire-window projection. This is a shot-value horizon, not a
        // contact grace-period timer.
        internal const int PursuitFireWindowTurns = FullAimBonusTurns + 2;

        private readonly RangedTargetingServices _services;
        // Aliases onto the bundle, named as the planner named them so the moved bodies read
        // unchanged against their original form.
        private readonly BattleGridManager _grid;
        private readonly IReadOnlyDictionary<int, BattleSoldier> _soldierMap;
        private readonly BattlePlanningContext _context;
        private readonly RangedShotEvaluator _shotEvaluator;

        internal RangedTargetSelector(RangedTargetingServices services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _grid = _services.Grid;
            _soldierMap = _services.SoldierMap;
            _context = _services.Context;
            _shotEvaluator = new RangedShotEvaluator(_services);
        }

        // Compatibility constructor for focused targeting callers that still start from the
        // complete planning bundle. The selector itself retains only the narrow read capability.
        internal RangedTargetSelector(SquadPlanningServices services)
            : this(new RangedTargetingServices(services))
        {
        }

        private bool IsPlaced(BattleSoldier soldier) => _services.IsPlaced(soldier);

        private static float GetBattleValue(BattleSoldier soldier) =>
            RangedTargetingServices.BattleValueOf(soldier);

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
            RangedTargetEvaluation evaluation = _shotEvaluator.EvaluateRangedTarget(
                soldier,
                target,
                weapon,
                range,
                AimAccuracy(soldier, weapon.Template) + Math.Max(aim.Item3, FullAimBonusTurns) + 1);
            return evaluation.Score > 0 && evaluation.HitProbability > StickyMinimumHitProbability;
        }

        /// <summary>
        /// Weighs firing now against aiming first. <see cref="FireTiming.PreparedRate"/> is the best
        /// expected value PER TURN of aiming one or more further turns: max over j of
        /// Net(shot after j more aim turns, at the range projected j turns ahead) / (j + 1).
        /// <see cref="FireTiming.ShootNowValue"/> is the net value of firing now, which takes one
        /// turn. The planner fires when the second is at least the first, and a planned or stored aim
        /// is priced at the prepared shot's gross removal per turn (<see cref="FireTiming.Readiness"/>)
        /// so the engagement potential values aiming in the same currency as its forecast. With no further aim possible
        /// the prepared rate is <see cref="float.MinValue"/>, which always favours firing.
        ///
        /// <para>Net value charges each shot for the rounds it spends: Score − scarcity × (value of
        /// a round) × rounds (<see cref="RoundPrice"/>). A round is worth the best score per round
        /// among the shots being compared, since that is what the round could buy instead, and
        /// scarcity rises from 0 while the rounds carried outlast the soldier's share of the work to
        /// 1 on an empty weapon (<see cref="Scarcity"/>). With enough rounds the comparison is pure
        /// expected value per turn; when they are short, the most round-efficient shot gains. Without this the rate rule
        /// treated rounds as free and pursuing marines emptied four magazines each on 7% bursts
        /// within 80 turns (Grist Nine Epsilon, 2026-09-22, third run).</para>
        ///
        /// <para>This replaced two rules that disagreed. A fresh target was shot only when
        /// P(hit now) × 2 beat P(hit after ONE aim turn), and on the long-range normal tail one aim
        /// turn always multiplies the hit chance by more than two. An aim already started then fired
        /// only at the full bonus of 3, so the soldier spent four turns aiming for a shot that one
        /// aim turn made nearly as good: in Grist Nine Epsilon (2026-09-22) pursuing marines fired
        /// one burst in ten turns. Comparing rates over the same horizon removes both.</para>
        ///
        /// <para>The range projection is what makes a moving target come out right. A target
        /// closing on the shooter improves with every turn of waiting (more aim AND less range), so
        /// aiming keeps winning; a target running away gives back range for every turn spent
        /// aiming, so the shot is taken sooner. At long range the range term is small (the range
        /// modifier's slope falls off as 1/range) and aim dominates; close in it can decide.</para>
        /// </summary>
        internal FireTiming EvaluateFireTiming(
            BattleSoldier soldier,
            BattleSoldier target,
            RangedTargetEvaluation shootNow,
            RangedWeapon aimWeapon,
            float range,
            int? currentAimBonus,
            float bulkMultiplier,
            float aimMultiplier)
        {
            List<(float Range, float Modifier, int Turns)> preparedPlans = [];
            if (soldier != null && target != null && aimWeapon != null && aimMultiplier > 0)
            {
                float rangeChange = ProjectedRangeChangePerTurn(target);
                // AimAction starts a new aim at bonus 0 and adds 1 per further turn; a shot is
                // taken with Accuracy + bonus + 1 (the aimed shot is an all-out attack). Waiting j
                // aim turns and then firing takes j + 1 turns.
                int firstBonus = currentAimBonus.HasValue ? currentAimBonus.Value + 1 : 0;
                for (int bonus = firstBonus, aimTurns = 1;
                    bonus <= FullAimBonusTurns;
                    bonus++, aimTurns++)
                {
                    float projectedRange = Math.Max(1f, range + (rangeChange * aimTurns));
                    if (projectedRange > aimWeapon.Template.MaximumRange)
                    {
                        // Only a receding target leaves range, and it only gets farther.
                        break;
                    }
                    float modifier = -(aimWeapon.Template.Bulk * bulkMultiplier)
                        + ((AimAccuracy(soldier, aimWeapon.Template) + bonus + 1) * aimMultiplier);
                    preparedPlans.Add((projectedRange, modifier, aimTurns + 1));
                }
            }

            // One price for a round in this whole decision (RoundPrice): every plan at its most
            // round-efficient burst sets the value a round could buy and the pace of the fastest
            // plan, and the soldier's share of the work sets how scarce rounds are. Each plan's
            // burst is then chosen at this one price, the price the comparison below charges it
            // (2026-10-11). Before, each plan chose its burst at its own best value per round, and
            // an un-aimed shot came back with a long burst charged at the aimed shot's price.
            List<PlanEfficiency> efficiencies = [];
            if (shootNow != null && soldier != null)
            {
                efficiencies.Add(Efficiency(
                    soldier, shootNow.Target, shootNow.Weapon, shootNow.Range,
                    shootNow.AdditionalToHitModifier, shootNow.TargetSpeed, 1));
            }
            foreach ((float planRange, float modifier, int turns) in preparedPlans)
            {
                efficiencies.Add(Efficiency(
                    soldier, target, aimWeapon, planRange, modifier, null, turns));
            }
            float aimPrice = RoundPrice(soldier, aimWeapon, efficiencies, out _);
            float shootPrice = shootNow == null || shootNow.Weapon == aimWeapon
                ? aimPrice
                : RoundPrice(soldier, shootNow.Weapon, efficiencies, out _);

            if (shootNow != null && soldier != null)
            {
                shootNow = _shotEvaluator.EvaluateRangedTarget(
                    soldier,
                    shootNow.Target,
                    shootNow.Weapon,
                    shootNow.Range,
                    shootNow.AdditionalToHitModifier,
                    shootNow.TargetSpeed,
                    roundPrice: shootPrice);
            }
            List<(RangedTargetEvaluation Shot, int Turns)> prepared = [];
            foreach ((float planRange, float modifier, int turns) in preparedPlans)
            {
                prepared.Add((
                    _shotEvaluator.EvaluateRangedTarget(
                        soldier,
                        target,
                        aimWeapon,
                        planRange,
                        modifier,
                        roundPrice: aimPrice),
                    turns));
            }

            float shootNowValue = shootNow == null
                ? float.MinValue
                : NetShotValue(shootNow, shootPrice);
            float preparedRate = float.MinValue;
            float preparedGrossRate = 0;
            foreach ((RangedTargetEvaluation shot, int turns) in prepared)
            {
                float rate = NetShotValue(shot, aimPrice) / turns;
                if (rate > preparedRate)
                {
                    preparedRate = rate;
                    preparedGrossRate = shot.Score / turns;
                }
            }
            return new FireTiming(
                shootNowValue, preparedRate, shootNow?.Score ?? 0, preparedGrossRate, shootNow);
        }

        /// <summary>
        /// The weapon Accuracy an aimed shot adds for this shooter, as the engine applies it
        /// (ShootAction.CalculateToHitModifiers): capped by the shooter's skill with the weapon. An
        /// aimed shot's whole bonus is this + the aim turns + 1. The planner added the uncapped
        /// Accuracy until 2026-10-09, which over-promised aiming to every shooter less skilled than
        /// his weapon is accurate.
        /// </summary>
        internal static float AimAccuracy(BattleSoldier shooter, RangedWeaponTemplate template) =>
            Math.Min(
                template.Accuracy,
                shooter.Soldier.GetTotalSkillValue(template.RelatedSkill));

        /// <summary>
        /// The two sides of the aim-versus-shoot comparison, both in battle value per turn and net
        /// of the round charge, and the gross removal per turn of each side's chosen shot.
        /// </summary>
        /// <para>The net values decide how the soldier fires (aim or shoot, how long to aim). The
        /// readiness values are the GROSS removal per turn of the plan that decision picks, because
        /// readiness is compared in the engagement potential with the exchange forecast's standing
        /// rate, which is gross removal of the same plan (PairRemovalTerm.SustainedRateAt). Until
        /// 2026-10-09 readiness was the net value: the round charge took about half of it, so a
        /// standing turn was credited half what the forecast said it was worth, and every step
        /// toward the enemy looked cheap (Neophyte Hybrids crept toward Shoota Boyz without firing;
        /// Design/Reference/BattleLogic.md §5.3).</para>
        /// <param name="ShootNowShot">The shot to fire now, its burst chosen at the decision's
        /// common round price; fire this one, not the shot passed in, when <see cref="ShootNow"/>.
        /// </param>
        internal readonly record struct FireTiming(
            float ShootNowValue,
            float PreparedRate,
            float ShootNowGross = 0,
            float PreparedGrossRate = 0,
            RangedTargetEvaluation ShootNowShot = null)
        {
            internal bool ShootNow => ShootNowValue >= PreparedRate;

            /// <summary>
            /// Readiness (engagement-potential) value of a planned aim: the gross removal per turn
            /// of the prepared shot the planner prefers, never negative. Nothing left to aim for,
            /// or a shot that removes nothing, is worth nothing.
            /// </summary>
            internal float Readiness =>
                PreparedRate == float.MinValue ? 0f : Math.Max(0f, PreparedGrossRate);

            /// <summary>
            /// Readiness of an aim already held: the gross value per turn of whichever the planner
            /// would do with it, cash it now or continue it.
            /// </summary>
            internal float StoredReadiness => ShootNow
                ? Math.Max(0f, ShootNowGross)
                : Readiness;
        }

        /// <summary>
        /// A weapon whose supply never runs down -- unlimited, self-regenerating, or reloaded from
        /// no counted reserve -- is never scarce, and its rounds are free.
        /// </summary>
        internal static bool IsNeverScarce(RangedWeapon weapon) =>
            weapon == null
            || weapon.IsUnlimited
            || weapon.IsSelfRegenerating
            || (weapon.HasSoldierReload && weapon.Template.AmmunitionType == null);

        /// <summary>Rounds a soldier has for this weapon: loaded and in the pouches, or items.</summary>
        internal static float RoundsCarried(RangedWeapon weapon) =>
            weapon.IsConsumableItem
                ? weapon.ConsumableQuantity
                : weapon.LoadedAmmo + weapon.ReserveAmmo;

        /// <summary>
        /// How scarce a soldier's rounds are for the work in front of him, 0 (they will last) to 1
        /// (none left). The work is his share of the enemy battle value his weapon can hurt
        /// (<see cref="RangedShotEvaluator.WorkPerShooter"/>). At the pace of his fastest plan as
        /// fired with free rounds, <paramref name="fastValuePerRound"/> battle value a round, that
        /// work needs
        /// <c>N = work / value</c> rounds. With <c>R</c> rounds carried the scarcity is
        /// <c>1 - R / N</c>, clamped: 0 whenever the rounds outlast the work, so the soldier fires
        /// for the most removal per turn, and rising as the shortfall grows.
        ///
        /// <para>WHY THE WORK. Until 2026-10-11 scarcity was the rounds carried against a fixed six
        /// magazines, so a marine with standard issue (four) was always a third scarce: against
        /// twenty PDF he aimed four turns for every shot to save rounds he would never need, and
        /// his squad removed about 30% less a turn than one aim turn would have. A turn spent
        /// aiming is a turn under fire; only a fight longer than the pouches is worth saving
        /// rounds for (product owner).</para>
        /// </summary>
        internal static float Scarcity(float roundsCarried, float workPerShooter, float fastValuePerRound)
        {
            if (roundsCarried <= 0)
            {
                return 1f;
            }
            if (workPerShooter <= 0 || fastValuePerRound <= 0)
            {
                return 0f;
            }
            float roundsNeeded = workPerShooter / fastValuePerRound;
            return Math.Clamp(1f - (roundsCarried / roundsNeeded), 0f, 1f);
        }

        /// <summary>
        /// One plan in a firing decision: its best value per round over every burst length, the
        /// burst it fires when rounds are free (score and rounds), and the turns the plan takes.
        /// </summary>
        internal readonly record struct PlanEfficiency(
            float ValuePerRound, float FreeScore, int FreeRounds, int Turns);

        /// <summary>
        /// The price of a round in one firing decision: scarcity x the best value per round on
        /// offer (what the round could buy instead). Scarcity is set by the decision's FASTEST plan
        /// AS FIRED WITH FREE ROUNDS -- the most removal per turn, at the burst it would then fire
        /// -- because that is the pace, and the rate of spending, the soldier would keep if rounds
        /// did not matter. (Priced at its most round-efficient burst instead, a marine aiming once
        /// looked to need a third of the rounds his nine-round bursts would really spend.)
        /// </summary>
        internal float RoundPrice(
            BattleSoldier soldier,
            RangedWeapon weapon,
            IReadOnlyList<PlanEfficiency> plans,
            out float scarcity)
        {
            scarcity = 0f;
            if (soldier == null || IsNeverScarce(weapon) || plans == null || plans.Count == 0)
            {
                return 0f;
            }
            float bestValue = 0f;
            float fastestRate = float.MinValue;
            float fastValue = 0f;
            foreach (PlanEfficiency plan in plans)
            {
                bestValue = Math.Max(bestValue, plan.ValuePerRound);
                float rate = plan.FreeScore / Math.Max(1, plan.Turns);
                if (rate > fastestRate)
                {
                    fastestRate = rate;
                    fastValue = Math.Max(0f, plan.FreeScore) / Math.Max(1, plan.FreeRounds);
                }
            }
            scarcity = Scarcity(
                RoundsCarried(weapon),
                _shotEvaluator.WorkPerShooter(soldier, weapon),
                fastValue);
            return scarcity * bestValue;
        }

        /// <summary>
        /// The round price for a STANDING shooter at a fixed range: the plans are firing now with
        /// <paramref name="referenceModifier"/> and every aim-then-fire cycle. The removal-rate
        /// table prices its plans with this, so its standing rate uses the planner's own charge.
        /// </summary>
        internal float StandingRoundPrice(
            BattleSoldier soldier,
            BattleSoldier target,
            RangedWeapon weapon,
            float range,
            float referenceModifier,
            float? targetSpeed,
            out float scarcity)
        {
            List<PlanEfficiency> plans =
            [
                Efficiency(soldier, target, weapon, range, referenceModifier, targetSpeed, 1)
            ];
            for (int bonus = 0; bonus <= FullAimBonusTurns; bonus++)
            {
                plans.Add(Efficiency(
                    soldier, target, weapon, range,
                    AimAccuracy(soldier, weapon.Template) + bonus + 1f, targetSpeed, bonus + 2));
            }
            return RoundPrice(soldier, weapon, plans, out scarcity);
        }

        private PlanEfficiency Efficiency(
            BattleSoldier soldier,
            BattleSoldier target,
            RangedWeapon weapon,
            float range,
            float modifier,
            float? targetSpeed,
            int turns)
        {
            (float valuePerRound, float freeScore, int freeRounds) = _shotEvaluator.BurstProfile(
                soldier, target, weapon, range, modifier, targetSpeed);
            return new PlanEfficiency(valuePerRound, freeScore, freeRounds, turns);
        }

        private static int RoundsUsed(RangedTargetEvaluation shot) =>
            shot.Weapon.IsUnlimited || shot.Weapon.IsSelfRegenerating
                ? 0
                : shot.Weapon.IsConsumableItem
                    ? 1
                    : Math.Max(1, shot.ShotsToFire);

        private static float NetShotValue(RangedTargetEvaluation shot, float roundPrice) =>
            shot.Score - (roundPrice * RoundsUsed(shot));

        /// <summary>
        /// Expected change in range to <paramref name="target"/> per turn, from the speed it moved
        /// at last turn and the direction its squad was going. Fleeing roles and a step back move
        /// away; the advancing options move closer. "Closer" is toward the squad's own chosen
        /// counterpart rather than necessarily toward this shooter, which is the right sign for the
        /// usual case of a squad closing on the force shooting at it and an honest zero for
        /// anything else.
        /// </summary>
        internal static float ProjectedRangeChangePerTurn(BattleSoldier target)
        {
            BattleSquad squad = target?.BattleSquad;
            float speed = Math.Max(0, target?.CurrentSpeed ?? 0);
            if (squad == null || speed <= 0)
            {
                return 0;
            }
            if (squad.WithdrawalRole is WithdrawalRole.Bound or WithdrawalRole.Routing
                || squad.MoraleState == MoraleState.Routing)
            {
                return speed;
            }
            return squad.LastEngagementOptionKind switch
            {
                EngagementOptionKind.StepBack => speed,
                EngagementOptionKind.StepForward
                    or EngagementOptionKind.JogToward
                    or EngagementOptionKind.RunToward
                    or EngagementOptionKind.CloseToContact => -speed,
                _ => 0
            };
        }

        /// <summary>
        /// Evaluates the same full-aim, projected-range shot used by the pursuit fire window.
        /// Preparation actions may call this before the weapon has ammunition in its magazine:
        /// a successful reload can be the first step of a multi-round reload, so the question is
        /// whether the weapon can lead to a worthwhile follow-up, not whether it can fire this
        /// instant.
        /// </summary>
        internal bool IsWorthwhilePursuitFollowUpShot(
            BattleSoldier shooter,
            BattleSoldier target,
            RangedWeapon weapon,
            float quarrySpeed,
            bool allowPendingPreparation = false)
        {
            RangedTargetEvaluation evaluation = EvaluatePursuitFireWindowShot(
                shooter,
                target,
                weapon,
                quarrySpeed,
                allowPendingPreparation);
            return evaluation != null
                && evaluation.HitProbability > StickyMinimumHitProbability
                && evaluation.Score > 0;
        }

        /// <summary>
        /// Returns the fire-window shot for one exact shooter/target/weapon combination. Keeping
        /// this calculation here lets the planner's projected Hold value and the withdrawal
        /// evidence check share range projection, aim bonus, target viability, and worthwhile-shot
        /// inputs instead of growing two ranged-combat approximations.
        /// </summary>
        internal RangedTargetEvaluation EvaluatePursuitFireWindowShot(
            BattleSoldier shooter,
            BattleSoldier target,
            RangedWeapon weapon,
            float quarrySpeed,
            bool allowPendingPreparation = false)
            => EvaluatePursuitFireWindowShot(
                shooter,
                target,
                weapon,
                _grid.GetDistanceBetweenSoldiers(shooter.Soldier.Id, target.Soldier.Id),
                quarrySpeed,
                PursuitFireWindowTurns,
                allowPendingPreparation);

        internal RangedTargetEvaluation EvaluatePursuitFireWindowShot(
            BattleSoldier shooter,
            BattleSoldier target,
            RangedWeapon weapon,
            float startingRange,
            float quarrySpeed,
            int futureMovementTurns,
            bool allowPendingPreparation = false)
        {
            if (shooter == null
                || target == null
                || weapon == null
                || !shooter.IsCombatEffective
                || !target.IsCombatEffective
                || !IsPlaced(shooter)
                || !IsPlaced(target)
                || _grid.GetSoldierSide(shooter.Soldier.Id)
                    == _grid.GetSoldierSide(target.Soldier.Id)
                || weapon.Template.IsTemplateWeapon
                || !shooter.RangedWeapons.Contains(weapon))
            {
                return null;
            }

            if (allowPendingPreparation)
            {
                if (!weapon.CanFire && !weapon.CanReload && weapon.ReloadProgress == 0)
                {
                    return null;
                }
            }
            else if (weapon.LoadedAmmo <= 0)
            {
                return null;
            }

            float projectedRange = startingRange
                + Math.Max(0, quarrySpeed) * Math.Max(0, futureMovementTurns);
            if (projectedRange > weapon.Template.MaximumRange)
            {
                return null;
            }

            return _shotEvaluator.EvaluateRangedTarget(
                shooter,
                target,
                weapon,
                projectedRange,
                AimAccuracy(shooter, weapon.Template) + FullAimBonusTurns + 1,
                quarrySpeed);
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
                RangedTargetEvaluation evaluation = _shotEvaluator.EvaluateRangedTarget(
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

        // Phase 3 fire distribution. Returns the shooter squad's lane frame against one target
        // squad, computed once per pair and memoized. The frame is a pure function of the frozen
        // layout, so every member of the shooter squad shares it.
        private SquadLaneFrame GetSquadLaneFrame(BattleSquad shooterSquad, BattleSquad targetSquad)
        {
            if (shooterSquad == null || targetSquad == null)
            {
                return default;
            }
            (int, int) key = (shooterSquad.Id, targetSquad.Id);
            if (_context.SquadLaneFrames.TryGetValue(key, out SquadLaneFrame cached))
            {
                return cached;
            }
            SquadLaneFrame frame = ComputeSquadLaneFrame(shooterSquad, targetSquad);
            _context.SquadLaneFrames[key] = frame;
            return frame;
        }

        private SquadLaneFrame ComputeSquadLaneFrame(BattleSquad shooterSquad, BattleSquad targetSquad)
        {
            List<(float X, float Y)> shooters = LanePositions(shooterSquad, requireEffective: false);
            List<(float X, float Y)> targets = LanePositions(targetSquad, requireEffective: true);
            if (shooters.Count == 0 || targets.Count == 0)
            {
                return default;
            }

            (float shooterX, float shooterY) = Centroid(shooters);
            (float targetX, float targetY) = Centroid(targets);
            float axisX = targetX - shooterX;
            float axisY = targetY - shooterY;
            float axisLength = MathF.Sqrt((axisX * axisX) + (axisY * axisY));
            if (axisLength < 1e-4f)
            {
                // Squads occupy the same point (should not happen with living enemies); no axis.
                return default;
            }
            // Perpendicular to the engagement axis is the lateral ("along the frontage") direction.
            // Both lines are measured along the SAME perpendicular, so "left" maps to "left".
            float perpX = -axisY / axisLength;
            float perpY = axisX / axisLength;
            (float shooterMinimum, float shooterMaximum) =
                LateralExtent(shooters, shooterX, shooterY, perpX, perpY);
            (float targetMinimum, float targetMaximum) =
                LateralExtent(targets, targetX, targetY, perpX, perpY);

            float discipline = shooterSquad.Faction?.FireDiscipline ?? DefaultFireDiscipline;
            return new SquadLaneFrame(
                perpX,
                perpY,
                shooterX,
                shooterY,
                shooterMinimum,
                shooterMaximum,
                targetX,
                targetY,
                targetMinimum,
                targetMaximum,
                BaseLaneSpreadFraction * discipline);
        }

        private List<(float X, float Y)> LanePositions(BattleSquad squad, bool requireEffective)
        {
            List<(float X, float Y)> positions = [];
            foreach (BattleSoldier member in squad.AbleSoldiers)
            {
                if ((requireEffective && !member.IsCombatEffective)
                    || member.TopLeft is not ValueTuple<int, int> position
                    || !_grid.IsSoldierPlaced(member.Soldier.Id))
                {
                    continue;
                }
                positions.Add((position.Item1, position.Item2));
            }
            return positions;
        }

        private static (float X, float Y) Centroid(List<(float X, float Y)> positions)
        {
            double sumX = 0;
            double sumY = 0;
            foreach ((float x, float y) in positions)
            {
                sumX += x;
                sumY += y;
            }
            return ((float)(sumX / positions.Count), (float)(sumY / positions.Count));
        }

        private static (float Minimum, float Maximum) LateralExtent(
            List<(float X, float Y)> positions,
            float centroidX,
            float centroidY,
            float perpX,
            float perpY)
        {
            float minimum = float.MaxValue;
            float maximum = float.MinValue;
            foreach ((float x, float y) in positions)
            {
                float lateral = ((x - centroidX) * perpX) + ((y - centroidY) * perpY);
                minimum = Math.Min(minimum, lateral);
                maximum = Math.Max(maximum, lateral);
            }
            return (minimum, maximum);
        }

        // Where a position sits along its line, 0..1. A line with no lateral extent (one man, or
        // a file straight down the axis) is treated as a single lane at its middle.
        private static float LaneFraction(
            float x,
            float y,
            float centroidX,
            float centroidY,
            float minimum,
            float maximum,
            in SquadLaneFrame frame)
        {
            float width = maximum - minimum;
            if (width < 1e-3f)
            {
                return 0.5f;
            }
            float lateral = ((x - centroidX) * frame.PerpX) + ((y - centroidY) * frame.PerpY);
            return Math.Clamp((lateral - minimum) / width, 0f, 1f);
        }

        /// <summary>
        /// Fraction of a candidate's selection score given up for lane mismatch: 0 for the target
        /// in the shooter's own lane, up to the frame's spread strength for the opposite end of the
        /// target squad.
        /// </summary>
        private static float LaneSpreadPenaltyFraction(
            in SquadLaneFrame frame,
            BattleSoldier shooter,
            BattleSoldier target)
        {
            if (!frame.Valid
                || frame.SpreadStrength <= 0f
                || shooter.TopLeft is not ValueTuple<int, int> shooterPosition
                || target.TopLeft is not ValueTuple<int, int> targetPosition)
            {
                return 0f;
            }
            float shooterLane = LaneFraction(
                shooterPosition.Item1,
                shooterPosition.Item2,
                frame.ShooterCentroidX,
                frame.ShooterCentroidY,
                frame.ShooterMinimum,
                frame.ShooterMaximum,
                frame);
            float targetLane = LaneFraction(
                targetPosition.Item1,
                targetPosition.Item2,
                frame.TargetCentroidX,
                frame.TargetCentroidY,
                frame.TargetMinimum,
                frame.TargetMaximum,
                frame);
            return frame.SpreadStrength * MathF.Abs(shooterLane - targetLane);
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

            // Phase 3: bias selection toward the enemy in the shooter's own firing lane of the
            // target squad so the shooter squad spreads its fire. The penalty affects only which
            // target is picked, not the returned evaluation's value (that still competes at its
            // true score against template/blast options).
            RangedTargetEvaluation best = null;
            float bestEffectiveScore = float.MinValue;
            foreach (BattleSquad candidateSquad in GetNearestInRangeEnemySquads(
                soldier,
                movementDirection))
            {
                SquadLaneFrame laneFrame = GetSquadLaneFrame(soldier.BattleSquad, candidateSquad);
                foreach (BattleSoldier target in candidateSquad.AbleSoldiers
                    .Where(IsPlaced)
                    .OrderBy(candidate => candidate.Soldier.Id))
                {
                    float range = _grid.GetDistanceBetweenSoldiers(soldier.Soldier.Id, target.Soldier.Id);
                    float lanePenalty = LaneSpreadPenaltyFraction(laneFrame, soldier, target);
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
                            toHitModifier += AimAccuracy(soldier, weapon.Template)
                                + soldier.Aim.Value.Item3 + 1;
                        }

                        RangedTargetEvaluation evaluation = _shotEvaluator.EvaluateRangedTarget(
                            soldier,
                            target,
                            weapon,
                            range,
                            toHitModifier);
                        // Candidate squads, soldiers, and weapons are ordered nearest-first and
                        // deterministically, so an exact tie naturally stays on the closer option.
                        float weightedScore = evaluation.Score * TargetSelectionWeight(target);
                        float effectiveScore =
                            weightedScore - (MathF.Abs(weightedScore) * lanePenalty);
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
                        float removalFraction = RangedShotEvaluator.CalculateRangedRemovalFraction(
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
            float? targetSpeed = null,
            int? availableAmmo = null,
            float? roundPrice = null)
            => _shotEvaluator.EvaluateRangedTarget(
                soldier,
                target,
                weapon,
                range,
                additionalToHitModifier,
                targetSpeed,
                availableAmmo,
                roundPrice);

        internal float WorkPerShooter(BattleSoldier soldier, RangedWeapon weapon) =>
            _shotEvaluator.WorkPerShooter(soldier, weapon);

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
                // base accuracy bonus is the weapon's accuracy (capped by skill, as the engine
                // applies it) plus 1 for aiming making it an all-out attack
                bulkAndAccMod += useAccuracy
                    ? (AimAccuracy(soldier, weapon.Template) + 1) * aimMultiplier
                    : 0;
                RangedTargetEvaluation evaluation = _shotEvaluator.EvaluateRangedTarget(
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

        // Compatibility forwarders. RangedShotEvaluator owns these calculations; keeping the
        // narrow seams here avoids forcing the legacy removal-rate and test callers to understand
        // the new collaborator in the same packet.
        internal static float CalculateRangedRemovalFraction(
            BattleSoldier target,
            RangedWeapon weapon,
            float range,
            float armor)
            => RangedShotEvaluator.CalculateRangedRemovalFraction(
                target,
                weapon,
                range,
                armor);

        internal static (float TakeOut, float WoundProgress) CalculateRangedHitRemoval(
            BattleSoldier target,
            RangedWeapon weapon,
            float range,
            float armor)
            => RangedShotEvaluator.CalculateRangedHitRemoval(
                target,
                weapon,
                range,
                armor);
    }
}
