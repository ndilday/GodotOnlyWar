using System;
using System.Collections.Generic;
using System.Data;

namespace OnlyWar.Helpers.Database.GameState
{
    // Primitive save rows deliberately isolate SQLite from the recruitment domain. The
    // recruitment feature maps its aggregate at the save boundary, so simulation types can
    // evolve without leaking behavior into persistence.
    public sealed record RecruitmentProgramRow(
        int Id, int HomeWorldPlanetId, bool IsConfigured, int Policy, int WorldType,
        int StrengthThreshold, int ConstitutionThreshold, int IntelligenceThreshold,
        int DexterityThreshold, int EgoThreshold, float GeneticCompatibilityThreshold,
        int EstablishedDate, int? LastProcessedDate);

    public sealed record RecruitmentUnscreenedCohortRow(
        int Id, int ProgramId, int CreatedDate, double RemainingPopulation,
        float MinimumAgeAtCreation, float MaximumAgeAtCreation, bool IsFoundingPool);

    public sealed record RecruitmentCandidateRow(
        int Id, int ProgramId, int SourcePlanetId, int BirthDate, float Strength,
        float Constitution, float Intelligence, float Dexterity, float Ego,
        float GeneticCompatibility, int QualificationDate, string Designation);

    public sealed record RecruitmentAspirantRow(
        int Id, int ProgramId, int SourcePlanetId, int BirthDate, float Strength,
        float Constitution, float Intelligence, float Dexterity, float Ego,
        float GeneticCompatibility, int AdmissionDate, int CurrentPhase,
        int PhaseStartedDate, int WeeksInCurrentPhase, float TrainingProgress,
        string Designation);

    public sealed record RecruitmentAspirantSkillRow(
        int AspirantId, int BaseSkillId, float PointsInvested);

    public sealed record RecruitmentAspirantEventRow(
        int AspirantId, int EventDate, int EventType, string Detail);

    public sealed record RecruitmentProcedureRow(
        int Id, int ProgramId, int AspirantId, float GeneticCompatibility, int ProcedureType,
        int Phase, int Status, int AssignedApothecarySoldierId, int WeeksRemaining,
        int? ReservedSquadId);

    public sealed record RecruitmentProgramLogRow(
        int ProgramId, int EventDate, int EventType, int EventCount, string Entry);

    public sealed class RecruitmentSaveData
    {
        public List<RecruitmentProgramRow> Programs { get; init; } = [];
        public List<RecruitmentUnscreenedCohortRow> UnscreenedCohorts { get; init; } = [];
        public List<RecruitmentCandidateRow> Candidates { get; init; } = [];
        public List<RecruitmentAspirantRow> Aspirants { get; init; } = [];
        public List<RecruitmentAspirantSkillRow> AspirantSkills { get; init; } = [];
        public List<RecruitmentAspirantEventRow> AspirantEvents { get; init; } = [];
        public List<RecruitmentProcedureRow> Procedures { get; init; } = [];
        public List<RecruitmentProgramLogRow> ProgramLog { get; init; } = [];

        public static RecruitmentSaveData Empty => new();
    }

    internal sealed class RecruitmentDataAccess
    {
        internal RecruitmentSaveData GetData(IDbConnection connection)
        {
            return new RecruitmentSaveData
            {
                Programs = GetPrograms(connection),
                UnscreenedCohorts = GetUnscreenedCohorts(connection),
                Candidates = GetCandidates(connection),
                Aspirants = GetAspirants(connection),
                AspirantSkills = GetAspirantSkills(connection),
                AspirantEvents = GetAspirantEvents(connection),
                Procedures = GetProcedures(connection),
                ProgramLog = GetProgramLog(connection)
            };
        }

