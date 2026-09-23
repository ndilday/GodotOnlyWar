using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Domain.Equippables;
using OnlyWar.Battles.Models;

namespace OnlyWar.Battles;

/// <summary>The kind of attack supplied by a counterfactual pursuit estimate.</summary>
public enum BattleAttackMode
{
    Ranged,
    Melee,
    Unreachable
}

/// <summary>
/// Preparation actions that must precede a projected ranged opportunity. This is a flags enum
/// because a carried empty weapon may need both Ready and Reload before it can fire.
/// </summary>
[Flags]
public enum BattleAttackPreparation
{
    None = 0,
    Ready = 1,
    Reload = 2,
    Aim = 4
}

/// <summary>
/// A pair-local, counterfactual attack opportunity. <see cref="ElapsedTurns"/> always counts
/// attack phases from the current turn-start attack phase: a shot available now is zero, while
/// contact or a destination reached during this turn is one. <see cref="MovementTurns"/> retains
/// the continuous movement estimate for diagnostics without changing that comparison convention.
/// </summary>
public sealed record BattleAttackOpportunity(
    int PursuerSquadId,
    int QuarrySquadId,
    BattleAttackMode Mode,
    BattleAttackPreparation Preparation,
    bool RequiresMovement,
    float ElapsedTurns,
    float MovementTurns,
    int? ShooterId,
    int? TargetId,
    int? WeaponTemplateId,
    string Reason,
    float? DestinationRange = null)
{
    /// <summary>
    /// Geometry used by the selected counterfactual route. This is diagnostic context, not a
    /// promise that the route was executed. An unreachable pair still carries its nearest-pair
    /// geometry when the projection had a valid pair to inspect.
    /// </summary>
    internal BattleInterceptionProjection.PairInput? Geometry { get; init; }

    /// <summary>Named destination condition supplied to the projection.</summary>
    internal string DestinationCondition { get; init; } = "none";

    public bool IsReachable =>
        Mode != BattleAttackMode.Unreachable
        && !float.IsNaN(ElapsedTurns)
        && !float.IsPositiveInfinity(ElapsedTurns);

    public string Kind => Mode switch
    {
        BattleAttackMode.Melee => "melee",
        BattleAttackMode.Unreachable => "unreachable",
        _ when Preparation == BattleAttackPreparation.None
            && !RequiresMovement
            && ElapsedTurns <= BattleInterceptionProjection.GeometryTolerance => "ranged_now",
        _ when Preparation == BattleAttackPreparation.None => "ranged_waiting",
        _ => "ranged_prepared"
    };

    internal static BattleAttackOpportunity Unreachable(
        int pursuerSquadId,
        int quarrySquadId,
        string reason,
        BattleInterceptionProjection.PairInput? geometry = null,
        string destinationCondition = "none") => new(
            pursuerSquadId,
            quarrySquadId,
            BattleAttackMode.Unreachable,
            BattleAttackPreparation.None,
            false,
            float.PositiveInfinity,
            float.PositiveInfinity,
            null,
            null,
            null,
            reason)
        {
            Geometry = geometry,
            DestinationCondition = destinationCondition
        };
}

/// <summary>
/// Projects useful ranged fire and melee contact for concrete squad pairs. The ranged side is
/// deliberately an adapter around <see cref="RangedTargetSelector"/>: this type does not invent a
/// second hit, armour, ammunition, or aim model. The geometry helper supplies only the time at
/// which a pair can enter a range; it never turns an approximate range into proof of an attack.
/// The useful-range boundary is a named, deterministic sampled/refined boundary of that live
/// evaluator. It is a destination-search approximation, not a replacement for the calibrated
/// useful-fire definition and not evidence that a shot executed.
/// </summary>
internal sealed class BattleAttackOpportunityProjection
{
    private const int MaximumPreparationTurns = RangedTargetSelector.FullAimBonusTurns + 8;
    private const int RangeSampleCount = 32;
    private const int RangeRefinementCount = 8;

    private readonly RangedTargetSelector _ranged;

    // The sampled useful-range boundary is a function of exactly the inputs the live evaluator
    // memoizes on (shooter, target, weapon template, target speed, ammunition), with range and
    // aim fixed by the sampler. It therefore shares the evaluator's lifetime: one projection per
    // frozen battlefield, never across a turn's movement or casualties.
    private readonly Dictionary<UsefulRangeKey, float> _usefulRanges = [];

    // Opt-in whole-pair memo. Only a caller that guarantees geometry, membership and weapon state
    // are frozen for the projection's lifetime may enable it (BattleWithdrawalService's
    // frozen-geometry scope). Direct callers keep projecting live state on every call.
    private readonly Dictionary<PairKey, PairResult> _pairResults;

    internal BattleAttackOpportunityProjection(
        RangedTargetSelector ranged,
        bool memoizePairs = false)
    {
        _ranged = ranged ?? throw new ArgumentNullException(nameof(ranged));
        _pairResults = memoizePairs ? [] : null;
    }

    private readonly record struct UsefulRangeKey(
        int ShooterId,
        int TargetId,
        int WeaponTemplateId,
        float TargetSpeed,
        int AvailableAmmo);

    private readonly record struct PairKey(
        int PursuerId,
        int QuarryId,
        BattleInterceptionProjection.Point QuarryHeading,
        float PursuerMoveSpeed,
        float QuarryMoveSpeed);

    internal readonly record struct PairInput(
        BattleSquad Pursuer,
        BattleSquad Quarry,
        BattleInterceptionProjection.Point QuarryHeading,
        float PursuerMoveSpeed,
        float QuarryMoveSpeed);

