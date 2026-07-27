using OnlyWar.Helpers.Extensions;
using OnlyWar.Models.Missions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    // Pure string-building for the end-of-turn construction entry, in the same second-person
    // "Your forces ..." voice as MissionReportSummaryBuilder and for the same reason: the
    // EndOfTurnDialogController that consumes it is a Godot partial class and can't be exercised
    // headlessly.
    //
    // Construction is the one player order with no completion event of its own - it just adds a
    // fractional amount to a RegionFaction defense stat every week, forever - and the region
    // dossier only ever shows the bucketed description ("None", "Minimal", ...). A squad whose
    // weekly output is well under a bucket therefore looks like it is doing nothing at all, which
    // is what issue #5 ("Do Constructions Ever Finish?") is reporting. So this builder deliberately
    // reports the raw levels and the projected weeks to the next visible rating rather than a
    // pass/fail outcome.
    public static class ConstructionReportBuilder
    {
        // The levels at which GetDefenseLevelDescription actually changes what it prints, which is
        // what the projection has to aim at: promising a rating the region already shows - or
        // still showing the old rating a week after the report said it would change - is worse
        // than not projecting at all.
        //
        // The description rounds, so it flips just ABOVE each midpoint (0.5 -> "Minimal",
        // 2.5 -> "Mediocre", ...), not at the band-start integers. Math.Round is banker's rounding,
        // so an exact midpoint falls to the lower band; Epsilon clears it.
        private static readonly double[] RatingThresholds = [0.5, 2.5, 4.5, 6.5, 8.5];
        private const double Epsilon = 1e-6;

        // Below this much movement per week, "about N weeks" stops being useful information.
        private const double MinimumProjectableRate = 0.01;

        public static string BuildTitle() => "Construction";

        public static string BuildSubject() => "Your forces";

        /// <param name="sharedLevelNow">
        /// The side's position as it stands when the report is READ, not when the squad's work was
        /// applied. Construction resolves early in the turn; allied building, decay, sabotage and
        /// handovers all land afterwards. Reading the live value here is what stops the report
        /// projecting "two weeks to Mediocre" for a region that already reads Mediocre by the time
        /// the player opens it.
        /// </param>
        public static string BuildOutcomeStatus(
            ConstructionProgressReport report, double sharedLevelNow)
        {
            if (report.AmountBuilt <= 0) return "NO PROGRESS";
            return RatingChanged(report, sharedLevelNow)
                ? "FORTIFICATIONS IMPROVED"
                : "WORK IN PROGRESS";
        }

        public static string BuildSubtitle(ConstructionProgressReport report, string location)
        {
            location = Location(location);
            List<string> squadNames = report.SquadNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();
            string force = squadNames.Count switch
            {
                0 => "Mission force",
                1 => squadNames[0],
                _ => $"{squadNames.Count} squads"
            };
            return $"{force} building {DescribeType(report.ConstructionType)} in {location}";
        }

        public static string BuildSummary(
            ConstructionProgressReport report, string location, double sharedLevelNow)
        {
            string subject = BuildSubject();
            location = Location(location);
            string work = DescribeType(report.ConstructionType);

            if (report.AmountBuilt <= 0)
            {
                return $"{subject} worked on {work} in {location} but made no measurable progress this week.";
            }

            string rating = Describe(sharedLevelNow);
            // The Chapter's own contribution, which is what the squad actually did. Reported
            // separately from the position because they are different numbers whenever an ally is
            // digging here too, and conflating them is what made construction look inert.
            string levels =
                $"({report.LevelBefore:F2} to {report.LevelAfter:F2}, +{report.AmountBuilt:F2} this week)";
            string progress = RatingChanged(report, sharedLevelNow)
                ? $"{subject} advanced {work} in {location} from {Describe(report.SharedLevelBefore)} to {rating} {levels}."
                : $"{subject} made progress on {work} in {location} {levels}; the works still rate {rating}.";

            // With allied works standing here the position is well above the Chapter's own stock,
            // so state it outright - otherwise the two numbers read as a contradiction the moment
            // the player opens the region.
            if (sharedLevelNow > report.LevelAfter + 0.005)
            {
                progress +=
                    $" Combined with allied works the position in {location} "
                    + $"now stands at {rating} ({sharedLevelNow:F2}).";
            }

            return progress + " " + BuildProjection(report, sharedLevelNow);
        }

        // The answer to "does this ever finish?": it never finishes, but here is when the rating
        // the region shows will next change. Everything is measured against the shared position as
        // it stands now, so the projection and the region dossier can never disagree.
        public static string BuildProjection(
            ConstructionProgressReport report, double sharedLevelNow)
        {
            if (report.AmountBuilt <= 0)
            {
                return "No progress was made, so no completion can be projected.";
            }

            double? nextThreshold = NextRatingThreshold(sharedLevelNow);
            if (nextThreshold == null)
            {
                return "These works are already at the highest rating; further effort continues to deepen them.";
            }
            // The rating changes only STRICTLY above the midpoint, so the answer is the smallest
            // whole number of weeks that clears it - floor + 1, not ceiling. Landing exactly on the
            // midpoint still displays the old rating (Math.Round sends an exact .5 to even), and
            // quoting that week would put the report one week ahead of the region screen.
            double threshold = nextThreshold.Value;

            // The rate is how far the POSITION moved this week, not how far the Chapter's own stock
            // did: allied building, decay and sabotage all bear on when the rating actually turns
            // over, and the player is being told about the region, not about the squad.
            double rate = sharedLevelNow - report.SharedLevelBefore;
            string rated = Describe(threshold + Epsilon);

            if (rate <= 0)
            {
                return $"The position lost as much ground this week as it gained, so {rated} "
                    + "is not in prospect at this rate.";
            }
            // Each level costs ten times the last, so a squad adding to a position an ally has
            // already built high moves the shared rating very little. Better to say that plainly
            // than to project a number of weeks that is really "not in this campaign".
            if (rate < MinimumProjectableRate)
            {
                return "The position is already deep enough that this week's work barely moved it; "
                    + $"reaching {rated} would take far longer at this rate.";
            }

            int weeks = (int)Math.Floor((threshold - sharedLevelNow) / rate) + 1;
            weeks = Math.Max(1, weeks);
            return weeks == 1
                ? $"At this rate the works reach {rated} next week."
                : $"At this rate the works reach {rated} in about {weeks} more weeks.";
        }

        public static string DescribeType(DefenseType defenseType) => defenseType switch
        {
            DefenseType.Entrenchment => "entrenchments",
            DefenseType.ListeningPost => "a listening post",
            DefenseType.AntiAir => "anti-air defenses",
            DefenseType.Organization => "local organization",
            _ => "fortifications"
        };

        // Measured on the shared position: the rating the player sees on the region is the side's,
        // not the Chapter's own stock.
        private static bool RatingChanged(ConstructionProgressReport report, double sharedLevelNow) =>
            Describe(report.SharedLevelBefore) != Describe(sharedLevelNow);

        // The lowest level that displays as a better rating than the current one, or null when the
        // current level is already in the top band.
        private static double? NextRatingThreshold(double level)
        {
            string current = Describe(level);
            foreach (double threshold in RatingThresholds)
            {
                if (threshold >= level && Describe(threshold + Epsilon) != current) return threshold;
            }
            return null;
        }

        private static string Describe(double level) =>
            RegionFactionExtensions.GetDefenseLevelDescription(level);

        private static string Location(string location) =>
            string.IsNullOrWhiteSpace(location) ? "an unknown location" : location;
    }
}