        internal void SaveData(IDbTransaction transaction, RecruitmentSaveData data)
        {
            data ??= RecruitmentSaveData.Empty;
            foreach (RecruitmentProgramRow row in data.Programs)
                SaveProgram(transaction, row);
            foreach (RecruitmentUnscreenedCohortRow row in data.UnscreenedCohorts)
                SaveUnscreenedCohort(transaction, row);
            foreach (RecruitmentCandidateRow row in data.Candidates)
                SaveCandidate(transaction, row);
            foreach (RecruitmentAspirantRow row in data.Aspirants)
                SaveAspirant(transaction, row);
            foreach (RecruitmentAspirantSkillRow row in data.AspirantSkills)
                SaveAspirantSkill(transaction, row);
            foreach (RecruitmentAspirantEventRow row in data.AspirantEvents)
                SaveAspirantEvent(transaction, row);
            foreach (RecruitmentProcedureRow row in data.Procedures)
                SaveProcedure(transaction, row);
            foreach (RecruitmentProgramLogRow row in data.ProgramLog)
                SaveProgramLog(transaction, row);
        }

        private static List<RecruitmentProgramRow> GetPrograms(IDbConnection connection)
        {
            List<RecruitmentProgramRow> rows = [];
            using IDbCommand command = Query(connection, @"SELECT Id, HomeWorldPlanetId,
                IsConfigured, Policy, WorldType, StrengthThreshold, ConstitutionThreshold,
                IntelligenceThreshold, DexterityThreshold, EgoThreshold,
                GeneticCompatibilityThreshold, EstablishedDate, LastProcessedDate
                FROM RecruitmentProgram ORDER BY Id");
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new(
                    reader.GetInt32(0), reader.GetInt32(1), reader.GetBoolean(2),
                    reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5),
                    reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8),
                    reader.GetInt32(9), (float)reader.GetDouble(10), reader.GetInt32(11),
                    NullableInt(reader, 12)));
            }
            return rows;
        }

        private static List<RecruitmentUnscreenedCohortRow> GetUnscreenedCohorts(
            IDbConnection connection)
        {
            List<RecruitmentUnscreenedCohortRow> rows = [];
            using IDbCommand command = Query(connection, @"SELECT Id, ProgramId, CreatedDate,
                RemainingPopulation, MinimumAgeAtCreation, MaximumAgeAtCreation, IsFoundingPool
                FROM RecruitmentUnscreenedCohort ORDER BY Id");
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new(
                    reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
                    reader.GetDouble(3), (float)reader.GetDouble(4),
                    (float)reader.GetDouble(5), reader.GetBoolean(6)));
            }
            return rows;
        }

        private static List<RecruitmentCandidateRow> GetCandidates(IDbConnection connection)
        {
            List<RecruitmentCandidateRow> rows = [];
            using IDbCommand command = Query(connection, @"SELECT Id, ProgramId, SourcePlanetId,
                BirthDate, Strength, Constitution, Intelligence, Dexterity, Ego,
                GeneticCompatibility, QualificationDate, Designation
                FROM RecruitmentCandidate ORDER BY Id");
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new(
                    reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
                    reader.GetInt32(3), (float)reader.GetDouble(4),
                    (float)reader.GetDouble(5), (float)reader.GetDouble(6),
                    (float)reader.GetDouble(7), (float)reader.GetDouble(8),
                    (float)reader.GetDouble(9), reader.GetInt32(10), reader.GetString(11)));
            }
            return rows;
        }

        private static List<RecruitmentAspirantRow> GetAspirants(IDbConnection connection)
        {
            List<RecruitmentAspirantRow> rows = [];
            using IDbCommand command = Query(connection, @"SELECT Id, ProgramId, SourcePlanetId,
                BirthDate, Strength, Constitution, Intelligence, Dexterity, Ego,
                GeneticCompatibility, AdmissionDate, CurrentPhase, PhaseStartedDate,
                WeeksInCurrentPhase, TrainingProgress, Designation
                FROM RecruitmentAspirant ORDER BY Id");
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new(
                    reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
                    reader.GetInt32(3), (float)reader.GetDouble(4),
                    (float)reader.GetDouble(5), (float)reader.GetDouble(6),
                    (float)reader.GetDouble(7), (float)reader.GetDouble(8),
                    (float)reader.GetDouble(9), reader.GetInt32(10), reader.GetInt32(11),
                    reader.GetInt32(12), reader.GetInt32(13), (float)reader.GetDouble(14),
                    reader.GetString(15)));
            }
            return rows;
        }

        private static List<RecruitmentAspirantSkillRow> GetAspirantSkills(
            IDbConnection connection)
        {
            List<RecruitmentAspirantSkillRow> rows = [];
            using IDbCommand command = Query(connection, @"SELECT AspirantId, BaseSkillId,
                PointsInvested FROM RecruitmentAspirantSkill ORDER BY AspirantId, BaseSkillId");
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
                rows.Add(new(reader.GetInt32(0), reader.GetInt32(1), (float)reader.GetDouble(2)));
            return rows;
        }

        private static List<RecruitmentAspirantEventRow> GetAspirantEvents(
            IDbConnection connection)
        {
            List<RecruitmentAspirantEventRow> rows = [];
            using IDbCommand command = Query(connection, @"SELECT AspirantId, EventDate,
                EventType, Detail FROM RecruitmentAspirantEvent
                ORDER BY AspirantId, EventDate, rowid");
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
                rows.Add(new(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
                    reader.GetString(3)));
            return rows;
        }

        private static List<RecruitmentProcedureRow> GetProcedures(IDbConnection connection)
        {
            List<RecruitmentProcedureRow> rows = [];
            using IDbCommand command = Query(connection, @"SELECT Id, ProgramId, AspirantId,
                GeneticCompatibility, ProcedureType, Phase, Status,
                AssignedApothecarySoldierId, WeeksRemaining, ReservedSquadId
                FROM RecruitmentProcedure ORDER BY Id");
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new(
                    reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
                    (float)reader.GetDouble(3), reader.GetInt32(4), reader.GetInt32(5),
                    reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8),
                    NullableInt(reader, 9)));
            }
            return rows;
        }

        private static List<RecruitmentProgramLogRow> GetProgramLog(IDbConnection connection)
        {
            List<RecruitmentProgramLogRow> rows = [];
            using IDbCommand command = Query(connection, @"SELECT ProgramId, EventDate,
                EventType, EventCount, Entry FROM RecruitmentProgramLog
                ORDER BY ProgramId, EventDate, rowid");
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
                rows.Add(new(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
                    reader.GetInt32(3), reader.GetString(4)));
            return rows;
        }

        private static void SaveProgram(IDbTransaction transaction, RecruitmentProgramRow row)
        {
            using IDbCommand command = Insert(transaction, @"INSERT INTO RecruitmentProgram
                (Id, HomeWorldPlanetId, IsConfigured, Policy, WorldType, StrengthThreshold,
                 ConstitutionThreshold, IntelligenceThreshold, DexterityThreshold, EgoThreshold,
                 GeneticCompatibilityThreshold, EstablishedDate, LastProcessedDate)
                VALUES (@id, @homeWorldPlanetId, @isConfigured, @policy, @worldType,
                 @strengthThreshold, @constitutionThreshold, @intelligenceThreshold,
                 @dexterityThreshold, @egoThreshold, @geneticCompatibilityThreshold,
                 @establishedDate, @lastProcessedDate)");
            command.AddParam("@id", row.Id);
            command.AddParam("@homeWorldPlanetId", row.HomeWorldPlanetId);
            command.AddParam("@isConfigured", row.IsConfigured ? 1 : 0);
            command.AddParam("@policy", row.Policy);
            command.AddParam("@worldType", row.WorldType);
            command.AddParam("@strengthThreshold", row.StrengthThreshold);
            command.AddParam("@constitutionThreshold", row.ConstitutionThreshold);
            command.AddParam("@intelligenceThreshold", row.IntelligenceThreshold);
            command.AddParam("@dexterityThreshold", row.DexterityThreshold);
            command.AddParam("@egoThreshold", row.EgoThreshold);
            command.AddParam("@geneticCompatibilityThreshold", row.GeneticCompatibilityThreshold);
            command.AddParam("@establishedDate", row.EstablishedDate);
            command.AddParam("@lastProcessedDate", row.LastProcessedDate);
            command.ExecuteNonQuery();
        }

        private static void SaveUnscreenedCohort(
            IDbTransaction transaction, RecruitmentUnscreenedCohortRow row)
        {
            using IDbCommand command = Insert(transaction, @"INSERT INTO
                RecruitmentUnscreenedCohort (Id, ProgramId, CreatedDate, RemainingPopulation,
                MinimumAgeAtCreation, MaximumAgeAtCreation, IsFoundingPool)
                VALUES (@id, @programId, @createdDate, @remainingPopulation,
                @minimumAgeAtCreation, @maximumAgeAtCreation, @isFoundingPool)");
            command.AddParam("@id", row.Id);
            command.AddParam("@programId", row.ProgramId);
            command.AddParam("@createdDate", row.CreatedDate);
            command.AddParam("@remainingPopulation", row.RemainingPopulation);
            command.AddParam("@minimumAgeAtCreation", row.MinimumAgeAtCreation);
            command.AddParam("@maximumAgeAtCreation", row.MaximumAgeAtCreation);
            command.AddParam("@isFoundingPool", row.IsFoundingPool ? 1 : 0);
            command.ExecuteNonQuery();
        }

        private static void SaveCandidate(IDbTransaction transaction, RecruitmentCandidateRow row)
        {
            using IDbCommand command = Insert(transaction, @"INSERT INTO RecruitmentCandidate
                (Id, ProgramId, SourcePlanetId, BirthDate, Strength, Constitution, Intelligence,
                 Dexterity, Ego, GeneticCompatibility, QualificationDate, Designation)
                VALUES (@id, @programId, @sourcePlanetId, @birthDate, @strength, @constitution,
                 @intelligence, @dexterity, @ego, @geneticCompatibility, @qualificationDate,
                 @designation)");
            command.AddParam("@id", row.Id);
            command.AddParam("@programId", row.ProgramId);
            command.AddParam("@sourcePlanetId", row.SourcePlanetId);
            command.AddParam("@birthDate", row.BirthDate);
            command.AddParam("@strength", row.Strength);
            command.AddParam("@constitution", row.Constitution);
            command.AddParam("@intelligence", row.Intelligence);
            command.AddParam("@dexterity", row.Dexterity);
            command.AddParam("@ego", row.Ego);
            command.AddParam("@geneticCompatibility", row.GeneticCompatibility);
            command.AddParam("@qualificationDate", row.QualificationDate);
            command.AddParam("@designation", row.Designation);
            command.ExecuteNonQuery();
        }

        private static void SaveAspirant(IDbTransaction transaction, RecruitmentAspirantRow row)
        {
            using IDbCommand command = Insert(transaction, @"INSERT INTO RecruitmentAspirant
                (Id, ProgramId, SourcePlanetId, BirthDate, Strength, Constitution, Intelligence,
                 Dexterity, Ego, GeneticCompatibility, AdmissionDate, CurrentPhase,
                 PhaseStartedDate, WeeksInCurrentPhase, TrainingProgress, Designation)
                VALUES (@id, @programId, @sourcePlanetId, @birthDate, @strength, @constitution,
                 @intelligence, @dexterity, @ego, @geneticCompatibility, @admissionDate,
                 @currentPhase, @phaseStartedDate, @weeksInCurrentPhase, @trainingProgress,
                 @designation)");
            command.AddParam("@id", row.Id);
            command.AddParam("@programId", row.ProgramId);
            command.AddParam("@sourcePlanetId", row.SourcePlanetId);
            command.AddParam("@birthDate", row.BirthDate);
            command.AddParam("@strength", row.Strength);
            command.AddParam("@constitution", row.Constitution);
            command.AddParam("@intelligence", row.Intelligence);
            command.AddParam("@dexterity", row.Dexterity);
            command.AddParam("@ego", row.Ego);
            command.AddParam("@geneticCompatibility", row.GeneticCompatibility);
            command.AddParam("@admissionDate", row.AdmissionDate);
            command.AddParam("@currentPhase", row.CurrentPhase);
            command.AddParam("@phaseStartedDate", row.PhaseStartedDate);
            command.AddParam("@weeksInCurrentPhase", row.WeeksInCurrentPhase);
            command.AddParam("@trainingProgress", row.TrainingProgress);
            command.AddParam("@designation", row.Designation);
            command.ExecuteNonQuery();
        }

        private static void SaveAspirantSkill(
            IDbTransaction transaction, RecruitmentAspirantSkillRow row)
        {
            using IDbCommand command = Insert(transaction, @"INSERT INTO RecruitmentAspirantSkill
                (AspirantId, BaseSkillId, PointsInvested)
                VALUES (@aspirantId, @baseSkillId, @pointsInvested)");
            command.AddParam("@aspirantId", row.AspirantId);
            command.AddParam("@baseSkillId", row.BaseSkillId);
            command.AddParam("@pointsInvested", row.PointsInvested);
            command.ExecuteNonQuery();
        }

        private static void SaveAspirantEvent(
            IDbTransaction transaction, RecruitmentAspirantEventRow row)
        {
            using IDbCommand command = Insert(transaction, @"INSERT INTO RecruitmentAspirantEvent
                (AspirantId, EventDate, EventType, Detail)
                VALUES (@aspirantId, @eventDate, @eventType, @detail)");
            command.AddParam("@aspirantId", row.AspirantId);
            command.AddParam("@eventDate", row.EventDate);
            command.AddParam("@eventType", row.EventType);
            command.AddParam("@detail", row.Detail);
            command.ExecuteNonQuery();
        }

        private static void SaveProcedure(IDbTransaction transaction, RecruitmentProcedureRow row)
        {
            using IDbCommand command = Insert(transaction, @"INSERT INTO RecruitmentProcedure
                (Id, ProgramId, AspirantId, GeneticCompatibility, ProcedureType, Phase, Status,
                 AssignedApothecarySoldierId, WeeksRemaining, ReservedSquadId)
                VALUES (@id, @programId, @aspirantId, @geneticCompatibility, @procedureType,
                 @phase, @status, @assignedApothecarySoldierId, @weeksRemaining,
                 @reservedSquadId)");
            command.AddParam("@id", row.Id);
            command.AddParam("@programId", row.ProgramId);
            command.AddParam("@aspirantId", row.AspirantId);
            command.AddParam("@geneticCompatibility", row.GeneticCompatibility);
            command.AddParam("@procedureType", row.ProcedureType);
            command.AddParam("@phase", row.Phase);
            command.AddParam("@status", row.Status);
            command.AddParam("@assignedApothecarySoldierId", row.AssignedApothecarySoldierId);
            command.AddParam("@weeksRemaining", row.WeeksRemaining);
            command.AddParam("@reservedSquadId", row.ReservedSquadId);
            command.ExecuteNonQuery();
        }

        private static void SaveProgramLog(
            IDbTransaction transaction, RecruitmentProgramLogRow row)
        {
            using IDbCommand command = Insert(transaction, @"INSERT INTO RecruitmentProgramLog
                (ProgramId, EventDate, EventType, EventCount, Entry)
                VALUES (@programId, @eventDate, @eventType, @eventCount, @entry)");
            command.AddParam("@programId", row.ProgramId);
            command.AddParam("@eventDate", row.EventDate);
            command.AddParam("@eventType", row.EventType);
            command.AddParam("@eventCount", row.EventCount);
            command.AddParam("@entry", row.Entry);
            command.ExecuteNonQuery();
        }

        private static int? NullableInt(IDataRecord reader, int ordinal) =>
            reader[ordinal] is DBNull ? null : reader.GetInt32(ordinal);

        private static IDbCommand Query(IDbConnection connection, string commandText)
        {
            IDbCommand command = connection.CreateCommand();
            command.CommandText = commandText;
            return command;
        }

        private static IDbCommand Insert(IDbTransaction transaction, string commandText)
        {
            IDbCommand command = transaction.Connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = commandText;
            return command;
        }
    }
}
