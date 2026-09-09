using OnlyWar.Domain;
using OnlyWar.Domain.Planets;
using System.Linq;

namespace OnlyWar.Campaign.Turns
{
    /// <summary>
    /// World-control policy for granting the Chapter its homeworld. This is a campaign consequence
    /// of winning the opening scenario, not part of world generation, so it lives with the runtime
    /// world-control owner rather than in the sector builder (SB-09).
    /// </summary>
    public static class ChapterHomeworldService
    {
        // Reward path (Design/Reference/OpeningScenario.md): install the player as the planet-wide
        // controlling faction, inheriting the displaced Imperial population/garrison region by
        // region. Invoked by TurnController when the opening scenario is won.
        //
        // The Imperial (default) faction is resolved from the planet's faction map rather than via
        // GetControllingFaction: on a freshly-liberated world a cleared former-Tyranid region can
        // momentarily have no public faction (the displaced civilian remnant is non-public), which
        // would make GetControllingFaction's per-region resolution throw. Each region inherits the
        // Imperial garrison/population if that faction is present there, otherwise it is granted to
        // the player at zero strength.
        public static void ReplaceChapterPlanetFaction(Planet chapterPlanet, Faction playerFaction)
        {
            PlanetFaction existingPlanetFaction = chapterPlanet.PlanetFactionMap.Values
                .FirstOrDefault(pf => pf.Faction.IsDefaultFaction);
            int? existingFactionId = existingPlanetFaction?.Faction.Id;

            PlanetFaction homePlanetFaction = new PlanetFaction(playerFaction)
            {
                IsPublic = true,
                Leader = null,
                PlayerReputation = 1
            };
            foreach (Region region in chapterPlanet.Regions)
            {
                RegionFaction homePlanetRegionFaction = new RegionFaction(homePlanetFaction, region)
                {
                    IsPublic = true
                };
                if (existingFactionId.HasValue
                    && region.RegionFactionMap.TryGetValue(existingFactionId.Value, out RegionFaction existingRegionFaction))
                {
                    homePlanetRegionFaction.Garrison = existingRegionFaction.Garrison;
                    homePlanetRegionFaction.Population = existingRegionFaction.Population;
                    region.RegionFactionMap.Remove(existingFactionId.Value);
                }
                region.RegionFactionMap[playerFaction.Id] = homePlanetRegionFaction;
            }
            if (existingFactionId.HasValue)
            {
                chapterPlanet.PlanetFactionMap.Remove(existingFactionId.Value);
            }
            chapterPlanet.PlanetFactionMap[homePlanetFaction.Faction.Id] = homePlanetFaction;
        }
    }
}
