using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Battles.Models;
using OnlyWar.Domain;
using OnlyWar.Domain.FactionBehaviors;

namespace OnlyWar.Battles
{
    /// <summary>
    /// Exchange rates sampled along the ranges a lane can pass through, interpolated linearly in
    /// range and held at the ends. Rates are battle value removed per turn at full strength:
    /// outgoing in the ENEMY's battle value, incoming in OURS. Optionally also the rounds each side
    /// spends per turn at full strength to deliver that fire, for the forecast's ammunition budget;
    /// without them a lane spends nothing.
    /// </summary>
    internal sealed class RangeRates
    {
        private readonly float[] _ranges;
        private readonly float[] _outgoing;
        private readonly float[] _incoming;
        private readonly float[] _outgoingRounds;
        private readonly float[] _incomingRounds;

        internal static readonly RangeRates None = new([0f], [0f], [0f]);

        internal RangeRates(
            float[] ranges,
            float[] outgoing,
            float[] incoming,
            float[] outgoingRounds = null,
            float[] incomingRounds = null)
        {
            _ranges = ranges;
            _outgoing = outgoing;
            _incoming = incoming;
            _outgoingRounds = outgoingRounds;
            _incomingRounds = incomingRounds;
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

        /// <summary>
        /// As <see cref="Sample(IReadOnlyList{float}, Func{float, float}, Func{float, float})"/>,
        /// with each side's fire and the rounds it spends read together.
        /// </summary>
        internal static RangeRates Sample(
            IReadOnlyList<float> ranges,
            Func<float, (float Removal, float Rounds)> outgoing,
            Func<float, (float Removal, float Rounds)> incoming)
        {
            float[] sorted = ranges.Distinct().OrderBy(range => range).ToArray();
            (float Removal, float Rounds)[] outgoingSamples = sorted.Select(outgoing).ToArray();
            (float Removal, float Rounds)[] incomingSamples = sorted.Select(incoming).ToArray();
            return new RangeRates(
                sorted,
                outgoingSamples.Select(sample => sample.Removal).ToArray(),
                incomingSamples.Select(sample => sample.Removal).ToArray(),
                outgoingSamples.Select(sample => sample.Rounds).ToArray(),
                incomingSamples.Select(sample => sample.Rounds).ToArray());
        }

        internal bool IsZero => _outgoing.All(rate => rate <= 0) && _incoming.All(rate => rate <= 0);

        /// <summary>
        /// The average of the rates over the separations between <paramref name="from"/> and
        /// <paramref name="to"/>: the exact integral of the interpolated (piecewise-linear, held at
        /// the ends) rates divided by the distance, so a turn of movement is priced over the ground
        /// it covers rather than at the range it began.
        /// </summary>
        internal (float Outgoing, float Incoming) Average(float from, float to) =>
            (Average(_outgoing, from, to), Average(_incoming, from, to));

        internal (float Outgoing, float Incoming) At(float range) =>
            (Interpolate(_outgoing, range), Interpolate(_incoming, range));

        /// <summary>Rounds per turn, averaged as <see cref="Average(float, float)"/>.</summary>
        internal (float Outgoing, float Incoming) RoundsAverage(float from, float to) => (
            _outgoingRounds == null ? 0f : Average(_outgoingRounds, from, to),
            _incomingRounds == null ? 0f : Average(_incomingRounds, from, to));

        internal (float Outgoing, float Incoming) RoundsAt(float range) => (
            _outgoingRounds == null ? 0f : Interpolate(_outgoingRounds, range),
            _incomingRounds == null ? 0f : Interpolate(_incomingRounds, range));

        /// <summary>
        /// <see cref="Average(float, float)"/> and <see cref="RoundsAverage"/> in one pass: each
        /// index is found once and read from all four arrays (2026-11-12). Separately, the four
        /// arrays each did their own searches, and <see cref="RangeRates"/> was 16% of all CPU in a
        /// profile of the Governance test. The arithmetic per array is the same as
        /// <see cref="Average(float[], float, float)"/>, in the same order, so the results are
        /// bit-identical.
        /// </summary>
        internal (float Outgoing, float Incoming, float OutgoingRounds, float IncomingRounds)
            AverageAll(float from, float to)
        {
            float lower = Math.Min(from, to);
            float upper = Math.Max(from, to);
            if (upper - lower < 0.0001f)
            {
                return AtAll(lower);
            }
            bool rounds = _outgoingRounds != null;
            bool incomingRounds = _incomingRounds != null;
            Position start = Locate(lower);
            float x = lower;
            float valueOut = Read(_outgoing, start);
            float valueIn = Read(_incoming, start);
            float valueOutRounds = rounds ? Read(_outgoingRounds, start) : 0f;
            float valueInRounds = incomingRounds ? Read(_incomingRounds, start) : 0f;
            float sumOut = 0, sumIn = 0, sumOutRounds = 0, sumInRounds = 0;
            for (int index = FirstAbove(lower); index < _ranges.Length && _ranges[index] < upper; index++)
            {
                float breakpoint = _ranges[index];
                Position at = AtBreakpoint(index);
                float width = breakpoint - x;
                float next = Read(_outgoing, at);
                sumOut += 0.5f * (valueOut + next) * width;
                valueOut = next;
                next = Read(_incoming, at);
                sumIn += 0.5f * (valueIn + next) * width;
                valueIn = next;
                if (rounds)
                {
                    next = Read(_outgoingRounds, at);
                    sumOutRounds += 0.5f * (valueOutRounds + next) * width;
                    valueOutRounds = next;
                }
                if (incomingRounds)
                {
                    next = Read(_incomingRounds, at);
                    sumInRounds += 0.5f * (valueInRounds + next) * width;
                    valueInRounds = next;
                }
                x = breakpoint;
            }
            Position end = Locate(upper);
            float span = upper - lower;
            float tail = upper - x;
            sumOut += 0.5f * (valueOut + Read(_outgoing, end)) * tail;
            sumIn += 0.5f * (valueIn + Read(_incoming, end)) * tail;
            if (rounds)
            {
                sumOutRounds += 0.5f * (valueOutRounds + Read(_outgoingRounds, end)) * tail;
            }
            if (incomingRounds)
            {
                sumInRounds += 0.5f * (valueInRounds + Read(_incomingRounds, end)) * tail;
            }
            return (
                sumOut / span,
                sumIn / span,
                rounds ? sumOutRounds / span : 0f,
                incomingRounds ? sumInRounds / span : 0f);
        }

        /// <summary><see cref="At"/> and <see cref="RoundsAt"/> with one search.</summary>
        internal (float Outgoing, float Incoming, float OutgoingRounds, float IncomingRounds)
            AtAll(float range)
        {
            Position at = Locate(range);
            return (
                Read(_outgoing, at),
                Read(_incoming, at),
                _outgoingRounds == null ? 0f : Read(_outgoingRounds, at),
                _incomingRounds == null ? 0f : Read(_incomingRounds, at));
        }

        /// <summary>
        /// Where <see cref="Interpolate"/> reads a range: the first sample (Lower = -1), the last
        /// (Lower = -2), or between Lower and Lower + 1 at fraction T, computed exactly as there.
        /// </summary>
        private readonly record struct Position(int Lower, float T);

        private Position Locate(float range)
        {
            if (_ranges.Length == 1 || range <= _ranges[0])
            {
                return new Position(-1, 0f);
            }
            int last = _ranges.Length - 1;
            if (range >= _ranges[last])
            {
                return new Position(-2, 0f);
            }
            int upper = 1;
            int high = last;
            while (upper < high)
            {
                int middle = (upper + high) >> 1;
                if (_ranges[middle] < range)
                {
                    upper = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }
            int lower = upper - 1;
            return new Position(lower, (range - _ranges[lower]) / (_ranges[upper] - _ranges[lower]));
        }

        /// <summary>
        /// <see cref="Locate"/> at the sample <paramref name="index"/> itself, without the search:
        /// the ranges are sorted and distinct, so the search would stop at this index.
        /// </summary>
        private Position AtBreakpoint(int index)
        {
            if (_ranges.Length == 1 || index == 0)
            {
                return new Position(-1, 0f);
            }
            if (index == _ranges.Length - 1)
            {
                return new Position(-2, 0f);
            }
            int lower = index - 1;
            return new Position(
                lower, (_ranges[index] - _ranges[lower]) / (_ranges[index] - _ranges[lower]));
        }

        private static float Read(float[] values, Position at) => at.Lower switch
        {
            -1 => values[0],
            -2 => values[values.Length - 1],
            _ => values[at.Lower] + at.T * (values[at.Lower + 1] - values[at.Lower]),
        };

        private float Average(float[] values, float from, float to)
        {
            float lower = Math.Min(from, to);
            float upper = Math.Max(from, to);
            if (upper - lower < 0.0001f)
            {
                return Interpolate(values, lower);
            }
            float sum = 0;
            float x = lower;
            float value = Interpolate(values, lower);
            // Only the breakpoints strictly inside (lower, upper), found by binary search, in the
            // same order and with the same arithmetic as the full scan this replaced, so the sum is
            // bit-identical. The full scan with a linear Interpolate per breakpoint was quadratic in
            // the sample count and took 11.7% of planning CPU on an 8 v 8 squad battle (2026-11-11).
            for (int index = FirstAbove(lower); index < _ranges.Length && _ranges[index] < upper; index++)
            {
                float breakpoint = _ranges[index];
                float next = Interpolate(values, breakpoint);
                sum += 0.5f * (value + next) * (breakpoint - x);
                (x, value) = (breakpoint, next);
            }
            sum += 0.5f * (value + Interpolate(values, upper)) * (upper - x);
            return sum / (upper - lower);
        }

        /// <summary>The first index whose range is strictly above <paramref name="range"/>.</summary>
        private int FirstAbove(float range)
        {
            int low = 0;
            int high = _ranges.Length;
            while (low < high)
            {
                int middle = (low + high) >> 1;
                if (_ranges[middle] <= range)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }
            return low;
        }

        private float Interpolate(float[] values, float range)
        {
            if (_ranges.Length == 1 || range <= _ranges[0])
            {
                return values[0];
            }
            int last = _ranges.Length - 1;
            if (range >= _ranges[last])
            {
                return values[last];
            }
            // The first sample at or above the range, by binary search (the same index the linear
            // scan found; the ranges are sorted and distinct, and range lies strictly inside them).
            int upper = 1;
            int high = last;
            while (upper < high)
            {
                int middle = (upper + high) >> 1;
                if (_ranges[middle] < range)
                {
                    upper = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }
            int lower = upper - 1;
            float t = (range - _ranges[lower]) / (_ranges[upper] - _ranges[lower]);
            return values[lower] + t * (values[upper] - values[lower]);
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
    /// <see cref="EnemyRounds"/> is the enemy's ammunition for this lane (infinite: not counted);
    /// its spend comes from the rates' incoming rounds.
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
        float ContactIncoming,
        float EnemyRounds = float.PositiveInfinity);

    /// <summary>
    /// The morale inputs a squad's break hazard needs, read once per planning pass. Mirrors the
    /// live check (<see cref="BattleMoraleEvaluator"/>): resolve from Ego, the leader's own
    /// resolve and threshold bonus, the synapse skip, and the casualties the squad has already
    /// taken this mission.
    /// </summary>
    /// <param name="ShockRelief">
    /// Taken off shock before the context multiplier, as the live check takes mob support
    /// (<see cref="MobMoraleSupportEvaluator"/>); negative adds stress.
    /// </param>
    /// <param name="LeaderCoerces">
    /// A mob leader ignores a rout result once, but not on two turns running
    /// (<see cref="BattleMoraleService"/>): while he lives the squad breaks only on a second
    /// failed check in a row.
    /// </param>
    /// <param name="HasFriends">
    /// The squad has friendly squads, whose rout the live check counts as shock
    /// (<see cref="MoraleConstants.ShockRoutingVisibleWeight"/>). See
    /// <see cref="EngagementExchangeForecast.RoutingFriends"/>.
    /// </param>
    /// <param name="RoutingFriendsNow">Share of the squad's friends (by able soldiers) routing now.</param>
    internal sealed record SquadBreakProfile(
        bool CanBreak,
        int AbleSoldiers,
        float MeanResolve,
        float ResolveSpread,
        bool HasLeader,
        float LeaderResolve,
        bool LeaderAlreadyLost,
        float PriorLossFraction,
        float ShockRelief = 0f,
        bool LeaderCoerces = false,
        bool HasFriends = false,
        float RoutingFriendsNow = 0f)
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
    /// <para>Mob support and a mob leader's coercion are modelled, held at their value at the
    /// start of the forecast (<see cref="SquadBreakProfile.ShockRelief"/>,
    /// <see cref="SquadBreakProfile.LeaderCoerces"/>, 2026-10-23), and so are routing friends, as the
    /// cascade of <see cref="RoutingFriends"/> (2026-10-26). Not modelled: local outnumbering and
    /// generic command auras (shock terms the live check has), and
    /// the rest of each side beyond the squads in the lanes. The context term uses the lanes'
    /// losses, not the whole side's.</para>
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

        /// <summary>
        /// The forecast's value terms (discounted, breaks included) and, for diagnostics and
        /// calibration against simulated battles, what it expects to happen: casualties each side
        /// takes from fire and melee (undiscounted, in that side's battle value, breaks excluded),
        /// the chance each side has broken by the end, the first turn any lane is in contact (-1
        /// for none), and the rounds each side has left at that turn and at the end (infinite when
        /// not counted).
        /// </summary>
        internal readonly record struct Result(
            float EnemyValueRemoved,
            float OwnValueLost,
            float EnemyCasualties = 0,
            float OwnCasualties = 0,
            float EnemyBreakChance = 0,
            float OwnBreakChance = 0,
            int ContactTurn = -1,
            float OwnRoundsAtContact = float.PositiveInfinity,
            float EnemyRoundsAtContact = float.PositiveInfinity,
            float OwnRoundsLeft = float.PositiveInfinity,
            float EnemyRoundsLeft = float.PositiveInfinity);

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
            float EnemyHazard,
            float OwnRounds = float.PositiveInfinity,
            float EnemyRounds = float.PositiveInfinity);

        /// <summary>
        /// The spread of the exchange's luck <see cref="RunExpected"/> averages over: the side
        /// points scale this squad's fire by <c>e^s</c> and <c>e^-s</c> (and the enemy's the other
        /// way). Calibrated against battles (Design/Reference/ForecastCalibration.md section 3); a static field
        /// rather than a constant so the diagnostics can sweep it. 0 turns the smoothing off.
        /// </summary>
        internal static float LuckSpread = 0.5f;

        // Below this break chance and with no contact in the central forecast, nothing in it can
        // flip with a little luck, so the side points are skipped.
        private const float LuckSensitiveBreakChance = 0.01f;

        /// <summary>
        /// The forecast's expected value over the exchange's luck (2026-10-23,
        /// Design/Reference/ForecastCalibration.md section 3): the luck-free forecast and, where a break or
        /// contact is in reach, the forecasts at luck <c>-s</c> and <c>+s</c>, weighted 1/6, 2/3,
        /// 1/6 (three-point Gauss-Hermite for a normal luck of spread <c>s / sqrt 3</c>).
        ///
        /// <para>WHY. <see cref="Run"/> follows one mean path, and on it a race decides most of the
        /// value at once: the enemy breaks (its hazard goes from 0 to 1 within a turn or two, and the
        /// broken lane's remaining value leaves at once) or it reaches contact first. A few yards
        /// flipped the race, and the potential jumped by 20-30 battle value across them: PDF squads
        /// ran at charging Hormagaunts on the strength of a jump. The battles give either outcome
        /// some of the time, because real fire is lumpy. Each luck point flips at a different
        /// separation, so the average is a ramp: a break, contact, the last rounds alike.</para>
        /// </summary>
        internal static Result RunExpected(
            float ownPool,
            SquadBreakProfile own,
            IReadOnlyList<ExchangeLane> lanes,
            float horizonTurns,
            float turnDiscount = TurnDiscount,
            float ownRounds = float.PositiveInfinity,
            bool levelBreaks = false,
            float? luckSpread = null)
        {
            Result central = Run(
                ownPool, own, lanes, horizonTurns, turnDiscount, null, ownRounds, levelBreaks);
            float spread = luckSpread ?? LuckSpread;
            if (spread <= 0f
                || (central.ContactTurn < 0
                    && central.EnemyBreakChance < LuckSensitiveBreakChance
                    && central.OwnBreakChance < LuckSensitiveBreakChance))
            {
                return central;
            }
            Result unlucky = Run(
                ownPool, own, lanes, horizonTurns, turnDiscount, null, ownRounds, levelBreaks, -spread);
            Result lucky = Run(
                ownPool, own, lanes, horizonTurns, turnDiscount, null, ownRounds, levelBreaks, spread);
            const float side = 1f / 6f;
            const float middle = 2f / 3f;
            float Mix(float low, float mid, float high) =>
                (side * low) + (middle * mid) + (side * high);
            return new Result(
                Mix(unlucky.EnemyValueRemoved, central.EnemyValueRemoved, lucky.EnemyValueRemoved),
                Mix(unlucky.OwnValueLost, central.OwnValueLost, lucky.OwnValueLost),
                Mix(unlucky.EnemyCasualties, central.EnemyCasualties, lucky.EnemyCasualties),
                Mix(unlucky.OwnCasualties, central.OwnCasualties, lucky.OwnCasualties),
                Mix(unlucky.EnemyBreakChance, central.EnemyBreakChance, lucky.EnemyBreakChance),
                Mix(unlucky.OwnBreakChance, central.OwnBreakChance, lucky.OwnBreakChance),
                // When contact comes and the rounds left are events of one path, not values: a
                // side point may never reach contact at all. The central path's.
                central.ContactTurn,
                central.OwnRoundsAtContact,
                central.EnemyRoundsAtContact,
                central.OwnRoundsLeft,
                central.EnemyRoundsLeft);
        }

        /// <param name="ownRounds">
        /// This squad's ammunition, shared by all its lanes (infinite: not counted). With a finite
        /// budget on either side, each turn's ranged fire is scaled by the share of the rounds it
        /// needs that are still there, so a side that has spent its rounds arrives at contact with
        /// nothing to shoot. Melee spends nothing.
        /// </param>
        /// <param name="levelBreaks">
        /// Breaks as a level, not a per-turn hazard: the chance a side has broken by a turn is the
        /// chance its realized losses put the check over the rout bar then
        /// (<see cref="BreakChanceAtLevel"/>), and it only ever rises. The default per-turn hazard
        /// treats every turn's check as a fresh, independent chance on the mean path, so a squad
        /// near the bar for a few turns breaks with near certainty, and it has no scatter in the
        /// losses themselves. Hormagaunts that hold the field in 56-69% of battles at 100-200 yards
        /// broke in 100% of forecasts (2026-10-23). Used by the opening forecast; the engagement
        /// potential keeps the hazard.
        /// </param>
        /// <param name="luck">
        /// The exchange's luck, for <see cref="RunExpected"/>: this squad's fire and melee are
        /// scaled by <c>e^luck</c> and the enemy's by <c>e^-luck</c> (rounds spent are not). 0 is
        /// the forecast as it was, to the bit.
        /// </param>
        internal static Result Run(
            float ownPool,
            SquadBreakProfile own,
            IReadOnlyList<ExchangeLane> lanes,
            float horizonTurns,
            float turnDiscount = TurnDiscount,
            Action<TurnTrace> trace = null,
            float ownRounds = float.PositiveInfinity,
            bool levelBreaks = false,
            float luck = 0f)
        {
            if (lanes == null || lanes.Count == 0 || ownPool <= 0
                || !float.IsFinite(horizonTurns) || horizonTurns <= 0)
            {
                return default;
            }
            float outgoingLuck = luck == 0f ? 1f : MathF.Exp(luck);
            float incomingLuck = luck == 0f ? 1f : MathF.Exp(-luck);

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
            StepResult[] steps = new StepResult[count];
            bool[] active = new bool[count];
            float[] enemyRounds = new float[count];
            // Each side's chance of failing last turn's check, for a mob leader's coercion.
            float[] previousCheckFails = new float[count];
            float ownPreviousCheckFails = 0f;
            for (int index = 0; index < count; index++)
            {
                enemyRounds[index] = lanes[index].EnemyRounds;
            }
            float enemyCasualties = 0;
            float ownCasualties = 0;
            int contactTurn = -1;
            float ownRoundsAtContact = float.PositiveInfinity;
            float enemyRoundsAtContact = float.PositiveInfinity;
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
                // The turn's geometry first, then its ammunition: this squad's rounds are shared
                // by its lanes, so what it can fire depends on all of them.
                float ownRoundsNeeded = 0;
                bool contactThisTurn = false;
                for (int index = 0; index < count; index++)
                {
                    active[index] = remaining[index] > FlowEpsilon
                        && survival[index] >= SurvivalEpsilon;
                    if (!active[index])
                    {
                        continue;
                    }
                    steps[index] = Step(lanes[index], range[index]);
                    contactThisTurn |= steps[index].Contact > 0;
                    ownRoundsNeeded += ownShare * steps[index].OutRounds * weight;
                }
                if (contactThisTurn && contactTurn < 0)
                {
                    contactTurn = turn;
                    ownRoundsAtContact = ownRounds;
                    enemyRoundsAtContact = SumRounds(enemyRounds);
                }
                float ownFireShare = AvailableShare(ownRounds, ownRoundsNeeded);
                ownRounds = Spend(ownRounds, ownFireShare * ownRoundsNeeded);

                for (int index = 0; index < count; index++)
                {
                    outgoing[index] = 0;
                    if (!active[index])
                    {
                        continue;
                    }
                    ExchangeLane lane = lanes[index];
                    StepResult step = steps[index];
                    // A share of this enemy has (in expectation) already broken; its fire went
                    // with it.
                    float enemyShare = survival[index] * (remaining[index] / lane.EnemyPool);
                    float enemyRoundsNeeded = enemyShare * step.InRounds * weight;
                    float enemyFireShare = AvailableShare(enemyRounds[index], enemyRoundsNeeded);
                    enemyRounds[index] = Spend(
                        enemyRounds[index], enemyFireShare * enemyRoundsNeeded);
                    float outRate = (step.RangedOutgoing * ownFireShare + step.ContactOutgoing)
                        * outgoingLuck;
                    float inRate = (step.RangedIncoming * enemyFireShare + step.ContactIncoming)
                        * incomingLuck;
                    traceOutRate += outRate;
                    traceInRate += inRate;
                    rangeMoved |= Math.Abs(step.NextRange - range[index]) > RangeEpsilon;
                    range[index] = step.NextRange;

                    outgoing[index] = Math.Min(remaining[index], ownShare * outRate * weight);
                    outgoingTotal += outgoing[index];
                    enemyCasualties += ownSurvival * survival[index] * outgoing[index];
                    incoming += enemyShare * inRate * weight;
                }

                float ownLoss = Math.Min(ownRemaining, incoming);
                float ownBefore = ownRemaining;
                ownRemaining -= ownLoss;
                if (RoundsLeaveWithSoldiers && ownBefore > 0)
                {
                    ownRounds = ScaleRounds(ownRounds, ownRemaining / ownBefore);
                }
                lost += discount * ownSurvival * ownLoss;
                ownCasualties += ownSurvival * ownLoss;
                float lanesBefore = 0;
                float lanesAfter = 0;
                for (int index = 0; index < count; index++)
                {
                    float laneRemainingBefore = remaining[index];
                    lanesBefore += remaining[index];
                    remaining[index] -= outgoing[index];
                    lanesAfter += remaining[index];
                    if (RoundsLeaveWithSoldiers && laneRemainingBefore > 0)
                    {
                        enemyRounds[index] = ScaleRounds(
                            enemyRounds[index], remaining[index] / laneRemainingBefore);
                    }
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
                    float hazard;
                    float routing = RoutingFriends(own, ownSurvival);
                    if (levelBreaks)
                    {
                        hazard = LevelHazard(ownSurvival, BreakChanceAtLevel(
                            own, ownLoss / ownBefore, ownCumulative, disadvantage, routing));
                    }
                    else
                    {
                        float checkFails = BreakHazard(
                            own,
                            ownLoss / ownBefore,
                            ownCumulative,
                            disadvantage,
                            routing);
                        hazard = Coerced(own, checkFails, ownPreviousCheckFails, ownCumulative);
                        ownPreviousCheckFails = checkFails;
                    }
                    lost += discount * ownSurvival * hazard * ownRemaining;
                    ownSurvival *= 1 - hazard;
                    traceOwnHazard = hazard;
                }
                else
                {
                    // No casualties, no check: a coerced rout does not carry over.
                    ownPreviousCheckFails = 0f;
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
                        float hazard;
                        float routing = RoutingFriends(lane.EnemyBreak, survival[index]);
                        if (levelBreaks)
                        {
                            hazard = LevelHazard(survival[index], BreakChanceAtLevel(
                                lane.EnemyBreak, outgoing[index] / laneBefore, laneCumulative,
                                disadvantage, routing));
                        }
                        else
                        {
                            float checkFails = BreakHazard(
                                lane.EnemyBreak,
                                outgoing[index] / laneBefore,
                                laneCumulative,
                                disadvantage,
                                routing);
                            hazard = Coerced(
                                lane.EnemyBreak, checkFails, previousCheckFails[index], laneCumulative);
                            previousCheckFails[index] = checkFails;
                        }
                        removed += discount * ownSurvival * survival[index] * hazard
                            * remaining[index];
                        survival[index] *= 1 - hazard;
                        traceEnemyHazard = Math.Max(traceEnemyHazard, hazard);
                    }
                    else
                    {
                        previousCheckFails[index] = 0f;
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
                        traceEnemyHazard, ownRounds, SumRounds(enemyRounds)));
                }
            }

