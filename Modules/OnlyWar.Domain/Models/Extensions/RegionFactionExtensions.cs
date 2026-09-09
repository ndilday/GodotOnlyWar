using OnlyWar.Domain;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Squads;
using System.Linq;

namespace OnlyWar.Domain.Extensions
{
    /// <summary>
    /// Intrinsic awareness and fielded-strength queries over the region graph. The intel-gated,
    /// player-facing descriptions that need the intelligence services stay with their owner in
    /// <c>RegionFactionDescriptionExtensions</c>.
    /// </summary>
    public static class RegionFactionExtensions
    {
        // This faction's situational awareness of its OWN region — the defensive face of the unified
        // per-(faction, region) intel value (replaces the old Detection stat). Fed by listening posts,
        // patrols, and recon; consumed by strategic combat and stealth-check difficulty. A patrol now
        // raises this directly (recon of one's own ground), so an actively-patrolled region is harder
        // to infiltrate as an emergent consequence rather than via a bolted-on penalty.
        public static float GetOwnRegionAwareness(this RegionFaction regionFaction) =>
            regionFaction.PlanetFaction.GetRegionAwareness(regionFaction.Region);

        // How well an arbitrary faction understands this region (0 if it has no presence/awareness).
        // The offensive face of the same value: what an attacker believes about a region it may hit.
        public static float GetFactionRegionAwareness(this Region region, Faction faction) =>
            region.GetFactionRegionAwareness(faction.Id);

        public static float GetFactionRegionAwareness(this Region region, int factionId) =>
            region.Planet.PlanetFactionMap.TryGetValue(factionId, out PlanetFaction planetFaction)
                ? planetFaction.GetRegionAwareness(region)
                : 0f;

        public static float GetPlayerVisibleIntel(this Region region)
        {
            if (region == null) return 0f;

            // The Chapter and the PDF share player-visible intelligence. A Chapter
            // PlanetFaction is created lazily when Marines first establish a presence on a
            // planet, so it may have no historical RegionAwareness entries even though the PDF
            // already knows the region. Do not let that empty, newly-created map mask the
            // allied intelligence that was visible before the Chapter presence existed.
            return region.Planet.PlanetFactionMap.Values
                .Where(pf => pf.Faction.IsPlayerFaction || pf.Faction.IsDefaultFaction)
                .Select(pf => pf.GetRegionAwareness(region))
                .DefaultIfEmpty(0f)
                .Max();
        }

        // Troops this faction actually has fielded and active in the region — the organized
        // portion of its fighting strength. MilitaryStrength resolves the horde-vs-civilian
        // split (Population for PopulationIsMilitary factions, Garrison otherwise). This is the
        // "patrolling + defending"
        // total: MissionStealthDifficulty splits it against GetPatrolStrength to separate the troops
        // out searching from the ones merely present, and the special-mission budget and the
        // target-guard checks (PerformSabotage/PerformAssassination) read the whole of it.
        public static long GetDeployedStrength(this RegionFaction rf) =>
            rf.OrganizedMilitaryStrength;

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
        // currency"), and the faction strategy facade proves it by seeding SpareTroops from
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
    }
}
