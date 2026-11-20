using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Domain
{
    /// <summary>
    /// An Imperial founding: a decree that created a number of new chapters at once
    /// (TDD §6.9). Authored in the rules database's Founding table. Years
    /// are absolute (<see cref="Date.AbsoluteYear"/>), so a range can cross a millennium.
    /// </summary>
    public sealed class Founding
    {
        public int Id { get; }
        public int Number { get; }
        // "Dark", "Cursed": flavour only, no mechanics.
        public string Epithet { get; }
        public int EarliestYear { get; }
        public int LatestYear { get; }

        public Founding(int id, int number, string epithet, int earliestYear, int latestYear)
        {
            if (number <= 0) throw new ArgumentOutOfRangeException(nameof(number));
            if (earliestYear < 1) throw new ArgumentOutOfRangeException(nameof(earliestYear));
            if (latestYear < earliestYear)
                throw new ArgumentException($"Founding {number} ends before it begins.", nameof(latestYear));
            Id = id;
            Number = number;
            Epithet = string.IsNullOrWhiteSpace(epithet) ? null : epithet.Trim();
            EarliestYear = earliestYear;
            LatestYear = latestYear;
        }

        public bool Contains(int absoluteYear) => absoluteYear >= EarliestYear && absoluteYear <= LatestYear;

        /// <summary>"Twenty-First Founding" style ordinal, for prose.</summary>
        public string OrdinalName => $"{Ordinals.ToWords(Number)} Founding";
    }

    /// <summary>
    /// The founding a campaign's chapter belongs to and the year of it. The campaign starts
    /// <see cref="TrainingYears"/> after the founding, when the founders finish training. A null
    /// <see cref="Founding"/> is a founding whose number the records do not preserve.
    /// </summary>
    public sealed record ChapterFounding(Founding Founding, int FoundingYear)
    {
        // The founders' training, from the decree to the campaign's first turn (SectorBuilder).
        public const int TrainingYears = 4;

        public Date FoundingDate => Date.FromAbsoluteYear(FoundingYear);
        public Date CampaignStartDate => Date.FromAbsoluteYear(FoundingYear + TrainingYears);
    }

    public static class Ordinals
    {
        private static readonly string[] Units =
        [
            "", "First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh", "Eighth",
            "Ninth", "Tenth", "Eleventh", "Twelfth", "Thirteenth", "Fourteenth", "Fifteenth",
            "Sixteenth", "Seventeenth", "Eighteenth", "Nineteenth"
        ];
        private static readonly string[] TensOrdinal =
            ["", "", "Twentieth", "Thirtieth", "Fortieth", "Fiftieth", "Sixtieth", "Seventieth", "Eightieth", "Ninetieth"];
        private static readonly string[] Tens =
            ["", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"];

        /// <summary>1 to 99 as an ordinal word ("Twenty-First"); other values as "Nth".</summary>
        public static string ToWords(int number)
        {
            if (number <= 0 || number >= 100) return $"{number}th";
            if (number < 20) return Units[number];
            int tens = number / 10;
            int units = number % 10;
            return units == 0 ? TensOrdinal[tens] : $"{Tens[tens]}-{Units[units]}";
        }
    }

    public static class FoundingCatalog
    {
        /// <summary>The founding whose range holds <paramref name="absoluteYear"/>, or null.</summary>
        public static Founding FindContaining(IEnumerable<Founding> foundings, int absoluteYear) =>
            (foundings ?? []).OrderBy(founding => founding.Number)
                .FirstOrDefault(founding => founding.Contains(absoluteYear));
    }
}