            float enemyKept = 0;
            for (int index = 0; index < count; index++)
            {
                enemyKept += lanesPool > 0
                    ? survival[index] * Math.Max(0, lanes[index].EnemyPool) / lanesPool
                    : 1f / count;
            }
            return new Result(
                removed,
                lost,
                enemyCasualties,
                ownCasualties,
                Math.Clamp(1f - enemyKept, 0f, 1f),
                Math.Clamp(1f - ownSurvival, 0f, 1f),
                contactTurn,
                ownRoundsAtContact,
                enemyRoundsAtContact,
                ownRounds,
                SumRounds(enemyRounds));
        }

        /// <summary>
        /// The share of the rounds a turn's fire needs that are still there: 1 while the supply
        /// lasts (and always for a supply that is not counted), the remainder's share once it runs
        /// short, 0 when it is gone.
        /// </summary>
        private static float AvailableShare(float rounds, float needed) =>
            needed <= FlowEpsilon || float.IsPositiveInfinity(rounds)
                ? 1f
                : Math.Clamp(rounds / needed, 0f, 1f);

        private static float Spend(float rounds, float spent) =>
            float.IsPositiveInfinity(rounds) ? rounds : Math.Max(0f, rounds - spent);

        /// <summary>
        /// A dead soldier's rounds go with him: each side's ammunition falls with its remaining
        /// value, so the survivors keep only their own. Without it (as before 2026-11-09) the dead
        /// men's rounds stayed in the budget while their fire left, and the survivors never ran
        /// dry: 4 Brood Brother squads against 1 Warrior squad at 195 yd were forecast to kill all
        /// nine Warriors by turn 80 with rounds to spare, and in the battle every man had fired
        /// all 320 of his rounds by turn 85 with one Warrior alive, which then routed all four.
        /// Switch: DUEL_ROUNDS_WITH_SOLDIERS=0.
        /// </summary>
        internal static bool RoundsLeaveWithSoldiers = true;

