using Godot;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class SystemInspector : Control
{
    public event EventHandler<int> OpenSystemPressed;
    public event EventHandler<int> PlotCoursePressed;
    public event EventHandler<int> DivideFleetPressed;
    public event EventHandler<int> MergeFleetPressed;
    public event EventHandler<int> LandSquadsPressed;
    public event EventHandler<int> LoadSquadsPressed;

    private Label _nameLabel;
    private Label _controlLabel;
    private Label _planetDetailLabel;
    private Label _orbitDetailLabel;
    private Label _requestDetailLabel;
    private Label _selectedFleetDetailLabel;
    private ItemList _fleetList;
    private Button _openSystemButton;
    private Button _plotCourseButton;
    private Button _divideButton;
    private Button _mergeButton;
    private Button _landSquadsButton;
    private Button _loadSquadsButton;
    private Planet _selectedPlanet;
    private readonly List<TaskForce> _orbitingFleets = [];
    private int? _selectedFleetId;
    private bool _isRefreshingFleetList;

    public override void _Ready()
    {
        _nameLabel = GetNode<Label>("Panel/MarginContainer/ScrollContainer/VBoxContainer/Header/SystemNameLabel");
        _controlLabel = GetNode<Label>("Panel/MarginContainer/ScrollContainer/VBoxContainer/ControlLabel");
        _planetDetailLabel = GetNode<Label>("Panel/MarginContainer/ScrollContainer/VBoxContainer/PlanetSection/PlanetDetailLabel");
        _orbitDetailLabel = GetNode<Label>("Panel/MarginContainer/ScrollContainer/VBoxContainer/OrbitSection/OrbitDetailLabel");
        _requestDetailLabel = GetNode<Label>("Panel/MarginContainer/ScrollContainer/VBoxContainer/RequestSection/RequestDetailLabel");
        _fleetList = GetNode<ItemList>("Panel/MarginContainer/ScrollContainer/VBoxContainer/OrbitSection/FleetList");
        _selectedFleetDetailLabel = GetNode<Label>("Panel/MarginContainer/ScrollContainer/VBoxContainer/OrbitSection/SelectedFleetDetailLabel");
        _openSystemButton = GetNode<Button>("Panel/MarginContainer/ScrollContainer/VBoxContainer/ActionSection/OpenSystemButton");
        _plotCourseButton = GetNode<Button>("Panel/MarginContainer/ScrollContainer/VBoxContainer/ActionSection/PlotCourseButton");
        _divideButton = GetNode<Button>("Panel/MarginContainer/ScrollContainer/VBoxContainer/ActionSection/DivideButton");
        _mergeButton = GetNode<Button>("Panel/MarginContainer/ScrollContainer/VBoxContainer/ActionSection/MergeButton");
        _landSquadsButton = GetNode<Button>("Panel/MarginContainer/ScrollContainer/VBoxContainer/ActionSection/LandSquadsButton");
        _loadSquadsButton = GetNode<Button>("Panel/MarginContainer/ScrollContainer/VBoxContainer/ActionSection/LoadSquadsButton");
        IconAtlas.Apply(_openSystemButton, "planet");
        IconAtlas.Apply(_plotCourseButton, "plot_course");
        IconAtlas.Apply(_divideButton, "divide");
        IconAtlas.Apply(_mergeButton, "merge");
        IconAtlas.Apply(_landSquadsButton, "land_squads");
        IconAtlas.Apply(_loadSquadsButton, "load_squads");
        _fleetList.ItemSelected += OnFleetListItemSelected;
        _openSystemButton.Pressed += () =>
        {
            if (_selectedPlanet != null) OpenSystemPressed?.Invoke(this, _selectedPlanet.Id);
        };
        _plotCourseButton.Pressed += () => InvokeSelectedFleetAction(PlotCoursePressed);
        _divideButton.Pressed += () => InvokeSelectedFleetAction(DivideFleetPressed);
        _mergeButton.Pressed += () => InvokeSelectedFleetAction(MergeFleetPressed);
        _landSquadsButton.Pressed += () => InvokeSelectedFleetAction(LandSquadsPressed);
        _loadSquadsButton.Pressed += () => InvokeSelectedFleetAction(LoadSquadsPressed);
        DisplayEmptyState();
    }

    public void DisplayPlanet(Planet planet, int? selectedFleetId = null)
    {
        if (planet == null)
        {
            DisplayEmptyState();
            return;
        }

        _selectedPlanet = planet;
        _selectedFleetId = selectedFleetId;
        Faction controllingFaction = planet.GetControllingFaction();
        _orbitingFleets.Clear();
        _orbitingFleets.AddRange(GameDataSingleton.Instance.Sector.Fleets.Values
            .Where(fleet => fleet.Planet == planet && fleet.TravelPhase == FleetTravelPhase.InOrbit)
            .OrderByDescending(fleet => fleet.Faction == GameDataSingleton.Instance.Sector.PlayerForce.Faction)
            .ThenBy(fleet => fleet.Id));
        int openRequests = GameDataSingleton.Instance.Sector.PlayerForce.Requests
            .Count(request => request.TargetPlanet == planet
                && request.Status is RequestStatus.Open or RequestStatus.InProgress);

        _nameLabel.Text = BuildSystemNameLabel(planet);
        _controlLabel.Text = controllingFaction != null
            ? $"Controlled by {controllingFaction.Name}"
            : "Control unknown";
        _planetDetailLabel.Text = $"{planet.Template.Name} | Pop {FormatPopulation(planet.Population)} | PDF {planet.PlanetaryDefenseForces:N0}";
        _orbitDetailLabel.Text = _orbitingFleets.Count == 1 ? "1 task force in orbit" : $"{_orbitingFleets.Count} task forces in orbit";
        _requestDetailLabel.Text = openRequests == 1 ? "1 active request" : $"{openRequests} active requests";

        if (_selectedFleetId.HasValue && !_orbitingFleets.Any(fleet => fleet.Id == _selectedFleetId.Value))
        {
            _selectedFleetId = null;
        }
        if (!_selectedFleetId.HasValue)
        {
            _selectedFleetId = _orbitingFleets.FirstOrDefault(IsActionablePlayerFleet)?.Id;
        }

        _isRefreshingFleetList = true;
        PopulateFleetList();
        SelectFleetListRow();
        _isRefreshingFleetList = false;
        RefreshActionState();
    }

    private static string BuildSystemNameLabel(Planet planet)
    {
        Subsector subsector = GameDataSingleton.Instance.Sector.Subsectors
            .FirstOrDefault(s => s.Planets.Contains(planet));
        if (subsector == null)
        {
            return planet.Name;
        }
        // The subsector capital is the most important planet in the subsector (PRD §4.1:
        // population drives capital selection, matching WarpLaneBuilder.SelectCapital).
        Planet capital = subsector.Planets
            .OrderByDescending(p => p.Population)
            .ThenByDescending(p => p.Importance)
            .ThenBy(p => p.Id)
            .First();
        return $"{planet.Name}, Subsector {capital.Name}";
    }

    public void DisplayEmptyState()
    {
        _selectedPlanet = null;
        _selectedFleetId = null;
        _orbitingFleets.Clear();
        _nameLabel.Text = "No System Selected";
        _controlLabel.Text = "Select a star system on the sector map";
        _planetDetailLabel.Text = "Planet data will appear here.";
        _orbitDetailLabel.Text = "Orbital task forces will appear here.";
        _requestDetailLabel.Text = "Active requests will appear here.";
        if (_selectedFleetDetailLabel != null) _selectedFleetDetailLabel.Text = "Select a task force for fleet actions.";
        _fleetList?.Clear();
        RefreshActionState();
    }

    private void PopulateFleetList()
    {
        _fleetList.Clear();
        foreach (TaskForce fleet in _orbitingFleets)
        {
            string ownership = fleet.Faction == GameDataSingleton.Instance.Sector.PlayerForce.Faction ? "Chapter" : fleet.Faction.Name;
            string shipText = fleet.Ships.Count == 1 ? "1 ship" : $"{fleet.Ships.Count} ships";
            int capacity = fleet.Ships.Sum(ship => ship.Template.SoldierCapacity);
            string prefix = fleet.Id == _selectedFleetId ? "> " : "";
            int index = _fleetList.AddItem($"{prefix}TF {fleet.Id} | {ownership} | {shipText} | Cap {capacity}", IconAtlas.GetIcon("fleet"), true);
            _fleetList.SetItemMetadata(index, fleet.Id);
            if (fleet.Faction != GameDataSingleton.Instance.Sector.PlayerForce.Faction)
            {
                _fleetList.SetItemCustomFgColor(index, Color.Color8(204, 83, 71));
            }
        }
    }

    private void SelectFleetListRow()
    {
        if (!_selectedFleetId.HasValue) return;

        int index = _orbitingFleets.FindIndex(fleet => fleet.Id == _selectedFleetId.Value);
        if (index >= 0)
        {
            _fleetList.Select(index);
        }
        else
        {
            _selectedFleetId = null;
        }
    }

    private void OnFleetListItemSelected(long index)
    {
        if (_isRefreshingFleetList) return;

        if (index < 0 || index >= _fleetList.ItemCount)
        {
            _selectedFleetId = null;
        }
        else
        {
            _selectedFleetId = _fleetList.GetItemMetadata((int)index).AsInt32();
        }

        _isRefreshingFleetList = true;
        PopulateFleetList();
        SelectFleetListRow();
        _isRefreshingFleetList = false;
        RefreshActionState();
    }

    private void RefreshActionState()
    {
        TaskForce selectedFleet = GetSelectedFleet();
        bool hasPlanet = _selectedPlanet != null;
        bool hasActionableFleet = selectedFleet != null && IsActionablePlayerFleet(selectedFleet);
        bool canDivide = hasActionableFleet && selectedFleet.Ships.Count > 1;
        bool canMerge = hasActionableFleet && FleetMergeDialogController.GetMergeCandidates(selectedFleet).Any();

        if (_openSystemButton != null) _openSystemButton.Disabled = !hasPlanet;
        if (_plotCourseButton != null) _plotCourseButton.Disabled = !hasActionableFleet;
        if (_divideButton != null) _divideButton.Disabled = !canDivide;
        if (_mergeButton != null) _mergeButton.Disabled = !canMerge;
        if (_landSquadsButton != null) _landSquadsButton.Disabled = !hasActionableFleet;
        if (_loadSquadsButton != null) _loadSquadsButton.Disabled = !hasActionableFleet;

        RefreshSelectedFleetDetail(selectedFleet, hasActionableFleet, canDivide, canMerge);
        RefreshActionTooltips(selectedFleet, hasPlanet, hasActionableFleet, canDivide, canMerge);
    }

    private TaskForce GetSelectedFleet()
    {
        if (!_selectedFleetId.HasValue) return null;
        return _orbitingFleets.FirstOrDefault(fleet => fleet.Id == _selectedFleetId.Value);
    }

    private static bool IsActionablePlayerFleet(TaskForce fleet)
    {
        return fleet != null
            && fleet.Faction == GameDataSingleton.Instance.Sector.PlayerForce.Faction
            && fleet.TravelPhase == FleetTravelPhase.InOrbit
            && fleet.Planet != null;
    }

    private void InvokeSelectedFleetAction(EventHandler<int> handler)
    {
        TaskForce fleet = GetSelectedFleet();
        if (!IsActionablePlayerFleet(fleet)) return;
        handler?.Invoke(this, fleet.Id);
    }

    private void RefreshSelectedFleetDetail(TaskForce selectedFleet, bool hasActionableFleet, bool canDivide, bool canMerge)
    {
        if (_selectedFleetDetailLabel == null) return;

        if (selectedFleet == null)
        {
            _selectedFleetDetailLabel.Text = _orbitingFleets.Count == 0
                ? "No task forces are in orbit."
                : "Select a task force for fleet actions.";
            return;
        }

        string ownership = selectedFleet.Faction == GameDataSingleton.Instance.Sector.PlayerForce.Faction
            ? "Chapter fleet"
            : selectedFleet.Faction.Name;
        int capacity = selectedFleet.Ships.Sum(ship => ship.Template.SoldierCapacity);
        int loaded = selectedFleet.Ships.Sum(ship => ship.LoadedSoldierCount);
        string commandState = hasActionableFleet
            ? $"Ready | Divide {(canDivide ? "yes" : "no")} | Merge {(canMerge ? "yes" : "no")}"
            : "Not commandable from this system";

        _selectedFleetDetailLabel.Text =
            $"TF {selectedFleet.Id} | {ownership} | {selectedFleet.Ships.Count} ships | {loaded}/{capacity} aboard\n{commandState}";
    }

    private void RefreshActionTooltips(TaskForce selectedFleet, bool hasPlanet, bool hasActionableFleet, bool canDivide, bool canMerge)
    {
        string noSystem = "Select a star system first.";
        string noFleet = selectedFleet == null
            ? "Select one of your task forces in orbit first."
            : "Only chapter task forces in orbit can receive orders here.";

        _openSystemButton.TooltipText = hasPlanet ? "Open the selected system's tactical screen." : noSystem;
        _plotCourseButton.TooltipText = hasActionableFleet ? "Plot a warp route for the selected task force." : noFleet;
        _divideButton.TooltipText = canDivide ? "Split ships out of the selected task force." :
            hasActionableFleet ? "This task force needs more than one ship to divide." : noFleet;
        _mergeButton.TooltipText = canMerge ? "Merge this task force with another compatible force in orbit." :
            hasActionableFleet ? "No compatible merge candidates are in orbit." : noFleet;
        _landSquadsButton.TooltipText = hasActionableFleet ? "Open the tactical screen to land squads." : noFleet;
        _loadSquadsButton.TooltipText = hasActionableFleet ? "Open the tactical screen to load squads." : noFleet;
    }

    private static string FormatPopulation(long populationInThousands)
    {
        double population = populationInThousands * 1000.0;
        if (population >= 1_000_000_000)
        {
            return $"{population / 1_000_000_000:0.##}B";
        }
        if (population >= 1_000_000)
        {
            return $"{population / 1_000_000:0.##}M";
        }
        return $"{population:N0}";
    }
}
