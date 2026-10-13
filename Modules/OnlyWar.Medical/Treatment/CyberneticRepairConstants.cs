using System;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Medical.Abstractions;

namespace OnlyWar.Medical.Treatment
{
    /// <summary>
    /// Tunables for Techmarine repair of cybernetic parts (OnlyWar_TDD.md §6.6.1,
    /// decided 2026-10-12).
    ///
    /// These live in CODE, never in the rules database, as <see cref="FieldCareConstants"/> and
    /// <c>MoraleConstants</c> do.
    /// </summary>
    public static class CyberneticRepairConstants
    {
        // ---- The working day ------------------------------------------------------------------

        /// <summary>
        /// Working hours a Techmarine (or the Mechanicus loan) has each day. Unused hours do not
        /// carry over, and a mission day uses all of them. No rating changes this.
        /// </summary>
        public const float DailyWorkingHours = 8f;

        /// <summary>Days of garrison repair settled by one turn-processing pass.</summary>
        public const int GarrisonDaysPerTurn = FieldCareConstants.GarrisonDaysPerTurn;

        // ---- Job time -------------------------------------------------------------------------

        /// <summary>
        /// Hours of a Moderate repair at the reference skill: one working day.
        /// <c>hours = BaseJobHours x severity multiplier x skill factor</c>.
        /// </summary>
        public const float BaseJobHours = 8f;

        /// <summary>The Engineering (Cybernetics) skill at which the skill factor is 1.</summary>
        public const float ReferenceSkill = 12f;

        /// <summary>Each skill point above the reference multiplies the job time by this.</summary>
        public const float SkillFactorPerPoint = 0.9f;

        /// <summary>
        /// Safety limits on the skill factor. Skill values run from about 6 (untrained, 1.88) to
        /// the high teens (16 gives 0.66), so they rarely bind.
        /// </summary>
        public const float MinSkillFactor = 0.5f;
        public const float MaxSkillFactor = 2.0f;

        /// <summary>Loaned Mechanicus adepts work at the reference skill.</summary>
        public const float LoanSkillFactor = 1.0f;

        /// <summary>Stable id of the loaned adepts in a repair pool and its report.</summary>
        public const int LoanProviderId = CyberneticRepairReport.LoanProviderId;

        public const string LoanProviderName = CyberneticRepairReport.LoanProviderName;

        // ---- Cost and experience --------------------------------------------------------------

        /// <summary>Flat Requisition per repair, paid when the job is queued.</summary>
        public const int RequisitionCost = 10;

        /// <summary>
        /// The ordinary weekly training points (ChapterUpkeepProcessor), spread over a training
        /// week of <see cref="TrainingWeekHours"/>.
        /// </summary>
        public const float WeeklyTrainingPoints = 0.2f;

        /// <summary>A training week: 7 days of 8 hours.</summary>
        public const float TrainingWeekHours = 56f;

        /// <summary>8 hours of repair are worth 4 hours of training.</summary>
        public const float TrainingHoursPerRepairHour = 0.5f;

        /// <summary>
        /// Engineering (Cybernetics) points for one hour of repair work: half a training hour.
        /// One 8-hour job gives about 0.014 points.
        /// </summary>
        public const float ExperiencePerRepairHour =
            TrainingHoursPerRepairHour * WeeklyTrainingPoints / TrainingWeekHours;

        /// <summary>
        /// The weekly drill a Techmarine loses for one hour of repair work, so the same hour is
        /// not counted as both repair and drill.
        /// </summary>
        public const float DrillPointsPerRepairHour = WeeklyTrainingPoints / TrainingWeekHours;

        // ---- Derived helpers ------------------------------------------------------------------

        /// <summary>
        /// The job-time multiplier for the worst wound band a part carries. A part severs at its
        /// template's sever level, so Mortal is the highest band a repairable part normally
        /// carries; Unsurvivable is mapped one step higher for a template that severs above it.
        /// </summary>
        public static float GetSeverityMultiplier(WoundLevel worstBand) => worstBand switch
        {
            WoundLevel.Negligible => 0.25f,
            WoundLevel.Minor => 0.5f,
            WoundLevel.Moderate => 1f,
            WoundLevel.Major => 2f,
            WoundLevel.Critical => 3f,
            WoundLevel.Massive => 4f,
            WoundLevel.Mortal => 5f,
            WoundLevel.Unsurvivable => 6f,
            _ => 0f
        };

        /// <summary>
        /// <c>clamp(0.9^(skill - 12), 0.5, 2.0)</c>: higher skill gives a shorter job.
        /// </summary>
        public static float GetSkillFactor(float skill)
        {
            if (float.IsNaN(skill)) return MaxSkillFactor;
            double factor = Math.Pow(SkillFactorPerPoint, skill - ReferenceSkill);
            return (float)Math.Clamp(factor, MinSkillFactor, MaxSkillFactor);
        }

        /// <summary>The hours of work a part needs at the reference skill.</summary>
        public static float GetJobHours(WoundLevel worstBand) =>
            BaseJobHours * GetSeverityMultiplier(worstBand);
    }
}
