using OnlyWar.Domain;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Medical.Abstractions;
using OnlyWar.Medical.Treatment;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Campaign.Medical
{
    /// <summary>
    /// Techmarine repair of damaged cybernetic parts (OnlyWar_TDD.md §6.6.1). The
    /// campaign adapter around <see cref="CyberneticRepairPolicy"/>: it owns the job queue on the
    /// Army, the Requisition cost, reach, reports and experience.
    ///
    /// THE QUEUE. The player queues a repair for one damaged augmetic and pays
    /// <see cref="CyberneticRepairConstants.RequisitionCost"/> at once, as surgery is paid. Jobs are
    /// worked in queue order. A job continues across days and turns until the part is reset.
    ///
    /// TWO POOLS, as Apothecary field care has. FIELD: the Techmarines under an order
    /// repair the queued parts of everyone under that order, on the days the order has no mission
    /// step (a mission day uses all of a Techmarine's hours). GARRISON: Techmarines not on a mission
    /// repair the queued parts of co-located brothers who are likewise not on a mission. The "not on
    /// a mission" test that defines the garrison makes the two pools disjoint, so field wins by
    /// construction.
    ///
    /// THE LOAN. While the Mechanicus loan runs, its adepts work in every pool, at the
    /// reference skill, as one more provider with a full working day. They earn no experience.
    /// </summary>
    public static class CyberneticRepairService
    {
        /// <summary>The stable rules-data key of Engineering (Cybernetics).</summary>
        public const string CyberneticsSkillKey = "engineering_cybernetics";

        /// <summary>
        /// Engineering (Cybernetics) from the rules data, or null. Not a NamedSkillRegistry entry,
        /// for the same reason the Medical skills are not: repair is not worth failing the rules
        /// load over. Without it every Techmarine works at the reference skill and earns no XP.
        /// </summary>
        public static BaseSkill ResolveCyberneticsSkill(IReadOnlyDictionary<int, BaseSkill> baseSkillMap) =>
            baseSkillMap?.Values.FirstOrDefault(skill =>
                string.Equals(skill?.SkillKey, CyberneticsSkillKey, StringComparison.Ordinal));

        // ---- The queue ------------------------------------------------------------------------

        public static CyberneticRepairJob FindJob(PlayerForce force, int soldierId, int hitLocationTemplateId) =>
            force?.Army?.CyberneticRepairJobs?.FirstOrDefault(job =>
                job.SoldierId == soldierId && job.HitLocationTemplateId == hitLocationTemplateId);

        /// <summary>
        /// What queueing a repair of this part needs, met or not, for the Apothecarium to list.
        /// Queueing needs no Techmarine present: the job waits until one can reach it.
        /// </summary>
        public static IReadOnlyList<ProcedureRequisite> EvaluateQueueRequisites(
            PlayerForce force, ISoldier soldier, int hitLocationTemplateId)
        {
            HitLocation location = FindLocation(soldier, hitLocationTemplateId);
            int balance = force?.Army?.Requisition ?? 0;
            return
            [
                new ProcedureRequisite("Damaged augmetic, not severed",
                    CyberneticRepairPolicy.IsRepairable(location)),
                new ProcedureRequisite("No repair already queued",
                    FindJob(force, soldier?.Id ?? 0, hitLocationTemplateId) == null),
                new ProcedureRequisite(
                    $"Requisition {CyberneticRepairConstants.RequisitionCost} (have {balance})",
                    balance >= CyberneticRepairConstants.RequisitionCost),
            ];
        }

        /// <summary>
        /// Queues a repair and pays for it now. False, with nothing changed, when any
        /// requisite is unmet.
        /// </summary>
        public static bool TryQueueRepair(PlayerForce force, ISoldier soldier, int hitLocationTemplateId)
        {
            if (force?.Army == null || soldier == null) return false;
            if (!force.Army.PlayerSoldierMap.ContainsKey(soldier.Id)) return false;
            if (!EvaluateQueueRequisites(force, soldier, hitLocationTemplateId).All(r => r.IsMet))
            {
                return false;
            }
            force.Army.Requisition -= CyberneticRepairConstants.RequisitionCost;
            force.Army.CyberneticRepairJobs.Add(new CyberneticRepairJob(
                soldier.Id, hitLocationTemplateId, 0f, CyberneticRepairConstants.RequisitionCost));
            return true;
        }

        /// <summary>
        /// Drops jobs that can no longer be worked: the brother is gone from the roster, or his
        /// part was severed (sever is final) or is no longer damaged. The Requisition
        /// was spent when the job was queued and is not returned.
        /// </summary>
        public static void PruneJobs(PlayerForce force)
        {
            List<CyberneticRepairJob> jobs = force?.Army?.CyberneticRepairJobs;
            if (jobs == null) return;
            jobs.RemoveAll(job =>
                !force.Army.PlayerSoldierMap.TryGetValue(job.SoldierId, out PlayerSoldier soldier)
                || !CyberneticRepairPolicy.IsRepairable(FindLocation(soldier, job.HitLocationTemplateId)));
        }

        // ---- Who can repair -------------------------------------------------------------------

        /// <summary>
        /// Fit to repair: a Techmarine by template, not down, and not on Mars. Identified through
        /// <see cref="MedicalProcedureService.IsTechmarine"/>, the one definition of who counts.
        /// </summary>
        public static bool IsAvailableTechmarine(PlayerSoldier soldier) =>
            soldier != null
            && MedicalProcedureService.IsTechmarine(soldier)
            && soldier.IsCombatEffective
            && !MechanicusTrainingService.IsOnMars(soldier);

        public static float GetSkillFactor(PlayerSoldier techmarine, BaseSkill cyberneticsSkill) =>
            cyberneticsSkill == null || techmarine == null
                ? CyberneticRepairConstants.LoanSkillFactor
                : CyberneticRepairConstants.GetSkillFactor(
                    techmarine.GetTotalSkillValue(cyberneticsSkill));

        // ---- Field ----------------------------------------------------------------------------

        /// <summary>
        /// One order's repair for one campaign day. Call once per distinct order per day, as
        /// field care is called. On a day the order has a mission step, nobody under it repairs:
        /// the step uses the whole working day. The order's Techmarines are still named on the
        /// report.
        /// </summary>
        public static void ApplyDailyFieldRepair(
            Order order,
            PlayerForce force,
            CyberneticRepairReport report,
            BaseSkill cyberneticsSkill,
            int day,
            bool missionStepToday)
        {
            if (order == null || report == null || force?.Army == null) return;

            List<PlayerSoldier> underOrder = FieldCareService.EnumerateUnderOrder(order).ToList();
            List<PlayerSoldier> techmarines = underOrder.Where(IsAvailableTechmarine).ToList();
            bool loan = TechmarineSupport.IsOnLoan(force);
            foreach (PlayerSoldier techmarine in techmarines)
            {
                report.AddTechnician(techmarine.Id, techmarine.Name);
            }
            report.HasMechanicusLoan |= loan;
            if (missionStepToday) return;

            RunOneDay(force, techmarines, loan, underOrder, report, cyberneticsSkill, day, order.Id);
        }

        // ---- Garrison -------------------------------------------------------------------------

        /// <summary>
        /// Garrison repair for one turn: <see cref="CyberneticRepairConstants.GarrisonDaysPerTurn"/>
        /// working days for every location where brothers not on a mission are present, with the
        /// Techmarines there (and the loan) working on their queued parts. Returns one report per
        /// location that had anyone to do the work.
        /// </summary>
        public static IReadOnlyList<CyberneticRepairReport> ApplyGarrisonRepair(
            PlayerForce force,
            BaseSkill cyberneticsSkill)
        {
            List<CyberneticRepairReport> reports = [];
            if (force?.Army == null) return reports;
            PruneJobs(force);
            if (force.Army.CyberneticRepairJobs.Count == 0) return reports;

            bool loan = TechmarineSupport.IsOnLoan(force);
            List<PlayerSoldier> garrison = force.Army.PlayerSoldierMap.Values
                .Where(soldier => soldier != null && !FieldCareService.IsOnMission(soldier))
                .ToList();

            foreach (IGrouping<string, PlayerSoldier> location in garrison
                .GroupBy(FieldCareService.GetLocationKey)
                .Where(group => group.Key != null)
                .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                List<PlayerSoldier> present = location.ToList();
                List<PlayerSoldier> techmarines = present.Where(IsAvailableTechmarine).ToList();
                if (techmarines.Count == 0 && !loan) continue;

                CyberneticRepairReport report = new() { HasMechanicusLoan = loan };
                foreach (PlayerSoldier techmarine in techmarines)
                {
                    report.AddTechnician(techmarine.Id, techmarine.Name);
                }
                for (int day = 1; day <= CyberneticRepairConstants.GarrisonDaysPerTurn; day++)
                {
                    RunOneDay(force, techmarines, loan, present, report, cyberneticsSkill, day, null);
                }
                reports.Add(report);
            }
            return reports;
        }

        // ---- The daily pass -------------------------------------------------------------------

        private static void RunOneDay(
            PlayerForce force,
            IReadOnlyList<PlayerSoldier> techmarines,
            bool loan,
            IReadOnlyList<PlayerSoldier> pool,
            CyberneticRepairReport report,
            BaseSkill cyberneticsSkill,
            int day,
            int? orderId)
        {
            Dictionary<int, PlayerSoldier> patients = pool
                .GroupBy(soldier => soldier.Id)
                .ToDictionary(group => group.Key, group => group.First());
            List<CyberneticRepairJobFacts> jobs = [];
            foreach (CyberneticRepairJob job in force.Army.CyberneticRepairJobs)
            {
                if (!patients.TryGetValue(job.SoldierId, out PlayerSoldier patient)) continue;
                HitLocation location = FindLocation(patient, job.HitLocationTemplateId);
                if (!CyberneticRepairPolicy.IsRepairable(location)) continue;
                jobs.Add(new CyberneticRepairJobFacts(job, patient.Name, location));
            }
            if (jobs.Count == 0) return;

            List<CyberneticRepairProviderFacts> providers = techmarines
                .Select(techmarine => new CyberneticRepairProviderFacts(
                    techmarine.Id,
                    techmarine.Name,
                    CyberneticRepairConstants.DailyWorkingHours,
                    GetSkillFactor(techmarine, cyberneticsSkill)))
                .ToList();
            if (loan)
            {
                providers.Add(new CyberneticRepairProviderFacts(
                    CyberneticRepairConstants.LoanProviderId,
                    CyberneticRepairConstants.LoanProviderName,
                    CyberneticRepairConstants.DailyWorkingHours,
                    CyberneticRepairConstants.LoanSkillFactor));
            }

            IReadOnlyList<CyberneticRepairWork> work =
                CyberneticRepairPolicy.ApplyDailyRepair(providers, jobs, day);
            foreach (CyberneticRepairWork entry in work)
            {
                report.RecordWork(entry);
                if (entry.Completed)
                {
                    force.Army.CyberneticRepairJobs.RemoveAll(job =>
                        job.SoldierId == entry.SoldierId
                        && job.HitLocationTemplateId == entry.HitLocationTemplateId);
                }
                GameLog.Debug(() =>
                    $"CYBERNETIC_REPAIR order={(orderId.HasValue ? orderId.Value.ToString() : "garrison")} "
                    + $"day={day} provider={entry.ProviderName}#{entry.ProviderId} "
                    + $"patient={entry.SoldierName}#{entry.SoldierId} location={entry.LocationName} "
                    + $"hours={entry.HoursWorked:F2} completed={entry.Completed}");
            }

            GrantRepairExperience(techmarines, work, cyberneticsSkill);
        }

        /// <summary>
        /// Each Techmarine's own hours, at half a training hour each, credited in full to
        /// Engineering (Cybernetics). Split by contribution because each man is
        /// credited only with the hours he worked.
        /// </summary>
        private static void GrantRepairExperience(
            IReadOnlyList<PlayerSoldier> techmarines,
            IReadOnlyList<CyberneticRepairWork> work,
            BaseSkill cyberneticsSkill)
        {
            if (cyberneticsSkill == null || work.Count == 0) return;
            foreach (PlayerSoldier techmarine in techmarines)
            {
                float hours = work
                    .Where(entry => entry.ProviderId == techmarine.Id)
                    .Sum(entry => entry.HoursWorked);
                if (hours <= 0f) continue;
                techmarine.AddSkillPoints(
                    cyberneticsSkill, hours * CyberneticRepairConstants.ExperiencePerRepairHour);
            }
        }

        private static HitLocation FindLocation(ISoldier soldier, int hitLocationTemplateId) =>
            soldier?.Body?.HitLocations?.FirstOrDefault(location =>
                location.Template.Id == hitLocationTemplateId);
    }
}
