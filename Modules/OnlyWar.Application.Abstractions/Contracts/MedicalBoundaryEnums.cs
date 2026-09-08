namespace OnlyWar.Application;

public enum RecoveryMovementChoice
{
    None,
    DetachCasualty,
    MoveWholeSquad
}

public enum CareDestinationState
{
    Ready = 0,
    Resolvable = 1,
    Ineligible = 2
}

public sealed record CareDestinationReason(string Code, string Message, bool IsResolvable);

public sealed record RecoveryPlanCommitResult(bool Succeeded, string Message);
