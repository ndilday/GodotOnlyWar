using OnlyWar.Domain;

namespace OnlyWar.Persistence.Database.GameRules;

public static class GameRulesLoader
{
    public static GameRulesData Load(string databasePath) =>
        new(new GameRulesDataAccess().GetData(databasePath));
}
