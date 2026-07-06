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
        public bool IsInitialized => GameRulesData != null && Sector != null && Date != null;

        public void InitializeNewGameData(GameRulesData gameRulesData, Date date, string chapterName = null, int seed = 1)
        {
            GameRulesData = gameRulesData;
            Date = date;
            Sector = SectorBuilder.GenerateSector(seed, gameRulesData, date, chapterName); // New sector generation
        }
        public void LoadGameDataFromBlob(GameRulesData gameRulesData, Date date, Sector sector)
        {
            GameRulesData = gameRulesData;
            Date = date;
            Sector = sector; // Load existing sector data
         }

        // Registers the sector while it is still being generated, so the opening-scenario stamp can
        // run its planet-scoped simulations (which read GameDataSingleton.Instance.Sector) before
        // SectorBuilder.GenerateSector returns and assigns the final Sector. Mirrors what
        // LoadGameDataFromBlob does for a restored sector. GameRulesData/Date are already set by
        // InitializeNewGameData (and by the tests) before generation runs.
        internal void SetSectorDuringGeneration(Sector sector)
        {
            Sector = sector;
        }

        private GameDataSingleton()
        {
        }
    }
}