    internal sealed record PairResult(
        BattleAttackOpportunity Ranged,
        BattleAttackOpportunity Melee,
        BattleAttackOpportunity Earliest);

    internal readonly record struct AggregateResult(
        BattleAttackOpportunity Ranged,
        BattleAttackOpportunity Melee,
        BattleAttackOpportunity Earliest,
        int PairCount)
    {
        internal bool HasRanged => Ranged?.IsReachable == true;
        internal bool HasMelee => Melee?.IsReachable == true;
        internal bool IsReachable => Earliest?.IsReachable == true;
    }

    /// <summary>How much of the ranged search a caller needs for one pair.</summary>
    internal enum RangedSearch
    {
        /// <summary>Every route: a shot now, preparation, movement into range, and aim.</summary>
        Full,

        /// <summary>
        /// Only a shot available now. Anything else is reported unreachable, so a caller may ask
        /// for this only when a later opportunity could no longer change its answer.
        /// </summary>
        ImmediateOnly,

        /// <summary>No ranged search at all.</summary>
        None
    }

    internal PairResult ProjectPair(PairInput input) => ProjectPair(input, RangedSearch.Full);

    internal PairResult ProjectPair(PairInput input, RangedSearch search) =>
        ProjectPair(input, search, acceptBelowTurns: 0);

    /// <summary>
    /// Projects one pair. With less than a <see cref="RangedSearch.Full"/> search, ranged routes
    /// that were not searched are reported unreachable; a caller may do this only when an earlier
    /// pair in its own selection order has already settled what it needs (for example an attack no
    /// later pair can precede, see <see cref="IsImmediate"/>). Such a partial result is never
    /// memoized.
    ///
    /// <para>With a positive <paramref name="acceptBelowTurns"/> a full search also stops at the
    /// first ranged opportunity earlier than that many turns, which need not be the earliest. A
    /// caller uses this when all it needs to know is whether such an opportunity exists; the
    /// result is then partial and not memoized either.</para>
    /// </summary>
    internal PairResult ProjectPair(PairInput input, RangedSearch search, float acceptBelowTurns)
    {
        if (!IsProjectable(input.Pursuer) || !IsProjectable(input.Quarry))
        {
            BattleAttackOpportunity unreachable = BattleAttackOpportunity.Unreachable(
                input.Pursuer?.Id ?? 0,
                input.Quarry?.Id ?? 0,
                "unplaced_or_inactive_pair");
            return new PairResult(unreachable, unreachable, unreachable);
        }

        PairKey key = new(
            input.Pursuer.Id,
            input.Quarry.Id,
            input.QuarryHeading,
            input.PursuerMoveSpeed,
            input.QuarryMoveSpeed);
        if (_pairResults != null && _pairResults.TryGetValue(key, out PairResult cached))
        {
            return cached;
        }

        BattleInterceptionProjection.PairInput meleeInput = BuildMeleeInput(input);
        BattleInterceptionProjection.PairResult meleeProjection =
            BattleInterceptionProjection.Project(meleeInput);
        BattleAttackOpportunity melee = meleeProjection.IsReachable
            ? new BattleAttackOpportunity(
                input.Pursuer.Id,
                input.Quarry.Id,
                BattleAttackMode.Melee,
                BattleAttackPreparation.None,
                meleeProjection.ContactTurns > BattleInterceptionProjection.GeometryTolerance,
                meleeProjection.AttackableContactTurns,
                meleeProjection.ContactTurns,
                null,
                null,
                null,
                meleeProjection.Reason,
                BattleContactRules.MeleeContactAllowance)
            {
                Geometry = meleeInput,
                DestinationCondition = "melee_contact"
            }
            : BattleAttackOpportunity.Unreachable(
                input.Pursuer.Id,
                input.Quarry.Id,
                meleeProjection.Reason,
                meleeInput,
                "melee_contact");

        if (search == RangedSearch.None)
        {
            BattleAttackOpportunity skipped = BattleAttackOpportunity.Unreachable(
                input.Pursuer.Id,
                input.Quarry.Id,
                "preceded_by_earlier_pair",
                meleeInput,
                "useful_range");
            return new PairResult(skipped, melee, ChooseEarlier(skipped, melee));
        }
        if (search == RangedSearch.ImmediateOnly)
        {
            BattleAttackOpportunity immediate = ProjectRanged(
                input,
                immediateOnly: true,
                acceptBelowTurns: 0);
            return new PairResult(immediate, melee, ChooseEarlier(immediate, melee));
        }

        BattleAttackOpportunity ranged = ProjectRanged(
            input,
            immediateOnly: false,
            acceptBelowTurns);
        BattleAttackOpportunity earliest = ChooseEarlier(ranged, melee);
        PairResult result = new(ranged, melee, earliest);
        if (_pairResults != null && acceptBelowTurns <= 0) _pairResults[key] = result;
        return result;
    }

    /// <summary>
    /// Whether an opportunity sits at the minimum of <see cref="Earliest"/>'s ordering on every
    /// key ahead of the squad/soldier/weapon ids: reachable, zero elapsed attack phases, no
    /// preparation, no movement. Nothing projected later in id order can precede it, so a search
    /// that visits candidates in that id order may stop at the first one.
    /// </summary>
    internal static bool IsImmediate(BattleAttackOpportunity opportunity) =>
        opportunity?.IsReachable == true
        && opportunity.ElapsedTurns <= 0
        && opportunity.Preparation == BattleAttackPreparation.None
        && !opportunity.RequiresMovement;

