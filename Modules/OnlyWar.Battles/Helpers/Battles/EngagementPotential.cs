using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Battles.Models;
using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Soldiers;

namespace OnlyWar.Battles
{
    /// <summary>
    /// The state value used by engagement posture scoring. The value is deliberately a function of
    /// state only: there is no engagement option kind in this type or in <see cref="Evaluate"/>.
    /// A candidate supplies the state it would leave behind -- projected geometry and the pure
    /// action descriptors are enough to value that state without mutating the live battle.
    /// </summary>
    internal sealed class EngagementPotential
    {
        private const float ContactSeekerRangedRelevanceFraction = 0.02f;

        /// <summary>
        /// How far beyond the range it wants a squad must be for the pursuit contact-progress
        /// potential to fall to half of <c>attainable</c>, in strides of its own move. Ten keeps
        /// the gradient perceptible over a long approach without letting position value rival the
        /// exchange terms at the ranges where the squad is already shooting. It is a floor under
        /// the band width, so a squad with a wide preferred band uses the band instead.
        /// </summary>
        internal const float ContactProgressHalfValueStrides = 10f;

        private static bool IsPursuitRole(EngagementSquadRole role) =>
            role is EngagementSquadRole.Pursuit
                or EngagementSquadRole.Follow
                or EngagementSquadRole.Press;

        private static bool IsFirePreservingPursuitRole(EngagementSquadRole role) =>
            role is EngagementSquadRole.Pursuit or EngagementSquadRole.Follow;

        private readonly BattleGridManager _grid;
        private readonly RangedTargetSelector _ranged;
        private readonly EngagementExchangeModel _exchange;
        private readonly BaseSkill _tacticsSkill;
        private readonly BattlePlanningContext _context;

        internal EngagementPotential(
            BattleGridManager grid,
            RangedTargetSelector ranged,
            EngagementExchangeModel exchange,
            BaseSkill tacticsSkill = null,
            BattlePlanningContext context = null)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _ranged = ranged ?? throw new ArgumentNullException(nameof(ranged));
            _exchange = exchange ?? throw new ArgumentNullException(nameof(exchange));
            _tacticsSkill = tacticsSkill;
            _context = context;
        }

        /// <summary>
        /// A frozen engagement state. <see cref="Actions"/> describes the pure actions that have
        /// already been projected into this state; it is not an engagement option and carries no
        /// policy decision by itself.
        ///
        /// <para>Every component of <see cref="Breakdown"/> is in tactical value, which prices both
        /// sides by one rule from one state, so there is no exchange rate between them
        /// (Design/Reference/BattleValueCalibration.md).</para>
        /// </summary>
        internal sealed record State(
            BattleSquad Squad,
            ValueTuple<float, float> Centroid,
            BattleSquadCapabilityProfile Profile,
            IReadOnlyDictionary<int, BattleSquadCapabilityProfile> Profiles,
            IReadOnlyDictionary<int, SquadEngagementFrame> Frames,
            IReadOnlyCollection<BattleSquad> Enemies,
            SquadEngagementFrame Frame,
            float FeasibleSpeed = 0,
            BattleSquad Primary = null,
            IReadOnlyList<PlannedSoldierAction> Actions = null,
            IReadOnlyCollection<BattleSquad> FriendlySquads = null,
            ValueTuple<float, float>? PrimaryCentroid = null);

        /// <summary>
        /// The auditable decomposition of Φ. Net-rate value is the coupled exchange forecast
        /// (<see cref="EngagementExchangeForecast"/>), which also prices the time a squad spends
        /// out of useful range. Readiness and screen values are stored future value of the
        /// resulting state. The role and fire-window pieces remain separate for trace readability
        /// while together forming the screen potential family.
        ///
        /// <para>There used to be an access term here, a per-turn charge of a fifth of the
        /// target's battle value for each turn spent unable to contribute. The exchange already
        /// prices that delay -- weak fire while out of range and a shorter standing phase inside
        /// the horizon -- so the term counted it twice, at a size the BV ratio between the sides
        /// set. It was removed on 2026-10-06 when the exchange became coupled; the forecast's
        /// standing continuation (shoot from here, aiming) took over the case it was written for,
        /// a squad that reads as idle where it stands.</para>
        /// </summary>
        /// <para><see cref="CloseAdvantage"/> is how much the Close continuation beats Stay in this
        /// state (0 when it does not). It is not part of <see cref="Total"/>: it tells the option
        /// selector that there is somewhere more useful to be, so a small but steady gain from
        /// moving there is a decision and not noise.</para>
        /// <para><see cref="CloseRemovesMore"/> is whether the Close continuation removes more
        /// enemy battle value than Stay. A positive CloseAdvantage alone can come from losing the
        /// same squad later: the forecast discounts later turns, so a run that only delays a rout
        /// scores above a firefight that loses the squad sooner (60 Kommandos running at 60 PDF,
        /// 2026-10-20: Close removed nothing). The commitment mask reads both.</para>
        internal readonly record struct Breakdown(
            float NetRateValue,
            float ReadinessValue,
            float RoleValue,
            float FireWindowValue,
            float MoraleValue,
            float CommandValue,
            float CloseAdvantage = 0,
            bool CloseRemovesMore = false,
            bool StayIsInert = false,
            bool NoWayToWin = false,
            EngagementOptionKind? CloseMove = null)
        {
            internal float ScreenValue => RoleValue + FireWindowValue;

            internal float Total =>
                NetRateValue
                + ReadinessValue
                + RoleValue
                + FireWindowValue
                + MoraleValue
                + CommandValue;
        }

        /// <summary>
        /// Evaluates Φ for one frozen state. This is the only entry point and intentionally has no
        /// engagement option parameter, so the root state's value is identical for every candidate
        /// in one decision.
        /// </summary>
        internal Breakdown Evaluate(State state)
        {
            ArgumentNullException.ThrowIfNull(state);
            (float exchange, float closeAdvantage, bool closeRemovesMore, bool stayIsInert, bool noWayToWin,
                EngagementOptionKind? closeMove) = EvaluateExchangePotential(state);
            return new Breakdown(
                exchange,
                EvaluateReadiness(state),
                EvaluateScreenRole(state)
                    + EvaluatePursuitContactProgress(state),
                // Shots at the quarry, in the quarry's value.
                EvaluateFireWindow(state),
                EvaluateMorale(state),
                EvaluateCommandAura(state),
                closeAdvantage,
                closeRemovesMore,
                stayIsInert,
                noWayToWin,
                closeMove);
        }

        internal static float ScoreTransition(
            float immediateExchange,
            Breakdown root,
            Breakdown projected) =>
            immediateExchange
            + EngagementPotentialDiscount * projected.Total
            - root.Total;

        // Φ is already a value function over the expected exchange horizon. The transition is a
        // potential difference, not a one-ply rollout, so applying the old 0.65 rollout discount
        // here would discount the same time preference twice. (The bounded rollout that discount
        // was tuned for has been removed.) EngagementExchangeModel.EngagementFutureDiscount is
        // still used per turn of delay in the fire-window projection below, where a shot genuinely
        // lands one or more turns in the future.
        internal const float EngagementPotentialDiscount = 1f;

