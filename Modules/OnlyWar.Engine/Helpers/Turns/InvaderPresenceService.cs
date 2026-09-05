using OnlyWar.Models;
using OnlyWar.Models.Planets;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>
    /// Establishes or reinforces a public regional foothold for surviving invaders.
    /// Shared by tactical aftermath, strategic combat, and planetary expansion.
    /// </summary>
    internal static class InvaderPresenceService
    {
        internal static void Establish(Faction attacker, Region region, long survivors)
        {
            if (region.RegionFactionMap.TryGetValue(attacker.Id, out RegionFaction existing))
            {
                existing.AddMilitaryStrength(survivors);
                return;
            }

            Planet planet = region.Planet;
            if (!planet.PlanetFactionMap.TryGetValue(attacker.Id, out PlanetFaction planetFaction))
            {
                planetFaction = new PlanetFaction(attacker) { IsPublic = true };
                planet.PlanetFactionMap[attacker.Id] = planetFaction;
            }

            RegionFaction foothold = new(planetFaction, region)
            {
                IsPublic = true,
                Organization = 100
            };
            foothold.AddMilitaryStrength(survivors);
            region.RegionFactionMap[attacker.Id] = foothold;
        }
    }
}