    internal AggregateResult EarliestPair(IEnumerable<PairInput> inputs) =>
        EarliestPair(inputs, RangedSearch.Full, acceptBelowTurns: 0);

    /// <summary>
    /// Aggregates pair projections. With <see cref="RangedSearch.Full"/> and no
    /// <paramref name="acceptBelowTurns"/> the ranged, melee and overall results are the earliest
    /// of every pair. <see cref="RangedSearch.ImmediateOnly"/> finds only a shot available now;
    /// a positive <paramref name="acceptBelowTurns"/> stops at the first ranged opportunity earlier
    /// than that, which answers "is there one?" without finding the earliest.
    /// </summary>
    internal AggregateResult EarliestPair(
        IEnumerable<PairInput> inputs,
        RangedSearch search,
        float acceptBelowTurns)
    {
        // Pairs are visited in the same pursuer/quarry order that Earliest uses to break ties.
        // Once one pair can shoot now, no later pair can supply an earlier ranged opportunity or
        // an earlier overall one, so later pairs only need their (cheap) melee projection. The
        // same holds, for the caller's purpose, once a bounded search has found its answer.
        //
        // A search that only asks whether an opportunity exists (bounded, or a shot now) does not
        // care which one it finds, so it tries the nearest pairs first, where one is likeliest.
        bool existenceSearch = acceptBelowTurns > 0 || search == RangedSearch.ImmediateOnly;
        IEnumerable<PairInput> ordered = inputs ?? [];
        ordered = existenceSearch
            ? ordered
                .OrderBy(NearestSeparation)
                .ThenBy(input => input.Pursuer?.Id ?? int.MaxValue)
                .ThenBy(input => input.Quarry?.Id ?? int.MaxValue)
            : ordered
                .OrderBy(input => input.Pursuer?.Id ?? int.MaxValue)
                .ThenBy(input => input.Quarry?.Id ?? int.MaxValue);
        List<PairResult> projections = [];
        bool rangedSettled = false;
        foreach (PairInput input in ordered)
        {
            PairResult projection = ProjectPair(
                input,
                rangedSettled ? RangedSearch.None : search,
                acceptBelowTurns);
            projections.Add(projection);
            if (IsImmediate(projection.Ranged)
                || (projection.Ranged.IsReachable
                    && projection.Ranged.ElapsedTurns < acceptBelowTurns))
            {
                rangedSettled = true;
            }
        }
        if (projections.Count == 0)
        {
            BattleAttackOpportunity none = BattleAttackOpportunity.Unreachable(0, 0,
                "no_valid_pair");
            return new AggregateResult(none, none, none, 0);
        }

        BattleAttackOpportunity ranged = Earliest(
            projections.Select(projection => projection.Ranged));
        BattleAttackOpportunity melee = Earliest(
            projections.Select(projection => projection.Melee));
        BattleAttackOpportunity earliest = Earliest(
            projections.Select(projection => projection.Earliest));
        return new AggregateResult(ranged, melee, earliest, projections.Count);
    }

    private BattleAttackOpportunity ProjectRanged(
        PairInput input,
        bool immediateOnly,
        float acceptBelowTurns)
    {
        // Shooter, target and weapon are visited in the same order Earliest uses to break ties,
        // so the first immediate candidate is the one Earliest would select from the full list.
        // A search that only asks whether an opportunity exists tries each shooter's nearest
        // targets first instead; which one it finds does not matter to it.
        bool existenceSearch = immediateOnly || acceptBelowTurns > 0;
        List<BattleAttackOpportunity> candidates = [];
        foreach (BattleSoldier shooter in input.Pursuer.AbleSoldiers
            .Where(IsPlaced)
            .OrderBy(soldier => soldier.Soldier.Id))
        {
            IEnumerable<BattleSoldier> targets = input.Quarry.AbleSoldiers.Where(IsPlaced);
            targets = existenceSearch
                ? targets
                    .OrderBy(target => Distance(shooter, target))
                    .ThenBy(target => target.Soldier.Id)
                : targets.OrderBy(target => target.Soldier.Id);
            foreach (BattleSoldier target in targets)
            {
                foreach (RangedWeapon weapon in shooter.RangedWeapons
                    .Where(weapon => weapon != null && !weapon.Template.IsTemplateWeapon)
                    .OrderBy(weapon => weapon.Template.Id))
                {
                    BattleAttackOpportunity candidate = ProjectWeapon(
                        input,
                        shooter,
                        target,
                        weapon,
                        immediateOnly);
                    if (!candidate.IsReachable) continue;
                    if (IsImmediate(candidate)) return candidate;
                    if (candidate.ElapsedTurns < acceptBelowTurns) return candidate;
                    candidates.Add(candidate);
                }
            }
        }

        return candidates.Count == 0
            ? BattleAttackOpportunity.Unreachable(
                input.Pursuer.Id,
                input.Quarry.Id,
                "no_attainable_ranged_attack",
                BuildMeleeInput(input),
                "useful_range")
            : Earliest(candidates);
    }

