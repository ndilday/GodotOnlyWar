using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Battles.Models;

namespace OnlyWar.Battles
{
    /// <summary>
    /// Exchange rates sampled along the ranges a lane can pass through, interpolated linearly in
    /// range and held at the ends. Rates are battle value removed per turn at full strength:
    /// outgoing in the ENEMY's battle value, incoming in OURS.
    /// </summary>
    internal sealed class RangeRates
    {
        private readonly float[] _ranges;
        private readonly float[] _outgoing;
        private readonly float[] _incoming;

        internal static readonly RangeRates None = new([0f], [0f], [0f]);

        internal RangeRates(float[] ranges, float[] outgoing, float[] incoming)
        {
            _ranges = ranges;
            _outgoing = outgoing;
            _incoming = incoming;
        }

        internal static RangeRates Constant(float outgoing, float incoming) =>
            new([0f], [outgoing], [incoming]);

        internal static RangeRates Sample(
            IReadOnlyList<float> ranges,
            Func<float, float> outgoing,
            Func<float, float> incoming)
        {
            float[] sorted = ranges.Distinct().OrderBy(range => range).ToArray();
            return new RangeRates(
                sorted,
                sorted.Select(outgoing).ToArray(),
                sorted.Select(incoming).ToArray());
        }

        internal bool IsZero => _outgoing.All(rate => rate <= 0) && _incoming.All(rate => rate <= 0);

        /// <summary>
        /// The average of the rates over the separations between <paramref name="from"/> and
        /// <paramref name="to"/>: the exact integral of the interpolated (piecewise-linear, held at
        /// the ends) rates divided by the distance, so a turn of movement is priced over the ground
        /// it covers rather than at the range it began.
        /// </summary>
        internal (float Outgoing, float Incoming) Average(float from, float to)
        {
            float lower = Math.Min(from, to);
            float upper = Math.Max(from, to);
            if (upper - lower < 0.0001f)
            {
                return At(lower);
            }
            float outgoingSum = 0;
            float incomingSum = 0;
            float x = lower;
            (float outgoing, float incoming) = At(lower);
            foreach (float breakpoint in _ranges)
            {
                if (breakpoint <= lower || breakpoint >= upper)
                {
                    continue;
                }
                (float nextOutgoing, float nextIncoming) = At(breakpoint);
                outgoingSum += 0.5f * (outgoing + nextOutgoing) * (breakpoint - x);
                incomingSum += 0.5f * (incoming + nextIncoming) * (breakpoint - x);
                (x, outgoing, incoming) = (breakpoint, nextOutgoing, nextIncoming);
            }
            (float endOutgoing, float endIncoming) = At(upper);
            outgoingSum += 0.5f * (outgoing + endOutgoing) * (upper - x);
            incomingSum += 0.5f * (incoming + endIncoming) * (upper - x);
            return (outgoingSum / (upper - lower), incomingSum / (upper - lower));
        }

        internal (float Outgoing, float Incoming) At(float range)
        {
            if (_ranges.Length == 1 || range <= _ranges[0])
            {
                return (_outgoing[0], _incoming[0]);
            }
            int last = _ranges.Length - 1;
            if (range >= _ranges[last])
            {
                return (_outgoing[last], _incoming[last]);
            }
            int upper = 1;
            while (_ranges[upper] < range)
            {
                upper++;
            }
            int lower = upper - 1;
            float t = (range - _ranges[lower]) / (_ranges[upper] - _ranges[lower]);
            return (
                _outgoing[lower] + t * (_outgoing[upper] - _outgoing[lower]),
                _incoming[lower] + t * (_incoming[upper] - _incoming[lower]));
        }
    }

