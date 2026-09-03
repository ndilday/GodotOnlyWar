using OnlyWar.Helpers.Simulation;
using OnlyWar.Models;
using OnlyWar.Models.FactionBehaviors;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orks;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using System.Collections.Generic;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>
    /// Compatibility facade for legacy Ork-focused tests and save migrations. The live campaign
    /// uses FactionCapabilityCampaignProcessor; this type contains no Ork-specific lifecycle.
    /// </summary>
    [System.Obsolete("Use FactionCapabilityCampaignProcessor.")]
    internal sealed class OrkCampaignProcessor
    {
        private readonly FactionCapabilityCampaignProcessor _inner;

        internal OrkCampaignProcessor(GameSession session)
        {
            _inner = new FactionCapabilityCampaignProcessor(session);
        }

        internal static void SeedGhostSources(Sector sector, GameRulesData rules, IRNG random) =>
            FactionCapabilityCampaignProcessor.SeedGhostSources(sector, rules, random);

        internal void ProcessWeeklyState(Sector sector) => _inner.ProcessWeeklyState(sector);

        internal void ProcessAttractionAndFragmentation(Sector sector) =>
            _inner.ProcessAttractionAndFragmentation(sector);

        internal OrkWaaagh EstablishOpeningWaaagh(Sector sector, Planet planet, Faction faction)
        {
            StrategicInvasionForce force = _inner.EstablishOpeningInvasion(sector, planet, faction);
            if (force == null) return null;
            if (force is OrkWaaagh legacy) return legacy;

            OrkWaaagh compatibility = new(
                force.Id,
                force.Faction,
                force.CommandSquad,
                force.CurrentRegion,
                force.OriginPlanet)
            {
                DestinationPlanet = force.DestinationPlanet,
                TravelWeeksRemaining = force.TravelWeeksRemaining,
                TransitBattleValue = force.TransitBattleValue,
                IsActive = force.IsActive
            };
            foreach (RegionFaction presence in force.KnownRegions)
            {
                compatibility.TrackRegion(presence);
            }
            sector.RemoveStrategicInvasionForce(force);
            sector.AddOrkWaaagh(compatibility);
            return compatibility;
        }

        internal static bool WarbossCanBeReached(
            OrkWaaagh force,
            Region region,
            float successMargin,
            IRNG random) => FactionCapabilityCampaignProcessor.StrategicCommanderCanBeReached(
                force, region, successMargin, random);

        internal static bool WarbossCanBeReached(
            OrkWaaagh force,
            Region region,
            float successMargin,
            IRNG random,
            OrkCampaignRulesProfile rules) => FactionCapabilityCampaignProcessor.StrategicCommanderCanBeReached(
                force, region, successMargin, random, rules);

        internal static void AttachWarbossesToTacticalOrders(
            Sector sector,
            Faction faction,
            IList<Order> orders,
            IEnumerable<Order> existingOrders = null) =>
            FactionCapabilityCampaignProcessor.AttachCommandersToTacticalOrders(
                sector, faction, orders, existingOrders);

        internal void ResolveStrategicLeaderDeaths(
            Sector sector,
            IEnumerable<StrategicCombatResult> results) =>
            _inner.ResolveStrategicLeaderDeaths(sector, results);

        internal void ResolveTacticalLeaderDeaths(
            Sector sector,
            IEnumerable<MissionContext> contexts) =>
            _inner.ResolveTacticalLeaderDeaths(sector, contexts);

        internal void AffiliateCapturedRegion(
            Sector sector,
            StrategicCombatResult result) =>
            _inner.AffiliateCapturedRegion(sector, result);

        internal void AffiliateTacticalCaptures(
            Sector sector,
            IEnumerable<MissionContext> contexts) =>
            _inner.AffiliateTacticalCaptures(sector, contexts);
    }
}