        /// <summary>
        /// The net exchange of the state, in the squad's own currency: the better of two
        /// continuations from here, each forecast with both sides' losses coupled and both sides
        /// able to break (<see cref="EngagementExchangeForecast"/>).
        ///
        /// <para><b>Stay</b>: stand where the state leaves the squad and shoot, aiming, for the
        /// whole horizon. <b>Close</b>: move to the range the squad wants against each enemy --
        /// firing nothing on the way and shot at as a moving target -- then stand and shoot. The
        /// value is a function of the state alone (it does not know which option produced it), so
        /// the max over continuations is what lets it assume the squad acts sensibly afterwards. A
        /// hold differs from a run only in its immediate term and in the state it leaves: a run
        /// gives up this turn's fire and is shot on the move, and in return its Close continuation
        /// is shorter. It wins only when that is worth the turn.</para>
        ///
        /// <para>Each continuation follows the separation as it changes (see
        /// <see cref="ExchangeLane"/>): an enemy contact seeker charging through our fire band, a
        /// pursuit quarry opening the range while we stand.</para>
        /// </summary>
        private (float Value, float CloseAdvantage, bool CloseRemovesMore, bool StayIsInert, bool NoWayToWin,
            EngagementOptionKind? CloseMove) EvaluateExchangePotential(
            State state)
        {
            float expectedExchangeTurns = _context?.ExpectedExchangeTurnsFor(state.Squad.Id)
                ?? EngagementHorizonModel.MaximumExchangeTurns;
            List<ExchangeLane> stay = [];
            List<ExchangeLane> close = [];
            // The same Close at a jog and at a walk, firing on the way (see ApproachFiring); a lane
            // that cannot approach that way is the run lane again.
            List<ExchangeLane> jogClose = [];
            List<ExchangeLane> walkClose = [];
            bool anyApproach = false;
            // The squad fights its share of each enemy, not all of it (2026-10-11). Each lane's
            // enemy pool is the enemy's battle value times this squad's share of its own force
            // (BattleEngagementFrameBuilder.ForceShare, the same share the opening range and the
            // effective engagement range use), so the enemy is worn down -- and its fire fades and
            // its morale breaks -- at the pace the whole force's fire would set, as if the other
            // squads fight as this one does. Until then each squad forecast the enemy worn down by
            // its own fire alone: one Insurrectionist Mob of ten saw a 200-yard run at a Tactical
            // squad as a loss, though the ten together out-shoot it at point blank, and the Mobs
            // stood and lost a 400-turn firefight. Removal is capped at the same share: the squad's
            // proportional part of the kill. A squad that fights alone has a share of 1.
            float forceShare = BattleEngagementFrameBuilder.ForceShare(state.Squad, state.FriendlySquads);
            // Melee on this squad from each enemy that can reach it, before the frontage is shared
            // out among them (see below the loop), and each lane's enemy.
            Dictionary<int, MeleeStrikeEstimator.ContactStrikes> incomingStrikes = [];
            Dictionary<int, MeleeStrikeEstimator.ContactStrikes> outgoingStrikes = [];
            List<int> stayEnemies = [];
            List<int> closeEnemies = [];
            // The close lanes whose stop may be moved out to a halfway stop (see below the loop).
            List<bool> halfwayLanes = [];
            foreach (BattleSquad enemy in (state.Enemies ?? []).OrderBy(candidate => candidate.Id))
            {
                if (!state.Profiles.TryGetValue(enemy.Id, out BattleSquadCapabilityProfile opposing)
                    || !state.Frames.ContainsKey(enemy.Id)
                    || opposing.TotalAbleBattleValue <= 0)
                {
                    continue;
                }

                float range = Math.Max(
                    0,
                    EngagementExchangeModel.Distance(
                        state.Centroid,
                        EnemyCentroid(state, enemy)));
                // A state in which the two squads' nearest soldiers are within melee reach is a state
                // in contact, whoever closed it and whatever either squad's doctrine: price it at
                // the contact rates. Measured soldier to soldier with each squad shifted to its
                // state centroid, not centroid to centroid: two blocks in contact keep their
                // centroids apart by their depth (4.8 yards for Hybrids against Boyz), and two lines
                // facing each other are wide but shallow, so no centroid allowance fits both. Not
                // against an enemy that is running away (Bound, Routing, BreakOff): it is leaving
                // the contact, not fighting it.
                bool enemyFleeing = state.Frames.GetValueOrDefault(enemy.Id)?.Role
                    is EngagementSquadRole.Bound
                        or EngagementSquadRole.Routing
                        or EngagementSquadRole.BreakOff;
                bool inContact = !enemyFleeing
                    && NearestSoldierSeparation(
                        state.Squad, state.Centroid, enemy, EnemyCentroid(state, enemy), range)
                        <= MeleeBand + BattleContactRules.MeleeContactAllowance;
                if (inContact)
                {
                    range = Math.Min(range, ContactRange);
                }
                bool firePreservingPursuit = state.Frame != null
                    && IsFirePreservingPursuitRole(state.Frame.Role)
                    && !state.Profile.IsContactSeeking;
                float desiredRange = state.Profile.IsContactSeeking
                    ? 1f
                    : firePreservingPursuit
                        ? Math.Max(1f, state.Profile.UsefulFireRange)
                        : Math.Max(1f, state.Profile.EffectiveEngagementRange);
                // Pursuit fire-support saturates at the useful band's boundary. Once it is inside
                // that band, moving still changes the live shot but does not create a new arrival
                // opportunity worth buying with a whole-battle horizon. Other doctrines retain
                // the actual-in-band destination rate so contact geometry can still be valued.
                float destinationRange = firePreservingPursuit
                    // Outside useful fire, price arrival at its boundary. Inside it, retain the
                    // actual geometry so the exchange curve -- not another shaping bounty --
                    // decides whether moving closer is worth the shot or aim it costs.
                    ? Math.Min(range, desiredRange)
                    : state.Frame != null && IsPursuitRole(state.Frame.Role)
                        ? Math.Max(1f, desiredRange)
                        : Math.Min(range, Math.Max(desiredRange, 0));
                SquadEngagementFrame opposingFrame = state.Frames.GetValueOrDefault(enemy.Id);
                // An enemy contact seeker closes on us whatever we do; a fleeing quarry opens the
                // range while we stand.
                float enemyApproach = IsApproachingContactSeeker(opposing, opposingFrame)
                    ? Math.Max(0, opposing.MoveSpeed)
                    : 0f;
                float quarryOpening = state.Frame == null
                    ? 0f
                    : EngagementExchangeModel.QuarryWithdrawalRate(state.Frame, opposingFrame?.Role);
                float standingClosing = enemyApproach - quarryOpening;
                float ownMove = Math.Max(0, state.Profile.MoveSpeed);
                bool moves = destinationRange < range - 0.01f;
                // Contact comes from either side: an enemy contact seeker charging us, our own
                // Close continuation running in to contact, or a state already in contact. Until
                // 2026-10-09 the last case was priced, between two squads neither of which seeks
                // contact, at the standing rates read at 1 yard: Neophyte Hybrids ran into Shoota
                // Boyz from 15 yards on the strength of their point-blank fire, met melee of 100 to
                // 130 battle value a turn against their own 10 to 20, and routed by turn six.
                bool contactReachable = enemyApproach > 0
                    || inContact
                    || (state.Profile.IsContactSeeking && destinationRange <= ContactRange);
                // The enemy fights this squad with its share of itself, as the lane's pool assumes.
                // Both are set again below the loop, once the frontage is shared out; these only
                // say whether there is any melee in the lane.
                float contactIncoming = 0f;
                float contactOutgoing = 0f;
                if (contactReachable)
                {
                    MeleeStrikeEstimator.ContactStrikes incomingMelee = _exchange.ContactStrikes(enemy, state.Squad);
                    MeleeStrikeEstimator.ContactStrikes outgoingMelee = _exchange.ContactStrikes(state.Squad, enemy);
                    incomingStrikes[enemy.Id] = incomingMelee;
                    outgoingStrikes[enemy.Id] = outgoingMelee;
                    contactIncoming = incomingMelee.RemovalRate(forceShare);
                    contactOutgoing = outgoingMelee.RemovalRate(defenderShare: forceShare);
                }
                bool contactPossible = contactIncoming > 0 || contactOutgoing > 0;
                // How far a quarry can open the range while it matters: the discount has spent
                // most of a turn's weight after about sixty turns.
                float maximumRange = range + Math.Max(0, -standingClosing) * 60f;
                // With contact in the lane, contact is priced by the contact rates and the range
                // rates stay ranged: sampled from outside the melee band, never at it. The
                // exchange model's rate at 1 yard IS the melee rate, and interpolating it toward
                // the next sample put melee-sized fire at 3-8 yards, so the turn a charge arrived
                // on decided whether the forecast squad broke there (Hormagaunts walked into PDF
                // fire at 30 yards, 2026-10-07).
                float minimumRange = contactPossible
                    ? Math.Min(range, RangedSampleFloor)
                    : Math.Min(range, moves ? destinationRange : range);
                IReadOnlyList<float> samples = RangeSamples(
                    minimumRange, maximumRange, range, destinationRange);

                RangeRates standing = RangeRates.Sample(
                    samples,
                    sample => _exchange.EvaluateSustainedOutgoing(
                        state.Squad, enemy, opposing, state.Frames, sample),
                    sample => _exchange.EvaluateSustainedIncoming(
                        state.Squad, enemy, state.Profile, state.Frames, sample));
                // The enemy's rounds for this lane: its carried rounds times the share of its fire
                // that falls on this squad, the allocation its incoming rate already carries.
                float enemyRounds = AmmunitionInPotential
                    ? EngagementExchangeModel.RoundsCarried(enemy)
                        * (opposingFrame?.PairWeights.GetValueOrDefault(state.Squad.Id) ?? 0f)
                    : float.PositiveInfinity;
                ExchangeLane standHere = new(
                    opposing.TotalAbleBattleValue * forceShare,
                    BreakProfileOf(enemy, state.Enemies),
                    range,
                    false,
                    range,
                    ownMove + standingClosing,
                    standingClosing,
                    maximumRange,
                    RangeRates.None,
                    standing,
                    contactPossible,
                    ContactRange,
                    contactOutgoing,
                    contactIncoming,
                    enemyRounds);
                if (standing.IsZero && !contactPossible && !moves)
                {
                    continue;
                }
                stay.Add(standHere);
                stayEnemies.Add(enemy.Id);
                closeEnemies.Add(enemy.Id);

                if (!moves)
                {
                    close.Add(standHere);
                    jogClose.Add(standHere);
                    walkClose.Add(standHere);
                    halfwayLanes.Add(false);
                    continue;
                }
                anyApproach = true;
                bool ordinaryApproach = !state.Profile.IsContactSeeking
                    && !(state.Frame != null && IsPursuitRole(state.Frame.Role))
                    && !firePreservingPursuit;
                halfwayLanes.Add(ordinaryApproach);
                ExchangeLane run = standHere with
                {
                    Moves = true,
                    StopRange = destinationRange,
                    Moving = RangeRates.Sample(
                        samples,
                        // Only a fire-preserving pursuer shoots on the run; anyone else running in
                        // fires nothing until it stands (the jog and walk approaches below fire).
                        sample => firePreservingPursuit
                            ? (_exchange.EvaluateOutgoingExchangeRate(
                                state.Squad, enemy, state.Profile, opposing, state.Frames, sample), 0f)
                            : (0f, 0f),
                        // A moving target is harder to hit: the engine adds its speed to the range
                        // in the hit modifier (BattleModifiersUtil.CalculateRangeModifier).
                        sample => _exchange.EvaluateIncoming(
                            state.Squad,
                            enemy,
                            state.Profile,
                            state.Frames,
                            sample,
                            targetSpeed: ownMove)),
                };
                close.Add(run);
                // Fire on the approach (2026-11-08, Design/Reference/BattleValueCalibration.md): a
                // squad that does not seek contact can also advance at a jog or a walk and shoot
                // as it goes, un-aimed and with the tier's bulk penalty, at the tier's speed. Until
                // then Close only ran in without firing, so Brood Brothers whose lasguns reach
                // 1,000 yards forecast a 450-yard walk under Deathspitter fire with no shot back
                // (lose 68%, break 92%) and stood 750 yards from Warriors they beat from 300.
                if (FireOnApproach && ordinaryApproach)
                {
                    jogClose.Add(ApproachFiring(
                        run, state, enemy, opposing, samples, standingClosing,
                        ownMove * SoldierMovementPlanner.JogSpeedMultiplier,
                        EngagementExchangeModel.PostureBulkMultiplier(EngagementOptionKind.JogToward)));
                    walkClose.Add(ApproachFiring(
                        run, state, enemy, opposing, samples, standingClosing,
                        ownMove * SoldierMovementPlanner.WalkSpeedMultiplier,
                        EngagementExchangeModel.PostureBulkMultiplier(EngagementOptionKind.StepForward)));
                }
                else
                {
                    jogClose.Add(run);
                    walkClose.Add(run);
                }
            }

            if (stay.Count == 0)
            {
                return (0, 0, false, false, false, null);
            }

            // Frontage (Design/Reference/ForecastCalibration.md section 4). Only so many attackers fit round
            // this squad, however many enemy squads can reach it, so the enemies in reach share its
            // frontage by their numbers; and this squad's own soldiers are split over the enemies it
            // can reach by theirs, fighting its force share of each. Until 2026-10-23 each lane
            // priced the whole melee of both squads as if they fought alone: a PDF squad expected
            // three Hormagaunt broods to strike it at once (90% of it gone in the first turn of
            // contact, against 23% in battle), and a Genestealer brood struck both PDF squads in full.
            float attackersInReach = incomingStrikes.Values.Sum(strikes => (float)strikes.Attackers);
            float defendersInReach = outgoingStrikes.Values.Sum(strikes => (float)strikes.Defenders);
            // A soldier met by strikers of two broods is taken out once: each lane is credited its
            // share of the loss all of them together inflict.
            float FrontageShare(MeleeStrikeEstimator.ContactStrikes strikes) => strikes.Attackers / attackersInReach;
            float incomingTakeOuts = attackersInReach > 0
                ? incomingStrikes.Values.Sum(strikes => strikes.EngagedTakeOuts(forceShare, FrontageShare(strikes)))
                : 0f;
            void ShareFrontage(List<ExchangeLane> lanes, List<int> enemies)
            {
                for (int index = 0; index < lanes.Count; index++)
                {
                    int enemyId = enemies[index];
                    if (!incomingStrikes.TryGetValue(enemyId, out MeleeStrikeEstimator.ContactStrikes incomingMelee))
                    {
                        continue;
                    }
                    MeleeStrikeEstimator.ContactStrikes outgoingMelee = outgoingStrikes[enemyId];
                    lanes[index] = lanes[index] with
                    {
                        ContactIncoming = attackersInReach > 0
                            ? incomingMelee.RemovalRate(
                                forceShare,
                                FrontageShare(incomingMelee),
                                otherTakeOuts: incomingTakeOuts
                                    - incomingMelee.EngagedTakeOuts(forceShare, FrontageShare(incomingMelee)))
                            : 0f,
                        ContactOutgoing = defendersInReach > 0
                            ? outgoingMelee.RemovalRate(
                                outgoingMelee.Defenders / defendersInReach, defenderShare: forceShare)
                            : 0f,
                    };
                }
            }
            ShareFrontage(stay, stayEnemies);
            ShareFrontage(close, closeEnemies);
            ShareFrontage(jogClose, closeEnemies);
            ShareFrontage(walkClose, closeEnemies);

            float ownPool = Math.Max(0, state.Profile.TotalAbleBattleValue);
            // This squad's rounds, shared by all its lanes (2026-11-08): a squad whose rounds will
            // not last the horizon no longer forecasts fire it cannot pay for, standing or closing.
            // Pooled across the squad's weapons, as the opening forecast pools them.
            float ownRounds = AmmunitionInPotential
                ? EngagementExchangeModel.RoundsCarried(state.Squad)
                : float.PositiveInfinity;
            SquadBreakProfile ownBreak = BreakProfileOf(state.Squad, state.FriendlySquads);
            // The horizon is how long the exchange at a geometry lasts. Close changes the geometry
            // first, so it gets the horizon at its destination after the turns it takes to get
            // there (2026-10-23): with the travel inside the horizon, a charge from 23 yards spent
            // its whole four-turn horizon running under fire and its melee was never priced.
            float CloseHorizon(IReadOnlyList<ExchangeLane> lanes) => !CloseHorizonAddsTravel ? expectedExchangeTurns : Math.Min(
                EngagementHorizonModel.MaximumExchangeTurns,
                expectedExchangeTurns + lanes
                    .Where(lane => lane.Moves && lane.MovingClosing > 0)
                    .Select(lane => Math.Max(0f, lane.StartRange - lane.StopRange) / lane.MovingClosing)
                    .DefaultIfEmpty(0f)
                    .Max());
            float closeHorizon = CloseHorizon(close);
            EngagementExchangeForecast.Result Forecast(IReadOnlyList<ExchangeLane> lanes, float horizon) =>
                EngagementExchangeForecast.RunExpected(
                    ownPool,
                    ownBreak,
                    lanes,
                    horizon,
                    turnDiscount: TurnDiscount,
                    ownRounds: ownRounds,
                    levelBreaks: UseLevelBreaks,
                    luckSpread: LuckSpread);
            float Value(EngagementExchangeForecast.Result result) =>
                result.EnemyValueRemoved - result.OwnValueLost;

            EngagementExchangeForecast.Result stayResult = Forecast(stay, expectedExchangeTurns);
            float stayValue = Value(stayResult);
            if (!anyApproach)
            {
                TraceForecast(state, "stay", stay, ownPool, ownBreak, expectedExchangeTurns);
                ReportRootForecast(state, stay, null, stayValue, float.NaN, ownPool, ownBreak, expectedExchangeTurns, closeHorizon,
                    RootSustainedCycles(state.Squad));
                return (stayValue, 0, false, IsInert(stayResult, ownPool), HasNoWayToWin(stayResult, stayValue, ownPool), null);
            }
            EngagementExchangeForecast.Result closeResult = Forecast(close, closeHorizon);
            float closeValue = Value(closeResult);
            TraceForecast(state, "candidate-run", close, ownPool, ownBreak, closeHorizon);
            // Halfway stops (2026-10-26, Design/Reference/BattleValueCalibration.md). A squad that does not seek
            // contact wants its effective engagement range, often a few yards, where standing fire
            // is best by a hair; the Close continuation walked it all the way there without firing.
            // From 446 yards a PDF squad against Tyranid Warriors forecast arriving broken after 90
            // turns under fire (-13 against -0.4 for standing), though standing at 160 yards wins
            // nearly as much as point blank, so twenty PDF squads stood out of reach for 600 turns.
            // Close may also stop at half or a quarter of the gap and stand there; the potential is
            // the best of them, and the next turn's state asks again from nearer in.
            // Each stop is cut from the run lanes, not from the best Close so far: until 2026-11-08 a
            // half stop that beat the run became the lane the quarter stop was cut from, the quarter
            // (nearer) stop was then inside it, and the loop ended. From 732 yards Brood Brothers
            // against Warriors tried 366 yards, where lasguns do nothing, and never 183, where they win.
            List<ExchangeLane> runLanes = close;
            for (int halving = 1; halving <= CloseHalfwayStops; halving++)
            {
                float fraction = MathF.Pow(0.5f, halving);
                List<ExchangeLane> halfway = new(runLanes.Count);
                bool changed = false;
                for (int index = 0; index < runLanes.Count; index++)
                {
                    ExchangeLane lane = runLanes[index];
                    float stop = lane.StartRange * fraction;
                    if (!halfwayLanes[index] || stop <= lane.StopRange + 0.01f)
                    {
                        halfway.Add(lane);
                        continue;
                    }
                    changed = true;
                    halfway.Add(lane with { StopRange = stop });
                }
                if (!changed)
                {
                    break;
                }
                float halfwayHorizon = CloseHorizon(halfway);
                EngagementExchangeForecast.Result halfwayResult = Forecast(halfway, halfwayHorizon);
                float halfwayValue = Value(halfwayResult);
                TraceForecast(state, "candidate-halfway", halfway, ownPool, ownBreak, halfwayHorizon);
                if (halfwayValue > closeValue)
                {
                    close = halfway;
                    closeHorizon = halfwayHorizon;
                    closeResult = halfwayResult;
                    closeValue = halfwayValue;
                }
            }
            // The approaches that fire on the way, each over its own horizon (its travel takes
            // longer); Close is the best of the ways in.
            // The run and its halfway stops are run at full speed; the approaches at their tier.
            EngagementOptionKind closeMove = EngagementOptionKind.RunToward;
            foreach ((List<ExchangeLane> approach, EngagementOptionKind move) in new[]
                {
                    (jogClose, EngagementOptionKind.JogToward),
                    (walkClose, EngagementOptionKind.StepForward),
                })
            {
                if (!FireOnApproach || approach.SequenceEqual(close))
                {
                    continue;
                }
                float approachHorizon = CloseHorizon(approach);
                EngagementExchangeForecast.Result approachResult = Forecast(approach, approachHorizon);
                float approachValue = Value(approachResult);
                TraceForecast(state, "candidate-approach", approach, ownPool, ownBreak, approachHorizon);
                if (approachValue > closeValue)
                {
                    close = approach;
                    closeHorizon = approachHorizon;
                    closeResult = approachResult;
                    closeValue = approachValue;
                    closeMove = move;
                }
            }
            TraceForecast(state, "stay", stay, ownPool, ownBreak, expectedExchangeTurns);
            TraceForecast(state, "close", close, ownPool, ownBreak, closeHorizon);
            ReportRootForecast(state, stay, close, stayValue, closeValue, ownPool, ownBreak, expectedExchangeTurns, closeHorizon,
                RootSustainedCycles(state.Squad));
            return (
                Math.Max(stayValue, closeValue),
                Math.Max(0, closeValue - stayValue),
                closeResult.EnemyValueRemoved > stayResult.EnemyValueRemoved,
                IsInert(stayResult, ownPool),
                HasNoWayToWin(stayResult, closeValue, ownPool),
                FollowPlanOnlyWhenStayLoses && stayValue >= 0 ? null : closeMove);
        }

