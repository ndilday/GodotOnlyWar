using OnlyWar.Domain;

namespace OnlyWar.Application;

/// <summary>
/// Icon key selection for the planetary map. Choosing which icon represents a faction is a
/// projection decision the application makes; resolving the key to an atlas texture stays in the
/// Godot host's <c>IconAtlas</c>.
/// </summary>
public static class MapIconKeys
{
    public static string ForFaction(Faction faction)
    {
        if (faction == null) return null;
        if (faction.IsPlayerFaction) return "map_player";
        if (faction.IsDefaultFaction) return "map_imperial";

        string name = faction.Name?.ToLowerInvariant() ?? "";
        if (name.Contains("tyranid")) return "map_tyranids";
        if (name.Contains("genestealer")) return "map_genestealer_cult";
        if (name.Contains("ork")) return "map_orks";
        // Faction art is required content. Returning no key makes the missing-art path loud in
        // RegionMapCardView instead of falsely representing a new faction as an existing faction.
        return null;
    }
}
