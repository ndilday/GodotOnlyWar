using Godot;
using OnlyWar.Application;
using OnlyWar.Helpers.PlanetaryOperations;
using OnlyWar.Models.Orders;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Planetary Operations screen. It owns selection, navigation and Godot rendering only: every
/// campaign fact arrives as a detached projection from <see cref="IOperationsScreenApplication"/>
/// and every change is a typed command carrying IDs and this screen's session token.
/// </summary>
public partial class PlanetaryOperationsScreenController : DialogController
{
    private IOperationsScreenApplication _operations;
    private Guid _sessionToken;
    private PlanetaryOperationsScreenView _view;

    // Selection state. These are identifiers and screen preferences, never campaign objects.
    private int _planetId = -1;
    private int _regionId = -1;
    private PlanetMapOverlay _overlay = PlanetMapOverlay.Control;
    private int? _selectedFactionId;
    private string _selectedMissionKey;
    private int? _selectedOrderId;
    private PlanetaryOperationsVerb _verb = PlanetaryOperationsVerb.Order;
    private ForceTreeGrouping _grouping = ForceTreeGrouping.Company;
    private string _filter = "";
    private readonly HashSet<int> _movementSquadIds = [];
    private readonly HashSet<int> _movementCharacterIds = [];
    private readonly HashSet<int> _casualtyIds = [];
    private int? _selectedShipId;
    private ConfirmationDialog _confirmation;
    private Action _pendingConfirmedAction;
    private Guid? _undoToken;
    private string _undoDescription;

