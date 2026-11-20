using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using OnlyWar.Domain.Events;

namespace OnlyWar.Campaign.Narrative
{
    /// <summary>
    /// One section of generated history: a pool of hand-authored variants that share one
    /// placeholder set (TDD §6.9). The founding myth is built from these,
    /// and the later chapter-history generator is meant to reuse them. A variant is chosen from
    /// the campaign seed and the section key alone, so a seed always reproduces the same text.
    /// </summary>
    public sealed class NarrativeSection
    {
        private static readonly Regex Placeholder = new(@"\{[A-Za-z]+\}", RegexOptions.Compiled);

        public string Key { get; }
        public IReadOnlyList<string> Variants { get; }

        public NarrativeSection(string key, params string[] variants)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A section key is required.", nameof(key));
            if (variants == null || variants.Length == 0)
                throw new ArgumentException($"Section '{key}' has no variants.", nameof(variants));
            Key = key;
            Variants = variants;
        }

        public int SelectVariant(long seed) =>
            SeededSelection.Select(seed, $"narrative/section/{Key}", Variants.Count);

        /// <summary>Fills the chosen variant. A placeholder with no token is an authoring error.</summary>
        public string Compose(long seed, IReadOnlyDictionary<string, string> tokens) =>
            Fill(Variants[SelectVariant(seed)], tokens, Key);

        public static string Fill(string template, IReadOnlyDictionary<string, string> tokens, string sectionKey)
        {
            string text = Placeholder.Replace(template, match =>
                tokens.TryGetValue(match.Value, out string value) && value != null
                    ? value
                    : throw new InvalidOperationException(
                        $"Narrative section '{sectionKey}' has no value for {match.Value}."));
            return text;
        }
    }
}