    /// <summary>
    /// What one squad and one enemy squad would do to each other in one continuation, as a range
    /// path: the separation starts at <see cref="StartRange"/>; while this squad moves it falls by
    /// <see cref="MovingClosing"/> a turn until it reaches <see cref="StopRange"/>, and while it
    /// stands it falls by <see cref="StandingClosing"/> (an approaching contact seeker closes it, a
    /// fleeing quarry opens it: negative). Rates are read along the path from
    /// <see cref="Moving"/> or <see cref="Standing"/>. When an approaching contact seeker reaches
    /// <see cref="ContactRange"/> both sides fight in melee at the contact rates.
    /// </summary>
    internal readonly record struct ExchangeLane(
        float EnemyPool,
        SquadBreakProfile EnemyBreak,
        float StartRange,
        bool Moves,
        float StopRange,
        float MovingClosing,
        float StandingClosing,
        float MaximumRange,
        RangeRates Moving,
        RangeRates Standing,
        bool ContactPossible,
        float ContactRange,
        float ContactOutgoing,
        float ContactIncoming);

    /// <summary>
    /// The morale inputs a squad's break hazard needs, read once per planning pass. Mirrors the
    /// live check (<see cref="BattleMoraleEvaluator"/>): resolve from Ego, the leader's own
    /// resolve and threshold bonus, the synapse skip, and the casualties the squad has already
    /// taken this mission.
    /// </summary>
    internal sealed record SquadBreakProfile(
        bool CanBreak,
        int AbleSoldiers,
        float MeanResolve,
        float ResolveSpread,
        bool HasLeader,
        float LeaderResolve,
        bool LeaderAlreadyLost,
        float PriorLossFraction)
    {
        internal static readonly SquadBreakProfile Unbreakable =
            new(false, 1, 0f, 0f, false, 0f, false, 0f);
    }

    /// <summary>
    /// The coupled exchange behind <see cref="EngagementPotential"/>'s net-rate value.
    ///
    /// <para>WHY COUPLED. The potential used to integrate our fire and theirs separately, each from
    /// today's full strength, and cap each at its own pool. That had two faults. The side that took
    /// its losses first (the one crossing open ground) still arrived firing at full strength; and
    /// over a long horizon both pools saturated together, so the forecast could say "we kill nearly
    /// all of them and they kill nearly all of us" and reduce to enemy pool minus own pool -- which
    /// a closing option could only win. Here every turn's fire is scaled by the surviving share of
    /// the side that fires, so losses taken early cost fire later.</para>
    ///
    /// <para>WHY BREAKS. Morale counts heads, and most fights end in a rout, not a last man. Each
    /// side carries a break hazard computed from the live check's own stress formula: per-turn and
    /// cumulative casualties, an expected leader loss, and the force disadvantage of the two loss
    /// shares. A squad that would rout after a quarter of its strength cannot buy kills with the
    /// rest, which is what made trading bodies for elites look profitable. Synapse-covered and
    /// synapse-providing squads never check, and neither does a lone model, so they never break
    /// here. A break removes the broken side's remaining battle value from the fight (the gauntlet
    /// prices battle value by who holds the field).</para>
    ///
    /// <para>WHY A RANGE PATH. The separation moves while the exchange runs: we close, an enemy
    /// contact seeker charges through our fire band, a quarry runs out of range. Holding the
    /// range fixed made standing against a charge look like never getting a shot.</para>
    ///
    /// <para>DETERMINISTIC AND SMOOTH. No RNG: the hazard is the chance that the realized fail
    /// fraction clears the rout threshold, with the binomial spread of a squad of n soldiers as the
    /// width, so scores move continuously with geometry instead of jumping when an expected value
    /// crosses a line.</para>
    ///
    /// <para>Not modelled: routing friendlies in sight, local outnumbering, command auras and mob
    /// support (shock terms the live check has), and the rest of each side beyond the squads in the
    /// lanes. The context term uses the lanes' losses, not the whole side's.</para>
    /// </summary>
    internal static class EngagementExchangeForecast
    {
        private const float FlowEpsilon = 0.0001f;
        private const float SurvivalEpsilon = 0.0001f;
        private const float RangeEpsilon = 0.001f;

