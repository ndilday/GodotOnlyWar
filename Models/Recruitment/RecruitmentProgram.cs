using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.Recruitment
{
    public sealed class RecruitmentAttributeFilters
    {
        public int StrengthHalfSigmaSteps { get; set; }
        public int ConstitutionHalfSigmaSteps { get; set; }
        public int IntelligenceHalfSigmaSteps { get; set; }
        public int DexterityHalfSigmaSteps { get; set; }
        public int EgoHalfSigmaSteps { get; set; }

        public IEnumerable<int> AllHalfSigmaSteps
        {
            get
            {
                yield return StrengthHalfSigmaSteps;
                yield return ConstitutionHalfSigmaSteps;
                yield return IntelligenceHalfSigmaSteps;
                yield return DexterityHalfSigmaSteps;
                yield return EgoHalfSigmaSteps;
            }
        }
    }

    public sealed class RecruitmentStaffAssignment
    {
        public int SoldierId { get; set; }
        public RecruitmentStaffRole Role { get; set; }
        public float LeadershipRating { get; set; }
        public float MedicalRating { get; set; }
        public float PietyRating { get; set; }

        public RecruitmentStaffAssignment()
        {
        }

        public RecruitmentStaffAssignment(
            int soldierId,
            RecruitmentStaffRole role,
            float leadershipRating = 0,
            float medicalRating = 0,
            float pietyRating = 0)
        {
            SoldierId = soldierId;
            Role = role;
            LeadershipRating = leadershipRating;
            MedicalRating = medicalRating;
            PietyRating = pietyRating;
        }
    }

    public sealed class RecruitmentProgram
    {
        public int Id { get; set; }
        public int HomeWorldPlanetId { get; set; }
        public Date EstablishedDate { get; set; }
        public Date LastProcessedDate { get; set; }
        public bool IsSetupComplete { get; set; }
        public RecruitmentPolicy Policy { get; set; } = RecruitmentPolicy.VoluntaryPresentation;
        public RecruitmentWorldType WorldType { get; set; } = RecruitmentWorldType.Standard;
        public RecruitmentAttributeFilters AttributeFilters { get; set; } = new();
        public float MinimumGeneticCompatibility { get; set; } = 0.9f;
        public List<RecruitmentStaffAssignment> StaffAssignments { get; } = [];
        public List<RecruitmentCohort> UnscreenedCohorts { get; } = [];
        public List<RecruitmentCandidate> QualifiedCandidates { get; } = [];
        public List<RecruitmentAspirant> Aspirants { get; } = [];
        public List<RecruitmentProcedure> Procedures { get; } = [];
        public List<RecruitmentProgramEvent> ProgramEvents { get; } = [];

        public double UnscreenedEligiblePopulation =>
            UnscreenedCohorts.Sum(cohort => Math.Max(0, cohort.RemainingPopulation));
    }
}
