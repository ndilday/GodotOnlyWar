using Godot;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class PlanetTacticalScreenController : DialogController
{
    private PlanetTacticalScreenView _view;
    private TacticalRegionController[] _tacticalRegions;
    private ButtonGroup _buttonGroup;

    public event EventHandler<Region> RegionDoubleClicked;
    public event EventHandler<Squad> OrbitalSquadDoubleClicked;

    public override void _Ready()
    {
        base._Ready();
        _buttonGroup = new ButtonGroup();
        _view = GetNode<PlanetTacticalScreenView>("PlanetTacticalScreenView");
        _view.SquadTreeItemDoubleClicked += OnSquadTreeItemDoubleClicked;
        _view.ClearRegionData();
        _tacticalRegions = new TacticalRegionController[16];
        for(int i=1; i<=16; i++)
        {
            _tacticalRegions[i - 1] = GetNode<TacticalRegionController>($"PlanetTacticalScreenView/TacticalRegionPanel/TacticalRegionController{i}");
            _tacticalRegions[i - 1].AddToButtonGroup(_buttonGroup);
            _tacticalRegions[i - 1].TacticalRegionPressed += OnTacticalRegionPressed;
        }
    }

    public void PopulatePlanetData(Planet planet)
    {
        PopulateOrbitalSquadList(planet.OrbitingTaskForceList);
        for(int i = 0; i < planet.Regions.Length; i++)
        {
            _tacticalRegions[i].Populate(planet.Regions[i]);
        }
    }

    private void OnTacticalRegionPressed(object sender, Region region)
    {
        // populate region data
        PopulateRegionDetails(region);
        // populate buttons
    }

    private void OnSquadTreeItemDoubleClicked(object sender, Vector2I e)
    {
        switch (e.X)
        {
            case 0:
                // Ship
                /*TreeItem item = (TreeItem)sender;
                _selectedLandedUnit = GameDataSingleton.Instance.Sector.PlayerForce.Army.OrderOfBattle.ChildUnits.First(u => u.Id == e.Y);
                _selectedLandedSquad = null;*/
                break;
            case 1:
                // Squad
                Squad squad = GameDataSingleton.Instance.Sector.PlayerForce.Army.SquadMap[e.Y];
                OrbitalSquadDoubleClicked.Invoke(this, squad);
                break;
        }
    }

    private void PopulateOrbitalSquadList(IReadOnlyList<TaskForce> orbitingFleets)
    {
        List<TreeNode> fleets = new List<TreeNode>();
        Faction playerFaction = GameDataSingleton.Instance.Sector.PlayerForce.Faction;

        foreach(TaskForce taskForce in orbitingFleets)
        {
            if(taskForce.Faction == playerFaction)
            {
                foreach(Ship ship in taskForce.Ships)
                {
                    if(ship.LoadedSquads.Any())
                    {
                        List<TreeNode> squads = new List<TreeNode>();
                        foreach (Squad squad in ship.LoadedSquads)
                        {
                            if (squad.Members.Count > 0)
                            {
                                squads.Add(new TreeNode(squad.Id, squad.Name, new List<TreeNode>()));
                            }
                        }
                        if(squads.Count > 0)
                        {
                            TreeNode shipNode = new TreeNode(ship.Id, ship.Name, squads);
                            fleets.Add(shipNode);
                        }
                    }
                }
            }
        }
        _view.PopulateRegionSquadTree(fleets);
    }

    private void PopulateRegionDetails(Region region)
    {
        List<Tuple<string, string>> lines = [];
        int i = region.Id % 16;
        lines.Add(new Tuple<string, string>("Name", region.Name));

        RegionFaction playerRegionFaction = region.RegionFactionMap.Values.FirstOrDefault(rf => rf.PlanetFaction.Faction.IsPlayerFaction);
        RegionFaction defaultFaction = region.RegionFactionMap.Values.FirstOrDefault(rf => rf.PlanetFaction.Faction.IsDefaultFaction);
        RegionFaction xenosRegionFaction = region.RegionFactionMap.Values.FirstOrDefault(rf => !rf.PlanetFaction.Faction.IsPlayerFaction && !rf.PlanetFaction.Faction.IsDefaultFaction);

        long civilianPopulation = 0;
        if (defaultFaction != null)
        {
            civilianPopulation += defaultFaction.Population;
        }
        if (playerRegionFaction != null)
        {
            civilianPopulation += playerRegionFaction.Population;
        }
        if (xenosRegionFaction != null && !xenosRegionFaction.IsPublic)
        {
            // hidden xenos are added to civilian population
            civilianPopulation += xenosRegionFaction.Population;
        }
        if (civilianPopulation > 0)
        {
            lines.Add(new Tuple<string, string>("Civilian Population", civilianPopulation.ToString()));
        }

        if(xenosRegionFaction != null && xenosRegionFaction.IsPublic)
        {
            lines.Add(new Tuple<string, string>("Xenos Population", xenosRegionFaction.GetPopulationDescription()));
            if (region.IntelligenceLevel > 1)
            {
                lines.Add(new Tuple<string, string>("Xenos Defenses", GetDefenseLevelDescription(xenosRegionFaction.Entrenchment)));
                lines.Add(new Tuple<string, string>("Xenos Listening Posts", GetDefenseLevelDescription(xenosRegionFaction.Detection)));
                lines.Add(new Tuple<string, string>("Xenos Anti Aircraft Coverage", GetDefenseLevelDescription(xenosRegionFaction.AntiAir)));
            }
        }

        int playerPopulation = 0;
        if (playerRegionFaction != null && playerRegionFaction.LandedSquads.Any())
        {
            playerPopulation = playerRegionFaction.LandedSquads.Sum(s => s.Members.Count());
        }
        lines.Add(new Tuple<string, string>("Marines in Region", playerPopulation.ToString()));

        _view.PopulateRegionData(lines);
    }

    private string GetDefenseLevelDescription(int level)
    {
        switch (level)
        {
            case 0:
                return "None";
            case 1:
            case 2:
                return "Minimal";
            case 3:
            case 4:
                return "Mediocre";
            case 5:
            case 6:
                return "Moderate";
            case 7:
            case 8:
                return "Heavy";
            default:
                return "Massive";
        }
    }
}