    private BattleAttackOpportunity ProjectWeapon(
        PairInput input,
        BattleSoldier shooter,
        BattleSoldier target,
        RangedWeapon weapon,
        bool immediateOnly)
    {
        if (!CanUseWeapon(shooter, weapon))
        {
            return BattleAttackOpportunity.Unreachable(
                input.Pursuer.Id,
                input.Quarry.Id,
                "weapon_not_usable",
                BuildGeometryInput(input, shooter, target, input.PursuerMoveSpeed),
                "useful_range");
        }

        // A weapon that is empty now and can never be refilled (typed ammunition with no reserve,
        // a spent consumable) has no route to a shot, so every search below would fail. Say so
        // before sampling its useful range: once a force has shot itself dry this is most of its
        // weapons, and each one otherwise costs dozens of shot evaluations per pair.
        if (!weapon.CanFire && !weapon.CanReload && !weapon.IsSelfRegenerating)
        {
            return BattleAttackOpportunity.Unreachable(
                input.Pursuer.Id,
                input.Quarry.Id,
                "out_of_ammunition",
                BuildGeometryInput(input, shooter, target, input.PursuerMoveSpeed),
                "useful_range");
        }

        bool readied = shooter.EquippedRangedWeapons.Contains(weapon);
        bool canReady = readied
            || weapon.Template.IsThrown
            || (int)weapon.Template.Location <= shooter.FunctioningHands;
        if (!canReady) return BattleAttackOpportunity.Unreachable(
            input.Pursuer.Id,
            input.Quarry.Id,
            "cannot_ready_weapon",
            BuildGeometryInput(input, shooter, target, input.PursuerMoveSpeed),
            "useful_range");

        int currentAmmo = CurrentAmmunitionForEvaluation(weapon);
        float currentRange = Distance(shooter, target);
        int? currentAim = ExistingAimFor(shooter, target, weapon);
        List<BattleAttackOpportunity> candidates = [];

        if (readied
            && CanFire(weapon, currentAmmo)
            && !ReloadActionRequired(weapon))
        {
            RangedTargetEvaluation current = EvaluateShot(
                shooter,
                target,
                weapon,
                currentRange,
                input.QuarryMoveSpeed,
                currentAmmo,
                currentAim,
                moving: false);
            if (IsUseful(current))
            {
                // A shot available now is the minimum every route below could reach, and it is
                // first in the candidate list, so the route searches cannot change the result.
                return Ranged(
                    input,
                    shooter,
                    target,
                    weapon,
                    BuildGeometryInput(input, shooter, target, input.PursuerMoveSpeed),
                    BattleAttackPreparation.None,
                    requiresMovement: false,
                    elapsedTurns: 0,
                    movementTurns: 0,
                    "shot_available_now",
                    FindSampledUsefulRange(
                        shooter,
                        target,
                        weapon,
                        input.QuarryMoveSpeed,
                        PotentialAmmunitionForEvaluation(weapon)));
            }
        }

        if (immediateOnly)
        {
            return BattleAttackOpportunity.Unreachable(
                input.Pursuer.Id,
                input.Quarry.Id,
                "no_shot_available_now",
                BuildGeometryInput(input, shooter, target, input.PursuerMoveSpeed),
                "useful_range");
        }

        // A target outside the weapon's reach that the moving shooter can never bring inside it
        // has no shot on any route: every route evaluates its shot at a projected range, and a
        // range beyond MaximumRange is no shot. Say so before sampling the useful range. In a
        // long pursuit this is most of a squad's shooter/target pairs, and the sampling is the
        // expensive part of the search.
        if (currentRange > EffectiveMaximumRange(shooter, weapon)
            && !BattleInterceptionProjection.ProjectToDestination(
                    BuildGeometryInput(input, shooter, target, input.PursuerMoveSpeed),
                    EffectiveMaximumRangeForProjection(weapon))
                .IsReachable)
        {
            return BattleAttackOpportunity.Unreachable(
                input.Pursuer.Id,
                input.Quarry.Id,
                "never_within_weapon_range",
                BuildGeometryInput(input, shooter, target, input.PursuerMoveSpeed),
                "useful_range");
        }

        float usefulRange = FindSampledUsefulRange(
            shooter,
            target,
            weapon,
            input.QuarryMoveSpeed,
            PotentialAmmunitionForEvaluation(weapon));
        if (usefulRange <= 0)
        {
            return candidates.Count == 0
                ? BattleAttackOpportunity.Unreachable(
                    input.Pursuer.Id,
                    input.Quarry.Id,
                    "target_defense_or_weapon_never_useful",
                    BuildGeometryInput(input, shooter, target, input.PursuerMoveSpeed),
                    "useful_range")
                : Earliest(candidates);
        }

        BattleInterceptionProjection.PairInput movingGeometry = BuildGeometryInput(
            input,
            shooter,
            target,
            input.PursuerMoveSpeed);
        BattleInterceptionProjection.PairInput stationaryGeometry = BuildGeometryInput(
            input,
            shooter,
            target,
            0);

        BattleAttackOpportunity stationaryPreparation = FindPreparationRoute(
            input,
            shooter,
            target,
            weapon,
            stationaryGeometry,
            usefulRange,
            requiresMovement: false);
        if (stationaryPreparation.IsReachable) candidates.Add(stationaryPreparation);

        if (input.PursuerMoveSpeed > BattleInterceptionProjection.GeometryTolerance)
        {
            BattleAttackOpportunity movingPreparation = FindPreparationRoute(
                input,
                shooter,
                target,
                weapon,
                movingGeometry,
                usefulRange,
                requiresMovement: true);
            if (movingPreparation.IsReachable) candidates.Add(movingPreparation);
        }

        BattleAttackOpportunity aimed = FindAimRoute(
            input,
            shooter,
            target,
            weapon,
            stationaryGeometry,
            usefulRange,
            currentAim);
        if (aimed.IsReachable) candidates.Add(aimed);

        return candidates.Count == 0
            ? BattleAttackOpportunity.Unreachable(
                input.Pursuer.Id,
                input.Quarry.Id,
                "ranged_destination_unreachable",
                BuildGeometryInput(input, shooter, target, input.PursuerMoveSpeed),
                "useful_range")
            : Earliest(candidates);
    }

