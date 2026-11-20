using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Domain;
using OnlyWar.Domain.Events;
using OnlyWar.Generation.Abstractions;

namespace OnlyWar.Campaign.Narrative
{
    /// <summary>
    /// Writes the founding myth (TDD §6.9): the chapter's history before the
    /// first turn, in the Chronicle's annal voice, with one passage marked as legend. It is
    /// built from five sections, each with its own variant pool: Origin, Trial, Election,
    /// Charge and Legend. Some legends join the Election paragraph, so the myth has four or five
    /// paragraphs. The text is composed once and frozen in the event payload.
    /// </summary>
    public static class FoundingMythComposer
    {
        public const string NarratorKey = "founding-myth";

        // Chapter names are often plural ("Iron Wardens"), so no template puts a verb, a pronoun or
        // a possessive directly on {chapter}; "the {chapter} Chapter" takes the singular. The
        // epithet note follows the sentence that names the founding, so its "it" is the founding.
        private static readonly NarrativeSection Origin = new(
            "founding-myth/origin",
            "The {chapter} Chapter was raised in {founding}, by decree of the High Lords of Terra "
            + "in {foundingYear}.{foundingNote} The Chapter's gene-seed came from Terra under seal, of "
            + "uncertain lineage; the records that would name its progenitors were not sent with "
            + "it. Its first aspirants were taken from {world}, far beyond the borders of the "
            + "sector.",

            "In {foundingYear} the High Lords of Terra decreed {founding}, and the {chapter} "
            + "Chapter was among those it brought into being.{foundingNote} The gene-seed "
            + "entrusted to the Chapter was of uncertain lineage. Its recruiters went first to {world}, "
            + "outside the sector, and took their aspirants there.",

            "The annals of the {chapter} begin in {foundingYear}, with {founding}.{foundingNote} "
            + "From the gene-vaults of Terra the Chapter received gene-seed of uncertain lineage, "
            + "and from {world}, a world outside the sector, it received its first aspirants.");

        private static readonly NarrativeSection Trial = new(
            "founding-myth/trial",
            "Of the {aspirants} aspirants who began the trials, {founders} survived to be named "
            + "battle-brothers. In {implantYear}, two years before the Chapter's first campaign, "
            + "the founders received their progenoid glands, from which the Chapter's next "
            + "generation would one day be made.",

            "{TrainingYears} years of trials followed. Some {aspirants} aspirants began them; "
            + "{founders} lived to take the oaths of a battle-brother. Their progenoid glands were "
            + "implanted in {implantYear}, so that each founder's gene-seed could one day be "
            + "harvested for those who came after.",

            "The trials were long, and the dead were many. Of some {aspirants} who began, "
            + "{founders} completed the {trainingYears} years of training. Their progenoid glands "
            + "were implanted in {implantYear}; the Chapter's second generation would be grown "
            + "from them.");

        private static readonly NarrativeSection Election = new(
            "founding-myth/election",
            "When the training was done, the founders chose their first Chapter Master by vote, "
            + "and the choice fell on {master}.{voteLegend}{notables}{mars}",

            "{master} was chosen by the vote of his brothers to be the first Chapter Master of the "
            + "{chapter}.{voteLegend}{notables}{mars}",

            "The founders elected {master} to lead them; the annals record no rival."
            + "{voteLegend}{notables}{mars}");

        // A chapter that founded without a Chapter Master still voted; the annals lost the name.
        // No legend that names the master joins it.
        private static readonly NarrativeSection ElectionUnrecorded = new(
            "founding-myth/election-unrecorded",
            "When the training was done, the founders chose their first Chapter Master by vote. "
            + "The annals do not keep his name.{voteLegend}{notables}{mars}");

        private static readonly NarrativeSection Charge = new(
            "founding-myth/charge",
            "The Chapter's first charge came from the {authorityTitle}, {authority}. {Enemy} "
            + "splinter had taken {planet}, in the {subsector}, and no regiment could be spared to "
            + "retake it. The {authorityTitle} promised the world to the Chapter as its home, if "
            + "the Chapter could take it back.",

            "The {authorityTitle}, {authority}, gave the {chapter} Chapter its first charge: to "
            + "free {planet}, in the {subsector}, from {enemy} splinter. If the Chapter succeeded, "
            + "{planet} would be its home.",

            "The Chapter had no world of its own. The {authorityTitle}, {authority}, offered it "
            + "one: {planet}, in the {subsector}, lost to {enemy} splinter. Whoever drove the "
            + "xenos out would hold it, and the {chapter} accepted the charge.");

        private static readonly NarrativeSection InvasionCharge = new(
            "founding-myth/charge-invasion",
            "The {authorityTitle}, {authority}, gave the {chapter} Chapter its first charge. "
            + "{Enemy} invasion had overrun {planet}, in the {subsector}. The Navy had denied the "
            + "invaders any reinforcement, but the horde on the ground remained. The world was "
            + "promised to the Chapter once the invasion was broken.",

            "The first charge of the {chapter} was {planet}, in the {subsector}, overrun by "
            + "{enemy} invasion. The {authorityTitle}, {authority}, promised the world to the "
            + "Chapter as its home once the last of the invaders had been hunted down.");

