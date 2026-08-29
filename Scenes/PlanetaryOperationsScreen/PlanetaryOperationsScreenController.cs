using Godot;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.UI;
using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Orders;
using OnlyWar.Helpers.PlanetaryOperations;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class PlanetaryOperationsScreenController : DialogController
{
    private readonly MedicalDetachmentService _medicalDetachments = new();
    private PlanetaryOperationsScreenView _view;
    private Planet _planet;
    private Region _selectedRegion;
    private PlanetMapOverlay _overlay = PlanetMapOverlay.Control;
    private int? _selectedFactionId;
    private AvailableMission _selectedMission;
    private Order _selectedOrder;
    private PlanetaryOperationsVerb _verb = PlanetaryOperationsVerb.Order;
    private ForceTreeGrouping _grouping = ForceTreeGrouping.Company;
    private string _filter = "";
    private readonly HashSet<int> _movementSquadIds = [];
    private readonly HashSet<int> _movementCharacterIds = [];
    private readonly HashSet<int> _casualtyIds = [];
    private int? _selectedShipId;
    private ConfirmationDialog _confirmation;
    private Action _pendingConfirmedAction;
    private Action _undoLast;
    private string _undoDescription;

    public event EventHandler<Squad> SquadDoubleClicked;
    public event EventHandler<Planet> FleetManagementRequested;
    public event EventHandler<PlayerSoldier> RecoveryOperationsRequested;
    public event EventHandler CampaignChanged;

    public PlanetaryOperationsNavigationState NavigationState => new(
        _planet?.Id ?? -1, _selectedRegion?.Id ?? -1, _overlay,
        _selectedFactionId, _selectedOrder?.Id, _selectedMission?.IdentityKey, _verb);
    public ulong MapInstanceId => _view?.MapInstanceId ?? 0;

    public override void _Ready()
    {
        base._Ready();
        ColorRect scrim = GetNodeOrNull<ColorRect>("DialogView/ModalScrim");
        if (scrim != null) scrim.Color = new Color(0.004f, 0.005f, 0.006f, 1f);
        _view = GetNode<PlanetaryOperationsScreenView>("DialogView/PlanetaryOperationsScreenView");
        _view.RegionSelected += OnRegionSelected;
        _view.RegionActivated += OnRegionActivated;
        _view.VerbSelected += OnVerbSelected;
        _view.ForceNodePressed += OnForceNodePressed;
        _view.ForceNodeActivated += OnForceNodeActivated;
        _view.ForceFilterChanged += (_, value) => { _filter = value ?? ""; RefreshWorkspace(); };
        _view.GroupingChanged += (_, value) => { _grouping = value; RefreshWorkspace(); };
        _view.MissionSelected += OnMissionSelected;
        _view.OrderSelected += OnOrderSelected;
        _view.RemoveSquadRequested += OnRemoveSquadRequested;
        _view.CancelOrderRequested += OnCancelOrderRequested;
        _view.AggressionSelected += OnAggressionSelected;
        _view.SpecialistToggleRequested += OnSpecialistToggleRequested;
        _view.UndoRequested += (_, _) => UndoLast();
        _view.ShipSelected += (_, id) => { _selectedShipId = id; RefreshWorkspace(); };
        _view.ConfirmMovementRequested += OnConfirmMovementRequested;
        _view.CasualtyToggled += OnCasualtyToggled;
        _view.RecoveryRequested += OnRecoveryRequested;
        _view.OpenShipManagementRequested += (_, _) => FleetManagementRequested?.Invoke(this, _planet);
        _confirmation = new ConfirmationDialog
        {
            Title = "Confirm Planetary Operation",
            OkButtonText = "CONFIRM",
            CancelButtonText = "RETURN"
        };
        _confirmation.Confirmed += OnConfirmed;
        AddChild(_confirmation);
    }

    public override void _ExitTree()
    {
        if (_confirmation != null) _confirmation.Confirmed -= OnConfirmed;
    }

    public void DisplayPlanet(Planet planet, Region selectedRegion = null,
        PlanetaryOperationsNavigationState restore = null)
    {
        _planet = planet;
        bool restoring = restore?.PlanetId == planet?.Id;
        _selectedRegion = selectedRegion
            ?? (restoring ? planet?.Regions.FirstOrDefault(region => region?.Id == restore.RegionId) : null)
            ?? planet?.Regions.FirstOrDefault(region => region?.Id == planet.CapitalRegionId)
            ?? planet?.Regions.FirstOrDefault();
        _overlay = restoring ? restore.Overlay : PlanetMapOverlay.Control;
        _selectedFactionId = restoring ? restore.FactionId : DefaultFactionId(planet);
        _verb = restoring ? restore.Verb : PlanetaryOperationsVerb.Order;
        _selectedOrder = restoring && restore.OrderId is int orderId
            ? Sector?.Orders.Values.FirstOrDefault(order => order.Id == orderId) : null;
        _selectedMission = restoring ? FindMission(restore.MissionKey) : null;
        ClearTransientSelection();
        RefreshWorkspace();
    }

    public void DisplayRegion(Region region, int? selectedSquadId = null)
    {
        DisplayPlanet(region?.Planet, region);
        if (selectedSquadId.HasValue)
        {
            Squad squad = FindPlanetSquad(selectedSquadId.Value);
            if (squad != null) SquadDoubleClicked?.Invoke(this, squad);
        }
    }

    public void DisplayGovernorRequest(Planet planet)
    {
        Region capital = planet?.Regions.FirstOrDefault(region => region?.Id == planet.CapitalRegionId)
            ?? planet?.Regions.FirstOrDefault();
        DisplayPlanet(planet, capital);
        RegionalOperationsViewModel model = PlanetaryOperationsViewModelBuilder.BuildRegional(
            Sector, capital, null, null);
        _selectedMission = model.SpecialMissions.FirstOrDefault(option =>
            option.SpecialMission?.MissionType == MissionType.ShowOfForce);
        _selectedOrder = _selectedMission == null ? null : OrderMutationService.FindEquivalentOrder(
            Sector, capital, _selectedMission, ResolveTargetFactionId(_selectedMission));
        RefreshWorkspace();
    }

    public void FocusRegion(Region region)
    {
        if (region?.Planet != _planet) return;
        _selectedRegion = region;
        ResetContext();
        RefreshWorkspace();
    }

    public void RefreshFromExternalChange()
    {
        RevalidateSelections();
        RefreshWorkspace();
    }

    public void ShowWorldDossierOverlay() => ShowWorldDossier();

    private Sector Sector => GameDataSingleton.Instance?.Sector;

    private void RefreshWorkspace()
    {
        if (_view == null || _planet == null || _selectedRegion == null) return;
        RevalidateSelections();
        _view.SetHeader(PlanetaryOperationsViewModelBuilder.BuildHeader(Sector, _planet));
        _view.DisplayMap(PlanetRegionMapViewModelBuilder.Build(
            Sector, _planet, _overlay, _selectedFactionId), _selectedRegion);
        _view.SetVerb(_verb);
        switch (_verb)
        {
            case PlanetaryOperationsVerb.Land: RefreshLanding(); break;
            case PlanetaryOperationsVerb.Embark: RefreshEmbarkation(); break;
            case PlanetaryOperationsVerb.Detach: RefreshDetach(); break;
            default: RefreshOrders(); break;
        }
    }

    private void RefreshOrders()
    {
        RegionalOperationsViewModel model = PlanetaryOperationsViewModelBuilder.BuildRegional(
            Sector, _selectedRegion, _selectedMission, _selectedOrder);
        if (_selectedOrder != null && !model.ActiveOrders.Contains(_selectedOrder)) _selectedOrder = null;
        if (_selectedMission != null && !model.OrdinaryMissions.Concat(model.SpecialMissions)
                .Any(option => option.RepresentsSameOption(_selectedMission))) _selectedMission = null;
        if (_selectedOrder != null && _selectedMission == null)
            _selectedMission = model.OrdinaryMissions.Concat(model.SpecialMissions)
                .FirstOrDefault(option => option.RepresentsOrder(_selectedOrder));

        List<ForceTreeSquad> roster = BuildOrderTreeRoster(model.Eligibility);
        IReadOnlyList<SpecialistOption> characterRoster = EnumerateSpecialists();
        IReadOnlyList<HierarchyTreeItem> forceTree = PlanetaryForceTreeBuilder.Build(
            roster, ForceTreeGrouping.Company, _filter, new HashSet<int>())
            .Concat(PlanetaryForceTreeBuilder.BuildCharacterGroup(characterRoster))
            .ToList();
        _view.DisplayOrders(model,
            forceTree,
            _filter, _selectedMission?.IdentityKey, _selectedOrder,
            EnumerateSpecialists(), _undoDescription);
    }

    private void RefreshLanding()
    {
        List<ForceTreeSquad> roster = PlanetForceMovementService
            .GetOrbitingPlayerShips(_planet, Sector.PlayerForce.Faction)
            .SelectMany(ship => ship.LoadedSquads.Where(squad => squad?.IsPresentOperationalForce == true)
                .Select(squad => new ForceTreeSquad(squad, ship.Name, ship))).ToList();
        IReadOnlyList<SpecialistOption> characters = EnumerateMovableCharacters();
        RevalidateIds(_movementSquadIds, roster.Select(item => item.Squad.Id));
        RevalidateIds(_movementCharacterIds, characters.Where(option => option.IsAvailable)
            .Select(option => option.Soldier.Id));
        IReadOnlyList<HierarchyTreeItem> forceTree = PlanetaryForceTreeBuilder.Build(
                roster, _grouping, _filter, _movementSquadIds)
            .Concat(PlanetaryForceTreeBuilder.BuildCharacterGroup(
                characters, _movementCharacterIds))
            .ToList();
        _view.DisplayMovement(_verb,
            PlanetaryOperationsViewModelBuilder.BuildRegionCards(_selectedRegion, Sector),
            forceTree,
            _filter, _grouping, [], null,
            _movementSquadIds.Count + _movementCharacterIds.Count);
    }

    private void RefreshEmbarkation()
    {
        List<Squad> squads = GetPlayerPresence(_selectedRegion)?.LandedSquads
            .Where(squad => squad?.IsPresentOperationalForce == true).ToList() ?? [];
        List<ForceTreeSquad> roster = squads.Select(squad => new ForceTreeSquad(
            squad, _selectedRegion.Name)).ToList();
        IReadOnlyList<SpecialistOption> characters = EnumerateMovableCharacters();
        RevalidateIds(_movementSquadIds, squads.Select(squad => squad.Id));
        RevalidateIds(_movementCharacterIds, characters.Where(option => option.IsAvailable)
            .Select(option => option.Soldier.Id));
        List<Squad> selected = squads.Where(squad => _movementSquadIds.Contains(squad.Id)).ToList();
        List<PlayerSoldier> selectedCharacters = characters
            .Where(option => option.IsAvailable && _movementCharacterIds.Contains(option.Soldier.Id))
            .Select(option => option.Soldier).ToList();
        IReadOnlyList<ShipCapacityChoice> ships = PlanetForceMovementService.BuildCapacityChoices(
            _planet, Sector.PlayerForce.Faction,
            new MovementParty(selected, selectedCharacters));
        if (_selectedShipId.HasValue && !ships.Any(choice =>
                choice.Ship.Id == _selectedShipId.Value && choice.Fits)) _selectedShipId = null;
        _view.DisplayMovement(_verb,
            PlanetaryOperationsViewModelBuilder.BuildRegionCards(_selectedRegion, Sector),
            PlanetaryForceTreeBuilder.Build(roster, ForceTreeGrouping.Company, _filter, _movementSquadIds)
                .Concat(PlanetaryForceTreeBuilder.BuildCharacterGroup(
                    characters, _movementCharacterIds)).ToList(),
            _filter, ForceTreeGrouping.Company, ships, _selectedShipId,
            _movementSquadIds.Count + _movementCharacterIds.Count);
    }

    private void RefreshDetach()
    {
        List<PlayerSoldier> casualties = GetPlayerPresence(_selectedRegion)?.LandedSquads
            .SelectMany(SoldierPresenceService.PresentMembers).OfType<PlayerSoldier>()
            .Where(soldier => soldier.IsWounded && soldier.IndividualPosting == null)
            .DistinctBy(soldier => soldier.Id)
            .OrderBy(soldier => soldier.AssignedSquad?.ParentUnit?.Name)
            .ThenBy(soldier => soldier.Name).ToList() ?? [];
        RevalidateIds(_casualtyIds, casualties.Select(soldier => soldier.Id));
        IReadOnlyList<ShipCapacityChoice> ships = PlanetForceMovementService
            .GetOrbitingPlayerShips(_planet, Sector.PlayerForce.Faction)
            .Select(ship => new ShipCapacityChoice(ship, ship.LoadedSoldierCount,
                _casualtyIds.Count, ship.LoadedSoldierCount + _casualtyIds.Count,
                ship.Template.SoldierCapacity,
                Math.Max(0, _casualtyIds.Count - ship.AvailableCapacity))).ToList();
        if (_selectedShipId.HasValue && !ships.Any(choice =>
                choice.Ship.Id == _selectedShipId.Value && choice.Fits)) _selectedShipId = null;
        _view.DisplayDetach(PlanetaryOperationsViewModelBuilder.BuildRegionCards(
            _selectedRegion, Sector), casualties, _casualtyIds, ships, _selectedShipId);
    }

    private void OnRegionSelected(object sender, Region region)
    {
        if (region?.Planet != _planet) return;
        _selectedRegion = region;

        if (_verb == PlanetaryOperationsVerb.Land)
        {
            // The orbiting force is the same regardless of the highlighted destination. Keep
            // its tree (and multi-selection) intact while updating only the map and destination
            // panel.
            _view.DisplayMap(PlanetRegionMapViewModelBuilder.Build(
                Sector, _planet, _overlay, _selectedFactionId), _selectedRegion);
            _view.UpdateLandingDestination(
                PlanetaryOperationsViewModelBuilder.BuildRegionCards(_selectedRegion, Sector));
            _view.ResetRightPanelScroll();
            return;
        }

        ResetContext();
        _view.ResetRightPanelScroll();
        RefreshWorkspace();
    }

    private void OnRegionActivated(object sender, Region region)
    {
        OnRegionSelected(sender, region);
        _view.FocusMap();
    }

    private void OnVerbSelected(object sender, PlanetaryOperationsVerb verb)
    {
        _verb = verb;
        ClearTransientSelection();
        RefreshWorkspace();
    }

    private void OnMissionSelected(object sender, string key)
    {
        _selectedMission = FindMission(key);
        _selectedOrder = _selectedMission == null ? null : OrderMutationService.FindEquivalentOrder(
            Sector, _selectedRegion, _selectedMission, ResolveTargetFactionId(_selectedMission));
        RefreshWorkspace();
    }

    private void OnOrderSelected(object sender, int id)
    {
        _selectedOrder = Sector?.Orders.Values.FirstOrDefault(order => order.Id == id);
        _selectedMission = null;
        RefreshWorkspace();
    }

    private void OnForceNodePressed(object sender, string key)
    {
        if (_verb == PlanetaryOperationsVerb.Order) MutateOrderSelection(key);
        else MutateMovementSelection(key);
    }

    private void MutateOrderSelection(string key)
    {
        if (_selectedMission == null) return;
        RegionalOperationsViewModel model = PlanetaryOperationsViewModelBuilder.BuildRegional(
            Sector, _selectedRegion, _selectedMission, _selectedOrder);
        List<ForceTreeSquad> roster = BuildOrderTreeRoster(model.Eligibility);
        IReadOnlyList<SpecialistOption> characterRoster = EnumerateSpecialists();
        List<PlayerSoldier> characters = PlanetaryForceTreeBuilder
            .ResolveCharacterSelection(characterRoster, key).ToList();
        if (characters.Count > 0)
        {
            bool removing = _selectedOrder != null
                && characters.All(character => ReferenceEquals(
                    character.CurrentOrder, _selectedOrder));
            OrderMutationResult characterResult = removing
                ? RemoveSpecialists(_selectedOrder, characters)
                : OrderMutationService.CreateOrAdd(
                    Sector, _selectedRegion, _selectedMission, [], characters,
                    ResolveTargetFactionId(_selectedMission),
                    _selectedOrder?.LevelOfAggression ?? Aggression.Normal);
            if (characterResult.Succeeded)
            {
                _selectedOrder = characterResult.Order;
                if (characterResult.Order != null && characterResult.Order.Force.IsEmpty)
                {
                    _selectedOrder = null;
                }
                Changed(characterResult.Message);
            }
            else ShowFeedback(characterResult.Message);
            RefreshWorkspace();
            return;
        }
        List<Squad> squads = PlanetaryForceTreeBuilder.ResolveSelection(roster, key)
            .Where(squad => roster.Any(item => item.Squad == squad && item.Selectable)).ToList();
        if (squads.Count == 0) return;
        bool created = _selectedOrder == null;
        OrderMutationResult result = OrderMutationService.CreateOrAdd(
            Sector, _selectedRegion, _selectedMission, squads,
            ResolveTargetFactionId(_selectedMission),
            _selectedOrder?.LevelOfAggression ?? Aggression.Normal);
        if (result.Succeeded)
        {
            _selectedOrder = result.Order;
            if (created) SetUndo("order creation", () => OrderMutationService.Cancel(Sector, result.Order));
            else SetUndo("squad addition", () => RemoveMany(result.Order, squads));
            Changed(result.Message);
        }
        else ShowFeedback(result.Message);
        RefreshWorkspace();
    }

    private void MutateMovementSelection(string key)
    {
        List<ForceTreeSquad> roster = _verb == PlanetaryOperationsVerb.Land
            ? PlanetForceMovementService.GetOrbitingPlayerShips(_planet, Sector.PlayerForce.Faction)
                .SelectMany(ship => ship.LoadedSquads.Select(squad => new ForceTreeSquad(squad, ship.Name, ship))).ToList()
            : (GetPlayerPresence(_selectedRegion)?.LandedSquads ?? [])
                .Select(squad => new ForceTreeSquad(squad, _selectedRegion.Name)).ToList();
        IReadOnlyList<SpecialistOption> characters = EnumerateMovableCharacters();
        IReadOnlyList<PlayerSoldier> selectedCharacters = PlanetaryForceTreeBuilder
            .ResolveCharacterSelection(characters, key);
        if (selectedCharacters.Count > 0)
        {
            bool characterAllSelected = selectedCharacters.All(character =>
                _movementCharacterIds.Contains(character.Id));
            foreach (PlayerSoldier character in selectedCharacters)
            {
                if (characterAllSelected) _movementCharacterIds.Remove(character.Id);
                else _movementCharacterIds.Add(character.Id);
            }
            RefreshWorkspace();
            return;
        }
        IReadOnlyList<Squad> squads = PlanetaryForceTreeBuilder.ResolveSelection(roster, key);
        bool allSelected = squads.Count > 0 && squads.All(squad => _movementSquadIds.Contains(squad.Id));
        foreach (Squad squad in squads)
        {
            if (allSelected) _movementSquadIds.Remove(squad.Id);
            else _movementSquadIds.Add(squad.Id);
        }
        RefreshWorkspace();
    }

    private void OnForceNodeActivated(object sender, string key)
    {
        if (key?.StartsWith("squad:") != true || !int.TryParse(key[6..], out int id)) return;
        Squad squad = FindPlanetSquad(id);
        if (squad != null) SquadDoubleClicked?.Invoke(this, squad);
    }

    private void OnRemoveSquadRequested(object sender, int squadId)
    {
        Order order = _selectedOrder;
        Squad squad = order?.AssignedSquads.FirstOrDefault(item => item.Id == squadId);
        OrderMutationResult result = OrderMutationService.RemoveSquad(Sector, order, squad);
        if (result.Succeeded)
        {
            SetUndo("squad removal", () => OrderMutationService.RestoreSquad(Sector, order, squad));
            if (order.Force.IsEmpty) _selectedOrder = null;
            Changed(result.Message);
        }
        else ShowFeedback(result.Message);
        RefreshWorkspace();
    }

    private void OnCancelOrderRequested(object sender, int id)
    {
        Order order = Sector?.Orders.Values.FirstOrDefault(item => item.Id == id);
        if (order == null) return;
        List<Squad> squads = order.AssignedSquads.ToList();
        List<PlayerSoldier> specialists = order.AssignedCharacters.ToList();
        Confirm($"Cancel {MissionAvailability.GetOrderLabel(order.Mission)}?\n\n"
            + $"{squads.Count} squads will be released; {specialists.Count} specialists will return.", () =>
        {
            OrderMutationResult result = OrderMutationService.Cancel(Sector, order);
            if (result.Succeeded)
            {
                _selectedOrder = null;
                SetUndo("order cancellation", () => RestoreOrder(order, squads, specialists));
                Changed(result.Message);
            }
            else ShowFeedback(result.Message);
            RefreshWorkspace();
        });
    }

    private void OnAggressionSelected(object sender, Aggression aggression)
    {
        if (_selectedOrder == null) return;
        Aggression previous = _selectedOrder.LevelOfAggression;
        OrderMutationResult result = OrderMutationService.SetAggression(Sector, _selectedOrder, aggression);
        if (result.Succeeded && previous != aggression)
        {
            Order order = _selectedOrder;
            SetUndo("aggression change", () => OrderMutationService.SetAggression(Sector, order, previous));
            Changed(result.Message);
        }
        RefreshWorkspace();
    }

    private void OnSpecialistToggleRequested(object sender, int soldierId)
    {
        if (_selectedOrder == null) return;
        PlayerSoldier soldier = FindPlayerSoldier(soldierId);
        bool attached = ReferenceEquals(soldier?.CurrentOrder, _selectedOrder);
        OrderMutationResult result = attached
            ? OrderMutationService.DetachSpecialist(Sector, _selectedOrder, soldier)
            : OrderMutationService.AttachSpecialist(Sector, _selectedOrder, soldier);
        if (result.Succeeded)
        {
            Order order = _selectedOrder;
            SetUndo(attached ? "specialist detachment" : "specialist attachment", () => attached
                ? OrderMutationService.AttachSpecialist(Sector, order, soldier)
                : OrderMutationService.DetachSpecialist(Sector, order, soldier));
            Changed(result.Message);
        }
        else ShowFeedback(result.Message);
        RefreshWorkspace();
    }

    private void OnConfirmMovementRequested(object sender, EventArgs e)
    {
        if (_verb == PlanetaryOperationsVerb.Land) CommitLanding();
        else if (_verb == PlanetaryOperationsVerb.Embark) CommitEmbarkation();
        else if (_verb == PlanetaryOperationsVerb.Detach) CommitDetach();
    }

    private void CommitLanding()
    {
        List<Squad> squads = PlanetForceMovementService.GetOrbitingPlayerShips(
                _planet, Sector.PlayerForce.Faction)
            .SelectMany(ship => ship.LoadedSquads)
            .Where(squad => _movementSquadIds.Contains(squad.Id)).ToList();
        List<PlayerSoldier> characters = EnumerateMovableCharacters()
            .Where(option => option.IsAvailable && _movementCharacterIds.Contains(option.Soldier.Id))
            .Select(option => option.Soldier).ToList();
        FinishMovement(PlanetForceMovementService.Land(
            Sector, _planet, _selectedRegion,
            new MovementParty(squads, characters)));
    }

    private void CommitEmbarkation()
    {
        Ship ship = GetSelectedShip();
        List<Squad> squads = GetPlayerPresence(_selectedRegion)?.LandedSquads
            .Where(squad => _movementSquadIds.Contains(squad.Id)).ToList() ?? [];
        List<PlayerSoldier> characters = EnumerateMovableCharacters()
            .Where(option => option.IsAvailable && _movementCharacterIds.Contains(option.Soldier.Id))
            .Select(option => option.Soldier).ToList();
        FinishMovement(PlanetForceMovementService.Embark(
            Sector, _planet, _selectedRegion, ship,
            new MovementParty(squads, characters)));
    }

    private void CommitDetach()
    {
        Ship ship = GetSelectedShip();
        List<PlayerSoldier> casualties = _casualtyIds.Select(FindPlayerSoldier)
            .Where(soldier => soldier != null).ToList();
        MedicalDetachmentResult result = _medicalDetachments.DetachToOrbit(
            Sector, _planet, _selectedRegion, ship, casualties, GameDataSingleton.Instance.Date);
        if (result.Succeeded) { ClearTransientSelection(); Changed(result.Message); }
        else ShowFeedback(result.Message);
        RefreshWorkspace();
    }

    private void OnCasualtyToggled(object sender, int id)
    {
        if (!_casualtyIds.Add(id)) _casualtyIds.Remove(id);
        RefreshWorkspace();
    }

    private void OnRecoveryRequested(object sender, int id)
    {
        PlayerSoldier soldier = FindPlayerSoldier(id);
        if (soldier != null) RecoveryOperationsRequested?.Invoke(this, soldier);
    }

    private void ShowWorldDossier() => _view.ShowWorldDossier(
        PlanetaryOperationsViewModelBuilder.BuildWorld(Sector, _planet, _selectedRegion));

    private IReadOnlyList<SpecialistOption> EnumerateSpecialists()
    {
        if (_selectedRegion == null) return [];

        // SpecialistAvailability also consults the global soldier map. Build the roster from
        // every valid origin so locally staged characters are included, then apply the same
        // target-or-adjacent region boundary used by the squad roster. Characters outside that
        // operational area should not appear in Order mode at all, even as unavailable rows.
        return _selectedRegion.GetSelfAndAdjacentRegions()
            .Select(GetPlayerPresence)
            .Where(presence => presence != null)
            .SelectMany(presence => SpecialistAvailability.EnumerateRoster(
                presence, _selectedRegion, _selectedOrder))
            .Where(option => IsInOrderArea(option?.Soldier, _selectedRegion))
            .GroupBy(option => option.Soldier.Id)
            .Select(group => group.First())
            .OrderBy(option => option.HomeSquad?.Name)
            .ThenBy(option => option.Soldier.Name)
            .ToList();
    }

    private static bool IsInOrderArea(PlayerSoldier soldier, Region target)
    {
        Region location = CampaignLocationService.ForSoldier(soldier)?.Region;
        return target != null && location != null
            && target.GetSelfAndAdjacentRegions().Contains(location);
    }

    private IReadOnlyList<SpecialistOption> EnumerateMovableCharacters()
    {
        if (Sector?.PlayerForce?.Army?.PlayerSoldierMap == null) return [];
        bool landing = _verb == PlanetaryOperationsVerb.Land;
        Ship destinationShip = landing ? null : GetSelectedShip();
        CampaignLocation destination = landing
            ? CampaignLocation.Landed(_selectedRegion)
            : destinationShip == null ? null : CampaignLocation.Aboard(destinationShip);
        CharacterAvailabilityService availability = new();
        return Sector.PlayerForce.Army.PlayerSoldierMap.Values
            .Where(character => character.AssignedSquad?.PermitsIndividualDeployment == true)
            .Where(character =>
            {
                CampaignLocation location = CampaignLocationService.ForSoldier(character);
                if (landing)
                {
                    return location?.Ship != null
                        && PlanetForceMovementService.GetOrbitingPlayerShips(
                            _planet, Sector.PlayerForce.Faction).Contains(location.Ship);
                }
                return location?.Region == _selectedRegion;
            })
            .Select(character =>
            {
                CharacterAvailabilityEvaluation evaluation = destination == null
                    ? new CharacterAvailabilityEvaluation(
                        false,
                        CharacterAvailabilityReasonCode.MissingLocation,
                        "Choose a destination ship.")
                    : availability.EvaluateMovement(character, destination);
                return new SpecialistOption(
                    character,
                    character.AssignedSquad,
                    evaluation.IsAllowed
                        ? CampaignLocationService.Format(CampaignLocationService.ForSoldier(character))
                        : evaluation.Reason,
                    evaluation.IsAllowed);
            })
            .OrderBy(option => option.HomeSquad?.Name)
            .ThenBy(option => option.Soldier.Name)
            .ToList();
    }

    private static OrderMutationResult RemoveSpecialists(
        Order order,
        IReadOnlyList<PlayerSoldier> characters)
    {
        int removed = 0;
        foreach (PlayerSoldier character in characters)
        {
            if (OrderMutationService.DetachSpecialist(
                    GameDataSingleton.Instance.Sector, order, character).Succeeded)
            {
                removed++;
            }
        }
        return new OrderMutationResult(
            removed == characters.Count,
            removed == characters.Count ? "Characters removed." : "Some characters could not be removed.",
            OrderMutationKind.SpecialistDetached,
            order,
            ReleasedSpecialists: removed);
    }

    internal static List<ForceTreeSquad> BuildOrderTreeRoster(RegionalEligibilityResult eligibility)
    {
        IEnumerable<RegionalSquadCandidate> candidates = eligibility == null
            ? Enumerable.Empty<RegionalSquadCandidate>()
            : eligibility.Groups.SelectMany(group => group.Candidates)
                .Concat(eligibility.Excluded);

        return candidates
            .Where(candidate => SpecialistAvailability.IsMissionSquadFormation(candidate.Squad))
            .DistinctBy(candidate => candidate.Squad.Id)
            .Select(candidate => new ForceTreeSquad(candidate.Squad, candidate.Origin.Name, null,
                candidate.Exclusion, candidate.IsAssignedToContext)).ToList();
    }

    private AvailableMission FindMission(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || _selectedRegion == null) return null;
        RegionalOperationsViewModel model = PlanetaryOperationsViewModelBuilder.BuildRegional(
            Sector, _selectedRegion, null, null);
        return model.OrdinaryMissions.Concat(model.SpecialMissions)
            .FirstOrDefault(option => option.IdentityKey == key);
    }

    private OrderMutationResult RemoveMany(Order order, IReadOnlyList<Squad> squads)
    {
        OrderMutationResult last = new(true, "Change undone.", Order: order);
        foreach (Squad squad in squads.Where(squad => ReferenceEquals(squad.CurrentOrders, order)).ToList())
            last = OrderMutationService.RemoveSquad(Sector, order, squad);
        return last;
    }

    private OrderMutationResult RestoreOrder(Order order, IReadOnlyList<Squad> squads,
        IReadOnlyList<PlayerSoldier> specialists)
    {
        OrderMutationResult result = null;
        foreach (Squad squad in squads)
        {
            result = OrderMutationService.RestoreSquad(Sector, order, squad);
            if (!result.Succeeded) return result;
        }
        foreach (PlayerSoldier specialist in specialists)
        {
            result = OrderMutationService.AttachSpecialist(Sector, order, specialist);
            if (!result.Succeeded) return result;
        }
        return result ?? new OrderMutationResult(false, "The order could not be restored.");
    }

    private void SetUndo(string description, Func<OrderMutationResult> action)
    {
        _undoDescription = description;
        _undoLast = () =>
        {
            OrderMutationResult result = action();
            if (result.Succeeded) Changed($"Undid {description}.");
            else ShowFeedback(result.Message);
        };
    }

    private void UndoLast()
    {
        Action undo = _undoLast;
        _undoLast = null;
        _undoDescription = null;
        undo?.Invoke();
        RevalidateSelections();
        RefreshWorkspace();
    }

    private void FinishMovement(ForceMovementResult result)
    {
        if (result.Succeeded) { ClearTransientSelection(); Changed(result.Message); }
        else ShowFeedback(result.Message);
        RefreshWorkspace();
    }

    private Ship GetSelectedShip() => PlanetForceMovementService.GetOrbitingPlayerShips(
        _planet, Sector.PlayerForce.Faction).FirstOrDefault(ship => ship.Id == _selectedShipId);

    private int ResolveTargetFactionId(AvailableMission mission)
    {
        int explicitTarget = mission?.TargetFaction?.PlanetFaction?.Faction?.Id
            ?? mission?.SpecialMission?.RegionFaction?.PlanetFaction?.Faction?.Id ?? -1;
        if (explicitTarget >= 0 || mission?.Kind != MissionAvailabilityKind.Diversion) return explicitTarget;
        return _selectedRegion.RegionFactionMap.Values
            .Where(presence => presence.IsPublic
                && !FactionRelationshipService.IsImperial(presence.PlanetFaction.Faction))
            .Select(presence => presence.PlanetFaction.Faction.Id).FirstOrDefault(-1);
    }

    private int? DefaultFactionId(Planet planet) => planet?.PlanetFactionMap.Values
        .OrderBy(presence => presence.Faction.IsDefaultFaction ? 0
            : presence.Faction.IsPlayerFaction ? 1 : 2)
        .Select(presence => (int?)presence.Faction.Id).FirstOrDefault();

    private RegionFaction GetPlayerPresence(Region region)
    {
        if (region == null || Sector?.PlayerForce?.Faction == null) return null;
        region.RegionFactionMap.TryGetValue(Sector.PlayerForce.Faction.Id, out RegionFaction presence);
        return presence;
    }

    private Squad FindPlanetSquad(int id) => _planet?.Regions.Where(region => region != null)
        .SelectMany(region => region.RegionFactionMap.Values).SelectMany(presence => presence.LandedSquads)
        .Concat(_planet.OrbitingTaskForceList.SelectMany(fleet => fleet.Ships)
            .SelectMany(ship => ship.LoadedSquads)).FirstOrDefault(squad => squad.Id == id);

    private PlayerSoldier FindPlayerSoldier(int id) =>
        Sector?.PlayerForce?.Army?.PlayerSoldierMap?.GetValueOrDefault(id);

    private void RevalidateSelections()
    {
        if (_planet == null) return;
        _selectedRegion = _planet.Regions.FirstOrDefault(region => region?.Id == _selectedRegion?.Id)
            ?? _planet.Regions.FirstOrDefault();
        _selectedOrder = _selectedOrder == null ? null
            : Sector?.Orders.Values.FirstOrDefault(order => order.Id == _selectedOrder.Id);
    }

    private static void RevalidateIds(HashSet<int> selected, IEnumerable<int> valid)
    {
        HashSet<int> validIds = valid.ToHashSet();
        selected.RemoveWhere(id => !validIds.Contains(id));
    }

    private void ResetContext()
    {
        _selectedMission = null;
        _selectedOrder = null;
        _filter = "";
        ClearTransientSelection();
    }

    private void ClearTransientSelection()
    {
        _movementSquadIds.Clear();
        _movementCharacterIds.Clear();
        _casualtyIds.Clear();
        _selectedShipId = null;
    }

    private void Changed(string message)
    {
        CampaignChanged?.Invoke(this, EventArgs.Empty);
        ShowFeedback(message);
    }

    private void Confirm(string text, Action action)
    {
        _pendingConfirmedAction = action;
        _confirmation.DialogText = text;
        _confirmation.PopupCentered(new Vector2I(620, 300));
    }

    private void OnConfirmed()
    {
        Action action = _pendingConfirmedAction;
        _pendingConfirmedAction = null;
        action?.Invoke();
    }

    private static void ShowFeedback(string message)
    {
        if (!string.IsNullOrWhiteSpace(message)) GD.Print($"Planetary Operations: {message}");
    }
}
