using System.Collections.Generic;
using OnlyWar.Models.Planets;

namespace OnlyWar.Helpers.Narrative
{
    // Tokens consumed by the Promised World briefing composer. All values are resolved by the
    // caller (ScenarioBuilder) from the generated sector: the chapter, the promised world and its
    // subsector, the invading faction, and the promising authority (the sitting Sector Lord, or a
    // fallback governor). TemplateSelector picks one of the deterministic templates; the caller
    // passes a stable per-seed value (the promised planet's id) so seed + scenario reproduces the
    // same briefing.
    public readonly struct BriefingTokens
    {
        public string ChapterName { get; init; }
        public string PlanetName { get; init; }
        public string SubsectorName { get; init; }
        public string AuthorityName { get; init; }
        public string AuthorityTitle { get; init; }
        public string EnemyName { get; init; }
        public int TemplateSelector { get; init; }
    }

    // Minimal token-substitution composer for the "Promised World" opening briefing
    // (Design/OpeningScenario.md §4). This is an explicit placeholder for the eventual §4.19
    // narrative system: it fills one of a small set of hand-authored templates, chosen
    // deterministically from the tokens, so a run has flavor variety without authoring a full
    // narrator. Output is BBCode (rendered by the briefing dialog's RichTextLabel).
    public static class BriefingComposer
    {
        // Hand-authored templates. Each uses the same placeholder set so substitution leaves no
        // residue regardless of which one is chosen.
        private static readonly string[] Templates =
        {
            "Brothers of the [b]{ChapterName}[/b], your Chapter is born into war. The world of "
            + "[b]{PlanetName}[/b], in the [b]{SubsectorName}[/b], has been bled by a {EnemyName} "
            + "splinter — its defenders broken, its skies cleared by the Navy, but no Guard "
            + "regiment can be spared to retake the ground. [b]{AuthorityTitle} {AuthorityName}[/b] "
            + "has marked it for you: take {PlanetName} from the swarm, and it is yours — your "
            + "Chapter World, in the Emperor's name.",

            "[b]{AuthorityTitle} {AuthorityName}[/b] has called upon the newborn "
            + "[b]{ChapterName}[/b]. A {EnemyName} incursion has fallen upon [b]{PlanetName}[/b] in "
            + "the [b]{SubsectorName}[/b]; the Imperial Navy has scoured the void clean above it, "
            + "but the ground is lost and no regiment can be spared. Cleanse {PlanetName} of the "
            + "xenos and the world is granted to you — the first holding of your Chapter, won "
            + "in blood.",

            "War is your Chapter's cradle, [b]{ChapterName}[/b]. In the [b]{SubsectorName}[/b], the "
            + "world of [b]{PlanetName}[/b] writhes under a {EnemyName} splinter — its defenders "
            + "shattered, its people overrun, its orbit at last broken open by the Imperial Navy. "
            + "[b]{AuthorityTitle} {AuthorityName}[/b] makes you a promise: liberate {PlanetName}, "
            + "and it shall be yours to hold for the Emperor evermore."
        };

        // The honorific borne by the promising authority, derived from the governance rank of the
        // world they are seated on (Design/OpeningScenario.md §2.3 / §4). A hand-authored
        // Character.Title can supersede this later.
        public static string GetAuthorityTitle(GovernanceTier tier)
        {
            return tier switch
            {
                GovernanceTier.SectorCapital => "Lord of the Sector",
                GovernanceTier.SubsectorCapital => "Lord of the Subsector",
                _ => "Planetary Governor"
            };
        }

        public static string ComposePromisedWorldBriefing(BriefingTokens tokens)
        {
            // Modulo selection (non-negative) keeps the choice stable and in-range for any
            // selector the caller hands in.
            int count = Templates.Length;
            int index = ((tokens.TemplateSelector % count) + count) % count;

            Dictionary<string, string> substitutions = new Dictionary<string, string>
            {
                ["{ChapterName}"] = tokens.ChapterName,
                ["{PlanetName}"] = tokens.PlanetName,
                ["{SubsectorName}"] = tokens.SubsectorName,
                ["{AuthorityName}"] = tokens.AuthorityName,
                ["{AuthorityTitle}"] = tokens.AuthorityTitle,
                ["{EnemyName}"] = tokens.EnemyName
            };

            string text = Templates[index];
            foreach (KeyValuePair<string, string> substitution in substitutions)
            {
                text = text.Replace(substitution.Key, substitution.Value);
            }
            return text;
        }
    }
}