        private sealed record Legend(string Key, string Text, bool JoinsElection, bool NeedsMaster);

        // the one passage marked as legend. The key is saved as an attribute.
        private static readonly Legend[] Legends =
        [
            new("comet",
                "It is said that on the night the first progenoids were implanted, a comet crossed "
                + "the sky above {worldName}, and that the Chapter's Chaplains have argued over its "
                + "meaning ever since.",
                false, false),
            new("for-standing",
                "It is said that when {master} was asked what the Chapter would be remembered for, "
                + "he answered only: \"For standing.\" The words are written on the first page of "
                + "the Chapter's roll.",
                true, true),
            new("banner",
                "It is said that the Chapter's first banner was woven from the cloaks of the "
                + "aspirants who died in the trials, so that none who died seeking the honour would "
                + "be forgotten.",
                false, false),
            new("warp-silence",
                "It is said that the astropaths who sent word of the founding to Terra heard, for "
                + "one moment, a silence in the warp, and that none of them would speak of it "
                + "afterwards.",
                false, false),
            new("blades",
                "It is said that the vote was taken in silence, each founder laying his blade "
                + "before the brother he chose, and that the blades before {master} could not be "
                + "counted.",
                true, true)
        ];

        // Invented worlds outside the sector. A sector world never shares a name with one.
        private static readonly string[] AspirantWorldNames =
        [
            "Skarn", "Vaskor", "Ulvane", "Morrowfall", "Kharos", "Ostra Fen", "Brannoch", "Iskar",
            "Teodric's Reach", "Cador Minor", "Grend", "Hollow Kell", "Saint Abelard", "Thessaly Deep",
            "Varnis", "Orrin's Rest", "Kaldmere", "Duskhallow"
        ];

        private static readonly string[] AspirantWorldKinds =
        [
            "the feral world", "the death world", "the ice world", "the hive world",
            "the penal world", "the shrine world"
        ];

        private static readonly string[] NumberWords =
        [
            "no", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
            "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen",
            "eighteen", "nineteen", "twenty"
        ];

        public static string ComposeTitle(string chapterName) => $"The Founding of the {chapterName}";

        public static ChapterFoundingMythPayload Compose(FoundingMythTokens tokens)
        {
            if (tokens == null) throw new ArgumentNullException(nameof(tokens));
            if (string.IsNullOrWhiteSpace(tokens.ChapterName))
                throw new ArgumentException("A chapter name is required.", nameof(tokens));
            if (tokens.Founding == null)
                throw new ArgumentException("A founding is required.", nameof(tokens));
            if (tokens.CampaignStartDate == null)
                throw new ArgumentException("A campaign start date is required.", nameof(tokens));

            long seed = tokens.Seed;
            Founding founding = tokens.Founding.Founding;
            string worldName = SelectAspirantWorld(seed, tokens.SectorWorldNames);
            string worldKind = AspirantWorldKinds[
                SeededSelection.Select(seed, "founding-myth/world-kind", AspirantWorldKinds.Length)];
            int aspirants = InventAspirantCount(seed, tokens.FounderCount);
            bool hasMaster = !string.IsNullOrWhiteSpace(tokens.ChapterMasterName);
            Legend[] legends = Legends.Where(legend => hasMaster || !legend.NeedsMaster).ToArray();
            Legend legend = legends[SeededSelection.Select(seed, "founding-myth/legend", legends.Length)];

            Dictionary<string, string> values = new(StringComparer.Ordinal)
            {
                ["{chapter}"] = tokens.ChapterName,
                ["{founding}"] = founding == null
                    ? "a founding whose number the records do not preserve"
                    : $"the {founding.OrdinalName}",
                ["{foundingNote}"] = founding?.Epithet == null
                    ? string.Empty
                    : $" Later ages would call it the {founding.Epithet} Founding.",
                ["{voteLegend}"] = string.Empty,
                ["{foundingYear}"] = tokens.Founding.FoundingDate.ToYearString(),
                ["{world}"] = $"{worldKind} of {worldName}",
                ["{worldName}"] = worldName,
                ["{aspirants}"] = aspirants.ToString("N0"),
                ["{founders}"] = tokens.FounderCount.ToString("N0"),
                ["{trainingYears}"] = CountWord(ChapterFounding.TrainingYears),
                ["{TrainingYears}"] = Capitalize(CountWord(ChapterFounding.TrainingYears)),
                ["{implantYear}"] = Date.FromAbsoluteYear(
                    Math.Max(1, tokens.CampaignStartDate.AbsoluteYear - 2)).ToYearString(),
                ["{master}"] = tokens.ChapterMasterName ?? string.Empty,
                ["{notables}"] = ComposeNotables(tokens),
                ["{mars}"] = ComposeMarsCohort(tokens.MarsCohortCount),
                ["{authorityTitle}"] = tokens.AuthorityTitle ?? "Sector Lord",
                ["{authority}"] = tokens.AuthorityName ?? "whose name is not recorded",
                ["{planet}"] = tokens.PlanetName ?? "the Promised World",
                ["{subsector}"] = tokens.SubsectorName ?? "sector",
                ["{enemy}"] = WithArticle(tokens.EnemyName ?? "alien"),
                ["{Enemy}"] = Capitalize(WithArticle(tokens.EnemyName ?? "alien"))
            };

            // A legend about the vote follows the vote inside the Election paragraph; any other
            // legend is the last paragraph.
            string legendText = NarrativeSection.Fill(legend.Text, values, "founding-myth/legend");
            values["{voteLegend}"] = legend.JoinsElection ? " " + legendText : string.Empty;
            List<string> paragraphs =
            [
                Origin.Compose(seed, values),
                Trial.Compose(seed, values),
                (hasMaster ? Election : ElectionUnrecorded).Compose(seed, values),
                (tokens.Invasion ? InvasionCharge : Charge).Compose(seed, values)
            ];
            if (!legend.JoinsElection) paragraphs.Add(legendText);

            return new ChapterFoundingMythPayload(
                tokens.ChapterName,
                founding?.Id,
                founding?.Number ?? 0,
                founding?.Epithet,
                tokens.Founding.FoundingYear,
                worldName,
                worldKind,
                aspirants,
                tokens.FounderCount,
                Math.Max(0, tokens.MarsCohortCount),
                legend.Key,
                ComposeTitle(tokens.ChapterName),
                string.Join("\n\n", paragraphs));
        }

