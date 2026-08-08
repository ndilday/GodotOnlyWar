using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// The per-turn exchange model behind posture choice: what a squad and its enemies would remove
    /// from each other, now and at every projected range the lookahead reaches.
    ///
    /// <para>Three questions live here. <b>Now</b> -- what is already landing on us this turn
    /// (<see cref="EvaluateIncomingNow"/>) and what a charge would trade (<see
    /// cref="EvaluateContactTerms"/>). <b>Sooner</b> -- what reaching a useful range earlier is
    /// worth (<see cref="EvaluateArrivalTimeValue"/>). <b>Later</b> -- a bounded policy rollout in
    /// which each future state chooses again (<see cref="EvaluateBestContinuation"/>).</para>
    ///
    /// <para>Everything is denominated in the same currency -- expected battle value removed per
    /// turn -- which is the entire point of Phase 5. Ranged rates come from
    /// <see cref="PairRemovalRateTable"/>; melee keeps a capability proxy because that table is
    /// ranged-only. Scoring only: services in, no <see cref="ActionSink"/>, so no path through here
    /// can emit an action.</para>
    /// </summary>
    internal sealed class EngagementExchangeModel
    {
        // Plies of policy rollout. Each ply re-chooses, so this is a depth, not a fixed script.
        internal const int EngagementLookaheadHorizon = 2;
        private const float EngagementFutureDiscount = 0.65f;
        // How many turns of battle a squad expects to still be fighting. Scales the lookahead
        // terminal so a short rollout does not make a battle that opens at 400 yards read as though
        // it ends before a charge could pay off.
        private const float ExpectedRemainingTurns = 20f;
        private const float WalkBulkMultiplier = 0.5f;
        private const float FullBulkMultiplier = 1f;

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

        internal (float MeleeNow, float Commitment) EvaluateContactTerms(
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
                // EstimateChargeNet already discounts the melee payoff by arrival time. It is
                // still a valid future commitment when contact takes several turns; this flag is
                // only about seats and weapon-lock cost that apply on the current turn.
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

        internal List<float> EvaluateFutureExchange(
            BattleSquad squad,
            ValueTuple<float, float> projectedCentroid,
            EngagementOptionKind kind,
            BattleSquadCapabilityProfile profile,
            IReadOnlyDictionary<int, BattleSquadCapabilityProfile> profiles,
            IReadOnlyDictionary<int, SquadEngagementFrame> frames,
            IReadOnlyCollection<BattleSquad> enemies)
        {
            Dictionary<int, float> ranges = enemies.ToDictionary(
                enemy => enemy.Id,
                enemy => Distance(projectedCentroid, BattleEngagementFrameBuilder.Centroid(enemy)));
            float continuation = EvaluateBestContinuation(
                squad,
                profile,
                profiles,
                frames,
                enemies,
                ranges,
                EngagementLookaheadHorizon);
            return [continuation];
        }

        /// <summary>
        /// Values the root option's change in time-to-useful-exchange using the same present-value
        /// currency as the lookahead terminal. A short rollout can make Walk, Jog and Run look
        /// nearly identical when the useful range is many turns away; this term exposes the root
        /// transition directly without assigning movement a unit-specific bonus.
        ///
        /// The value is positive when the candidate reaches a useful exchange sooner and negative
        /// when the exchange at that range is unfavorable. The latter is intentional: movement
        /// should not be rewarded merely because it is movement. A ranged squad uses its derived
        /// effective band; a contact-seeking squad uses the contact boundary.
        /// </summary>
        internal float EvaluateArrivalTimeValue(
            BattleSquad squad,
            ValueTuple<float, float> projectedCentroid,
            BattleSquadCapabilityProfile profile,
            IReadOnlyDictionary<int, BattleSquadCapabilityProfile> profiles,
            IReadOnlyDictionary<int, SquadEngagementFrame> frames,
            IReadOnlyCollection<BattleSquad> enemies,
            SquadEngagementFrame frame)
        {
            // A squad already inside its ordinary ranged band should not be pulled toward the
            // sharper derived range merely because that range exists. The baseline posture is the
            // existing generic statement of whether approach is currently warranted; this term
            // adds value to the speed of that approach rather than replacing the band policy.
            if (profile.MoveSpeed <= 0
                || enemies.Count == 0
                || frame.BaselinePosture is not (
                    EngagementOptionKind.CloseToContact
                    or EngagementOptionKind.JogToward
                    or EngagementOptionKind.RunToward))
            {
                return 0;
            }

            ValueTuple<float, float> currentCentroid =
                BattleEngagementFrameBuilder.Centroid(squad);
            float desiredRange = profile.IsContactSeeking
                ? 1f
                : Math.Max(1f, profile.EffectiveEngagementRange);
            float value = 0;
            foreach (BattleSquad enemy in enemies.OrderBy(candidate => candidate.Id))
            {
                if (!profiles.TryGetValue(enemy.Id, out BattleSquadCapabilityProfile opposing)
                    || !frames.ContainsKey(enemy.Id))
                {
                    continue;
                }

                ValueTuple<float, float> enemyCentroid =
                    BattleEngagementFrameBuilder.Centroid(enemy);
                float before = Distance(currentCentroid, enemyCentroid);
                float after = Distance(projectedCentroid, enemyCentroid);

                // Both distances are measured to where the quarry is standing NOW, so against a
                // withdrawing enemy the gross closing this option shows is not what the squad
                // keeps: the quarry spends the same turn opening the range again. Netting it out
                // is what stops a stern chase from being repriced as progress every turn. Without
                // it a pursuer at matched speed scored the full value of closing 6 yards, took
                // none of it, and scored the identical 6 yards again next turn — arrival_value
                // 65.8 per turn for an arrival that never came (Xibarrus Theta, 2026-08-04).
                float quarrySpeed = QuarryWithdrawalRate(
                    frame, frames[enemy.Id].Role);
                after = before - Math.Max(0, before - after - quarrySpeed);
                if (before <= desiredRange || after >= before - 0.0001f) continue;

                // The discount has to run on the same net rate: at matched speed the useful range
                // is not profile.MoveSpeed turns away, it is unreachable, and the floor makes that
                // read as "so far off it is worth nothing" rather than "arrives next turn".
                float speed = Math.Max(0.1f, profile.MoveSpeed - quarrySpeed);
                float turnsBefore = Math.Max(0, before - desiredRange) / speed;
                float turnsAfter = Math.Max(0, after - desiredRange) / speed;
                float arrivalDiscountDelta =
                    1f / (1f + turnsAfter) - 1f / (1f + turnsBefore);
                if (arrivalDiscountDelta <= 0) continue;

                // Arrival value is the offensive opportunity unlocked by reaching the useful
                // range. Incoming exposure remains in EvaluateIncomingNow and the continuation
                // exchange, so using the net rate here would count that risk twice and could make
                // every necessary approach look worse simply because the enemy can shoot back.
                //
                // It is the MARGINAL rate, not the gross one. The gross rate at the destination
                // prices arrival as though the squad were doing nothing where it stands, so a
                // squad already delivering fire is paid the full post-arrival rate for abandoning
                // it. Measured 2026-08-07: a flamer bearer standing 10 yards from its target --
                // inside a 30-yard weapon, burning it for 0.775 battle value this turn -- scored
                // arrival 0.971 for running to contact and taking 0.000, so CloseToContact beat
                // Hold 1.705 to 1.310 and the cone was never fired.
                //
                // What closing actually buys is the IMPROVEMENT in the per-turn rate. A squad
                // whose rate is already what it will be at the destination gains nothing by
                // arriving sooner; a melee squad out of reach still scores 0 where it stands and
                // closes exactly as it did before, as does any squad outside its weapon's reach.
                // This is the same invariant the BaselinePosture guard above reaches for -- do not
                // pull a squad toward a sharper range merely because that range exists -- which
                // that guard cannot enforce for a contact-seeking profile, since a contact seeker
                // is precisely the case whose baseline posture is always a closing one.
                float exchangeRate = EvaluateOutgoingExchangeRate(
                    squad,
                    enemy,
                    profile,
                    opposing,
                    frames,
                    desiredRange);
                float currentRate = EvaluateOutgoingExchangeRate(
                    squad,
                    enemy,
                    profile,
                    opposing,
                    frames,
                    before);
                float rateGain = exchangeRate - currentRate;
                if (rateGain <= 0) continue;
                value += rateGain * ExpectedRemainingTurns * arrivalDiscountDelta;
            }
            return value;
        }

        private float EvaluateBestContinuation(
            BattleSquad squad,
            BattleSquadCapabilityProfile profile,
            IReadOnlyDictionary<int, BattleSquadCapabilityProfile> profiles,
            IReadOnlyDictionary<int, SquadEngagementFrame> frames,
            IReadOnlyCollection<BattleSquad> enemies,
            IReadOnlyDictionary<int, float> ranges,
            int depth)
        {
            if (depth <= 0)
            {
                // PHASE 5d (Design/Reference/EngagementScoringOverhaul.md). The terminal used to be
                // `attainable * 0.25 / (1 + turnsToAct)` -- 41% of `future` (9.312 of 22.84) built
                // from the squad's OWN battle value, with no per-turn semantics and no reference to
                // what it was shooting at. It remains the same per-turn net exchange as the
                // plies, but arrival is scaled separately from the short-ply discount:
                //
                //     terminal = exchange(rangeWhenActing) * ExpectedRemainingTurns
                //         / (1 + turnsToAct)
                //
                // Read literally: "once I am standing where I want to stand, this is what each
                // further turn is worth after arrival, scaled by the expected remaining battle
                // length. A short rollout discount must not make a battle that starts 400 yards
                // away effectively end before the charge can pay off.
                //
                // The closing gradient survives the switch to the honest rate table: a squad out of
                // weapon reach scores 0 exchange AT its current range, but the terminal is
                // evaluated at rangeWhenActing -- where it will be standing -- so closing still pays.
                // `desired` remains EffectiveEngagementRange (Phase 2), not PreferredBandUpper.
                float terminal = 0;
                foreach (BattleSquad enemy in enemies.OrderBy(candidate => candidate.Id))
                {
                    float range = Math.Max(0, ranges[enemy.Id]);
                    float desired = profile.IsContactSeeking
                        ? 1f
                        : Math.Max(1f, profile.EffectiveEngagementRange);
                    // TurnsUntilWeReachTarget: own speed, own preferred band.
                    float turnsToAct = Math.Max(0, range - desired)
                        / Math.Max(0.1f, profile.MoveSpeed);
                    float rangeWhenActing = Math.Min(range, Math.Max(desired, 0f));
                    float exchangeRate = EvaluateExchangeRate(
                        squad,
                        enemy,
                        profile,
                        profiles[enemy.Id],
                        frames,
                        rangeWhenActing,
                        // A squad that has taken position stands and shoots, so the terminal is
                        // priced at the Hold retention rather than at a moving policy's.
                        outgoingRetention: 1f,
                        targetSpeed: 0f);
                    terminal += exchangeRate * ExpectedRemainingTurns
                        / (1f + turnsToAct);
                    // Terminal value represents attainable action opportunity, not generic distance:
                    // a squad with no usable offense receives no reward merely for closing.
                }
                return terminal;
            }
            float best = float.MinValue;
            // A future state chooses again.  This is the bounded policy comparison the previous
            // fixed baseline rollout lacked: root Hold may continue with Run, root Run may continue
            // with Hold/fire, and Jog is valued only at its aggregate moving-fire retention.
            foreach (EngagementOptionKind policy in new[]
            {
                EngagementOptionKind.Hold,
                EngagementOptionKind.JogToward,
                EngagementOptionKind.RunToward
            })
            {
                float exchange = 0;
                Dictionary<int, float> nextRanges = [];
                foreach (BattleSquad enemy in enemies.OrderBy(candidate => candidate.Id))
                {
                    BattleSquadCapabilityProfile opposing = profiles[enemy.Id];
                    float range = Math.Max(0, ranges[enemy.Id]);
                    float outgoingRetention = policy switch
                    {
                        EngagementOptionKind.Hold => 1f,
                        EngagementOptionKind.JogToward => 0.65f,
                        _ => 0f
                    };
                    float ourMotion = PolicyRangeDelta(profile, range, policy);
                    exchange += EvaluateExchangeRate(
                        squad,
                        enemy,
                        profile,
                        opposing,
                        frames,
                        range,
                        outgoingRetention,
                        targetSpeed: Math.Max(0, -ourMotion));
                    float theirMotion = (frames[squad.Id].Role
                        is EngagementSquadRole.Pursuit or EngagementSquadRole.Standoff)
                        ? Math.Max(0, frames[squad.Id].QuarryRunSpeed)
                        : BaselineRangeDelta(opposing, frames[enemy.Id].Role, range);
                    nextRanges[enemy.Id] = Math.Max(0, range + ourMotion + theirMotion);
                }
                float value = exchange + EngagementFutureDiscount * EvaluateBestContinuation(
                    squad, profile, profiles, frames, enemies, nextRanges, depth - 1);
                if (value > best) best = value;
            }
            return best == float.MinValue ? 0 : best;
        }

        // Projected own motion for one lookahead policy. Phase 2
        // (Design/Reference/EngagementScoringOverhaul.md): `desired` is the effectiveness-derived
        // EffectiveEngagementRange, not PreferredBandUpper. PreferredBandUpper is the weapon's
        // MAXIMUM range, so any range already inside reach yielded `range > desired == false` and
        // this returned 0 own-motion for EVERY policy -- the lookahead could not see its own
        // movement at all.
        private static float PolicyRangeDelta(
            BattleSquadCapabilityProfile profile,
            float range,
            EngagementOptionKind policy)
        {
            if (policy == EngagementOptionKind.Hold) return 0;
            float speed = profile.MoveSpeed * (policy == EngagementOptionKind.JogToward
                ? SoldierMovementPlanner.JogSpeedMultiplier
                : 1f);
            float desired = profile.IsContactSeeking
                ? 1f
                : Math.Max(1f, profile.EffectiveEngagementRange);
            return range > desired ? -Math.Min(speed, range - desired) : 0;
        }

        // `opposingRole` is the target's SquadEngagementFrame.Role for the CURRENT turn (Layer 1's
        // frozen withdrawal declaration -- see BattleEngagementFrameBuilder.BuildSide), not morale.
        // Bound and Routing squads have been ordered to run at full MoveSpeed away from the fight
        // (see BuildSide's quarryRunSpeed switch, which uses exactly these two roles); that takes
        // precedence over IsContactSeeking, so a melee-only profile does not get projected as
        // charging while its own side has it fleeing. Cover/RearGuard hold position to screen the
        // withdrawal (quarryRunSpeed 0 for those) and fall through to the normal band logic below --
        // Phase 1, Design/Reference/EngagementScoringOverhaul.md.
        private static float BaselineRangeDelta(
            BattleSquadCapabilityProfile profile,
            EngagementSquadRole opposingRole,
            float range)
        {
            if (opposingRole is EngagementSquadRole.Bound or EngagementSquadRole.Routing)
            {
                return profile.MoveSpeed;
            }
            if (profile.IsContactSeeking) return range > 1
                ? -Math.Min(profile.MoveSpeed, range - 1)
                : 0;
            // Phase 2 audit: kept on the PreferredBand pair rather than EffectiveEngagementRange.
            // This is a hysteresis BAND with a matched lower edge (PreferredBandLower is derived
            // from the same reach), and it must agree with
            // BattleEngagementFrameBuilder.Baseline's posture choice, which uses the same pair.
            // Substituting only the upper edge could invert the band whenever the effectiveness-
            // derived range falls below PreferredBandLower.
            if (range > profile.PreferredBandUpper + 1)
            {
                return -Math.Min(profile.MoveSpeed * SoldierMovementPlanner.JogSpeedMultiplier,
                    range - profile.PreferredBandUpper);
            }
            if (range < profile.PreferredBandLower - 1)
            {
                return Math.Min(profile.MoveSpeed * SoldierMovementPlanner.WalkSpeedMultiplier,
                    profile.PreferredBandLower - range);
            }
            return 0;
        }

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
            frame.Role == EngagementSquadRole.Pursuit
                && quarryRole is EngagementSquadRole.Bound or EngagementSquadRole.Routing
                    ? Math.Max(0, frame.QuarryRunSpeed)
                    : 0;

        /// <summary>
        /// PHASE 5c (Design/Reference/EngagementScoringOverhaul.md). One ply's net battle-value
        /// exchange between <paramref name="squad"/> and <paramref name="enemy"/> at a projected
        /// centroid separation. This is what makes `outgoing` and `future` commensurable: both are
        /// now <c>hit * (takeOut + lambda * woundProgress) * targetBV</c>, summed per-soldier.
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
        /// twice and systematically understate every shooting option. The lookahead does not go
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
        /// <para>MELEE is untouched by the table, which is ranged-only, and keeps its capability
        /// proxy (13% of the attacker's usable melee battle value inside 1.5). Dropping it would
        /// make melee-only enemies read as harmless in the lookahead. The outgoing melee half keeps
        /// its <c>PairWeights</c> allocation -- a squad can only be in contact with so many enemies
        /// at once -- and the two halves are combined with <c>max</c>, as before.</para>
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
                    outgoingAllocation * MeleeRemovalRate(profile, range)));
        }

        internal float EvaluateExchangeRate(
            BattleSquad squad,
            BattleSquad enemy,
            BattleSquadCapabilityProfile profile,
            BattleSquadCapabilityProfile opposing,
            IReadOnlyDictionary<int, SquadEngagementFrame> frames,
            float range,
            float outgoingRetention,
            float targetSpeed)
        {
            float incomingAllocation = frames.TryGetValue(
                enemy.Id, out SquadEngagementFrame theirFrame)
                    ? theirFrame.PairWeights.GetValueOrDefault(squad.Id)
                    : 0f;

            float outgoing = EvaluateOutgoingExchangeRate(
                squad,
                enemy,
                profile,
                opposing,
                frames,
                range);
            float incomingBulk = PostureBulkMultiplier(
                frames.GetValueOrDefault(enemy.Id)?.BaselinePosture
                    ?? EngagementOptionKind.Hold);
            float incoming = float.IsPositiveInfinity(incomingBulk)
                ? 0
                : incomingAllocation * Math.Min(
                    profile.TotalAbleBattleValue,
                    Math.Max(
                        TotalRangedRemovalRate(
                            enemy,
                            range,
                            targetSpeed,
                            incomingBulk),
                        MeleeRemovalRate(opposing, range)));
            return (outgoing * outgoingRetention) - incoming;
        }

        // The one surviving piece of the old capability proxy. The Phase 4/5 removal-rate table is
        // ranged-only, so melee threat is still priced from the attacker's usable melee battle
        // value. PHASE 6 did not replace it -- it is a per-turn exchange rate at contact, not a
        // range question, and the removal-rate table has no melee side to read. What Phase 6 did do
        // is share the coefficient: BattleEngagementFrameBuilder.CalculateEffectiveEngagementRange
        // prices the SAME melee threat (discounted by arrival time) when it derives a standoff, and
        // the two must not disagree about what a charge landing is worth.
        private static float MeleeRemovalRate(
            BattleSquadCapabilityProfile attacker,
            float range)
        {
            return range <= 1.5f
                ? attacker.UsableMeleeBattleValue
                    * BattleModifiersUtil.MeleeContactRemovalFraction
                : 0f;
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
