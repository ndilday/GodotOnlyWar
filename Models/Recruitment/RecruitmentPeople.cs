using System.Collections.Generic;

namespace OnlyWar.Models.Recruitment
{
    public sealed class RecruitmentCandidateAttributes
    {
        public float Strength { get; set; }
        public float Constitution { get; set; }
        public float Intelligence { get; set; }
        public float Dexterity { get; set; }
        public float Ego { get; set; }
    }

    public sealed class RecruitmentCohort
    {
        public int Id { get; set; }
        public Date CreatedDate { get; set; }
        public double RemainingPopulation { get; set; }
        public float MinimumAgeAtCreation { get; set; }
        public float MaximumAgeAtCreation { get; set; }
        public bool IsFoundingCohort { get; set; }
    }

    public sealed class RecruitmentCandidate
    {
        public int Id { get; set; }
        public string InductionDesignation { get; set; }
        public int SourceWorldPlanetId { get; set; }
        public Date BirthDate { get; set; }
        public Date QualifiedDate { get; set; }
        public float GeneticCompatibility { get; set; }
        public RecruitmentCandidateAttributes Attributes { get; set; } = new();
    }

    public sealed class RecruitmentAspirant
    {
        public int Id { get; set; }
        public string InductionDesignation { get; set; }
        public int SourceWorldPlanetId { get; set; }
        public Date BirthDate { get; set; }
        public Date AdmittedDate { get; set; }
        public RecruitmentPhase Phase { get; set; } = RecruitmentPhase.Phase0PreImplantation;
        public Date PhaseStartedDate { get; set; }
        public int WeeksInCurrentPhase { get; set; }
        public float TrainingProgress { get; set; }
        public float GeneticCompatibility { get; set; }
        public RecruitmentCandidateAttributes Attributes { get; set; } = new();
        public Dictionary<int, float> SkillPoints { get; } = [];
        public List<RecruitmentAspirantEvent> Events { get; } = [];
    }

    public sealed class RecruitmentAspirantEvent
    {
        public Date Date { get; set; }
        public RecruitmentEventType Type { get; set; }
        public string Detail { get; set; }
    }

    public sealed class RecruitmentProgramEvent
    {
        public Date Date { get; set; }
        public RecruitmentEventType Type { get; set; }
        public int Count { get; set; }
        public string Detail { get; set; }
    }

    public sealed class RecruitmentProcedure
    {
        public int Id { get; set; }
        // The subject is an aspirant for implantation procedures and an active
        // PlayerSoldier for Black Carapace procedures. Keep AspirantId as the
        // v3 persistence alias so the clean-break schema remains stable.
        public int SubjectId { get; set; }
        public int AspirantId
        {
            get => SubjectId;
            set => SubjectId = value;
        }
        // Snapshot retained after Phase 12 removes the source aspirant. Phase 13
        // resolves Black Carapace survival against this exact compatibility value.
        public float GeneticCompatibility { get; set; }
        public RecruitmentProcedureType Type { get; set; }
        public RecruitmentPhase Phase { get; set; }
        public RecruitmentProcedureStatus Status { get; set; }
        public int AssignedApothecarySoldierId { get; set; }
        public int WeeksRemaining { get; set; }
        public int? ReservedSquadId { get; set; }
    }
}
