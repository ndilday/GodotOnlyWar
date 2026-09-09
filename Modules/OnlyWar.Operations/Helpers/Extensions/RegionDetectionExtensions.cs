using OnlyWar.Domain.Missions;
using OnlyWar.Domain;
using OnlyWar.Domain.Planets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Operations.Extensions
{
    /// <summary>
    /// Region queries that depend on mission detection policy. Split from the intrinsic
    /// <see cref="RegionExtensions"/> (now Domain-owned) so the region graph itself does not carry a
    /// dependency on mission rules. Extension resolution is by namespace, so callers are unaffected.
    /// </summary>
    public static class RegionDetectionExtensions
    {
        // The non-player, non-default factions that could plausibly detect an intruder in this
        // region: any faction with a force fielded here (MilitaryStrength) or its own awareness of
        // the ground (RegionAwareness). A region can hold more than one at once (e.g. a public Tyranid
        // incursion sitting on a still-hidden cult), so detection must aggregate across all of them.
        // Both the aggregated stealth difficulty (ReconStealthMissionStep) and the spotter roll
        // (SelectSpotter) read this same set so the difficulty and the interceptor always agree on
        // "the enemies present" (OnlyWar_TDD.md §6.2, "Multi-faction regions").
        public static List<RegionFaction> GetDetectingEnemyFactions(this Region region)
        {
            return region.RegionFactionMap.Values
                .Where(rf => !FactionRoles.IsImperial(rf.PlanetFaction.Faction)
                             && (rf.MilitaryStrength > 0 || rf.GetOwnRegionAwareness() > 0))
                .ToList();
        }

        // Chooses which enemy faction detects an intruder (OnlyWar_TDD.md §6.2). The
        // spotter is drawn in proportion to each faction's WatchScore — the exact per-faction number
        // that MissionStealthDifficulty summed to decide the crossing was hard in the first place.
        //
        // Using the same function on both sides is the point. This used to weight by own-region intel,
        // falling back to deployed strength only when nobody had any intel, while the difficulty was
        // built from intel AND strength together. Those are two different rankings and they routinely
        // disagreed: a faction contributing almost all of the difficulty through sheer fielded
        // strength could not be the spotter at all as long as some other faction had a single point of
        // intel, so the intruder was regularly "caught" by the faction least responsible for catching
        // it — and then fought an interceptor raised from that faction's order of battle.
        //
        // Returns null only when no enemy faction is present at all (the caller then falls back to the
        // mission's target). When every faction present scores 0 — present, but neither watching nor
        // searching nor numerous enough to register — there is nothing to weight by, so the first
        // enemy stands in rather than dividing by zero.
        public static RegionFaction SelectSpotter(this Region region, IRNG random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            List<RegionFaction> enemies = region.GetDetectingEnemyFactions();
            if (enemies.Count == 0) return null;

            double totalWatch = enemies.Sum(
                rf => (double)MissionStealthDifficulty.CalculateWatchScore(rf));
            if (totalWatch <= 0) return enemies[0];
            return WeightedPick(
                enemies, rf => MissionStealthDifficulty.CalculateWatchScore(rf), totalWatch, random);
        }

        // Roulette-wheel pick over a non-empty list using the shared RNG, given a per-item weight and
        // its precomputed positive total. Falls through to the last item to absorb float rounding.
        private static RegionFaction WeightedPick(
            List<RegionFaction> factions,
            Func<RegionFaction, double> weight,
            double totalWeight,
            IRNG random)
        {
            double roll = random.GetLinearDouble() * totalWeight;
            double cumulative = 0;
            foreach (RegionFaction rf in factions)
            {
                cumulative += weight(rf);
                if (roll < cumulative) return rf;
            }
            return factions[factions.Count - 1];
        }
    }
}