        // The first name in the pool, from a seeded start, that no sector world uses.
        private static string SelectAspirantWorld(long seed, IReadOnlyCollection<string> sectorWorldNames)
        {
            HashSet<string> taken = new(sectorWorldNames ?? [], StringComparer.OrdinalIgnoreCase);
            int start = SeededSelection.Select(seed, "founding-myth/world", AspirantWorldNames.Length);
            for (int offset = 0; offset < AspirantWorldNames.Length; offset++)
            {
                string candidate = AspirantWorldNames[(start + offset) % AspirantWorldNames.Length];
                if (!taken.Contains(candidate)) return candidate;
            }
            return $"{AspirantWorldNames[start]} Secundus";
        }

        // Three to six aspirants began for every founder who survived, rounded to the hundred.
        private static int InventAspirantCount(long seed, int founders)
        {
            int survivors = Math.Max(1, founders);
            double ratio = 3.0 + (0.25 * SeededSelection.Select(seed, "founding-myth/aspirants", 13));
            int rounded = (int)(Math.Round(survivors * ratio / 100.0) * 100);
            return Math.Max(survivors + 100, rounded);
        }

        private static string ComposeNotables(FoundingMythTokens tokens)
        {
            List<string> names = [];
            if (!string.IsNullOrWhiteSpace(tokens.FirstCaptainName))
                names.Add($"{tokens.FirstCaptainName}, Captain of the First Company");
            if (!string.IsNullOrWhiteSpace(tokens.ChampionName))
                names.Add($"{tokens.ChampionName}, the Chapter's first Champion");
            if (!string.IsNullOrWhiteSpace(tokens.AncientName))
                names.Add($"{tokens.AncientName}, who first carried its banner as Ancient");
            if (!string.IsNullOrWhiteSpace(tokens.LibrarianName))
                names.Add($"{tokens.LibrarianName}, master of its Librarius");
            return names.Count == 0
                ? string.Empty
                : $" Among the founders the annals also name {JoinWithAnd(names)}.";
        }

        // the Mars cohort is a count, not names.
        private static string ComposeMarsCohort(int count) => count switch
        {
            <= 0 => string.Empty,
            1 => " One brother was sent to Mars to begin his decades-long training as a Techmarine.",
            _ => $" {Capitalize(CountWord(count))} brothers were sent to Mars to begin their "
                + "decades-long training as Techmarines."
        };

        private static string JoinWithAnd(IReadOnlyList<string> items) => items.Count switch
        {
            1 => items[0],
            2 => $"{items[0]} and {items[1]}",
            _ => $"{string.Join("; ", items.Take(items.Count - 1))}; and {items[^1]}"
        };

        private static string CountWord(int count) =>
            count >= 0 && count < NumberWords.Length ? NumberWords[count] : count.ToString("N0");

        // The enemy names a splinter or an invasion, so a plural faction name ("Orks") is used in
        // its singular, adjective form ("an Ork invasion").
        private static string WithArticle(string noun)
        {
            string trimmed = noun.Trim();
            if (trimmed.Length > 3 && trimmed.EndsWith('s') && !trimmed.EndsWith("ss", StringComparison.Ordinal))
                trimmed = trimmed[..^1];
            bool vowel = trimmed.Length > 0 && "AEIOUaeiou".IndexOf(trimmed[0]) >= 0;
            return $"{(vowel ? "an" : "a")} {trimmed}";
        }

        private static string Capitalize(string text) =>
            string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }
}