        private static float ScaleRounds(float rounds, float share) =>
            float.IsPositiveInfinity(rounds) ? rounds : rounds * Math.Clamp(share, 0f, 1f);

        private static float SumRounds(float[] rounds)
        {
            float total = 0;
            for (int index = 0; index < rounds.Length; index++)
            {
                total += rounds[index];
            }
            return total;
        }

        /// <summary>
        /// One turn of one lane: the ranged rates (before the ammunition share), the contact part
        /// (already weighted by the share of the turn spent in contact), the rounds the ranged fire
        /// needs, the contact share itself and the separation at the turn's end.
        /// </summary>
        private readonly record struct StepResult(
            float RangedOutgoing,
            float RangedIncoming,
            float ContactOutgoing,
            float ContactIncoming,
            float OutRounds,
            float InRounds,
            float Contact,
            float NextRange);

        /// <summary>
        /// One turn of a lane's range path and the separation at its end. The turn is split into
        /// the share spent moving (rates at the turn's starting separation), standing (rates where
        /// the squad stands: the arrival point in a turn in which it arrives), and in contact (all
        /// of a turn that starts in the band, the rest of the turn after arrival in one that
        /// reaches it). The range rates of a lane with contact in it are ranged only; contact is
        /// priced by <see cref="ExchangeLane.ContactOutgoing"/> and
        /// <see cref="ExchangeLane.ContactIncoming"/>.
        /// </summary>
        private static StepResult Step(
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
            (float movingOut, float movingIn, float movingOutRounds, float movingInRounds) =
                movingShare > 0
                    ? lane.Moving.AverageAll(range, movingEnd)
                    : (0f, 0f, 0f, 0f);
            (float standingOut, float standingIn, float standingOutRounds, float standingInRounds) =
                standingShare <= 0
                    ? (0f, 0f, 0f, 0f)
                    : moving > 0
                        ? lane.Standing.AtAll(next)
                        : lane.Standing.AverageAll(range, next);
            return new StepResult(
                movingShare * movingOut + standingShare * standingOut,
                movingShare * movingIn + standingShare * standingIn,
                contact * lane.ContactOutgoing,
                contact * lane.ContactIncoming,
                movingShare * movingOutRounds + standingShare * standingOutRounds,
                movingShare * movingInRounds + standingShare * standingInRounds,
                contact,
                next);
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
            float forceDisadvantage,
            float routingFriends = 0f)
        {
            if (profile == null || !profile.CanBreak)
            {
                return 0;
            }

            (float failFraction, float margin) = FailMargin(
                profile, casualtyFractionThisTurn, cumulativeLossOfCurrentPool, forceDisadvantage, routingFriends);
            // The realized fail fraction of n soldiers scatters about its expectation by
            // sqrt(F(1-F)/n): that scatter, not a hand-set band, is the width. It vanishes as F
            // goes to 0, so a squad nobody in which would fail does not break.
            float width = (float)Math.Sqrt(
                Math.Max(failFraction * (1 - failFraction), 0.0001f)
                / Math.Max(1, profile.AbleSoldiers));
            return BattleMoraleEvaluator.NormalCdf(margin / width);
        }

