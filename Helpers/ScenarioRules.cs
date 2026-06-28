namespace OnlyWar.Helpers
{
    // Centralized tunables for the Opening Scenario ("Promised World" — Design/OpeningScenario.md
    // §8). These are the load-bearing balance numbers that decide whether the first objective is
    // tense-and-winnable; they live here (mirroring MedicalProcedureRules / GeneseedRules) so they
    // are tuned in one place rather than scattered as literals across the builder. The values below
    // are a starting point flagged for playtesting (design step 7), not final.
    public static class ScenarioRules
    {
        // Eligible-promised-world population band (raw headcount): big enough to be a worthwhile
        // reward, small enough to be a starter rather than a hive world.
        public const long MinPromisedWorldPopulation = 5_000_000L;
        public const long MaxPromisedWorldPopulation = 500_000_000L;

        // Number of regions stamped with Tyranid presence (a contiguous-ish cluster of the
        // world's 16 regions). Chosen deterministically within this inclusive band.
        public const int MinTyranidRegions = 2;
        public const int MaxTyranidRegions = 3;

        // Tuned starting strength per stamped Tyranid region. Garrison is the load-bearing
        // balance number (§8); Population seeds the swarm's organic base.
        public const long TyranidRegionGarrison = 2_000L;
        public const long TyranidRegionPopulation = 50_000L;

        // Organic-growth throttle on stamped Tyranid regions (multiplicative, < 1). Felt, but
        // unable to outpace a player who is actually fighting (§8 proposes ~0.3–0.5).
        public const float TyranidGrowthMultiplier = 0.4f;

        // Fraction of a stamped region's original Imperial civilian population left behind as a
        // displaced remnant, so the region reads as "overrun" rather than empty. The remnant's
        // garrison is zeroed (the local PDF was broken) and it is hidden (non-public), so the
        // region resolves to single, Tyranid control.
        public const float ImperialRemnantFraction = 0.1f;
    }
}
