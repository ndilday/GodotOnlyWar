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
        private const float FinitePoolEpsilon = 0.0001f;
        // Access is tempo rather than casualty value: it prices how long a squad remains unable to
        // contribute. Five turns keeps that signal material without letting it recreate the
        // whole-horizon multiplication removed from the finite exchange component.
        //
        // This sets the MAGNITUDE of the tempo signal only. Whether a squad counts as unable to
        // contribute is a separate question, and is measured against the negligible-removal floor
        // -- see EvaluateContinuousAccessValue for why reusing this constant for that test made a
        // shooting squad read as helpless.
        internal const float AccessValueTurns = 5f;

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
        /// The auditable decomposition of Φ. Net-rate value conserves finite casualty pools;
        /// access value prices time before useful contribution. Readiness and screen values are
        /// stored future value of the resulting state. The role and fire-window pieces remain
        /// separate for trace readability while together forming the screen potential family.
        /// </summary>
        internal readonly record struct Breakdown(
            float NetRateValue,
            float ReadinessValue,
            float RoleValue,
            float FireWindowValue,
            float MoraleValue,
            float CommandValue,
            float AccessValue = 0)
        {
            internal float ScreenValue => RoleValue + FireWindowValue;

            internal float Total =>
                NetRateValue
                + ReadinessValue
                + RoleValue
                + FireWindowValue
                + MoraleValue
                + CommandValue
                + AccessValue;
        }

        /// <summary>
        /// Evaluates Φ for one frozen state. This is the only entry point and intentionally has no
        /// engagement option parameter, so the root state's value is identical for every candidate
        /// in one decision.
        /// </summary>
        internal Breakdown Evaluate(State state)
        {
            ArgumentNullException.ThrowIfNull(state);
            (float finiteExchangeValue, float accessValue) = EvaluateExchangePotential(state);
            return new Breakdown(
                finiteExchangeValue,
                EvaluateReadiness(state),
                EvaluateScreenRole(state)
                    + EvaluatePursuitContactProgress(state),
                EvaluateFireWindow(state),
                EvaluateMorale(state),
                EvaluateCommandAura(state),
                accessValue);
        }

        internal static float ScoreTransition(
            float immediateExchange,
            Breakdown root,
            Breakdown projected,
            float contactCommitment) =>
            immediateExchange
            + EngagementPotentialDiscount * projected.Total
            - root.Total
            - contactCommitment;

        // Φ is already a value function over the expected exchange horizon. The transition is a
        // potential difference, not a one-ply rollout, so applying the old 0.65 rollout discount
        // here would discount the same time preference twice. (The bounded rollout that discount
        // was tuned for has been removed.) EngagementExchangeModel.EngagementFutureDiscount is
        // still used per turn of delay in the fire-window projection below, where a shot genuinely
        // lands one or more turns in the future.
        internal const float EngagementPotentialDiscount = 1f;

        private (float FiniteExchangeValue, float AccessValue) EvaluateExchangePotential(State state)
        {
            float outgoingValue = 0;
            float incomingOpportunity = 0;
            float accessValue = 0;
            float expectedExchangeTurns = _context?.ExpectedExchangeTurnsFor(state.Squad.Id)
                ?? EngagementHorizonModel.MaximumExchangeTurns;
            foreach (BattleSquad enemy in (state.Enemies ?? []).OrderBy(candidate => candidate.Id))
            {
                if (!state.Profiles.TryGetValue(enemy.Id, out BattleSquadCapabilityProfile opposing)
                    || !state.Frames.ContainsKey(enemy.Id))
                {
                    continue;
                }

                float range = Math.Max(
                    0,
                    EngagementExchangeModel.Distance(
                        state.Centroid,
                        EnemyCentroid(state, enemy)));
                bool firePreservingPursuit = state.Frame != null
                    && IsFirePreservingPursuitRole(state.Frame.Role)
                    && !state.Profile.IsContactSeeking;
                float desiredRange = state.Profile.IsContactSeeking
                    ? 1f
                    : firePreservingPursuit
                        ? Math.Max(1f, state.Profile.UsefulFireRange)
                        : Math.Max(1f, state.Profile.EffectiveEngagementRange);
                float turnsToUsefulRange = TurnsToUsefulRange(state, range, desiredRange);
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
                float currentOutgoingRate = _exchange.EvaluateOutgoingExchangeRate(
                    state.Squad,
                    enemy,
                    state.Profile,
                    opposing,
                    state.Frames,
                    range);
                float destinationOutgoingRate = _exchange.EvaluateOutgoingExchangeRate(
                    state.Squad,
                    enemy,
                    state.Profile,
                    opposing,
                    state.Frames,
                    destinationRange);
                float currentIncomingRate = _exchange.EvaluateIncomingExchangeRate(
                    state.Squad,
                    enemy,
                    state.Profile,
                    state.Frames,
                    range,
                    targetSpeed: 0f);
                float destinationIncomingRate = _exchange.EvaluateIncomingExchangeRate(
                    state.Squad,
                    enemy,
                    state.Profile,
                    state.Frames,
                    destinationRange,
                    targetSpeed: 0f);
                float turnsAtCurrentRate = Math.Min(
                    turnsToUsefulRange,
                    expectedExchangeTurns);
                float turnsAtDestinationRate = Math.Max(
                    0,
                    expectedExchangeTurns - turnsToUsefulRange);
                float targetValue = Math.Max(0, opposing.TotalAbleBattleValue);
                float outgoingOpportunity = IntegrateOpportunity(
                    currentOutgoingRate,
                    destinationOutgoingRate,
                    turnsAtCurrentRate,
                    turnsAtDestinationRate);
                outgoingValue += SaturateFinitePool(outgoingOpportunity, targetValue);
                incomingOpportunity += IntegrateOpportunity(
                    currentIncomingRate,
                    destinationIncomingRate,
                    turnsAtCurrentRate,
                    turnsAtDestinationRate);
                incomingOpportunity += EvaluateProjectedMeleeOpportunity(
                    state,
                    enemy,
                    opposing,
                    range,
                    expectedExchangeTurns);
                // A shooting squad's access reads the rate WITH aiming: one that can aim and then
                // fire from here is not waiting for access. See
                // SquadPairRemovalRate.SustainedRateAtRange. A contact seeker's access is the
                // time to contact, and it closes on the move, un-aimed, so it keeps the plain rate.
                bool standsToShoot = !state.Profile.IsContactSeeking;
                accessValue += EvaluateContinuousAccessValue(
                    standsToShoot
                        ? _exchange.EvaluateSustainedOutgoingRate(
                            state.Squad, enemy, opposing, state.Frames, range)
                        : currentOutgoingRate,
                    standsToShoot
                        ? _exchange.EvaluateSustainedOutgoingRate(
                            state.Squad, enemy, opposing, state.Frames, destinationRange)
                        : destinationOutgoingRate,
                    turnsToUsefulRange,
                    targetValue,
                    firePreservingPursuit ? expectedExchangeTurns : float.PositiveInfinity,
                    firePreservingPursuit);
            }
            float friendlyPool = Math.Max(0, state.Profile.TotalAbleBattleValue);
            float incomingValue = SaturateFinitePool(incomingOpportunity, friendlyPool);
            return (outgoingValue - incomingValue, accessValue);
        }

        private float EvaluateProjectedMeleeOpportunity(
            State state,
            BattleSquad enemy,
            BattleSquadCapabilityProfile opposing,
            float range,
            float expectedExchangeTurns)
        {
            SquadEngagementFrame opposingFrame = state.Frames.GetValueOrDefault(enemy.Id);
            if (opposingFrame == null
                || !opposing.IsContactSeeking
                || opposingFrame.Role is EngagementSquadRole.Bound
                    or EngagementSquadRole.Routing
                    or EngagementSquadRole.BreakOff
                    or EngagementSquadRole.Cover
                    or EngagementSquadRole.RearGuard)
            {
                return 0;
            }

            float contactRate = _exchange.EvaluateContactRemovalRate(enemy, state.Squad);
            if (contactRate <= 0)
            {
                return 0;
            }

            float turnsToContact = Math.Max(0, range - 1f)
                / Math.Max(0.1f, opposing.MoveSpeed);
            float contactTurns = Math.Max(
                0,
                expectedExchangeTurns - turnsToContact);
            return contactRate * contactTurns;
        }

        /// <summary>
        /// Maps accumulated positive removal opportunity into one finite battle-value pool. The
        /// mapping is continuous and monotonic, applies at every magnitude, and never exposes a
        /// threshold where nearly identical geometries use different value scales.
        /// </summary>
        internal static float SaturateFinitePool(
            float opportunity,
            float battleValuePool)
        {
            if (float.IsNaN(opportunity)
                || opportunity <= 0
                || !float.IsFinite(battleValuePool)
                || battleValuePool <= 0)
            {
                return 0;
            }

            if (float.IsPositiveInfinity(opportunity))
            {
                return battleValuePool;
            }

            return battleValuePool * (1f - (float)Math.Exp(
                -opportunity / Math.Max(FinitePoolEpsilon, battleValuePool)));
        }

        private static float IntegrateOpportunity(
            float currentRate,
            float destinationRate,
            float currentTurns,
            float destinationTurns)
        {
            float currentOpportunity = float.IsFinite(currentRate) && currentRate > 0
                ? currentRate * Math.Max(0, currentTurns)
                : 0;
            float destinationOpportunity = float.IsFinite(destinationRate) && destinationRate > 0
                ? destinationRate * Math.Max(0, destinationTurns)
                : 0;
            return currentOpportunity + destinationOpportunity;
        }

        /// <summary>
        /// How long before this squad reaches the range at which it wants to be shooting,
        /// measured against the NET closing rate rather than the squad's own move.
        ///
        /// <para>WHY NET. A chase closes at the difference of the two speeds, because the quarry
        /// is running as well. Dividing the raw gap by the pursuer's own move prices a full step
        /// against a stationary target, and the next turn re-prices the shortened gap the same
        /// way, so the access term pays the same tempo bonus every turn for an arrival that keeps
        /// receding. Observed 2026-09-20 (Grist Nine Epsilon): 315 marines pursued 20 routing
        /// orks for the last 700 turns of a 1000-turn battle. The access delta held at ~10 battle
        /// value per turn while the separation fell from 323 to 104 -- a stock that should have
        /// been depleting did not move -- and it outbid a standing shot worth ~3 on 1112 of 1112
        /// squad-turns. <see cref="BattleContactRules.CanReachContactThisTurn"/> carries the same
        /// correction for the contact-break tests, made after a 2026-08-04 stern chase ran to the
        /// turn cap for the same reason.</para>
        ///
        /// <para>A quarry the squad cannot out-run returns positive infinity, which
        /// <see cref="EvaluateContinuousAccessValue"/>'s non-finite guard turns into no access
        /// value at all. That is the right answer rather than a degenerate one: the arrival never
        /// happens, so there is no delay to buy out, and the squad should shoot from where it
        /// stands. The exchange integral reads the same infinity as "stay at the current rate for
        /// the whole horizon", which is also correct.</para>
        ///
        /// <para>Only a pursuit frame carries a quarry speed; every other role sees the
        /// unchanged raw-move behaviour.</para>
        /// </summary>
        private static float TurnsToUsefulRange(State state, float range, float desiredRange)
        {
            float gap = Math.Max(0, range - desiredRange);
            if (gap <= 0) return 0;
            float quarrySpeed = state.Frame != null && IsPursuitRole(state.Frame.Role)
                ? Math.Max(0, state.Frame.QuarryRunSpeed)
                : 0;
            float closingRate = state.Profile.MoveSpeed - quarrySpeed;
            return closingRate <= 0 ? float.PositiveInfinity : gap / closingRate;
        }

        /// <summary>
        /// Prices delay before a squad can contribute without a hard useful/useless branch. The
        /// weight approaches zero smoothly as current fire becomes useful, and is exactly zero
        /// when the destination geometry cannot produce removal either.
        /// </summary>
        internal static float EvaluateContinuousAccessValue(
            float currentRate,
            float destinationRate,
            float turnsToUsefulRange,
            float targetBattleValue,
            float maximumDelayTurns = float.PositiveInfinity,
            bool destinationDefinesUsefulFire = false)
        {
            if (!float.IsFinite(currentRate)
                || !float.IsFinite(destinationRate)
                || !float.IsFinite(turnsToUsefulRange)
                || !float.IsFinite(targetBattleValue)
                || destinationRate <= 0
                || turnsToUsefulRange <= 0
                || targetBattleValue <= 0)
            {
                return 0;
            }

            float scale = targetBattleValue / AccessValueTurns;
            float nonNegativeCurrent = Math.Max(0, currentRate);
            float helplessness;
            if (destinationDefinesUsefulFire)
            {
                // The useful-range boundary supplies the rate that ends the access deficit. This
                // smoothstep is exactly zero at and inside that boundary; improvements after
                // arrival belong solely to the exchange curve. It therefore cannot pay twice for
                // closing within useful fire.
                float deficitFraction = Math.Clamp(
                    (destinationRate - nonNegativeCurrent) / destinationRate,
                    0,
                    1);
                helplessness = deficitFraction * deficitFraction
                    * (3f - 2f * deficitFraction);
            }
            else
            {
                // Preserve the existing non-pursuit definition: access there is the inability to
                // contribute above the shared plinking floor, not arrival at a pursuit fire band.
                float negligibleRate =
                    targetBattleValue * RangedEffectivenessCurve.NegligibleRemovalFraction;
                helplessness = negligibleRate / (negligibleRate + nonNegativeCurrent);
            }
            float viability = destinationRate / (scale + destinationRate);
            float tempoRate = scale * helplessness * viability;
            float boundedDelay = Math.Min(turnsToUsefulRange, maximumDelayTurns);
            return -tempoRate * boundedDelay;
        }

        private float EvaluateReadiness(State state)
        {
            if (state.Actions != null)
            {
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
                aim.Item2.Template.Accuracy + aim.Item3 + 1);
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
            float holding = Math.Min(
                1f,
                (state.Profile.UsableMeleeBattleValue
                    + state.Profile.TotalAbleBattleValue * 0.25f)
                    / Math.Max(1, threatProfile.UsableMeleeBattleValue));
            float capacity = Math.Min(
                1f,
                state.Profile.ContactCapacity
                    / (float)Math.Max(1, threatProfile.ContactCapacity));
            float interceptDiscount = 1f / (1f + turnsUntilThreatReachesInterceptPoint);
            return Math.Min(
                    threatProfile.UsableMeleeBattleValue,
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
