using OnlyWar.Models;
using OnlyWar.Models.Recruitment;
using System;
using System.IO;
using System.Linq;

namespace OnlyWar.Helpers.Database.GameState
{
    internal static class RecruitmentSaveMapper
    {
        internal static RecruitmentSaveData ToSaveData(RecruitmentProgram program)
        {
            if (program == null)
            {
                return RecruitmentSaveData.Empty;
            }

            RecruitmentSaveData data = new();
            data.Programs.Add(new RecruitmentProgramRow(
                program.Id,
                program.HomeWorldPlanetId,
                program.IsSetupComplete,
                (int)program.Policy,
                (int)program.WorldType,
                program.AttributeFilters.StrengthHalfSigmaSteps,
                program.AttributeFilters.ConstitutionHalfSigmaSteps,
                program.AttributeFilters.IntelligenceHalfSigmaSteps,
                program.AttributeFilters.DexterityHalfSigmaSteps,
                program.AttributeFilters.EgoHalfSigmaSteps,
                program.MinimumGeneticCompatibility,
                TotalWeeks(program.EstablishedDate, nameof(program.EstablishedDate)),
                program.LastProcessedDate?.GetTotalWeeks()));

            foreach (RecruitmentCohort cohort in program.UnscreenedCohorts)
            {
                data.UnscreenedCohorts.Add(new RecruitmentUnscreenedCohortRow(
                    cohort.Id,
                    program.Id,
                    TotalWeeks(cohort.CreatedDate, nameof(cohort.CreatedDate)),
                    cohort.RemainingPopulation,
                    cohort.MinimumAgeAtCreation,
                    cohort.MaximumAgeAtCreation,
                    cohort.IsFoundingCohort));
            }

            foreach (RecruitmentCandidate candidate in program.QualifiedCandidates)
            {
                data.Candidates.Add(new RecruitmentCandidateRow(
                    candidate.Id,
                    program.Id,
                    candidate.SourceWorldPlanetId,
                    TotalWeeks(candidate.BirthDate, nameof(candidate.BirthDate)),
                    candidate.Attributes.Strength,
                    candidate.Attributes.Constitution,
                    candidate.Attributes.Intelligence,
                    candidate.Attributes.Dexterity,
                    candidate.Attributes.Ego,
                    candidate.GeneticCompatibility,
                    TotalWeeks(candidate.QualifiedDate, nameof(candidate.QualifiedDate)),
                    candidate.InductionDesignation ?? string.Empty));
            }

            foreach (RecruitmentAspirant aspirant in program.Aspirants)
            {
                data.Aspirants.Add(new RecruitmentAspirantRow(
                    aspirant.Id,
                    program.Id,
                    aspirant.SourceWorldPlanetId,
                    TotalWeeks(aspirant.BirthDate, nameof(aspirant.BirthDate)),
                    aspirant.Attributes.Strength,
                    aspirant.Attributes.Constitution,
                    aspirant.Attributes.Intelligence,
                    aspirant.Attributes.Dexterity,
                    aspirant.Attributes.Ego,
                    aspirant.GeneticCompatibility,
                    TotalWeeks(aspirant.AdmittedDate, nameof(aspirant.AdmittedDate)),
                    (int)aspirant.Phase,
                    TotalWeeks(aspirant.PhaseStartedDate, nameof(aspirant.PhaseStartedDate)),
                    aspirant.WeeksInCurrentPhase,
                    aspirant.TrainingProgress,
                    aspirant.InductionDesignation ?? string.Empty));

                foreach ((int baseSkillId, float pointsInvested) in aspirant.SkillPoints)
                {
                    data.AspirantSkills.Add(new RecruitmentAspirantSkillRow(
                        aspirant.Id, baseSkillId, pointsInvested));
                }

                foreach (RecruitmentAspirantEvent aspirantEvent in aspirant.Events)
                {
                    data.AspirantEvents.Add(new RecruitmentAspirantEventRow(
                        aspirant.Id,
                        TotalWeeks(aspirantEvent.Date, nameof(aspirantEvent.Date)),
                        (int)aspirantEvent.Type,
                        aspirantEvent.Detail ?? string.Empty));
                }
            }

            foreach (RecruitmentProcedure procedure in program.Procedures)
            {
                data.Procedures.Add(new RecruitmentProcedureRow(
                    procedure.Id,
                    program.Id,
                    procedure.AspirantId,
                    procedure.GeneticCompatibility,
                    (int)procedure.Type,
                    (int)procedure.Phase,
                    (int)procedure.Status,
                    procedure.AssignedApothecarySoldierId,
                    procedure.WeeksRemaining,
                    procedure.ReservedSquadId));
            }

            foreach (RecruitmentProgramEvent programEvent in program.ProgramEvents)
            {
                data.ProgramLog.Add(new RecruitmentProgramLogRow(
                    program.Id,
                    TotalWeeks(programEvent.Date, nameof(programEvent.Date)),
                    (int)programEvent.Type,
                    programEvent.Count,
                    programEvent.Detail ?? string.Empty));
            }

            return data;
        }

