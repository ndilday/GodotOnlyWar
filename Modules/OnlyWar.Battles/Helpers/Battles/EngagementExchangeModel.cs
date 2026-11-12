using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Battles.Models;
using OnlyWar.Domain.Equippables;

namespace OnlyWar.Battles
{
    /// <summary>
    /// The per-turn exchange model behind posture choice: what a squad and its enemies would remove
    /// from each other now, plus the melee a squad already in contact lands this turn.
    ///
    /// <para>Three questions live here. <b>Now</b> -- what is already landing on us this turn
    /// (<see cref="EvaluateIncomingNow"/>) and what a squad already in contact strikes (<see
    /// cref="EvaluateContactMeleeNow"/>). Future position value belongs to the state-only
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

        /// <summary>
        /// The melee a CloseToContact option lands THIS turn: non-zero only when the squad is
        /// already in contact with its primary at turn start, since a squad that arrives this turn
        /// strikes next turn (TDD §6.6).
        ///
        /// <para>Nothing else about a charge is priced here. Until 2026-10-19 this also returned a
        /// run-in cost and a gun-lock cost, and credited a charge from range with its melee
        /// divided by (1 + turns to contact). All three are what the potential's Close
        /// continuation already prices: the crossing under fire, the guns given up in contact
        /// (contact is fought at melee rates), and the melee after arrival. The run-in cost also
        /// summed, for every charger, the fire of every enemy shooter as if each shot at him alone,
        /// so it did not share the enemy's fire across our soldiers and squads. 60 Kommandos at 200
        /// yards against 60 PDF priced every charge about 20 battle value below holding and stood in
        /// a pistol-against-autogun firefight they lost.</para>
        /// </summary>
        internal float EvaluateContactMeleeNow(
            BattleSquad squad,
            EngagementOptionKind kind,
            BattleSquad primary,
            BattleSquadCapabilityProfile profile)
        {
            if (kind != EngagementOptionKind.CloseToContact || primary == null)
            {
                return 0;
            }
            // In contact means within the 1-cell contact allowance, the distance at which the old
            // turns-to-contact count was zero.
            if (BattleEngagementFrameBuilder.MinimumDistance(squad, primary) > 1f)
            {
                return 0;
            }
            float melee = 0;
            foreach (BattleSoldier soldier in squad.AbleSoldiers.OrderBy(member => member.Soldier.Id))
            {
                melee += _melee.EstimateMeleeInContact(soldier, primary);
            }
            float seatFraction = Math.Min(1f,
                profile.ContactCapacity / (float)Math.Max(1, squad.AbleSoldiers.Count));
            return Math.Min(melee * seatFraction, primary.AbleSoldiers.Sum(GetBattleValue));
        }

        /// <summary>
        /// Returns the expected battle value removed by a stationary contact exchange. This is
        /// exposed to the state potential so an enemy's declared withdrawal role can affect the
        /// projected future without smuggling an engagement option into the value calculation.
        /// </summary>
        internal float EvaluateContactRemovalRate(
            BattleSquad attacker,
            BattleSquad target,
            float attackerShare = 1f,
            float frontageShare = 1f,
            float defenderShare = 1f) =>
            attacker == null || target == null
                ? 0f
                : ContactStrikes(attacker, target).RemovalRate(attackerShare, frontageShare, defenderShare);

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
        /// <see cref="EvaluateOutgoingExchangeRate"/> for a squad that stands and may aim: the rate
        /// of the standing phases of <see cref="EngagementExchangeForecast"/>. See
        /// <see cref="SquadPairRemovalRate.SustainedRateAtRange"/>.
        /// </summary>
        /// <summary>The removal-rate table's cells for one shooter squad (diagnostics read the terms).</summary>
        internal IReadOnlyDictionary<int, SquadPairRemovalRate> PairRemovalRates(BattleSquad squad) =>
            _removalRates.GetPairRemovalRates(squad);

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
        /// <see cref="EvaluateSustainedOutgoingRate"/> with the rounds that fire spends per turn, for
        /// the forecast's ammunition budget. No rounds when melee is the larger part.
        /// </summary>
        internal (float Removal, float Rounds) EvaluateSustainedOutgoing(
            BattleSquad squad,
            BattleSquad enemy,
            BattleSquadCapabilityProfile opposing,
            IReadOnlyDictionary<int, SquadEngagementFrame> frames,
            float range)
        {
            float removal = EvaluateSustainedOutgoingRate(squad, enemy, opposing, frames, range);
            if (!_removalRates.GetPairRemovalRates(squad).TryGetValue(enemy.Id, out SquadPairRemovalRate rate))
            {
                return (removal, 0f);
            }
            bool rangedLeads = rate.SustainedRateAtRange(range) >= removal - 0.0001f;
            return (removal, rangedLeads ? rate.SustainedRoundsAtRange(range) : 0f);
        }