        // Half-width of the central difference that reads how fast the check's margin moves with
        // the cumulative loss.
        private const float LossSlopeStep = 0.01f;

        /// <summary>
        /// The chance that a squad whose EXPECTED losses are these is over the rout bar: the
        /// check's margin (expected fail fraction less the bar, as <see cref="BreakHazard"/> reads
        /// them) against two scatters, the roll's (sqrt(F(1-F)/n), as there) and the losses'
        /// own. Which soldiers a volley takes is a draw, so the realized loss of n soldiers
        /// scatters about its expectation by sqrt(L(1-L)/n), and the margin moves with it at its
        /// slope in L. See <c>levelBreaks</c> on <see cref="Run"/>.
        /// </summary>
        internal static float BreakChanceAtLevel(
            SquadBreakProfile profile,
            float casualtyFractionThisTurn,
            float cumulativeLossOfCurrentPool,
            float forceDisadvantage,
            float routingFriends = 0f)
        {
            if (profile == null || !profile.CanBreak)
            {
                return 0;
            }
            float loss = Math.Clamp(cumulativeLossOfCurrentPool, 0, 1);
            (float fail, float margin) = FailMargin(
                profile, casualtyFractionThisTurn, loss, forceDisadvantage, routingFriends);
            float lower = Math.Max(0, loss - LossSlopeStep);
            float upper = Math.Min(1, loss + LossSlopeStep);
            float slope = upper > lower
                ? (FailMargin(profile, casualtyFractionThisTurn, upper, forceDisadvantage, routingFriends).Margin
                    - FailMargin(profile, casualtyFractionThisTurn, lower, forceDisadvantage, routingFriends).Margin)
                    / (upper - lower)
                : 0f;
            float soldiers = Math.Max(1, profile.AbleSoldiers);
            float lossScatter = slope * (float)Math.Sqrt(loss * (1 - loss) / soldiers);
            float width = (float)Math.Sqrt(
                (Math.Max(fail * (1 - fail), 0.0001f) / soldiers) + (lossScatter * lossScatter));
            return BattleMoraleEvaluator.NormalCdf(margin / width);
        }

