using OnlyWar.Medical.Abstractions;
using OnlyWar.Medical.Readiness;

namespace OnlyWar.Medical.Readiness;

/// <summary>
/// Medical's implementation of the readiness capability Operations consumes (SB-05b-1). It is a
/// pure adapter over the fact-based policies owned by the Medical assembly. Live campaign
/// projection is deliberately kept out of this capability so Operations can depend on the port
/// without importing Campaign model types.
/// </summary>
public sealed class MedicalReadinessDecisions : IReadinessDecisions
{
    public DutyReadinessEvaluation EvaluateSoldier(
        DutyReadinessFacts facts,
        DutyReadinessPolicyOptions options = default) =>
        DutyReadinessPolicy.Evaluate(facts, options);

    public SquadReadinessSnapshot EvaluateSquad(
        SquadReadinessFacts facts,
        DutyReadinessPolicyOptions options = default) =>
        SquadReadinessPolicy.Evaluate(facts, options);
}
