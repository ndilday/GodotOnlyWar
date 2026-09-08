using OnlyWar.Models;

namespace OnlyWar.Helpers.Database.GameRules;

public static class GameRulesLoader
{
    public static GameRulesData Load(string databasePath) =>
        new(new GameRulesDataAccess().GetData(databasePath));
}
