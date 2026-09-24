using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Battles.Models;
using OnlyWar.Domain.Equippables;

namespace OnlyWar.Battles
{
    /// <summary>
    /// The per-turn exchange model behind posture choice: what a squad and its enemies would remove
    /// from each other now, plus the current-turn contact terms.
    ///
    /// <para>Three questions live here. <b>Now</b> -- what is already landing on us this turn
    /// (<see cref="EvaluateIncomingNow"/>) and what a charge would trade (<see
    /// cref="EvaluateContactTerms"/>). Future position value belongs to the state-only
    /// <see cref="EngagementPotential"/> collaborator, so this class does not own option-specific
    /// arrival or continuation shaping.</para>
    ///
    /// <para>Everything is denominated in the same currency -- expected battle value removed per
    /// turn -- which is the entire point of Phase 5. Ranged rates come from
    /// <see cref="PairRemovalRateTable"/> and melee rates come from
    /// <see cref="MeleeStrikeEstimator"/>. Scoring only: services in, no <see cref="ActionSink"/>,
    /// so no path through here can emit an action.</para>
    /// </summary>
    internal sealed class EngagementExchangeModel
    {
        // Originally the depth of the bounded policy rollout, which has since been removed in
        // favour of EngagementPotential. The value survives as the retargeting horizon
        // (BattleEscapeRules.RetargetingHorizonTurns, via BattleSquadPlanner).
        internal const int EngagementLookaheadHorizon = 2;
        internal const float EngagementFutureDiscount = 0.65f;
        private const float WalkBulkMultiplier = SoldierMovementPlanner.WalkBulkMultiplier;
        private const float FullBulkMultiplier = SoldierMovementPlanner.FullBulkMultiplier;

        private readonly SquadPlanningServices _services;
        private readonly RangedTargetSelector _ranged;
        private readonly MeleeStrikeEstimator _melee;
        private readonly PairRemovalRateTable _removalRates;
        private readonly BattleGridManager _grid;
        private readonly BattlePlanningContext _context;

        internal EngagementExchangeModel(
            SquadPlanningServices services,
            RangedTargetSelector ranged,
            MeleeStrikeEstimator melee,
            PairRemovalRateTable removalRates)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _ranged = ranged ?? throw new ArgumentNullException(nameof(ranged));
            _melee = melee ?? throw new ArgumentNullException(nameof(melee));
            _removalRates = removalRates ?? throw new ArgumentNullException(nameof(removalRates));
            _grid = _services.Grid;
            _context = _services.Context;
        }

        private bool IsPlaced(BattleSoldier soldier) => _services.IsPlaced(soldier);

        private static float GetBattleValue(BattleSoldier soldier) =>
            SquadPlanningServices.BattleValueOf(soldier);