        /// <summary>
        /// This squad's fire on <paramref name="enemy"/> while it moves (2026-11-08, fire on the
        /// approach): every soldier fires un-aimed each turn with the bulk penalty of its movement
        /// tier (<paramref name="shooterBulkMultiplier"/>, <see cref="PostureBulkMultiplier"/>),
        /// against a target that stands. With the rounds it spends per turn.
        /// </summary>
        internal (float Removal, float Rounds) EvaluateMovingOutgoing(
            BattleSquad squad,
            BattleSquad enemy,
            BattleSquadCapabilityProfile opposing,
            float range,
            float shooterBulkMultiplier)
        {
            if (float.IsPositiveInfinity(shooterBulkMultiplier)
                || !_removalRates.GetPairRemovalRates(squad).TryGetValue(enemy.Id, out SquadPairRemovalRate rate))
            {
                return (0f, 0f);
            }
            return (
                Math.Min(opposing.TotalAbleBattleValue, rate.RateAtRange(range, 0f, shooterBulkMultiplier)),
                rate.RoundsPerTurnAtRange(range));
        }

        /// <summary>
        /// <see cref="EvaluateIncomingExchangeRate"/> with the rounds the enemy spends per turn on
        /// this squad (its whole-squad rounds times the same allocation).
        /// </summary>
        internal (float Removal, float Rounds) EvaluateIncoming(
            BattleSquad squad,
            BattleSquad enemy,
            BattleSquadCapabilityProfile profile,
            IReadOnlyDictionary<int, SquadEngagementFrame> frames,
            float range,
            float targetSpeed)
        {
            float removal = EvaluateIncomingExchangeRate(squad, enemy, profile, frames, range, targetSpeed);
            float allocation = frames.TryGetValue(enemy.Id, out SquadEngagementFrame theirFrame)
                ? theirFrame.PairWeights.GetValueOrDefault(squad.Id)
                : 0f;
            if (removal <= 0f || allocation <= 0f || MeleeRemovalRate(enemy, squad, range) > 0f)
            {
                return (removal, 0f);
            }
            float rounds = 0f;
            foreach (SquadPairRemovalRate rate in _removalRates.GetPairRemovalRates(enemy).Values)
            {
                rounds += rate.RoundsPerTurnAtRange(range);
            }
            return (removal, allocation * rounds);
        }

        /// <summary>
        /// <see cref="EvaluateSustainedIncomingRate"/> with the rounds the enemy spends per turn on
        /// this squad.
        /// </summary>
        internal (float Removal, float Rounds) EvaluateSustainedIncoming(
            BattleSquad squad,
            BattleSquad enemy,
            BattleSquadCapabilityProfile profile,
            IReadOnlyDictionary<int, SquadEngagementFrame> frames,
            float range)
        {
            float incomingBulk = PostureBulkMultiplier(
                ForecastFiringPosture(frames.GetValueOrDefault(enemy.Id)));
            if (incomingBulk != 0f)
            {
                return EvaluateIncoming(squad, enemy, profile, frames, range, 0f);
            }
            float removal = EvaluateSustainedIncomingRate(squad, enemy, profile, frames, range);
            float allocation = frames.TryGetValue(enemy.Id, out SquadEngagementFrame theirFrame)
                ? theirFrame.PairWeights.GetValueOrDefault(squad.Id)
                : 0f;
            if (removal <= 0f || allocation <= 0f || MeleeRemovalRate(enemy, squad, range) > 0f)
            {
                return (removal, 0f);
            }
            float rounds = 0f;
            foreach (SquadPairRemovalRate rate in _removalRates.GetPairRemovalRates(enemy).Values)
            {
                rounds += rate.SustainedRoundsAtRange(range);
            }
            return (removal, allocation * rounds);
        }

