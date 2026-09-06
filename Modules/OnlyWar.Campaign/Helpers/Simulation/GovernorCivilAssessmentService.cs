using OnlyWar.Models;
using OnlyWar.Models.Planets;
using System;
using System.Linq;

namespace OnlyWar.Helpers.Simulation;

/// <summary>
/// Produces the governor's qualitative, personality-biased claim about civil order. The Chapter
/// never receives the underlying Contentment value; open revolt and independent intelligence remain
/// separate, harder evidence.
/// </summary>
public static class GovernorCivilAssessmentService
{
    public static string Assess(Planet planet, Character governor)
    {
        if (planet == null || governor == null) return "No reliable assessment";
        if (planet.Regions.Where(region => region != null)
            .SelectMany(region => region.RegionFactionMap.Values)
            .Any(rf => rf.IsPublic && rf.PlanetFaction.Faction.GrowthType == GrowthType.Unrest))
        {
            return "Organized rebellion underway";
        }

        var imperialRegions = planet.Regions.Where(region => region != null)
            .Select(region => region.RegionFactionMap.Values.FirstOrDefault(rf =>
                rf.PlanetFaction.Faction.IsDefaultFaction && rf.Population > 0))
            .Where(rf => rf != null)
            .ToList();
        long population = imperialRegions.Sum(rf => rf.Population);
        if (population <= 0) return "No reliable assessment";

        double actual = imperialRegions.Sum(rf => rf.Contentment * (double)rf.Population) / population;
        // Poor investigators default to the comforting official story; paranoia pulls the report
        // sharply in the other direction and can report treason where no organized movement exists.
        double perceived = Math.Clamp(
            actual + 15.0 * (1.0 - governor.Investigation) - 20.0 * governor.Paranoia,
            0.0,
            100.0);
        return perceived switch
        {
            >= 70.0 => "The population is steadfastly loyal",
            >= 55.0 => "Civil order is secure",
            >= 40.0 => "Isolated sedition is suspected",
            >= 25.0 => "Seditious activity is widespread",
            _ => "The world is rife with traitors"
        };
    }
}
