using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// Scores and selects the semantic engagement option for one squad.
    ///
    /// <para>The policy owns legal-option enumeration, force-horizon preparation, candidate
    /// projection, and the unchanged engagement formulas. It composes the decision-only
    /// <see cref="SoldierActionPlanner"/> for root descriptors, but it neither declares live
    /// movement nor constructs executable actions.</para>
    /// </summary>
    internal sealed class SquadEngagementPolicy
    {
        private const float WalkSpeedMultiplier = SoldierMovementProjector.WalkSpeedMultiplier;
        private const float JogSpeedMultiplier = SoldierMovementProjector.JogSpeedMultiplier;
        private const float WalkBulkMultiplier = SoldierMovementProjector.WalkBulkMultiplier;
        private const float FullBulkMultiplier = SoldierMovementProjector.FullBulkMultiplier;
        private const float EngagementIndifferenceFraction = 0.02f;
        private const float ContactSeekerRangedRelevanceFraction = 0.02f;

        private readonly SquadPlanningServices _services;
        private readonly BattleGridManager _grid;
        private readonly IReadOnlyDictionary<int, BattleSoldier> _soldierMap;
        private readonly RangedTargetSelector _ranged;
        private readonly SoldierMovementProjector _movement;
        private readonly EngagementExchangeModel _exchange;
        private readonly EngagementPotential _potential;
        private readonly SoldierActionPlanner _soldierActions;
        private readonly BattlePlanningContext _context;

        internal int TraceTurnNumber { get; set; }
        internal string TraceSideLabel { get; set; }

        internal bool EngagementHorizonInitialized => _context.EngagementHorizonInitialized;

        internal float ExpectedExchangeTurnsFor(int squadId) =>
            _context.ExpectedExchangeTurnsFor(squadId);

        internal EngagementPotential.Breakdown EvaluatePotential(
            EngagementPotential.State state) => _potential.Evaluate(state);

        internal SquadEngagementPolicy(
            SquadPlanningServices services,
            RangedTargetSelector ranged,
            SoldierMovementProjector movement,
            EngagementExchangeModel exchange,
            EngagementPotential potential,
            SoldierActionPlanner soldierActions)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _grid = services.Grid;
            _soldierMap = services.SoldierMap;
            _ranged = ranged ?? throw new ArgumentNullException(nameof(ranged));
            _movement = movement ?? throw new ArgumentNullException(nameof(movement));
            _exchange = exchange ?? throw new ArgumentNullException(nameof(exchange));
            _potential = potential ?? throw new ArgumentNullException(nameof(potential));
            _soldierActions = soldierActions
                ?? throw new ArgumentNullException(nameof(soldierActions));
            _context = services.Context;
        }

        internal SquadEngagementDecision ChooseEngagementOption(
            BattleSquad squad,
            SquadEngagementFrame frame,
            IReadOnlyDictionary<int, BattleSquadCapabilityProfile> profiles,
            IReadOnlyDictionary<int, SquadEngagementFrame> allFrames,
            IReadOnlyCollection<BattleSquad> enemySquads,
            IReadOnlyCollection<BattleSquad> roleTargets = null)
        {
            ArgumentNullException.ThrowIfNull(squad);
            ArgumentNullException.ThrowIfNull(frame);
            EnsureEngagementHorizon(profiles, allFrames);
            BattleSquadCapabilityProfile profile = profiles[squad.Id];
            List<BattleSquad> enemies = (roleTargets ?? enemySquads ?? [])
                .Where(candidate => candidate != null
                    && candidate.Status == BattleSquadStatus.Active
                    && candidate.AbleSoldiers.Count > 0)
                .OrderBy(candidate => candidate.Id)
                .ToList();
            BattleSquad primary = ResolvePrimary(frame, enemies, enemySquads);
            List<EngagementOptionKind> legal = GetLegalOptionKinds(
                squad,
                frame,
                primary,
                profile);
            List<EngagementOptionEvaluation> evaluations = legal
                .Select(kind => EvaluateEngagementOption(
                    squad,
                    kind,
                    frame,
                    profile,
                    profiles,
                    allFrames,
                    primary,
                    enemies))
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Kind)
                .ToList();
            float bestScore = evaluations.Select(candidate => candidate.Score)
                .DefaultIfEmpty(0)
                .Max();
            float indifference = Math.Max(
                0.1f,
                profile.TotalAbleBattleValue * EngagementIndifferenceFraction);
            EngagementOptionEvaluation chosen = evaluations
                .Where(candidate => bestScore - candidate.Score <= indifference)
                .OrderByDescending(candidate => candidate.Kind == frame.BaselinePosture)
                .ThenByDescending(candidate => candidate.Kind == squad.LastEngagementOptionKind)
                .ThenByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Kind)
                .FirstOrDefault()
                ?? new EngagementOptionEvaluation(
                    EngagementOptionKind.Hold,
                    SquadMovementTier.Stationary,
                    null,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    [],
                    0,
                    0,
                    0,
                    0);
            return new SquadEngagementDecision(
                squad,
                frame,
                chosen,
                evaluations,
                roleTargets);
        }

        internal SquadEngagementDecision ChooseEngagementOption(
            BattleSquad squad,
            SquadEngagementFrame frame,
            IReadOnlyDictionary<int, BattleSquadCapabilityProfile> profiles,
            IReadOnlyCollection<BattleSquad> friendlySquads,
            IReadOnlyCollection<BattleSquad> enemySquads,
            IReadOnlyCollection<BattleSquad> roleTargets = null)
        {
            BattleEngagementFrameBuilder.PairedFrame paired =
                BattleEngagementFrameBuilder.Build(friendlySquads, enemySquads);
            return ChooseEngagementOption(
                squad,
                frame,
                profiles,
                paired.Frames,
                enemySquads,
                roleTargets);
        }

        internal void InitializeEngagementHorizon(
            IReadOnlyDictionary<int, BattleSquadCapabilityProfile> profiles,
            IReadOnlyDictionary<int, SquadEngagementFrame> frames,
            int maxDegreeOfParallelism) =>
            EnsureEngagementHorizon(profiles, frames, maxDegreeOfParallelism);

        private void EnsureEngagementHorizon(
            IReadOnlyDictionary<int, BattleSquadCapabilityProfile> profiles,
            IReadOnlyDictionary<int, SquadEngagementFrame> frames,
            int maxDegreeOfParallelism = 1)
        {
            if (_context.EngagementHorizonInitialized)
            {
                return;
            }

            lock (_context.EngagementHorizonGate)
            {
                if (_context.EngagementHorizonInitialized)
                {
                    return;
                }

                List<BattleSquad> active = _soldierMap.Values
                    .Select(soldier => soldier.BattleSquad)
                    .Where(candidate => candidate != null
                        && candidate.Status == BattleSquadStatus.Active
                        && candidate.AbleSoldiers.Any(IsPlaced))
                    .DistinctBy(candidate => candidate.Id)
                    .OrderBy(candidate => candidate.Id)
                    .ToList();
                Dictionary<int, bool> sideBySquad = [];
                foreach (BattleSquad candidate in active)
                {
                    BattleSoldier anchor = candidate.AbleSoldiers.First(IsPlaced);
                    sideBySquad[candidate.Id] = _grid.GetSoldierSide(anchor.Soldier.Id);
                }

                Dictionary<bool, List<BattleSquad>> sides = active
                    .GroupBy(candidate => sideBySquad[candidate.Id])
                    .ToDictionary(group => group.Key, group => group.ToList());
                Dictionary<int, float> expectedExchangeTurnsBySquad = [];
                float totalBattleValueAtRisk = 0;
                float totalRemovalRate = 0;
                foreach ((bool side, List<BattleSquad> attackers) in sides)
                {
                    List<BattleSquad> targets = sides
                        .Where(entry => entry.Key != side)
                        .SelectMany(entry => entry.Value)
                        .OrderBy(candidate => candidate.Id)
                        .ToList();
                    float battleValueAtRisk = targets.Sum(candidate =>
                        profiles.TryGetValue(
                            candidate.Id,
                            out BattleSquadCapabilityProfile candidateProfile)
                                ? candidateProfile.TotalAbleBattleValue
                                : candidate.AbleSoldiers.Sum(GetBattleValue));
                    float[] attackerRates = new float[attackers.Count];
                    void AccumulateAttackerRate(int attackerIndex)
                    {
                        BattleSquad attacker = attackers[attackerIndex];
                        if (!frames.TryGetValue(
                                attacker.Id,
                                out SquadEngagementFrame attackerFrame)
                            || attackerFrame.Role is EngagementSquadRole.Pursuit
                                or EngagementSquadRole.Follow
                                or EngagementSquadRole.Press)
                        {
                            return;
                        }

                        if (!profiles.TryGetValue(
                                attacker.Id,
                                out BattleSquadCapabilityProfile attackerProfile))
                        {
                            return;
                        }

                        float attackerRate = 0;
                        foreach (BattleSquad target in targets)
                        {
                            if (!profiles.TryGetValue(
                                    target.Id,
                                    out BattleSquadCapabilityProfile targetProfile)
                                || !frames.ContainsKey(target.Id))
                            {
                                continue;
                            }

                            float range = EngagementExchangeModel.Distance(
                                BattleEngagementFrameBuilder.Centroid(attacker),
                                BattleEngagementFrameBuilder.Centroid(target));
                            attackerRate += Math.Max(
                                0,
                                _exchange.EvaluateOutgoingExchangeRate(
                                    attacker,
                                    target,
                                    attackerProfile,
                                    targetProfile,
                                    frames,
                                    range));
                        }
                        attackerRates[attackerIndex] = attackerRate;
                    }

                    if (maxDegreeOfParallelism <= 1 || attackers.Count <= 1)
                    {
                        for (int index = 0; index < attackers.Count; index++)
                        {
                            AccumulateAttackerRate(index);
                        }
                    }
                    else
                    {
                        Parallel.For(
                            0,
                            attackers.Count,
                            new ParallelOptions
                            {
                                MaxDegreeOfParallelism = maxDegreeOfParallelism
                            },
                            AccumulateAttackerRate);
                    }

                    float currentRemovalRate = 0;
                    for (int index = 0; index < attackerRates.Length; index++)
                    {
                        currentRemovalRate += attackerRates[index];
                    }

                    float expectedExchangeTurns =
                        EngagementHorizonModel.DeriveExpectedExchangeTurns(
                            battleValueAtRisk,
                            currentRemovalRate);
                    foreach (BattleSquad attacker in attackers)
                    {
                        expectedExchangeTurnsBySquad[attacker.Id] = expectedExchangeTurns;
                    }
                    totalBattleValueAtRisk += battleValueAtRisk;
                    totalRemovalRate += currentRemovalRate;
                }

                _context.SetEngagementHorizon(
                    expectedExchangeTurnsBySquad,
                    totalBattleValueAtRisk,
                    totalRemovalRate);
            }
        }

        private static bool HasNoViableRangedOption(BattleSquadCapabilityProfile profile) =>
            profile.IsContactSeeking
                && (profile.UsableRangedBattleValue <= 0
                    || profile.EffectiveEngagementRange <= 0
                    || profile.PeakRangedRemovalFraction
                        < ContactSeekerRangedRelevanceFraction);

        private static bool IsPursuitRole(EngagementSquadRole role) =>
            role is EngagementSquadRole.Pursuit
                or EngagementSquadRole.Follow
                or EngagementSquadRole.Press;

        private static EngagementOptionKind FastApproachOption(BattleSquad squad) =>
            squad.CanRun
                ? EngagementOptionKind.RunToward
                : EngagementOptionKind.JogToward;

        private List<EngagementOptionKind> GetLegalOptionKinds(
            BattleSquad squad,
            SquadEngagementFrame frame,
            BattleSquad primary,
            BattleSquadCapabilityProfile profile)
        {
            if (frame.Role is EngagementSquadRole.Bound
                or EngagementSquadRole.BreakOff)
            {
                return frame.Role == EngagementSquadRole.Bound
                    ? [FastApproachOption(squad)]
                    : [EngagementOptionKind.Hold];
            }
            if (frame.Role == EngagementSquadRole.Routing)
            {
                return [FastApproachOption(squad)];
            }
            if (squad.IsInMelee || frame.Role == EngagementSquadRole.RearGuard && squad.IsInMelee)
            {
                return [EngagementOptionKind.CloseToContact];
            }
            if (frame.Role is EngagementSquadRole.Cover or EngagementSquadRole.RearGuard)
            {
                return [EngagementOptionKind.Hold, EngagementOptionKind.StepBack];
            }
            if (frame.Role == EngagementSquadRole.Standoff)
            {
                return [EngagementOptionKind.Hold];
            }
            if (frame.Role == EngagementSquadRole.Follow)
            {
                if (primary == null) return [EngagementOptionKind.Hold];
                float distance = BattleEngagementFrameBuilder.MinimumDistance(squad, primary);
                bool contactSeekerMustClose = profile.IsContactSeeking
                    && (HasNoViableRangedOption(profile)
                        || distance > profile.PreferredBandUpper);
                if (contactSeekerMustClose)
                {
                    return distance <= profile.MoveSpeed + BattleContactRules.MeleeContactAllowance
                        ? [EngagementOptionKind.CloseToContact]
                        : [FastApproachOption(squad)];
                }
                if (HasPursuitAimCommitment(squad, frame, primary))
                {
                    return [EngagementOptionKind.Hold];
                }
                return squad.CanRun
                    ? [
                        EngagementOptionKind.Hold,
                        EngagementOptionKind.JogToward,
                        EngagementOptionKind.RunToward
                    ]
                    : [EngagementOptionKind.Hold, EngagementOptionKind.JogToward];
            }
            if (frame.Role == EngagementSquadRole.Press)
            {
                if (primary == null) return [EngagementOptionKind.Hold];
                float distance = BattleEngagementFrameBuilder.MinimumDistance(squad, primary);
                return distance <= profile.MoveSpeed + BattleContactRules.MeleeContactAllowance
                    ? [EngagementOptionKind.CloseToContact]
                    : [FastApproachOption(squad)];
            }
            if (frame.Role == EngagementSquadRole.Pursuit)
            {
                if (primary == null) return [EngagementOptionKind.Hold];
                float distance = BattleEngagementFrameBuilder.MinimumDistance(squad, primary);
                bool contactSeekerMustClose = profile.IsContactSeeking
                    && (HasNoViableRangedOption(profile)
                        || distance > profile.PreferredBandUpper);
                if (contactSeekerMustClose)
                {
                    return distance <= profile.MoveSpeed + BattleContactRules.MeleeContactAllowance
                        ? [EngagementOptionKind.CloseToContact]
                        : [FastApproachOption(squad)];
                }
                if (HasPursuitAimCommitment(squad, frame, primary))
                {
                    return [EngagementOptionKind.Hold];
                }
                EngagementOptionKind fast = distance <= profile.MoveSpeed
                        + BattleContactRules.MeleeContactAllowance
                    ? EngagementOptionKind.CloseToContact
                    : FastApproachOption(squad);
                return new[] { EngagementOptionKind.Hold, EngagementOptionKind.JogToward, fast }
                    .Distinct()
                    .ToList();
            }

            if (primary == null)
            {
                return [EngagementOptionKind.Hold];
            }

            float primaryDistance = BattleEngagementFrameBuilder.MinimumDistance(squad, primary);
            bool noViableRangedOption = HasNoViableRangedOption(profile)
                || (profile.IsContactSeeking && primaryDistance > profile.PreferredBandUpper);
            List<EngagementOptionKind> result =
            [
                EngagementOptionKind.Hold,
                EngagementOptionKind.StepBack,
                EngagementOptionKind.StepForward,
                EngagementOptionKind.JogToward,
                EngagementOptionKind.CloseToContact
            ];
            bool hasRangedWeapon = squad.AbleSoldiers.Any(
                soldier => soldier.RangedWeapons.Count > 0);
            if (noViableRangedOption
                && hasRangedWeapon
                && primaryDistance > profile.MoveSpeed
                    + BattleContactRules.MeleeContactAllowance)
            {
                result = [FastApproachOption(squad)];
            }
            if (!profile.IsContactSeeking
                && primaryDistance > profile.MoveSpeed + BattleContactRules.MeleeContactAllowance)
            {
                result.Remove(EngagementOptionKind.CloseToContact);
                result.Add(FastApproachOption(squad));
            }
            if (noViableRangedOption && primaryDistance
                > BattleContactRules.MeleeContactAllowance)
            {
                result.Remove(EngagementOptionKind.Hold);
                result.Remove(EngagementOptionKind.StepBack);
                if (primaryDistance > profile.MoveSpeed
                    + BattleContactRules.MeleeContactAllowance)
                {
                    result.Remove(EngagementOptionKind.CloseToContact);
                    result.Add(FastApproachOption(squad));
                }
            }
            if (frame.InterposePoint.HasValue)
            {
                result.Add(EngagementOptionKind.MoveToInterpose);
            }
            return result.Distinct().ToList();
        }

        private EngagementOptionEvaluation EvaluateEngagementOption(
            BattleSquad squad,
            EngagementOptionKind kind,
            SquadEngagementFrame frame,
            BattleSquadCapabilityProfile profile,
            IReadOnlyDictionary<int, BattleSquadCapabilityProfile> profiles,
            IReadOnlyDictionary<int, SquadEngagementFrame> allFrames,
            BattleSquad primary,
            IReadOnlyCollection<BattleSquad> enemies)
        {
            SquadMovementTier tier = GetOptionTier(kind, squad, primary, frame);
            ValueTuple<float, float>? intended = GetIntendedDestination(
                squad,
                kind,
                frame,
                primary,
                allFrames);
            (float feasibleSpeed, ValueTuple<float, float> projectedCentroid) =
                ProjectFeasibleSquadEndpoint(squad, kind, tier, intended, primary, frame);
            ValueTuple<int, int>? direction = GetOptionDirection(
                squad,
                kind,
                frame,
                intended);
            (float enemyRemoval, float friendlyFire, float readiness,
                IReadOnlyList<PlannedSoldierAction> rootActions) =
                EvaluateImmediateActionValue(squad, tier, direction);
            float incoming = EvaluateIncomingNow(
                squad,
                feasibleSpeed,
                profiles,
                allFrames,
                enemies);
            (float meleeNow, float contactCommitment) = EvaluateContactTerms(
                squad,
                kind,
                primary,
                profile);
            IReadOnlyCollection<BattleSquad> friendlySquads = GetFriendlySquads(squad);
            EngagementPotential.Breakdown rootPotential = _potential.Evaluate(
                new EngagementPotential.State(
                    squad,
                    BattleEngagementFrameBuilder.Centroid(squad),
                    profile,
                    profiles,
                    allFrames,
                    enemies,
                    frame,
                    0,
                    primary,
                    null,
                    friendlySquads));
            EngagementPotential.Breakdown projectedPotential = _potential.Evaluate(
                new EngagementPotential.State(
                    squad,
                    projectedCentroid,
                    profile,
                    profiles,
                    allFrames,
                    enemies,
                    frame,
                    feasibleSpeed,
                    primary,
                    rootActions,
                    friendlySquads));
            float discountedNetRate =
                EngagementPotential.EngagementPotentialDiscount
                * projectedPotential.NetRateValue;
            float arrivalTimeValue = -rootPotential.NetRateValue;
            readiness = EngagementPotential.EngagementPotentialDiscount
                * projectedPotential.ReadinessValue
                - rootPotential.ReadinessValue;
            float roleTerm = EngagementPotential.EngagementPotentialDiscount
                * projectedPotential.RoleValue
                - rootPotential.RoleValue;
            float fireWindowValue = EngagementPotential.EngagementPotentialDiscount
                * projectedPotential.FireWindowValue
                - rootPotential.FireWindowValue;
            float moraleTerm = EngagementPotential.EngagementPotentialDiscount
                * projectedPotential.MoraleValue
                - rootPotential.MoraleValue;
            float commandTerm = EngagementPotential.EngagementPotentialDiscount
                * projectedPotential.CommandValue
                - rootPotential.CommandValue;
            float accessTerm = EngagementPotential.EngagementPotentialDiscount
                * projectedPotential.AccessValue
                - rootPotential.AccessValue;
            List<float> future = [discountedNetRate];
            float score = EngagementPotential.ScoreTransition(
                enemyRemoval - friendlyFire - incoming + meleeNow,
                rootPotential,
                projectedPotential,
                contactCommitment);
            return new EngagementOptionEvaluation(
                kind,
                tier,
                intended,
                feasibleSpeed,
                enemyRemoval,
                friendlyFire,
                readiness,
                fireWindowValue,
                incoming,
                meleeNow,
                future,
                arrivalTimeValue,
                roleTerm,
                contactCommitment,
                score,
                rootActions,
                moraleTerm,
                commandTerm,
                accessTerm);
        }

        private static SquadMovementTier GetOptionTier(
            EngagementOptionKind kind,
            BattleSquad squad,
            BattleSquad primary,
            SquadEngagementFrame frame) => kind switch
        {
            EngagementOptionKind.Hold => SquadMovementTier.Stationary,
            EngagementOptionKind.StepBack or EngagementOptionKind.StepForward =>
                SquadMovementTier.Walk,
            EngagementOptionKind.JogToward => SquadMovementTier.Jog,
            EngagementOptionKind.MoveToInterpose => InterposeTier(squad, frame),
            EngagementOptionKind.CloseToContact => primary != null
                && BattleEngagementFrameBuilder.MinimumDistance(squad, primary)
                    <= squad.GetSquadMove() + 1
                        ? SquadMovementTier.InMelee
                        : squad.CanRun
                            ? SquadMovementTier.Run
                            : SquadMovementTier.Jog,
            EngagementOptionKind.RunToward => squad.CanRun
                ? SquadMovementTier.Run
                : SquadMovementTier.Jog,
            _ => SquadMovementTier.Stationary
        };

        private static SquadMovementTier InterposeTier(
            BattleSquad squad,
            SquadEngagementFrame frame)
        {
            if (!frame.InterposePoint.HasValue) return SquadMovementTier.Stationary;
            (float x, float y) = BattleEngagementFrameBuilder.Centroid(squad);
            float dx = frame.InterposePoint.Value.Item1 - x;
            float dy = frame.InterposePoint.Value.Item2 - y;
            float distance = (float)Math.Sqrt(dx * dx + dy * dy);
            float move = squad.GetSquadMove();
            if (distance <= move * WalkSpeedMultiplier) return SquadMovementTier.Walk;
            if (distance <= move * JogSpeedMultiplier) return SquadMovementTier.Jog;
            return squad.CanRun ? SquadMovementTier.Run : SquadMovementTier.Jog;
        }

        private static ValueTuple<float, float>? GetIntendedDestination(
            BattleSquad squad,
            EngagementOptionKind kind,
            SquadEngagementFrame frame,
            BattleSquad primary,
            IReadOnlyDictionary<int, SquadEngagementFrame> allFrames)
        {
            if (kind == EngagementOptionKind.MoveToInterpose) return frame.InterposePoint;
            if (primary == null) return null;
            ValueTuple<float, float> target = BattleEngagementFrameBuilder.Centroid(primary);
            if (!IsPursuitRole(frame.Role)
                || allFrames.GetValueOrDefault(primary.Id)?.Role is not
                    (EngagementSquadRole.Bound or EngagementSquadRole.Routing))
            {
                return target;
            }

            SquadEngagementFrame quarryFrame = allFrames[primary.Id];
            float leadX;
            float leadY;
            if (quarryFrame.Role == EngagementSquadRole.Bound
                && quarryFrame.FixedHeading.HasValue)
            {
                (int x, int y) = BattleForcePlanner.GetHeadingVector(
                    quarryFrame.FixedHeading.Value);
                leadX = x;
                leadY = y;
            }
            else
            {
                (float x, float y) = BattleEngagementFrameBuilder.Centroid(squad);
                leadX = target.Item1 - x;
                leadY = target.Item2 - y;
            }
            float length = (float)Math.Sqrt(leadX * leadX + leadY * leadY);
            if (length <= 0.0001f) return target;
            float leadDistance = Math.Max(0, frame.QuarryRunSpeed);
            return (
                target.Item1 + leadX / length * leadDistance,
                target.Item2 + leadY / length * leadDistance);
        }

        private static ValueTuple<int, int>? GetOptionDirection(
            BattleSquad squad,
            EngagementOptionKind kind,
            SquadEngagementFrame frame,
            ValueTuple<float, float>? intended)
        {
            if (kind == EngagementOptionKind.Hold) return null;
            if (kind == EngagementOptionKind.StepBack && frame.FixedHeading.HasValue)
            {
                return BattleForcePlanner.GetHeadingVector(frame.FixedHeading.Value);
            }
            (float x, float y) = BattleEngagementFrameBuilder.Centroid(squad);
            float targetX = intended?.Item1 ?? x;
            float targetY = intended?.Item2 ?? y;
            int dx = Math.Sign(targetX - x);
            int dy = Math.Sign(targetY - y);
            if (kind == EngagementOptionKind.StepBack)
            {
                dx = -dx;
                dy = -dy;
            }
            return new ValueTuple<int, int>(dx, dy);
        }

        private (float FeasibleSpeed, ValueTuple<float, float> Centroid)
            ProjectFeasibleSquadEndpoint(
                BattleSquad squad,
                EngagementOptionKind kind,
                SquadMovementTier tier,
                ValueTuple<float, float>? intended,
                BattleSquad primary,
                SquadEngagementFrame frame)
        {
            if (tier == SquadMovementTier.Stationary)
            {
                (float x, float y) = BattleEngagementFrameBuilder.Centroid(squad);
                return (0, (x, y));
            }
            BattleGridManager overlay = (BattleGridManager)_grid.Clone();
            float distanceTotal = 0;
            float xTotal = 0;
            float yTotal = 0;
            int count = 0;
            foreach (BattleSoldier soldier in squad.AbleSoldiers
                .Where(IsPlaced)
                .OrderBy(member => member.Soldier.Id))
            {
                ValueTuple<int, int> line = MovementLineFor(
                    soldier,
                    kind,
                    frame,
                    primary,
                    intended);
                float budget = GetMovementBudget(soldier, tier);
                ValueTuple<int, int> desired =
                    CalculateMovementAlongLine(line, budget);
                ValueTuple<int, int> target = (
                    soldier.TopLeft.Value.Item1 + desired.Item1,
                    soldier.TopLeft.Value.Item2 + desired.Item2);
                ushort orientation = CalculateOrientationFromVector(line, soldier, tier);
                ValueTuple<int, int> endpoint = FindBestLocation(
                    soldier,
                    soldier.TopLeft.Value,
                    target,
                    budget,
                    orientation,
                    overlay);
                overlay.ReserveMoveDestination(soldier, endpoint, orientation);
                int dx = endpoint.Item1 - soldier.TopLeft.Value.Item1;
                int dy = endpoint.Item2 - soldier.TopLeft.Value.Item2;
                distanceTotal += (float)Math.Sqrt(dx * dx + dy * dy);
                xTotal += endpoint.Item1;
                yTotal += endpoint.Item2;
                count++;
            }
            if (count == 0) return (0, (0, 0));
            return (distanceTotal / count, (xTotal / count, yTotal / count));
        }

        internal ValueTuple<int, int> MovementLineFor(
            BattleSoldier soldier,
            EngagementOptionKind kind,
            SquadEngagementFrame frame,
            BattleSquad primary,
            ValueTuple<float, float>? intended)
        {
            if (kind == EngagementOptionKind.StepBack && frame.FixedHeading.HasValue)
            {
                ValueTuple<int, int> heading = BattleForcePlanner.GetHeadingVector(
                    frame.FixedHeading.Value);
                return (heading.Item1 * 10_000, heading.Item2 * 10_000);
            }
            float targetX = intended?.Item1
                ?? primary?.AbleSoldiers.FirstOrDefault(IsPlaced)?.TopLeft?.Item1
                ?? soldier.TopLeft.Value.Item1;
            float targetY = intended?.Item2
                ?? primary?.AbleSoldiers.FirstOrDefault(IsPlaced)?.TopLeft?.Item2
                ?? soldier.TopLeft.Value.Item2;
            int dx = (int)Math.Round(targetX - soldier.TopLeft.Value.Item1);
            int dy = (int)Math.Round(targetY - soldier.TopLeft.Value.Item2);
            if (kind == EngagementOptionKind.StepBack)
            {
                dx = -dx;
                dy = -dy;
            }
            if (dx == 0 && dy == 0) dy = 1;
            return (dx, dy);
        }

        private (float EnemyRemoval, float FriendlyFire, float Readiness,
            IReadOnlyList<PlannedSoldierAction> RootActions)
            EvaluateImmediateActionValue(
                BattleSquad squad,
                SquadMovementTier tier,
                ValueTuple<int, int>? direction)
        {
            float bulk = tier switch
            {
                SquadMovementTier.Walk => WalkBulkMultiplier,
                SquadMovementTier.Jog => FullBulkMultiplier,
                _ => 0
            };
            float removal = 0;
            float friendly = 0;
            float readiness = 0;
            Dictionary<int, float> awardedByTarget = [];
            List<PlannedSoldierAction> rootActions = [];
            foreach (BattleSoldier soldier in squad.AbleSoldiers.OrderBy(member => member.Soldier.Id))
            {
                if (!IsPlaced(soldier)) continue;
                PlannedSoldierAction action = _soldierActions.PlanRootAction(
                    soldier,
                    tier,
                    bulk,
                    direction);
                rootActions.Add(action);
                float awardedRemoval = action.ExpectedEnemyBattleValueRemoved;
                if (action.TargetId.HasValue && awardedRemoval > 0)
                {
                    int targetId = action.TargetId.Value;
                    float cap = _soldierMap.TryGetValue(targetId, out BattleSoldier target)
                        ? GetBattleValue(target)
                        : awardedRemoval;
                    float prior = awardedByTarget.GetValueOrDefault(targetId);
                    float award = Math.Min(
                        awardedRemoval,
                        Math.Max(0, cap - prior));
                    awardedByTarget[targetId] = prior + award;
                    removal += award;
                }
                friendly += action.ExpectedFriendlyBattleValueLost;
                readiness += action.ReadinessValue;
            }
            float enemyCap = _soldierMap.Values
                .Where(target => target.IsCombatEffective
                    && IsPlaced(target)
                    && target.BattleSquad != squad
                    && _grid.GetSoldierSide(target.Soldier.Id)
                        != _grid.GetSoldierSide(squad.AbleSoldiers[0].Soldier.Id))
                .Sum(GetBattleValue);
            return (Math.Min(removal, enemyCap), friendly, readiness, rootActions);
        }

        private float EvaluateIncomingNow(
            BattleSquad squad,
            float feasibleSpeed,
            IReadOnlyDictionary<int, BattleSquadCapabilityProfile> profiles,
            IReadOnlyDictionary<int, SquadEngagementFrame> frames,
            IReadOnlyCollection<BattleSquad> enemies) =>
            _exchange.EvaluateIncomingNow(squad, feasibleSpeed, profiles, frames, enemies);

        private (float MeleeNow, float Commitment) EvaluateContactTerms(
            BattleSquad squad,
            EngagementOptionKind kind,
            BattleSquad primary,
            BattleSquadCapabilityProfile profile) =>
            _exchange.EvaluateContactTerms(squad, kind, primary, profile);

        private bool HasPursuitAimCommitment(
            BattleSquad squad,
            SquadEngagementFrame frame,
            BattleSquad primary)
        {
            if (!IsPursuitRole(frame.Role)
                || squad.LastEngagementOptionKind != EngagementOptionKind.Hold
                || primary == null)
            {
                return false;
            }
            return squad.AbleSoldiers.Any(soldier =>
                soldier.Aim is ValueTuple<int, RangedWeapon, int> aim
                && _soldierMap.TryGetValue(aim.Item1, out BattleSoldier target)
                && target.BattleSquad?.Id == primary.Id
                && _ranged.IsExistingAimStillViable(soldier));
        }

        internal BattleSquad ResolvePrimary(
            SquadEngagementFrame frame,
            IReadOnlyCollection<BattleSquad> preferredTargets,
            IReadOnlyCollection<BattleSquad> allTargets)
        {
            IEnumerable<BattleSquad> targets = (preferredTargets ?? [])
                .Concat(allTargets ?? [])
                .Where(target => target != null)
                .DistinctBy(target => target.Id);
            if (frame.PrimaryCounterpartSquadId.HasValue)
            {
                BattleSquad primary = targets.FirstOrDefault(
                    target => target.Id == frame.PrimaryCounterpartSquadId.Value);
                if (primary != null) return primary;
            }
            return targets.OrderBy(target => target.Id).FirstOrDefault();
        }

        internal void LogEngagementOptions(SquadEngagementDecision decision)
        {
            if (!BattleLog.IsEnabled) return;
            float runnerUp = decision.Candidates
                .Where(candidate => !ReferenceEquals(candidate, decision.Chosen))
                .Select(candidate => candidate.Score)
                .DefaultIfEmpty(decision.Chosen.Score)
                .Max();
            string revealedEnemyChoices = RenderRevealedEnemyChoices(decision.Frame);
            foreach (EngagementOptionEvaluation candidate in decision.Candidates
                .OrderBy(option => option.Kind))
            {
                BattleLog.Write(new BattleDecisionTrace("ENGAGE_EVAL",
                [
                    BattleDecisionTrace.Field("turn", TraceTurnNumber),
                    BattleDecisionTrace.Field("side", TraceSideLabel ?? "none"),
                    BattleDecisionTrace.Field("squad", decision.Squad.Id),
                    BattleDecisionTrace.Field("role", decision.Frame.Role),
                    BattleDecisionTrace.Field("kind", candidate.Kind),
                    BattleDecisionTrace.Field("tier", candidate.Tier),
                    BattleDecisionTrace.Field("intended", candidate.IntendedDestination?.ToString() ?? "none"),
                    BattleDecisionTrace.Field("feasible_speed", candidate.FeasibleSpeed),
                    BattleDecisionTrace.Field("outgoing", candidate.ImmediateEnemyRemoval),
                    BattleDecisionTrace.Field("friendly_fire", candidate.ImmediateFriendlyFire),
                    BattleDecisionTrace.Field("readiness", candidate.ReadinessValue),
                    BattleDecisionTrace.Field("fire_window", candidate.FireWindowValue),
                    BattleDecisionTrace.Field("incoming", candidate.IncomingNow),
                    BattleDecisionTrace.Field("melee", candidate.MeleeNow),
                    BattleDecisionTrace.Field("future", string.Join(',', candidate.FutureExchange.Select(value => value.ToString("0.###", CultureInfo.InvariantCulture)))),
                    BattleDecisionTrace.Field("arrival_value", candidate.ArrivalTimeValue),
                    BattleDecisionTrace.Field("role_term", candidate.RoleTerm),
                    BattleDecisionTrace.Field("access_potential", candidate.AccessPotentialValue),
                    BattleDecisionTrace.Field("morale_potential", candidate.MoralePotentialValue),
                    BattleDecisionTrace.Field("command_potential", candidate.CommandPotentialValue),
                    BattleDecisionTrace.Field("commitment", candidate.ContactCommitmentCost),
                    BattleDecisionTrace.Field("score", candidate.Score),
                    BattleDecisionTrace.Field("chosen", candidate.Kind == decision.Chosen.Kind),
                    BattleDecisionTrace.Field("margin", decision.Chosen.Score - runnerUp),
                    BattleDecisionTrace.Field("baseline", decision.Frame.BaselinePosture),
                    BattleDecisionTrace.Field("enemy_revealed", revealedEnemyChoices)
                ]).Render());
            }
        }

        private string RenderRevealedEnemyChoices(SquadEngagementFrame frame)
        {
            Dictionary<int, BattleSquad> squads = _soldierMap.Values
                .Select(soldier => soldier.BattleSquad)
                .Where(squad => squad != null)
                .DistinctBy(squad => squad.Id)
                .ToDictionary(squad => squad.Id);
            string revealed = string.Join(',', frame.PairWeights.Keys
                .OrderBy(id => id)
                .Select(id => squads.TryGetValue(id, out BattleSquad squad)
                    ? $"{id}:{squad.LastEngagementOptionKind?.ToString() ?? "none"}"
                    : $"{id}:missing"));
            return string.IsNullOrEmpty(revealed) ? "none" : revealed;
        }

        private IReadOnlyCollection<BattleSquad> GetFriendlySquads(BattleSquad squad)
        {
            BattleSoldier anchor = squad?.AbleSoldiers.FirstOrDefault(IsPlaced);
            if (anchor == null)
            {
                return squad == null ? [] : [squad];
            }

            bool side = _grid.GetSoldierSide(anchor.Soldier.Id);
            return _soldierMap.Values
                .Select(soldier => soldier.BattleSquad)
                .Where(candidate => candidate != null
                    && candidate.AbleSoldiers.Any(member =>
                        IsPlaced(member)
                        && _grid.GetSoldierSide(member.Soldier.Id) == side))
                .DistinctBy(candidate => candidate.Id)
                .OrderBy(candidate => candidate.Id)
                .ToList();
        }

        private bool IsPlaced(BattleSoldier soldier) => _services.IsPlaced(soldier);

        private static float GetMovementBudget(BattleSoldier soldier, SquadMovementTier tier) =>
            SoldierMovementProjector.GetMovementBudget(soldier, tier);

        private ValueTuple<int, int> CalculateMovementAlongLine(
            ValueTuple<int, int> line,
            float moveSpeed) =>
            _movement.CalculateMovementAlongLine(line, moveSpeed);

        private ushort CalculateOrientationFromVector(
            ValueTuple<int, int> vector,
            BattleSoldier soldier,
            SquadMovementTier tier) =>
            _movement.CalculateOrientationFromVector(vector, soldier, tier);

        private ValueTuple<int, int> FindBestLocation(
            BattleSoldier soldier,
            ValueTuple<int, int> startingPoint,
            ValueTuple<int, int> targetPoint,
            float speed,
            ushort orientation,
            BattleGridManager grid) =>
            _movement.FindBestLocation(
                soldier,
                startingPoint,
                targetPoint,
                speed,
                orientation,
                grid);

        private static float GetBattleValue(BattleSoldier soldier) =>
            SquadPlanningServices.BattleValueOf(soldier);
    }
}
