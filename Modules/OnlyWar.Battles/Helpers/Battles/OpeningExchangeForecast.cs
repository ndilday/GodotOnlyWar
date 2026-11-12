using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Battles
{
    /// <summary>
    /// Settings for <see cref="OpeningExchangeForecast"/>: which curve variant feeds the lanes,
    /// whether ammunition is counted, and the horizon and discount of the exchange.
    ///
    /// <para>CALIBRATED 2026-10-24 (Design/Active/OpeningRangeRefactor.md, phase 3) against the
    /// measured approach curves. <b>No discount:</b> the opening question is who comes out ahead,
    /// and a battle's result does not depend on how long it takes; with the 0.97 discount a kill 60
    /// turns away counted a sixth, and every gunline's best opening against melee stopped at about a
    /// third of the measured one (PDF v Hormagaunts 105-139 yards against 450). <b>Planner-priced
    /// fire:</b> each round charged as the shot planner charges it, and its own held-aim rule; with
    /// rounds free, Tactical squads ran dry by 200 yards (83% left at contact in battle) and expected
    /// Hormagaunts to arrive against empty bolters. Together they put the forecast's best opening
    /// within one ladder step of the measured best for every panel side but the Carnifex, whose curve
    /// is flat.</para>
    /// </summary>
    internal sealed record OpeningForecastOptions(
        CurveFireModel FireModel = CurveFireModel.PlannerPriced,
        bool CountAmmunition = true,
        float HorizonTurns = EngagementHorizonModel.MaximumExchangeTurns,
        float TurnDiscount = 1f,
        bool LevelBreaks = true,
        OnlyWar.Domain.FactionBehaviors.FactionBehaviorRulesProfile MoraleRules = null,
        float? LuckSpread = null)
    {
        internal static readonly OpeningForecastOptions Default = new();
    }

    /// <summary>
    /// What a battle that opens at a given range would do, for one squad against an opposing
    /// force, asked BEFORE a grid exists (Design/Active/OpeningRangeRefactor.md, section 2.1). The
    /// lanes of <see cref="EngagementExchangeForecast"/> are built from the grid-free curves
    /// (<see cref="RangedEffectivenessCurve"/>, the grid-free
    /// <see cref="MeleeStrikeEstimator.EstimateContactRemovalRate(IReadOnlyCollection{BattleSoldier}, IReadOnlyCollection{BattleSoldier})"/>)
    /// once per squad pair, and a candidate opening only changes their start range, so many
    /// candidates cost one sampling.
    ///
    /// <para>WHO MOVES. Each side follows its doctrine: a contact seeker closes at its own speed
    /// and fires nothing on the way; a shooter stands and shoots. This squad's own continuation is
    /// chosen the same way, not maximized over, because the question is where this fight should
    /// start, not what is best to do from there.</para>
    ///
    /// <para>SHARES. The squad fights its <see cref="BattleEngagementFrameBuilder.ForceShare"/> of
    /// each enemy squad and takes that share of each enemy squad's fire and melee, as the opening
    /// range and the engagement potential do. Its own fire and melee are split across the enemy
    /// squads by their battle value, and not moved to another squad when one is destroyed.</para>
    ///
    /// <para>AMMUNITION. Each side carries the rounds of its weapons that run down; the forecast
    /// spends them at the rate the curve variant fires (<see cref="RangedEffectivenessCurve.SustainedAt"/>)
    /// and scales the fire by the rounds still there.</para>
    /// </summary>
    internal sealed class OpeningExchangeForecast
    {
        // Separation at which a closing contact seeker is in melee, as in EngagementPotential.
        private const float ContactRange = 1f;
        // The nearest range the ranged rates are read at; contact is priced by the contact rates.
        private const float RangedSampleFloor = 2f;
        private const int RangeSampleCount = 24;

        private readonly List<ExchangeLane> _lanes;
        private readonly float _ownPool;
        private readonly SquadBreakProfile _ownBreak;
        private readonly float _ownRounds;
        private readonly OpeningForecastOptions _options;

        /// <summary>
        /// False for a squad with no usable ranged weapon: it has nothing to stand off for, and its
        /// answer to the opening question is no preference rather than a range.
        /// </summary>
        internal bool HasRangedWeapon { get; }

        internal bool IsContactSeeking { get; }

        /// <summary>The farthest any of this squad's weapons reaches.</summary>
        internal float Reach { get; }

        internal float OwnPool => _ownPool;

        /// <summary>The opposing force's battle value this squad plans against (its share).</summary>
        internal float EnemyPool { get; }

        internal float OwnRounds => _ownRounds;

        internal float EnemyRounds { get; }

        internal IReadOnlyList<ExchangeLane> Lanes => _lanes;

        private OpeningExchangeForecast(
            List<ExchangeLane> lanes,
            float ownPool,
            SquadBreakProfile ownBreak,
            float ownRounds,
            OpeningForecastOptions options,
            bool hasRangedWeapon,
            bool isContactSeeking,
            float reach)
        {
            _lanes = lanes;
            _ownPool = ownPool;
            _ownBreak = ownBreak;
            _ownRounds = ownRounds;
            _options = options;
            HasRangedWeapon = hasRangedWeapon;
            IsContactSeeking = isContactSeeking;
            Reach = reach;
            EnemyPool = lanes.Sum(lane => lane.EnemyPool);
            EnemyRounds = lanes.Sum(lane => lane.EnemyRounds);
        }

        /// <summary>
        /// Samples the lanes for <paramref name="squad"/> against <paramref name="opposingSquads"/>.
        /// Null when either side has no able soldiers.
        /// </summary>
        /// <param name="friendlySquads">
        /// The squad's whole force, itself included or not (<see cref="BattleEngagementFrameBuilder.ForceShare"/>).
        /// Null or empty means it fights alone.
        /// </param>
        internal static OpeningExchangeForecast Build(
            BattleSquad squad,
            IReadOnlyCollection<BattleSquad> opposingSquads,
            IReadOnlyCollection<BattleSquad> friendlySquads = null,
            OpeningForecastOptions options = null)
        {
            options ??= OpeningForecastOptions.Default;
            List<BattleSoldier> ours = (squad?.AbleSoldiers ?? [])
                .OrderBy(soldier => soldier.Soldier.Id)
                .ToList();
            List<BattleSquad> enemies = (opposingSquads ?? [])
                .Where(enemy => enemy != null && enemy.AbleSoldiers.Count > 0)
                .OrderBy(enemy => enemy.Id)
                .ToList();
            if (ours.Count == 0 || enemies.Count == 0)
            {
                return null;
            }
            // Before the first planning pass no soldier has a tactical value yet.
            TacticalValueTable.EnsureAssigned(
                (friendlySquads ?? []).Append(squad).Distinct().ToList(),
                enemies);

            float forceShare = BattleEngagementFrameBuilder.ForceShare(squad, friendlySquads);
            bool ourSeeker = BattleEngagementFrameBuilder.IsContactSeekingBeforePlacement(squad);
            float ownMove = Math.Max(0f, squad.GetSquadMove());
            // A contact seeker that cannot move cannot close: it stands like anyone else.
            bool weClose = ourSeeker && ownMove > 0;
            EngagementTargetProfile us = BattleEngagementFrameBuilder.RepresentativeProfile(ours);
            float enemyValue = enemies.Sum(SquadValue);

            List<(BattleSquad Enemy, List<BattleSoldier> Soldiers,
                RangedEffectivenessCurve Outgoing, RangedEffectivenessCurve Incoming)> pairs = [];
            float reach = 0f;
            float farthest = RangedSampleFloor;
            float ownRounds = 0f;
            bool hasRangedWeapon = false;
            foreach (BattleSquad enemy in enemies)
            {
                List<BattleSoldier> theirs = enemy.AbleSoldiers
                    .OrderBy(soldier => soldier.Soldier.Id)
                    .ToList();
                RangedEffectivenessCurve outgoing = RangedEffectivenessCurve.Build(
                    ours,
                    BattleEngagementFrameBuilder.RepresentativeProfile(theirs),
                    RepresentativeSoldier(theirs));
                RangedEffectivenessCurve incoming = RangedEffectivenessCurve.Build(
                    theirs, us, RepresentativeSoldier(ours));
                hasRangedWeapon |= !outgoing.IsEmpty;
                reach = Math.Max(reach, outgoing.Reach);
                farthest = Math.Max(farthest, Math.Max(outgoing.Reach, incoming.Reach));
                // The same shooters against every enemy: the rounds are counted once.
                ownRounds = Math.Max(ownRounds, outgoing.RoundsCarried);
                pairs.Add((enemy, theirs, outgoing, incoming));
            }

            bool CanContact(BattleSquad enemy) =>
                weClose
                || (BattleEngagementFrameBuilder.IsContactSeekingBeforePlacement(enemy)
                    && enemy.GetSquadMove() > 0);
            // The enemies that can reach us share our frontage by their numbers, as in the
            // engagement potential (Design/Active/ContactRateCalibration.md).
            // Keyed by the squad itself: squads built outside a battle may share an id.
            Dictionary<BattleSquad, MeleeStrikeEstimator.ContactStrikes> incomingStrikes = pairs
                .Where(pair => CanContact(pair.Enemy))
                .ToDictionary(
                    pair => pair.Enemy,
                    pair => MeleeStrikeEstimator.EstimateContactStrikes(pair.Soldiers, ours));
            float attackersInReach = incomingStrikes.Values.Sum(strikes => (float)strikes.Attackers);
            float FrontageShare(MeleeStrikeEstimator.ContactStrikes strikes) => strikes.Attackers / attackersInReach;
            float incomingTakeOuts = incomingStrikes.Values.Sum(
                strikes => strikes.EngagedTakeOuts(forceShare, FrontageShare(strikes)));

            // One shooter's share of the work, as the shot planner prices his rounds
            // (RangedShotEvaluator.WorkPerShooter): the other force's value over the soldiers on his
            // side who carry a reloadable ranged weapon.
            static int Shooters(IEnumerable<BattleSquad> force) => force
                .SelectMany(member => member.AbleSoldiers)
                .Count(soldier => soldier.RangedWeapons.Any(carried => !carried.IsConsumableItem));
            IReadOnlyCollection<BattleSquad> ourForce = friendlySquads is { Count: > 0 } ? friendlySquads : [squad];
            float ourWork = enemyValue / Math.Max(1, Shooters(ourForce));
            float theirWork = ourForce.Sum(SquadValue) / Math.Max(1, Shooters(enemies));

            float[] samples = GeometricSamples(RangedSampleFloor, farthest);
            List<ExchangeLane> lanes = [];
            foreach ((BattleSquad enemy, List<BattleSoldier> theirs,
                RangedEffectivenessCurve outgoing, RangedEffectivenessCurve incoming) in pairs)
            {
                float fireShare = enemyValue > 0 ? SquadValue(enemy) / enemyValue : 1f / pairs.Count;
                bool theySeek = BattleEngagementFrameBuilder.IsContactSeekingBeforePlacement(enemy);
                float enemyApproach = theySeek ? Math.Max(0f, enemy.GetSquadMove()) : 0f;
                bool contactPossible = CanContact(enemy);
                float contactOutgoing = contactPossible
                    ? MeleeStrikeEstimator.EstimateContactStrikes(ours, theirs)
                        .RemovalRate(fireShare, defenderShare: forceShare)
                    : 0f;
                float contactIncoming = contactPossible
                    && incomingStrikes.TryGetValue(enemy, out MeleeStrikeEstimator.ContactStrikes strikes)
                    ? strikes.RemovalRate(
                        forceShare,
                        FrontageShare(strikes),
                        otherTakeOuts: incomingTakeOuts - strikes.EngagedTakeOuts(forceShare, FrontageShare(strikes)))
                    : 0f;
                contactPossible = contactOutgoing > 0 || contactIncoming > 0;

                // A contact seeker fires nothing on the way in, whichever side it is on.
                (float, float) Fire(float range, float targetSpeed) => weClose
                    ? (0f, 0f)
                    : Scale(outgoing.SustainedAt(range, options.FireModel, targetSpeed, ourWork), fireShare);
                (float, float) ReturnFire(float range, float targetSpeed) => theySeek
                    ? (0f, 0f)
                    : Scale(incoming.SustainedAt(range, options.FireModel, targetSpeed, theirWork), forceShare);

                RangeRates standing = RangeRates.Sample(
                    samples,
                    range => Fire(range, enemyApproach),
                    range => ReturnFire(range, 0f));
                RangeRates moving = weClose
                    ? RangeRates.Sample(
                        samples,
                        range => Fire(range, enemyApproach),
                        // The engine adds a moving target's speed to the range in the hit modifier.
                        range => ReturnFire(range, ownMove))
                    : RangeRates.None;
                lanes.Add(new ExchangeLane(
                    SquadValue(enemy) * forceShare,
                    EngagementExchangeForecast.ProfileOf(enemy, opposingSquads, null, options.MoraleRules),
                    StartRange: 0f,
                    Moves: weClose,
                    StopRange: weClose ? ContactRange : 0f,
                    MovingClosing: ownMove + enemyApproach,
                    StandingClosing: enemyApproach,
                    MaximumRange: 0f,
                    Moving: moving,
                    Standing: standing,
                    ContactPossible: contactPossible,
                    ContactRange: ContactRange,
                    ContactOutgoing: contactOutgoing,
                    ContactIncoming: contactIncoming,
                    EnemyRounds: options.CountAmmunition && !theySeek
                        ? incoming.RoundsCarried * forceShare
                        : float.PositiveInfinity));
            }

            return new OpeningExchangeForecast(
                lanes,
                SquadValue(squad),
                EngagementExchangeForecast.ProfileOf(squad, friendlySquads, null, options.MoraleRules),
                options.CountAmmunition && !weClose ? ownRounds : float.PositiveInfinity,
                options,
                hasRangedWeapon,
                ourSeeker,
                reach);
        }

        /// <summary>The forecast of a battle that opens at <paramref name="openingRange"/>.</summary>
        internal EngagementExchangeForecast.Result Evaluate(
            float openingRange,
            Action<EngagementExchangeForecast.TurnTrace> trace = null)
        {
            List<ExchangeLane> lanes = LanesAt(openingRange);
            // A trace follows one path: the luck-free one. The value is the expectation over luck.
            return trace != null
                ? EngagementExchangeForecast.Run(
                    _ownPool,
                    _ownBreak,
                    lanes,
                    _options.HorizonTurns,
                    _options.TurnDiscount,
                    trace,
                    _ownRounds,
                    _options.LevelBreaks)
                : EngagementExchangeForecast.RunExpected(
                    _ownPool,
                    _ownBreak,
                    lanes,
                    _options.HorizonTurns,
                    _options.TurnDiscount,
                    _ownRounds,
                    _options.LevelBreaks,
                    _options.LuckSpread);
        }

        /// <summary>
        /// The forecast of a battle that opens at <paramref name="openingRange"/> on its unlucky
        /// path: this squad's fire scaled down and the enemy's up by the luck spread, the low point
        /// of the average <see cref="Evaluate"/> takes. It prices the risk of an opening.
        /// </summary>
        internal EngagementExchangeForecast.Result EvaluateUnlucky(float openingRange) =>
            EngagementExchangeForecast.Run(
                _ownPool,
                _ownBreak,
                LanesAt(openingRange),
                _options.HorizonTurns,
                _options.TurnDiscount,
                null,
                _ownRounds,
                _options.LevelBreaks,
                -(_options.LuckSpread ?? EngagementExchangeForecast.LuckSpread));

        private List<ExchangeLane> LanesAt(float openingRange)
        {
            float start = Math.Max(0f, openingRange);
            List<ExchangeLane> lanes = new(_lanes.Count);
            foreach (ExchangeLane lane in _lanes)
            {
                lanes.Add(lane with
                {
                    StartRange = start,
                    StopRange = lane.Moves ? lane.StopRange : start,
                    MaximumRange = start,
                });
            }
            return lanes;
        }

        /// <summary>
        /// The value of a forecast: enemy tactical value removed less its own lost, breaks
        /// included. Both sides are priced by one rule from one state, so there is one currency.
        /// </summary>
        internal static float Net(EngagementExchangeForecast.Result result) =>
            result.EnemyValueRemoved - result.OwnValueLost;

        private static (float, float) Scale((float Removal, float Rounds) rate, float share) =>
            (rate.Removal * share, rate.Rounds * share);

        /// <summary>
        /// The soldier whose hit locations price a hit on this squad: the first (by id) of its
        /// most numerous soldier template, so a squad of troops and one leader is priced as troops.
        /// </summary>
        private static BattleSoldier RepresentativeSoldier(IReadOnlyList<BattleSoldier> soldiers) =>
            soldiers
                .GroupBy(soldier => soldier.Soldier.Template.Id)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .Select(group => group.First())
                .FirstOrDefault();

        private static float SquadValue(BattleSquad squad) => squad.AbleSoldiers
            .Sum(soldier => Math.Max(TacticalValueTable.MinimumValue, soldier.PlanningValue));

        /// <summary>
        /// Geometric, as in <c>EngagementPotential.RangeSamples</c>: fire climbs steeply in the last
        /// yards and the rates are linear between samples. One more sample just past the farthest
        /// reach reads zero there, and the rates hold that zero beyond it.
        /// </summary>
        private static float[] GeometricSamples(float low, float high)
        {
            high = Math.Max(low + 1f, high);
            float[] samples = new float[RangeSampleCount + 1];
            for (int index = 0; index < RangeSampleCount; index++)
            {
                samples[index] = low * MathF.Pow(high / low, index / (float)(RangeSampleCount - 1));
            }
            samples[RangeSampleCount] = high + 1f;
            return samples;
        }
    }
}
