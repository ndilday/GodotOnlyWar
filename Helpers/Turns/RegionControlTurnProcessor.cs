using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Fortifications;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>
    /// Settles control-related regional state after growth: abandoned works, occupation decay,
    /// Imperial remnants, migration, and empty faction-presence cleanup.
    /// </summary>
    internal sealed class RegionControlTurnProcessor
    {
        private const double OccupiedDefenseDecayPerTurn = 0.25;
        private const double ImperialEmigrationRate = 0.05;

        private readonly ICollection<FortificationTransferReport> _fortificationTransfers;

        internal RegionControlTurnProcessor(
            ICollection<FortificationTransferReport> fortificationTransfers = null)
        {
            _fortificationTransfers = fortificationTransfers;
        }

        internal void SettleRegion(Region region)
        {
            TransferAbandonedWorksToAllies(region);
            DecayUnmannedDefenses(region);
            RemoveEmptyRegionFactions(region);
        }

        /// <summary>
        /// Updates every remnant's visibility before moving any population, so emigration observes
        /// the finalized state of every neighboring Imperial remnant.
        /// </summary>
        internal static void ProcessImperialRemnants(Planet planet)
        {
            foreach (Region region in planet.Regions)
            {
                UpdateImperialRemnantState(region);
            }
            foreach (Region region in planet.Regions)
            {
                ProcessImperialEmigration(region);
            }
        }

        internal static void UpdateImperialRemnantState(Region region)
        {
            RegionFaction defaultFaction = region.RegionFactionMap.Values
                .FirstOrDefault(rf => rf.PlanetFaction.Faction.IsDefaultFaction);
            if (defaultFaction == null) return;

            bool publicEnemy = HasPublicEnemy(region);
            if (defaultFaction.IsPublic)
            {
                if (defaultFaction.Garrison <= 0 && publicEnemy)
                {
                    defaultFaction.IsPublic = false;
                    if (RegionDefenses.TransferToAlly(defaultFaction) == null)
                    {
                        defaultFaction.HalveDefensesOnGoingToGround();
                    }
                }
            }
            else if (!publicEnemy || LoyalStrengthOutweighsEnemy(region))
            {
                defaultFaction.IsPublic = true;
            }
        }

        private static bool LoyalStrengthOutweighsEnemy(Region region)
        {
            long loyal = 0;
            long enemy = 0;
            foreach (RegionFaction regionFaction in region.RegionFactionMap.Values)
            {
                Faction faction = regionFaction.PlanetFaction.Faction;
                if (FactionRelationshipService.IsImperial(faction))
                {
                    loyal += regionFaction.MilitaryStrength;
                    loyal += SquadBattleValue(regionFaction.LandedSquads);
                }
                else
                {
                    enemy += regionFaction.MilitaryStrength;
                }
            }
            return loyal > 0 && loyal > enemy;
        }

        internal static void DecayUnmannedDefenses(Region region)
        {
            foreach (RegionFaction regionFaction in region.RegionFactionMap.Values)
            {
                if (regionFaction.IsPublic) continue;
                if (!RegionDefenses.HasAnyWorks(regionFaction)) continue;

                bool occupierPresent = region.RegionFactionMap.Values.Any(other =>
                    other.IsPublic
                    && other.MilitaryStrength > 0
                    && !FactionRelationshipService.AreAllied(
                        other.PlanetFaction.Faction,
                        regionFaction.PlanetFaction.Faction,
                        region.Planet));
                if (!occupierPresent) continue;

                regionFaction.Entrenchment = Math.Max(
                    0.0,
                    regionFaction.Entrenchment - OccupiedDefenseDecayPerTurn);
                regionFaction.ListeningPost = Math.Max(
                    0.0,
                    regionFaction.ListeningPost - OccupiedDefenseDecayPerTurn);
                regionFaction.AntiAir = Math.Max(
                    0.0,
                    regionFaction.AntiAir - OccupiedDefenseDecayPerTurn);
                GameLog.Trace(() =>
                    $"Occupation decay {DescribeRegionFaction(regionFaction)}: "
                    + $"ent={regionFaction.Entrenchment:F2}, lp={regionFaction.ListeningPost:F2}, "
                    + $"aa={regionFaction.AntiAir:F2}");
            }
        }

        private void TransferAbandonedWorksToAllies(Region region)
        {
            foreach (RegionFaction regionFaction in region.RegionFactionMap.Values.ToList())
            {
                if (regionFaction.MilitaryStrength > 0) continue;
                if (regionFaction.LandedSquads.Count > 0) continue;
                if (!RegionDefenses.HasAnyWorks(regionFaction)) continue;

                RegionFaction inheritor = RegionDefenses.TransferToAlly(regionFaction);
                if (inheritor == null) continue;

                _fortificationTransfers?.Add(new FortificationTransferReport(
                    region,
                    regionFaction.PlanetFaction.Faction,
                    inheritor.PlanetFaction.Faction,
                    RegionDefenses.GetShared(inheritor, DefenseType.Entrenchment)));
                GameLog.Debug(() =>
                    $"Fortifications transferred {regionFaction.PlanetFaction.Faction.Name} -> "
                    + $"{inheritor.PlanetFaction.Faction.Name} in "
                    + $"{region.Planet.Name}/{region.Name}");
            }
        }

        private static void RemoveEmptyRegionFactions(Region region)
        {
            foreach (RegionFaction regionFaction in region.RegionFactionMap.Values.ToList())
            {
                if (CanRemoveRegionFaction(regionFaction))
                {
                    region.RegionFactionMap.Remove(regionFaction.PlanetFaction.Faction.Id);
                }
            }
        }

        internal static bool CanRemoveRegionFaction(RegionFaction regionFaction)
        {
            return regionFaction.Population <= 0
                && regionFaction.Garrison <= 0
                && regionFaction.LandedSquads.Count == 0
                && !RegionDefenses.HasAnyWorks(regionFaction);
        }

        private static bool HasPublicEnemy(Region region)
        {
            return region.RegionFactionMap.Values.Any(rf =>
                rf.IsPublic
                && !FactionRelationshipService.IsImperial(rf.PlanetFaction.Faction)
                && (rf.Population > 0 || rf.Garrison > 0));
        }

        internal static void ProcessImperialEmigration(Region region)
        {
            RegionFaction remnant = region.RegionFactionMap.Values.FirstOrDefault(
                rf => rf.PlanetFaction.Faction.IsDefaultFaction && !rf.IsPublic);
            if (remnant == null || remnant.Population <= 0) return;

            List<RegionFaction> refuges = region.GetAdjacentRegions()
                .Select(r => r.RegionFactionMap.Values.FirstOrDefault(
                    rf => rf.PlanetFaction.Faction.IsDefaultFaction && rf.IsPublic))
                .Where(rf => rf != null)
                .ToList();
            if (refuges.Count == 0) return;

            long available = remnant.Population - remnant.Garrison;
            long emigrants = (long)(available * ImperialEmigrationRate);
            if (emigrants <= 0) return;
            remnant.Population -= emigrants;
            ScenarioMetricsCollector.RecordScenarioImmigration(region, -emigrants);

            long refugeTotal = refuges.Sum(rf => rf.Population);
            long distributed = 0;
            for (int i = 0; i < refuges.Count; i++)
            {
                long share = i == refuges.Count - 1
                    ? emigrants - distributed
                    : refugeTotal > 0
                        ? (long)(emigrants * (double)refuges[i].Population / refugeTotal)
                        : emigrants / refuges.Count;
                refuges[i].Population += share;
                ScenarioMetricsCollector.RecordScenarioImmigration(refuges[i].Region, share);
                distributed += share;
            }
        }

        private static long SquadBattleValue(IEnumerable<Squad> squads)
        {
            return squads.SelectMany(squad => squad.Members)
                .Sum(member => (long)member.Template.BattleValue);
        }

        private static string DescribeRegionFaction(RegionFaction regionFaction)
        {
            if (regionFaction == null) return "unknown";
            return $"{regionFaction.Region.Planet.Name}/{regionFaction.Region.Name}/"
                + $"{regionFaction.PlanetFaction.Faction.Name}";
        }
    }
}