        /// <summary>
        /// A Close lane that advances at <paramref name="speed"/> and fires on the way: every soldier
        /// shoots un-aimed each turn with the movement tier's bulk penalty
        /// (<paramref name="shooterBulkMultiplier"/>), and the enemy shoots at a target moving at
        /// that speed. The rest of the lane (where it stops, contact, the enemy) is the run lane's.
        /// </summary>
        private ExchangeLane ApproachFiring(
            ExchangeLane run,
            State state,
            BattleSquad enemy,
            BattleSquadCapabilityProfile opposing,
            IReadOnlyList<float> samples,
            float standingClosing,
            float speed,
            float shooterBulkMultiplier) =>
            run with
            {
                MovingClosing = Math.Max(0f, speed) + standingClosing,
                Moving = RangeRates.Sample(
                    samples,
                    sample => _exchange.EvaluateMovingOutgoing(
                        state.Squad, enemy, opposing, sample, shooterBulkMultiplier),
                    sample => _exchange.EvaluateIncoming(
                        state.Squad, enemy, state.Profile, state.Frames, sample, targetSpeed: speed)),
            };

        /// <summary>
        /// Close may advance at a jog or a walk and fire on the way, as well as run in without
        /// firing (2026-11-08). A static field so the diagnostics can compare:
        /// DUEL_FIRE_ON_APPROACH=0 is the Close of before.
        /// </summary>
        internal static bool FireOnApproach = true;

