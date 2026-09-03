using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models
{
    /// <summary>
    /// Narrow capability queries shared by campaign, battle, mission, and presentation code.
    /// Keeping these queries beside the flags prevents callers from rebuilding composite faction
    /// identities such as "the one hostile indelible horde".
    /// </summary>
    public static class FactionCapabilities
    {
        public static bool HasGhostPlanets(Faction faction) =>
            faction?.HasBehavior(FactionBehavior.HasGhostPlanets) == true;

        public static bool HasDormantPopulations(Faction faction) =>
            faction?.HasBehavior(FactionBehavior.HasDormantPopulations) == true;

        public static bool GeneratesInvasions(Faction faction) =>
            faction?.HasBehavior(FactionBehavior.GeneratesInvasions) == true;

        public static bool HasMobMentality(Faction faction) =>
            faction?.HasBehavior(FactionBehavior.MobMentality) == true;

        public static IEnumerable<Faction> WithCapability(
            IEnumerable<Faction> factions,
            FactionBehavior capability) =>
            (factions ?? Enumerable.Empty<Faction>())
                .Where(faction => faction?.HasBehavior(capability) == true);
    }
}
