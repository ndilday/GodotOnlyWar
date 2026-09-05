using System.Collections.Generic;
using System.Linq;
using OnlyWar.Contracts.Medical;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Medical.Treatment;

/// <summary>
/// Sole owner of body-level natural healing and replacement completion.  Campaign adapters keep
/// ownership of rosters, resources, postings, and recovery events.
/// </summary>
public static class MedicalHealthPolicy
{
    public static void ApplyDailyHealing(Body body, bool acceleratedHealing)
    {
        if (body == null || !acceleratedHealing) return;
        foreach (HitLocation location in body.HitLocations)
        {
            if (!location.IsSevered && !location.IsCybernetic)
                location.Wounds.ClearNegligibleWounds();
        }
    }

    public static void ApplyWeeklyHealing(Body body)
    {
        if (body == null) return;
        foreach (HitLocation location in body.HitLocations)
        {
            if (location.Wounds.WoundTotal > 0
                && !location.IsCybernetic
                && !location.IsSevered
                && !location.IsCoveredBySeveredParent
                && !location.IsReplacementEligible)
            {
                location.Wounds.ApplyWeekOfHealing();
            }
        }
    }

    public static MedicalProcedureCompletion AdvanceProcedure(
        MedicalProcedure procedure,
        IReadOnlyDictionary<int, Body> bodies)
    {
        if (procedure == null || bodies == null
            || !bodies.TryGetValue(procedure.SoldierId, out Body body)
            || body == null)
        {
            return null;
        }

        procedure.WeeksRemaining--;
        if (procedure.WeeksRemaining > 0) return null;

        HitLocation location = body.HitLocations.FirstOrDefault(
            candidate => candidate.Template.Id == procedure.HitLocationTemplateId);
        if (location == null) return null;

        bool wasAlreadyCybernetic = location.IsCybernetic;
        bool wasSevered = location.IsSevered;
        foreach (HitLocation restored in body.HitLocations.Where(candidate =>
            candidate == location || body.GetReplacementParent(candidate) == location))
        {
            restored.Wounds.HealWounds();
            restored.IsCybernetic = procedure.ProcedureType == MedicalProcedureType.Cybernetic;
        }

        return new MedicalProcedureCompletion(
            procedure.SoldierId,
            location.Template.Id,
            location.Template.Name,
            procedure.ProcedureType,
            wasAlreadyCybernetic,
            MedicalProcedureRules.GetWeeks(procedure.ProcedureType, wasSevered),
            procedure.RequisitionCost);
    }
}
