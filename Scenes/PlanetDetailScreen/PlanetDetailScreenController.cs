using Godot;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class PlanetDetailScreenController : Control
{
	PlanetDetailScreenView _view;
	public event EventHandler CloseButtonPressed;

	public override void _Ready()
	{
		_view = GetNode<PlanetDetailScreenView>("PlanetDetailScreenView");
		_view.CloseButtonPressed += (object sender, EventArgs e) => CloseButtonPressed?.Invoke(this, e);
	}

	public void PopulateFleetTree(Planet planet)
	{
		List<TreeNode> shipList = new List<TreeNode>();
		foreach(TaskForce taskForce in planet.OrbitingTaskForceList)
		{
			if(taskForce.Faction == GameDataSingleton.Instance.Sector.PlayerForce.Faction)
			{
				foreach(Ship ship in taskForce.Ships)
				{
					List<TreeNode> nodes = new List<TreeNode>();
					foreach(Squad squad in ship.LoadedSquads)
					{
						TreeNode node = new TreeNode(squad.Id, squad.Name, new List<TreeNode>());
						nodes.Add(node);
					}
					TreeNode shipNode = new TreeNode(ship.Id, ship.Name, nodes);
					shipList.Add(shipNode);
				}
			}
		}
		_view.PopulateShipTree(shipList);
	}

	public void PopulateRegionTree(Planet planet)
	{
		List<TreeNode> regionList = new List<TreeNode>();
		Faction playerFaction = GameDataSingleton.Instance.Sector.PlayerForce.Faction;
		for(int i = 0; i < 16; i++)
		{
			Region region = planet.Regions[i];
            List<TreeNode> unitsInRegion = new List<TreeNode>();
            if (region.RegionFactionMap.ContainsKey(playerFaction.Id))
			{
				var unitSquadMap = region.RegionFactionMap[playerFaction.Id].LandedSquads.GroupBy(s => s.ParentUnit).ToDictionary(group => group.Key, group => group.ToList());
				foreach(var kvp in unitSquadMap)
				{
					List<TreeNode> squads = new List<TreeNode>();
					foreach (Squad squad in kvp.Value)
					{
						if (squad.Members.Count > 0)
						{
							squads.Add(new TreeNode(squad.Id, squad.Name, new List<TreeNode>()));
						}
					}
					TreeNode unit = new TreeNode(kvp.Key.Id, kvp.Key.Name, squads);
					unitsInRegion.Add(unit);
				}
				
			}
            TreeNode regionNode = new TreeNode(region.Id, GetRegionName(planet, i), unitsInRegion);
            regionList.Add(regionNode);
		}
		_view.PopulateRegionTree(regionList);
	}

	private string GetRegionName(Planet planet, int i)
	{
		switch(i)
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
