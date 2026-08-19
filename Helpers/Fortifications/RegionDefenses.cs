using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Fortifications
{
    /// <summary>
    /// The defensive works standing in a region, as the side holding it experiences them.
    /// </summary>
    /// <remarks>
    /// Entrenchments, listening posts and anti-air batteries are structures on the ground, not
    /// private faction property: when the Chapter digs a trench line in a world the PDF also holds,
    /// both man it. Storage stays per-RegionFaction because that records who <em>built</em> what -
    /// which is what construction reporting and build pricing need - but every reader asks this
    /// service for the side's position rather than reading one faction's field. Contributions are
    /// pooled through FortificationMath, so a shared position is worth what its total investment
    /// bought and never the naive sum of its parts.
    ///
    /// Only public allies pool their works. A faction that has gone to ground has abandoned or
    /// concealed its works (RegionFaction.HalveDefensesOnGoingToGround, then
    /// RegionControlTurnProcessor.DecayUnmannedDefenses grinds down the remainder) precisely because
    /// nobody is manning them, so they cannot fortify the ally still standing in the open.
    /// </remarks>
    public static class RegionDefenses
    {
        /// <summary>
        /// The level of the given works as they stand for this faction's side - its own
        /// contribution pooled with those of every public ally in the region.
        /// </summary>
        public static double GetShared(RegionFaction regionFaction, DefenseType defenseType)
        {
            if (regionFaction?.Region == null) return regionFaction?.GetDefense(defenseType) ?? 0.0;

            double points = 0.0;
            foreach (RegionFaction contributor in GetContributors(regionFaction))
            {
                points += FortificationMath.LevelToPoints(contributor.GetDefense(defenseType));
            }
            return FortificationMath.PointsToLevel(points);
        }

        /// <summary>
        /// The shared works an arbitrary faction would find in a region, for callers that hold a
        /// region and a faction rather than a RegionFaction (0 when it has no presence there).
        /// </summary>
        public static double GetSharedFor(Region region, Faction faction, DefenseType defenseType)
        {
            if (region == null || faction == null) return 0.0;
            return region.RegionFactionMap.TryGetValue(faction.Id, out RegionFaction regionFaction)
                ? GetShared(regionFaction, defenseType)
                : 0.0;
        }

        /// <summary>
        /// Records construction against the building faction's own stock. Effort arrives as
        /// construction points, so a fixed weekly output climbs fast at first and then flattens -
        /// the same diminishing return the AI pays for through its escalating build cost.
        /// </summary>
        public static void Build(RegionFaction builder, DefenseType defenseType, double points)
        {
            if (builder == null || points <= 0.0) return;
            builder.SetDefense(
                defenseType,
                FortificationMath.AddPoints(builder.GetDefense(defenseType), points));
        }

        /// <summary>
        /// Wrecks <paramref name="levelReduction"/> off the side's shared position, spreading the
        /// loss across contributors in proportion to what each has invested. Sabotage strikes the
        /// works that are actually there, so a raid on a position the player built most of takes
        /// most of it out of the player's stock - not out of whichever ally happened to be the
        /// mission's nominal target.
        /// </summary>
        /// <returns>
        /// The level actually knocked off the shared position, which is what the mission report
        /// tells the player it achieved. It is less than <paramref name="levelReduction"/> whenever
        /// the works were already thinner than the charges laid on them - reporting the requested
        /// figure would credit a raid for wrecking defenses that were never there.
        /// </returns>
        public static double Damage(RegionFaction target, DefenseType defenseType, double levelReduction)
        {
            if (target == null || levelReduction <= 0.0) return 0.0;

            List<RegionFaction> contributors = GetContributors(target)
                .Where(rf => rf.GetDefense(defenseType) > 0.0)
                .ToList();
            if (contributors.Count == 0) return 0.0;

            double sharedBefore = GetShared(target, defenseType);
            double sharedAfter = Math.Max(0.0, sharedBefore - levelReduction);
            double pointsBefore = FortificationMath.LevelToPoints(sharedBefore);
            double pointsAfter = FortificationMath.LevelToPoints(sharedAfter);
            double pointsLost = pointsBefore - pointsAfter;
            if (pointsLost <= 0.0) return 0.0;

            double survivingShare = pointsBefore <= 0.0 ? 0.0 : pointsAfter / pointsBefore;
            foreach (RegionFaction contributor in contributors)
            {
                double ownPoints = FortificationMath.LevelToPoints(contributor.GetDefense(defenseType));
                contributor.SetDefense(
                    defenseType,
                    FortificationMath.PointsToLevel(ownPoints * survivingShare));
            }
            return sharedBefore - sharedAfter;
        }

        /// <summary>
        /// Hands a departing faction's works to an ally that still holds the region, and reports
        /// which ally took them (null when none could, leaving the works to be abandoned by the
        /// caller's usual path).
        /// </summary>
        /// <remarks>
        /// Custody changes; the position does not. Because the merge happens in point space the
        /// side's shared level is identical before and after - which is the whole reason this is
        /// safe to do automatically. Level 2 works handed to a level 1 ally leave that ally at
        /// 2.34, exactly the shared level the two of them already had.
        /// </remarks>
        public static RegionFaction TransferToAlly(RegionFaction departing)
        {
            if (departing == null || !HasAnyWorks(departing)) return null;

            RegionFaction inheritor = FindInheritor(departing);
            if (inheritor == null) return null;

            foreach (DefenseType defenseType in BuildableDefenses)
            {
                double moved = departing.GetDefense(defenseType);
                if (moved <= 0.0) continue;

                inheritor.SetDefense(
                    defenseType,
                    FortificationMath.Combine(inheritor.GetDefense(defenseType), moved));
                departing.SetDefense(defenseType, 0.0);
            }
            return inheritor;
        }

        /// <summary>
        /// The public ally best placed to take over a departing faction's works: the faction
        /// holding the region if it is one, else the strongest ally standing in it. Requires a real
        /// presence - works nobody can man are abandoned, not inherited.
        /// </summary>
        public static RegionFaction FindInheritor(RegionFaction departing)
        {
            if (departing?.Region == null) return null;

            List<RegionFaction> candidates = departing.Region.RegionFactionMap.Values
                .Where(rf => !ReferenceEquals(rf, departing)
                    && rf.IsPublic
                    && FactionRelationshipService.AreAllied(
                        rf.PlanetFaction.Faction,
                        departing.PlanetFaction.Faction,
                        departing.Region.Planet)
                    && HasPresence(rf))
                .ToList();
            if (candidates.Count == 0) return null;

            RegionFaction controller = departing.Region.ControllingFaction;
            RegionFaction controllingAlly = candidates
                .FirstOrDefault(rf => ReferenceEquals(rf, controller));
            return controllingAlly ?? candidates
                .OrderByDescending(rf => rf.MilitaryStrength)
                .ThenByDescending(rf => rf.LandedSquads.Count)
                .First();
        }

        /// <summary>
        /// The construction points public allies (not this faction) hold in the region. Lets a
        /// planner project its own builds against the side's position without re-walking the region
        /// on every iteration.
        /// </summary>
        public static double GetAlliedPoints(RegionFaction regionFaction, DefenseType defenseType)
        {
            if (regionFaction?.Region == null) return 0.0;

            double points = 0.0;
            foreach (RegionFaction contributor in GetContributors(regionFaction))
            {
                if (ReferenceEquals(contributor, regionFaction)) continue;
                points += FortificationMath.LevelToPoints(contributor.GetDefense(defenseType));
            }
            return points;
        }

        public static bool HasAnyWorks(RegionFaction regionFaction) =>
            regionFaction != null && BuildableDefenses.Any(t => regionFaction.GetDefense(t) > 0.0);

        // Troops that could actually man the works: a garrison, or squads on the ground. A bare
        // civilian population is not a presence for this purpose.
        private static bool HasPresence(RegionFaction regionFaction) =>
            regionFaction.MilitaryStrength > 0 || regionFaction.LandedSquads.Count > 0;

        private static IEnumerable<RegionFaction> GetContributors(RegionFaction regionFaction)
        {
            // A faction that has gone to ground contributes nothing to the shared position, but it
            // still reads its own works when it is the one asking - it knows what it left behind.
            yield return regionFaction;

            foreach (RegionFaction other in regionFaction.Region.RegionFactionMap.Values)
            {
                if (ReferenceEquals(other, regionFaction)) continue;
                if (!other.IsPublic) continue;
                if (!FactionRelationshipService.AreAllied(
                    other.PlanetFaction.Faction,
                    regionFaction.PlanetFaction.Faction,
                    regionFaction.Region.Planet))
                {
                    continue;
                }
                yield return other;
            }
        }

        internal static readonly DefenseType[] BuildableDefenses =
        [
            DefenseType.Entrenchment,
            DefenseType.ListeningPost,
            DefenseType.AntiAir
        ];
    }
}
