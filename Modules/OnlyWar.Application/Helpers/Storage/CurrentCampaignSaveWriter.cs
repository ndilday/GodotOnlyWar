using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Models;
using OnlyWar.Contracts.Application;
using OnlyWar.Helpers.Simulation;
using System;
using System.Linq;

namespace OnlyWar.Helpers.Storage
{
    /// <summary>
    /// Captures the currently loaded campaign through the single production persistence path.
    /// Slot selection, autosave rotation, diagnostics, and UI feedback belong to callers; this
    /// class only maps the live aggregate into the existing atomic database writer.
    /// </summary>
    public static class CurrentCampaignSaveWriter
    {
        public static void Write(string filePath)
        {
            GameDataSingleton game = GameDataSingleton.Instance;
            if (!game.IsInitialized)
            {
                throw new InvalidOperationException("No campaign is currently loaded.");
            }

            Write(filePath, new GameSession(
                game.GameRulesData,
                game.Sector,
                game.Date,
                StaticRNG.Instance));
            game.UpgradePending = false;
        }

        public static void Write(string filePath, ICampaignSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            var force = session.Sector.PlayerForce;
            var units = session.Rules.Factions.SelectMany(faction => faction.Units);
            GameStateDataAccess.Instance.SaveData(
                filePath,
                session.CurrentDate,
                force.Army.Requisition,
                force.GeneseedStockpile,
                force.GeneseedPurity,
                session.Sector.Scenario,
                force.Army.MedicalProcedures,
                session.Sector.Characters,
                force.Requests,
                force.Pledges,
                session.Sector.Planets.Values,
                session.Sector.Fleets.Values,
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
                relationshipLedger: session.Sector.RelationshipLedger,
                equipmentLoadoutDoctrine: force.Army.EquipmentLoadoutDoctrine,
                worldControlEpisodes: force.WorldControlEpisodes.States,
                additionalOrders: force.RecruitmentProgram?.TaskOrder == null
                    ? []
                    : [force.RecruitmentProgram.TaskOrder],
                ghostPopulationSources: session.Sector.GhostPopulationSources,
                strategicInvasionForces: session.Sector.StrategicInvasionForces,
                chapterOperationalDoctrine: force.Army.ChapterOperationalDoctrine);
            // A current-format save has committed successfully at this point. SaveData is
            // atomic, so reaching this line is the success boundary.
            if (session is GameSession applicationSession)
            {
                applicationSession.UpgradePending = false;
            }
        }
    }
}
