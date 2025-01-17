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
	private Planet _selectedPlanet;
	private Ship _selectedShip;
	private Unit _selectedLoadedUnit;
	private Squad _selectedLoadedSquad;
	private Region _selectedRegion;
	private Unit _selectedLandedUnit;
	private Squad _selectedLandedSquad;

	public event EventHandler CloseButtonPressed;

	public override void _Ready()
	{
		_view = GetNode<PlanetDetailScreenView>("PlanetDetailScreenView");
		_view.CloseButtonPressed += (object sender, EventArgs e) => CloseButtonPressed?.Invoke(this, e);
		_view.RegionTreeDeselected += OnRegionTreeDeselected;
		_view.RegionTreeItemClicked += OnRegionTreeItemClicked;
		_view.FleetTreeDeselected += OnFleetTreeDeselected;
		_view.FleetTreeItemClicked += OnFleetTreeItemClicked;
		_view.LandingButtonPressed += OnLandingButtonPressed;
		_view.LoadingButtonPressed += OnLoadingButtonPressed;
	}

	public void PopulatePlanetData(Planet planet)
	{
		_selectedPlanet = planet;
		PopulatePlanetDetails(planet);
		PopulateFleetTree(planet);
		PopulateRegionTree(planet);
		UpdateButtons();
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
					var unitSquadMap = ship.LoadedSquads.GroupBy(s => s.ParentUnit).ToDictionary(group => group.Key, group => group.ToList());
					nodes = GetUnitTreeNodes(unitSquadMap);
					/*foreach (Squad squad in ship.LoadedSquads)
					{
						TreeNode node = new TreeNode(squad.Id, squad.Name, new List<TreeNode>());
						nodes.Add(node);
					}*/
					string text = $"{ship.Name} ({ship.LoadedSoldierCount}/{ship.Template.SoldierCapacity})";
					TreeNode shipNode = new TreeNode(ship.Id, text, nodes);
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
				unitsInRegion = GetUnitTreeNodes(unitSquadMap);

			}
			TreeNode regionNode = new TreeNode(region.Id, GetRegionName(planet, i), unitsInRegion);
			regionList.Add(regionNode);
		}
		_view.PopulateRegionTree(regionList);
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
		_view.PopulatePlanetData(lines);
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
		_selectedLandedSquad = null;
		_selectedLandedUnit = null;
	}

	private void OnFleetTreeDeselected(object sender, EventArgs e)
	{
		_selectedShip = null;
		_selectedLoadedUnit = null;
		_selectedLoadedSquad = null;
	}

	private void OnFleetTreeItemClicked(object sender, Vector2I e)
	{
		switch(e.X)
		{
			case 0:
				// Fleet
				_selectedShip = _selectedPlanet.OrbitingTaskForceList.SelectMany(tf => tf.Ships).First(s => s.Id == e.Y);
				_selectedLoadedUnit = null;
				_selectedLoadedSquad = null;
				break;
			case 1:
				// Unit
				TreeItem item = (TreeItem)sender;
				Vector2I shipMeta = item.GetParent().GetMetadata(0).AsVector2I();
				_selectedShip = _selectedPlanet.OrbitingTaskForceList.SelectMany(tf => tf.Ships).First(s => s.Id == e.Y);
				_selectedLoadedUnit = GameDataSingleton.Instance.Sector.PlayerForce.Army.OrderOfBattle.ChildUnits.First(u => u.Id == e.Y);
				_selectedLoadedSquad = null;
				break;
			case 2:
				// Squad
				_selectedLandedUnit = null;
				_selectedLoadedSquad = GameDataSingleton.Instance.Sector.PlayerForce.Army.SquadMap[e.Y];
				_selectedShip = _selectedLoadedSquad.BoardedLocation;
				break;
		}
		UpdateButtons();
	}

	private void OnRegionTreeItemClicked(object sender, Vector2I e)
	{
		switch (e.X)
		{
			case 0:
				// Region
				_selectedRegion = _selectedPlanet.Regions.First(r => r.Id == e.Y);
				_selectedLandedUnit = null;
				_selectedLandedSquad = null;
				break;
			case 1:
				// Unit
				TreeItem item = (TreeItem)sender;
				Vector2I regionMeta = item.GetParent().GetMetadata(0).AsVector2I();
				_selectedRegion = _selectedPlanet.Regions.First(r => r.Id == regionMeta.Y);
				_selectedLandedUnit = GameDataSingleton.Instance.Sector.PlayerForce.Army.OrderOfBattle.ChildUnits.First(u => u.Id == e.Y);
				_selectedLandedSquad = null;
				break;
			case 2:
				// Squad
				Squad squad = GameDataSingleton.Instance.Sector.PlayerForce.Army.SquadMap[e.Y];
				item = (TreeItem)sender;
				regionMeta = item.GetParent().GetParent().GetMetadata(0).AsVector2I();
				_selectedRegion = _selectedPlanet.Regions.First(r => r.Id == regionMeta.Y);
				_selectedLandedUnit = null;
				_selectedLandedSquad = GameDataSingleton.Instance.Sector.PlayerForce.Army.SquadMap[e.Y];
				break;
		}
		UpdateButtons();
	}

	private void UpdateButtons()
	{
		// see if a loaded squad or ship is selected, and something selected on the right side
		if (_selectedShip != null && _selectedShip.LoadedSquads.Count() > 0 && _selectedRegion != null)
		{
			if (_selectedLoadedSquad != null)
			{
				_view.EnableLandingButton(true, $"Land {_selectedLandedSquad.Name} in region >");
			}
			else if (_selectedLandedUnit != null)
			{
				// multiple squads selected at the unit or ship level
				_view.EnableLandingButton(true, $"Land troops from {_selectedShip.Name} in region >");
			}
			else
			{
				_view.EnableLandingButton(true, $"Land all Squads on {_selectedShip.Name} in region >");
			}
		}
		else if(_selectedShip != null && _selectedRegion != null)
		{
			_view.EnableLandingButton(false, "No squads on ship to land >");
		}
		else if(_selectedShip != null)
		{
			_view.EnableLandingButton(false, "Select a Region to Land Troops >");
		}
		else if(_selectedRegion != null)
		{
			_view.EnableLandingButton(false, "Select a Ship with troops to land >");
		}
		else
		{
			_view.EnableLandingButton(false, "Land Squad in region >");
		}

		// loading squads is more complicated
		if(_selectedShip == null)
		{
			_view.EnableLoadingButton(false, "< Select a ship to load troops into");
		}
		else if(_selectedRegion == null)
		{
			_view.EnableLoadingButton(false, "< Select a squad to load into ships");
		}
		else
		{
			int shipCapacity;
			string shipName;
			// determine ship capacity
			if(_selectedShip == null)
			{
				shipName = _selectedLoadedSquad.BoardedLocation.Name;
				shipCapacity = _selectedLoadedSquad.BoardedLocation.AvailableCapacity;
			}
			else
			{
				shipName = _selectedShip.Name;
				shipCapacity = _selectedShip.AvailableCapacity;
			}

			//determine number of troops that will be loaded
			int capacityRequired;
			if(_selectedLandedSquad != null)
			{
				capacityRequired = _selectedLandedSquad.Members.Count;
			}
			else if(_selectedLandedUnit != null)
			{
				capacityRequired = _selectedRegion.RegionFactionMap[GameDataSingleton.Instance.GameRulesData.PlayerFaction.Id].LandedSquads
					.Where(s => s.ParentUnit == _selectedLandedUnit)
					.Sum(s => s.Members.Count);
			}
			else
			{
				capacityRequired = _selectedRegion.RegionFactionMap[GameDataSingleton.Instance.GameRulesData.PlayerFaction.Id].LandedSquads
					.Sum(s => s.Members.Count);
			}


			if(capacityRequired > shipCapacity)
			{
				_view.EnableLoadingButton(false, $"< {shipName} cannot fit that many troops!");
			}
			else if(_selectedLandedSquad != null)
			{
				_view.EnableLoadingButton(true, $"< Load {_selectedLandedSquad.Name} onto {shipName}");
			}
			else
			{
				_view.EnableLoadingButton(true, $"< Load troops onto {shipName}");
			}
		}
	}

	private void ClearSelections()
	{
		_selectedShip = null;
		_selectedLoadedUnit = null;
		_selectedLoadedSquad = null;
		_selectedRegion = null;
		_selectedLandedUnit = null;
		_selectedLandedSquad = null;
	}

	private void OnLandingButtonPressed(object sender, EventArgs e)
	{
		foreach(Squad squad in GetSelectedLoadedSquads())
		{
			// remove squad from ship
			Ship ship = squad.BoardedLocation;
			ship.RemoveSquad(squad);
			// add squad to region
			_selectedRegion.RegionFactionMap[GameDataSingleton.Instance.Sector.PlayerForce.Faction.Id].LandedSquads.Add(squad);
			// update squad
			squad.CurrentRegion = _selectedRegion;
			squad.BoardedLocation = null;
			
		}
		ClearSelections();
		PopulateFleetTree(_selectedPlanet);
		PopulateRegionTree(_selectedPlanet);
		UpdateButtons();
	}

	private IEnumerable<Squad> GetSelectedLoadedSquads()
	{
		if (_selectedLoadedSquad != null)
		{
			return new List<Squad>{ _selectedLoadedSquad };
		}
		else if(_selectedLoadedUnit != null)
		{
			return _selectedShip.LoadedSquads.Where(s => s.ParentUnit == _selectedLoadedUnit).ToList();
		}
		else
		{
			return _selectedShip.LoadedSquads.ToList();
		}
	}

	private void OnLoadingButtonPressed(object sender, EventArgs e)
	{
		foreach (Squad squad in GetSelectedLandedSquads())
		{
			// remove squad from region
			RegionFaction regionFaction = _selectedRegion.RegionFactionMap[GameDataSingleton.Instance.Sector.PlayerForce.Faction.Id];
			regionFaction.LandedSquads.Remove(squad);
			// add squad to ship
			_selectedShip.LoadSquad(squad);
			// update squad
			squad.CurrentRegion = null;
			squad.BoardedLocation = _selectedShip;

		}
		ClearSelections();
		PopulateFleetTree(_selectedPlanet);
		PopulateRegionTree(_selectedPlanet);
		UpdateButtons();
	}

	private IEnumerable<Squad> GetSelectedLandedSquads()
	{
		if (_selectedLandedSquad != null)
		{
			return new List<Squad> { _selectedLandedSquad };
		}
		else
		{
			RegionFaction regionFaction = _selectedRegion.RegionFactionMap[GameDataSingleton.Instance.Sector.PlayerForce.Faction.Id];
			if (_selectedLandedUnit != null)
			{
				return regionFaction.LandedSquads.Where(s => s.ParentUnit == _selectedLandedUnit).ToList();
			}
			else
			{
				return regionFaction.LandedSquads.ToList();
			}
		}
	}
}