        /// <summary>
        /// The expected fail fraction and its margin over the rout bar, exactly as
        /// <see cref="BreakHazard"/> computes them.
        /// </summary>
        internal static (float Fail, float Margin) FailMargin(
            SquadBreakProfile profile,
            float casualtyFractionThisTurn,
            float cumulativeLossOfCurrentPool,
            float forceDisadvantage,
            float routingFriends = 0f)
        {
            float priorKept = 1 - Math.Clamp(profile.PriorLossFraction, 0, 1);
            float cumulative = 1 - priorKept * (1 - Math.Clamp(cumulativeLossOfCurrentPool, 0, 1));
            // A soldier lost is the leader with probability equal to the share of the squad lost.
            float leaderLost = profile.LeaderAlreadyLost
                ? 1f
                : profile.HasLeader ? Math.Clamp(cumulativeLossOfCurrentPool, 0, 1) : 0f;
            float shock = MoraleConstants.ShockCasualtyThisTurnWeight
                    * Math.Clamp(casualtyFractionThisTurn, 0, 1)
                + MoraleConstants.ShockCumulativeCasualtyWeight * cumulative
                + MoraleConstants.ShockLeaderDeadWeight * leaderLost
                + MoraleConstants.ShockRoutingVisibleWeight * Math.Clamp(routingFriends, 0, 1);
            // As the live check: support off shock, floored at 0, before the context multiplier.
            // Shock is never negative, so with no relief this is shock unchanged.
            shock = Math.Max(0f, shock - profile.ShockRelief);
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
            return (failFraction, failFraction - threshold);
        }

