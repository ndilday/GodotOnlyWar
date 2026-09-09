using OnlyWar.Generation.World;
using OnlyWar.Persistence.Database.GameState;
using OnlyWar.Domain;
using OnlyWar.Campaign.Simulation;
using OnlyWar.Domain;
using OnlyWar.Operations.Abstractions;
using OnlyWar.Runtime.Allocators;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Application.Storage
{
    /// <summary>
    /// Reconstructs a campaign from a selected save and publishes it only after the complete
    /// load has succeeded. Both the title screen and the in-campaign Load action use this path,
    /// so choosing a file never falls back to an implicit "newest save" policy.
    /// </summary>
    public sealed class CampaignLoader
    {
        private readonly GameStorage _storage;
        private readonly GameStateDataAccess _dataAccess;

        public CampaignLoader(GameStorage storage, GameStateDataAccess dataAccess)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _dataAccess = dataAccess ?? throw new ArgumentNullException(nameof(dataAccess));
        }

        /// <summary>
        /// Reconstructs a detached session. Callers decide whether and when the fully validated
        /// result becomes active.
        /// </summary>
        public GameSession LoadSession(
            string savePath,
            IRNG random,
            IOrderCommitmentSurface commitments)
        {
            if (string.IsNullOrWhiteSpace(savePath))
            {
                throw new ArgumentException("A save file must be selected.", nameof(savePath));
            }
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (commitments == null) throw new ArgumentNullException(nameof(commitments));

            GameRulesData gameRulesData = OnlyWar.Persistence.Database.GameRules.GameRulesLoader.Load(
                _storage.RulesDatabasePath);
            GameStateDataBlob gameState = LoadGameData(gameRulesData, savePath);
            Sector sector = SavedGameLoader.BuildSectorFromBlob(
                gameState, gameRulesData, commitments);

            // Subsectors and warp lanes are derived deterministically from planet positions
            // rather than persisted, so rebuild them before returning the detached session.
            OnlyWar.Runtime.WorldGeometry.SectorTopologyBuilder.Rebuild(sector, gameRulesData);
            PersistentIdAllocator identity = new(
                nextSoldierId: NextIdAfter(gameState.HighestSoldierId),
                nextRequestId: NextIdAfter(gameState.HighestRequestId),
                nextMissionId: gameState.NextMissionId,
                nextOrderId: gameState.NextOrderId,
                nextCharacterId: NextIdAfter(gameState.Characters?.Select(character => character.Id)),
                nextPlanetId: NextIdAfter(gameState.Planets?.Select(planet => planet.Id)),
                nextUnitId: NextIdAfter(gameState.Units?.Select(unit => unit.Id)),
                nextSquadId: NextIdAfter(gameState.Units?
                    .SelectMany(unit => unit.GetAllSquads() ?? [])
                    .Select(squad => squad.Id)),
                nextTaskForceId: NextIdAfter(gameState.Fleets?.Select(fleet => fleet.Id)));
            GameSession session = new(gameRulesData, sector, gameState.CurrentDate, random, identity)
            {
                UpgradePending = gameState.UpgradePending
            };
            return session;
        }

        private static int NextIdAfter(int highestId) =>
            highestId == int.MaxValue
                ? throw new InvalidOperationException("Persistent ID range is exhausted.")
                : highestId + 1;

        private static int NextIdAfter(IEnumerable<int> ids) =>
            NextIdAfter(ids?.DefaultIfEmpty(-1).Max() ?? -1);

        private GameStateDataBlob LoadGameData(
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

            return _dataAccess.GetData(
                savePath,
                gameRulesData.Factions.ToDictionary(faction => faction.Id),
                gameRulesData.PlanetTemplateMap,
                shipTemplateMap,
                unitTemplateMap,
                squadTemplateMap,
                gameRulesData.WeaponSets,
                hitLocationMap,
                gameRulesData.BaseSkillMap,
                soldierTemplateMap,
                gameRulesData.EquipmentTemplates,
                gameRulesData.EquipmentKits,
                gameRulesData.ScoutTrainingOptions);
        }
    }
}
