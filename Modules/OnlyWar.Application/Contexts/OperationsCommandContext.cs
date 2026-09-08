using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Abstractions;
using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Orders;
using OnlyWar.Helpers.PlanetaryOperations;
using OnlyWar.Helpers.Readiness;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Operations.Abstractions;
using OnlyWar.Operations.Personnel;

namespace OnlyWar.Application;

/// <summary>
/// The command-side capabilities of Planetary Operations.
///
/// The mutation pipeline still operates on the live campaign entities internally, but the screen
/// service no longer receives the Sector, date, identity allocator, or personnel implementation as
/// a collection of loose arguments. This is the seam to replace with command-specific ports when
/// the underlying mutation helpers are ready for them.
/// </summary>
internal sealed class OperationsCommandContext
{
    private readonly Sector _sector;
    private readonly MedicalDetachmentService _medicalDetachments;
    private readonly Date _currentDate;
    private readonly IPersistentIdAllocator _identity;
    private readonly IReadinessDecisions _readiness;
    private readonly IOperationsPersonnelSurface _personnel;
    private readonly IPersonnelAvailabilityQueries _personnelQueries;

    internal OperationsCommandContext(
        Sector sector,
        Date currentDate,
        IPersistentIdAllocator identity,
        IReadinessDecisions readiness,
        IOperationsPersonnelSurface personnel,
        IPersonnelAvailabilityQueries personnelQueries)
    {
        _sector = sector ?? throw new ArgumentNullException(nameof(sector));
        _currentDate = currentDate ?? throw new ArgumentNullException(nameof(currentDate));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        _personnel = personnel ?? throw new ArgumentNullException(nameof(personnel));
        _personnelQueries = personnelQueries
            ?? throw new ArgumentNullException(nameof(personnelQueries));
        _medicalDetachments = new MedicalDetachmentService(_personnel);
    }

    internal RegionFaction PlayerPresence(Region region)
    {
        if (region == null || _sector.PlayerForce?.Faction == null) return null;
        region.RegionFactionMap.TryGetValue(
            _sector.PlayerForce.Faction.Id, out RegionFaction presence);
        return presence;
    }

    internal IEnumerable<Squad> OrbitingSquads(Planet planet) =>
        planet == null || _sector.PlayerForce?.Faction == null
            ? []
            : PlanetForceMovementService
                .GetOrbitingPlayerShips(planet, _sector.PlayerForce.Faction)
                .SelectMany(ship => ship.LoadedSquads);

    internal Ship FindOrbitingShip(Planet planet, int shipId) =>
        planet == null || _sector.PlayerForce?.Faction == null
            ? null
            : PlanetForceMovementService
                .GetOrbitingPlayerShips(planet, _sector.PlayerForce.Faction)
                .FirstOrDefault(ship => ship.Id == shipId);

    internal OrderMutationResult CreateOrAdd(
        Region region,
        AvailableMission mission,
        IReadOnlyList<Squad> squads,
        IReadOnlyList<PlayerSoldier> characters,
        int targetFactionId,
        Aggression aggression) =>
        OrderMutationService.CreateOrAdd(
            _sector,
            region,
            mission,
            squads,
            characters,
            targetFactionId,
            aggression,
            _readiness,
            _currentDate,
            _personnelQueries,
            _identity);

    internal OrderMutationResult RemoveSquad(Order order, Squad squad) =>
        OrderMutationService.RemoveSquad(_sector, order, squad);

    internal OrderRestoreToken CaptureCancellationUndo(Order order) =>
        OrderMutationService.CaptureCancellationUndo(_sector, order);

    internal OrderMutationResult Cancel(Order order) =>
        OrderMutationService.Cancel(_sector, order);

    internal OrderMutationResult Restore(OrderRestoreToken token) =>
        OrderMutationService.Restore(
            _sector, token, _readiness, _currentDate, _personnelQueries);

    internal OrderMutationResult RestoreSquad(Order order, Squad squad) =>
        OrderMutationService.RestoreSquad(
            _sector, order, squad, _readiness, _currentDate, _personnelQueries);

    internal OrderMutationResult SetAggression(Order order, Aggression aggression) =>
        OrderMutationService.SetAggression(_sector, order, aggression);

    internal OrderMutationResult AttachSpecialist(Order order, PlayerSoldier soldier) =>
        OrderMutationService.AttachSpecialist(
            _sector, order, soldier, _readiness, _currentDate, _personnelQueries);

    internal OrderMutationResult DetachSpecialist(Order order, PlayerSoldier soldier) =>
        OrderMutationService.DetachSpecialist(_sector, order, soldier);

    internal OrderMutationResult DetachSpecialists(
        Order order, IReadOnlyList<PlayerSoldier> characters)
    {
        IReadOnlyList<PlayerSoldier> selected = characters ?? [];
        int removed = selected
            .Count(character => DetachSpecialist(order, character).Succeeded);
        return new OrderMutationResult(
            removed == selected.Count,
            removed == selected.Count
                ? "Characters removed." : "Some characters could not be removed.",
            OrderMutationKind.SpecialistDetached,
            order,
            ReleasedSpecialists: removed);
    }

    internal OrderMutationResult RemoveMany(Order order, IReadOnlyList<Squad> squads)
    {
        OrderMutationResult last = new(true, "Change undone.", Order: order);
        foreach (Squad squad in (squads ?? [])
            .Where(squad => ReferenceEquals(squad.CurrentOrders, order)).ToList())
        {
            last = RemoveSquad(order, squad);
        }
        return last;
    }

    internal List<Squad> ResolveEligibleSquads(
        Region region,
        AvailableMission mission,
        Order context,
        IReadOnlyList<int> squadIds)
    {
        if (squadIds == null || squadIds.Count == 0) return [];
        RegionalEligibilityResult eligibility = RegionalOrderEligibilityService.Build(
            _sector, region, _readiness, mission, context);
        return eligibility.Groups.SelectMany(group => group.Candidates)
            .Concat(eligibility.Excluded)
            .Where(candidate => candidate.Exclusion == SquadEligibilityExclusion.None
                && !candidate.IsAssignedToContext
                && SpecialistAvailability.IsMissionSquadFormation(candidate.Squad)
                && squadIds.Contains(candidate.Squad.Id))
            .DistinctBy(candidate => candidate.Squad.Id)
            .Select(candidate => candidate.Squad)
            .ToList();
    }

    internal ForceMovementResult Land(
        Planet planet,
        Region destination,
        MovementParty party) =>
        PlanetForceMovementService.Land(
            _sector, planet, destination, party, _currentDate, _personnel);

    internal ForceMovementResult Embark(
        Planet planet,
        Region source,
        Ship destination,
        MovementParty party) =>
        PlanetForceMovementService.Embark(
            _sector, planet, source, destination, party, _currentDate, _personnel);

    internal MedicalDetachmentResult DetachCasualties(
        Planet planet,
        Region source,
        Ship destination,
        IReadOnlyList<PlayerSoldier> casualties) =>
        _medicalDetachments.DetachToOrbit(
            _sector, planet, source, destination, casualties, _currentDate);
}
