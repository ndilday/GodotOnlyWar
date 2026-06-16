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
    public event EventHandler ManageFleetsPressed;

    private Label _nameLabel;
    private Label _controlLabel;
    private Label _planetDetailLabel;
    private Label _orbitDetailLabel;
    private Label _requestDetailLabel;
    private ItemList _fleetList;
    private Button _openSystemButton;
    private Button _plotCourseButton;
    private Button _divideButton;
    private Button _mergeButton;
    private Button _landSquadsButton;
    private Button _loadSquadsButton;
    private Button _manageFleetsButton;
    private Planet _selectedPlanet;
    private readonly List<TaskForce> _orbitingFleets = [];
    private int? _selectedFleetId;

    public override void _Ready()
    {
        _nameLabel = GetNode<Label>("Panel/MarginContainer/VBoxContainer/Header/SystemNameLabel");
        _controlLabel = GetNode<Label>("Panel/MarginContainer/VBoxContainer/ControlLabel");
        _planetDetailLabel = GetNode<Label>("Panel/MarginContainer/VBoxContainer/PlanetSection/PlanetDetailLabel");
        _orbitDetailLabel = GetNode<Label>("Panel/MarginContainer/VBoxContainer/OrbitSection/OrbitDetailLabel");
        _requestDetailLabel = GetNode<Label>("Panel/MarginContainer/VBoxContainer/RequestSection/RequestDetailLabel");
        _fleetList = GetNode<ItemList>("Panel/MarginContainer/VBoxContainer/OrbitSection/FleetList");
        _openSystemButton = GetNode<Button>("Panel/MarginContainer/VBoxContainer/ActionSection/OpenSystemButton");
        _plotCourseButton = GetNode<Button>("Panel/MarginContainer/VBoxContainer/ActionSection/PlotCourseButton");
        _divideButton = GetNode<Button>("Panel/MarginContainer/VBoxContainer/ActionSection/DivideButton");
        _mergeButton = GetNode<Button>("Panel/MarginContainer/VBoxContainer/ActionSection/MergeButton");
        _landSquadsButton = GetNode<Button>("Panel/MarginContainer/VBoxContainer/ActionSection/LandSquadsButton");
        _loadSquadsButton = GetNode<Button>("Panel/MarginContainer/VBoxContainer/ActionSection/LoadSquadsButton");
        _manageFleetsButton = GetNode<Button>("Panel/MarginContainer/VBoxContainer/ActionSection/ManageFleetsButton");
        IconAtlas.Apply(GetNode<Button>("Panel/MarginContainer/VBoxContainer/Header/SettingsButton"), "settings");
        IconAtlas.Apply(_openSystemButton, "planet");
        IconAtlas.Apply(_plotCourseButton, "plot_course");
        IconAtlas.Apply(_divideButton, "divide");
        IconAtlas.Apply(_mergeButton, "merge");
        IconAtlas.Apply(_landSquadsButton, "land_squads");
        IconAtlas.Apply(_loadSquadsButton, "load_squads");
        IconAtlas.Apply(_manageFleetsButton, "fleet");
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
        _manageFleetsButton.Pressed += () => ManageFleetsPressed?.Invoke(this, EventArgs.Empty);
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
            .Count(request => request.TargetPlanet == planet && request.DateRequestFulfilled == null);

        _nameLabel.Text = planet.Name;
        _controlLabel.Text = controllingFaction != null
            ? $"Controlled by {controllingFaction.Name}"
            : "Control unknown";
        _planetDetailLabel.Text = $"{planet.Template.Name} | Pop {FormatPopulation(planet.Population)} | PDF {planet.PlanetaryDefenseForces:N0}";
        _orbitDetailLabel.Text = _orbitingFleets.Count == 1 ? "1 task force in orbit" : $"{_orbitingFleets.Count} task forces in orbit";
        _requestDetailLabel.Text = openRequests == 1 ? "1 active request" : $"{openRequests} active requests";
        PopulateFleetList();
        SelectFleetListRow();
        if (!_selectedFleetId.HasValue)
        {
            SelectFirstActionableFleet();
        }
        RefreshActionState();
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
            int index = _fleetList.AddItem($"TF {fleet.Id} | {ownership} | {shipText} | Cap {capacity}", IconAtlas.GetIcon("fleet"), true);
            _fleetList.SetItemMetadata(index, fleet.Id);
        }
    }

    private void SelectFirstActionableFleet()
    {
        TaskForce firstPlayerFleet = _orbitingFleets.FirstOrDefault(IsActionablePlayerFleet);
        if (firstPlayerFleet == null) return;

        _selectedFleetId = firstPlayerFleet.Id;
        int index = _orbitingFleets.FindIndex(fleet => fleet.Id == firstPlayerFleet.Id);
        if (index >= 0)
        {
            _fleetList.Select(index);
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
        if (index < 0 || index >= _fleetList.ItemCount)
        {
            _selectedFleetId = null;
        }
        else
        {
            _selectedFleetId = _fleetList.GetItemMetadata((int)index).AsInt32();
        }

        RefreshActionState();
    }

    private void RefreshActionState()
    {
        TaskForce selectedFleet = GetSelectedFleet();
        bool hasPlanet = _selectedPlanet != null;
        bool hasActionableFleet = selectedFleet != null && IsActionablePlayerFleet(selectedFleet);

        if (_openSystemButton != null) _openSystemButton.Disabled = !hasPlanet;
        if (_plotCourseButton != null) _plotCourseButton.Disabled = !hasActionableFleet;
        if (_divideButton != null) _divideButton.Disabled = !hasActionableFleet || selectedFleet.Ships.Count <= 1;
        if (_mergeButton != null) _mergeButton.Disabled = !hasActionableFleet || !FleetMergeDialogController.GetMergeCandidates(selectedFleet).Any();
        if (_landSquadsButton != null) _landSquadsButton.Disabled = !hasActionableFleet;
        if (_loadSquadsButton != null) _loadSquadsButton.Disabled = !hasActionableFleet;
        if (_manageFleetsButton != null) _manageFleetsButton.Disabled = false;
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