        /// <summary>
        /// Whether the forecast prices the shock of routing friends (2026-10-26). A static field so
        /// the diagnostics can compare (DUEL_ROUTING_CASCADE=0 is the forecast of before).
        /// </summary>
        internal static bool PriceRoutingFriends = true;

        /// <summary>
        /// The share of a squad's friends routing, for the live check's routing-friends shock
        /// (2026-10-26, Design/Reference/BattleValueCalibration.md). The forecast assumes the rest of the side fights
        /// as this squad does, so the friends not routing yet break as it does: the share is the
        /// squad's own chance to have broken by now, on top of those routing at the start. Until
        /// then the forecast left the term out, and at the same inputs as the live checks it routed
        /// PDF squads against Tyranid Warriors a third to two thirds as often (0.8% against 2.4%
        /// under a quarter's losses, 26% against 40% past half); with the live share of routing
        /// friends added it matched in every band (2.4%, 13.5% against 14.0%, 37.5% against 39.5%).
        /// A squad that walked in under fire saw a 2-9% break chance and routed every time.
        /// </summary>
        private static float RoutingFriends(SquadBreakProfile profile, float survival)
        {
            if (!PriceRoutingFriends || profile == null || !profile.HasFriends)
            {
                return 0f;
            }
            float now = Math.Clamp(profile.RoutingFriendsNow, 0f, 1f);
            return now + ((1f - now) * Math.Clamp(1f - survival, 0f, 1f));
        }

