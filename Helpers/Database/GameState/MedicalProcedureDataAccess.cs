using OnlyWar.Models.Soldiers;
using System.Collections.Generic;
using System.Data;

namespace OnlyWar.Helpers.Database.GameState
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
    }
}
