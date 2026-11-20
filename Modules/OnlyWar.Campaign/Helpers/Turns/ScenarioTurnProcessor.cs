using OnlyWar.Campaign.Simulation;
using OnlyWar.Domain;
using OnlyWar.Domain.Planets;
using OnlyWar.Operations.Abstractions;
using System;
using System.Linq;

namespace OnlyWar.Campaign.Turns
{
    /// <summary>
    /// Resolves campaign-scenario objectives after all other weekly simulation has settled.
    /// </summary>
    internal sealed class ScenarioTurnProcessor
    {
        private readonly CampaignTurnContext _turn;
        private readonly ChapterWorldGrantService _grant;

        internal ScenarioTurnProcessor(
            ICampaignSimulationSession session,
            IOperationsPersonnelSurface personnel,
            IOrderCommitmentSurface commitments)
            : this(CampaignTurnContext.From(session), personnel, commitments)
        {
        }

        internal ScenarioTurnProcessor(
            CampaignTurnContext turn,
            IOperationsPersonnelSurface personnel,
            IOrderCommitmentSurface commitments)
        {
            _turn = turn ?? throw new ArgumentNullException(nameof(turn));
            _grant = new ChapterWorldGrantService(_turn, personnel, commitments);
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
            ScenarioProfile profile = GetProfile(scenario);

            // Both outcomes are measured by who holds ground OPENLY, not by headcount. An earlier
            // rule required every non-Imperial faction on the world to reach zero population and
            // garrison, which no campaign could ever reach: the stamp seeds the scenario infiltrator in
            // all sixteen regions using the scenario infiltrator override's strength share, an infiltrator driven
            // below the suppression threshold goes to ground with its population intact and can no
            // longer be targeted at all, and organic growth replaces it between turns. The mirror
            // Lapse is likewise based on public Imperial ground, so a civilian remnant that has gone
            // to ground no longer prevents the objective from resolving. Reading public control
            // instead makes both ends reachable and matches how the rest of the simulation decides
            // who owns a region.
            if (!ChapterWorldTests.LoyalistsHoldGround(promised))
            {
                Lapse(sector, scenario, profile);
                notification =
                    $"[b]The Promised World is lost.[/b]\n\n{promised.Name} has fallen — no Imperial "
                    + "or Astartes force holds ground there any longer. The promise is withdrawn, and "
                    + "your standing with the Sector Lord suffers for it. The war goes on.";
                return true;
            }

            if (ChapterWorldTests.IsLiberated(promised))
            {
                scenario.State = ObjectiveState.Won;
                _grant.Grant(sector, promised);
                Character lord = sector.GetSectorLord();
                if (lord != null)
                {
                    lord.OpinionOfPlayerForce += profile.SectorLordOpinionReward;
                }

                // Deliberately not "cleansed": an infiltrator that has gone to ground survives the grant,
                // and is now the player's problem on their own Chapter World.
                notification =
                    $"[b]The Promised World is liberated.[/b]\n\nNo enemy holds ground on "
                    + $"{promised.Name} any longer, and the world is granted to your Chapter. It is "
                    + "your home now — hold it in the Emperor's name.";
                return true;
            }

            return false;
        }

        /// <summary>
        /// The player gave up retaking the Promised World. It has the same result as a
        /// lapse, the Sector Lord's opinion penalty included.
        /// </summary>
        internal static bool Abandon(Sector sector, GameRulesData rules)
        {
            CampaignScenario scenario = sector?.Scenario;
            if (scenario is not { State: ObjectiveState.Pending }) return false;
            Lapse(sector, scenario, GetProfile(scenario, rules));
            return true;
        }

        private ScenarioProfile GetProfile(CampaignScenario scenario) =>
            GetProfile(scenario, _turn.Rules);

        private static ScenarioProfile GetProfile(CampaignScenario scenario, GameRulesData rules)
        {
            int primaryFactionId = scenario.InvaderFactionId != 0
                ? scenario.InvaderFactionId
                : rules.SectorFactions.Invader.Id;
            return rules.ScenarioProfiles.GetRequiredForScenario(
                ScenarioKeys.PromisedWorld, primaryFactionId);
        }

        private static void Lapse(Sector sector, CampaignScenario scenario, ScenarioProfile profile)
        {
            scenario.State = ObjectiveState.Lapsed;
            Character sectorLord = sector.GetSectorLord();
            if (sectorLord != null)
            {
                sectorLord.OpinionOfPlayerForce -= profile.SectorLordOpinionPenalty;
            }
        }
    }

    /// <summary>
    /// The two public-presence tests the Promised World and the Chapter World mandates share
    /// (PRD §4.25: judged by public presence, so a hidden remnant is never disclosed). Hidden presences never count: an infiltrator
    /// that has gone to ground is not holding the region, and a remnant in hiding is not
    /// defending it.
    /// </summary>
    public static class ChapterWorldTests
    {
        public static bool LoyalistsHoldGround(Planet planet) =>
            HasPublicPresence(planet, FactionRelationshipService.IsImperial);

        public static bool HasPublicHostilePresence(Planet planet) =>
            HasPublicPresence(planet, faction => !FactionRelationshipService.IsImperial(faction));

        // The Promised World win test: loyalists still hold ground and no enemy holds any openly.
        public static bool IsLiberated(Planet planet) =>
            LoyalistsHoldGround(planet) && !HasPublicHostilePresence(planet);

        public static bool HasPublicPresence(Planet planet, Func<Faction, bool> factionMatches)
        {
            if (planet?.Regions == null) return false;
            // A planet can have empty region slots (tests and partly built sectors).
            return planet.Regions.Any(region =>
                region != null && region.RegionFactionMap.Values.Any(regionFaction =>
                    regionFaction.IsPublic
                    && regionFaction.PlanetFaction?.Faction != null
                    && factionMatches(regionFaction.PlanetFaction.Faction)
                    && (regionFaction.Population > 0 || regionFaction.Garrison > 0)));
        }
    }
}
