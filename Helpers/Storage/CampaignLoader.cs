using OnlyWar.Builders;
using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Models;
using System;
using System.Linq;

namespace OnlyWar.Helpers.Storage
{
    /// <summary>
    /// Reconstructs a campaign from a selected save and publishes it only after the complete
    /// load has succeeded. Both the title screen and the in-campaign Load action use this path,
    /// so choosing a file never falls back to an implicit "newest save" policy.
    /// </summary>
    internal static class CampaignLoader
    {
        internal static void LoadIntoSingleton(string savePath)
        {
            if (string.IsNullOrWhiteSpace(savePath))
            {
                throw new ArgumentException("A save file must be selected.", nameof(savePath));
            }

            GameRulesData gameRulesData = new(GameStorage.RulesDatabasePath);
            GameStateDataBlob gameState = LoadGameData(gameRulesData, savePath);
            Sector sector = SavedGameLoader.BuildSectorFromBlob(gameState, gameRulesData);

            // Subsectors and warp lanes are derived deterministically from planet positions
            // rather than persisted, so rebuild them before publishing the loaded campaign.
            SectorBuilder.GenerateWarpNetwork(sector, gameRulesData);
            GameDataSingleton.Instance.LoadGameDataFromBlob(
                gameRulesData,
                gameState.CurrentDate,
                sector);
        }

        private static GameStateDataBlob LoadGameData(
            GameRulesData gameRulesData,
            string savePath)
        {
            var shipTemplateMap = gameRulesData.Factions
                .Where(faction => faction.ShipTemplates != null)
                .SelectMany(faction => faction.ShipTemplates.Values)
                .ToDictionary(template => template.Id);
            var unitTemplateMap = gameRulesData.Factions
                .Where(faction => faction.UnitTemplates != null)
                .SelectMany(faction => faction.UnitTemplates.Values)
                .ToDictionary(template => template.Id);
            var squadTemplateMap = gameRulesData.Factions
                .Where(faction => faction.SquadTemplates != null)
                .SelectMany(faction => faction.SquadTemplates.Values)
                .ToDictionary(template => template.Id);
            var hitLocationMap = gameRulesData.BodyHitLocationTemplateMap.Values
                .SelectMany(locations => locations)
                .Distinct()
                .ToDictionary(location => location.Id);
            var soldierTemplateMap = gameRulesData.Factions
                .Where(faction => faction.SoldierTemplates != null)
                .SelectMany(faction => faction.SoldierTemplates.Values)
                .ToDictionary(template => template.Id);

            return GameStateDataAccess.Instance.GetData(
                savePath,
                gameRulesData.Factions.ToDictionary(faction => faction.Id),
                gameRulesData.PlanetTemplateMap,
                shipTemplateMap,
                unitTemplateMap,
                squadTemplateMap,
                gameRulesData.WeaponSets,
                hitLocationMap,
                gameRulesData.BaseSkillMap,
                soldierTemplateMap);
        }
    }
}
