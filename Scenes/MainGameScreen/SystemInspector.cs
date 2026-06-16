using Godot;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using System.Linq;

public partial class SystemInspector : Control
{
    private Label _nameLabel;
    private Label _controlLabel;
    private Label _planetDetailLabel;
    private Label _orbitDetailLabel;
    private Label _requestDetailLabel;
    private Button _plotCourseButton;
    private Button _manageFleetsButton;

    public override void _Ready()
    {
        _nameLabel = GetNode<Label>("Panel/MarginContainer/VBoxContainer/Header/SystemNameLabel");
        _controlLabel = GetNode<Label>("Panel/MarginContainer/VBoxContainer/ControlLabel");
        _planetDetailLabel = GetNode<Label>("Panel/MarginContainer/VBoxContainer/PlanetSection/PlanetDetailLabel");
        _orbitDetailLabel = GetNode<Label>("Panel/MarginContainer/VBoxContainer/OrbitSection/OrbitDetailLabel");
        _requestDetailLabel = GetNode<Label>("Panel/MarginContainer/VBoxContainer/RequestSection/RequestDetailLabel");
        _plotCourseButton = GetNode<Button>("Panel/MarginContainer/VBoxContainer/ActionSection/PlotCourseButton");
        _manageFleetsButton = GetNode<Button>("Panel/MarginContainer/VBoxContainer/ActionSection/ManageFleetsButton");
        IconAtlas.Apply(GetNode<Button>("Panel/MarginContainer/VBoxContainer/Header/SettingsButton"), "settings");
        IconAtlas.Apply(_plotCourseButton, "plot_course");
        IconAtlas.Apply(_manageFleetsButton, "fleet");
    }

    public void DisplayPlanet(Planet planet)
    {
        if (planet == null)
        {
            DisplayEmptyState();
            return;
        }

        Faction controllingFaction = planet.GetControllingFaction();
        int orbitingFleets = GameDataSingleton.Instance.Sector.Fleets.Values
            .Count(fleet => fleet.Planet == planet && fleet.TravelPhase == FleetTravelPhase.InOrbit);
        int openRequests = GameDataSingleton.Instance.Sector.PlayerForce.Requests
            .Count(request => request.TargetPlanet == planet && request.DateRequestFulfilled == null);

        _nameLabel.Text = planet.Name;
        _controlLabel.Text = controllingFaction != null
            ? $"Controlled by {controllingFaction.Name}"
            : "Control unknown";
        _planetDetailLabel.Text = $"{planet.Template.Name} | Pop {FormatPopulation(planet.Population)} | PDF {planet.PlanetaryDefenseForces:N0}";
        _orbitDetailLabel.Text = orbitingFleets == 1 ? "1 task force in orbit" : $"{orbitingFleets} task forces in orbit";
        _requestDetailLabel.Text = openRequests == 1 ? "1 active request" : $"{openRequests} active requests";
    }

    public void DisplayEmptyState()
    {
        _nameLabel.Text = "No System Selected";
        _controlLabel.Text = "Select a star system on the sector map";
        _planetDetailLabel.Text = "Planet data will appear here.";
        _orbitDetailLabel.Text = "Orbital task forces will appear here.";
        _requestDetailLabel.Text = "Active requests will appear here.";
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
