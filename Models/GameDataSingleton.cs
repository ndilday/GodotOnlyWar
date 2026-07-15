using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Helpers.Storage;
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
        internal CampaignRecoverabilityTracker Recoverability { get; } = new();

        public void InitializeNewGameData(GameRulesData gameRulesData, Date date, string chapterName = null, int seed = 1)
        {
            GameRulesData = gameRulesData;
            Date = date;
            Sector = SectorBuilder.GenerateSector(seed, gameRulesData, date, chapterName); // New sector generation
            Recoverability.BeginNewCampaign();
        }
        public void LoadGameDataFromBlob(GameRulesData gameRulesData, Date date, Sector sector)
        {
            GameRulesData = gameRulesData;
            Date = date;
            Sector = sector; // Load existing sector data
            Recoverability.BeginLoadedCampaign();
         }

        /// <summary>
        /// Releases the active campaign when returning to the title screen. The immutable rules
        /// database is reloaded with the next New Game/Load operation, keeping title-screen state
        /// from accidentally exposing the campaign that was just closed.
        /// </summary>
        public void ClearCampaign()
        {
            GameRulesData = null;
            Date = null;
            Sector = null;
        }

        // Registers the sector while it is still being generated, so the opening-scenario stamp can
        // create a GameSession for its planet-scoped simulations before SectorBuilder.GenerateSector
        // returns and assigns the final Sector. Mirrors what LoadGameDataFromBlob does for a restored
        // sector. GameRulesData/Date are already set by InitializeNewGameData (and by the tests)
        // before generation runs.
        internal void SetSectorDuringGeneration(Sector sector)
        {
            Sector = sector;
        }

        private GameDataSingleton()
        {
        }
    }
}