        /// <summary>
        /// Per-turn weight on the exchange and on breaks. Without it a long horizon (up to
        /// <see cref="EngagementHorizonModel.MaximumExchangeTurns"/>) lets any positive rate grind
        /// the other side to nothing, every option ends in the same place, and the forecast stops
        /// telling them apart. The same outcome sooner is worth more, because later turns are less
        /// certain: other squads intervene and the battle moves on. 0.97 halves a turn's weight
        /// after about 23 turns.
        /// </summary>
        internal const float TurnDiscount = 0.97f;

        internal readonly record struct Result(float EnemyValueRemoved, float OwnValueLost);

        /// <summary>
        /// Diagnostics: one turn of a forecast, summed over its lanes (range is the first lane's at
        /// the turn's end). Rates are per turn before survival and strength scaling; the flows are
        /// what the turn actually moved; hazards are this turn's break chances.
        /// </summary>
        internal readonly record struct TurnTrace(
            int Turn,
            float Range,
            float OutRate,
            float InRate,
            float Outgoing,
            float Incoming,
            float OwnRemaining,
            float OwnSurvival,
            float OwnHazard,
            float EnemyRemaining,
            float EnemySurvival,
            float EnemyHazard);

        internal static Result Run(
            float ownPool,
            SquadBreakProfile own,
            IReadOnlyList<ExchangeLane> lanes,
            float horizonTurns,
            float turnDiscount = TurnDiscount,
            Action<TurnTrace> trace = null)
        {
            if (lanes == null || lanes.Count == 0 || ownPool <= 0
                || !float.IsFinite(horizonTurns) || horizonTurns <= 0)
            {
                return default;
            }

            int count = lanes.Count;
            float[] remaining = new float[count];
            float[] survival = new float[count];
            float[] previousLoss = new float[count];
            float[] range = new float[count];
            float lanesPool = 0;
            for (int index = 0; index < count; index++)
            {
                remaining[index] = Math.Max(0, lanes[index].EnemyPool);
                survival[index] = 1f;
                range[index] = lanes[index].StartRange;
                lanesPool += remaining[index];
            }

            float ownRemaining = ownPool;
            float ownSurvival = 1f;
            float ownPreviousLoss = 0;
            float removed = 0;
            float lost = 0;
            float[] outgoing = new float[count];
            int turns = (int)Math.Ceiling(horizonTurns);
            for (int turn = 0; turn < turns; turn++)
            {
                if (ownSurvival < SurvivalEpsilon || ownRemaining <= FlowEpsilon)
                {
                    break;
                }

                float weight = Math.Min(1f, horizonTurns - turn);
                float discount = MathF.Pow(turnDiscount, turn);
                float ownShare = ownRemaining / ownPool;
                float incoming = 0;
                float outgoingTotal = 0;
                bool rangeMoved = false;
                float traceOutRate = 0;
                float traceInRate = 0;
                float traceOwnHazard = 0;
                float traceEnemyHazard = 0;
                for (int index = 0; index < count; index++)
                {
                    ExchangeLane lane = lanes[index];
                    outgoing[index] = 0;
                    if (remaining[index] <= FlowEpsilon || survival[index] < SurvivalEpsilon)
                    {
                        continue;
                    }

                    (float outRate, float inRate, float nextRange) = Step(lane, range[index]);
                    traceOutRate += outRate;
                    traceInRate += inRate;
                    rangeMoved |= Math.Abs(nextRange - range[index]) > RangeEpsilon;
                    range[index] = nextRange;

                    outgoing[index] = Math.Min(remaining[index], ownShare * outRate * weight);
                    outgoingTotal += outgoing[index];
                    // A share of this enemy has (in expectation) already broken; its fire went
                    // with it.
                    incoming += survival[index] * (remaining[index] / lane.EnemyPool)
                        * inRate * weight;
                }

                float ownLoss = Math.Min(ownRemaining, incoming);
                float ownBefore = ownRemaining;
                ownRemaining -= ownLoss;
                lost += discount * ownSurvival * ownLoss;
                float lanesBefore = 0;
                float lanesAfter = 0;
                for (int index = 0; index < count; index++)
                {
                    lanesBefore += remaining[index];
                    remaining[index] -= outgoing[index];
                    lanesAfter += remaining[index];
                    removed += discount * ownSurvival * survival[index] * outgoing[index];
                }

                if (!rangeMoved && ownLoss <= FlowEpsilon && outgoingTotal <= FlowEpsilon)
                {
                    // Nothing is happening and the geometry has stopped changing: nothing will.
                    break;
                }

                // Loss shares for the context term, each a fraction of that side's own pool.
                float ownLossThisTurn = ownLoss / ownPool;
                float ownCumulative = 1 - ownRemaining / ownPool;
                float enemyLossThisTurn = lanesPool > 0 ? (lanesBefore - lanesAfter) / lanesPool : 0;
                float enemyCumulative = lanesPool > 0 ? 1 - lanesAfter / lanesPool : 0;
                float enemyPreviousLoss = 0;
                for (int index = 0; index < count; index++)
                {
                    enemyPreviousLoss += previousLoss[index];
                }
                enemyPreviousLoss = lanesPool > 0 ? enemyPreviousLoss / lanesPool : 0;

                if (ownLoss > FlowEpsilon)
                {
                    float disadvantage = BattleMoraleEvaluator.ComputeForceDisadvantage(
                        ownCumulative,
                        enemyCumulative,
                        ownLossThisTurn + ownPreviousLoss,
                        enemyLossThisTurn + enemyPreviousLoss);
                    float hazard = BreakHazard(
                        own,
                        ownLoss / ownBefore,
                        ownCumulative,
                        disadvantage);
                    lost += discount * ownSurvival * hazard * ownRemaining;
                    ownSurvival *= 1 - hazard;
                    traceOwnHazard = hazard;
                }

                for (int index = 0; index < count; index++)
                {
                    float laneBefore = remaining[index] + outgoing[index];
                    if (outgoing[index] > FlowEpsilon && laneBefore > 0)
                    {
                        ExchangeLane lane = lanes[index];
                        float laneCumulative = 1 - remaining[index] / lane.EnemyPool;
                        float disadvantage = BattleMoraleEvaluator.ComputeForceDisadvantage(
                            laneCumulative,
                            ownCumulative,
                            (outgoing[index] + previousLoss[index]) / lane.EnemyPool,
                            ownLossThisTurn + ownPreviousLoss);
                        float hazard = BreakHazard(
                            lane.EnemyBreak,
                            outgoing[index] / laneBefore,
                            laneCumulative,
                            disadvantage);
                        removed += discount * ownSurvival * survival[index] * hazard
                            * remaining[index];
                        survival[index] *= 1 - hazard;
                        traceEnemyHazard = Math.Max(traceEnemyHazard, hazard);
                    }
                    previousLoss[index] = outgoing[index];
                }
                ownPreviousLoss = ownLossThisTurn;
                if (trace != null)
                {
                    float enemySurvival = 0;
                    for (int index = 0; index < count; index++)
                    {
                        enemySurvival += lanesPool > 0
                            ? survival[index] * Math.Max(0, lanes[index].EnemyPool) / lanesPool
                            : 0;
                    }
                    trace(new TurnTrace(
                        turn, range[0], traceOutRate, traceInRate, outgoingTotal, ownLoss,
                        ownRemaining, ownSurvival, traceOwnHazard, lanesAfter, enemySurvival,
                        traceEnemyHazard));
                }
            }

            return new Result(removed, lost);
        }

