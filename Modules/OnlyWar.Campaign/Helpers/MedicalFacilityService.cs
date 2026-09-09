using OnlyWar.Medical.Abstractions;
using MedicalFacilityPolicy = OnlyWar.Medical.Treatment.MedicalFacilityPolicy;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Planets;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Campaign
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
            MedicalFacilityPolicy.SupportsMajorSurgery(new MedicalFacilityFacts(
                IsShipFacility: true,
                HasSoldierCapacity: template?.SoldierCapacity > 0,
                WorldType: null,
                IsImperialControlled: false,
                PublicImperialPopulation: 0));

        public bool SupportsMajorSurgery(Region region)
        {
            if (region?.Planet?.Template == null)
            {
                return false;
            }
            List<RegionFaction> publicImperials = region.RegionFactionMap.Values
                .Where(state => state.IsPublic
                    && FactionRelationshipService.IsImperial(state.PlanetFaction.Faction))
                .ToList();
            bool imperialControlled = region.ControllingFaction != null
                && FactionRelationshipService.IsImperial(
                    region.ControllingFaction.PlanetFaction.Faction);
            return MedicalFacilityPolicy.SupportsMajorSurgery(new MedicalFacilityFacts(
                IsShipFacility: false,
                HasSoldierCapacity: false,
                WorldType: region.Planet.Template.Name,
                IsImperialControlled: imperialControlled,
                PublicImperialPopulation: publicImperials.Sum(state => state.Population)));
        }

        public bool SupportsMajorSurgery(OnlyWar.Domain.CampaignLocation location) =>
            location?.Ship != null
                ? SupportsMajorSurgery(location.Ship.Template)
                : SupportsMajorSurgery(location?.Region);
    }
}
