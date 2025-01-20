using Godot;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class PlanetTacticalScreenController : Control
{
	private PlanetTacticalScreenView _view;
	private TacticalRegionController[] _tacticalRegions;
	private ButtonGroup _buttonGroup;

	public event EventHandler CloseButtonPressed;

	public override void _Ready()
	{
		_view = GetNode<PlanetTacticalScreenView>("PlanetTacticalScreenView");
		_view.CloseButtonPressed += (object sender, EventArgs e) => CloseButtonPressed?.Invoke(this, e);
		_tacticalRegions = new TacticalRegionController[16];
		for(int i=1; i<=16; i++)
		{
			_tacticalRegions[i - 1] = GetNode<TacticalRegionController>($"PlanetTacticalScreenView/TacticalRegionPanel/TacticalRegionController{i}");
			_tacticalRegions[i - 1].AddToButtonGroup(_buttonGroup);
			_tacticalRegions[i - 1].TacticalRegionPressed += OnTacticalRegionPressed;
		}
		_buttonGroup = new ButtonGroup();
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

	private void PopulateRegionSquadList(Region region)
	{
        List<TreeNode> unitsInRegion = new List<TreeNode>();
        Faction playerFaction = GameDataSingleton.Instance.Sector.PlayerForce.Faction;

        if (region.RegionFactionMap.ContainsKey(playerFaction.Id))
        {
            var unitSquadMap = region.RegionFactionMap[playerFaction.Id].LandedSquads.GroupBy(s => s.ParentUnit).ToDictionary(group => group.Key, group => group.ToList());
            unitsInRegion = GetUnitTreeNodes(unitSquadMap);

        }
        _view.PopulateRegionTree(unitsInRegion);
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
        lines.Add(new Tuple<string, string>("Name", GetRegionName(region.Planet, i)));

        RegionFaction playerRegionFaction = region.RegionFactionMap.Values.FirstOrDefault(rf => rf.PlanetFaction.Faction.IsPlayerFaction);
        RegionFaction defaultFaction = region.RegionFactionMap.Values.FirstOrDefault(rf => rf.PlanetFaction.Faction.IsDefaultFaction);
        RegionFaction xenosRegionFaction = region.RegionFactionMap.Values.FirstOrDefault(rf => !rf.PlanetFaction.Faction.IsPlayerFaction && !rf.PlanetFaction.Faction.IsDefaultFaction);

        long population = 0;
        if (defaultFaction != null)
        {
            population += defaultFaction.Population;
        }
        if (playerRegionFaction != null)
        {
            population += playerRegionFaction.Population;
        }
        if (xenosRegionFaction != null && !xenosRegionFaction.IsPublic)
        {
            // hidden xenos are added to civilian population
            population += xenosRegionFaction.Population;
        }
        lines.Add(new Tuple<string, string>("Civilian Population", population.ToString()));

        int playerPopulation = 0;
        if (playerRegionFaction != null && playerRegionFaction.LandedSquads.Any())
        {
            playerPopulation = playerRegionFaction.LandedSquads.Sum(s => s.Members.Count());
        }
        lines.Add(new Tuple<string, string>("Marines in Region", playerPopulation.ToString()));

        
        if (xenosRegionFaction != null && xenosRegionFaction.IsPublic)
        {
            // TODO: do we need to add squads?
            lines.Add(new Tuple<string, string>("Estimated Xenos Infestation", xenosRegionFaction.Population.ToString()));
        }

        _view.PopulateRegionData(lines);
    }

    private string GetRegionName(Planet planet, int i)
    {
        switch (i)
        {
            case 0:
                return $"{planet.Name} Alpha";
            case 1:
                return $"{planet.Name} Beta";
            case 2:
                return $"{planet.Name} Gamma";
            case 3:
                return $"{planet.Name} Delta";
            case 4:
                return $"{planet.Name} Epsilon";
            case 5:
                return $"{planet.Name} Zeta";
            case 6:
                return $"{planet.Name} Eta";
            case 7:
                return $"{planet.Name} Theta";
            case 8:
                return $"{planet.Name} Iota";
            case 9:
                return $"{planet.Name} Kappa";
            case 10:
                return $"{planet.Name} Lambda";
            case 11:
                return $"{planet.Name} Mu";
            case 12:
                return $"{planet.Name} Nu";
            case 13:
                return $"{planet.Name} Xi";
            case 14:
                return $"{planet.Name} Omicron";
            case 15:
                return $"{planet.Name} Pi";
            default:
                return $"{planet.Name} Omega";
        }
    }
}