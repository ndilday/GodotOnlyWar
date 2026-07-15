using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models
{
    /// <summary>
    /// Resolves the specific non-player factions that sector generation places by
    /// name into stable, typed accessors. Resolution happens once at rules-database
    /// load (via <see cref="GameRulesData"/>) and fails fast with a clear error if a
    /// required faction is missing or ambiguous, instead of producing a runtime null
    /// deep inside <c>SectorBuilder</c>.
    ///
    /// This is the sector-generation sibling of <see cref="Soldiers.NamedSkillRegistry"/>
    /// and <see cref="ChapterGenerationTemplates"/>; see TDD §8.3 / §4.1.1 for the
    /// validated-registry pattern these are migrating toward. The role-based property
    /// names document how each faction is used; the concrete lore faction is noted
    /// alongside.
    /// </summary>
    internal sealed class SectorGenerationFactions
    {
        /// <summary>The hidden, infiltration-capable faction (Genestealer Cult).</summary>
        public Faction Infiltrator { get; }

        /// <summary>The overt invasion faction (Tyranids).</summary>
        public Faction Invader { get; }

        /// <summary>The sector-wide secular-rebellion faction.</summary>
        public Faction Insurrectionists { get; }

        public SectorGenerationFactions(IReadOnlyList<Faction> factions)
        {
            Infiltrator = Resolve(factions, "Genestealer Cult");
            Invader = Resolve(factions, "Tyranids");
            Insurrectionists = Resolve(factions, "Insurrectionists");
        }

        private static Faction Resolve(IReadOnlyList<Faction> factions, string name)
        {
            List<Faction> matches = factions?.Where(f => f.Name == name).ToList() ?? new List<Faction>();
            if (matches.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Required faction '{name}' was not found in the rules database.");
            }
            if (matches.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Required faction '{name}' is ambiguous; {matches.Count} entries share that name.");
            }
            return matches[0];
        }
    }
}
