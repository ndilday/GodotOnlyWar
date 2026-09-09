namespace OnlyWar.Application.Abstractions;

public enum RecoveryMovementChoice
{
    None,
    DetachCasualty,
    MoveWholeSquad
}

/// <summary>Application-facing treatment choices; the medical module owns its implementation enum.</summary>
public enum MedicalProcedureChoice
{
    Cybernetic,
    VatGrown
}

public enum MedicalWoundLevel
{
    None,
    Negligible,
    Minor,
    Moderate,
    Major,
    Critical,
    Massive,
    Mortal,
    Unsurvivable
}

public enum CareDestinationState
{
    Ready = 0,
    Resolvable = 1,
    Ineligible = 2
}

public sealed record CareDestinationReason(string Code, string Message, bool IsResolvable);

public sealed record RecoveryPlanCommitResult(bool Succeeded, string Message);
