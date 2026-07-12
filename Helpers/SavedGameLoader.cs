using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Models;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Helpers
{
    /// <summary>
    /// Rebuilds the in-memory <see cref="Sector"/> from a loaded <see cref="GameStateDataBlob"/>.
    /// Extracted from the StartMenu load flow so the reconstruction is unit-testable without the
    /// Godot runtime; the caller is still responsible for wiring the sector into
    /// <see cref="GameDataSingleton"/> and rebuilding the (derived) warp network.
    /// </summary>
    internal static class SavedGameLoader
    {
        internal static Sector BuildSectorFromBlob(GameStateDataBlob gameState, GameRulesData gameRulesData)
        {
            // The loaded root units are not registered on their faction by the data access
            // layer, but both the Army construction below and the in-game save path
            // (MainGameScene enumerates units via Faction.Units) expect the player's order of
            // battle to live there. Register the loaded player-faction root unit(s) here,
            // mirroring NewChapterBuilder, so both load and any subsequent save work.
            foreach (var rootUnit in gameState.Units
                         .Where(u => u.UnitTemplate.Faction.Id == gameRulesData.PlayerFaction.Id))
            {
                if (!gameRulesData.PlayerFaction.Units.Contains(rootUnit))
                {
                    gameRulesData.PlayerFaction.Units.Add(rootUnit);
                }
            }
            Army army = new Army(
                "Player Chapter",
                null,
                "Chapter Master",
                gameRulesData.PlayerFaction.Units.First(),
                gameRulesData.PlayerFaction.Units.First().GetAllMembers().Select(m => (PlayerSoldier)m));
            army.Requisition = gameState.Requisition;
            army.MedicalProcedures.AddRange(gameState.MedicalProcedures ?? new List<MedicalProcedure>());
            // Restore the fallen brothers, who belong to no unit and so are carried separately.
            foreach (PlayerSoldier fallen in gameState.FallenBrothers ?? new List<PlayerSoldier>())
            {
                army.FallenBrothers[fallen.Id] = fallen;
            }
            Fleet fleet = new Fleet(
                "Chapter Navy",
                null,
                "Chapter Master");
            fleet.TaskForces.AddRange(gameState.Fleets.Where(f => f.Faction.Id == gameRulesData.PlayerFaction.Id));
            PlayerForce playerForce = new PlayerForce(
                gameRulesData.PlayerFaction,
                army,
                fleet);
            playerForce.GeneseedStockpile = (ushort)gameState.GeneseedStockpile;
            playerForce.GeneseedPurity = gameState.GeneseedPurity;
            Sector sector = new Sector(playerForce, gameState.Characters, gameState.Planets, gameState.Fleets);
            // Reattach the Opening Scenario state (null for legacy/sandbox saves), which rides on the
            // GlobalData row rather than being derived (Design/OpeningScenario.md §7).
            sector.Scenario = gameState.Scenario;

            // The data-access layer restores each Order onto its squads (Squad.CurrentOrders) but
            // never re-registers it with the Sector, whose Orders collection is authoritative for
            // turn processing (TurnController reads sector.Orders.Values) and the region/planet
            // "inbound orders" views. Rebuild it here from the loaded player squads - the exact
            // inverse of SaveData, which persists the distinct CurrentOrders of the player's squads.
            // Without this a reloaded game processes no standing orders and shows none as inbound.
            foreach (Order order in gameState.Units
                         .Where(u => u.UnitTemplate.Faction.Id == gameRulesData.PlayerFaction.Id)
                         .SelectMany(u => u.GetAllSquads())
                         .Select(squad => squad.CurrentOrders)
                         .Where(o => o != null && o.Mission != null)
                         .Distinct())
            {
                sector.AddNewOrder(order);
            }
            return sector;
        }
    }
}