        /// <summary>
        /// The forecast counts both sides' rounds (2026-11-08): fire stops when the rounds run out.
        /// A static field so the diagnostics can compare: DUEL_POTENTIAL_AMMO=0 is the forecast of
        /// before, with rounds free.
        /// </summary>
        internal static bool AmmunitionInPotential = true;

        /// <summary>Below this share of the squad's own pool, a forecast's exchange counts as nothing.</summary>
        private const float NegligibleExchangeShare = 0.001f;

        /// <summary>
        /// How little Stay may remove, per value it loses, and still count as doing nothing. A squad
        /// that trades badly but really hurts the enemy is fighting, not waiting to die.
        /// </summary>
        private const float NoWinRemovalPerLoss = 0.1f;

        /// <summary>Standing here, neither side hurts the other over the whole horizon.</summary>
        private static bool IsInert(EngagementExchangeForecast.Result stay, float ownPool) =>
            stay.EnemyValueRemoved + stay.OwnValueLost < NegligibleExchangeShare * ownPool;

        /// <summary>
        /// Standing here the squad is hurt and does next to nothing in return, and closing is a net
        /// loss as well: it has no continuation that wins (see <see cref="NoWayToWinWithdrawal"/>).
        /// </summary>
        private static bool HasNoWayToWin(
            EngagementExchangeForecast.Result stay,
            float closeValue,
            float ownPool) =>
            closeValue < 0
            && stay.OwnValueLost >= NegligibleExchangeShare * ownPool
            && stay.EnemyValueRemoved < NoWinRemovalPerLoss * stay.OwnValueLost;

        /// <summary>
        /// A squad whose Stay exchange is nothing, and whose Close removes more, is not offered Hold
        /// or StepBack (<see cref="SquadEngagementPolicy.ApplyStandoffCommitment"/>; adopted 2026-11-06,
        /// Design/Reference/BattleValueCalibration.md, stalemate evidence). A static field so the
        /// diagnostics can compare: DUEL_STANDOFF_CLOSE=0 turns it off.
        /// </summary>
        internal static bool StandoffClose = true;

