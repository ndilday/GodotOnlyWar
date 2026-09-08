using OnlyWar.Helpers.PlanetaryOperations;
using OnlyWar.Models.Orders;
using System.Collections.Generic;

namespace OnlyWar.Application;

/// <summary>
/// Detached Planetary Operations projections. Every reference to the campaign is an ID or already
/// formatted text: the screen can render and re-issue commands from these values alone, and holds
/// nothing it could mutate. Colour and texture selection stay in the host, which resolves the
/// <see cref="UiAccent"/> and icon keys carried here.
/// </summary>
public sealed record LabeledValue(string Label, string Value);

public sealed record DossierCardView(
    string Title,
    string Subtitle,
    IReadOnlyList<LabeledValue> Rows,
    UiAccent Accent,
    int? AccentFactionArgb = null,
    float? BarFraction = null);

public sealed record OperationsHeaderView(
    string PlanetName,
    int ImperialRegions,
    int TotalRegions,
    int Landed,
    int InOrbit,
    string RequestClock);

public sealed record RegionEnemyForceEstimate(string FactionName, string ForceEstimate);

public sealed record MapRegionCard(
    int RegionId,
    string Name,
    RegionControlState Control,
    int? ControlFactionId,
    string ControlFactionName,
    UiAccent ControlBorderAccent,
    int? ControlBorderFactionArgb,
    int PlayerSquads,
    int PlayerEffectiveStrength,
    int PlayerFullStrength,
    int ActiveOrders,
    int UnassignedSquads,
    int MissionOpportunities,
    IReadOnlyList<RegionEnemyForceEstimate> PublicEnemyForces,
    IReadOnlyList<RegionPresencePresentation> Presences,
    string OverlayText,
    string OverlayTooltip,
    string IntelConfidence,
    int TerrainVariant,
    bool HasPlayerForces,
    string FactionActivity = null,
    string FactionActivityIconKey = null,
    // Keyboard navigation across the map follows region adjacency, which is a campaign fact rather
    // than a screen layout. The projection names the neighbour in each direction so the map view
    // never walks the region graph itself.
    int? NorthRegionId = null,
    int? SouthRegionId = null,
    int? WestRegionId = null,
    int? EastRegionId = null)
{
    public int PlayerDutyReadyStrength => PlayerEffectiveStrength;
}

public sealed record PlanetMapProjection(
    IReadOnlyList<IReadOnlyList<MapRegionCard>> Rows,
    PlanetMapOverlay Overlay,
    int? SelectedFactionId,
    string OverlayLegend);

public sealed record WorldDossierView(
    IReadOnlyList<DossierCardView> ProfileCards,
    IReadOnlyList<DossierCardView> StrengthCards,
    IReadOnlyList<DossierCardView> SelectedRegionCards);

/// <summary>A mission the player may order from this region, identified by its stable key.</summary>
public sealed record MissionOptionView(
    string Key,
    string Label,
    string IconKey,
    string Tooltip,
    bool IsSpecial,
    string RecommendedForce = null,
    bool IsAlreadyActive = false);

public sealed record ActiveOrderView(
    int OrderId,
    string Label,
    int SquadCount,
    int CharacterCount,
    Aggression Aggression,
    string IconKey,
    string Tooltip);

public sealed record OrderParticipantView(int Id, string Name, string Tooltip = null);

/// <summary>The live-order editor's contents. Null when no order is selected.</summary>
public sealed record OrderEditorView(
    int OrderId,
    string Label,
    int SquadCount,
    int CharacterCount,
    Aggression Aggression,
    IReadOnlyList<OrderParticipantView> AssignedSquads,
    IReadOnlyList<OrderParticipantView> AssignedCharacters,
    IReadOnlyList<OrderParticipantView> AvailableSpecialists,
    bool HasSpecialistPool);

public sealed record ShipChoiceView(
    int ShipId,
    string Name,
    int FleetId,
    string FleetName,
    int CurrentPassengers,
    int SelectedPassengers,
    int ResultingPassengers,
    int Capacity,
    int Shortfall)
{
    public bool Fits => Shortfall == 0;
}

public sealed record CasualtyRowView(int SoldierId, string Name, string SquadName);

/// <summary>The Order workspace: region dossier, live orders, mission choices and the force tree.</summary>
public sealed record RegionalOperationsView(
    IReadOnlyList<DossierCardView> SelectedRegionCards,
    IReadOnlyList<ActiveOrderView> ActiveOrders,
    IReadOnlyList<MissionOptionView> OrdinaryMissions,
    IReadOnlyList<MissionOptionView> SpecialMissions,
    IReadOnlyList<HierarchyTreeItem> ForceTree,
    OrderEditorView Editor,
    int? SelectedOrderId,
    string SelectedMissionKey);

/// <summary>The Land/Embark workspace.</summary>
public sealed record MovementOperationsView(
    IReadOnlyList<DossierCardView> RegionCards,
    IReadOnlyList<HierarchyTreeItem> ForceTree,
    IReadOnlyList<ShipChoiceView> Ships,
    int? SelectedShipId,
    int SelectedCount,
    IReadOnlySet<int> ValidSquadIds,
    IReadOnlySet<int> ValidCharacterIds);

/// <summary>The Detach workspace.</summary>
public sealed record DetachOperationsView(
    IReadOnlyList<DossierCardView> RegionCards,
    IReadOnlyList<CasualtyRowView> Casualties,
    IReadOnlyList<ShipChoiceView> Ships,
    int? SelectedShipId,
    IReadOnlySet<int> ValidCasualtyIds);

/// <summary>Everything the screen needs for one refresh of the whole workspace.</summary>
public sealed record OperationsWorkspaceView(
    System.Guid SessionToken,
    bool Exists,
    OperationsHeaderView Header,
    PlanetMapProjection Map,
    int SelectedRegionId,
    PlanetaryOperationsVerb Verb,
    RegionalOperationsView Orders,
    MovementOperationsView Movement,
    DetachOperationsView Detach);

public sealed record OperationsWorkspaceQuery(
    int PlanetId,
    int RegionId,
    PlanetMapOverlay Overlay,
    int? FactionId,
    PlanetaryOperationsVerb Verb,
    string MissionKey,
    int? OrderId,
    string Filter,
    ForceTreeGrouping Grouping,
    IReadOnlySet<int> MovementSquadIds,
    IReadOnlySet<int> MovementCharacterIds,
    IReadOnlySet<int> CasualtyIds,
    int? SelectedShipId);

/// <summary>The selection a screen restores when it reopens a planet.</summary>
public sealed record OperationsEntryView(
    int PlanetId,
    int RegionId,
    int? FactionId,
    string MissionKey = null,
    int? OrderId = null);