        /// <summary>
        /// One turn of a lane's range path and the separation at its end. The turn is split into
        /// the share spent moving (rates at the turn's starting separation), standing (rates where
        /// the squad stands: the arrival point in a turn in which it arrives), and in contact (all
        /// of a turn that starts in the band, the rest of the turn after arrival in one that
        /// reaches it). The range rates of a lane with contact in it are ranged only; contact is
        /// priced by <see cref="ExchangeLane.ContactOutgoing"/> and
        /// <see cref="ExchangeLane.ContactIncoming"/>.
        /// </summary>
        private static (float Outgoing, float Incoming, float NextRange) Step(
            ExchangeLane lane,
            float range)
        {
            float moving = 0;
            if (lane.Moves && range > lane.StopRange + RangeEpsilon)
            {
                moving = lane.MovingClosing > 0
                    ? Math.Clamp((range - lane.StopRange) / lane.MovingClosing, 0f, 1f)
                    : 1f;
            }
            float closing = moving * lane.MovingClosing + (1 - moving) * lane.StandingClosing;
            float floor = lane.ContactPossible
                ? lane.ContactRange
                : Math.Min(range, lane.Moves ? lane.StopRange : range);
            float next = Math.Clamp(range - closing, Math.Min(floor, range), Math.Max(
                lane.MaximumRange, range));

            // Share of the turn spent in contact: all of it once the turn starts inside the band,
            // and the part after arrival in the turn that reaches it.
            float contact = 0;
            if (lane.ContactPossible)
            {
                if (range <= lane.ContactRange + RangeEpsilon)
                {
                    contact = 1f;
                }
                else if (next <= lane.ContactRange + RangeEpsilon)
                {
                    contact = 1f - TimeToContact(lane, range, moving);
                }
            }
            float movingShare = Math.Min(moving, 1f - contact);
            float standingShare = Math.Max(0f, 1f - movingShare - contact);

            // Each part of the turn is priced over the ground it covers, not at the range the turn
            // began: the moving part from the start to where the move ends, the standing part at
            // the arrival point in a turn in which the squad arrives, and over its own drift (an
            // enemy charging in, a quarry opening the range) otherwise. Read at the start range,
            // fire that climbs steeply close in made a charge's value a sawtooth with the period of
            // one turn's run: whether its last moving turn began at 4, 8 or 12 yards decided a
            // large part of the incoming (30 Hormagaunts against 20 PDF, plus or minus 10 to 15
            // battle value on an 8-yard period, 2026-10-09).
            float movingEnd = Math.Max(
                Math.Min(floor, range),
                range - movingShare * lane.MovingClosing);
            (float movingOut, float movingIn) = movingShare > 0
                ? lane.Moving.Average(range, movingEnd)
                : (0f, 0f);
            (float standingOut, float standingIn) = standingShare <= 0
                ? (0f, 0f)
                : moving > 0
                    ? lane.Standing.At(next)
                    : lane.Standing.Average(range, next);
            float outRate = movingShare * movingOut + standingShare * standingOut
                + contact * lane.ContactOutgoing;
            float inRate = movingShare * movingIn + standingShare * standingIn
                + contact * lane.ContactIncoming;
            return (outRate, inRate, next);
        }

