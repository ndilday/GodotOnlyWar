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
        // The factions that could plausibly detect this intruder in this region: every faction hostile
        // to it with a force fielded here (MilitaryStrength) or its own awareness of the ground
        // (RegionAwareness). A region can hold more than one at once (e.g. a public Tyranid incursion
        // sitting on a still-hidden cult), so detection must aggregate across all of them. Both the
        // aggregated stealth difficulty (MissionStealthDifficulty) and the spotter roll
        // (SelectSpotter) read this same set so the difficulty and the interceptor always agree on
        // "the enemies present" (OnlyWar_TDD.md §6.2, "Multi-faction regions").
        //
        // Hostility comes from the relationship ledger, from the intruder's side. This used to be
        // "every non-Imperial faction", which is the enemy set as the Chapter sees it and nobody else:
        // an Ork force slipping into an Imperial region was watched by any other xenos there - and by
        // its own presence - but never by the PDF or the Chapter holding the ground.
        public static List<RegionFaction> GetDetectingEnemyFactions(this Region region, Faction intruder)
        {
            return region.RegionFactionMap.Values
                .Where(rf => WatchesFor(rf.PlanetFaction.Faction, intruder, region.Planet)
                             && (rf.MilitaryStrength > 0 || rf.GetOwnRegionAwareness() > 0))
                .ToList();
        }

        // With no intruder faction to ask about (a mission force with no resolvable faction, and the
        // pure difficulty-model tests), fall back to the Chapter-side view the model was built on.
        private static bool WatchesFor(Faction watcher, Faction intruder, Planet planet)
        {
            if (watcher == null) return false;
            if (intruder == null) return !FactionRoles.IsImperial(watcher);
            if (watcher.Id == intruder.Id) return false;
            return FactionRelationshipService.AreHostile(intruder, watcher, planet);
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
        // Returns null only when no faction hostile to the intruder is present at all, in which case
        // nobody was there to see it. When every faction present scores 0 — present, but neither
        // watching nor searching nor numerous enough to register — there is nothing to weight by, so
        // the first enemy stands in rather than dividing by zero.
        public static RegionFaction SelectSpotter(this Region region, Faction intruder, IRNG random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            List<RegionFaction> enemies = region.GetDetectingEnemyFactions(intruder);
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
