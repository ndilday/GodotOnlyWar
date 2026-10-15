using System.Collections.Generic;
using System.Linq;
using OnlyWar.Medical.Abstractions;
using OnlyWar.Domain.Soldiers;

namespace OnlyWar.Medical.Treatment;

/// <summary>
/// Sole owner of body-level natural healing and replacement completion.  Campaign adapters keep
/// ownership of rosters, resources, postings, and recovery events.
/// </summary>
public static class MedicalHealthPolicy
{
    // Augmetic organs never heal naturally; their wounds are on their own tracks, which neither
    // pass touches. A location marked for organ replacement does not heal either.
    public static void ApplyDailyHealing(Body body, bool acceleratedHealing)
    {
        if (body == null || !acceleratedHealing) return;
        foreach (HitLocation location in body.HitLocations)
        {
            if (!location.IsSevered && !location.IsCybernetic && !location.RequiresOrganReplacement)
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

    /// <param name="cyberneticOption">
    /// The augmetic tier a cybernetic replacement installs: its armor and constitution go onto
    /// every restored location. Null installs a part with no values of its own.
    /// </param>
    /// <param name="organOption">
    /// The tier a cybernetic organ replacement installs as an augmetic organ. Null installs an
    /// organ that uses the soldier's constitution.
    /// </param>
    public static MedicalProcedureCompletion AdvanceProcedure(
        MedicalProcedure procedure,
        IReadOnlyDictionary<int, Body> bodies,
        CyberneticOption cyberneticOption = null,
        CyberneticOption organOption = null)
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
        bool severedPricing = MedicalProcedureRules.UsesSeveredPricing(location);
        if (location.RequiresOrganReplacement)
        {
            CompleteOrganReplacement(location, procedure.ProcedureType, organOption);
            return new MedicalProcedureCompletion(
                procedure.SoldierId,
                location.Template.Id,
                location.Template.Name,
                procedure.ProcedureType,
                wasAlreadyCybernetic,
                MedicalProcedureRules.GetWeeks(procedure.ProcedureType, severedPricing),
                procedure.RequisitionCost);
        }

        foreach (HitLocation restored in body.HitLocations.Where(candidate =>
            candidate == location || body.GetReplacementParent(candidate) == location))
        {
            restored.Wounds.HealWounds();
            if (procedure.ProcedureType == MedicalProcedureType.Cybernetic)
            {
                restored.InstallCybernetic(cyberneticOption);
            }
            else
            {
                restored.IsCybernetic = false;
            }
        }

        return new MedicalProcedureCompletion(
            procedure.SoldierId,
            location.Template.Id,
            location.Template.Name,
            procedure.ProcedureType,
            wasAlreadyCybernetic,
            MedicalProcedureRules.GetWeeks(procedure.ProcedureType, severedPricing),
            procedure.RequisitionCost);
    }

    /// <summary>
    /// Design/Active/VitalLocationReplacement.md §3.3: the location's organic wounds and the flag
    /// are cleared, and the location stays organic. A severed augmetic organ is removed. A
    /// cybernetic replacement then adds one augmetic organ, unless the location is at the cap; a
    /// vat-grown replacement adds nothing. So replacing a lost organ never grows the count. The
    /// progenoid gland is not touched (D9).
    /// </summary>
    private static void CompleteOrganReplacement(
        HitLocation location,
        MedicalProcedureType type,
        CyberneticOption organOption)
    {
        location.Wounds.HealWounds();
        foreach (AugmeticOrgan lost in location.AugmeticOrgans.Where(organ => organ.IsSevered).ToList())
        {
            location.RemoveAugmeticOrgan(lost);
        }
        if (type == MedicalProcedureType.Cybernetic)
        {
            location.AddAugmeticOrgan(organOption);
        }
        location.RequiresOrganReplacement = false;
    }
}
