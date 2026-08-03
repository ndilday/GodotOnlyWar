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
    float ReadinessValue = 0);

public sealed record BattleSquadCapabilityProfile(
    int SquadId,
    float TotalAbleBattleValue,
    float UsableRangedBattleValue,
    float UsableMeleeBattleValue,
    float EffectiveMeleeFraction,
    float PreferredBandLower,
    float PreferredBandUpper,
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
