using OnlyWar.Domain;
using OnlyWar.Domain.Missions;
using System.Collections.Generic;

namespace OnlyWar.Operations.Abstractions;

/// <summary>Terminal disposition of a mission force, independent of objective success.</summary>
public enum MissionForceDisposition
{
    Nominal = 0,
    BrokeContact,
    LostContact,
    WithdrewUnderFire,
    AbortedBeforeObjective
}

/// <summary>
/// Structured mission outcome facts shared by Operations' recorder and Application's report
/// projections. It is intentionally a value surface; mission-step state remains in Operations.
/// </summary>
public sealed class MissionOutcomeClassification
{
    public MissionType MissionType { get; init; }
    public bool WasDetected { get; init; }
    public bool ReturnedToBase { get; init; }
    public bool RemainedInTargetRegion { get; init; }
    public MissionForceDisposition Disposition { get; init; }
    public bool NoViableTarget { get; init; }
    public AmbushSpoilStage AmbushSpoiled { get; init; }
    public bool TargetLocated { get; init; }
    public bool TargetEliminated { get; init; }
    public int EnemiesKilled { get; init; }
    public int EnemyKillCredits { get; init; }
    public int FriendlyDeaths { get; init; }
    public int FriendlyIncapacitated { get; init; }
    public IReadOnlyList<string> FieldCareApothecaries { get; init; } = [];
    public int FieldCareTreatments { get; init; }
    public int FieldCareTreatedBrothers { get; init; }
    public float Impact { get; init; }
    public DefenseType? SabotageTarget { get; init; }
    public double SabotageDamage { get; init; }
    public double SabotageLevelBefore { get; init; }
}