    public event EventHandler<int> SquadDoubleClicked;
    public event EventHandler<int> FleetManagementRequested;
    public event EventHandler<int> RecoveryOperationsRequested;
    public event EventHandler CampaignChanged;

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
        _view.RecoveryRequested += (_, id) => RecoveryOperationsRequested?.Invoke(this, id);
        _view.OpenShipManagementRequested +=
            (_, _) => FleetManagementRequested?.Invoke(this, _planetId);
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
        if (_operations != null) _operations.SessionChanged -= OnSessionChanged;
    }

    public void Configure(IOperationsScreenApplication operations)
    {
        if (_operations != null) _operations.SessionChanged -= OnSessionChanged;
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _operations.SessionChanged += OnSessionChanged;
        _sessionToken = operations.SessionToken;
    }

    // A load or new game replaces the campaign this screen was editing. Adopt the new token and
    // drop every selection, pending confirmation and undo offer built from the old one.
    private void OnSessionChanged(object sender, EventArgs args)
    {
        _sessionToken = _operations.SessionToken;
        _pendingConfirmedAction = null;
        _undoToken = null;
        _undoDescription = null;
        _planetId = -1;
        _regionId = -1;
        ResetContext();
    }

    public void DisplayPlanet(int planetId, int? regionId = null)
    {
        OperationsEntryView entry = _operations.QueryEntry(planetId, regionId);
        _planetId = entry.PlanetId;
        _regionId = entry.RegionId;
        _overlay = PlanetMapOverlay.Control;
        _selectedFactionId = entry.FactionId;
        _verb = PlanetaryOperationsVerb.Order;
        _selectedOrderId = entry.OrderId;
        _selectedMissionKey = entry.MissionKey;
        ClearTransientSelection();
        RefreshWorkspace();
    }

    public void DisplayRegion(int regionId, int planetId, int? selectedSquadId = null)
    {
        DisplayPlanet(planetId, regionId);
        if (selectedSquadId is int squadId
            && _operations.FindPlanetSquad(_planetId, squadId) is int found)
        {
            SquadDoubleClicked?.Invoke(this, found);
        }
    }

    public void DisplayGovernorRequest(int planetId)
    {
        OperationsEntryView entry = _operations.QueryGovernorRequestEntry(planetId);
        _planetId = entry.PlanetId;
        _regionId = entry.RegionId;
        _overlay = PlanetMapOverlay.Control;
        _selectedFactionId = entry.FactionId;
        _verb = PlanetaryOperationsVerb.Order;
        _selectedMissionKey = entry.MissionKey;
        _selectedOrderId = entry.OrderId;
        ClearTransientSelection();
        RefreshWorkspace();
    }

    public void FocusRegion(int regionId)
    {
        _regionId = regionId;
        ResetContext();
        RefreshWorkspace();
    }

    public void RefreshFromExternalChange() => RefreshWorkspace();

    public void ShowWorldDossierOverlay() =>
        _view.ShowWorldDossier(_operations.QueryWorldDossier(_planetId, _regionId));

    private OperationsWorkspaceQuery CurrentQuery => new(
        _planetId, _regionId, _overlay, _selectedFactionId, _verb, _selectedMissionKey,
        _selectedOrderId, _filter, _grouping,
        _movementSquadIds, _movementCharacterIds, _casualtyIds, _selectedShipId);

    private void RefreshWorkspace()
    {
        if (_view == null || _operations == null || _planetId < 0) return;
        OperationsWorkspaceView workspace = _operations.QueryOperations(CurrentQuery);
        if (!workspace.Exists) return;

        _sessionToken = workspace.SessionToken;
        _regionId = workspace.SelectedRegionId;
        _view.SetHeader(workspace.Header);
        _view.DisplayMap(workspace.Map, _regionId);
        _view.SetVerb(_verb);
        switch (_verb)
        {
            case PlanetaryOperationsVerb.Land:
            case PlanetaryOperationsVerb.Embark:
                AdoptMovement(workspace.Movement);
                _view.DisplayMovement(_verb, workspace.Movement, _filter, _grouping);
                break;
            case PlanetaryOperationsVerb.Detach:
                _casualtyIds.IntersectWith(workspace.Detach.ValidCasualtyIds);
                _selectedShipId = workspace.Detach.SelectedShipId;
                _view.DisplayDetach(workspace.Detach, _casualtyIds);
                break;
            default:
                _selectedOrderId = workspace.Orders.SelectedOrderId;
                _selectedMissionKey = workspace.Orders.SelectedMissionKey;
                _view.DisplayOrders(workspace.Orders, _filter, _undoDescription);
                break;
        }
    }

    // The application reports which of the screen's remembered ids are still valid participants.
    private void AdoptMovement(MovementOperationsView movement)
    {
        _movementSquadIds.IntersectWith(movement.ValidSquadIds);
        _movementCharacterIds.IntersectWith(movement.ValidCharacterIds);
        _selectedShipId = movement.SelectedShipId;
    }

    private void OnRegionSelected(object sender, int regionId)
    {
        _regionId = regionId;
        if (_verb == PlanetaryOperationsVerb.Land)
        {
            // The orbiting force is the same regardless of the highlighted destination. Keep
            // its tree (and multi-selection) intact while updating only the map and destination
            // panel.
            OperationsWorkspaceView workspace = _operations.QueryOperations(CurrentQuery);
            if (!workspace.Exists) return;
            _view.DisplayMap(workspace.Map, _regionId);
            _view.UpdateLandingDestination(_operations.QueryRegionCards(_regionId));
            _view.ResetRightPanelScroll();
            return;
        }

        ResetContext();
        _view.ResetRightPanelScroll();
        RefreshWorkspace();
    }

    private void OnRegionActivated(object sender, int regionId)
    {
        OnRegionSelected(sender, regionId);
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
        _selectedMissionKey = key;
        _selectedOrderId = _operations.FindOrderForMission(_regionId, key);
        RefreshWorkspace();
    }

    private void OnOrderSelected(object sender, int id)
    {
        _selectedOrderId = id;
        _selectedMissionKey = null;
        RefreshWorkspace();
    }

    private void OnForceNodePressed(object sender, string key)
    {
        OperationsSelection selection = _operations.ResolveForceSelection(CurrentQuery, key);
        if (selection.IsEmpty) return;
        if (_verb == PlanetaryOperationsVerb.Order)
        {
            // Whether this adds, reinforces or releases participants is an order rule the
            // application owns; the tree only reports which rows the player activated.
            Apply(_operations.SetOrderParticipants(new(
                _sessionToken, _regionId, _selectedMissionKey,
                selection.SquadIds, selection.CharacterIds, _selectedOrderId)));
            return;
        }
        ToggleMovementSelection(_movementCharacterIds, selection.CharacterIds);
        ToggleMovementSelection(_movementSquadIds, selection.SquadIds);
        RefreshWorkspace();
    }

    private static void ToggleMovementSelection(HashSet<int> selected, IReadOnlyList<int> ids)
    {
        if (ids.Count == 0) return;
        bool allSelected = ids.All(selected.Contains);
        foreach (int id in ids)
        {
            if (allSelected) selected.Remove(id);
            else selected.Add(id);
        }
    }

    private void OnForceNodeActivated(object sender, string key)
    {
        if (key?.StartsWith("squad:") != true || !int.TryParse(key[6..], out int id)) return;
        if (_operations.FindPlanetSquad(_planetId, id) is int found)
            SquadDoubleClicked?.Invoke(this, found);
    }

    private void OnRemoveSquadRequested(object sender, int squadId)
    {
        if (_selectedOrderId is not int orderId) return;
        Apply(_operations.RemoveOrderSquad(new(_sessionToken, orderId, squadId)));
    }

    private void OnCancelOrderRequested(object sender, int id)
    {
        OrderCancellationPrompt prompt = _operations.DescribeOrderCancellation(id);
        if (!prompt.Exists) return;
        // Bind the confirmation to the campaign the player is looking at now, so a load between
        // the prompt and the CONFIRM press cannot cancel a same-numbered order elsewhere.
        Guid token = _sessionToken;
        Confirm($"Cancel {prompt.OrderLabel}?\n\n"
            + $"{prompt.SquadCount} squads will be released; "
            + $"{prompt.SpecialistCount} specialists will return.",
            () => Apply(_operations.CancelOrder(new(token, id))));
    }

    private void OnAggressionSelected(object sender, Aggression aggression)
    {
        if (_selectedOrderId is not int orderId) return;
        OperationsCommandResult result = _operations.SetOrderAggression(
            new(_sessionToken, orderId, aggression));
        // An unchanged aggression succeeds without offering an undo; leave the previous offer and
        // the feedback line alone, exactly as the pre-migration screen did.
        if (!result.Succeeded || result.UndoToken.HasValue) Apply(result);
        else RefreshWorkspace();
    }

    private void OnSpecialistToggleRequested(object sender, int soldierId)
    {
        if (_selectedOrderId is not int orderId) return;
        Apply(_operations.ToggleOrderSpecialist(new(_sessionToken, orderId, soldierId)));
    }

    private void OnConfirmMovementRequested(object sender, EventArgs e)
    {
        OperationsCommandResult result = _verb switch
        {
            PlanetaryOperationsVerb.Land => _operations.LandForce(new(
                _sessionToken, _planetId, _regionId,
                [.. _movementSquadIds], [.. _movementCharacterIds])),
            PlanetaryOperationsVerb.Embark => _operations.EmbarkForce(new(
                _sessionToken, _planetId, _regionId, _selectedShipId ?? -1,
                [.. _movementSquadIds], [.. _movementCharacterIds])),
            _ => _operations.DetachCasualties(new(
                _sessionToken, _planetId, _regionId, _selectedShipId ?? -1, [.. _casualtyIds]))
        };
        if (result.Succeeded) { ClearTransientSelection(); Changed(result.Message); }
        else ShowFeedback(result.Message);
        RefreshWorkspace();
    }

    private void OnCasualtyToggled(object sender, int id)
    {
        if (!_casualtyIds.Add(id)) _casualtyIds.Remove(id);
        RefreshWorkspace();
    }

    private void UndoLast()
    {
        Guid? token = _undoToken;
        _undoToken = null;
        _undoDescription = null;
        if (token is Guid value)
        {
            OperationsCommandResult result = _operations.UndoLastOperation(
                new(_sessionToken, value));
            if (result.Succeeded) Changed(result.Message);
            else ShowFeedback(result.Message);
        }
        RefreshWorkspace();
    }

    /// <summary>
    /// Adopts the command's reported order selection and undo offer. A rejected command leaves the
    /// screen's selection and any earlier undo offer untouched.
    /// </summary>
    private void Apply(OperationsCommandResult result)
    {
        if (result.Succeeded)
        {
            _selectedOrderId = result.OrderId;
            if (result.UndoToken is Guid token)
            {
                _undoToken = token;
                _undoDescription = result.UndoDescription;
            }
            Changed(result.Message);
        }
        else ShowFeedback(result.Message);
        RefreshWorkspace();
    }

    private void ResetContext()
    {
        _selectedMissionKey = null;
        _selectedOrderId = null;
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
