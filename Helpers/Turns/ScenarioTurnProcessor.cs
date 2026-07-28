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
        // Resolution reads only the promised world's own board, so nothing is kept from the
        // session; the session-shaped constructor is retained so this processor is built like
        // every other one in the turn loop.
        internal ScenarioTurnProcessor(GameSession session)
        {
            ArgumentNullException.ThrowIfNull(session);
        }

        internal bool TryResolve(Sector sector, out string notification)
        {
            notification = null;
            CampaignScenario scenario = sector.Scenario;
            if (scenario is not { State: ObjectiveState.Pending })
            {
                return false;
            }

            Planet promised = sector.GetPlanet(scenario.PromisedPlanetId);
            Faction player = sector.PlayerForce.Faction;

            // Both outcomes are measured by who holds ground OPENLY, not by headcount. An earlier
            // rule required every non-Imperial faction on the world to reach zero population and
            // garrison, which no campaign could ever reach: the stamp seeds a Genestealer Cult in
            // all sixteen regions (ScenarioRules.PromisedWorldCultStrengthFraction), a cult driven
            // below the suppression threshold goes to ground with its population intact and can no
            // longer be targeted at all, and organic growth replaces it between turns. The mirror
            // defect sat on the lapse side: the displaced Imperial remnant left by the stamp
            // (ImperialRemnantFraction) is hidden but alive, so "no Imperial presence" never held
            // either. Reading public control instead makes both ends reachable and matches how the
            // rest of the simulation decides who owns a region.
            bool loyalistsHoldGround = HasPublicPresence(promised, FactionDispositionService.IsImperial);
            if (!loyalistsHoldGround)
            {
                scenario.State = ObjectiveState.Lapsed;
                Character sectorLord = sector.GetSectorLord();
                if (sectorLord != null)
                {
                    sectorLord.OpinionOfPlayerForce -= ScenarioRules.SectorLordOpinionPenalty;
                }

                notification =
                    $"[b]The Promised World is lost.[/b]\n\n{promised.Name} has fallen — no Imperial "
                    + "or Astartes force holds ground there any longer. The promise is withdrawn, and "
                    + "your standing with the Sector Lord suffers for it. The war goes on.";
                return true;
            }

            if (!HasPublicPresence(promised, faction => !FactionDispositionService.IsImperial(faction)))
            {
                scenario.State = ObjectiveState.Won;
                SectorBuilder.ReplaceChapterPlanetFaction(promised, player);
                Character lord = sector.GetSectorLord();
                if (lord != null)
                {
                    lord.OpinionOfPlayerForce += ScenarioRules.SectorLordOpinionReward;
                }

                // Deliberately not "cleansed": a cult that has gone to ground survives the grant,
                // and is now the player's problem on their own Chapter World.
                notification =
                    $"[b]The Promised World is liberated.[/b]\n\nNo enemy holds ground on "
                    + $"{promised.Name} any longer, and the world is granted to your Chapter. It is "
                    + "your home now — hold it in the Emperor's name.";
                return true;
            }

            return false;
        }

        // Whether any faction matching the predicate openly holds ground somewhere on the world.
        // Hidden presences are excluded on purpose: an infiltrator that has gone to ground is not
        // holding the region, and a displaced remnant in hiding is not defending it.
        private static bool HasPublicPresence(Planet planet, Func<Faction, bool> factionMatches)
        {
            return planet.Regions.Any(region =>
                region.RegionFactionMap.Values.Any(regionFaction =>
                    regionFaction.IsPublic
                    && factionMatches(regionFaction.PlanetFaction.Faction)
                    && (regionFaction.Population > 0 || regionFaction.Garrison > 0)));
        }
    }
}
