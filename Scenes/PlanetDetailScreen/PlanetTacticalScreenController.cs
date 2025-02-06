using Godot;
using OnlyWar.Models;
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

    public event EventHandler<Squad> SquadDoubleClicked;

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
        for(int i = 0; i < planet.Regions.Length; i++)
        {
            _tacticalRegions[i].Populate(planet.Regions[i]);
        }
    }

    private void OnTacticalRegionPressed(object sender, Region region)
    {
        // populate squad list
        PopulateRegionSquadList(region);
        // populate region data
        PopulateRegionDetails(region);
        // populate buttons
    }

    private void OnSquadTreeItemDoubleClicked(object sender, Vector2I e)
    {
        switch (e.X)
        {
            case 0:
                // Unit
                /*TreeItem item = (TreeItem)sender;
                _selectedLandedUnit = GameDataSingleton.Instance.Sector.PlayerForce.Army.OrderOfBattle.ChildUnits.First(u => u.Id == e.Y);
                _selectedLandedSquad = null;*/
                break;
            case 1:
                // Squad
                Squad squad = GameDataSingleton.Instance.Sector.PlayerForce.Army.SquadMap[e.Y];
                SquadDoubleClicked.Invoke(this, squad);
                break;
        }
    }

    private void PopulateRegionSquadList(Region region)
    {
        List<TreeNode> unitsInRegion = new List<TreeNode>();
        Faction playerFaction = GameDataSingleton.Instance.Sector.PlayerForce.Faction;

        if (region.RegionFactionMap.ContainsKey(playerFaction.Id))
        {
            var unitSquadMap = region.RegionFactionMap[playerFaction.Id].LandedSquads.GroupBy(s => s.ParentUnit).ToDictionary(group => group.Key, group => group.ToList());
            unitsInRegion = GetUnitTreeNodes(unitSquadMap);

        }
        _view.PopulateRegionSquadTree(unitsInRegion);
    }

    private static List<TreeNode> GetUnitTreeNodes(Dictionary<Unit, List<Squad>> unitSquadMap)
    {
        List<TreeNode> unitTreeNodes = new List<TreeNode>();
        foreach (var kvp in unitSquadMap)
        {
            List<TreeNode> squads = new List<TreeNode>();
            foreach (Squad squad in kvp.Value)
            {
                if (squad.Members.Count > 0)
                {
                    squads.Add(new TreeNode(squad.Id, squad.Name, new List<TreeNode>()));
                }
            }
            if (squads.Count > 0)
            {
                TreeNode unit = new TreeNode(kvp.Key.Id, kvp.Key.Name, squads);
                unitTreeNodes.Add(unit);
            }
        }
        return unitTreeNodes;
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
        if(civilianPopulation > 0)
        {
            lines.Add(new Tuple<string, string>("Civilian Population", civilianPopulation.ToString()));
        }

        if(xenosRegionFaction != null && xenosRegionFaction.IsPublic)
        {
            long xenosPopulation = xenosRegionFaction.Population/1000 * 1000;
            if (xenosPopulation > 0)
            {
                lines.Add(new Tuple<string, string>("Xenos Strength", xenosPopulation.ToString()));
            }
            else if(xenosRegionFaction.Population > 0)
            {
                lines.Add(new Tuple<string, string>("Xenos Strength", "Low"));
            }
            {

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
}