        /// <summary>
        /// The rounds a squad's able soldiers carry for the weapons whose supply runs down
        /// (<see cref="RangedTargetSelector.RoundsCarried"/>), pooled for the squad as the opening
        /// forecast pools them.
        /// </summary>
        internal static float RoundsCarried(BattleSquad squad)
        {
            float rounds = 0f;
            foreach (BattleSoldier soldier in squad?.AbleSoldiers ?? [])
            {
                foreach (RangedWeapon weapon in soldier.EquippedRangedWeapons)
                {
                    if (!RangedTargetSelector.IsNeverScarce(weapon))
                    {
                        rounds += RangedTargetSelector.RoundsCarried(weapon);
                    }
                }
            }
            return rounds;
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
                ForecastFiringPosture(frames.GetValueOrDefault(enemy.Id)));
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
        /// The posture the exchange forecast assumes an enemy shoots from: standing, so its fire
        /// is the aimed sustained rate, unless it is charging to contact (a contact seeker's
        /// baseline) or running away (a fleeing role), when its own baseline still decides.
        ///
        /// <para>WHY NOT THE BASELINE. The baseline posture is the enemy's fallback option, a
        /// containment rule ("inside my preferred band's lower edge, step back"), not a prediction
        /// of how it will fight. Read as one, it said that two shooting squads at 260 yards, each
        /// inside the other's wide band, would both keep stepping back, so each priced the other's
        /// fire un-aimed and on the move -- 20 to 30 times below the standing rate. Both forecast a
        /// win; the Shoota Boyz believed a firefight they lost at four to one cost them almost
        /// nothing (2026-10-09, Design/Reference/BattleLogic.md §5.2). The squads stood and
        /// aimed.</para>
        /// </summary>
        internal static EngagementOptionKind ForecastFiringPosture(SquadEngagementFrame enemyFrame)
        {
            if (enemyFrame == null)
            {
                return EngagementOptionKind.Hold;
            }
            return enemyFrame.Role is EngagementSquadRole.Bound
                    or EngagementSquadRole.Routing
                    or EngagementSquadRole.BreakOff
                || enemyFrame.BaselinePosture == EngagementOptionKind.CloseToContact
                    ? enemyFrame.BaselinePosture
                    : EngagementOptionKind.Hold;
        }

        /// <summary>
        /// <see cref="EvaluateIncomingExchangeRate"/> against a stationary squad, with the enemy
        /// aiming when it stands (<see cref="ForecastFiringPosture"/>): the incoming half of the
        /// forecast's standing phases, so both sides of a standing exchange may aim. An enemy
        /// charging to contact or running away keeps its own posture's rate.
        /// </summary>
        internal float EvaluateSustainedIncomingRate(
            BattleSquad squad,
            BattleSquad enemy,
            BattleSquadCapabilityProfile profile,
            IReadOnlyDictionary<int, SquadEngagementFrame> frames,
            float range)
        {
            float incomingBulk = PostureBulkMultiplier(
                ForecastFiringPosture(frames.GetValueOrDefault(enemy.Id)));
            if (incomingBulk != 0f)
            {
                return EvaluateIncomingExchangeRate(squad, enemy, profile, frames, range, 0f);
            }

            float incomingAllocation = frames.TryGetValue(
                enemy.Id, out SquadEngagementFrame theirFrame)
                    ? theirFrame.PairWeights.GetValueOrDefault(squad.Id)
                    : 0f;
            float ranged = 0f;
            foreach (SquadPairRemovalRate rate in
                _removalRates.GetPairRemovalRates(enemy).Values)
            {
                ranged += rate.SustainedRateAtRange(range);
            }
            return incomingAllocation * Math.Min(
                profile.TotalAbleBattleValue,
                Math.Max(ranged, MeleeRemovalRate(enemy, squad, range)));
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

            return ContactStrikes(attacker, target).RemovalRate();
        }

        /// <summary>
        /// One squad's melee against another before the frontage is shared out (see
        /// <see cref="MeleeStrikeEstimator.ContactStrikes"/>), memoized for the turn.
        /// </summary>
        internal MeleeStrikeEstimator.ContactStrikes ContactStrikes(
            BattleSquad attacker,
            BattleSquad target) =>
            _context.MeleeContactStrikes.GetOrAdd(
                (attacker.Id, target.Id),
                _ => _melee.EstimateContactStrikes(attacker, target));

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
