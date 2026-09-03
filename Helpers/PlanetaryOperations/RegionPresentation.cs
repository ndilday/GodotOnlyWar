using OnlyWar.Helpers;
using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Models.FactionBehaviors;
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
                .ThenBy(presence => PresenceName(presence.PlanetFaction.Faction))
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
                        PresenceName(faction),
                        FactionRelationshipService.IsImperial(faction),
                        faction.IsPlayerFaction,
                        IconAtlas.GetPlanetaryOperationsFactionIconKey(faction));
                }).ToList());
        }

        // The default Imperial presence is always first. Every other disclosed faction is
        // alphabetical via the ThenBy in Build; strength and player ownership never reorder the
        // badges, because either would leak or distort the regional picture.
        private static int PresenceOrder(Faction faction) =>
            faction.IsDefaultFaction ? 0 : 1;

        private static string PresenceName(Faction faction) =>
            faction.IsPlayerFaction ? "Chapter" : faction.Name;
    }

    /// <summary>
    public sealed record FactionActivityPresentationModel(
        Faction Faction,
        string Text,
        string IconKey);

    /// Belief-gated dormant/invasion activity text shared by the map card and the selected-region dossier.
    /// Ground truth is used only after the presence is already open; hidden feral activity is
    /// disclosed solely through the observer's target-specific intelligence belief.
    /// </summary>
    public static class FactionActivityPresentation
    {
        public static string Build(Region region)
        {
            return BuildDetails(region)?.Text;
        }

        public static FactionActivityPresentationModel BuildDetails(Region region)
        {
            RegionFaction dormantPresence = region?.RegionFactionMap.Values
                .Where(presence => FactionCapabilities.HasDormantPopulations(
                    presence?.PlanetFaction?.Faction))
                .OrderBy(presence => presence.PlanetFaction.Faction.Id)
                .FirstOrDefault();
            if (dormantPresence == null) return null;

            FactionIntelBelief belief = IntelligenceTargetService.GetBestPlayerVisibleBelief(
                region,
                dormantPresence.PlanetFaction.Faction);
            if (!dormantPresence.IsOpenlyActive && !(belief?.Level >= IntelLevel.Confirmed))
            {
                return null;
            }

            string confidence = dormantPresence.IsOpenlyActive
                ? "Open activity"
                : $"{belief.Level} intelligence";
            Faction faction = dormantPresence.PlanetFaction.Faction;
            string text = dormantPresence.StrategicInvasionForceId.HasValue
                ? $"Invasion force · {confidence}"
                : $"Dormant population · {confidence}";
            return new FactionActivityPresentationModel(
                faction,
                text,
                IconAtlas.GetPlanetaryOperationsFactionIconKey(faction));
        }

        public static string GetIconKey(Region region) => BuildDetails(region)?.IconKey;
    }

    [System.Obsolete("Use FactionActivityPresentation.")]
    public static class OrkActivityPresentation
    {
        public static string Build(Region region) => FactionActivityPresentation.Build(region);

        public static bool IsOrkFaction(Faction faction) =>
            FactionCapabilities.HasDormantPopulations(faction);
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