        /// <summary>
        /// The share of a turn that passes before the separation reaches the contact range: the
        /// moving part covers <see cref="ExchangeLane.MovingClosing"/> a turn down to the stop
        /// range, and after it an approaching enemy closes the rest at
        /// <see cref="ExchangeLane.StandingClosing"/>.
        /// </summary>
        private static float TimeToContact(ExchangeLane lane, float range, float moving)
        {
            float afterMoving = range - moving * lane.MovingClosing;
            if (afterMoving <= lane.ContactRange + RangeEpsilon)
            {
                return lane.MovingClosing > 0
                    ? Math.Clamp((range - lane.ContactRange) / lane.MovingClosing, 0f, 1f)
                    : 0f;
            }
            if (lane.StandingClosing <= 0)
            {
                return 1f;
            }
            return Math.Clamp(
                moving + (afterMoving - lane.ContactRange) / lane.StandingClosing, 0f, 1f);
        }

        /// <summary>
        /// The chance that a squad taking these losses routs on this turn's check. Stress is the
        /// live check's (<see cref="MoraleConstants"/>): shock from this turn's and the cumulative
        /// casualty fractions plus the expected leader loss, times the context multiplier. The
        /// expected fail fraction is the normal CDF of stress against the squad's mean resolve,
        /// widened by the spread of resolve inside the squad; a leader who would hold raises the
        /// rout bar as he does live.
        /// </summary>
        internal static float BreakHazard(
            SquadBreakProfile profile,
            float casualtyFractionThisTurn,
            float cumulativeLossOfCurrentPool,
            float forceDisadvantage)
        {
            if (profile == null || !profile.CanBreak)
            {
                return 0;
            }

            float priorKept = 1 - Math.Clamp(profile.PriorLossFraction, 0, 1);
            float cumulative = 1 - priorKept * (1 - Math.Clamp(cumulativeLossOfCurrentPool, 0, 1));
            // A soldier lost is the leader with probability equal to the share of the squad lost.
            float leaderLost = profile.LeaderAlreadyLost
                ? 1f
                : profile.HasLeader ? Math.Clamp(cumulativeLossOfCurrentPool, 0, 1) : 0f;
            float shock = MoraleConstants.ShockCasualtyThisTurnWeight
                    * Math.Clamp(casualtyFractionThisTurn, 0, 1)
                + MoraleConstants.ShockCumulativeCasualtyWeight * cumulative
                + MoraleConstants.ShockLeaderDeadWeight * leaderLost;
            float stress = shock * (1 + MoraleConstants.ContextDisadvantageCoefficient
                * Math.Clamp(forceDisadvantage, 0, 1));

            float sigma = MoraleConstants.MoraleRollSigma;
            float spread = (float)Math.Sqrt(sigma * sigma
                + profile.ResolveSpread * profile.ResolveSpread);
            float failFraction = BattleMoraleEvaluator.NormalCdf(
                (stress - profile.MeanResolve) / spread);
            float leaderHolds = profile.HasLeader
                ? (1 - leaderLost) * BattleMoraleEvaluator.NormalCdf(
                    (profile.LeaderResolve - stress) / sigma)
                : 0f;
            float threshold = MoraleConstants.RoutThreshold
                + MoraleConstants.LeaderThresholdBonus * leaderHolds;
            // The realized fail fraction of n soldiers scatters about its expectation by
            // sqrt(F(1-F)/n): that scatter, not a hand-set band, is the width. It vanishes as F
            // goes to 0, so a squad nobody in which would fail does not break.
            float width = (float)Math.Sqrt(
                Math.Max(failFraction * (1 - failFraction), 0.0001f)
                / Math.Max(1, profile.AbleSoldiers));
            return BattleMoraleEvaluator.NormalCdf((failFraction - threshold) / width);
        }

