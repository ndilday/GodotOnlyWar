using System;
using OnlyWar.Models;
using OnlyWar.Models.Planets;

namespace OnlyWar.Helpers.Simulation;

/// <summary>
/// Applies the population-conserving transition from a hidden regional presence to a public one.
/// The garrison of a hidden host-defending faction represents personnel embedded in the nominal
/// PDF, so reveal removes them from that roster before combat begins.
/// </summary>
public static class FactionRevealService
{
    public static void Reveal(RegionFaction regionFaction)
    {
        ArgumentNullException.ThrowIfNull(regionFaction);

        Faction faction = regionFaction.PlanetFaction.Faction;
        switch (faction.GrowthType)
        {
            case GrowthType.Unrest:
                // Defecting PDF members join the revolution's existing civilian fighting force.
                // Garrison and ArmedCivilians are disjoint subsets of Population, so this transfer
                // changes neither allegiance headcount nor total armed revolutionary headcount.
                long embeddedPdf = regionFaction.Garrison;
                regionFaction.Garrison = 0;
                regionFaction.ArmedCivilians += embeddedPdf;
                break;

            case GrowthType.Conversion:
                // A cult's Population is already its fighting force. Its embedded garrison is a
                // subset of that population, not additional people to add when the cult reveals.
                regionFaction.Garrison = 0;
                break;
        }

        regionFaction.IsPublic = true;
        regionFaction.HasEmergenceAdvantage = true;
        // PlanetFaction visibility is the planet-level rollup: public in at least one region.
        regionFaction.PlanetFaction.IsPublic = true;
    }
}
