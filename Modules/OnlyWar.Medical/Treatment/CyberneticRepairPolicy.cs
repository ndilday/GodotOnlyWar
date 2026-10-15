using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Medical.Abstractions;

namespace OnlyWar.Medical.Treatment;

/// <summary>
/// Techmarine repair of cybernetic parts (OnlyWar_TDD.md §6.6.1), applied to explicit
/// provider and job facts. It owns which parts are repairable, job time, and the daily allocation
/// of working hours; campaign adapters own reach, the job queue, cost, reports and experience.
///
/// One job fully resets one part, whatever its damage. Damage changes only the time. A severed
/// part is gone and is never repairable. Repair works on cybernetic parts only, and organic
/// healing never does, so the two run on the same soldier independently.
/// </summary>
public static class CyberneticRepairPolicy
{
    private const float Epsilon = 0.0001f;

    /// <summary>A part a Techmarine can repair: cybernetic, not severed, and damaged.</summary>
    public static bool IsRepairable(HitLocation location) =>
        location != null
        && location.IsCybernetic
        && !location.IsSevered
        && location.Wounds.WoundTotal > 0;

    /// <summary>
    /// An augmetic organ a Techmarine can repair: still installed, not severed, and damaged
    /// (Design/Active/VitalLocationReplacement.md §3.4). A severed organ needs an organ
    /// replacement, not a repair.
    /// </summary>
    public static bool IsRepairable(AugmeticOrgan organ) =>
        organ?.Host != null
        && !organ.IsSevered
        && organ.Wounds.WoundTotal > 0;

    /// <summary>Hours the part needs in total, at the reference skill.</summary>
    public static float GetJobHours(HitLocation location) =>
        location == null ? 0f : CyberneticRepairConstants.GetJobHours(location.Wounds.WorstLevel);

    public static float GetJobHours(AugmeticOrgan organ) =>
        organ == null ? 0f : CyberneticRepairConstants.GetJobHours(organ.Wounds.WorstLevel);

    /// <summary>Hours of work left on a job, at the reference skill. Never negative.</summary>
    public static float GetRemainingHours(CyberneticRepairJob job, HitLocation location) =>
        Math.Max(0f, GetJobHours(location) - (job?.HoursCompleted ?? 0f));

    public static float GetRemainingHours(CyberneticRepairJob job, AugmeticOrgan organ) =>
        Math.Max(0f, GetJobHours(organ) - (job?.HoursCompleted ?? 0f));

    /// <summary>Is the part a job's facts name repairable: the organ if it names one, else the location.</summary>
    public static bool IsRepairable(CyberneticRepairJobFacts facts) =>
        facts?.Job != null
        && (facts.Job.OrganIndex.HasValue
            ? IsRepairable(facts.Organ)
            : IsRepairable(facts.Location));

    private static float GetRemainingHours(CyberneticRepairJobFacts facts) =>
        facts.Job.OrganIndex.HasValue
            ? GetRemainingHours(facts.Job, facts.Organ)
            : GetRemainingHours(facts.Job, facts.Location);

    /// <summary>
    /// One day of repair for one pool. Jobs are worked in queue order, with no triage by severity.
    /// The most skilled provider works first; a provider who finishes a job moves on to the next
    /// with the hours he has left, and a job one provider leaves unfinished is continued by the
    /// next. Work is credited at the provider's skill: an hour by a provider with skill factor f
    /// does 1/f hours of reference work. Hours left at the end of the day are lost.
    ///
    /// A finished job resets the part (<see cref="Wounds.HealWounds"/>), which also raises the
    /// part's injury revision for any cached battle estimate. The caller removes finished jobs
    /// from its queue. A job whose part is no longer repairable is skipped, not worked.
    /// </summary>
    public static IReadOnlyList<CyberneticRepairWork> ApplyDailyRepair(
        IReadOnlyList<CyberneticRepairProviderFacts> providers,
        IReadOnlyList<CyberneticRepairJobFacts> jobs,
        int day = 0)
    {
        if (providers == null || jobs == null || jobs.Count == 0) return [];

        List<CyberneticRepairWork> work = [];
        int jobIndex = 0;
        foreach (CyberneticRepairProviderFacts provider in providers
            .Where(provider => provider.HoursAvailable > 0f && provider.SkillFactor > 0f)
            .OrderBy(provider => provider.SkillFactor)
            .ThenBy(provider => provider.ProviderId == CyberneticRepairConstants.LoanProviderId)
            .ThenBy(provider => provider.ProviderId))
        {
            float hoursLeft = provider.HoursAvailable;
            while (hoursLeft > Epsilon && jobIndex < jobs.Count)
            {
                CyberneticRepairJobFacts facts = jobs[jobIndex];
                if (!IsRepairable(facts))
                {
                    jobIndex++;
                    continue;
                }

                float remaining = GetRemainingHours(facts);
                float hoursNeeded = remaining * provider.SkillFactor;
                float worked = Math.Min(hoursLeft, hoursNeeded);
                bool completed = worked >= hoursNeeded - Epsilon;
                hoursLeft -= worked;
                facts.Job.HoursCompleted += worked / provider.SkillFactor;
                if (completed)
                {
                    (facts.Job.OrganIndex.HasValue ? facts.Organ.Wounds : facts.Location.Wounds)
                        .HealWounds();
                    jobIndex++;
                }

                work.Add(new CyberneticRepairWork(
                    provider.ProviderId,
                    provider.Name,
                    facts.Job.SoldierId,
                    facts.SoldierName,
                    facts.Job.HitLocationTemplateId,
                    facts.Job.OrganIndex.HasValue
                        ? facts.Organ.Name
                        : facts.Location.Template?.Name ?? "augmetic",
                    worked,
                    completed,
                    day,
                    facts.Job.OrganIndex));
            }
            if (jobIndex >= jobs.Count) break;
        }

        return work;
    }
}
