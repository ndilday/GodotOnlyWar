using Godot;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Orders;
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
		AddToGroup(DialogController.DialogInputBlockerGroup);
		_view = GetNode<OrderDialogView>("OrderDialogView");
		_view.RegionOptionSelected += OnRegionOptionSelected;
		_view.MissionOptionSelected += OnMissionOptionSelected;
		_view.AggressionOptionSelected += OnAggressionOptionSelected;
		_view.OrdersConfirmed += OnOrdersConfirmed;
		_view.Canceled += OnCanceled;
	}

	public void RequestClose()
	{
		OnCanceled(this, EventArgs.Empty);
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

	// Maps a MissionAvailability.AvailableMission back onto the OptionButton id scheme the view
	// and OnMissionOptionSelected/OnOrdersConfirmed still key off of.
	// NOTE: id -9 (not -1) for Recon. Godot's OptionButton.AddItem treats id == -1
	// as "auto-assign to the item's index", so a literal -1 would be silently replaced
	// with the item index (0), breaking Recon selection/confirmation.
	private static int GetOptionButtonId(AvailableMission mission)
	{
		return mission.Kind switch
		{
			MissionAvailabilityKind.Recon => -9,
			MissionAvailabilityKind.Attack => -2,
			MissionAvailabilityKind.Move => -2,
			MissionAvailabilityKind.Defend => -3,
			MissionAvailabilityKind.Patrol => -4,
			MissionAvailabilityKind.FortifyEntrenchment => -5,
			MissionAvailabilityKind.BuildListeningPost => -6,
			MissionAvailabilityKind.BuildAntiAir => -7,
			MissionAvailabilityKind.Diversion => -8,
			_ => mission.SpecialMission.Id,
		};
	}

	private void PopulateMissions()
	{
		var availableMissions = MissionAvailability.GetAvailableMissions(_squad.CurrentRegion, _currentlySelectedRegion);
		List<Tuple<string, int>> missionOptions = availableMissions
			.Select(m => new Tuple<string, int>(m.Label, GetOptionButtonId(m)))
			.ToList();
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

	// Maps the OptionButton mission id (args.MissionCode) back onto an AvailableMission descriptor
	// so OrderAssignment.AssignSquadsToMission can build the Mission the same way
	// MissionAvailability.GetAvailableMissions would have offered it. Note -2 covers both Attack
	// and Move (PopulateMissions never offers both for the same target region), and both resolve
	// identically in OrderAssignment (target-faction dropdown falls back to the player's own
	// RegionFaction when no enemy was targetable), so mapping -2 to Attack here is safe.
	private AvailableMission ResolveSelectedMission(Region selectedRegion, int missionCode)
	{
		switch (missionCode)
		{
			case -9: return new AvailableMission("Recon", MissionAvailabilityKind.Recon);
			case -2: return new AvailableMission("Attack", MissionAvailabilityKind.Attack);
			case -3: return new AvailableMission("Defend", MissionAvailabilityKind.Defend);
			case -4: return new AvailableMission("Patrol", MissionAvailabilityKind.Patrol);
			case -5: return new AvailableMission("Build Fortifications", MissionAvailabilityKind.FortifyEntrenchment);
			case -6: return new AvailableMission("Build Listening Post", MissionAvailabilityKind.BuildListeningPost);
			case -7: return new AvailableMission("Build Anti-Air", MissionAvailabilityKind.BuildAntiAir);
			case -8: return new AvailableMission("Diversion", MissionAvailabilityKind.Diversion);
			default:
				Mission specialMission = selectedRegion.SpecialMissions.FirstOrDefault(m => m.Id == missionCode);
				return specialMission == null ? null : new AvailableMission(specialMission.MissionType.ToString(), MissionAvailabilityKind.Special, specialMission);
		}
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

		AvailableMission mission = ResolveSelectedMission(selectedRegion, args.MissionCode);
		if (mission == null)
		{
			GD.PushWarning($"Could not confirm orders for {selectedRegion.Name}: mission id {args.MissionCode} no longer exists.");
			return;
		}

		Order newOrder = OrderAssignment.AssignSquadsToMission(
			new List<Squad> { _squad }, selectedRegion, mission, args.TargetFactionId, aggro);
		if (newOrder == null)
		{
			// Recon/Diversion couldn't resolve a valid region-faction target (same failure modes
			// OnOrdersConfirmed used to guard against inline).
			GD.PushWarning($"Could not confirm orders for {selectedRegion.Name}: no valid region faction target for mission {mission.Kind}.");
			return;
		}

		Visible = false;
		OrdersConfirmed?.Invoke(this, EventArgs.Empty);
	}

	private void OnCanceled(object sender, EventArgs e)
	{
		Visible = false;
	}
}