        /// <summary>
        /// FOLLOW THE PLAN (adopted 2026-11-09, with <see cref="FollowPlanOnlyWhenStayLoses"/>). When the root Close beats Stay and removes
        /// more, the squad is offered only the option that moves as the winning Close moves
        /// (<see cref="SquadEngagementPolicy.ApplyPlanCommitment"/>). Φ changes very little per yard
        /// where nobody can hurt anybody, so a step costs about what a run costs, and the walk wins on
        /// its immediate fire: 4 Brood Brothers that forecast a win from a run to 183 yards walked
        /// at a yard a turn through 250 turns of Deathspitter fire instead. A static field so the
        /// diagnostics can compare (DUEL_FOLLOW_PLAN=0 turns it off). Needed since the rounds fix
        /// (EngagementExchangeForecast.RoundsLeaveWithSoldiers): without it 10 Mobs that forecast a
        /// narrow win from closing on a Tactical squad stood and lost 0/88; with it they won 8/8.
        /// </summary>
        internal static bool FollowClosePlan = true;

        /// <summary>
        /// The narrowed follow-the-plan rule (2026-11-09): the plan is followed only when standing
        /// is itself a loss (Stay's value below 0). Without it, PDF facing charging Hormagaunts
        /// followed a Close toward the charge and went from 62/38 to 12/88. A Close that wins
        /// narrowly over a Stay that also wins is left to the ordinary choice. The breakdown's
        /// CloseMove is null when the rule does not apply, so nothing else reads it.
        /// Switch: DUEL_FOLLOW_ANY_STAY=1 restores the unnarrowed rule.
        /// </summary>
        internal static bool FollowPlanOnlyWhenStayLoses = true;

        /// <summary>
        /// A side withdraws, whatever its aggression, when each of its squads has seen no way to win
        /// for <see cref="NoWayToWinPatienceTurns"/> turns running and it has lost the larger share
        /// (adopted 2026-11-06). A static field so the diagnostics can compare:
        /// DUEL_NO_WIN_WITHDRAW=0 turns it off.
        /// </summary>
        internal static bool NoWayToWinWithdrawal = true;

        /// <summary>
        /// Patience 30 and 100 decide the same share of the stalled duels; at 100 the losing side's
        /// loss is nearer the rules-off result. A static field so the diagnostics can compare
        /// (DUEL_NO_WIN_PATIENCE).
        /// </summary>
        internal static int NoWayToWinPatienceTurns = 100;

        /// <summary>
        /// One squad's root forecast, for the forecast-against-battle diagnostic: the turn-by-turn
        /// trace of the continuation that sets Φ (Stay or Close, whichever is worth more), its
        /// value, and the squad's own pool.
        /// </summary>
        internal readonly record struct RootForecast(
            int SquadId,
            bool Close,
            float Value,
            float OwnPool,
            IReadOnlyList<EngagementExchangeForecast.TurnTrace> Turns,
            float StayValue = float.NaN,
            float CloseValue = float.NaN,
            IReadOnlyList<EngagementExchangeForecast.TurnTrace> CloseTurns = null,
            SustainedCycles Cycles = default);

        /// <summary>
        /// The standing fire the squad's removal-rate terms credit, at each term's own range: the
        /// shooters with a term, the volleys a turn their cycles fire (the sum of 1 / cycle turns),
        /// and the gross removal a turn (the sum of gross / cycle turns, before focus-fire waste).
        /// </summary>
        internal readonly record struct SustainedCycles(int Shooters, float VolleysPerTurn, float RatePerTurn);

        private SustainedCycles RootSustainedCycles(BattleSquad squad)
        {
            if (RootForecastSink == null)
            {
                return default;
            }
            int shooters = 0;
            float volleys = 0f;
            float rate = 0f;
            foreach (SquadPairRemovalRate pair in _exchange.PairRemovalRates(squad).Values)
            {
                foreach (PairRemovalTerm term in pair.Terms)
                {
                    (float gross, float turns) = term.SustainedCycleAt(term.ReferenceRange);
                    shooters += term.Count;
                    if (gross > 0f)
                    {
                        volleys += term.Count / turns;
                        rate += term.Count * gross / turns;
                    }
                }
            }
            return new SustainedCycles(shooters, volleys, rate);
        }

        /// <summary>
        /// Diagnostics only (ForecastAgainstBattleDiagnostics): when set, every root state's
        /// forecast is reported here, on the planning thread. Null in the game.
        /// </summary>
        [ThreadStatic]
        internal static Action<RootForecast> RootForecastSink;

        private static void ReportRootForecast(
            State state,
            IReadOnlyList<ExchangeLane> stay,
            IReadOnlyList<ExchangeLane> close,
            float stayValue,
            float closeValue,
            float ownPool,
            SquadBreakProfile ownBreak,
            float horizon,
            float closeHorizon,
            SustainedCycles cycles)
        {
            Action<RootForecast> sink = RootForecastSink;
            if (sink == null || state.Actions != null)
            {
                return;
            }
            bool useClose = close != null && closeValue > stayValue;
            List<EngagementExchangeForecast.TurnTrace> turns = [];
            EngagementExchangeForecast.Run(
                ownPool, ownBreak, useClose ? close : stay, useClose ? closeHorizon : horizon, trace: turns.Add);
            List<EngagementExchangeForecast.TurnTrace> closeTurns = null;
            if (close != null)
            {
                closeTurns = [];
                EngagementExchangeForecast.Run(ownPool, ownBreak, close, closeHorizon, trace: closeTurns.Add);
            }
            sink(new RootForecast(
                state.Squad.Id, useClose, useClose ? closeValue : stayValue, ownPool, turns,
                stayValue, closeValue, closeTurns, cycles));
        }

        /// <summary>
        /// Diagnostics only (RateTableDiagnostics): when set, the root state's forecasts for the
        /// squad <see cref="ForecastTraceSquadId"/> are written here turn by turn. A root state is
        /// one with no planned actions, as the policy builds it before projecting options.
        /// </summary>
        [ThreadStatic]
        internal static Action<string> ForecastTraceSink;

        /// <summary>
        /// Experiment switch (diagnostics only, 2026-10-23): price breaks in the potential's
        /// forecasts by level (<c>EngagementExchangeForecast.Run</c>'s <c>levelBreaks</c>) instead of the
        /// per-turn hazard, to test whether the jumps of the potential across a few yards of
        /// separation come from the break cliff. Process-wide; false in the game.
        /// </summary>
        internal static bool UseLevelBreaks;

        /// <summary>
        /// Whether the Close continuation's horizon adds the turns it spends getting to its
        /// destination (true since 2026-10-23, Design/Reference/ForecastCalibration.md section 5). Process-wide;
        /// a static field so the diagnostics can compare (DUEL_CLOSE_TRAVEL=0).
        /// </summary>
        internal static bool CloseHorizonAddsTravel = true;

        /// <summary>
        /// How many halfway stops the Close continuation of a squad that does not seek contact also
        /// tries: 1 adds half the gap, 2 also a quarter (2026-10-26). Process-wide; a static field
        /// so the diagnostics can compare (DUEL_CLOSE_STOPS=2 tries them). 0, off, until the study in
        /// Design/Reference/BattleValueCalibration.md settles: under the 0.97 discount they changed no decision.
        /// </summary>
        internal static int CloseHalfwayStops = 0;

        /// <summary>
        /// The per-turn discount of the potential's forecasts. The forecast's own default, 0.97,
        /// until 2026-10-26; a static field so the diagnostics can compare (DUEL_PHI_DISCOUNT). The
        /// opening forecast dropped the same discount in phase 3 of the opening-range refactor: it
        /// weighs a walk's losses now against fire that comes a quarter as heavy 45 turns later.
        /// </summary>
        internal static float TurnDiscount = EngagementExchangeForecast.TurnDiscount;

        /// <summary>
        /// The luck spread of the potential's forecasts (<c>EngagementExchangeForecast.RunExpected</c>).
        /// 0, off, since 2026-10-23: the luck average barely smoothed the potential, whose jumps come
        /// mostly from the rate table's whole-soldier fire allocation
        /// (Design/Reference/ForecastCalibration.md section 3). The opening forecast keeps it. A static
        /// field so the diagnostics can sweep it (PHI_LUCK).
        /// </summary>
        internal static float LuckSpread;

        [ThreadStatic]
        internal static int ForecastTraceSquadId;

