using System;
using OnlyWar.Models.Command;

namespace OnlyWar.Application;

/// <summary>
/// Where a navigation request actually lands, once the campaign has resolved it. A squad resolves
/// to the region it holds or to the Classis when it is still aboard ship; a mission or an order
/// resolves to the region it is fought in, or to its ordered squad when it has no ground.
/// </summary>
public enum CampaignNavigationRouteKind
{
    None,
    LastTurnReport,
    Recruitment,
    Fleet,
    SquadOnShip,
    SquadInRegion,
    Soldier,
    PlanetOperations,
    RegionOperations,
    Diplomacy,
    Apothecarium,
    SectorMap
}

public sealed record CampaignNavigationRoute(
    CampaignNavigationRouteKind Kind,
    int? PlanetId = null,
    int? RegionId = null,
    int? SquadId = null,
    int? SoldierId = null,
    int? FocusId = null)
{
    public static readonly CampaignNavigationRoute None =
        new(CampaignNavigationRouteKind.None);
}

/// <summary>
/// Resolves the ids a screen needs to navigate. Keeping this behind the boundary is what lets the
/// main scene stop resolving live planets, regions, missions, orders, squads and soldiers itself.
/// </summary>
public interface ICampaignNavigationApplication
{
    Guid SessionToken { get; }
    event EventHandler SessionChanged;

    CampaignNavigationRoute ResolveNavigation(
        CampaignNavigationTargetKind kind, int? primaryId);

    /// <summary>Where a squad can be opened: its region, its ship, or nowhere.</summary>
    CampaignNavigationRoute ResolveSquadLocation(int squadId);

    /// <summary>The world a region belongs to, for opening Planetary Operations on it.</summary>
    int? QueryRegionPlanet(int regionId);

    /// <summary>The world's name, for the Planetary Operations title.</summary>
    string QueryPlanetName(int planetId);
}
