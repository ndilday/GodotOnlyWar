using System;
using OnlyWar.Medical.Abstractions;
using OnlyWar.Medical.Readiness;
using OnlyWar.Domain.Soldiers;

namespace OnlyWar.Medical.Readiness;

/// <summary>
/// The headless owner of the individual duty decision.  Adapters may translate their personnel
/// model into <see cref="DutyReadinessFacts"/>, but no adapter may reimplement this precedence.
/// </summary>
public static class DutyReadinessPolicy
{
    public static DutyReadinessEvaluation Evaluate(
        in DutyReadinessFacts facts,
        in DutyReadinessPolicyOptions options = default)
    {
        string name = string.IsNullOrWhiteSpace(facts.Name) ? "Soldier" : facts.Name;
        if (facts.HasUntreatedSeveredLimb)
        {
            return Reject(DutyReadinessReasonCode.UntreatedSeverance,
                $"{name} has an untreated severed limb.");
        }

        if (facts.IsProcedureReserved)
        {
            return Reject(DutyReadinessReasonCode.ProcedureReservation,
                $"{name} is reserved for a medical or recruitment procedure.");
        }

        if (!facts.IsCombatEffective)
        {
            return Reject(DutyReadinessReasonCode.CombatIncapacitation,
                $"{name} is combat-incapacitated.");
        }

        if (facts.FunctioningHands < 2)
        {
            return Reject(DutyReadinessReasonCode.InsufficientFunctioningArms,
                $"{name} has fewer than two functioning arm/hand groups.");
        }

        WoundLevel worst = facts.WorstWoundLevel;
        if (options.InjuryThreshold is WoundLevel threshold
            && worst != WoundLevel.None
            && SeverityIndex(worst) >= SeverityIndex(threshold))
        {
            return Reject(
                DutyReadinessReasonCode.ChapterInjuryThreshold,
                $"{name} is withheld by the Chapter's {threshold} injury threshold.",
                worst);
        }

        return worst == WoundLevel.None
            ? DutyReadinessEvaluation.Ready
            : new DutyReadinessEvaluation(true, DutyReadinessReasonCode.Ready, null, worst);
    }

    public static int SeverityIndex(WoundLevel level) => level switch
    {
        WoundLevel.Negligible => 1,
        WoundLevel.Minor => 2,
        WoundLevel.Moderate => 3,
        WoundLevel.Major => 4,
        WoundLevel.Critical => 5,
        WoundLevel.Massive => 6,
        WoundLevel.Mortal => 7,
        WoundLevel.Unsurvivable => 8,
        _ => 0
    };

    private static DutyReadinessEvaluation Reject(
        DutyReadinessReasonCode code,
        string reason,
        WoundLevel? worst = null) => new(false, code, reason, worst);
}