    private BattleAttackOpportunity FindPreparationRoute(
        PairInput pair,
        BattleSoldier shooter,
        BattleSoldier target,
        RangedWeapon weapon,
        BattleInterceptionProjection.PairInput geometry,
        float usefulRange,
        bool requiresMovement)
    {
        WeaponProjectionState state = WeaponProjectionState.From(shooter, weapon);
        if (!state.CanBecomeReady) return BattleAttackOpportunity.Unreachable(
            pair.Pursuer.Id,
            pair.Quarry.Id,
            "cannot_ready_weapon",
            geometry,
            "useful_range");

        List<int> candidateTurns = CandidateAttackTurns(
            geometry,
            usefulRange,
            weapon,
            PreparationHorizon(state, weapon, includeAim: false));
        foreach (int elapsed in candidateTurns)
        {
            WeaponProjectionState atTurn = state.AdvanceTo(elapsed, weapon);
            if (!atTurn.Ready || !atTurn.CanFire(weapon)) continue;

            float range = BattleInterceptionProjection.SeparationAtTime(geometry, elapsed);
            RangedTargetEvaluation evaluation = EvaluateShot(
                shooter,
                target,
                weapon,
                range,
                pair.QuarryMoveSpeed,
                atTurn.AmmunitionForEvaluation(weapon),
                aimBonus: null,
                moving: false);
            if (!IsUseful(evaluation)) continue;

            return Ranged(
                pair,
                shooter,
                target,
                weapon,
                geometry,
                atTurn.Preparation,
                requiresMovement && elapsed > 0,
                elapsed,
                requiresMovement ? elapsed : 0,
                atTurn.Preparation == BattleAttackPreparation.None
                    ? "useful_range_reached"
                    : "preparation_completed",
                usefulRange);
        }

        return BattleAttackOpportunity.Unreachable(
            pair.Pursuer.Id,
            pair.Quarry.Id,
            requiresMovement ? "moving_route_never_useful" : "stationary_route_never_useful",
            geometry,
            "useful_range");
    }

    private BattleAttackOpportunity FindAimRoute(
        PairInput pair,
        BattleSoldier shooter,
        BattleSoldier target,
        RangedWeapon weapon,
        BattleInterceptionProjection.PairInput geometry,
        float usefulRange,
        int? existingAim)
    {
        WeaponProjectionState state = WeaponProjectionState.From(shooter, weapon);
        if (!state.Ready && !state.CanBecomeReady)
        {
            return BattleAttackOpportunity.Unreachable(
                pair.Pursuer.Id,
                pair.Quarry.Id,
                "cannot_ready_weapon",
                geometry,
                "useful_range");
        }

        foreach (int elapsed in CandidateAttackTurns(
            geometry,
            usefulRange,
            weapon,
            PreparationHorizon(state, weapon, includeAim: true))
            .Where(turn => turn > 0))
        {
            WeaponProjectionState atTurn = state.AdvanceForAim(
                elapsed,
                weapon,
                existingAim,
                out int aimBonus);
            if (!atTurn.Ready || !atTurn.CanFire(weapon) || aimBonus < 0) continue;

            float range = BattleInterceptionProjection.SeparationAtTime(geometry, elapsed);
            RangedTargetEvaluation evaluation = EvaluateShot(
                shooter,
                target,
                weapon,
                range,
                pair.QuarryMoveSpeed,
                atTurn.AmmunitionForEvaluation(weapon),
                aimBonus,
                moving: false);
            if (!IsUseful(evaluation)) continue;

            return Ranged(
                pair,
                shooter,
                target,
                weapon,
                geometry,
                atTurn.Preparation,
                requiresMovement: false,
                elapsed,
                movementTurns: 0,
                "aim_completed",
                usefulRange);
        }

        return BattleAttackOpportunity.Unreachable(
            pair.Pursuer.Id,
            pair.Quarry.Id,
            "aim_window_leaves_useful_range",
            geometry,
            "useful_range");
    }

    private RangedTargetEvaluation EvaluateShot(
        BattleSoldier shooter,
        BattleSoldier target,
        RangedWeapon weapon,
        float range,
        float targetSpeed,
        int availableAmmo,
        int? aimBonus,
        bool moving)
    {
        if (!IsFinite(range)
            || range < 0
            || range > EffectiveMaximumRange(shooter, weapon))
        {
            return null;
        }

        float modifier = moving
            ? -weapon.Template.Bulk * SoldierMovementProjector.FullBulkMultiplier
            : 0;
        if (aimBonus.HasValue)
        {
            modifier += weapon.Template.Accuracy + aimBonus.Value + 1;
        }
        return _ranged.EvaluateRangedTarget(
            shooter,
            target,
            weapon,
            range,
            modifier,
            targetSpeed,
            availableAmmo);
    }

    private float FindSampledUsefulRange(
        BattleSoldier shooter,
        BattleSoldier target,
        RangedWeapon weapon,
        float targetSpeed,
        int availableAmmo)
    {
        UsefulRangeKey key = new(
            shooter.Soldier.Id,
            target.Soldier.Id,
            weapon.Template.Id,
            targetSpeed,
            availableAmmo);
        if (_usefulRanges.TryGetValue(key, out float cached)) return cached;
        float usefulRange = SampleUsefulRange(
            shooter,
            target,
            weapon,
            targetSpeed,
            availableAmmo);
        _usefulRanges[key] = usefulRange;
        return usefulRange;
    }

