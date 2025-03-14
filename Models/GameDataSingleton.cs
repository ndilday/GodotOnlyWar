using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Database.GameState;
using System;

namespace OnlyWar.Models
{
    internal sealed class GameDataSingleton
    {
        private static readonly Lazy<GameDataSingleton> lazy =
        new Lazy<GameDataSingleton>(() => new GameDataSingleton());

        public static GameDataSingleton Instance { get { return lazy.Value; } }

        public GameRulesData GameRulesData { get; private set; }
        public Sector Sector { get; private set; }
        public Date Date { get; set; }

        public void InitializeNewGameData(GameRulesData gameRulesData, Date date)
        {
            GameRulesData = gameRulesData;
            Date = date;
            Sector = SectorBuilder.GenerateSector(1, gameRulesData, date); // New sector generation
        }
        public void LoadGameDataFromBlob(GameRulesData gameRulesData, Date date, Sector sector)
        {
            GameRulesData = gameRulesData;
            Date = date;
            Sector = sector; // Load existing sector data
         }

        private GameDataSingleton()
        {
        }
    }
}
