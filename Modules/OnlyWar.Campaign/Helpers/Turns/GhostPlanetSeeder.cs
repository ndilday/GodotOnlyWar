using OnlyWar.Campaign.Simulation;
using OnlyWar.Domain;

namespace OnlyWar.Campaign.Turns
{
    /// <summary>Seeds off-map ghost population sources for factions with that capability.</summary>
    internal static class GhostPlanetSeeder
    {
        internal static void Seed(Sector sector, GameRulesData rules, IRNG random) =>
            StrategicInvasionLifecycleProcessor.SeedGhostSources(sector, rules, random);
    }
}
