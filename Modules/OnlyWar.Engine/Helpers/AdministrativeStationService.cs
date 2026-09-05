using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    public sealed record AdministrativeStationResult(
        bool Succeeded,
        string Message,
        int FormationCount = 0)
    {
        public static AdministrativeStationResult Failure(string message) =>
            new(false, message);
    }

    /// <summary>
    /// Sole mutation boundary for administrative duty stations and the ship station manifest.
    /// Posted characters are deliberately untouched when a station relocates.
    /// </summary>
    public sealed class AdministrativeStationService
    {
        private readonly FlagshipService _flagships = new();
        private readonly IndividualPostingService _postings = new();
        public AdministrativeStationResult SeatFormation(
            Squad formation,
            CampaignLocation station)
        {
            if (formation?.PermitsIndividualDeployment != true)
            {
                return AdministrativeStationResult.Failure(
                    "Only MembersOnly administrative formations have duty stations.");
            }
            if (station == null || station.IsShip == station.IsRegion)
            {
                return AdministrativeStationResult.Failure(
                    "A duty station must be exactly one ship or one region.");
            }

            if (formation.DutyStation?.IsSamePlace(station) == true)
            {
                ClearOperationalPresence(formation);
                return new AdministrativeStationResult(true, "Formation already seated.", 1);
            }

            if (station.Ship != null)
            {
                int oldStationed = formation.DutyStation?.Ship == station.Ship
                    ? SoldierPresenceService.PresentCount(formation)
                    : 0;
                if (ShipCapacityService.AvailableCapacity(station.Ship) + oldStationed
                    < SoldierPresenceService.PresentCount(formation))
                {
                    return AdministrativeStationResult.Failure(
                        $"{station.Ship.Name} has insufficient capacity for the administrative station.");
                }
            }

            RemoveFromCurrentStation(formation);
            formation.DutyStation = station;
            if (station.Ship != null)
            {
                station.Ship.StationAdministrativeFormation(formation);
            }
            NormalizeMembers(formation);
            return new AdministrativeStationResult(true, "Administrative formation seated.", 1);
        }

        public AdministrativeStationResult SeatAll(
            Unit chapter,
            Ship flagship)
        {
            if (chapter == null || flagship == null)
            {
                return AdministrativeStationResult.Failure("A chapter and flagship are required.");
            }
            List<Squad> formations = chapter.GetAllSquads()
                .Where(squad => squad.PermitsIndividualDeployment)
                .OrderBy(squad => squad.Id)
                .ToList();
            return SeatFormations(formations, CampaignLocation.Aboard(flagship));
        }

        public AdministrativeStationResult MoveAllToRegion(Unit chapter, Region region)
        {
            if (chapter == null || region == null)
            {
                return AdministrativeStationResult.Failure("A chapter and region are required.");
            }
            return SeatFormations(
                chapter.GetAllSquads().Where(squad => squad.PermitsIndividualDeployment)
                    .OrderBy(squad => squad.Id).ToList(),
                CampaignLocation.Landed(region));
        }

        public AdministrativeStationResult MoveAllToFlagship(Unit chapter, Ship flagship)
        {
            if (chapter == null || flagship == null)
            {
                return AdministrativeStationResult.Failure("A chapter and flagship are required.");
            }
            return SeatFormations(
                chapter.GetAllSquads().Where(squad => squad.PermitsIndividualDeployment)
                    .OrderBy(squad => squad.Id).ToList(),
                CampaignLocation.Aboard(flagship));
        }

        public AdministrativeStationResult RelocateAfterFlagshipDestruction(
            Unit chapter,
            Ship destroyedShip,
            IEnumerable<Ship> survivingShips)
        {
            if (chapter == null || destroyedShip == null)
            {
                return AdministrativeStationResult.Failure("A chapter and destroyed ship are required.");
            }
            Ship successor;
            try
            {
                successor = _flagships.FindSuccessor(
                    chapter.Faction,
                    (survivingShips ?? Enumerable.Empty<Ship>())
                        .Where(ship => ship != null && ship != destroyedShip));
            }
            catch (InvalidOperationException exception)
            {
                return AdministrativeStationResult.Failure(exception.Message);
            }
            List<Squad> stranded = destroyedShip.AdministrativeStations
                .Where(squad => squad.PermitsIndividualDeployment)
                .ToList();
            int incoming = stranded.Sum(SoldierPresenceService.PresentCount);
            if (ShipCapacityService.AvailableCapacity(successor) < incoming)
            {
                return AdministrativeStationResult.Failure(
                    $"{successor.Name} cannot seat all administrative survivors atomically.");
            }
            _flagships.SetFlagship(
                chapter.Faction,
                (survivingShips ?? Enumerable.Empty<Ship>()).Append(destroyedShip),
                successor);
            return SeatFormations(stranded, CampaignLocation.Aboard(successor));
        }

        private AdministrativeStationResult SeatFormations(
            IReadOnlyList<Squad> formations,
            CampaignLocation destination)
        {
            List<Squad> distinct = (formations ?? [])
                .Where(squad => squad != null)
                .DistinctBy(squad => squad.Id)
                .ToList();
            if (distinct.Count == 0)
            {
                return new AdministrativeStationResult(true, "No administrative formations to relocate.");
            }

            if (destination.Ship != null)
            {
                int incoming = distinct
                    .Where(squad => squad.DutyStation?.Ship != destination.Ship)
                    .Sum(SoldierPresenceService.PresentCount);
                if (ShipCapacityService.AvailableCapacity(destination.Ship) < incoming)
                {
                    return AdministrativeStationResult.Failure(
                        $"{destination.Ship.Name} cannot seat all administrative formations atomically.");
                }
            }

            foreach (Squad formation in distinct)
            {
                RemoveFromCurrentStation(formation);
            }
            foreach (Squad formation in distinct)
            {
                formation.DutyStation = destination;
                if (destination.Ship != null)
                {
                    destination.Ship.StationAdministrativeFormation(formation);
                }
                NormalizeMembers(formation);
            }
            return new AdministrativeStationResult(
                true,
                $"Relocated {distinct.Count} administrative formation{(distinct.Count == 1 ? "" : "s")}.",
                distinct.Count);
        }

        private static void RemoveFromCurrentStation(Squad formation)
        {
            formation.DutyStation?.Ship?.RemoveAdministrativeFormation(formation);
            ClearOperationalPresence(formation);
        }

        private static void ClearOperationalPresence(Squad formation)
        {
            formation.BoardedLocation?.RemoveSquad(formation);
            formation.BoardedLocation = null;
            if (formation.CurrentRegion != null
                && formation.Faction != null
                && formation.CurrentRegion.RegionFactionMap.TryGetValue(
                    formation.Faction.Id, out RegionFaction regionFaction))
            {
                regionFaction.LandedSquads.Remove(formation);
            }
            formation.CurrentRegion = null;
        }

        private void NormalizeMembers(Squad formation)
        {
            foreach (PlayerSoldier member in formation.Members.OfType<PlayerSoldier>())
            {
                _postings.NormalizeReunion(member);
            }
        }
    }
}
