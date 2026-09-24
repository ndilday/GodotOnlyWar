using OnlyWar.Domain;
using System;
using System.IO;
using OnlyWar.Persistence.Database.GameRules;

namespace OnlyWar.Tests.Fixtures;

internal static class RulesDatabaseFixture
{
    public static string RepositoryRoot
    {
        get
        {
            return Directory.GetParent(DatabasePath)?.Parent?.FullName
                ?? throw new DirectoryNotFoundException("Could not resolve repository root from the rules database path.");
        }
    }

    public static string DatabasePath
    {
        get
        {
            string directory = AppContext.BaseDirectory;
            while (directory != null)
            {
                string candidate = Path.Combine(directory, "Database", "OnlyWar.s3db");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = Directory.GetParent(directory)?.FullName;
            }

            throw new FileNotFoundException("Could not find Database\\OnlyWar.s3db from the test output path.");
        }
    }

    public static string SaveSchemaPath =>
        Path.Combine(RepositoryRoot, "Database", "SaveStructure.sql");

    public static GameRulesBlob LoadRules()
    {
        return new GameRulesDataAccess().GetData(DatabasePath);
    }

    /// <summary>
    /// Copies the rules database to a unique file under %TEMP% for a test to mutate or load.
    /// Pair with <see cref="DeleteTemporaryCopy"/> in a finally block.
    /// </summary>
    public static string CreateTemporaryCopy(string prefix)
    {
        string path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.s3db");
        File.Copy(DatabasePath, path);
        return path;
    }

    // Total wait is 650 ms across the retries.
    private static readonly int[] DeleteRetryDelaysMs = { 50, 100, 200, 300 };

    /// <summary>
    /// Best-effort delete of a temporary database file. Our own connections are unpooled and
    /// disposed, so a lock here is external (typically antivirus scanning the new file); retry
    /// briefly, then leave the file in %TEMP% rather than fail the test over cleanup.
    /// </summary>
    public static void DeleteTemporaryCopy(string path)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                return;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                if (attempt >= DeleteRetryDelaysMs.Length)
                {
                    return;
                }
                System.Threading.Thread.Sleep(DeleteRetryDelaysMs[attempt]);
            }
        }
    }
}
