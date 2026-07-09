namespace OnlyWar.Helpers
{
    // Centralized tunables for the Opening Scenario ("Promised World" — Design/OpeningScenario.md
    // §8). These are the load-bearing balance numbers that decide whether the first objective is
    // tense-and-winnable; they live here (mirroring MedicalProcedureRules / GeneseedRules) so they
    // are tuned in one place rather than scattered as literals across the builder. The values below
    // are a starting point flagged for playtesting (design step 7), not final.
    public static class ScenarioRules
    {
        // The promised world is constrained by *type* rather than by a population floor. There is no
        // minimum size: several canon Chapters recruit from feral and death worlds, so a sparse,
        // low-population world is a perfectly good — even iconic — first home. The upper bound stays
        // as a soft ceiling so the starter objective is not a multi-billion-population behemoth.
        public const long MaxPromisedWorldPopulation = 50_000_000L;

        // Planet types that can never be the promised world. Hive worlds are ruled out (a hive world
        // is not a "grant it to your Chapter" starter, and its scale swamps the opening fight); forge
        // worlds likewise (a Mechanicus holding is not the Sector Lord's to pledge, and they sit at
        // billions of population). Matched against PlanetTemplate.Name. Everything else — Civilised,
        // Agri, Feudal, Feral, Death — is eligible.
        public static readonly System.Collections.Generic.HashSet<string> ExcludedPromisedWorldTypes =
            new(System.StringComparer.OrdinalIgnoreCase) { "Hive", "Forge" };

        // Number of regions stamped with Tyranid presence (a contiguous-ish cluster of the
        // world's 16 regions). Chosen deterministically within this inclusive band.
        public const int MinTyranidRegions = 2;
        public const int MaxTyranidRegions = 3;

        // Starting strength per stamped Tyranid region, expressed as a fraction of the promised
        // world's *average Imperial region* (garrison and population), measured before the stamp
        // (§8). This is the load-bearing balance number.
        //
        // It is relative rather than absolute because the promised world is drawn from a wide
        // population band ([5M, 500M]); a fixed headcount that is a tense garrison on a 5M world is
        // a rounding error against a 500M world's multi-million-strong PDF (an early playtest of the
        // old absolute 2,000-garrison constant found the Tyranids ~1000x too weak to register on
        // the larger worlds — trivial to clear and unable to press outward). Scaling to the world's
        // own PDF keeps the fight in the same ballpark across the whole band. < 1 so a single
        // stamped region is weaker than a full Imperial region, keeping the objective winnable.
        public const float TyranidStrengthFraction = 0.5f;

        // Fraction of a stamped region's original Imperial civilian population left behind as a
        // displaced remnant, so the region reads as "overrun" rather than empty. The remnant's
        // garrison is zeroed (the local PDF was broken) and it is hidden (non-public), so the
        // region resolves to single, Tyranid control.
        public const float ImperialRemnantFraction = 0.1f;

        // Opening-scenario temporal sequencing (Design/OpeningScenario.md §4.24, "Opening Scenario
        // Application"). Rather than authoring a static board, the stamp plays the opening out as a
        // timed sequence during generation: the revealed Genestealer Cult fights the PDF for
        // PreLandingTurns weeks, the Tyranids then make planetfall, and the stranded swarm feeds for
        // a Gaussian-random stretch before the player arrives. This produces the varied "Promised
        // World" states the campaign is built around from the same simulation that runs during play.
        // All three values below are first-pass tunables flagged for playtesting.
        //
        // Weeks the revealed cult wars against the PDF before the Tyranids land, weakening defenders.
        public const int PreLandingTurns = 2;

        // The promised world is the hive fleet's landing site because a deep, long-established
        // Genestealer Cult has hollowed out its PDF and its psychic beacon called the swarm down. A
        // random infiltration roll can leave only a token cult, so on this one world the cult is
        // pulled up to this share of each region's combined (cult + Imperial) population and garrison,
        // carving the difference out of the Imperial owner. A cult now rises on its POPULATION vs the
        // PDF's garrison (PRD §4.24), so even 10% of the world dwarfs the PDF and comfortably sustains
        // the revolt — a larger share would represent a cult that should have revolted long ago, and
        // pushes an absurd fraction of the planet into open cult hands. Playtest-pending.
        public const float PromisedWorldCultStrengthFraction = 0.10f;

        // The promised world's Cult has infiltrated the PDF and government before it rises, so it
        // starts with enough per-region belief to choose assaults from knowledge rather than spending
        // the opening sim repeatedly scouting what its agents should already understand.
        public const float PromisedWorldCultStartingIntel = 3.0f;

        // Post-landing feeding runs for max(0, round(PostLandingTurnsMean + z)) weeks, z ~ N(0,1):
        // sometimes the player inherits a fresh beachhead, sometimes a month-eaten ruin.
        public const double PostLandingTurnsMean = 4.0;

        // Sector Lord opinion swing when the scenario resolves (Design/OpeningScenario.md §6.2).
        // Applied to the *current* seat-holder's OpinionOfPlayerForce: a promise honored raises it,
        // a promise lost lowers it. Magnitudes are starting points for playtesting.
        public const float SectorLordOpinionReward = 0.5f;
        public const float SectorLordOpinionPenalty = 0.5f;
    }
}
