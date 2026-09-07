using Godot;
using OnlyWar.Application;
using OnlyWar.Helpers.UI;
using System;
using System.Collections.Generic;

public partial class SystemInspector : Control
{
    // The inspector renders one application projection; which fleets are in orbit, which one is
    // selected and whether its actions are legal are all decided behind that boundary.
    private ISystemInspectorApplication _application;

    public void Configure(ISystemInspectorApplication application) =>
        _application = application ?? throw new ArgumentNullException(nameof(application));

    public event EventHandler<int> OpenSystemPressed;
    public event EventHandler<int> PlotCoursePressed;
    public event EventHandler<int> DivideFleetPressed;
    public event EventHandler<int> MergeFleetPressed;
    public event EventHandler<int> LandSquadsPressed;
    public event EventHandler<int> LoadSquadsPressed;
    public event EventHandler<int> AnswerGovernorRequestPressed;

    private Label _nameLabel;
    private Label _controlLabel;
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
    private Button _answerRequestButton;
    private VBoxContainer _dossierSection;
    private VBoxContainer _dossierContent;
    private int? _selectedPlanetId;
    private int? _selectedFleetId;
    private bool _showDossier;
    private SystemInspectorView _current = SystemInspectorView.Empty;
    private bool _isRefreshingFleetList;

    public override void _Ready()
    {
        _nameLabel = GetNode<Label>("Panel/MarginContainer/ScrollContainer/VBoxContainer/Header/SystemNameLabel");
        _controlLabel = GetNode<Label>("Panel/MarginContainer/ScrollContainer/VBoxContainer/ControlLabel");
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
        _answerRequestButton = GetNode<Button>("Panel/MarginContainer/ScrollContainer/VBoxContainer/RequestSection/AnswerRequestButton");
        _dossierSection = GetNode<VBoxContainer>("Panel/MarginContainer/ScrollContainer/VBoxContainer/DossierSection");
        _dossierContent = GetNode<VBoxContainer>("Panel/MarginContainer/ScrollContainer/VBoxContainer/DossierSection/DossierContent");
        IconAtlas.Apply(_openSystemButton, "planet");
        IconAtlas.Apply(_plotCourseButton, "plot_course");
        IconAtlas.Apply(_divideButton, "divide");
        IconAtlas.Apply(_mergeButton, "merge");
        IconAtlas.Apply(_landSquadsButton, "land_squads");
        IconAtlas.Apply(_loadSquadsButton, "load_squads");
        _fleetList.ItemSelected += OnFleetListItemSelected;
        _openSystemButton.Pressed += () =>
        {
            if (_selectedPlanetId.HasValue) OpenSystemPressed?.Invoke(this, _selectedPlanetId.Value);
        };
        _plotCourseButton.Pressed += () => InvokeSelectedFleetAction(PlotCoursePressed);
        _divideButton.Pressed += () => InvokeSelectedFleetAction(DivideFleetPressed);
        _mergeButton.Pressed += () => InvokeSelectedFleetAction(MergeFleetPressed);
        _landSquadsButton.Pressed += () => InvokeSelectedFleetAction(LandSquadsPressed);
        _loadSquadsButton.Pressed += () => InvokeSelectedFleetAction(LoadSquadsPressed);
        _answerRequestButton.Pressed += () =>
        {
            if (_selectedPlanetId.HasValue)
                AnswerGovernorRequestPressed?.Invoke(this, _selectedPlanetId.Value);
        };
        DisplayEmptyState();
    }

    public void DisplayPlanet(int? planetId, int? selectedFleetId = null) =>
        DisplaySystemContext(planetId, selectedFleetId, showDossier: true);

    public void DisplayFleetContext(int? planetId, int? selectedFleetId = null) =>
        DisplaySystemContext(planetId, selectedFleetId, showDossier: false);

    /// <summary>True when the last requested selection resolved to a live world.</summary>
    public bool HasSystem => _current.HasSystem;

    private void DisplaySystemContext(int? planetId, int? selectedFleetId, bool showDossier)
    {
        _selectedPlanetId = planetId;
        _selectedFleetId = selectedFleetId;
        _showDossier = showDossier;
        Render();
    }

    private void Render()
    {
        _current = _application == null
            ? SystemInspectorView.Empty
            : _application.QuerySystemInspector(_selectedPlanetId, _selectedFleetId, _showDossier);
        if (!_current.HasSystem)
        {
            RenderEmptyState();
            return;
        }

        _selectedFleetId = _current.SelectedFleetId;
        _nameLabel.Text = _current.SystemName;
        _controlLabel.Text = _current.ControlText;
        _orbitDetailLabel.Text = _current.OrbitDetailText;
        _requestDetailLabel.Text = _current.RequestDetailText;
        _answerRequestButton.Visible = _current.HasAnswerableRequest;
        _dossierSection.Visible = _showDossier;
        if (_showDossier && _current.Dossier != null)
        {
            RenderDossier(_current.Dossier);
        }
        else
        {
            Clear(_dossierContent);
        }

        _isRefreshingFleetList = true;
        PopulateFleetList();
        SelectFleetListRow();
        _isRefreshingFleetList = false;
        RefreshActionState();
    }

    public void DisplayEmptyState()
    {
        _selectedPlanetId = null;
        _selectedFleetId = null;
        _showDossier = false;
        _current = SystemInspectorView.Empty;
        RenderEmptyState();
    }

