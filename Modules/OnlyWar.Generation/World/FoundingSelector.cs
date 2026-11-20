using System;
using OnlyWar.Domain;
using OnlyWar.Domain.Events;

namespace OnlyWar.Generation.World
{
    /// <summary>
    /// Chooses the founding a new chapter belongs to (TDD §6.9). The
    /// founding sets the era: the campaign starts when the founders finish training.
    /// </summary>
    public static class FoundingSelector
    {
        // The year an unnumbered founding takes when the rules carry no foundings: the start of
        // 500.M39 that every campaign used before foundings existed.
        private const int DefaultFoundingYear = 38496;

        /// <summary>
        /// A founding and a year within its range, both from the seed alone. Each row has the
        /// same chance, and so does each year in the chosen row's range.
        /// </summary>
        public static ChapterFounding Select(GameRulesData rules, int seed)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (rules.Foundings.Count == 0) return new ChapterFounding(null, DefaultFoundingYear);
            Founding founding = rules.Foundings[
                SeededSelection.Select(seed, "founding/row", rules.Foundings.Count)];
            int span = founding.LatestYear - founding.EarliestYear + 1;
            int year = founding.EarliestYear + SeededSelection.Select(seed, "founding/year", span);
            return new ChapterFounding(founding, year);
        }

        /// <summary>
        /// The founding that agrees with a given campaign start date: the one whose range holds the
        /// start year less the training years. Tests and the debug preview start on 500.M39, which
        /// gives the 24th Founding in 496.M39. A start no founding covers gets a founding
        /// whose number the records do not preserve.
        /// </summary>
        public static ChapterFounding ForCampaignStart(GameRulesData rules, Date campaignStart)
        {
            if (campaignStart == null) throw new ArgumentNullException(nameof(campaignStart));
            int year = Math.Max(1, campaignStart.AbsoluteYear - ChapterFounding.TrainingYears);
            return new ChapterFounding(FoundingCatalog.FindContaining(rules?.Foundings, year), year);
        }
    }
}
