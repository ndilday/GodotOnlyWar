using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Models;
using System;
using System.Linq;

namespace OnlyWar.Helpers.Storage
{
    /// <summary>
    /// Captures the currently loaded campaign through the single production persistence path.
    /// Slot selection, autosave rotation, diagnostics, and UI feedback belong to callers; this
    /// class only maps the live aggregate into the existing atomic database writer.
    /// </summary>
    internal static class CurrentCampaignSaveWriter
    {
        internal static void Write(string filePath)
        {
            GameDataSingleton game = GameDataSingleton.Instance;
            if (!game.IsInitialized)
            {
                throw new InvalidOperationException("No campaign is currently loaded.");
            }

            var force = game.Sector.PlayerForce;
            var units = game.GameRulesData.Factions.SelectMany(faction => faction.Units);
            GameStateDataAccess.Instance.SaveData(
                filePath,
                game.Date,
                force.Army.Requisition,
                force.GeneseedStockpile,
                force.GeneseedPurity,
                game.Sector.Scenario,
                force.Army.MedicalProcedures,
                game.Sector.Characters,
                force.Requests,
                force.Pledges,
                game.Sector.Planets.Values,
                game.Sector.Fleets.Values,
                units,
                force.Army.PlayerSoldierMap.Values,
                force.Army.FallenBrothers.Values,
                force.Army.LoadoutDoctrine,
                force.Army.CharacterLoadoutDoctrine,
                homeWorldPlanetId: force.HomeWorldPlanetId,
                recruitment: RecruitmentSaveMapper.ToSaveData(force.RecruitmentProgram),
                lastTurnReportSnapshot: force.LastTurnReportSnapshot,
                campaignEventLedger: force.CampaignEventLedger,
                chapterChronicle: force.ChapterChronicle,
                campaignIdentity: force.CampaignIdentity,
                relationshipLedger: game.Sector.RelationshipLedger,
                equipmentLoadoutDoctrine: force.Army.EquipmentLoadoutDoctrine,
                worldControlEpisodes: force.WorldControlEpisodes.States,
                additionalOrders: force.RecruitmentProgram?.TaskOrder == null
                    ? []
                    : [force.RecruitmentProgram.TaskOrder]);
            // A current-format save has committed successfully at this point. SaveData is
            // atomic, so reaching this line is the success boundary.
            game.UpgradePending = false;
        }
    }
}