    private float SampleUsefulRange(
        BattleSoldier shooter,
        BattleSoldier target,
        RangedWeapon weapon,
        float targetSpeed,
        int availableAmmo)
    {
        float maximum = EffectiveMaximumRange(shooter, weapon);
        if (maximum <= 0 || availableAmmo <= 0 && !weapon.IsUnlimited && !weapon.IsConsumableItem)
        {
            return 0;
        }

        float step = maximum / RangeSampleCount;
        float lastUseful = 0;
        float firstNotUseful = maximum;
        bool found = false;
        for (int index = 0; index <= RangeSampleCount; index++)
        {
            float range = maximum * index / RangeSampleCount;
            RangedTargetEvaluation evaluation = EvaluateShot(
                shooter,
                target,
                weapon,
                range,
                targetSpeed,
                availableAmmo,
                RangedTargetSelector.FullAimBonusTurns,
                moving: false);
            if (IsUseful(evaluation))
            {
                found = true;
                lastUseful = range;
            }
            else if (found)
            {
                firstNotUseful = range;
                break;
            }
        }
        if (!found) return 0;
        if (firstNotUseful <= lastUseful + BattleInterceptionProjection.GeometryTolerance)
        {
            return lastUseful;
        }

        float lower = lastUseful;
        float upper = firstNotUseful;
        for (int index = 0; index < RangeRefinementCount; index++)
        {
            float middle = (lower + upper) / 2;
            RangedTargetEvaluation evaluation = EvaluateShot(
                shooter,
                target,
                weapon,
                middle,
                targetSpeed,
                availableAmmo,
                RangedTargetSelector.FullAimBonusTurns,
                moving: false);
            if (IsUseful(evaluation)) lower = middle;
            else upper = middle;
        }
        return Math.Max(0, lower - step / (RangeRefinementCount * 2));
    }

    private List<int> CandidateAttackTurns(
        BattleInterceptionProjection.PairInput geometry,
        float usefulRange,
        RangedWeapon weapon,
        int preparationHorizon)
    {
        SortedSet<int> turns = [];
        int horizon = Math.Max(MaximumPreparationTurns, preparationHorizon);
        for (int turn = 0; turn <= horizon; turn++) turns.Add(turn);

        BattleInterceptionProjection.DestinationResult destination =
            BattleInterceptionProjection.ProjectToDestination(geometry, usefulRange);
        if (destination.IsReachable)
        {
            int arrival = Math.Max(0, (int)MathF.Ceiling(
                destination.DestinationTurns - BattleInterceptionProjection.GeometryTolerance));
            for (int offset = -1; offset <= MaximumPreparationTurns + 1; offset++)
            {
                if (arrival + offset >= 0) turns.Add(arrival + offset);
            }
        }

        BattleInterceptionProjection.DestinationResult maximum =
            BattleInterceptionProjection.ProjectToDestination(
                geometry,
                EffectiveMaximumRangeForProjection(weapon));
        if (maximum.IsReachable)
        {
            int arrival = Math.Max(0, (int)MathF.Ceiling(
                maximum.DestinationTurns - BattleInterceptionProjection.GeometryTolerance));
            turns.Add(arrival);
            turns.Add(arrival + 1);
        }
        return turns.ToList();
    }

    private static int PreparationHorizon(
        WeaponProjectionState state,
        RangedWeapon weapon,
        bool includeAim)
    {
        int horizon = MaximumPreparationTurns;
        if (!state.Ready) horizon++;
        if (state.RequiresReload(weapon))
        {
            horizon += Math.Max(1, (int)weapon.Template.ReloadTime);
        }
        if (!state.CanFire(weapon) && weapon.IsSelfRegenerating)
        {
            horizon += Math.Max(1, (int)weapon.Template.RecoveryDuration);
        }
        if (includeAim) horizon += RangedTargetSelector.FullAimBonusTurns + 1;
        return horizon;
    }

    private static BattleInterceptionProjection.PairInput BuildGeometryInput(
        PairInput pair,
        BattleSoldier shooter,
        BattleSoldier target,
        float pursuerSpeed) => new(
        pair.Pursuer.Id,
        pair.Quarry.Id,
        Point(shooter),
        Point(target),
        Math.Max(0, pursuerSpeed),
        Math.Max(0, pair.QuarryMoveSpeed),
        pair.QuarryHeading);

    private static BattleInterceptionProjection.PairInput BuildMeleeInput(PairInput pair)
    {
        (BattleSoldier pursuer, BattleSoldier quarry) = NearestPlacedPair(
            pair.Pursuer,
            pair.Quarry);
        return new BattleInterceptionProjection.PairInput(
            pair.Pursuer.Id,
            pair.Quarry.Id,
            Point(pursuer),
            Point(quarry),
            Math.Max(0, pair.PursuerMoveSpeed),
            Math.Max(0, pair.QuarryMoveSpeed),
            pair.QuarryHeading);
    }

    private static BattleAttackOpportunity Ranged(
        PairInput pair,
        BattleSoldier shooter,
        BattleSoldier target,
        RangedWeapon weapon,
        BattleInterceptionProjection.PairInput geometry,
        BattleAttackPreparation preparation,
        bool requiresMovement,
        float elapsedTurns,
        float movementTurns,
        string reason,
        float? destinationRange = null) => new(
        pair.Pursuer.Id,
        pair.Quarry.Id,
        BattleAttackMode.Ranged,
        preparation,
        requiresMovement,
        elapsedTurns,
        movementTurns,
        shooter.Soldier.Id,
        target.Soldier.Id,
        weapon.Template.Id,
        reason,
        destinationRange)
        {
            Geometry = geometry,
            DestinationCondition = "useful_range"
        };

