using Godot;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using System;
using System.Linq;

public partial class TacticalRegionController : Control
{
	private TacticalRegionView _view;
	private Button _button;

	public override void _Ready()
	{
		_view = GetNode<TacticalRegionView>("TacticalRegionView");
		_button = GetNode<Button>("TacticalRegionView/Button");
	}

	public void Populate(Region region)
	{
		RegionFaction playerRegionFaction = region.RegionFactionMap.Values.FirstOrDefault(rf => rf.PlanetFaction.Faction.IsPlayerFaction);
		RegionFaction defaultFaction = region.RegionFactionMap.Values.FirstOrDefault(rf => rf.PlanetFaction.Faction.IsDefaultFaction);
		RegionFaction xenosRegionFaction = region.RegionFactionMap.Values.FirstOrDefault(rf => !rf.PlanetFaction.Faction.IsPlayerFaction && !rf.PlanetFaction.Faction.IsDefaultFaction);
		
		// we'll need to adjust this to take into account orders later
		bool showPlayerPublic = playerRegionFaction != null && playerRegionFaction.LandedSquads.Any();
		string playerPopulation = "";
		if(showPlayerPublic)
		{
			playerPopulation = playerRegionFaction.LandedSquads.Sum(s => s.Members.Count()).ToString();
		}
		
		bool showCivilian = defaultFaction != null && defaultFaction.Population > 0 || (playerRegionFaction != null && playerRegionFaction.Population > 0);
		string civilianPopulation = "";
		if(showCivilian)
		{
			long population = 0;
			if(defaultFaction != null)
			{
				population += defaultFaction.Population;
			}
			if(playerRegionFaction != null)
			{
				population += playerRegionFaction.Population;
			}
			if(xenosRegionFaction != null && !xenosRegionFaction.IsPublic)
			{
				// hidden xenos are added to civilian population
				population += xenosRegionFaction.Population;
			}
			civilianPopulation = population.ToString();
		}

		bool showXenos = xenosRegionFaction != null && xenosRegionFaction.IsPublic;
		string xenosPopulation = "";
		if(showXenos)
		{
			xenosPopulation = xenosRegionFaction.Population.ToString();
		}

		_view.Populate(region.Id, showPlayerPublic, false, showCivilian, showXenos, false, false, playerPopulation, civilianPopulation, xenosPopulation);
	}

	public void AddToButtonGroup(ButtonGroup buttonGroup)
	{
		_button.ButtonGroup = buttonGroup;
	}
}