        private static void TraceForecast(
            State state,
            string name,
            IReadOnlyList<ExchangeLane> lanes,
            float ownPool,
            SquadBreakProfile ownBreak,
            float horizon)
        {
            Action<string> sink = ForecastTraceSink;
            if (sink == null || state.Squad.Id != ForecastTraceSquadId || state.Actions != null)
            {
                return;
            }
            foreach (ExchangeLane lane in lanes)
            {
                sink(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"  {name} lane: enemyPool={lane.EnemyPool:F1} start={lane.StartRange:F1} "
                    + $"moves={lane.Moves} stop={lane.StopRange:F1} movingClosing={lane.MovingClosing:F2} "
                    + $"standingClosing={lane.StandingClosing:F2} contact={lane.ContactPossible} "
                    + $"contactOut={lane.ContactOutgoing:F2} contactIn={lane.ContactIncoming:F2}"));
            }
            EngagementExchangeForecast.Result result = EngagementExchangeForecast.Run(
                ownPool, ownBreak, lanes, horizon, TurnDiscount,
                trace: turn =>
                {
                    if (turn.Turn < 60 || turn.Turn % 10 == 0)
                    {
                        sink(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                            $"    {name} t={turn.Turn,3} r={turn.Range,6:F1} outRate={turn.OutRate,7:F3} "
                            + $"inRate={turn.InRate,7:F3} out={turn.Outgoing,7:F3} in={turn.Incoming,7:F3} "
                            + $"own={turn.OwnRemaining,7:F1} ownSurv={turn.OwnSurvival:F3} "
                            + $"ownHaz={turn.OwnHazard:F3} enemy={turn.EnemyRemaining,7:F1} "
                            + $"enemySurv={turn.EnemySurvival:F3} enemyHaz={turn.EnemyHazard:F3}"));
                    }
                });
            sink(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"  {name} TOTAL: removed={result.EnemyValueRemoved:F2} lost={result.OwnValueLost:F2} "
                + $"value={result.EnemyValueRemoved - result.OwnValueLost:F2} "
                + $"ownPool={ownPool:F1} horizon={horizon:F1}"));
        }

        /// <summary>
        /// The distance between the nearest soldiers of two squads, each moved as a block so that
        /// its centroid is at the given state centroid (a projected state moves a squad as a
        /// block). <paramref name="centroidRange"/> when either squad has no placed soldier.
        /// </summary>
        private float NearestSoldierSeparation(
            BattleSquad squad,
            ValueTuple<float, float> squadCentroid,
            BattleSquad enemy,
            ValueTuple<float, float> enemyCentroid,
            float centroidRange)
        {
            SquadCellLayout ours = LayoutOf(squad);
            SquadCellLayout theirs = LayoutOf(enemy);
            if (ours.Cells.Length == 0 || theirs.Cells.Length == 0)
            {
                return centroidRange;
            }
            // No pair can be nearer than the centroids less both radii: far apart, that decides.
            float lowerBound = centroidRange - ours.Radius - theirs.Radius;
            if (lowerBound > MeleeBand + BattleContactRules.MeleeContactAllowance)
            {
                return lowerBound;
            }
            float ourShiftX = squadCentroid.Item1 - ours.CentroidX;
            float ourShiftY = squadCentroid.Item2 - ours.CentroidY;
            float theirShiftX = enemyCentroid.Item1 - theirs.CentroidX;
            float theirShiftY = enemyCentroid.Item2 - theirs.CentroidY;
            float nearest = float.MaxValue;
            foreach ((float x, float y) in ours.Cells)
            {
                foreach ((float ex, float ey) in theirs.Cells)
                {
                    float dx = (x + ourShiftX) - (ex + theirShiftX);
                    float dy = (y + ourShiftY) - (ey + theirShiftY);
                    nearest = Math.Min(nearest, dx * dx + dy * dy);
                }
            }
            return MathF.Sqrt(nearest);
        }

        private SquadCellLayout LayoutOf(BattleSquad squad) =>
            _context == null || squad == null
                ? BuildLayout(squad)
                : _context.SquadCellLayouts.GetOrAdd(squad.Id, _ => BuildLayout(squad));

        private SquadCellLayout BuildLayout(BattleSquad squad)
        {
            List<(float X, float Y)> cells = [];
            foreach (BattleSoldier soldier in squad?.AbleSoldiers ?? [])
            {
                if (soldier.TopLeft is ValueTuple<int, int> cell
                    && (_grid == null || _grid.IsSoldierPlaced(soldier.Soldier.Id)))
                {
                    cells.Add((cell.Item1, cell.Item2));
                }
            }
            if (cells.Count == 0)
            {
                return new SquadCellLayout([], 0, 0, 0);
            }
            float centroidX = cells.Average(cell => cell.X);
            float centroidY = cells.Average(cell => cell.Y);
            float radius = cells.Max(cell => MathF.Sqrt(
                (cell.X - centroidX) * (cell.X - centroidX)
                + (cell.Y - centroidY) * (cell.Y - centroidY)));
            return new SquadCellLayout([.. cells], centroidX, centroidY, radius);
        }


        // Separation at which an arriving contact seeker is in melee, as in the exchange model's
        // melee rate (range <= 1.5).
        private const float ContactRange = 1f;
        // The exchange model's melee band (EngagementExchangeModel.MeleeRemovalRate: range <= 1.5).
        private const float MeleeBand = 1.5f;
        // Just outside the exchange model's melee band (range <= 1.5): the nearest range at which
        // a lane with contact in it reads ranged rates.
        private const float RangedSampleFloor = 2f;
        private const int RangeSampleCount = 9;

        /// <summary>
        /// Where along a lane's path to read the exchange rates: evenly spaced over the ranges the
        /// path can reach, plus the current and destination ranges exactly.
        /// </summary>
        private static IReadOnlyList<float> RangeSamples(
            float minimum,
            float maximum,
            float current,
            float destination)
        {
            List<float> samples = [current];
            if (destination >= minimum && destination <= maximum)
            {
                samples.Add(destination);
            }
            if (maximum - minimum > 0.5f)
            {
                // Geometric, not even: fire climbs steeply in the last yards, as the range modifier
                // goes with ln(1 / range), and the rates are linear between samples. Evenly spaced
                // samples put a straight line from point blank to a quarter of the way out, far above
                // the true curve, which the forecast's exact average over a turn's movement then
                // charged in full: 30 Hormagaunts charging 20 PDF from 100 yards read a loss of 109
                // where the duels say they win most such fights (2026-10-09).
                float low = Math.Max(1f, minimum);
                float high = Math.Max(low, maximum);
                for (int index = 0; index < RangeSampleCount; index++)
                {
                    samples.Add(low * MathF.Pow(high / low, index / (float)(RangeSampleCount - 1)));
                }
            }
            return samples;
        }

        /// <summary>
        /// A squad's break profile, memoized for the planning pass when the friendly squads that
        /// decide synapse coverage are known.
        /// </summary>
        private SquadBreakProfile BreakProfileOf(
            BattleSquad squad,
            IReadOnlyCollection<BattleSquad> friendlySquads)
        {
            if (friendlySquads == null || _context == null)
            {
                return EngagementExchangeForecast.ProfileOf(squad, friendlySquads, _grid);
            }
            return _context.SquadBreakProfiles.GetOrAdd(
                squad.Id,
                _ => EngagementExchangeForecast.ProfileOf(
                    squad, friendlySquads, _grid, _context.FactionBehaviorRules));
        }

        // Two lessons the lane's range path carries (EvaluateExchangePotential): time to arrive is
        // measured against the NET closing rate. A chase closes at the difference of the speeds;
        // pricing each step against a stationary quarry bought the same arrival again every turn
        // (Grist Nine Epsilon, 2026-09-20: 315 marines chased 20 routing orks for 700 turns; see
        // also BattleContactRules.CanReachContactThisTurn). And an enemy that is itself closing
        // covers part of the gap: ork shootas ran at approaching Genestealers for ten turns at
        // 200 yards (2026-10-01) because each step was priced as a whole turn off the wait.
        private static bool IsApproachingContactSeeker(
            BattleSquadCapabilityProfile opposing, SquadEngagementFrame opposingFrame) =>
            opposingFrame != null
            && opposing.IsContactSeeking
            && opposingFrame.Role is not (EngagementSquadRole.Bound
                or EngagementSquadRole.Routing
                or EngagementSquadRole.BreakOff
                or EngagementSquadRole.Cover
                or EngagementSquadRole.RearGuard);

        private float EvaluateReadiness(State state)
        {
            if (state.Actions != null)
            {
                // An Aim's readiness is the prepared SHOT, priced in the target's value
                // (RangedTargetSelector.EvaluateFireTiming); Ready and Reload are a fraction of the
                // soldier's OWN value (SoldierActionPlanner), because they prepare no particular
                // shot. Both are tactical value.
                float readiness = state.Actions.Sum(action => action?.ReadinessValue ?? 0f);
                bool pursuitPreparation = state.Frame != null
                    && IsFirePreservingPursuitRole(state.Frame.Role)
                    && state.Actions.Any(IsFirePreparation);
                // Preparation is stored value only while its eventual shot survives the quarry's
                // intervening movement. EvaluateFireWindow uses the same action descriptor and
                // projected geometry, so ready/reload/aim cannot retain an abstract readiness
                // bonus after the actual firing opportunity has closed.
                return !pursuitPreparation || EvaluateFireWindow(state) > 0
                    ? readiness
                    : 0;
            }

            // The root has no projected action descriptors. Price the readiness currently stored
            // in a live aim here instead, so abandoning that aim is a real potential loss rather
            // than something the legality mask must forbid. The projected Aim action uses the
            // same removal-conditioned calculation below, which makes the value telescope.
            // A stored aim is a prepared shot, priced like the projected Aim above.
            return state.Squad.AbleSoldiers.Sum(shooter =>
                EvaluateStoredAimReadiness(state, shooter));
        }

        private float EvaluateStoredAimReadiness(State state, BattleSoldier shooter)
        {
            if (shooter?.Aim is not ValueTuple<int, RangedWeapon, int> aim)
            {
                return 0;
            }

            BattleSoldier target = (state.Enemies ?? [])
                .SelectMany(enemy => enemy?.AbleSoldiers ?? [])
                .FirstOrDefault(candidate => candidate?.Soldier.Id == aim.Item1);
            if (target == null
                || !_grid.IsSoldierPlaced(shooter.Soldier.Id)
                || !_ranged.IsExistingAimStillViable(shooter))
            {
                return 0;
            }

            float range = _grid.GetDistanceBetweenSoldiers(
                shooter.Soldier.Id,
                target.Soldier.Id);
            RangedTargetEvaluation shot = _ranged.EvaluateRangedTarget(
                shooter,
                target,
                aim.Item2,
                range,
                RangedTargetSelector.AimAccuracy(shooter, aim.Item2.Template) + aim.Item3 + 1);
            // A stored aim is worth the best per-turn value it can still be cashed for: fire it now,
            // or keep aiming at the rate the planner would price that Aim at, both net of the
            // rounds they spend. This is the same currency as outgoing fire and as the projected
            // Aim action's readiness (RangedTargetSelector.EvaluateFireTiming), so the potential
            // telescopes and aiming is never valued below the shot it prepares. Until 2026-09-22
            // both sides used 5% of the shooter's battle value times the removal fraction, about a
            // tenth of the shot, which made any squad whose soldiers chose to aim look idle to the
            // option scorer.
            return _ranged.EvaluateFireTiming(
                    shooter,
                    target,
                    shot,
                    aim.Item2,
                    range,
                    aim.Item3,
                    bulkMultiplier: 0,
                    aimMultiplier: 1f)
                .StoredReadiness;
        }

        private static float EvaluateMorale(State state)
        {
            if (state.Squad.MoraleState != MoraleState.Shaken
                || !IsAdvancing(state))
            {
                return 0;
            }

            // A shaken advance is less valuable because it spends the turn exposing a formation
            // whose morale is already below the ordinary advance threshold. A withdrawal is not
            // an advance and therefore does not inherit this forward-pressure cost. This remains a
            // state value of the projected geometry, not an option-specific score penalty.
            return -state.Profile.TotalAbleBattleValue * 0.35f;
        }

        private static bool IsAdvancing(State state)
        {
            if (state.FeasibleSpeed <= 0 || state.Primary == null)
            {
                return false;
            }

            float currentDistance = EngagementExchangeModel.Distance(
                BattleEngagementFrameBuilder.Centroid(state.Squad),
                BattleEngagementFrameBuilder.Centroid(state.Primary));
            float projectedDistance = EngagementExchangeModel.Distance(
                state.Centroid,
                EnemyCentroid(state, state.Primary));
            return projectedDistance < currentDistance - 0.001f;
        }

        private float EvaluateCommandAura(State state)
        {
            if (_tacticsSkill == null
                || state.FriendlySquads == null
                || !state.Squad.SquadProvidesCommandAura)
            {
                return 0;
            }

            float radius = state.Squad.GetCommandAuraRadius(_tacticsSkill);
            if (radius <= 0)
            {
                return 0;
            }

            // Use the same stateless evaluator for the root and projected states. The optional
            // centroid lets the evaluator read the projected receiver geometry without mutating
            // the live squad or falling back to a constant approximation.
            float commandModifier = CommandAuraEvaluator.ComputeCommandAuraModifier(
                state.Squad,
                state.FriendlySquads,
                _grid,
                _tacticsSkill,
                state.Centroid);
            if (commandModifier <= 0)
            {
                return 0;
            }

            float supportedBattleValue = 0;
            foreach (BattleSquad friendly in state.FriendlySquads
                .Where(candidate => candidate != null
                    && candidate.Id != state.Squad.Id
                    && candidate.Status == BattleSquadStatus.Active
                    && !candidate.IsInMelee
                    && !candidate.SquadProvidesCommandAura))
            {
                float distance = EngagementExchangeModel.Distance(
                    state.Centroid,
                    BattleEngagementFrameBuilder.Centroid(friendly));
                if (distance > radius)
                {
                    continue;
                }

                supportedBattleValue += state.Profiles.TryGetValue(
                    friendly.Id,
                    out BattleSquadCapabilityProfile friendlyProfile)
                    ? friendlyProfile.TotalAbleBattleValue
                    : friendly.AbleSoldiers.Sum(SquadPlanningServices.BattleValueOf);
            }

            return supportedBattleValue
                * MoraleConstants.CommandAuraSupportWeight
                * commandModifier;
        }

        private static bool HasNoViableRangedOption(
            BattleSquadCapabilityProfile profile) =>
            profile.IsContactSeeking
                && (profile.UsableRangedBattleValue <= 0
                    || profile.EffectiveEngagementRange <= 0
                    || profile.PeakRangedRemovalFraction
                        < ContactSeekerRangedRelevanceFraction);

        private static float EvaluateScreenRole(State state)
        {
            if (state.Frame == null
                || !state.Frame.ProtectedSquadId.HasValue
                || !state.Frame.ScreenThreatSquadId.HasValue)
            {
                return 0;
            }

            BattleSquad threat = (state.Enemies ?? [])
                .FirstOrDefault(candidate => candidate.Id == state.Frame.ScreenThreatSquadId.Value);
            if (threat == null
                || !state.Profiles.TryGetValue(
                    threat.Id,
                    out BattleSquadCapabilityProfile threatProfile)
                || !state.Profiles.TryGetValue(
                    state.Frame.ProtectedSquadId.Value,
                    out BattleSquadCapabilityProfile protectedProfile))
            {
                return 0;
            }

            float interceptDistance = EngagementExchangeModel.Distance(
                state.Centroid,
                BattleEngagementFrameBuilder.Centroid(threat));
            float turnsUntilThreatReachesInterceptPoint = interceptDistance
                / Math.Max(0.1f, threatProfile.MoveSpeed);
            float threatMelee = threatProfile.UsableMeleeBattleValue;
            float holding = Math.Min(
                1f,
                (state.Profile.UsableMeleeBattleValue
                    + state.Profile.TotalAbleBattleValue * 0.25f)
                    / Math.Max(1f, threatMelee));
            float capacity = Math.Min(
                1f,
                state.Profile.ContactCapacity
                    / (float)Math.Max(1, threatProfile.ContactCapacity));
            float interceptDiscount = 1f / (1f + turnsUntilThreatReachesInterceptPoint);
            return Math.Min(
                    threatMelee,
                    protectedProfile.TotalAbleBattleValue)
                * holding
                * capacity
                * interceptDiscount;
        }

        /// <summary>
        /// How much of its own fighting value this squad can bring to bear from where it now
        /// stands, rising as it closes on its quarry and saturating at <c>attainable</c> once it
        /// is inside the range it wants.
        ///
        /// <para>A FUNCTION OF PROJECTED GEOMETRY ONLY. This used to multiply <c>attainable</c> by
        /// the fraction of the squad's maximum closing speed the candidate actually used, which
        /// made a state potential depend on how fast the squad was travelling when it arrived.
        /// The root state is always evaluated at zero speed, so the root scored zero and every
        /// moving candidate scored the whole bounty; the potential difference never telescoped
        /// and degenerated into a per-turn payment that could not deplete. Observed 2026-09-20
        /// (Grist Nine Epsilon, second run): this term read EXACTLY 141.150 for a running squad in
        /// two windows seventy turns apart while the separation closed, against a standing shot
        /// worth 7.4, and no amount of closing ever reduced it.</para>
        ///
        /// <para>Expressed as a position value the same reward telescopes: closing pays for the
        /// ground gained, once, and the whole approach is worth <c>attainable</c> in total rather
        /// than <c>attainable</c> every turn. It is also a genuine Φ term again, per §5.2's rule
        /// that doctrine lives in the legal-option mask and value lives in Φ.</para>
        ///
        /// <para>The quarry's speed is deliberately absent. A positional potential answers "how
        /// good is standing here"; how long the ground takes to cover is a question about time,
        /// and is priced by evaluating both the pursuer and quarry at the same projected instant.
        /// That geometry makes a hold open the range and an advance close it only by the net
        /// feasible movement, without a separate speed-ratio penalty.</para>
        ///
        /// <para>The saturating form has no flat region, so there is always a gradient toward the
        /// quarry however far away it is. The old form went flat once the band pressure clamped,
        /// which is the dead zone the approach-gradient test was written to catch.</para>
        /// </summary>
        private static float EvaluatePursuitContactProgress(State state)
        {
            bool closingIsTheOnlyPlay = HasNoViableRangedOption(state.Profile);
            if (state.Frame == null
                || (!IsPursuitRole(state.Frame.Role) && !closingIsTheOnlyPlay)
                || state.Primary == null)
            {
                return 0;
            }

            float attainable = state.Profile.IsContactSeeking
                ? state.Profile.UsableMeleeBattleValue
                : state.Profile.UsableRangedBattleValue;
            if (attainable <= 0)
            {
                return 0;
            }

            float projectedDistance = EngagementExchangeModel.Distance(
                state.Centroid,
                EnemyCentroid(state, state.Primary));
            // A squad that keeps shooting while it pursues seeks the first useful firing
            // opportunity. One closing to contact, or pressing without fire, retains the prior
            // contact/reach objective.
            bool holdsFireWhilePursuing = IsFirePreservingPursuitRole(state.Frame.Role)
                && !state.Profile.IsContactSeeking
                && state.Profile.PreferredBandUpper > state.Profile.PreferredBandLower;
            float desiredRange = state.Profile.IsContactSeeking
                ? 1f
                : holdsFireWhilePursuing
                    ? Math.Max(1f, state.Profile.UsefulFireRange)
                    : Math.Max(1f, state.Profile.PreferredBandUpper);
            float span = Math.Max(
                holdsFireWhilePursuing
                    ? state.Profile.PreferredBandUpper - desiredRange
                    : 0f,
                Math.Max(1f, state.Profile.MoveSpeed) * ContactProgressHalfValueStrides);
            float excess = Math.Max(0, projectedDistance - desiredRange);
            return attainable * span / (span + excess);
        }

        private float EvaluateFireWindow(State state)
        {
            if (state.Actions == null
                || !state.Actions.Any(IsFirePreparation)
                || state.Frame == null
                || !IsFirePreservingPursuitRole(state.Frame.Role)
                || state.Profile.IsContactSeeking
                || state.Primary == null)
            {
                return 0;
            }

            EngagementSquadRole? quarryRole = state.Frames
                .GetValueOrDefault(state.Primary.Id)?.Role;
            float quarrySpeed = EngagementExchangeModel.QuarryWithdrawalRate(
                state.Frame,
                quarryRole);
            Dictionary<int, float> awardedByTarget = [];
            float projectedValue = 0;

            foreach (BattleSoldier shooter in state.Squad.AbleSoldiers
                .OrderBy(soldier => soldier.Soldier.Id))
            {
                PlannedSoldierAction preparation = state.Actions.FirstOrDefault(
                    action => action?.SoldierId == shooter.Soldier.Id
                        && IsFirePreparation(action));
                if (!_grid.IsSoldierPlaced(shooter.Soldier.Id)
                    || preparation == null)
                {
                    continue;
                }

                RangedTargetEvaluation best = null;
                foreach (BattleSoldier target in state.Primary.AbleSoldiers
                    .Where(candidate => candidate.IsCombatEffective
                        && _grid.IsSoldierPlaced(candidate.Soldier.Id))
                    .OrderBy(candidate => candidate.Soldier.Id))
                {
                    foreach (RangedWeapon weapon in shooter.EquippedRangedWeapons
                        .Concat(shooter.RangedWeapons)
                        .Where(candidate => candidate.Template.Id == preparation.WeaponTemplateId
                            && !candidate.Template.IsTemplateWeapon)
                        .Distinct()
                        .OrderByDescending(candidate => candidate.Template.DamageMultiplier)
                        .ThenBy(candidate => candidate.Template.Id))
                    {
                        float currentRange = _grid.GetDistanceBetweenSoldiers(
                            shooter.Soldier.Id,
                            target.Soldier.Id);
                        float rootPairRange = EngagementExchangeModel.Distance(
                            BattleEngagementFrameBuilder.Centroid(state.Squad),
                            BattleEngagementFrameBuilder.Centroid(state.Primary));
                        float projectedPairRange = EngagementExchangeModel.Distance(
                            state.Centroid,
                            EnemyCentroid(state, state.Primary));
                        float rangeAfterThisTurn = Math.Max(
                            0,
                            currentRange + projectedPairRange - rootPairRange);
                        int remainingPreparationTurns = RemainingPreparationTurns(
                            shooter,
                            weapon,
                            preparation);
                        RangedTargetEvaluation evaluation = _ranged.EvaluatePursuitFireWindowShot(
                            shooter,
                            target,
                            weapon,
                            rangeAfterThisTurn,
                            quarrySpeed,
                            remainingPreparationTurns,
                            allowPendingPreparation: true);
                        if (evaluation == null
                            || evaluation.HitProbability <= RangedTargetSelector.StickyMinimumHitProbability
                            || evaluation.Score <= 0)
                        {
                            continue;
                        }
                        if (best == null
                            || evaluation.Score > best.Score
                            || (Math.Abs(evaluation.Score - best.Score) < 0.0001f
                                && evaluation.Target.Soldier.Id < best.Target.Soldier.Id))
                        {
                            best = evaluation;
                        }
                    }
                }

                if (best == null)
                {
                    continue;
                }

                float alreadyAwarded = awardedByTarget.GetValueOrDefault(best.Target.Soldier.Id);
                float remainingValue = Math.Max(
                    0,
                    SquadPlanningServices.BattleValueOf(best.Target) - alreadyAwarded);
                float contribution = Math.Min(
                    remainingValue,
                    Math.Max(0, best.Score))
                    * (float)Math.Pow(
                        EngagementExchangeModel.EngagementFutureDiscount,
                        1 + RemainingPreparationTurns(shooter, best.Weapon, preparation));
                if (contribution <= 0)
                {
                    continue;
                }
                awardedByTarget[best.Target.Soldier.Id] = alreadyAwarded + contribution;
                projectedValue += contribution;
            }

            return projectedValue;
        }

        private static bool IsFirePreparation(PlannedSoldierAction action) =>
            action?.Kind is PlannedSoldierActionKind.Aim
                or PlannedSoldierActionKind.Ready
                or PlannedSoldierActionKind.Reload;

        private static int RemainingPreparationTurns(
            BattleSoldier shooter,
            RangedWeapon weapon,
            PlannedSoldierAction action)
        {
            if (action.Kind == PlannedSoldierActionKind.Aim)
            {
                int resultingAim = shooter.Aim is ValueTuple<int, RangedWeapon, int> aim
                    && aim.Item1 == action.TargetId
                    && aim.Item2 == weapon
                        ? aim.Item3 + 1
                        : 0;
                return Math.Max(0, RangedTargetSelector.FullAimBonusTurns - resultingAim);
            }
            if (action.Kind == PlannedSoldierActionKind.Reload)
            {
                int progressAfterThisTurn = weapon.ReloadProgress + 1;
                return Math.Max(0, weapon.Template.ReloadTime - progressAfterThisTurn)
                    + RangedTargetSelector.FullAimBonusTurns;
            }
            return RangedTargetSelector.FullAimBonusTurns;
        }

        private static ValueTuple<float, float> EnemyCentroid(State state, BattleSquad enemy) =>
            state.PrimaryCentroid.HasValue && enemy?.Id == state.Primary?.Id
                ? state.PrimaryCentroid.Value
                : BattleEngagementFrameBuilder.Centroid(enemy);
    }
}