    private static BattleAttackOpportunity ChooseEarlier(
        BattleAttackOpportunity first,
        BattleAttackOpportunity second) => Earliest([first, second]);

    private static BattleAttackOpportunity Earliest(
        IEnumerable<BattleAttackOpportunity> opportunities)
    {
        List<BattleAttackOpportunity> ordered = (opportunities ?? [])
            .Where(opportunity => opportunity != null)
            .OrderBy(opportunity => opportunity.IsReachable ? 0 : 1)
            .ThenBy(opportunity => opportunity.ElapsedTurns)
            .ThenBy(opportunity => PreparationOrder(opportunity.Preparation))
            .ThenBy(opportunity => opportunity.RequiresMovement ? 1 : 0)
            .ThenBy(opportunity => opportunity.PursuerSquadId)
            .ThenBy(opportunity => opportunity.QuarrySquadId)
            .ThenBy(opportunity => opportunity.ShooterId ?? int.MaxValue)
            .ThenBy(opportunity => opportunity.TargetId ?? int.MaxValue)
            .ThenBy(opportunity => opportunity.WeaponTemplateId ?? int.MaxValue)
            .ToList();
        return ordered.FirstOrDefault()
            ?? BattleAttackOpportunity.Unreachable(0, 0, "unreachable");
    }

    private static int PreparationOrder(BattleAttackPreparation preparation) => preparation switch
    {
        BattleAttackPreparation.None => 0,
        BattleAttackPreparation.Ready => 1,
        BattleAttackPreparation.Reload => 2,
        BattleAttackPreparation.Aim => 3,
        _ => 4
    };

    private static bool IsUseful(RangedTargetEvaluation evaluation) => evaluation != null
        && evaluation.HitProbability > RangedTargetSelector.StickyMinimumHitProbability
        && evaluation.Score > 0;

    private static bool CanUseWeapon(BattleSoldier shooter, RangedWeapon weapon) =>
        shooter != null
        && weapon != null
        && shooter.RangedWeapons.Contains(weapon)
        && shooter.FunctioningHands > 0;

    private static int? ExistingAimFor(
        BattleSoldier shooter,
        BattleSoldier target,
        RangedWeapon weapon) => shooter.Aim is (int TargetId, RangedWeapon AimWeapon, int Turns)
            && TargetId == target.Soldier.Id
            && ReferenceEquals(AimWeapon, weapon)
            ? Turns
            : null;

    private static bool IsPlaced(BattleSoldier soldier) =>
        soldier?.TopLeft.HasValue == true;

    private static float NearestSeparation(PairInput input)
    {
        if (!IsProjectable(input.Pursuer) || !IsProjectable(input.Quarry))
        {
            return float.PositiveInfinity;
        }
        (BattleSoldier pursuer, BattleSoldier quarry) = NearestPlacedPair(
            input.Pursuer,
            input.Quarry);
        return Distance(pursuer, quarry);
    }

    private static bool IsProjectable(BattleSquad squad) =>
        squad != null
        && squad.Status == BattleSquadStatus.Active
        && squad.AbleSoldiers.Any(IsPlaced);

    private static (BattleSoldier Pursuer, BattleSoldier Quarry) NearestPlacedPair(
        BattleSquad pursuer,
        BattleSquad quarry) => pursuer.AbleSoldiers
        .Where(IsPlaced)
        .SelectMany(first => quarry.AbleSoldiers
            .Where(IsPlaced)
            .Select(second => (Pursuer: first, Quarry: second,
                Distance: Distance(first, second))))
        .OrderBy(pair => pair.Distance)
        .ThenBy(pair => pair.Pursuer.Soldier.Id)
        .ThenBy(pair => pair.Quarry.Soldier.Id)
        .Select(pair => (pair.Pursuer, pair.Quarry))
        .First();

    private static float Distance(BattleSoldier first, BattleSoldier second) =>
        BattleInterceptionProjection.Distance(Point(first), Point(second));

    private static BattleInterceptionProjection.Point Point(BattleSoldier soldier) =>
        new(soldier.TopLeft.Value.Item1, soldier.TopLeft.Value.Item2);

    private static float EffectiveMaximumRange(BattleSoldier shooter, RangedWeapon weapon) =>
        Math.Max(0, Math.Min(
            weapon.Template.MaximumRange,
            BattleModifiersUtil.GetEffectiveMaxRange(shooter.Soldier, weapon.Template)));

    private static float EffectiveMaximumRangeForProjection(RangedWeapon weapon) =>
        Math.Max(0, weapon.Template.MaximumRange);

    private static int CurrentAmmunitionForEvaluation(RangedWeapon weapon) => weapon.IsUnlimited
        ? int.MaxValue
        : weapon.IsConsumableItem
            ? Math.Max(0, weapon.ConsumableQuantity)
            : weapon.LoadedAmmo;

    private static int PotentialAmmunitionForEvaluation(RangedWeapon weapon) =>
        weapon.IsUnlimited
            ? int.MaxValue
            : weapon.IsConsumableItem
                ? Math.Max(0, weapon.ConsumableQuantity)
                : Math.Max(weapon.LoadedAmmo, weapon.Template.AmmoCapacity);

    private static bool CanFire(RangedWeapon weapon, int ammunition) =>
        weapon.IsUnlimited || weapon.IsConsumableItem
            ? ammunition > 0
            : ammunition > 0;

    private static bool ReloadActionRequired(RangedWeapon weapon) =>
        weapon.CanReload
        && (weapon.ReloadProgress > 0 || weapon.LoadedAmmo == 0);

