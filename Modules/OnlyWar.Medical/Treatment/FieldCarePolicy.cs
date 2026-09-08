using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Medical.Abstractions;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Medical;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Medical.Treatment;

/// <summary>
/// Applies one bounded field-care pass to explicit provider/patient facts.  It owns wound
/// demotions and triage ordering; campaign adapters own reach, location, reports and experience.
/// </summary>
public static class FieldCarePolicy
{
    public static IReadOnlyList<FieldCareTreatmentResult> ApplyDailyCare(
        IReadOnlyList<FieldCareProviderFacts> providers,
        IReadOnlyList<FieldCarePatientFacts> patients,
        IRNG random,
        int day = 0)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (providers == null || patients == null) return [];

        float capacity = providers.Sum(provider => Math.Max(0f, provider.DailyCapacity));
        if (capacity <= 0f) return [];

        Dictionary<int, int> tieBreak = BuildTieBreak(patients, random);
        List<FieldCareTreatmentResult> treatments = [];
        float spent = 0f;
        while (true)
        {
            bool treated = false;
            foreach (FieldCarePatientFacts patient in Triage(patients, tieBreak))
            {
                HitLocation location = FindWorstTreatableLocation(patient.Body);
                if (location == null) continue;

                (WoundLevel band, int count) = location.Wounds.FindTreatableBand();
                float cost = FieldCareConstants.GetDemotionCost(band, count);
                if (cost > capacity - spent) continue;

                location.Wounds.ApplyTreatmentDemotion();
                spent += cost;
                treatments.Add(new FieldCareTreatmentResult(
                    patient.SoldierId,
                    patient.Name,
                    location.Template?.Name ?? "wound",
                    band,
                    count,
                    cost,
                    day));
                treated = true;
                break;
            }

            if (!treated) break;
        }

        return treatments;
    }

    private static Dictionary<int, int> BuildTieBreak(
        IReadOnlyList<FieldCarePatientFacts> patients,
        IRNG random)
    {
        Dictionary<int, int> keys = [];
        foreach (FieldCarePatientFacts patient in patients
            .Where(patient => patient.Body != null)
            .OrderBy(patient => patient.SoldierId))
        {
            if (keys.ContainsKey(patient.SoldierId)) continue;
            keys[patient.SoldierId] = random.GetIntBelowMax(0, int.MaxValue);
        }
        return keys;
    }

    private static IEnumerable<FieldCarePatientFacts> Triage(
        IReadOnlyList<FieldCarePatientFacts> patients,
        IReadOnlyDictionary<int, int> tieBreak) =>
        patients
            .Where(patient => patient.Body != null && GetTreatableSeverity(patient.Body) > 0)
            .OrderByDescending(patient => GetTreatableSeverity(patient.Body))
            .ThenByDescending(patient => patient.Rank)
            .ThenByDescending(patient => patient.Subrank)
            .ThenBy(patient => tieBreak.TryGetValue(patient.SoldierId, out int key) ? key : 0)
            .ThenBy(patient => patient.SoldierId);

    private static byte GetTreatableSeverity(Body body)
    {
        byte worst = 0;
        foreach (HitLocation location in body?.HitLocations ?? [])
        {
            if (!IsTreatable(location)) continue;
            byte weeks = location.Wounds.RecoveryTimeLeft();
            if (weeks > worst) worst = weeks;
        }
        return worst;
    }

    private static HitLocation FindWorstTreatableLocation(Body body)
    {
        HitLocation worst = null;
        uint worstTotal = 0;
        foreach (HitLocation location in body?.HitLocations ?? [])
        {
            if (!IsTreatable(location)) continue;
            if (location.Wounds.WoundTotal > worstTotal)
            {
                worstTotal = location.Wounds.WoundTotal;
                worst = location;
            }
        }
        return worst;
    }

    private static bool IsTreatable(HitLocation location) =>
        location != null
        && !location.IsCybernetic
        && !location.IsSevered
        && !location.IsCoveredBySeveredParent
        && !location.IsReplacementEligible
        && location.Wounds.FindTreatableBand().Count > 0;
}