        /// <summary>Plane distance between two centroids. Shared by every projection here and by
        /// the option enumeration above it. Deliberately double-precision sqrt then cast, as the
        /// original was: these values feed seeded posture decisions at thresholds.</summary>
        internal static float Distance(
            ValueTuple<float, float> first,
            ValueTuple<float, float> second)
        {
            float dx = first.Item1 - second.Item1;
            float dy = first.Item2 - second.Item2;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        internal static float PostureBulkMultiplier(EngagementOptionKind posture)
        {
            return posture switch
            {
                EngagementOptionKind.StepBack or EngagementOptionKind.StepForward =>
                    WalkBulkMultiplier,
                EngagementOptionKind.JogToward => FullBulkMultiplier,
                EngagementOptionKind.CloseToContact or EngagementOptionKind.RunToward =>
                    float.PositiveInfinity,
                _ => 0f
            };
        }

        internal float EvaluateIncomingNow(
            BattleSquad squad,
            float feasibleSpeed,
            IReadOnlyDictionary<int, BattleSquadCapabilityProfile> profiles,
            IReadOnlyDictionary<int, SquadEngagementFrame> frames,
            IReadOnlyCollection<BattleSquad> enemies)
        {
            float incoming = 0;
            foreach (BattleSquad enemy in enemies.OrderBy(candidate => candidate.Id))
            {
                if (!profiles.ContainsKey(enemy.Id)
                    || !frames.TryGetValue(enemy.Id, out SquadEngagementFrame enemyFrame))
                {
                    continue;
                }
                float allocation = enemyFrame.PairWeights.GetValueOrDefault(squad.Id);
                float attackerBulk = PostureBulkMultiplier(enemyFrame.BaselinePosture);
                if (!float.IsPositiveInfinity(attackerBulk))
                {
                    incoming += allocation * EstimateIncomingResponse(
                        enemy, squad, feasibleSpeed, attackerBulk);
                }
            }
            return incoming;
        }

        private float EstimateIncomingResponse(
            BattleSquad attackerSquad,
            BattleSquad targetSquad,
            float targetSpeed,
            float attackerBulk)
        {
            var cacheKey = (
                attackerSquad.Id,
                targetSquad.Id,
                BitConverter.SingleToInt32Bits(targetSpeed),
                BitConverter.SingleToInt32Bits(attackerBulk));
            if (_context.IncomingResponses.TryGetValue(cacheKey, out float cached))
            {
                return cached;
            }

            float response = 0;
            foreach (BattleSoldier shooter in attackerSquad.AbleSoldiers
                .Where(IsPlaced)
                .OrderBy(member => member.Soldier.Id))
            {
                RangedTargetEvaluation best = null;
                foreach (BattleSoldier target in targetSquad.AbleSoldiers
                    .Where(IsPlaced)
                    .OrderBy(candidate => _grid.GetDistanceBetweenSoldiers(
                        shooter.Soldier.Id, candidate.Soldier.Id))
                    .ThenBy(candidate => candidate.Soldier.Id)
                    .Take(3))
                {
                    float range = _grid.GetDistanceBetweenSoldiers(
                        shooter.Soldier.Id, target.Soldier.Id);
                    foreach (RangedWeapon weapon in shooter.EquippedRangedWeapons
                        .Where(candidate => candidate.LoadedAmmo > 0
                            && !candidate.Template.IsTemplateWeapon
                            && range <= candidate.Template.MaximumRange)
                        .OrderBy(candidate => candidate.Template.Id))
                    {
                        RangedTargetEvaluation evaluation = _ranged.EvaluateRangedTarget(
                            shooter,
                            target,
                            weapon,
                            range,
                            -weapon.Template.Bulk * attackerBulk,
                            targetSpeed);
                        if (best == null || evaluation.Score > best.Score)
                        {
                            best = evaluation;
                        }
                    }
                }
                if (best != null && best.Score > 0)
                {
                    response += best.ExpectedEnemyBattleValueRemoved;
                }
            }
            response = Math.Min(
                response,
                targetSquad.AbleSoldiers.Where(IsPlaced).Sum(GetBattleValue));
            _context.IncomingResponses[cacheKey] = response;
            return response;
        }

        internal (float MeleeValue, float Commitment) EvaluateContactTerms(
            BattleSquad squad,
            EngagementOptionKind kind,
            BattleSquad primary,
            BattleSquadCapabilityProfile profile)
        {
            if (kind != EngagementOptionKind.CloseToContact || primary == null)
            {
                return (0, 0);
            }
            float distance = BattleEngagementFrameBuilder.MinimumDistance(squad, primary);
            float melee = 0;
            float closing = 0;
            int reaches = 0;
            foreach (BattleSoldier soldier in squad.AbleSoldiers.OrderBy(member => member.Soldier.Id))
            {
                MeleeStrikeEstimator.ChargeAssessment estimate =
                    _melee.EstimateChargeNet(soldier, primary, distance);
                closing += estimate.ClosingCost;
                // EstimateChargeNet has already present-valued this: a soldier who is not in
                // contact at turn start cannot strike this turn, so his payoff is discounted by the
                // full turns-to-contact. The current turn's incoming fire is represented by
                // EvaluateIncomingNow; this commitment is only the remaining exposure while contact
                // is pending.
                melee += estimate.MeleeBattleValue;
                if (estimate.ReachesContactThisTurn)
                {
                    reaches++;
                }
            }
            float seatFraction = Math.Min(1f,
                profile.ContactCapacity / (float)Math.Max(1, squad.AbleSoldiers.Count));
            float currentContactFraction = reaches > 0
                ? Math.Min(seatFraction,
                    reaches / (float)Math.Max(1, squad.AbleSoldiers.Count))
                : seatFraction;
            melee *= currentContactFraction;
            float lockCost = reaches > 0
                ? Math.Max(0, profile.UsableRangedBattleValue - profile.UsableMeleeBattleValue)
                    * 0.12f
                : 0;
            return (
                Math.Min(melee, primary.AbleSoldiers.Sum(GetBattleValue)),
                Math.Min(closing, profile.TotalAbleBattleValue) + lockCost);
        }

        /// <summary>
        /// Returns the expected battle value removed by a stationary contact exchange. This is
        /// exposed to the state potential so an enemy's declared withdrawal role can affect the
        /// projected future without smuggling an engagement option into the value calculation.
        /// </summary>
        internal float EvaluateContactRemovalRate(
            BattleSquad attacker,
            BattleSquad target) =>
            MeleeRemovalRate(attacker, target, 1f);

        /// <summary>
        /// How fast the quarry is opening the range, when this squad is the one chasing.
        /// </summary>
        /// <remarks>
        /// QuarryRunSpeed is only populated for a Pursuit frame; on an ordinary approach the primary
        /// is not fleeing, so there is no withdrawal rate to subtract.
        /// </remarks>
        internal static float QuarryWithdrawalRate(
            SquadEngagementFrame frame,
            EngagementSquadRole? quarryRole) =>
            (frame.Role is EngagementSquadRole.Pursuit
                or EngagementSquadRole.Follow
                or EngagementSquadRole.Press)
                && (quarryRole is EngagementSquadRole.Bound or EngagementSquadRole.Routing)
                    ? Math.Max(0, frame.QuarryRunSpeed)
                    : 0;

        /// <summary>
        /// PHASE 5c (Design/Reference/BattleLogic.md). The outgoing half of the per-turn
        /// battle-value exchange between <paramref name="squad"/> and <paramref name="enemy"/> at a
        /// projected centroid separation; <see cref="EvaluateIncomingExchangeRate"/> is the other
        /// half. <see cref="EngagementPotential"/> reads both directions at the current and projected
        /// separations, which keeps immediate fire and future position value commensurable: both
        /// are <c>hit * (takeOut + lambda * woundProgress) * targetBV</c>, summed per-soldier.
        /// (Phase 5c originally built this for one ply of the bounded policy rollout, which has
        /// since been removed; the two directions used to be netted in a signed
        /// <c>EvaluateExchangeRate</c> that went with it.)
        ///
        /// <para>The predecessor, <c>AggregateRemovalRate</c>, was a CAPABILITY PROXY: a flat 10%
        /// of the ATTACKER'S OWN <c>UsableRangedBattleValue</c> per turn, with the defender read
        /// only as a cap and no hit, penetration, armour or constitution input anywhere. In the
        /// reference trace it asserted 8.198 BV/turn for a squad whose honest immediate-fire value
        /// was 0.001 -- the two halves of one score disagreeing about the same squad's shooting by
        /// a factor of ~8,000.</para>
        ///
        /// <para>PAIR WEIGHTS vs ARGMAX -- the question Phase 4 deliberately left open, resolved
        /// here ASYMMETRICALLY, because the two halves are asking different questions.</para>
        ///
        /// <para>OUTGOING uses the argmax table and NO <c>PairWeights</c>. The table is already
        /// target-selected: each of our soldiers contributes its single best target's removal to
        /// exactly one enemy squad's cell, so summing the cells over enemies reconstructs this
        /// squad's true whole-squad removal per turn -- the same quantity, computed the same way,
        /// as `outgoing`. <c>PairWeights</c> is a normalized allocation (it sums to 1 across enemy
        /// squads); multiplying an already-allocated rate by it would divide the squad's fire
        /// twice and systematically understate every shooting option. The potential does not go
        /// blind to a flank threat by this: the threat still appears in the INCOMING half below,
        /// which is where a distant enemy squad actually costs us something.</para>
        ///
        /// <para>INCOMING keeps <c>PairWeights</c>, because there it genuinely is an allocation:
        /// the question is what share of that enemy squad's fire lands on US rather than on our
        /// neighbours, and its argmax cell cannot answer that -- it is a single frozen choice made
        /// against this turn's geometry, so reading it directly would swing our projected incoming
        /// between "all of it" and "none of it" as the enemy's best target flickered between our
        /// squads. So: the enemy's WHOLE-squad rate at our projected separation, times our share.
        /// This mirrors the pre-Phase-5 structure exactly; only the rate itself became honest.</para>
        ///
        /// <para>MELEE is evaluated by <see cref="MeleeStrikeEstimator"/> against the actual target
        /// soldiers, while the ranged half reads the removal-rate table. The outgoing melee half
        /// keeps its <c>PairWeights</c> allocation -- a squad can only be in contact with so many
        /// enemies at once -- and the two halves are combined with <c>max</c>, as before.</para>
        /// </summary>
        internal float EvaluateOutgoingExchangeRate(
            BattleSquad squad,
            BattleSquad enemy,
            BattleSquadCapabilityProfile profile,
            BattleSquadCapabilityProfile opposing,
            IReadOnlyDictionary<int, SquadEngagementFrame> frames,
            float range)
        {
            float outgoingAllocation = frames.TryGetValue(
                squad.Id, out SquadEngagementFrame ourFrame)
                    ? ourFrame.PairWeights.GetValueOrDefault(enemy.Id)
                    : 0f;
            return Math.Min(
                opposing.TotalAbleBattleValue,
                Math.Max(
                    PairRangedRemovalRate(squad, enemy.Id, range),
                    outgoingAllocation * MeleeRemovalRate(squad, enemy, range)));
        }

        /// <summary>
        /// <see cref="EvaluateOutgoingExchangeRate"/> for a squad that stands and may aim. For the
        /// access potential only -- see <see cref="SquadPairRemovalRate.SustainedRateAtRange"/>
        /// for why the exchange forecast does not read it.
        /// </summary>
        internal float EvaluateSustainedOutgoingRate(
            BattleSquad squad,
            BattleSquad enemy,
            BattleSquadCapabilityProfile opposing,
            IReadOnlyDictionary<int, SquadEngagementFrame> frames,
            float range)
        {
            float outgoingAllocation = frames.TryGetValue(
                squad.Id, out SquadEngagementFrame ourFrame)
                    ? ourFrame.PairWeights.GetValueOrDefault(enemy.Id)
                    : 0f;
            float ranged = _removalRates.GetPairRemovalRates(squad)
                .TryGetValue(enemy.Id, out SquadPairRemovalRate rate)
                    ? rate.SustainedRateAtRange(range)
                    : 0f;
            return Math.Min(
                opposing.TotalAbleBattleValue,
                Math.Max(ranged, outgoingAllocation * MeleeRemovalRate(squad, enemy, range)));
        }

        /// <summary>
        /// The positive rate at which <paramref name="enemy"/> removes battle value from
        /// <paramref name="squad"/>. Keeping this direction separate lets finite-pool consumers
        /// conserve the friendly and enemy pools independently instead of saturating a signed,
        /// already-netted rate.
        /// </summary>
        internal float EvaluateIncomingExchangeRate(
            BattleSquad squad,
            BattleSquad enemy,
            BattleSquadCapabilityProfile profile,
            IReadOnlyDictionary<int, SquadEngagementFrame> frames,
            float range,
            float targetSpeed)
        {
            float incomingAllocation = frames.TryGetValue(
                enemy.Id, out SquadEngagementFrame theirFrame)
                    ? theirFrame.PairWeights.GetValueOrDefault(squad.Id)
                    : 0f;
            float incomingBulk = PostureBulkMultiplier(
                frames.GetValueOrDefault(enemy.Id)?.BaselinePosture
                    ?? EngagementOptionKind.Hold);
            return float.IsPositiveInfinity(incomingBulk)
                ? 0
                : incomingAllocation * Math.Min(
                    profile.TotalAbleBattleValue,
                    Math.Max(
                        TotalRangedRemovalRate(
                            enemy,
                            range,
                            targetSpeed,
                            incomingBulk),
                        MeleeRemovalRate(enemy, squad, range)));
        }

        /// <summary>
        /// The melee counterpart to <see cref="PairRangedRemovalRate"/>. It is deliberately
        /// calculated from the actual attacker/target soldiers rather than a capability fraction:
        /// the estimator plans contact strikes and credits their take-out/removal probabilities
        /// in the same battle-value currency as the ranged removal table.
        /// </summary>
        private float MeleeRemovalRate(
            BattleSquad attacker,
            BattleSquad target,
            float range)
        {
            if (range > 1.5f || attacker == null || target == null)
            {
                return 0f;
            }

            return _context.MeleeContactRemovalRates.GetOrAdd(
                (attacker.Id, target.Id),
                _ => _melee.EstimateContactRemovalRate(attacker, target));
        }

        /// <summary>
        /// This squad's per-turn removal against ONE enemy squad at a projected separation, from
        /// the Phase 4 table. An absent cell is a genuine 0: no soldier's best target is in that
        /// squad, so the squad is not shooting at it.
        /// </summary>
        private float PairRangedRemovalRate(
            BattleSquad shooterSquad,
            int targetSquadId,
            float range)
        {
            return _removalRates.GetPairRemovalRates(shooterSquad)
                .TryGetValue(targetSquadId, out SquadPairRemovalRate rate)
                    ? rate.RateAtRange(range)
                    : 0f;
        }

        /// <summary>
        /// This squad's whole-squad per-turn removal at a projected separation -- every cell of its
        /// table row summed. Used for the INCOMING half, where the consumer then takes its own
        /// <c>PairWeights</c> share of the total.
        /// </summary>
        private float TotalRangedRemovalRate(
            BattleSquad shooterSquad,
            float range,
            float targetSpeed,
            float shooterBulkMultiplier)
        {
            float total = 0f;
            foreach (SquadPairRemovalRate rate in
                _removalRates.GetPairRemovalRates(shooterSquad).Values)
            {
                total += rate.RateAtRange(range, targetSpeed, shooterBulkMultiplier);
            }
            return total;
        }
    }
}
