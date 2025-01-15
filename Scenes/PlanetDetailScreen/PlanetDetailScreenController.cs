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
	private PlanetDetailScreenView _view;
	private int? _selectedShip;
	private int? _selectedLoadedSquad;
	private int? _selectedRegion;
	private int? _selectedUnit;
	private int? _selectedSquad;

	public event EventHandler CloseButtonPressed;

	public override void _Ready()
	{
		_view = GetNode<PlanetDetailScreenView>("PlanetDetailScreenView");
		_view.CloseButtonPressed += (object sender, EventArgs e) => CloseButtonPressed?.Invoke(this, e);
		_view.RegionTreeDeselected += OnRegionTreeDeselected;
		_view.RegionTreeItemClicked += OnRegionTreeItemClicked;
		_view.FleetTreeDeselected += OnFleetTreeDeselected;
		_view.FleetTreeItemClicked += OnFleetTreeItemClicked;
	}

	public void PopulatePlanetData(Planet planet)
	{
		PopulatePlanetDetails(planet);
		PopulateFleetTree(planet);
		PopulateRegionTree(planet);
	}

	private void PopulateFleetTree(Planet planet)
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

	private void PopulateRegionTree(Planet planet)
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

	private void PopulatePlanetDetails(Planet planet)
	{
		List<Tuple<string, string>> lines = [];
		lines.Add(new Tuple<string, string>("Name", planet.Name));
		if (planet.ControllingFaction.IsDefaultFaction || planet.ControllingFaction.IsPlayerFaction)
		{
			lines.Add(new Tuple<string, string>("Classification", planet.Template.Name));
			lines.Add(new Tuple<string, string>("Population", planet.Population.ToString()));
			lines.Add(new Tuple<string, string>("PDF Size", planet.PlanetaryDefenseForces.ToString()));
			lines.Add(new Tuple<string, string>("Aestimare", ConvertImportanceToString(planet.Importance)));
			lines.Add(new Tuple<string, string>("Tithe Grade", ConvertTaxRangeToString(planet.TaxLevel)));
			if (planet.PlanetFactionMap[planet.ControllingFaction.Id].Leader?.ActiveRequest != null)
			{
				lines.Add(new Tuple<string, string>("The planetary governor has requested our assistance", ""));
			}
		}
		else
		{
			lines.Add(new Tuple<string, string>("Xenos Present", planet.ControllingFaction.Name));
		}
	}

	private string ConvertImportanceToString(int importance)
	{
		if (importance > 6000)
		{
			return $"G{importance % 1000}";
		}
		else if (importance > 5000)
		{
			return $"F{importance % 1000}";
		}
		else if (importance > 4000)
		{
			return $"E{importance % 1000}";
		}
		else if (importance > 3000)
		{
			return $"D{importance % 1000}";
		}
		else if (importance > 2000)
		{
			return $"C{importance % 1000}";
		}
		else if (importance > 1000)
		{
			return $"B{importance % 1000}";
		}
		else
		{
			return $"A{importance}";
		}
	}

	private string ConvertTaxRangeToString(int taxRate)
	{
		return taxRate switch
		{
			0 => "Adeptus Non",
			1 => "Solutio Tertius",
			2 => "Solutio Secundus",
			3 => "Solutio Prima",
			4 => "Solutio Particular",
			5 => "Solutio Extremis",
			6 => "Decuma Tertius",
			7 => "Decuma Secundus",
			8 => "Decuma Prima",
			9 => "Decuma Particular",
			10 => "Decuma Extremis",
			11 => "Exactis Tertius",
			12 => "Exactis Secundus",
			13 => "Exactis Prima",
			14 => "Exactis Median",
			15 => "Exactis Particular",
			16 => "Exactis Extremis",
			_ => "",
		};
	}

	private void OnRegionTreeDeselected(object sender, EventArgs e)
	{
		_selectedRegion = null;
		_selectedSquad = null;
		_selectedUnit = null;
	}

	private void OnFleetTreeDeselected(object sender, EventArgs e)
	{
		_selectedShip = null;
		_selectedLoadedSquad = null;
	}

	private void OnFleetTreeItemClicked(object sender, Vector2I e)
	{
		switch(e.X)
		{
			case 0:
				// Fleet
				_selectedShip = e.Y;
				_selectedLoadedSquad = null;
				break;
			case 1:
				// Squad
				_selectedShip = null;
				_selectedLoadedSquad = e.Y;
				break;
		}
	}

	private void OnRegionTreeItemClicked(object sender, Vector2I e)
	{
		switch (e.X)
		{
			case 0:
				// Region
				_selectedRegion = e.Y;
				_selectedUnit = null;
				_selectedSquad = null;
				break;
			case 1:
				// Unit
				_selectedRegion = null;
				_selectedUnit = e.Y;
				_selectedSquad = null;
				break;
			case 2:
				// Squad
				_selectedRegion = null;
				_selectedUnit = null;
				_selectedSquad = e.Y;
				break;
		}
	}
}
