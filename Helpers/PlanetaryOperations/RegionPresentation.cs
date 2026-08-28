using OnlyWar.Helpers;
using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.PlanetaryOperations
{
    public enum RegionControlState
    {
        Imperial,
        Enemy,
        Contested
    }

    public sealed record RegionPresencePresentation(
        int FactionId,
        string FactionName,
        bool IsImperial,
        bool IsPlayer,
        string IconKey);

    public sealed record RegionControlPresentationModel(
        RegionControlState State,
        IReadOnlyList<RegionPresencePresentation> Presences);

    public static class RegionControlPresentation
    {
        public static RegionControlPresentationModel Build(Region region)
        {
            if (region == null)
            {
                return new RegionControlPresentationModel(
                    RegionControlState.Contested, []);
            }

            List<RegionFaction> disclosed = region.RegionFactionMap.Values
                .Where(presence => presence?.IsPublic == true)
                .OrderBy(presence => PresenceOrder(presence.PlanetFaction.Faction))
                .ThenBy(presence => presence.PlanetFaction.Faction.Name)
                .ThenBy(presence => presence.PlanetFaction.Faction.Id)
                .ToList();
            RegionControlState state = region.ControllingFaction == null
                ? RegionControlState.Contested
                : FactionRelationshipService.IsImperial(
                    region.ControllingFaction.PlanetFaction.Faction)
                    ? RegionControlState.Imperial
                    : RegionControlState.Enemy;

            return new RegionControlPresentationModel(
                state,
                disclosed.Select(presence =>
                {
                    Faction faction = presence.PlanetFaction.Faction;
                    return new RegionPresencePresentation(
                        faction.Id,
                        faction.Name,
                        FactionRelationshipService.IsImperial(faction),
                        faction.IsPlayerFaction,
                        IconAtlas.GetPlanetaryOperationsFactionIconKey(faction));
                }).ToList());
        }

        private static int PresenceOrder(Faction faction) =>
            faction.IsDefaultFaction ? 0 : faction.IsPlayerFaction ? 1 : 2;
    }

    public static class RegionTerrainPresentation
    {
        public const int VariantCount = 6;

        public static int GetVariantIndex(Region region)
        {
            if (region == null) return 0;
            unchecked
            {
                uint hash = 2166136261;
                hash = (hash ^ (uint)region.Id) * 16777619;
                hash = (hash ^ (uint)region.Coordinates.X) * 16777619;
                hash = (hash ^ (uint)region.Coordinates.Y) * 16777619;
                foreach (char character in region.Name ?? string.Empty)
                {
                    hash = (hash ^ character) * 16777619;
                }
                return (int)(hash % VariantCount);
            }
        }

        public static string GetAssetPath(Region region) =>
            $"res://Assets/UI/RegionTerrain/terrain_{GetVariantIndex(region) + 1}.png";
    }
}