    private readonly record struct WeaponProjectionState(
        bool Ready,
        bool CanBecomeReady,
        int LoadedAmmo,
        int ReserveAmmo,
        int ConsumableQuantity,
        int ReloadProgress,
        int RecoveryProgress,
        BattleAttackPreparation Preparation)
    {
        internal static WeaponProjectionState From(BattleSoldier shooter, RangedWeapon weapon) => new(
            shooter.EquippedRangedWeapons.Contains(weapon),
            weapon.Template.IsThrown || (int)weapon.Template.Location <= shooter.FunctioningHands,
            weapon.IsUnlimited || weapon.IsConsumableItem ? 0 : weapon.LoadedAmmo,
            weapon.ReserveAmmo,
            weapon.ConsumableQuantity,
            weapon.ReloadProgress,
            weapon.RecoveryProgress,
            BattleAttackPreparation.None);

        internal bool CanFire(RangedWeapon weapon) => weapon.IsUnlimited
            ? true
            : weapon.IsConsumableItem
                ? ConsumableQuantity > 0
                : LoadedAmmo > 0;

        internal int AmmunitionForEvaluation(RangedWeapon weapon) => weapon.IsUnlimited
            ? int.MaxValue
            : weapon.IsConsumableItem
                ? Math.Max(0, ConsumableQuantity)
                : Math.Max(0, LoadedAmmo);

        internal bool RequiresReload(RangedWeapon weapon) => NeedsReload(weapon);

        internal WeaponProjectionState AdvanceTo(int elapsedTurns, RangedWeapon weapon)
        {
            WeaponProjectionState state = this;
            int turns = Math.Max(0, elapsedTurns);
            for (int turn = 0; turn < turns; turn++)
            {
                state = state.AdvanceRecovery(weapon);
                if (!state.Ready)
                {
                    state = state with
                    {
                        Ready = state.CanBecomeReady,
                        Preparation = state.Preparation | BattleAttackPreparation.Ready
                    };
                    continue;
                }

                if (state.NeedsReload(weapon))
                {
                    state = state.AdvanceReload(weapon);
                }
            }
            return state;
        }

        internal WeaponProjectionState AdvanceForAim(
            int elapsedTurns,
            RangedWeapon weapon,
            int? initialAim,
            out int aimBonus)
        {
            WeaponProjectionState state = this;
            aimBonus = initialAim ?? -1;
            int turns = Math.Max(0, elapsedTurns);
            for (int turn = 0; turn < turns; turn++)
            {
                state = state.AdvanceRecovery(weapon);
                if (!state.Ready)
                {
                    state = state with
                    {
                        Ready = state.CanBecomeReady,
                        Preparation = state.CanBecomeReady
                            ? state.Preparation | BattleAttackPreparation.Ready
                            : state.Preparation
                    };
                    continue;
                }

                if (state.NeedsReload(weapon))
                {
                    state = state.AdvanceReload(weapon);
                    continue;
                }

                aimBonus = aimBonus < 0
                    ? 0
                    : Math.Min(RangedTargetSelector.FullAimBonusTurns, aimBonus + 1);
                state = state with
                {
                    Preparation = state.Preparation | BattleAttackPreparation.Aim
                };
            }
            return state;
        }

        private WeaponProjectionState AdvanceRecovery(RangedWeapon weapon)
        {
            if (!weapon.IsSelfRegenerating
                || LoadedAmmo >= weapon.Template.AmmoCapacity
                || weapon.Template.RecoveryDuration == 0)
            {
                return this;
            }

            int progress = RecoveryProgress + 1;
            int loaded = LoadedAmmo;
            int duration = Math.Max(1, (int)weapon.Template.RecoveryDuration);
            int amount = Math.Max(1, (int)weapon.Template.RecoveryAmount);
            while (progress >= duration && loaded < weapon.Template.AmmoCapacity)
            {
                loaded = Math.Min(
                    weapon.Template.AmmoCapacity,
                    loaded + amount);
                progress -= duration;
            }

            return this with
            {
                LoadedAmmo = loaded,
                RecoveryProgress = progress
            };
        }

        private bool NeedsReload(RangedWeapon weapon) =>
            weapon != null
            && weapon.HasSoldierReload
            && LoadedAmmo < weapon.Template.AmmoCapacity
            && (ReloadProgress > 0
                || !CanFire(weapon))
            && (weapon.Template.AmmunitionType == null || ReserveAmmo > 0);

        private WeaponProjectionState AdvanceReload(RangedWeapon weapon)
        {
            int progress = ReloadProgress + 1;
            int loaded = LoadedAmmo;
            int reserve = ReserveAmmo;
            if (progress >= Math.Max(1, (int)weapon.Template.ReloadTime))
            {
                int amount = weapon.Template.AmmunitionType == null
                    ? weapon.Template.AmmoCapacity - loaded
                    : weapon.Template.AmmunitionBehavior == AmmunitionBehavior.Incremental
                        ? Math.Max(1, (int)weapon.Template.ReloadAmount)
                        : weapon.Template.AmmoCapacity - loaded;
                amount = Math.Min(amount, weapon.Template.AmmoCapacity - loaded);
                if (weapon.Template.AmmunitionType != null)
                {
                    amount = Math.Min(amount, reserve);
                    reserve -= Math.Max(0, amount);
                }
                loaded += Math.Max(0, amount);
                progress = 0;
            }
            return this with
            {
                LoadedAmmo = loaded,
                ReserveAmmo = reserve,
                ReloadProgress = progress,
                Preparation = Preparation | BattleAttackPreparation.Reload
            };
        }
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