        internal static RecruitmentProgram FromSaveData(RecruitmentSaveData data)
        {
            if (data == null || data.Programs.Count == 0)
            {
                return null;
            }
            if (data.Programs.Count != 1)
            {
                throw new InvalidDataException(
                    $"Recruitment v1 expects exactly one program, found {data.Programs.Count}.");
            }

            RecruitmentProgramRow row = data.Programs[0];
            RecruitmentProgram program = new()
            {
                Id = row.Id,
                HomeWorldPlanetId = row.HomeWorldPlanetId,
                IsSetupComplete = row.IsConfigured,
                Policy = (RecruitmentPolicy)row.Policy,
                WorldType = (RecruitmentWorldType)row.WorldType,
                MinimumGeneticCompatibility = row.GeneticCompatibilityThreshold,
                EstablishedDate = Date.FromTotalWeeks(row.EstablishedDate),
                LastProcessedDate = row.LastProcessedDate.HasValue
                    ? Date.FromTotalWeeks(row.LastProcessedDate.Value)
                    : null,
                AttributeFilters = new RecruitmentAttributeFilters
                {
                    StrengthHalfSigmaSteps = row.StrengthThreshold,
                    ConstitutionHalfSigmaSteps = row.ConstitutionThreshold,
                    IntelligenceHalfSigmaSteps = row.IntelligenceThreshold,
                    DexterityHalfSigmaSteps = row.DexterityThreshold,
                    EgoHalfSigmaSteps = row.EgoThreshold
                }
            };

            foreach (RecruitmentUnscreenedCohortRow cohort in
                     data.UnscreenedCohorts.Where(item => item.ProgramId == program.Id))
            {
                program.UnscreenedCohorts.Add(new RecruitmentCohort
                {
                    Id = cohort.Id,
                    CreatedDate = Date.FromTotalWeeks(cohort.CreatedDate),
                    RemainingPopulation = cohort.RemainingPopulation,
                    MinimumAgeAtCreation = cohort.MinimumAgeAtCreation,
                    MaximumAgeAtCreation = cohort.MaximumAgeAtCreation,
                    IsFoundingCohort = cohort.IsFoundingPool
                });
            }

            foreach (RecruitmentCandidateRow candidate in
                     data.Candidates.Where(item => item.ProgramId == program.Id))
            {
                program.QualifiedCandidates.Add(new RecruitmentCandidate
                {
                    Id = candidate.Id,
                    InductionDesignation = candidate.Designation,
                    SourceWorldPlanetId = candidate.SourcePlanetId,
                    BirthDate = Date.FromTotalWeeks(candidate.BirthDate),
                    QualifiedDate = Date.FromTotalWeeks(candidate.QualificationDate),
                    GeneticCompatibility = candidate.GeneticCompatibility,
                    Attributes = Attributes(
                        candidate.Strength,
                        candidate.Constitution,
                        candidate.Intelligence,
                        candidate.Dexterity,
                        candidate.Ego)
                });
            }

            foreach (RecruitmentAspirantRow aspirantRow in
                     data.Aspirants.Where(item => item.ProgramId == program.Id))
            {
                RecruitmentAspirant aspirant = new()
                {
                    Id = aspirantRow.Id,
                    InductionDesignation = aspirantRow.Designation,
                    SourceWorldPlanetId = aspirantRow.SourcePlanetId,
                    BirthDate = Date.FromTotalWeeks(aspirantRow.BirthDate),
                    AdmittedDate = Date.FromTotalWeeks(aspirantRow.AdmissionDate),
                    Phase = (RecruitmentPhase)aspirantRow.CurrentPhase,
                    PhaseStartedDate = Date.FromTotalWeeks(aspirantRow.PhaseStartedDate),
                    WeeksInCurrentPhase = aspirantRow.WeeksInCurrentPhase,
                    TrainingProgress = aspirantRow.TrainingProgress,
                    GeneticCompatibility = aspirantRow.GeneticCompatibility,
                    Attributes = Attributes(
                        aspirantRow.Strength,
                        aspirantRow.Constitution,
                        aspirantRow.Intelligence,
                        aspirantRow.Dexterity,
                        aspirantRow.Ego)
                };

                foreach (RecruitmentAspirantSkillRow skill in
                         data.AspirantSkills.Where(item => item.AspirantId == aspirant.Id))
                {
                    aspirant.SkillPoints[skill.BaseSkillId] = skill.PointsInvested;
                }
                foreach (RecruitmentAspirantEventRow aspirantEvent in
                         data.AspirantEvents.Where(item => item.AspirantId == aspirant.Id))
                {
                    aspirant.Events.Add(new RecruitmentAspirantEvent
                    {
                        Date = Date.FromTotalWeeks(aspirantEvent.EventDate),
                        Type = (RecruitmentEventType)aspirantEvent.EventType,
                        Detail = aspirantEvent.Detail
                    });
                }
                program.Aspirants.Add(aspirant);
            }

            foreach (RecruitmentProcedureRow procedure in
                     data.Procedures.Where(item => item.ProgramId == program.Id))
            {
                program.Procedures.Add(new RecruitmentProcedure
                {
                    Id = procedure.Id,
                    AspirantId = procedure.AspirantId,
                    GeneticCompatibility = procedure.GeneticCompatibility,
                    Type = (RecruitmentProcedureType)procedure.ProcedureType,
                    Phase = (RecruitmentPhase)procedure.Phase,
                    Status = (RecruitmentProcedureStatus)procedure.Status,
                    AssignedApothecarySoldierId = procedure.AssignedApothecarySoldierId,
                    WeeksRemaining = procedure.WeeksRemaining,
                    ReservedSquadId = procedure.ReservedSquadId
                });
            }

            foreach (RecruitmentProgramLogRow programEvent in
                     data.ProgramLog.Where(item => item.ProgramId == program.Id))
            {
                program.ProgramEvents.Add(new RecruitmentProgramEvent
                {
                    Date = Date.FromTotalWeeks(programEvent.EventDate),
                    Type = (RecruitmentEventType)programEvent.EventType,
                    Count = programEvent.EventCount,
                    Detail = programEvent.Entry
                });
            }

            return program;
        }

        private static RecruitmentCandidateAttributes Attributes(
            float strength, float constitution, float intelligence, float dexterity, float ego)
        {
            return new RecruitmentCandidateAttributes
            {
                Strength = strength,
                Constitution = constitution,
                Intelligence = intelligence,
                Dexterity = dexterity,
                Ego = ego
            };
        }

        private static int TotalWeeks(Date date, string fieldName)
        {
            if (date == null)
            {
                throw new InvalidOperationException(
                    $"Recruitment {fieldName} must be set before saving.");
            }
            return date.GetTotalWeeks();
        }
    }
}
