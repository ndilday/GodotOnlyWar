using Microsoft.Data.Sqlite;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Persistence.Database.GameState;
using OnlyWar.Tests.Fixtures;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace OnlyWar.Tests.Data;

/// <summary>
/// Cybernetic repair state in the save (OnlyWar_TDD.md §6.6.1): queued jobs keep their
/// order and progress, and an installed augmetic keeps its own armor and constitution.
/// </summary>
public sealed class CyberneticRepairPersistenceTests
{
    [Fact]
    public void RepairJobs_RoundTripInQueueOrderWithTheirProgress()
    {
        string path = GameStateRoundTripFixture.CreateTempDbPath("cybernetic_repair_jobs");
        try
        {
            using SqliteConnection connection = OpenDatabase(path);
            CreateSchema(connection);
            // The jobs name soldiers this test does not write.
            using (SqliteCommand pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA foreign_keys = OFF";
                pragma.ExecuteNonQuery();
            }
            List<CyberneticRepairJob> jobs =
            [
                new(12, 4, 6.5f, 10),
                new(3, 5, 0f, 10),
                new(12, 7, 17.25f, 10)
            ];
            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                new MedicalProcedureDataAccess().SaveRepairJobs(transaction, jobs);
                transaction.Commit();
            }

            List<CyberneticRepairJob> loaded = new MedicalProcedureDataAccess().GetRepairJobs(connection);

            Assert.Equal(
                jobs.Select(job => (job.SoldierId, job.HitLocationTemplateId, job.HoursCompleted, job.RequisitionCost)),
                loaded.Select(job => (job.SoldierId, job.HitLocationTemplateId, job.HoursCompleted, job.RequisitionCost)));
        }
        finally
        {
            GameStateRoundTripFixture.CleanupDb(path);
        }
    }

    [Fact]
    public void AnInstalledAugmetic_RoundTripsItsArmorAndConstitution()
    {
        string path = GameStateRoundTripFixture.CreateTempDbPath("cybernetic_hit_location");
        try
        {
            SoldierTemplate template = new(
                941, TestModelFactory.HumanSpecies, "Battle-Brother", 1, 1, false, 0,
                Array.Empty<ValueTuple<BaseSkill, float>>());
            Soldier soldier = TestModelFactory.CreateSoldier(template, "Varro");
            BaseSkill skill = new(942, SkillCategory.Tech, "Engineering (Cybernetics)",
                OnlyWar.Domain.Soldiers.Attribute.Intelligence, 2f);
            soldier.AddSkillPoints(skill, 1f);
            HitLocation arm = soldier.Body.HitLocations.First(location => location.Template.Id == 4);
            arm.InstallCybernetic(new CyberneticOption(1, "Standard Augmetic", 5f, 40f));
            arm.Wounds.AddWound(WoundLevel.Major);

            using SqliteConnection connection = OpenDatabase(path);
            CreateSchema(connection);
            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                new SoldierDataAccess().SaveSoldier(transaction, soldier);
                transaction.Commit();
            }

            Dictionary<int, Soldier> loaded = new SoldierDataAccess().GetData(
                connection,
                soldier.Body.HitLocations.ToDictionary(location => location.Template.Id, location => location.Template),
                new Dictionary<int, BaseSkill> { [skill.Id] = skill },
                new Dictionary<int, SoldierTemplate> { [template.Id] = template },
                new Dictionary<int, Squad>());
            HitLocation loadedArm = loaded[soldier.Id].Body.HitLocations
                .First(location => location.Template.Id == 4);

            Assert.True(loadedArm.IsCybernetic);
            Assert.Equal(5f, loadedArm.NaturalArmor);
            Assert.Equal(40f, loadedArm.Constitution);
            Assert.Equal(arm.Wounds.WoundTotal, loadedArm.Wounds.WoundTotal);
        }
        finally
        {
            GameStateRoundTripFixture.CleanupDb(path);
        }
    }

    private static SqliteConnection OpenDatabase(string path)
    {
        SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            ForeignKeys = false,
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
}
