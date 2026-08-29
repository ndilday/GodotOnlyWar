using OnlyWar.Models;
using OnlyWar.Helpers.Recruitment;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    public enum CareDestinationState
    {
        Ready = 0,
        Resolvable = 1,
        Ineligible = 2
    }

    public sealed record CareDestinationReason(string Code, string Message, bool IsResolvable);

    public sealed record CareDestinationCandidate(
        CampaignLocation Location,
        string Name,
        string SiteType,
        CareDestinationState State,
        int RequiredBerths,
        int AvailableBerths,
        PlayerSoldier Apothecary,
        PlayerSoldier Techmarine,
        IReadOnlyList<CareDestinationReason> Reasons);

    public sealed class CareDestinationService
    {
        private readonly MedicalFacilityService _facilities = new();

        public IReadOnlyList<CareDestinationCandidate> Enumerate(
            PlayerForce force,
            IEnumerable<Planet> planets,
            PlayerSoldier patient,
            ReplacementOption option)
        {
            IEnumerable<Ship> ships = force?.Fleet?.TaskForces?.SelectMany(fleet => fleet.Ships)
                ?? [];
            IEnumerable<Region> regions = (planets ?? [])
                .Where(planet => planet != null)
                .SelectMany(planet => planet.Regions)
                .Where(region => region != null);
            return ships.Select(ship => Evaluate(force, patient, option, CampaignLocation.Aboard(ship)))
                .Concat(regions.Select(region => Evaluate(
                    force, patient, option, CampaignLocation.Landed(region))))
                .OrderBy(candidate => candidate.State)
                .ThenBy(candidate => candidate.SiteType)
                .ThenBy(candidate => candidate.Name)
                .ToList();
        }

        public CareDestinationCandidate Evaluate(
            PlayerForce force,
            PlayerSoldier patient,
            ReplacementOption option,
            CampaignLocation location)
        {
            List<CareDestinationReason> reasons = [];
            bool intrinsic = _facilities.SupportsMajorSurgery(location);
            if (!intrinsic)
            {
                reasons.Add(new("facility", "Site cannot support major surgery.", false));
            }
            if (location?.Ship?.Fleet?.TravelPhase == FleetTravelPhase.InWarp)
            {
                reasons.Add(new("route", "Ship is currently in the Warp.", false));
            }

            IReadOnlyList<PlayerSoldier> roster = force?.Army?.PlayerSoldierMap?.Values.ToList() ?? [];
            PlayerSoldier apothecary = FindStaff(roster, location, MedicalProcedureService.IsApothecary);
            PlayerSoldier techmarine = FindStaff(roster, location, MedicalProcedureService.IsTechmarine);
            bool movableApothecary = apothecary != null || roster.Any(member =>
                CanMoveStaff(member, MedicalProcedureService.IsApothecary));
            bool movableTechmarine = techmarine != null || roster.Any(member =>
                CanMoveStaff(member, MedicalProcedureService.IsTechmarine));
            if (apothecary == null)
            {
                reasons.Add(new("apothecary", "No fit, unreserved Apothecary is present.", movableApothecary));
            }
            if (techmarine == null)
            {
                reasons.Add(new("techmarine", "No fit, unreserved Techmarine is present.", movableTechmarine));
            }

            int requiredBerths = location?.Ship != null
                && !CampaignLocationService.ForSoldier(patient)?.IsSamePlace(location) == true ? 1 : 0;
            int availableBerths = location?.Ship?.AvailableCapacity ?? 0;
            if (requiredBerths > availableBerths)
            {
                bool canRebalance = location.Ship.Fleet?.Ships.Any(ship =>
                    !ReferenceEquals(ship, location.Ship) && ship.AvailableCapacity > 0) == true;
                reasons.Add(new("capacity", "No passenger berth is currently available.", canRebalance));
            }
            if ((force?.Army?.Requisition ?? 0) < (option?.RequisitionCost ?? 0))
            {
                reasons.Add(new("requisition", "Insufficient requisition.", false));
            }

            CareDestinationState state = reasons.Count == 0
                ? CareDestinationState.Ready
                : reasons.All(reason => reason.IsResolvable)
                    ? CareDestinationState.Resolvable
                    : CareDestinationState.Ineligible;
            return new CareDestinationCandidate(
                location,
                location?.Ship?.Name ?? location?.Region?.Name ?? "Unknown site",
                location?.Ship != null ? "Ship" : "Region",
                state,
                requiredBerths,
                availableBerths,
                apothecary,
                techmarine,
                reasons);
        }

        private static PlayerSoldier FindStaff(
            IEnumerable<PlayerSoldier> roster,
            CampaignLocation location,
            System.Func<ISoldier, bool> role) => roster.FirstOrDefault(member =>
                IsAvailableStaff(member, role)
                && CampaignLocationService.ForSoldier(member)?.IsSamePlace(location) == true);

        private static bool IsAvailableStaff(
            PlayerSoldier soldier,
            System.Func<ISoldier, bool> role) => soldier?.IsCombatEffective == true
                && role(soldier)
                && !RecruitmentPromotionService.IsReservedForProcedure(
                    GameDataSingleton.Instance?.Sector?.PlayerForce?.RecruitmentProgram,
                    soldier.Id);

        private static bool CanMoveStaff(
            PlayerSoldier soldier,
            System.Func<ISoldier, bool> role) => IsAvailableStaff(soldier, role)
                && soldier.AssignedSquad?.PermitsIndividualDeployment == true;
    }
}
