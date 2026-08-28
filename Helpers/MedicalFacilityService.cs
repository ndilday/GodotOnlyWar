using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    public static class MedicalFacilityRules
    {
        public const long MinimumImperialPopulation = 1_000_000;
        public static readonly IReadOnlySet<string> SurgeryWorldTypes =
            new HashSet<string> { "Hive", "Forge", "Civilised" };
    }

    public sealed class MedicalFacilityService
    {
        public bool SupportsMajorSurgery(ShipTemplate template) =>
            template?.SoldierCapacity > 0;

        public bool SupportsMajorSurgery(Region region)
        {
            if (region?.Planet?.Template == null
                || !MedicalFacilityRules.SurgeryWorldTypes.Contains(region.Planet.Template.Name))
            {
                return false;
            }
            List<RegionFaction> publicImperials = region.RegionFactionMap.Values
                .Where(state => state.IsPublic
                    && FactionRelationshipService.IsImperial(state.PlanetFaction.Faction))
                .ToList();
            return publicImperials.Count > 0
                && publicImperials.Sum(state => state.Population)
                    >= MedicalFacilityRules.MinimumImperialPopulation
                && region.ControllingFaction != null
                && FactionRelationshipService.IsImperial(
                    region.ControllingFaction.PlanetFaction.Faction);
        }

        public bool SupportsMajorSurgery(Models.CampaignLocation location) =>
            location?.Ship != null
                ? SupportsMajorSurgery(location.Ship.Template)
                : SupportsMajorSurgery(location?.Region);
    }
}
