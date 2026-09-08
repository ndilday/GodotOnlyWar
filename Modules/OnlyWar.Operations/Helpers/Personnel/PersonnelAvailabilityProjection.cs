using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Readiness;
using OnlyWar.Models;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Operations.Abstractions;

namespace OnlyWar.Operations.Personnel;

/// <summary>
/// Projects live Campaign objects into the request records consumed by the personnel query port.
/// This keeps aggregate traversal at the application/operations edge rather than in the shared
/// contract itself.
/// </summary>
public static class PersonnelAvailabilityProjection
{
    public static PersonnelMovementRequest ForMovement(
        PlayerSoldier character,
        CampaignLocation destination,
        ChapterOperationalDoctrine doctrine = null,
        RecruitmentProgram program = null) =>
        new(
            ForCharacter(character, doctrine, program),
            ForLocation(destination));

    public static PersonnelOrderAssignmentRequest ForOrderAssignment(
        PlayerSoldier character,
        Order order,
        Region explicitOrigin = null,
        IReadOnlyList<Squad> stagingSquads = null,
        ChapterOperationalDoctrine doctrine = null,
        RecruitmentProgram program = null) =>
        new(
            ForCharacter(character, doctrine, program),
            order?.Id,
            ForRegion(explicitOrigin),
            (stagingSquads ?? Array.Empty<Squad>())
                .Select(squad => ForLocation(CampaignLocationService.ForSquad(squad)))
                .Where(location => location != null)
                .ToArray(),
            ForRegion(order?.Mission?.RegionFaction?.Region));

    public static PersonnelFormationSnapshot ForFormation(Squad squad) =>
        new(
            squad?.Id ?? 0,
            squad?.Members?.Count(member =>
                member is not PlayerSoldier player || player.IndividualPosting == null) ?? 0);

    public static PersonnelCharacterSnapshot ForCharacter(
        PlayerSoldier character,
        ChapterOperationalDoctrine doctrine = null,
        RecruitmentProgram program = null) =>
        character == null
            ? null
            : new(
                character.Id,
                character.Name,
                character.AssignedSquad?.PermitsIndividualDeployment == true,
                character.CurrentOrder?.Id,
                ForLocation(CampaignLocationService.ForSoldier(character)),
                character.ToDutyReadinessFacts(program),
                new DutyReadinessPolicyOptions(
                    doctrine?.InjuryThreshold,
                    doctrine?.RequireDutyReadySquadLeader ?? false,
                    doctrine?.MinimumDutyReadySquadStrength ?? 0));

    private static PersonnelLocationSnapshot ForRegion(Region region) =>
        region == null
            ? null
            : new(
                RegionId: region.Id,
                AdjacentRegionIds: region.GetAdjacentRegions()
                    .Where(adjacent => adjacent != null)
                    .Select(adjacent => adjacent.Id)
                    .ToArray());

    private static PersonnelLocationSnapshot ForLocation(CampaignLocation location)
    {
        if (location?.Ship != null)
        {
            return new PersonnelLocationSnapshot(
                ShipId: location.Ship.Id,
                IsInWarp: location.Ship.Fleet?.TravelPhase
                    == Models.Fleets.FleetTravelPhase.InWarp);
        }
        return ForRegion(location?.Region);
    }
}
