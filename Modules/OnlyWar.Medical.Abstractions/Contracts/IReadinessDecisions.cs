namespace OnlyWar.Medical.Abstractions;

/// <summary>
/// The fact-only readiness policy port. Readiness policy belongs to Medical; Operations and other
/// callers project their live campaign state into the records in this assembly and receive a
/// detached decision back. The port intentionally has no knowledge of soldiers, squads, orders,
/// postings, or campaign state.
/// </summary>
public interface IReadinessDecisions
{
    /// <summary>Evaluates one already-projected member.</summary>
    DutyReadinessEvaluation EvaluateSoldier(
        DutyReadinessFacts facts,
        DutyReadinessPolicyOptions options = default);

    /// <summary>Evaluates one already-projected formation and its deployment context.</summary>
    SquadReadinessSnapshot EvaluateSquad(
        SquadReadinessFacts facts,
        DutyReadinessPolicyOptions options = default);
}
