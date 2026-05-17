using System;
using System.IO;
using OnlyWar.Helpers.Database.GameRules;

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

    public static GameRulesBlob LoadRules()
    {
        return GameRulesDataAccess.Instance.GetData(DatabasePath);
    }
}
