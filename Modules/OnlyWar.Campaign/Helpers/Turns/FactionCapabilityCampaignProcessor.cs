using OnlyWar.Campaign.Simulation;
using OnlyWar.Domain;
using OnlyWar.Domain.FactionBehaviors;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Planets;
using System.Collections.Generic;

namespace OnlyWar.Campaign.Turns
{
    /// <summary>
    /// Coordinates the capability-owned campaign processors. The coordinator contains no faction
    /// identity logic; each stage resolves its participating factions from capability flags.
    /// </summary>
    internal sealed class FactionCapabilityCampaignProcessor
    {
        private readonly DormantPopulationProcessor _dormantPopulationProcessor;
        private readonly InvasionGenerationProcessor _invasionGenerationProcessor;
        private readonly StrategicInvasionLifecycleProcessor _strategicInvasionLifecycleProcessor;

        internal FactionCapabilityCampaignProcessor(ICampaignSimulationSession session)
            : this(CampaignTurnContext.From(session))
        {
        }

        internal FactionCapabilityCampaignProcessor(CampaignTurnContext turn)
        {
            StrategicInvasionLifecycleProcessor lifecycle =
                new StrategicInvasionLifecycleProcessor(turn);
            _dormantPopulationProcessor = new DormantPopulationProcessor(lifecycle);
            _invasionGenerationProcessor = new InvasionGenerationProcessor(lifecycle);
            _strategicInvasionLifecycleProcessor = lifecycle;
        }

        internal static void SeedGhostSources(Sector sector, GameRulesData rules, IRNG random) =>
            GhostPlanetSeeder.Seed(sector, rules, random);

        internal void ProcessWeeklyState(Sector sector) =>
            _dormantPopulationProcessor.ProcessWeeklyState(sector);

        internal void ProcessAttractionAndFragmentation(Sector sector) =>
            _invasionGenerationProcessor.ProcessAttractionAndFragmentation(sector);

        internal StrategicInvasionForce EstablishOpeningInvasion(
            Sector sector,
            Planet planet,
            Faction invasionFaction) =>
            _invasionGenerationProcessor.EstablishOpeningInvasion(
                sector, planet, invasionFaction);

        internal static bool StrategicCommanderCanBeReached(
            StrategicInvasionForce force,
            Region region,
            float successMargin,
            IRNG random) => StrategicInvasionLifecycleProcessor.StrategicCommanderCanBeReached(
                force, region, successMargin, random);

        internal static bool StrategicCommanderCanBeReached(
            StrategicInvasionForce force,
            Region region,
            float successMargin,
            IRNG random,
            FactionBehaviorRulesProfile rules) => StrategicInvasionLifecycleProcessor.StrategicCommanderCanBeReached(
                force, region, successMargin, random, rules);

        internal static void AttachCommandersToTacticalOrders(
            Sector sector,
            Faction faction,
            IList<Order> orders,
            IEnumerable<Order> existingOrders = null) =>
            StrategicInvasionLifecycleProcessor.AttachCommandersToTacticalOrders(
                sector, faction, orders, existingOrders);

        internal void ResolveStrategicLeaderDeaths(
            Sector sector,
            IEnumerable<StrategicCombatResult> results) =>
            _strategicInvasionLifecycleProcessor.ResolveStrategicLeaderDeaths(sector, results);

        internal void ResolveTacticalLeaderDeaths(
            Sector sector,
            IEnumerable<MissionContext> contexts) =>
            _strategicInvasionLifecycleProcessor.ResolveTacticalLeaderDeaths(sector, contexts);

        internal void AffiliateCapturedRegion(
            Sector sector,
            StrategicCombatResult result) =>
            _strategicInvasionLifecycleProcessor.AffiliateCapturedRegion(sector, result);

        internal void AffiliateTacticalCaptures(
            Sector sector,
            IEnumerable<MissionContext> contexts) =>
            _strategicInvasionLifecycleProcessor.AffiliateTacticalCaptures(sector, contexts);
    }
}

