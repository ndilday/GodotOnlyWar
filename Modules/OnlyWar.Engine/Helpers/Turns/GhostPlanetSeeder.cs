using OnlyWar.Helpers.Simulation;
using OnlyWar.Models;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>Seeds off-map ghost population sources for factions with that capability.</summary>
    internal static class GhostPlanetSeeder
    {
        internal static void Seed(Sector sector, GameRulesData rules, IRNG random) =>
            StrategicInvasionLifecycleProcessor.SeedGhostSources(sector, rules, random);
    }
}
