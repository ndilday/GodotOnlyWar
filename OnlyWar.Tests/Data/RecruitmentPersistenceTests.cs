using Microsoft.Data.Sqlite;
using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Models;
using OnlyWar.Models.Recruitment;
using System;
using System.IO;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Data;

public sealed class RecruitmentPersistenceTests
{
    [Fact]
    public void CurrentSchema_RoundTripsHomeWorldAndRecruitmentAggregate()
    {
        Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
        string path = GameStateRoundTripFixture.CreateTempDbPath("recruitment_current");
        try
        {
            using SqliteConnection connection = OpenDatabase(path);
            CreateSchema(connection);
            CreateReferencedRows(connection);

            RecruitmentProgram original = CreateProgram();
            RecruitmentSaveData saveData = RecruitmentSaveMapper.ToSaveData(original);
            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                new RecruitmentDataAccess().SaveData(transaction, saveData);
                new GlobalDataAccess().SaveGlobalData(
                    transaction,
                    new Date(42, 100, 10),
                    requisition: 91,
                    geneseedStockpile: 12,
                    geneseedPurity: 0.87f,
                    scenario: null,
                    homeWorldPlanetId: 42);
                transaction.Commit();
            }

            GlobalState global = new GlobalDataAccess().GetGlobalData(connection);
            Assert.Equal(SaveFormat.CurrentVersion, new GlobalDataAccess().GetSaveVersion(connection));
            Assert.Equal(42, global.HomeWorldPlanetId);
            Assert.Equal(91, global.Requisition);

            RecruitmentSaveData loadedRows = new RecruitmentDataAccess().GetData(connection);
            RecruitmentProgram loaded = RecruitmentSaveMapper.FromSaveData(loadedRows);

            Assert.NotNull(loaded);
            Assert.Equal(original.Id, loaded.Id);
            Assert.Equal(original.HomeWorldPlanetId, loaded.HomeWorldPlanetId);
            Assert.Equal(original.LastProcessedDate, loaded.LastProcessedDate);
            Assert.Equal(RecruitmentPolicy.PlanetaryTithe, loaded.Policy);
            Assert.Equal(RecruitmentWorldType.Death, loaded.WorldType);
            Assert.Equal(3, loaded.AttributeFilters.StrengthHalfSigmaSteps);
            Assert.Equal(0.82f, loaded.MinimumGeneticCompatibility, 3);

            RecruitmentCohort cohort = Assert.Single(loaded.UnscreenedCohorts);
            Assert.Equal(5432.25, cohort.RemainingPopulation, 3);
            Assert.True(cohort.IsFoundingCohort);

            RecruitmentCandidate candidate = Assert.Single(loaded.QualifiedCandidates);
            Assert.Equal("Candidate XVII", candidate.InductionDesignation);
            Assert.Equal(61.5f, candidate.Attributes.Strength, 3);
            Assert.Equal(original.QualifiedCandidates[0].QualifiedDate, candidate.QualifiedDate);

            RecruitmentAspirant aspirant = Assert.Single(loaded.Aspirants);
            Assert.Equal(900_001, aspirant.Id);
            Assert.Equal(RecruitmentPhase.Phase4, aspirant.Phase);
            Assert.Equal(2, aspirant.WeeksInCurrentPhase);
            Assert.Equal(1.75f, aspirant.TrainingProgress, 3);
            Assert.Equal(4.25f, aspirant.SkillPoints[8], 3);
            Assert.Equal("Implantation stable", Assert.Single(aspirant.Events).Detail);

            RecruitmentProcedure procedure = Assert.Single(loaded.Procedures);
            Assert.Equal(RecruitmentProcedureType.Implantation, procedure.Type);
            Assert.Equal(RecruitmentPhase.Phase4, procedure.Phase);
            Assert.Equal(0.94f, procedure.GeneticCompatibility, 3);
            Assert.Equal(7, procedure.ReservedSquadId);
            Assert.Equal(0, procedure.AssignedApothecarySoldierId);

            RecruitmentProgramEvent programEvent = Assert.Single(loaded.ProgramEvents);
            Assert.Equal(RecruitmentEventType.CandidateQualified, programEvent.Type);
            Assert.Equal(3, programEvent.Count);
        }
        finally
        {
            GameStateRoundTripFixture.CleanupDb(path);
        }
    }

    private static RecruitmentProgram CreateProgram()
    {
        RecruitmentProgram program = new()
        {
            Id = 5,
            HomeWorldPlanetId = 42,
            EstablishedDate = new Date(42, 100, 1),
            LastProcessedDate = new Date(42, 100, 9),
            IsSetupComplete = true,
            Policy = RecruitmentPolicy.PlanetaryTithe,
            WorldType = RecruitmentWorldType.Death,
            MinimumGeneticCompatibility = 0.82f,
            AttributeFilters = new RecruitmentAttributeFilters
            {
                StrengthHalfSigmaSteps = 3,
                ConstitutionHalfSigmaSteps = 2,
                IntelligenceHalfSigmaSteps = 1,
                DexterityHalfSigmaSteps = 0,
                EgoHalfSigmaSteps = -1
            }
        };
        program.UnscreenedCohorts.Add(new RecruitmentCohort
        {
            Id = 6,
            CreatedDate = new Date(42, 100, 1),
            RemainingPopulation = 5432.25,
            MinimumAgeAtCreation = 10,
            MaximumAgeAtCreation = 12,
            IsFoundingCohort = true
        });
        program.QualifiedCandidates.Add(new RecruitmentCandidate
        {
            Id = 17,
            InductionDesignation = "Candidate XVII",
            SourceWorldPlanetId = 42,
            BirthDate = new Date(42, 88, 4),
            QualifiedDate = new Date(42, 100, 3),
            GeneticCompatibility = 0.91f,
            Attributes = Attributes(61.5f, 58, 54, 57, 60)
        });
        RecruitmentAspirant aspirant = new()
        {
            Id = 900_001,
            InductionDesignation = "Aspirant IX",
            SourceWorldPlanetId = 42,
            BirthDate = new Date(42, 87, 2),
            AdmittedDate = new Date(42, 99, 40),
            Phase = RecruitmentPhase.Phase4,
            PhaseStartedDate = new Date(42, 100, 8),
            WeeksInCurrentPhase = 2,
            TrainingProgress = 1.75f,
            GeneticCompatibility = 0.94f,
            Attributes = Attributes(63, 62, 56, 59, 61)
        };
        aspirant.SkillPoints[8] = 4.25f;
        aspirant.Events.Add(new RecruitmentAspirantEvent
        {
            Date = new Date(42, 100, 8),
            Type = RecruitmentEventType.ImplantationCompleted,
            Detail = "Implantation stable"
        });
        program.Aspirants.Add(aspirant);
        program.Procedures.Add(new RecruitmentProcedure
        {
            Id = 21,
            AspirantId = aspirant.Id,
            GeneticCompatibility = aspirant.GeneticCompatibility,
            Type = RecruitmentProcedureType.Implantation,
            Phase = RecruitmentPhase.Phase4,
            Status = RecruitmentProcedureStatus.Pending,
            AssignedApothecarySoldierId = 0,
            WeeksRemaining = 1,
            ReservedSquadId = 7
        });
        program.ProgramEvents.Add(new RecruitmentProgramEvent
        {
            Date = new Date(42, 100, 3),
            Type = RecruitmentEventType.CandidateQualified,
            Count = 3,
            Detail = "Three candidates qualified"
        });
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

    private static SqliteConnection OpenDatabase(string path)
    {
        SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            ForeignKeys = true,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void CreateSchema(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = File.ReadAllText(
            Path.Combine(RulesDatabaseFixture.RepositoryRoot, "Database", "SaveStructure.sql"));
        command.ExecuteNonQuery();
    }

    private static void CreateReferencedRows(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO Planet
                (Id, PlanetTemplateId, Name, x, y, Importance, TaxLevel, CapitalRegionId)
                VALUES (42, 1, 'Home World', 0, 0, 1, 0, NULL);
            INSERT INTO Unit (Id, FactionId, UnitTemplateId, ParentUnitId, Name)
                VALUES (4, 1, 1, NULL, 'Tenth Company');
            INSERT INTO Squad
                (Id, SquadTemplateId, ParentUnitId, Name, LoadedShipId, LandedRegionId,
                 TrainingFocus)
                VALUES (7, 1, 4, 'Company Headquarters', NULL, NULL, 0);";
        command.ExecuteNonQuery();
    }
}