        /// <summary>Reads a squad's break profile from its able soldiers.</summary>
        internal static SquadBreakProfile ProfileOf(
            BattleSquad squad,
            IEnumerable<BattleSquad> friendlySquads,
            BattleGridManager grid)
        {
            List<BattleSoldier> able = squad?.AbleSoldiers;
            if (able == null || able.Count == 0)
            {
                return SquadBreakProfile.Unbreakable;
            }

            // A lone model never takes a casualty check: its first casualty is the whole squad.
            // The forecast's losses are continuous, so without this it would rout a single
            // soldier, or a Carnifex, over half a lost model.
            bool checks = able.Count >= 2
                && squad.WithdrawalRole != WithdrawalRole.Routing
                && !squad.SquadProvidesSynapse
                && (grid == null || friendlySquads == null
                    || !SynapseCoverageEvaluator.IsSynapseCovered(squad, friendlySquads, grid));
            float[] resolves = able
                .Select(soldier => BattleMoraleEvaluator.EgoToResolve(soldier.Soldier.Ego))
                .ToArray();
            float mean = resolves.Average();
            float variance = resolves.Sum(value => (value - mean) * (value - mean))
                / resolves.Length;
            BattleSoldier leader = squad.SquadLeader;
            int starting = Math.Max(able.Count, squad.MissionStartingAbleSoldierCount);
            return new SquadBreakProfile(
                checks,
                able.Count,
                mean,
                (float)Math.Sqrt(variance),
                leader != null,
                leader == null ? 0f : BattleMoraleEvaluator.EgoToResolve(leader.Soldier.Ego),
                leader == null && squad.Soldiers.Any(
                    soldier => soldier.Soldier.Template.IsSquadLeader),
                starting > 0 ? 1f - able.Count / (float)starting : 0f);
        }
    }
}
