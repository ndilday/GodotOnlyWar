using OnlyWar.Models;
using OnlyWar.Models.Planets;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>
    /// Collects debug-only, region-scoped diagnostics for the promised-world scenario.
    /// The active collection is process-wide to preserve the former TurnController semantics:
    /// planet and mission resolution can record metrics without carrying a turn object through
    /// every simulation helper.
    /// </summary>
    internal static class ScenarioMetricsCollector
    {
        private static Dictionary<Region, ScenarioRegionTurnMetrics> _activeScenarioRegionMetrics;
        private static Planet _activeScenarioMetricsPlanet;
        private static Faction _activeScenarioMetricsImperialFaction;

        private sealed class ScenarioRegionTurnMetrics
        {
            public long NaturalPopulationChange { get; set; }
            public long ImmigrationNet { get; set; }
            public long CiviliansKilledByEnemyForces { get; set; }
            public long PdfLost { get; set; }
            public long PdfDrafted { get; set; }
            public long Blighting { get; set; }
        }

        internal static Planet GetScenarioMetricsPlanet(Sector sector)
        {
            CampaignScenario scenario = sector?.Scenario;
            if (scenario?.Type != ScenarioType.PromisedWorld)
            {
                return null;
            }

            return sector.Planets.TryGetValue(scenario.PromisedPlanetId, out Planet planet)
                ? planet
                : null;
        }

        internal static void BeginScenarioRegionMetrics(Planet planet, Faction imperialFaction)
        {
            EndScenarioRegionMetrics();
            if (!GameLog.IsEnabled(GameLogLevel.Debug) || planet == null || imperialFaction == null)
            {
                return;
            }

            _activeScenarioMetricsPlanet = planet;
            _activeScenarioMetricsImperialFaction = imperialFaction;
            _activeScenarioRegionMetrics = planet.Regions.ToDictionary(
                region => region,
                _ => new ScenarioRegionTurnMetrics());
        }

        internal static void EndScenarioRegionMetrics()
        {
            _activeScenarioRegionMetrics = null;
            _activeScenarioMetricsPlanet = null;
            _activeScenarioMetricsImperialFaction = null;
        }

        internal static void RecordScenarioNaturalPopulationChange(RegionFaction regionFaction, long delta)
        {
            if (!IsTrackedImperialFaction(regionFaction)) return;
            ScenarioRegionTurnMetrics metrics = GetActiveScenarioMetrics(regionFaction.Region);
            if (metrics == null) return;
            metrics.NaturalPopulationChange += delta;
        }

        internal static void RecordScenarioImmigration(Region region, long delta)
        {
            ScenarioRegionTurnMetrics metrics = GetActiveScenarioMetrics(region);
            if (metrics == null) return;
            metrics.ImmigrationNet += delta;
        }

        internal static void RecordScenarioCivilianKills(RegionFaction regionFaction, long killed, Faction attacker)
        {
            if (killed <= 0 || !IsEnemyFaction(attacker) || !IsTrackedImperialFaction(regionFaction)) return;
            ScenarioRegionTurnMetrics metrics = GetActiveScenarioMetrics(regionFaction.Region);
            if (metrics == null) return;
            metrics.CiviliansKilledByEnemyForces += killed;
        }

        internal static void RecordScenarioPdfLost(RegionFaction regionFaction, long lost, Faction attacker)
        {
            if (lost <= 0 || !IsEnemyFaction(attacker) || !IsTrackedImperialFaction(regionFaction)) return;
            ScenarioRegionTurnMetrics metrics = GetActiveScenarioMetrics(regionFaction.Region);
            if (metrics == null) return;
            metrics.PdfLost += lost;
        }

        internal static void RecordScenarioPdfDrafted(RegionFaction regionFaction, long drafted)
        {
            if (drafted <= 0 || !IsTrackedImperialFaction(regionFaction)) return;
            ScenarioRegionTurnMetrics metrics = GetActiveScenarioMetrics(regionFaction.Region);
            if (metrics == null) return;
            metrics.PdfDrafted += drafted;
        }

        internal static void RecordScenarioBlighting(Region region, long stripped, Faction consumer)
        {
            if (stripped <= 0 || !IsEnemyFaction(consumer)) return;
            ScenarioRegionTurnMetrics metrics = GetActiveScenarioMetrics(region);
            if (metrics == null) return;
            metrics.Blighting += stripped;
        }

        internal static void LogScenarioRegionMetrics(string turnLabel)
        {
            if (_activeScenarioRegionMetrics == null || _activeScenarioMetricsPlanet == null)
            {
                return;
            }

            foreach (Region region in _activeScenarioMetricsPlanet.Regions)
            {
                ScenarioRegionTurnMetrics metrics = GetActiveScenarioMetrics(region);
                if (metrics == null) continue;

                RegionFaction imperial = region.RegionFactionMap.TryGetValue(
                    _activeScenarioMetricsImperialFaction.Id, out RegionFaction rf)
                    ? rf
                    : null;
                long population = imperial?.Population ?? 0;
                long garrison = imperial?.Garrison ?? 0;

                GameLog.Debug(() =>
                    $"Scenario region metrics {turnLabel} "
                    + $"{region.Planet.Name}/{region.Name}/{_activeScenarioMetricsImperialFaction.Name}: "
                    + $"naturalPop={metrics.NaturalPopulationChange}, "
                    + $"immigrationNet={metrics.ImmigrationNet}, "
                    + $"civiliansKilledByEnemy={metrics.CiviliansKilledByEnemyForces}, "
                    + $"pdfLost={metrics.PdfLost}, pdfDrafted={metrics.PdfDrafted}, "
                    + $"blighting={metrics.Blighting}, "
                    + $"population={population}, pdf={garrison}, "
                    + $"carryingCapacity={region.CarryingCapacity}/{region.MaximumCarryingCapacity}");
            }
        }

        private static ScenarioRegionTurnMetrics GetActiveScenarioMetrics(Region region)
        {
            if (_activeScenarioRegionMetrics == null || region == null)
            {
                return null;
            }

            if (!ReferenceEquals(region.Planet, _activeScenarioMetricsPlanet))
            {
                return null;
            }

            return _activeScenarioRegionMetrics.TryGetValue(region, out ScenarioRegionTurnMetrics metrics)
                ? metrics
                : null;
        }

        private static bool IsTrackedImperialFaction(RegionFaction regionFaction)
        {
            return regionFaction?.PlanetFaction?.Faction != null
                && ReferenceEquals(regionFaction.PlanetFaction.Faction, _activeScenarioMetricsImperialFaction);
        }

        private static bool IsEnemyFaction(Faction faction)
        {
            return faction != null
                && !faction.IsPlayerFaction
                && !faction.IsDefaultFaction;
        }
    }
}
