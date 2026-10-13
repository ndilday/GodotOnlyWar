using OnlyWar.Domain.Soldiers;
using System.Collections.Generic;
using System.Data;

namespace OnlyWar.Persistence.Database.GameState
{
    public class MedicalProcedureDataAccess
    {
        public List<MedicalProcedure> GetProcedures(IDbConnection connection)
        {
            List<MedicalProcedure> procedures = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"SELECT SoldierId, HitLocationTemplateId, ProcedureType,
                    WeeksRemaining, RequisitionCost FROM MedicalProcedure";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    procedures.Add(new MedicalProcedure(
                        reader.GetInt32(0),
                        reader.GetInt32(1),
                        (MedicalProcedureType)reader.GetInt32(2),
                        reader.GetInt32(3),
                        reader.GetInt32(4)));
                }
            }
            return procedures;
        }

        public void SaveProcedure(IDbTransaction transaction, MedicalProcedure procedure)
        {
            using (var command = transaction.Connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO MedicalProcedure
                    (SoldierId, HitLocationTemplateId, ProcedureType, WeeksRemaining, RequisitionCost)
                    VALUES (@soldierId, @hitLocationTemplateId, @procedureType, @weeksRemaining, @requisitionCost);";
                command.AddParam("@soldierId", procedure.SoldierId);
                command.AddParam("@hitLocationTemplateId", procedure.HitLocationTemplateId);
                command.AddParam("@procedureType", (int)procedure.ProcedureType);
                command.AddParam("@weeksRemaining", procedure.WeeksRemaining);
                command.AddParam("@requisitionCost", procedure.RequisitionCost);
                command.ExecuteNonQuery();
            }
        }

        /// <summary>Queued cybernetic repairs, in queue order.</summary>
        public List<CyberneticRepairJob> GetRepairJobs(IDbConnection connection)
        {
            List<CyberneticRepairJob> jobs = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"SELECT SoldierId, HitLocationTemplateId, HoursCompleted,
                    RequisitionCost FROM CyberneticRepairJob ORDER BY Ordinal";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    jobs.Add(new CyberneticRepairJob(
                        reader.GetInt32(0),
                        reader.GetInt32(1),
                        System.Convert.ToSingle(reader[2]),
                        reader.GetInt32(3)));
                }
            }
            return jobs;
        }

        public void SaveRepairJobs(IDbTransaction transaction, IEnumerable<CyberneticRepairJob> jobs)
        {
            int ordinal = 0;
            foreach (CyberneticRepairJob job in jobs ?? [])
            {
                using var command = transaction.Connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO CyberneticRepairJob
                    (Ordinal, SoldierId, HitLocationTemplateId, HoursCompleted, RequisitionCost)
                    VALUES (@ordinal, @soldierId, @hitLocationTemplateId, @hoursCompleted, @requisitionCost);";
                command.AddParam("@ordinal", ordinal++);
                command.AddParam("@soldierId", job.SoldierId);
                command.AddParam("@hitLocationTemplateId", job.HitLocationTemplateId);
                command.AddParam("@hoursCompleted", job.HoursCompleted);
                command.AddParam("@requisitionCost", job.RequisitionCost);
                command.ExecuteNonQuery();
            }
        }
    }
}