    private void RenderEmptyState()
    {
        _selectedFleetId = null;
        _nameLabel.Text = "No System Selected";
        _controlLabel.Text = "Select a star system on the sector map";
        _orbitDetailLabel.Text = "Orbital task forces will appear here.";
        _requestDetailLabel.Text = "Active requests will appear here.";
        if (_selectedFleetDetailLabel != null)
            _selectedFleetDetailLabel.Text = "No task forces are in orbit.";
        _fleetList?.Clear();
        if (_answerRequestButton != null) _answerRequestButton.Visible = false;
        if (_dossierSection != null) _dossierSection.Visible = false;
        Clear(_dossierContent);
        RefreshActionState();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // ScrollContainer stops consuming wheel events at its scroll limits. Keep
        // those events inside the inspector instead of letting the map interpret
        // them as zoom commands.
        if (!IsVisibleInTree()
            || @event is not InputEventMouseButton mouse
            || !mouse.Pressed
            || mouse.ButtonIndex is not (MouseButton.WheelUp or MouseButton.WheelDown)
            || !GetGlobalRect().HasPoint(GetViewport().GetMousePosition()))
        {
            return;
        }

        GetViewport().SetInputAsHandled();
    }

    private void RenderDossier(WorldDossierView dossier)
    {
        Clear(_dossierContent);
        foreach (DossierCardView card in dossier.ProfileCards)
        {
            _dossierContent.AddChild(DossierCard.Create(card));
        }
        foreach (DossierCardView card in dossier.StrengthCards)
        {
            _dossierContent.AddChild(DossierCard.Create(card));
        }
    }

    private static void Clear(Node parent)
    {
        if (parent == null) return;
        foreach (Node child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void PopulateFleetList()
    {
        _fleetList.Clear();
        foreach (OrbitingFleetRow fleet in _current.OrbitingFleets)
        {
            string prefix = fleet.FleetId == _selectedFleetId ? "> " : "";
            int index = _fleetList.AddItem(
                $"{prefix}{fleet.Label}", IconAtlas.GetIcon("fleet"), true);
            _fleetList.SetItemMetadata(index, fleet.FleetId);
            if (!fleet.IsPlayerFleet)
            {
                _fleetList.SetItemCustomFgColor(index, Color.Color8(204, 83, 71));
            }
        }
    }

    private void SelectFleetListRow()
    {
        if (!_selectedFleetId.HasValue) return;

        IReadOnlyList<OrbitingFleetRow> fleets = _current.OrbitingFleets;
        for (int index = 0; index < fleets.Count; index++)
        {
            if (fleets[index].FleetId == _selectedFleetId.Value)
            {
                _fleetList.Select(index);
                return;
            }
        }

        _selectedFleetId = null;
    }

    private void OnFleetListItemSelected(long index)
    {
        if (_isRefreshingFleetList) return;

        _selectedFleetId = index < 0 || index >= _fleetList.ItemCount
            ? null
            : _fleetList.GetItemMetadata((int)index).AsInt32();
        Render();
    }

    private void RefreshActionState()
    {
        FleetActionAvailability actions = _current.SelectedFleetActions;
        bool hasPlanet = _current.HasSystem;

        if (_openSystemButton != null) _openSystemButton.Disabled = !hasPlanet;
        if (_plotCourseButton != null) _plotCourseButton.Disabled = !actions.CanPlotCourse;
        if (_divideButton != null) _divideButton.Disabled = !actions.CanDivide;
        if (_mergeButton != null) _mergeButton.Disabled = !actions.CanMerge;
        if (_landSquadsButton != null) _landSquadsButton.Disabled = !actions.IsActionable;
        if (_loadSquadsButton != null) _loadSquadsButton.Disabled = !actions.IsActionable;

        if (_selectedFleetDetailLabel != null && _current.HasSystem)
        {
            _selectedFleetDetailLabel.Text = _current.SelectedFleetDetail;
        }
        RefreshActionTooltips(hasPlanet, actions);
    }

    private void InvokeSelectedFleetAction(EventHandler<int> handler)
    {
        if (!_current.SelectedFleetActions.IsActionable || !_current.SelectedFleetId.HasValue)
        {
            return;
        }

        handler?.Invoke(this, _current.SelectedFleetId.Value);
    }

    private void RefreshActionTooltips(bool hasPlanet, FleetActionAvailability actions)
    {
        string noSystem = "Select a star system first.";
        string noFleet = _current.SelectedFleetId == null
            ? "Select one of your task forces in orbit first."
            : "Only chapter task forces in orbit can receive orders here.";

        _openSystemButton.TooltipText = hasPlanet
            ? "Open the selected system's tactical screen."
            : noSystem;
        _plotCourseButton.TooltipText = actions.CanPlotCourse
            ? "Plot a warp route for the selected task force."
            : noFleet;
        _divideButton.TooltipText = actions.CanDivide
            ? "Split ships out of the selected task force."
            : actions.IsActionable ? "This task force needs more than one ship to divide." : noFleet;
        _mergeButton.TooltipText = actions.CanMerge
            ? "Merge this task force with another compatible force in orbit."
            : actions.IsActionable ? "No compatible merge candidates are in orbit." : noFleet;
        _landSquadsButton.TooltipText = actions.IsActionable
            ? "Open the tactical screen to land squads."
            : noFleet;
        _loadSquadsButton.TooltipText = actions.IsActionable
            ? "Open the tactical screen to load squads."
            : noFleet;
    }
}
