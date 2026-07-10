using Godot;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class OrderDialogController : Control
{
	private OrderDialogView _view;
	private Squad _squad;
	private Region _currentlySelectedRegion;
	private const string AVOID = "The force will avoid any encounter that can't be overcome quickly and quietly.";
	private const string CAUTIOUS = "The force will avoid encounters where they do not have an overwhelming advantage.";
	private const string NORMAL = "The force will engage when it can vanquish the enemy with minimal casualties.";
	private const string ATTRITIONAL = "The force will engage in evenly matched combat; losses are likely";
	private const string AGGRESSIVE = "Here we stand and here shall we die, unbroken and unbowed, though the very hand of death itself come for us, we will spit our defiance to the end!";
	public event EventHandler OrdersConfirmed;

	public override void _Ready()
	{
		_view = GetNode<OrderDialogView>("OrderDialogView");
		_view.RegionOptionSelected += OnRegionOptionSelected;
		_view.MissionOptionSelected += OnMissionOptionSelected;
		_view.AggressionOptionSelected += OnAggressionOptionSelected;
		_view.OrdersConfirmed += OnOrdersConfirmed;
		_view.Canceled += OnCanceled;
	}

	// Returns the public, non-player, non-default RegionFaction entries eligible to be an
	// enemy-directed order's target - the same population used for the mission-availability
	// check at PopulateMissions ([-2]/[-8] branches) and for the Target Faction dropdown.
	private static List<RegionFaction> GetTargetableEnemyRegionFactions(Region region)
	{
		return region.RegionFactionMap.Values
			.Where(rf => rf.IsPublic
				&& !rf.PlanetFaction.Faction.IsPlayerFaction
				&& !rf.PlanetFaction.Faction.IsDefaultFaction)
			.ToList();
	}

	public void PopulateOrderData(Squad squad)
	{
		_squad = squad;
		
		string header = $"Orders for {squad.Name}, {squad.ParentUnit.Name}";
		_view.SetHeader(header);

		// determine the regions adjacent to the squad's current region
		var adjacentRegions = RegionExtensions.GetSelfAndAdjacentRegions(squad.CurrentRegion);
		_currentlySelectedRegion = squad.CurrentOrders?.Mission.RegionFaction.Region;
		PopulateRegionOptions(adjacentRegions);

		if (squad.CurrentOrders == null)
		{
			_view.UnsetAggressionOption();
			_view.DisableMissionOption();
			_view.SetAggressionDescription("");
			_view.SetMissionDescription("");
		}
	}

	private void PopulateRegionOptions(IReadOnlyList<Region> regions)
	{
		// foreach region, make a tuple of the region name and region Id
		var tuples = regions.Select(r => new Tuple<string, int>(r.Name, r.Id)).ToList();
		_view.PopulateRegionOptions(tuples);
		if (_currentlySelectedRegion != null)
		{
			_view.SelectRegion(_currentlySelectedRegion.Id);
		}
		else
		{
			_view.SelectRegion(-1);
		}
	}

	private void PopulateMissions()
	{
		List<Tuple<string, int>> missionOptions = new List<Tuple<string, int>>();
		// NOTE: id -9 (not -1) for Recon. Godot's OptionButton.AddItem treats id == -1
		// as "auto-assign to the item's index", so a literal -1 would be silently replaced
		// with the item index (0), breaking Recon selection/confirmation.
		missionOptions.Add(new Tuple<string, int>("Recon", -9));
		if (_currentlySelectedRegion == _squad.CurrentRegion)
		{
			missionOptions.Add(new Tuple<string, int>("Defend", -3));
			missionOptions.Add(new Tuple<string, int>("Patrol", -4));
			// Fortification: the squad spends the turn building defenses in its own region.
			missionOptions.Add(new Tuple<string, int>("Fortify (Entrenchment)", -5));
			missionOptions.Add(new Tuple<string, int>("Build Listening Post", -6));
			missionOptions.Add(new Tuple<string, int>("Build Anti-Air", -7));
		}
		else if(_currentlySelectedRegion.RegionFactionMap.Values.Any(rf => !rf.PlanetFaction.Faction.IsDefaultFaction && !rf.PlanetFaction.Faction.IsPlayerFaction))
		{
			missionOptions.Add(new Tuple<string, int>("Attack", -2));
			missionOptions.Add(new Tuple<string, int>("Diversion", -8));
		}
		else
		{
			missionOptions.Add(new Tuple<string, int>("Move", -2));
		}
		foreach (var mission in _currentlySelectedRegion.SpecialMissions)
		{
			missionOptions.Add(new Tuple<string, int>(mission.MissionType.ToString(), mission.Id));
		}
		_view.PopulateMissionOptions(missionOptions);
	}

	private void OnRegionOptionSelected(object sender, int e)
	{
		// get selected region
		_currentlySelectedRegion = _squad.CurrentRegion.Planet.Regions.FirstOrDefault(r => r.Id == e);
		if (_currentlySelectedRegion == null)
		{
			GD.PushWarning($"Could not find selected order region id {e}.");
			_view.DisableMissionOption();
			return;
		}
		// get the missions available for the selected region
		// get the currently selected mission, if any
		// populate the mission dropbox
		// if the currently selected mission is still possible in the newly selected region, select it in the new region
		PopulateMissions();
	}

	private void OnMissionOptionSelected(object sender, int e)
	{
		// change aggression helper text
		string text;
		MissionType missionType;
		if (e == -9)
		{
			missionType = MissionType.Recon;
		}
		else if (e == -2)
		{
			missionType = MissionType.Advance;
		}
		else if(e == -3)
		{
			missionType = MissionType.DefenseInDepth;
		}
		else if(e == -4)
		{
			missionType = MissionType.Patrol;
		}
		else if(e <= -5 && e >= -7)
		{
			missionType = MissionType.Construction;
		}
		else if(e == -8)
		{
			missionType = MissionType.Diversion;
		}
		else
		{
			missionType = _currentlySelectedRegion.SpecialMissions.First(m => m.Id == e).MissionType;
		}
		switch (missionType)
		{
			case MissionType.Advance:
				text = "Enter the region, engaging any enemy forces there.";
				break;
			case MissionType.Ambush:
				text = "We have identified an area the enemy frequently moves forces through that contains oppotune firing lines. Setting up an ambush here will allow us to reduce enemy forces at relatively low risk to our troops, assuming they can get into position undetected.";
				break;
			case MissionType.Assassination:
				text = "A high-value target has been identified in the area. They will likely be guarded by an elite bodyguard. Eliminating them will greatly reduce the enemy's command and control capabilities.";
				break;
			case MissionType.DefenseInDepth:
				text = "Defend the region from attacks by enemy forces";
				break;
			case MissionType.Diversion:
				text = "Make an overt show of force against this region to draw the enemy's attention. A convincing feint makes our force look far larger than it is, pinning the enemy's garrison in place—and, if we press aggressively, baiting them into committing to a counterattack—leaving their other regions exposed to our real strike. The feinting force fights in the open, so it risks being caught by the very counterattack it provokes.";
				break;
			case MissionType.Extermination:
				text = "We have identified a hidden enemy cell that we can take out.";
				break;
			case MissionType.Patrol:
				text = "Move around the region, attempting to find hidden or infiltrating enemy forces";
				break;
			case MissionType.Recon:
				text = "Probe the area to find hidden enemy forces and opportunities for special missions";
				break;
			case MissionType.Construction:
				text = "The squad will spend the turn fortifying this region. Progress scales with squad size and engineering skill; defenses accumulate over successive turns.";
				break;
				case MissionType.Sabotage:
				text = "We have identified a target that, if destroyed, will greatly reduce the enemy's ability to wage war in this region. We will need to get in and out quickly, as the enemy will likely be on high alert.";
				break;
			default:
				text = "Huh?";
				break;
		}

		// Target Faction selector: only the enemy-directed synthesized missions (Advance,
		// Diversion) target a specific enemy faction chosen by the player. Everything else
		// either targets the player's own RegionFaction, any region-scoped anchor (Recon), or
		// already carries a baked-in target (special missions).
		if (missionType == MissionType.Advance || missionType == MissionType.Diversion)
		{
			PopulateTargetFactionOptions();
		}
		else
		{
			_view.HideTargetFactionOption();
		}

		_view.SetMissionDescription(text);
	}

	// Populates the Target Faction dropdown from the current region's public, non-player,
	// non-default RegionFactions. Exactly one candidate auto-selects and locks the dropdown
	// (common case stays one-click); two or more require an explicit pick.
	private void PopulateTargetFactionOptions()
	{
		List<RegionFaction> enemies = GetTargetableEnemyRegionFactions(_currentlySelectedRegion);
		// Guard against id -1: Godot's OptionButton.AddItem treats id == -1 as "auto-assign to
		// the item's index" (same quirk noted for Recon above), which would silently corrupt the
		// selection. Real faction ids should never be -1, but skip defensively rather than crash.
		var options = enemies
			.Where(rf => rf.PlanetFaction.Faction.Id != -1)
			.Select(rf => new Tuple<string, int>(
				$"{rf.PlanetFaction.Faction.Name} — {rf.GetForceMagnitudeDescription()}",
				rf.PlanetFaction.Faction.Id))
			.ToList();
		if (options.Count == 0)
		{
			// Shouldn't happen: the mission itself is disabled upstream when there are no
			// enemies. Fail safe by hiding the selector rather than showing an empty dropdown.
			_view.HideTargetFactionOption();
			return;
		}
		_view.PopulateTargetFactionOptions(options);
	}

	private void OnAggressionOptionSelected(object sender, int e)
	{
		// change aggression helper text
		string text;
		switch (e)
		{
			case 0:
				text = AVOID;
				break;
			case 1:
				text = CAUTIOUS;
				break;
			case 2:
				text = NORMAL;
				break;
			case 3:
				text = ATTRITIONAL;
				break;
			default:
				text = AGGRESSIVE;
				break;
		}

		_view.SetAggressionDescription(text);
	}

	private void OnOrdersConfirmed(object sender, OrderDialogResult args)
	{
		Region selectedRegion = _squad.CurrentRegion.Planet.Regions.FirstOrDefault(r => r.Id == args.RegionId);
		if (selectedRegion == null)
		{
			GD.PushWarning($"Could not confirm orders: region id {args.RegionId} no longer exists.");
			return;
		}

		//mission stuff related to args.MissionCode;
		Aggression aggro = (Aggression)args.Aggression;

		Mission mission;
		if(args.MissionCode == -9)
		{
			// use the first non-player, non-default region faction in this region
			RegionFaction enemyRegionFaction = GetEnemyRegionFaction(selectedRegion)
				?? GetDefaultRegionFaction(selectedRegion)
				?? GetOrCreatePlayerRegionFaction(selectedRegion);
			if (enemyRegionFaction == null)
			{
				GD.PushWarning($"Could not confirm recon orders for {selectedRegion.Name}: no valid region faction target.");
				return;
			}
			mission = new Mission(MissionType.Recon, enemyRegionFaction, 0);
		}
		else if(args.MissionCode == -2)
		{
			// Player-selected target faction from the dialog's dropdown, when one was targetable
			// (region has public enemies) — falls back to the player's own RegionFaction for the
			// "Move" case where PopulateMissions offered no enemy target.
			RegionFaction enemyRegionFaction = GetSelectedTargetRegionFaction(selectedRegion, args.TargetFactionId)
				?? GetOrCreatePlayerRegionFaction(selectedRegion);
			mission = new Mission(MissionType.Advance, enemyRegionFaction, 0);
		}
		else if(args.MissionCode == -3)
		{
			mission = new Mission(MissionType.DefenseInDepth, GetOrCreatePlayerRegionFaction(selectedRegion), 0);
		}
		else if(args.MissionCode == -4)
		{
			mission = new Mission(MissionType.Patrol, GetOrCreatePlayerRegionFaction(selectedRegion), 0);
		}
		else if (args.MissionCode <= -5 && args.MissionCode >= -7)
		{
			DefenseType defenseType = args.MissionCode switch
			{
				-5 => DefenseType.Entrenchment,
				-6 => DefenseType.ListeningPost,
				_ => DefenseType.AntiAir
			};
			mission = new ConstructionMission(defenseType, 0, GetOrCreatePlayerRegionFaction(selectedRegion));
		}
		else if (args.MissionCode == -8)
		{
			// Diversion: feint against an enemy-held region while the squad stays in its own
			// region (it demonstrates from adjacent territory rather than entering the target).
			RegionFaction enemyRegionFaction = GetSelectedTargetRegionFaction(selectedRegion, args.TargetFactionId);
			if (enemyRegionFaction == null)
			{
				GD.PushWarning($"Could not confirm diversion orders for {selectedRegion.Name}: no enemy target.");
				return;
			}
			mission = new Mission(MissionType.Diversion, enemyRegionFaction, 0);
		}
		else
		{
			mission = selectedRegion.SpecialMissions.FirstOrDefault(m => m.Id == args.MissionCode);
			if (mission == null)
			{
				GD.PushWarning($"Could not confirm orders for {selectedRegion.Name}: mission id {args.MissionCode} no longer exists.");
				return;
			}
		}

		if(_squad.CurrentOrders != null)
		{
			GameDataSingleton.Instance.Sector.RemoveOrder(_squad.CurrentOrders);
		}

		_squad.CurrentOrders = new Order(new List<Squad> { _squad }, Disposition.Mobile, true, false, aggro, mission);
		GameDataSingleton.Instance.Sector.AddNewOrder(_squad.CurrentOrders);
		Visible = false;
		OrdersConfirmed?.Invoke(this, EventArgs.Empty);
	}

	private void OnCanceled(object sender, EventArgs e)
	{
		Visible = false;
	}

	// Returns the player's RegionFaction in the given region, creating (and registering) one
	// if the player does not yet have a presence there. Player-built fortifications are stored
	// on this region faction. Mirrors the on-demand creation used for Advance orders.
	private static RegionFaction GetOrCreatePlayerRegionFaction(Region region)
	{
		Faction playerFaction = GameDataSingleton.Instance.Sector.PlayerForce.Faction;
		if (!region.RegionFactionMap.TryGetValue(playerFaction.Id, out RegionFaction playerRegionFaction))
		{
			playerRegionFaction = new RegionFaction(region.Planet.PlanetFactionMap[playerFaction.Id], region);
			region.RegionFactionMap[playerFaction.Id] = playerRegionFaction;
		}
		return playerRegionFaction;
	}

	private static RegionFaction GetEnemyRegionFaction(Region region)
	{
		return region.RegionFactionMap.Values.FirstOrDefault(rf =>
			!rf.PlanetFaction.Faction.IsPlayerFaction
			&& !rf.PlanetFaction.Faction.IsDefaultFaction);
	}

	// Looks up the enemy RegionFaction the player picked in the Target Faction dropdown by
	// faction id. Returns null if no target was selected (dropdown not applicable/populated) or
	// the region faction map no longer contains that faction (e.g. it was wiped out this turn).
	private static RegionFaction GetSelectedTargetRegionFaction(Region region, int targetFactionId)
	{
		if (targetFactionId < 0)
		{
			return null;
		}
		return region.RegionFactionMap.TryGetValue(targetFactionId, out RegionFaction targetRegionFaction)
			? targetRegionFaction
			: null;
	}

	private static RegionFaction GetDefaultRegionFaction(Region region)
	{
		return region.RegionFactionMap.Values.FirstOrDefault(rf => rf.PlanetFaction.Faction.IsDefaultFaction);
	}
}
