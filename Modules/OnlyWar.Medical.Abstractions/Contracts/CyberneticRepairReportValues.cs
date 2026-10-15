using OnlyWar.Domain.Soldiers;
using System.Collections.Generic;

namespace OnlyWar.Medical.Abstractions
{
    /// <summary>Explicit provider input for one day of cybernetic repair.</summary>
    /// <param name="HoursAvailable">The provider's working hours this day.</param>
    /// <param name="SkillFactor">
    /// Multiplier on job time; 1 at the reference skill, smaller for a more skilled provider.
    /// </param>
    public readonly record struct CyberneticRepairProviderFacts(
        int ProviderId,
        string Name,
        float HoursAvailable,
        float SkillFactor);

    /// <summary>
    /// One queued job with the live part it repairs. The Medical owner may mutate only the job's
    /// progress and the supplied part's wounds. <paramref name="Organ"/> is the augmetic organ
    /// under repair when the job names one (<see cref="CyberneticRepairJob.OrganIndex"/>); the
    /// part is then the organ, not the location.
    /// </summary>
    public sealed record CyberneticRepairJobFacts(
        CyberneticRepairJob Job,
        string SoldierName,
        HitLocation Location,
        AugmeticOrgan Organ = null);

    /// <summary>One provider's work on one job in one day.</summary>
    public sealed record CyberneticRepairWork(
        int ProviderId,
        string ProviderName,
        int SoldierId,
        string SoldierName,
        int HitLocationTemplateId,
        string LocationName,
        float HoursWorked,
        bool Completed,
        int Day = 0,
        int? OrganIndex = null);

    /// <summary>
    /// What cybernetic repair did for one order (or one garrison location) over the days it ran:
    /// the repair equivalent of <see cref="FieldCareReport"/> (OnlyWar_TDD.md §6.6.1).
    /// Carried into the mission report, and read at turn end to take each
    /// Techmarine's repair hours out of his weekly drill.
    /// </summary>
    public sealed class CyberneticRepairReport
    {
        /// <summary>Stable provider id of the loaned Mechanicus adepts.</summary>
        public const int LoanProviderId = -1;
        public const string LoanProviderName = "Mechanicus adepts";

        public List<string> TechnicianNames { get; } = [];
        public List<int> TechnicianIds { get; } = [];
        public bool HasMechanicusLoan { get; set; }
        public List<CyberneticRepairWork> Work { get; } = [];
        public Dictionary<int, float> HoursByProvider { get; } = [];
        public HashSet<int> RepairedSoldierIds { get; } = [];
        public float HoursSpent { get; private set; }
        public int CompletedRepairCount { get; private set; }

        public bool HasTechnician => TechnicianIds.Count > 0 || HasMechanicusLoan;

        public void AddTechnician(int id, string name)
        {
            if (TechnicianIds.Contains(id)) return;
            TechnicianIds.Add(id);
            TechnicianNames.Add(name);
        }

        public void RecordWork(CyberneticRepairWork work)
        {
            Work.Add(work);
            HoursSpent += work.HoursWorked;
            HoursByProvider[work.ProviderId] =
                HoursByProvider.GetValueOrDefault(work.ProviderId) + work.HoursWorked;
            if (work.Completed)
            {
                CompletedRepairCount++;
                RepairedSoldierIds.Add(work.SoldierId);
            }
        }
    }
}
