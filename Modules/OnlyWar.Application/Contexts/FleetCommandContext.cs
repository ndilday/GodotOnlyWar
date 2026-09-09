using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Campaign.Fleets;
using OnlyWar.Domain;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Recruitment;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;

namespace OnlyWar.Application;

/// <summary>
/// The Classis facts and commands required by the fleet screen.
///
/// Route geometry, fleet ownership and fleet reorganization all need live campaign data, but the
/// screen service should not know that those facts happen to live under Sector or GameRulesData.
/// </summary>
internal sealed class FleetCommandContext
{
    private readonly Sector _sector;
    private readonly ushort _maxSubsectorDiameter;
    private readonly IPersistentIdAllocator _identity;

    internal Faction PlayerFaction => _sector.PlayerForce?.Faction;
    internal RecruitmentProgram RecruitmentProgram =>
        _sector.PlayerForce?.RecruitmentProgram;
    internal ChapterOperationalDoctrine OperationalDoctrine =>
        _sector.PlayerForce?.Army?.OperationalDoctrine;
    internal IEnumerable<TaskForce> TaskForces => _sector.Fleets.Values;
    internal IEnumerable<Planet> Planets => _sector.Planets.Values;

    internal FleetCommandContext(
        Sector sector,
        GameRulesData rules,
        IPersistentIdAllocator identity)
    {
        _sector = sector ?? throw new ArgumentNullException(nameof(sector));
        _maxSubsectorDiameter = rules?.SectorGenerationProfile?.MaxSubsectorDiameter
            ?? throw new ArgumentNullException(nameof(rules));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
    }

    internal TaskForce FindTaskForce(int id) => _sector.Fleets.GetValueOrDefault(id);

    internal Planet FindPlanet(int id) => _sector.Planets.GetValueOrDefault(id);

    internal Ship FindShip(int shipId) => _sector.Fleets.Values
        .SelectMany(fleet => fleet.Ships)
        .FirstOrDefault(ship => ship.Id == shipId);

    internal Squad FindLoadedSquad(int squadId) => _sector.Fleets.Values
        .SelectMany(fleet => fleet.Ships)
        .SelectMany(ship => ship.LoadedSquads)
        .FirstOrDefault(squad => squad.Id == squadId);

    internal FleetRoute CalculateRoute(TaskForce origin, Planet destination)
    {
        if (origin?.Planet == null || destination == null || origin.Planet == destination)
        {
            return null;
        }

        FleetRouteScope scope = FleetRouteCalculator.DetermineScope(
            origin.Planet, destination, _maxSubsectorDiameter);
        return new FleetRouteCalculator().CalculateBestRoute(
            origin.Planet, destination, _sector.WarpLanes, scope);
    }

    internal void Split(TaskForce original, IReadOnlyCollection<Ship> ships) =>
        _sector.SplitOffNewFleet(original, ships, _identity);

    internal void Combine(TaskForce remaining, TaskForce merging) =>
        _sector.CombineFleets(remaining, merging);
}