        /// <summary>
        /// The break hazard with a mob leader's coercion: he ignores a failed check unless the
        /// check before it failed too, so while he lives (expected share alive
        /// <c>1 - cumulative loss</c>, as <see cref="FailMargin"/> reads his loss) the squad breaks
        /// only on the second failure running. Without coercion this is the check's hazard. Level
        /// breaks do not apply it: a squad over the bar stays over it, so coercion only delays the
        /// break by a turn there.
        /// </summary>
        private static float Coerced(
            SquadBreakProfile profile,
            float checkFails,
            float previousCheckFails,
            float cumulativeLossOfCurrentPool)
        {
            if (profile == null || !profile.LeaderCoerces)
            {
                return checkFails;
            }
            float leaderAlive = 1f - Math.Clamp(cumulativeLossOfCurrentPool, 0, 1);
            return checkFails * ((1f - leaderAlive) + (leaderAlive * previousCheckFails));
        }

        /// <summary>
        /// The hazard that takes survival from <paramref name="survival"/> down to
        /// <c>1 - breakChance</c>, or 0 when it is already there or lower: under level breaks a
        /// squad's chance of having broken only rises.
        /// </summary>
        private static float LevelHazard(float survival, float breakChance) =>
            survival <= SurvivalEpsilon
                ? 0f
                : Math.Clamp(1f - ((1f - breakChance) / survival), 0f, 1f);

