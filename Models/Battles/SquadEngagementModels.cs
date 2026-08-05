using System;
using System.Collections.Generic;
using OnlyWar.Helpers.Battles;

namespace OnlyWar.Models.Battles;

/// <summary>
/// Stable, destination-independent identity of a squad movement choice.  This is deliberately
/// persisted only for the lifetime of a tactical battle so hysteresis survives cloned planning
/// states without turning a moving interpose point into part of the choice identity.
/// </summary>
public enum EngagementOptionKind
{
    Hold = 0,
    StepBack = 1,
    StepForward = 2,
    JogToward = 3,
    CloseToContact = 4,
    MoveToInterpose = 5,
    RunToward = 6
}

public enum EngagementSquadRole
{
    Normal = 0,
    Pursuit = 1,
    Cover = 2,
    RearGuard = 3,
    Bound = 4,
    Routing = 5,
    BreakOff = 6
}

/// <summary>
/// The root-turn action selected while an engagement candidate is being evaluated.  These are
/// data-only descriptions: workers may build them against the frozen planning state, and the
/// serial declaration barrier later materializes the corresponding battle actions without making
/// a second tactical choice.
/// </summary>
public enum PlannedSoldierActionKind
{
    None = 0,
    Shoot = 1,
    Aim = 2,
    Reload = 3,
    Ready = 4,
    AreaAttack = 5,
    BlastAttack = 6
}

public sealed record PlannedSoldierAction(
    int SoldierId,
    PlannedSoldierActionKind Kind,
    int? TargetId = null,
    int? WeaponTemplateId = null,
    float Range = 0,
    int ShotsToFire = 0,
    float BulkMultiplier = 0,
    float AimMultiplier = 0,
    float ExpectedEnemyBattleValueRemoved = 0,
    float ExpectedFriendlyBattleValueLost = 0,
    float ReadinessValue = 0,
    // Pre-rendered planning trace, emitted only if this action is the one actually materialized.
    // Root actions are planned for EVERY candidate posture, so logging at plan time would report
    // throws that were never made; carrying the string lets the decision be explained by the
    // planner (which has the alternatives it beat) and logged by the materializer (which knows it
    // won). Null unless a log sink is attached.
    string Diagnostic = null);

public sealed record BattleSquadCapabilityProfile(
    int SquadId,
    float TotalAbleBattleValue,
    float UsableRangedBattleValue,
    float UsableMeleeBattleValue,
    float EffectiveMeleeFraction,
    float PreferredBandLower,
    float PreferredBandUpper,
    // Effectiveness-derived range this squad should steer TOWARD, deliberately distinct from
    // PreferredBandUpper (which is weapon REACH -- the battle-value-weighted mean of
    // EffectiveMaximumRange). Consumers that mean "can I reach at all" (the posture baseline, and
    // the per-term reach gate PairRemovalTerm.MaximumEffectiveRange, which is where the removed
    // AggregateRemovalRate's range cutoff went) must keep using PreferredBandUpper;
    // consumers that mean "where do I want to be standing" use this. Derived by
    // BattleEngagementFrameBuilder.CalculateEffectiveEngagementRange against a representative
    // averaged opponent -- Phase 2, Design/Active/EngagementScoringOverhaul.md. PHASE 6 replaced
    // that derivation's body, without touching any call site, with the argmax of
    // removal(r) - incoming(r): our RangedEffectivenessCurve minus the enemy's, minus an
    // arrival-discounted melee term. For a non-degrading weapon against a penetrable target,
    // closing only improves removal, so the standoff this reports is set by RETURN FIRE, not by
    // the gun. 0 means "nothing to stand off for" -- no usable ranged weapon, or a target that
    // cannot be penetrated at any range.
    float EffectiveEngagementRange,
    // BEST CASE ranged effectiveness against the force this profile was built against: how much
    // ONE of this squad's shooters removes per turn of ONE representative opponent, maximized over
    // every range it could shoot from, as a fraction of that opponent's battle value.
    //
    // <para>Per-shooter and target-relative rather than a squad total in raw battle value, which
    // makes it the same dimensionless quantity as
    // RangedEffectivenessCurve.NegligibleRemovalFraction -- one scale for both -- and keeps it
    // steady as a squad takes casualties or as the enemy gets more numerous.</para>
    //
    // <para>MAXIMIZED, not sampled at EffectiveEngagementRange. That field is the argmax of
    // removal minus incoming, which degenerates to maximum range precisely when the outgoing curve
    // is flat, so reading removal there reports ~0 for a good rifle and a useless pistol alike.
    // The pair answers two different questions: EffectiveEngagementRange is "where would I stand",
    // this is "and is my shooting worth anything at all". A squad can have a perfectly
    // well-defined preferred range at which it accomplishes nothing -- an Acolyte Hybrid's
    // autopistol against Astartes power armour -- and only both values together tell that apart
    // from a real standoff. 0 whenever EffectiveEngagementRange is 0.</para>
    float PeakRangedRemovalFraction,
    float MoveSpeed,
    int ContactCapacity,
    IReadOnlyDictionary<int, float> CapabilityGroups)
{
    public bool IsContactSeeking => EffectiveMeleeFraction >= 0.55f;
    public bool IsFireSupport => EffectiveMeleeFraction <= 0.35f
        && UsableRangedBattleValue > UsableMeleeBattleValue;
}

public sealed record EngagementRoleConstraint(
    EngagementSquadRole Role,
    ushort? FixedHeading = null,
    float QuarryRunSpeed = 0,
    IReadOnlyCollection<BattleSquad> RoleTargets = null);

/// <summary>One immutable force-frame entry consumed by squad option scoring.</summary>
public sealed record SquadEngagementFrame(
    int SquadId,
    EngagementSquadRole Role,
    int? ProtectedSquadId,
    int? ScreenThreatSquadId,
    ValueTuple<float, float>? InterposePoint,
    int? PrimaryCounterpartSquadId,
    IReadOnlyDictionary<int, float> PairWeights,
    EngagementOptionKind BaselinePosture,
    ushort? FixedHeading = null,
    float QuarryRunSpeed = 0);

/// <summary>
/// Auditable Battle-Value terms for a candidate. FutureExchange stores the bounded continuation
/// policy value; future rollout code is aggregate-only and never calls per-soldier target selection.
/// </summary>
public sealed record EngagementOptionEvaluation(
    EngagementOptionKind Kind,
    SquadMovementTier Tier,
    ValueTuple<float, float>? IntendedDestination,
    float FeasibleSpeed,
    float ImmediateEnemyRemoval,
    float ImmediateFriendlyFire,
    float ReadinessValue,
    float IncomingNow,
    float MeleeNow,
    IReadOnlyList<float> FutureExchange,
    // Present-value benefit (or cost) of shortening the time to the squad's useful exchange
    // range for this candidate's projected endpoint. Kept separate from FutureExchange so the
    // root transition is visible in diagnostics rather than being buried in the bounded rollout.
    float ArrivalTimeValue,
    float RoleTerm,
    float ContactCommitmentCost,
    float Hysteresis,
    float Score,
    IReadOnlyList<PlannedSoldierAction> RootActions = null);

/// <summary>Pure Layer-2 result.  Declaration and action construction happen later.</summary>
public sealed record SquadEngagementDecision(
    BattleSquad Squad,
    SquadEngagementFrame Frame,
    EngagementOptionEvaluation Chosen,
    IReadOnlyList<EngagementOptionEvaluation> Candidates,
    IReadOnlyCollection<BattleSquad> RoleTargets = null);
