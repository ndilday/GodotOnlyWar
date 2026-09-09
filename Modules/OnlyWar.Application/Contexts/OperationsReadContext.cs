using OnlyWar.Domain;
using OnlyWar.Domain.Missions;
using OnlyWar.Operations.Orders;
using OnlyWar.Operations.Planetary;
using OnlyWar.Medical.Readiness;
using OnlyWar.Domain;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Recruitment;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Operations.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Application;

/// <summary>
/// The live facts needed by the Planetary Operations read surface.
///
/// The application lifetime creates this from the installed session. The full campaign aggregate
/// is intentionally private here: Operations queries can resolve the world, force and order facts
/// they need without receiving a general-purpose <see cref="Sector"/> handle. The returned domain
/// entities are implementation inputs for the projector and never cross the UI contracts.
/// </summary>
internal sealed class OperationsReadContext
{
    private readonly Sector _sector;

    internal Date CurrentDate { get; }
    internal int CurrentWeek => CurrentDate?.GetTotalWeeks() ?? 0;
    internal IReadinessDecisions Readiness { get; }
    internal IPersonnelAvailabilityQueries Personnel { get; }

    internal Faction PlayerFaction => _sector?.PlayerForce?.Faction;
    internal RecruitmentProgram RecruitmentProgram =>
        _sector?.PlayerForce?.RecruitmentProgram;
    internal ushort GeneseedStockpile => _sector?.PlayerForce?.GeneseedStockpile ?? 0;
    internal float GeneseedPurity => _sector?.PlayerForce?.GeneseedPurity ?? 0;
    internal ForceTreeInputs TreeInputs => new(
        _sector?.PlayerForce?.RecruitmentProgram,
        _sector?.PlayerForce?.Army?.ChapterOperationalDoctrine);

    internal IEnumerable<Order> Orders =>
        _sector?.Orders?.Values ?? Enumerable.Empty<Order>();

    internal IEnumerable<PlayerSoldier> PlayerSoldiers =>
        _sector?.PlayerForce?.Army?.PlayerSoldierMap?.Values
            ?? Enumerable.Empty<PlayerSoldier>();

    internal OperationsReadContext(
        Sector sector,
        Date currentDate,
        IReadinessDecisions readiness,
        IPersonnelAvailabilityQueries personnel)
    {
        _sector = sector ?? throw new ArgumentNullException(nameof(sector));
        CurrentDate = currentDate ?? throw new ArgumentNullException(nameof(currentDate));
        Readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        Personnel = personnel ?? throw new ArgumentNullException(nameof(personnel));
    }

    internal Planet FindPlanet(int id) => _sector.Planets.GetValueOrDefault(id);

    internal Region FindRegion(int id) => _sector.Planets.Values
        .SelectMany(planet => planet.Regions)
        .FirstOrDefault(region => region?.Id == id);

    internal Order FindOrder(int? id) => id is int value
        ? _sector.Orders.Values.FirstOrDefault(order => order.Id == value)
        : null;

    internal PlayerSoldier FindPlayerSoldier(int id) =>
        _sector.PlayerForce?.Army?.PlayerSoldierMap?.GetValueOrDefault(id);

    internal RegionFaction PlayerPresence(Region region)
    {
        if (region == null || PlayerFaction == null) return null;
        region.RegionFactionMap.TryGetValue(PlayerFaction.Id, out RegionFaction presence);
        return presence;
    }

    internal IReadOnlyList<Ship> OrbitingShips(Planet planet) =>
        planet == null || PlayerFaction == null
            ? []
            : PlanetForceMovementService.GetOrbitingPlayerShips(planet, PlayerFaction);

    internal Ship FindOrbitingShip(Planet planet, int shipId) =>
        OrbitingShips(planet).FirstOrDefault(ship => ship.Id == shipId);

    internal RegionalEligibilityResult BuildEligibility(
        Region region,
        AvailableMission mission,
        Order contextOrder) =>
        RegionalOrderEligibilityService.Build(
            _sector, region, Readiness, mission, contextOrder);

    internal Order FindEquivalentOrder(
        Region region,
        AvailableMission mission,
        int targetFactionId) =>
        OrderMutationService.FindEquivalentOrder(
            _sector, region, mission, targetFactionId);
}