        /// <summary>
        /// Reads a squad's break profile from its able soldiers.
        /// </summary>
        /// <param name="rules">
        /// The faction morale rules, for a mob squad's support (<see cref="MobMoraleSupportEvaluator"/>)
        /// and its leader's coercion. Null prices neither. Until 2026-10-23 the forecast priced
        /// neither for anyone: Kommandos that charge through about 45% losses in the battles saw
        /// themselves break on turn 3 of a 25-yard run, held to shoot sluggas instead, and lost 94%
        /// of those battles while winning 94% from 12 yards.
        /// </param>
        internal static SquadBreakProfile ProfileOf(
            BattleSquad squad,
            IEnumerable<BattleSquad> friendlySquads,
            BattleGridManager grid,
            FactionBehaviorRulesProfile rules = null)
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
            bool mob = rules != null && FactionCapabilities.HasMobMentality(squad.Faction);
            List<BattleSquad> friends = (friendlySquads ?? [])
                // By reference: squads built outside a battle may share an id.
                .Where(other => other != null && !ReferenceEquals(other, squad) && other.AbleSoldiers.Count > 0)
                .ToList();
            int friendSoldiers = friends.Sum(other => other.AbleSoldiers.Count);
            int routingSoldiers = friends
                .Where(other => other.WithdrawalRole == WithdrawalRole.Routing)
                .Sum(other => other.AbleSoldiers.Count);
            return new SquadBreakProfile(
                checks,
                able.Count,
                mean,
                (float)Math.Sqrt(variance),
                leader != null,
                leader == null ? 0f : BattleMoraleEvaluator.EgoToResolve(leader.Soldier.Ego),
                leader == null && squad.Soldiers.Any(
                    soldier => soldier.Soldier.Template.IsSquadLeader),
                starting > 0 ? 1f - able.Count / (float)starting : 0f,
                mob ? MobSupport(squad, friendlySquads, grid, rules) : 0f,
                mob && leader != null,
                friendSoldiers > 0,
                friendSoldiers > 0 ? routingSoldiers / (float)friendSoldiers : 0f);
        }

        /// <summary>
        /// The live check's mob support for this squad now (<see cref="MobMoraleSupportEvaluator"/>),
        /// held for the forecast. Before placement every friendly squad counts as in sight, whole,
        /// and close: the support a warband starts the battle with.
        /// </summary>
        private static float MobSupport(
            BattleSquad squad,
            IEnumerable<BattleSquad> friendlySquads,
            BattleGridManager grid,
            FactionBehaviorRulesProfile rules)
        {
            List<BattleSquad> friendly = (friendlySquads ?? []).Where(other => other != null).ToList();
            if (grid != null)
            {
                HashSet<int> routing = friendly
                    .Where(other => other.WithdrawalRole == WithdrawalRole.Routing)
                    .Select(other => other.Id)
                    .ToHashSet();
                return MobMoraleSupportEvaluator.ComputeSupport(
                    squad,
                    friendly.Where(other => other.WithdrawalRole != WithdrawalRole.Routing),
                    friendly,
                    grid,
                    rules,
                    other => other.MissionStartingAbleSoldierCount,
                    routing);
            }
            float support = friendly
                .Where(other => other.Id != squad.Id && other.AbleSoldiers.Count > 0)
                .Sum(other => (float)rules.MoraleNearbyMobSupport
                    + (other.SquadProvidesCommandAura ? (float)rules.MoraleLivingLeaderSupport : 0f));
            return Math.Clamp(support, -1f, (float)rules.MoraleMaximumSupport);
        }
    }
}
