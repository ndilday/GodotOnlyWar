using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnlyWar.Helpers.Extensions
{
    public static class RegionFactionExtensions
    {
        // This faction's situational awareness of its OWN region — the defensive face of the unified
        // per-(faction, region) intel value (replaces the old Detection stat). Fed by listening posts,
        // patrols, and recon; consumed by strategic combat and stealth-check difficulty. A patrol now
        // raises this directly (recon of one's own ground), so an actively-patrolled region is harder
        // to infiltrate as an emergent consequence rather than via a bolted-on penalty.
        public static float GetOwnRegionIntel(this RegionFaction regionFaction) =>
            regionFaction.PlanetFaction.GetRegionIntel(regionFaction.Region);

        // How well an arbitrary faction understands this region (0 if it has no presence/awareness).
        // The offensive face of the same value: what an attacker believes about a region it may hit.
        public static float GetFactionRegionIntel(this Region region, Faction faction) =>
            region.GetFactionRegionIntel(faction.Id);

        public static float GetFactionRegionIntel(this Region region, int factionId) =>
            region.Planet.PlanetFactionMap.TryGetValue(factionId, out PlanetFaction planetFaction)
                ? planetFaction.GetRegionIntel(region)
                : 0f;

        public static float GetPlayerVisibleIntel(this Region region)
        {
            if (region == null) return 0f;

            PlanetFaction playerFaction = region.Planet.PlanetFactionMap.Values
                .FirstOrDefault(pf => pf.Faction.IsPlayerFaction);
            if (playerFaction != null)
            {
                return playerFaction.GetRegionIntel(region);
            }

            PlanetFaction defaultFaction = region.Planet.PlanetFactionMap.Values
                .FirstOrDefault(pf => pf.Faction.IsDefaultFaction);
            return defaultFaction?.GetRegionIntel(region) ?? 0f;
        }

        public static string GetPopulationDescription(this RegionFaction regionFaction)
        {
            if (regionFaction != null && regionFaction.IsPublic)
            {
                float intel = regionFaction.Region.GetPlayerVisibleIntel();
                if (intel <= 0)
                {
                    return "Unknown";
                }
                else if (intel >= 6)
                {
                    return regionFaction.Population.ToString();
                }
                else
                {
                    int divisor = (int)Math.Pow(10, 6 - (int)intel);
                    long popCount = regionFaction.Population / divisor * divisor;
                    if (popCount > 0)
                    {
                        return popCount.ToString();
                    }
                    else if (regionFaction.Population > 0)
                    {
                        return "Low";
                    }
                }
            }
            return "None";
        }

        // Fuzzy, fog-of-war-friendly description of a defensive value (Entrenchment,
        // Detection, Anti-Air). Shared by the planet-tactical and region screens so enemy
        // defenses read consistently and never expose the raw value to the player.
        // Stats are fractional; rounding to the nearest whole level keeps the old int bands.
        public static string GetDefenseLevelDescription(double level)
        {
            switch ((int)Math.Round(level))
            {
                case <= 0:
                    return "None";
                case 1:
                case 2:
                    return "Minimal";
                case 3:
                case 4:
                    return "Mediocre";
                case 5:
                case 6:
                    return "Moderate";
                case 7:
                case 8:
                    return "Heavy";
                default:
                    return "Massive";
            }
        }

        // Troops this faction actually has fielded and active in the region — the fielded
        // portion of its fighting strength. MilitaryStrength resolves the horde-vs-civilian
        // split (Population for PopulationIsMilitary factions, Garrison otherwise); Organization
        // (0-100%) trims it to the share that can be deployed. This is the "patrolling + defending"
        // total: MissionStealthDifficulty splits it against GetPatrolStrength to separate the troops
        // out searching from the ones merely present, and the special-mission budget and the
        // target-guard checks (PerformSabotage/PerformAssassination) read the whole of it.
        public static long GetDeployedStrength(this RegionFaction rf) =>
            (long)(rf.MilitaryStrength * rf.Organization / 100.0f);

        // The share of this faction's fielded troops that is actually out LOOKING for something, as
        // opposed to merely being present. Only Patrol and Recon count: those are the two orders whose
        // whole content is "cover ground and report what you find", so they are the only ones that
        // turn bodies into search coverage.
        //
        // Every other mission type counts as static, and that is the rule doing the work here. A squad
        // fortifying, assaulting, escorting, or sitting on an objective is occupied by that mission -
        // it is an obstacle in the region, not a sweep of it, and an intruder can plan around a force
        // whose attention is committed elsewhere. Without that distinction "how hard is this region to
        // cross" collapses back into "how many armed people are in it", which is exactly the headcount
        // model MissionStealthDifficulty exists to replace: a hive fleet mid-assault on a neighbouring
        // region would be as hard to slip past as one actively hunting for intruders.
        //
        // Counted in BATTLE VALUE, not headcount, because that is the currency GetDeployedStrength is
        // denominated in and the two are subtracted from each other. Garrison and Population are BV
        // pools (RegionFaction.AddMilitaryStrength: "forces are raised, lost, and returned in the same
        // currency"), and FactionStrategyController proves it by seeding SpareTroops from
        // GetDeployedStrength and then decrementing that same variable by SquadBattleValue. Summing
        // Members.Count here instead would subtract headcount from battle value: the ambient remainder
        // would be under-subtracted, and - far worse - the patrol term would be computed on a number
        // one to two orders of magnitude smaller than the scale it was calibrated against, quietly
        // costing patrols most of the difficulty they are supposed to contribute.
        //
        // Using BV rather than bodies also reads correctly on its own terms: a patrol's worth as a
        // search is not just how many pairs of eyes it has but how well equipped and trained they are
        // to use them, which is most of what BV already measures.
        public static long GetPatrolStrength(this RegionFaction rf)
        {
            if (rf?.LandedSquads == null) return 0L;
            long total = 0L;
            foreach (Squad squad in rf.LandedSquads)
            {
                Mission mission = squad?.CurrentOrders?.Mission;
                if (mission == null) continue;
                if (mission.MissionType == MissionType.Patrol
                    || mission.MissionType == MissionType.Recon)
                {
                    total += squad.Members.Sum(member => (long)member.Template.BattleValue);
                }
            }
            return total;
        }

        // Strength magnitude expressed as an order-of-magnitude word, intel-gated to match
        // fog-of-war disclosure. Lower intel yields coarser estimates (same as GetPopulationDescription).
        public static string GetForceMagnitudeDescription(this RegionFaction regionFaction)
        {
            if (regionFaction != null && regionFaction.IsPublic)
            {
                float intel = regionFaction.Region.GetPlayerVisibleIntel();
                if (intel <= 0)
                {
                    return "Unknown";
                }

                long deployedStrength = regionFaction.GetDeployedStrength();

                if (intel >= 6)
                {
                    // Exact value available
                    return GetMagnitudeWord(deployedStrength);
                }
                else
                {
                    // Round to nearest order of magnitude based on intel level
                    int divisor = (int)Math.Pow(10, 6 - (int)intel);
                    long roundedStrength = deployedStrength / divisor * divisor;
                    if (roundedStrength == 0 && deployedStrength > 0)
                    {
                        return "Handful";  // Very small non-zero force below rounding threshold
                    }
                    return GetMagnitudeWord(roundedStrength);
                }
            }
            return "None";
        }

        // Maps a deployed strength value to a rough order-of-magnitude word.
        private static string GetMagnitudeWord(long strength)
        {
            if (strength <= 0)
                return "None";
            if (strength < 10)
                return "Handful";
            if (strength < 100)
                return "Dozens";
            if (strength < 1000)
                return "Hundreds";
            if (strength < 1000000)
                return "Thousands";
            if (strength < 1000000000)
                return "Millions";
            return "Billions";
        }
    }
}
