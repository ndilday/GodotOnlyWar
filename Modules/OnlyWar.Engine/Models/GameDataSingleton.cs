using OnlyWar.Builders;
using OnlyWar.Helpers.Storage;
using System;

namespace OnlyWar.Models
{
    public sealed class GameDataSingleton
    {
        private static readonly Lazy<GameDataSingleton> lazy =
        new Lazy<GameDataSingleton>(() => new GameDataSingleton());

        public static GameDataSingleton Instance { get { return lazy.Value; } }

        public GameRulesData GameRulesData { get; private set; }
        public Sector Sector { get; private set; }
        public Date Date { get; set; }
        public bool UpgradePending { get; internal set; }
        public bool IsInitialized => GameRulesData != null && Sector != null && Date != null;
        public CampaignRecoverabilityTracker Recoverability { get; } = new();

        /// <summary>
        /// Builds a complete candidate campaign and installs it only once generation has succeeded.
        /// Generation and the opening-scenario warm-up run against the candidate's own session
        /// (SB-09), so a failed attempt leaves the campaign that is already loaded untouched.
        /// </summary>
        public void InitializeNewGameData(GameRulesData gameRulesData, Date date, string chapterName = null, int seed = 1,
                                          ScenarioFactionSelection invaderSelection = null)
        {
            Sector candidate = SectorBuilder.GenerateSector(
                seed,
                gameRulesData,
                date,
                Helpers.Application.Adapters.Generation.CandidateGenerationSupport.For(
                    gameRulesData, date, Helpers.StaticRNG.Instance),
                chapterName,
                invaderSelection);
            GameRulesData = gameRulesData;
            Date = date;
            Sector = candidate;
            UpgradePending = false;
            Recoverability.BeginNewCampaign();
        }
        public void LoadGameDataFromBlob(
            GameRulesData gameRulesData,
            Date date,
            Sector sector,
            bool upgradePending = false)
        {
            GameRulesData = gameRulesData;
            Date = date;
            Sector = sector; // Load existing sector data
            Helpers.Simulation.CampaignEventBindings.Attach(sector, date);
            UpgradePending = upgradePending;
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

        private GameDataSingleton()
        {
        }
    }
}
