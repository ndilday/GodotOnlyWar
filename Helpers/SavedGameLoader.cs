using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Models;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Events;
using OnlyWar.Models.FactionBehaviors;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;

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
            // (MainGameScene enumerates units via Faction.Units) expect root units to live on their
            // owning faction. This includes persistent invasion units; registering only the
            // Chapter roots would make the first save after loading silently drop a Warboss unit.
            Dictionary<int, Faction> factionsById = gameRulesData.Factions
                .ToDictionary(faction => faction.Id);
            foreach (var rootUnit in gameState.Units)
            {
                int? ownerId = rootUnit.UnitTemplate.Faction?.Id;
                if (ownerId.HasValue
                    && factionsById.TryGetValue(ownerId.Value, out Faction owner)
                    && !owner.Units.Contains(rootUnit))
                {
                    owner.Units.Add(rootUnit);
                }
            }
            Army army = new Army(
                "Player Chapter",
                null,
                "Chapter Master",
                gameRulesData.PlayerFaction.Units.First(),
                gameRulesData.PlayerFaction.Units.First().GetAllMembers().Select(m => (PlayerSoldier)m));
            army.Requisition = gameState.Requisition;
            army.LoadoutDoctrine.ReplaceWith(gameState.ChapterLoadoutDoctrine);
            army.CharacterLoadoutDoctrine.ReplaceWith(gameState.CharacterLoadoutDoctrine);
            army.EquipmentLoadoutDoctrine.ReplaceWith(gameState.EquipmentLoadoutDoctrine);
            army.MedicalProcedures.AddRange(gameState.MedicalProcedures ?? new List<MedicalProcedure>());
            MedicalProcedureService.SynchronizeProcedureReservations(
                army.PlayerSoldierMap.Values,
                army.MedicalProcedures);
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
            playerForce.CampaignIdentity = gameState.CampaignIdentity
                ?? OnlyWar.Models.Events.CampaignIdentity.Empty;
            foreach (var @event in gameState.CampaignEventLedger?.Events ?? [])
            {
                playerForce.CampaignEventLedger.Append(@event);
            }
            foreach (var entry in gameState.ChapterChronicle?.Entries ?? [])
            {
                playerForce.ChapterChronicle.Append(entry);
            }
            playerForce.GeneseedStockpile = (ushort)gameState.GeneseedStockpile;
            playerForce.GeneseedPurity = gameState.GeneseedPurity;
            playerForce.HomeWorldPlanetId = gameState.HomeWorldPlanetId;
            playerForce.RecruitmentProgram =
                RecruitmentSaveMapper.FromSaveData(gameState.Recruitment);
            ValidateSquadLineageInvariants(playerForce);
            playerForce.LastTurnReportSnapshot = gameState.LastTurnReportSnapshot;
            playerForce.RestoreWorldControlEpisodes(gameState.WorldControlEpisodes);
            foreach (var historyDay in gameState.History ?? new Dictionary<Date, List<EventHistory>>())
            {
                foreach (EventHistory entry in historyDay.Value ?? [])
                {
                    playerForce.AddToBattleHistory(
                        historyDay.Key,
                        entry.EventTitle,
                        entry.SubEvents ?? []);
                }
            }
            playerForce.Requests.AddRange(gameState.Requests ?? []);
            playerForce.Pledges.AddRange(gameState.Pledges ?? []);
            Sector sector = new Sector(
                playerForce,
                gameState.Characters,
                gameState.Planets,
                gameState.Fleets,
                gameState.RelationshipLedger);
            RestoreFactionCapabilityState(sector, gameState, gameRulesData);
            // Reattach the Opening Scenario state (null for sandbox saves), which rides on the
            // GlobalData row rather than being derived (Design/Reference/OpeningScenario.md).
            sector.Scenario = gameState.Scenario;
            EnsureCompatibilityFoundingEvent(playerForce, sector, gameRulesData, gameState.CurrentDate);

            // Orders are independent of whether their force currently contains a squad. Rebuild
            // the sector index from the loaded Assignment rows so character-only and empty
            // continuous-task orders survive a round trip too.
            foreach (Order order in (gameState.Orders ?? [])
                         .Where(o => o != null && o.Mission != null)
                         .Distinct())
            {
                sector.AddNewOrder(order);
            }
            if (playerForce.RecruitmentProgram != null)
            {
                playerForce.RecruitmentProgram.TaskOrder = sector.Orders.Values.FirstOrDefault(order =>
                    order.Mission?.MissionType == Models.Missions.MissionType.Recruitment
                    && order.OwnerFaction == playerForce.Faction);
            }
            return sector;
        }

        private static void RestoreFactionCapabilityState(
            Sector sector,
            GameStateDataBlob gameState,
            GameRulesData gameRulesData)
        {
            foreach (GhostPopulationSource source in gameState.GhostPopulationSources ?? [])
            {
                sector.AddGhostPopulationSource(source);
            }

            Dictionary<int, Region> regions = sector.Planets.Values
                .SelectMany(planet => planet.Regions)
                .ToDictionary(region => region.Id);
            Dictionary<int, Squad> squads = gameState.Units
                .SelectMany(unit => unit.GetAllSquads())
                .ToDictionary(squad => squad.Id);
            foreach (StrategicInvasionForceSaveData saved in gameState.StrategicInvasionForces ?? [])
            {
                if (!gameRulesData.Factions.Any(faction => faction.Id == saved.FactionId))
                {
                    throw new InvalidOperationException(
                        $"Strategic invasion force {saved.Id} references missing faction {saved.FactionId}.");
                }
                if (!gameRulesData.Factions.ToDictionary(faction => faction.Id)
                    .TryGetValue(saved.FactionId, out Faction faction)
                    || !squads.TryGetValue(saved.CommandSquadId, out Squad commandSquad))
                {
                    throw new InvalidOperationException(
                        $"Strategic invasion force {saved.Id} references a missing faction or command squad.");
                }

                Region currentRegion = saved.CurrentRegionId.HasValue
                    ? regions.GetValueOrDefault(saved.CurrentRegionId.Value)
                    : null;
                Planet originPlanet = saved.OriginPlanetId.HasValue
                    ? sector.Planets.GetValueOrDefault(saved.OriginPlanetId.Value)
                    : null;
                Planet destinationPlanet = saved.DestinationPlanetId.HasValue
                    ? sector.Planets.GetValueOrDefault(saved.DestinationPlanetId.Value)
                    : null;
                StrategicInvasionForce force = new(
                    saved.Id,
                    faction,
                    commandSquad,
                    currentRegion,
                    originPlanet)
                {
                    DestinationPlanet = destinationPlanet,
                    TravelWeeksRemaining = saved.TravelWeeksRemaining,
                    TransitBattleValue = saved.TransitBattleValue,
                    IsActive = saved.IsActive
                };
                commandSquad.CurrentRegion = currentRegion;
                if (currentRegion != null
                    && currentRegion.RegionFactionMap.TryGetValue(faction.Id, out RegionFaction presence))
                {
                    // UnitDataAccess treats every seated squad as a landed squad. The strategic
                    // The strategic commander is physical but deliberately absent from LandedSquads.
                    presence.LandedSquads.Remove(commandSquad);
                }
                foreach (RegionFaction trackedPresence in regions.Values
                    .Select(region => region.RegionFactionMap.GetValueOrDefault(faction.Id))
                    .Where(item => item?.StrategicInvasionForceId == saved.Id))
                {
                    force.TrackRegion(trackedPresence);
                }
                sector.AddStrategicInvasionForce(force);
            }
        }

        private static void ValidateSquadLineageInvariants(PlayerForce force)
        {
            force.Army.PopulateSquadMap();
            List<OnlyWar.Models.Squads.Squad> squads = force.Army.OrderOfBattle
                .GetAllSquads().ToList();
            foreach (OnlyWar.Models.Squads.Squad squad in squads.Where(squad => squad.Members.Count == 0))
            {
                if (squad.CurrentOrders != null || squad.BoardedLocation != null || squad.CurrentRegion != null)
                {
                    throw new InvalidOperationException(
                        $"Save contains empty formation {squad.Id} with an active deployment.");
                }
            }
            foreach (var company in squads
                .Where(SquadDesignationFormatter.IsNumberedLineFormation)
                .GroupBy(squad => squad.ParentUnit))
            {
                if (company.Any(squad => !squad.FormationOrdinal.HasValue))
                {
                    throw new InvalidOperationException(
                        $"Save contains a numbered line formation without an ordinal in {company.Key?.Name}.");
                }
                if (company.GroupBy(squad => squad.FormationOrdinal.Value).Any(group => group.Count() > 1))
                {
                    throw new InvalidOperationException(
                        $"Save contains duplicate formation ordinals in {company.Key?.Name}.");
                }
                foreach (var squad in company)
                {
                    string canonical = SquadDesignationFormatter.Format(squad);
                    if (!string.Equals(squad.Name, canonical, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Save contains non-canonical designation '{squad.Name}' for formation {squad.Id}; expected '{canonical}'.");
                    }
                }
            }
            HashSet<int> retainedIds = squads.Select(squad => squad.Id).ToHashSet();
            if (!retainedIds.SetEquals(force.Army.SquadMap.Keys))
            {
                throw new InvalidOperationException("Save squad map does not match retained Chapter formations.");
            }
        }

        private static void EnsureCompatibilityFoundingEvent(
            PlayerForce playerForce,
            Sector sector,
            GameRulesData gameRulesData,
            Date currentDate)
        {
            if (playerForce == null
                || sector?.Scenario == null
                || playerForce.CampaignEventLedger.Events.Any(
                    @event => @event.Type == CampaignEventType.ChapterFounded))
            {
                return;
            }

            Planet promisedWorld = sector.Planets.GetValueOrDefault(
                sector.Scenario.PromisedPlanetId);
            string chapterName = playerForce.Army.OrderOfBattle?.Name
                ?? playerForce.Faction?.Name
                ?? "Chapter";
            PlayerSoldier chapterMaster = playerForce.Army.OrderOfBattle?.GetAllMembers()
                .OfType<PlayerSoldier>()
                .FirstOrDefault(soldier => soldier.Template?.Id
                    == gameRulesData.ChapterDoctrine.ChapterMaster.Id);
            Character authority = sector.Characters.FirstOrDefault(character =>
                character.Id == sector.Scenario.OriginalAuthorityCharacterId);
            int fallbackWeek = Math.Max(1, currentDate?.GetTotalWeeks() ?? 1);
            int foundingWeek = playerForce.CampaignEventLedger.Events
                .Select(@event => @event.OccurredWeek)
                .Where(week => week > 0)
                .DefaultIfEmpty(fallbackWeek)
                .Min();
            string planetName = promisedWorld?.Name
                ?? $"Planet {sector.Scenario.PromisedPlanetId}";
            string directive = string.IsNullOrWhiteSpace(sector.Scenario.BriefingText)
                ? "The Chapter's opening directive was preserved without additional briefing text."
                : sector.Scenario.BriefingText;
            int activeStrength = playerForce.Army.OrderOfBattle == null
                ? 0
                : playerForce.Army.OrderOfBattle.GetAllMembers().Count();
            ChapterFoundedPayload payload = new(
                chapterName,
                foundingWeek,
                chapterMaster?.Id,
                chapterMaster?.Name ?? "Unknown Chapter Master",
                activeStrength,
                authority?.Name ?? "The Sector Lord",
                directive,
                sector.Scenario.PromisedPlanetId,
                planetName);
            playerForce.RecordChapterFounded(
                Date.FromTotalWeeks(foundingWeek),
                payload,
                chapterMaster?.Id,
                chapterMaster?.Name,
                sector.Scenario.PromisedPlanetId,
                planetName);
        }
    }
}
