using OnlyWar.Builders;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using System;
using System.Linq;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>
    /// Resolves campaign-scenario objectives after all other weekly simulation has settled.
    /// </summary>
    internal sealed class ScenarioTurnProcessor
    {
        private readonly GameSession _session;

        internal ScenarioTurnProcessor(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        internal bool TryResolve(Sector sector, out string notification)
        {
            notification = null;
            CampaignScenario scenario = sector.Scenario;
            if (scenario is not { State: ObjectiveState.Pending })
            {
                return false;
            }

            GameRulesData data = _session.Rules;
            Planet promised = sector.GetPlanet(scenario.PromisedPlanetId);
            Faction player = sector.PlayerForce.Faction;
            Faction imperial = data.DefaultFaction;

            if (!HasEnemyPresence(promised))
            {
                scenario.State = ObjectiveState.Won;
                SectorBuilder.ReplaceChapterPlanetFaction(promised, player);
                Character lord = sector.GetSectorLord();
                if (lord != null)
                {
                    lord.OpinionOfPlayerForce += ScenarioRules.SectorLordOpinionReward;
                }

                notification =
                    $"[b]The Promised World is liberated.[/b]\n\n{promised.Name} is cleansed of the "
                    + "enemy — swarm and cult alike — and granted to your Chapter. It is your home "
                    + "now — hold it in the Emperor's name.";
                return true;
            }

            bool imperialRemains = HasPresence(promised, imperial.Id);
            bool playerRemains = HasPresence(promised, player.Id);
            if (imperialRemains || playerRemains)
            {
                return false;
            }

            scenario.State = ObjectiveState.Lapsed;
            Character sectorLord = sector.GetSectorLord();
            if (sectorLord != null)
            {
                sectorLord.OpinionOfPlayerForce -= ScenarioRules.SectorLordOpinionPenalty;
            }

            notification =
                $"[b]The Promised World is lost.[/b]\n\n{promised.Name} has fallen — no Imperial "
                + "or Astartes presence remains to hold it. The promise is withdrawn, and your "
                + "standing with the Sector Lord suffers for it. The war goes on.";
            return true;
        }

        private static bool HasPresence(Planet planet, int factionId)
        {
            return planet.Regions.Any(region =>
                region.RegionFactionMap.TryGetValue(factionId, out RegionFaction regionFaction)
                && (regionFaction.Population > 0 || regionFaction.Garrison > 0));
        }

        private static bool HasEnemyPresence(Planet planet)
        {
            return planet.Regions.Any(region =>
                region.RegionFactionMap.Values.Any(regionFaction =>
                    !regionFaction.PlanetFaction.Faction.IsDefaultFaction
                    && !regionFaction.PlanetFaction.Faction.IsPlayerFaction
                    && (regionFaction.Population > 0 || regionFaction.Garrison > 0)));
        }
    }
}
