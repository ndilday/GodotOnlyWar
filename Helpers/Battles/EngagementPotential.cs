using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// The state value used by engagement posture scoring. The value is deliberately a function of
    /// state only: there is no engagement option kind in this type or in <see cref="Evaluate"/>.
    /// A candidate supplies the state it would leave behind -- projected geometry and the pure
    /// action descriptors are enough to value that state without mutating the live battle.
    /// </summary>
    internal sealed class EngagementPotential
    {
        private const int PursuitFireWindowTurns = RangedTargetSelector.FullAimBonusTurns + 2;
        private const float ContactSeekerRangedRelevanceFraction = 0.02f;
        private const float FinitePoolEpsilon = 0.0001f;
        // Access is tempo rather than casualty value: it prices how long a squad remains unable to
        // contribute. Five turns keeps that signal material without letting it recreate the
        // whole-horizon multiplication removed from the finite exchange component.
        internal const float AccessValueTurns = 5f;

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
            IReadOnlyCollection<BattleSquad> FriendlySquads = null);

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
                    + EvaluatePursuitContactProgress(state)
                    + EvaluatePursuitClosingValue(state),
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
        // here would discount the same time preference twice. Genuine bounded rollouts elsewhere
        // continue to use EngagementExchangeModel.EngagementFutureDiscount.
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
                        BattleEngagementFrameBuilder.Centroid(enemy)));
                float desiredRange = state.Profile.IsContactSeeking
                    ? 1f
                    : Math.Max(1f, state.Profile.EffectiveEngagementRange);
                float turnsToUsefulRange = Math.Max(0, range - desiredRange)
                    / Math.Max(0.1f, state.Profile.MoveSpeed);
                // Pursuit fire-support saturates at the useful band's boundary. Once it is inside
                // that band, moving still changes the live shot but does not create a new arrival
                // opportunity worth buying with a whole-battle horizon. Other doctrines retain
                // the actual-in-band destination rate so contact geometry can still be valued.
                float destinationRange = state.Frame?.Role == EngagementSquadRole.Pursuit
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
                accessValue += EvaluateContinuousAccessValue(
                    currentOutgoingRate,
                    destinationOutgoingRate,
                    turnsToUsefulRange,
                    targetValue);
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
        /// Prices delay before a squad can contribute without a hard useful/useless branch. The
        /// weight approaches zero smoothly as current fire becomes useful, and is exactly zero
        /// when the destination geometry cannot produce removal either.
        /// </summary>
        internal static float EvaluateContinuousAccessValue(
            float currentRate,
            float destinationRate,
            float turnsToUsefulRange,
            float targetBattleValue)
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
            float helplessness = scale / (scale + nonNegativeCurrent);
            float viability = destinationRate / (scale + destinationRate);
            float tempoRate = scale * helplessness * viability;
            return -tempoRate * turnsToUsefulRange;
        }

        private float EvaluateReadiness(State state)
        {
            if (state.Actions != null)
            {
                return state.Actions.Sum(action => action?.ReadinessValue ?? 0f);
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
            return ReadinessForPreparedShot(shooter, shot);
        }

        internal static float ReadinessForPreparedShot(
            BattleSoldier soldier,
            RangedTargetEvaluation shot)
        {
            if (soldier == null
                || shot == null
                || shot.ExpectedEnemyBattleValueRemoved <= 0)
            {
                return 0;
            }

            float targetBattleValue = Math.Max(
                1,
                SquadPlanningServices.BattleValueOf(shot.Target));
            float effectiveRemovalFraction = Math.Clamp(
                shot.ExpectedEnemyBattleValueRemoved / targetBattleValue,
                0,
                1);
            return SquadPlanningServices.BattleValueOf(soldier)
                * 0.05f
                * effectiveRemovalFraction;
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
                BattleEngagementFrameBuilder.Centroid(state.Primary));
            return projectedDistance < currentDistance - 0.001f;
        }

        private static float EvaluatePursuitClosingValue(State state)
        {
            if (state.Frame == null
                || state.Frame.Role != EngagementSquadRole.Pursuit
                || state.FeasibleSpeed <= 0
                || state.Frame.QuarryRunSpeed <= 0
                || state.FeasibleSpeed >= state.Frame.QuarryRunSpeed)
            {
                return 0;
            }

            float closingFraction = Math.Clamp(
                state.FeasibleSpeed / Math.Max(0.1f, state.Frame.QuarryRunSpeed),
                0,
                1);
            return -state.Profile.TotalAbleBattleValue * (1f - closingFraction);
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

        private static float EvaluatePursuitContactProgress(State state)
        {
            bool closingIsTheOnlyPlay = HasNoViableRangedOption(state.Profile);
            if (state.Frame == null
                || (state.Frame.Role != EngagementSquadRole.Pursuit && !closingIsTheOnlyPlay)
                || state.Primary == null)
            {
                return 0;
            }

            float before = EngagementExchangeModel.Distance(
                state.Centroid,
                BattleEngagementFrameBuilder.Centroid(state.Primary));
            EngagementSquadRole? quarryRole = state.Frames
                .GetValueOrDefault(state.Primary.Id)?.Role;
            float quarrySpeed = EngagementExchangeModel.QuarryWithdrawalRate(
                state.Frame,
                quarryRole);
            float attainable = state.Profile.IsContactSeeking
                ? state.Profile.UsableMeleeBattleValue
                : state.Profile.UsableRangedBattleValue;

            if (state.Frame.Role == EngagementSquadRole.Pursuit
                && !state.Profile.IsContactSeeking
                && state.Profile.PreferredBandUpper > state.Profile.PreferredBandLower)
            {
                float bandWidth = Math.Max(
                    0.1f,
                    state.Profile.PreferredBandUpper - state.Profile.PreferredBandLower);
                float bandPressure = Math.Clamp(
                    (before - state.Profile.PreferredBandLower) / bandWidth,
                    0,
                    1);
                float maximumNetClosing = Math.Max(
                    0,
                    state.Profile.MoveSpeed - quarrySpeed);
                float actualNetClosing = Math.Max(
                    0,
                    state.FeasibleSpeed - quarrySpeed);
                float closingFraction = maximumNetClosing <= 0
                    ? 0
                    : Math.Clamp(actualNetClosing / maximumNetClosing, 0, 1);
                return attainable * bandPressure * closingFraction;
            }

            float desiredRange = state.Profile.IsContactSeeking
                ? 1f
                : Math.Max(1f, state.Profile.PreferredBandUpper);
            if (before <= desiredRange)
            {
                return 0;
            }

            float usefulStride = Math.Min(
                Math.Max(0, state.Profile.MoveSpeed - quarrySpeed),
                before - desiredRange);
            float progress = Math.Min(
                usefulStride,
                Math.Max(0, state.FeasibleSpeed - quarrySpeed));
            return usefulStride <= 0
                ? 0
                : attainable * progress / Math.Max(0.1f, usefulStride);
        }

        private float EvaluateFireWindow(State state)
        {
            if (state.Actions == null
                || !state.Actions.Any(action =>
                    action?.Kind == PlannedSoldierActionKind.Aim)
                || state.Frame == null
                || state.Frame.Role != EngagementSquadRole.Pursuit
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
            float projectedOpening = quarrySpeed * PursuitFireWindowTurns;
            Dictionary<int, float> awardedByTarget = [];
            float projectedValue = 0;

            foreach (BattleSoldier shooter in state.Squad.AbleSoldiers
                .OrderBy(soldier => soldier.Soldier.Id))
            {
                if (!_grid.IsSoldierPlaced(shooter.Soldier.Id)
                    || shooter.EquippedRangedWeapons.Count == 0)
                {
                    continue;
                }

                RangedTargetEvaluation best = null;
                foreach (BattleSoldier target in state.Primary.AbleSoldiers
                    .Where(candidate => candidate.IsCombatEffective
                        && _grid.IsSoldierPlaced(candidate.Soldier.Id))
                    .OrderBy(candidate => candidate.Soldier.Id))
                {
                    float currentRange = _grid.GetDistanceBetweenSoldiers(
                        shooter.Soldier.Id,
                        target.Soldier.Id);
                    float projectedRange = currentRange + projectedOpening;
                    foreach (RangedWeapon weapon in shooter.EquippedRangedWeapons
                        .Where(candidate => !candidate.Template.IsTemplateWeapon
                            && candidate.LoadedAmmo > 0
                            && projectedRange <= candidate.Template.MaximumRange)
                        .OrderByDescending(candidate => candidate.Template.DamageMultiplier)
                        .ThenBy(candidate => candidate.Template.Id))
                    {
                        RangedTargetEvaluation evaluation = _ranged.EvaluateRangedTarget(
                            shooter,
                            target,
                            weapon,
                            projectedRange,
                            weapon.Template.Accuracy + RangedTargetSelector.FullAimBonusTurns + 1,
                            quarrySpeed);
                        if (evaluation.HitProbability <= RangedTargetSelector.StickyMinimumHitProbability
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
                    Math.Max(0, best.Score));
                if (contribution <= 0)
                {
                    continue;
                }
                awardedByTarget[best.Target.Soldier.Id] = alreadyAwarded + contribution;
                projectedValue += contribution;
            }

            return projectedValue
                * (float)Math.Pow(
                    EngagementExchangeModel.EngagementFutureDiscount,
                    PursuitFireWindowTurns);
        }
    }
}
