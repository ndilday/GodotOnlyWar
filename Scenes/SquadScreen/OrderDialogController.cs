using Godot;
using OnlyWar.Helpers;
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
    private const string AVOID = "The force will avoid any encounter that can't be overcome quickly and quietly.";
    private const string CAUTIOUS = "The force will avoid encounters where they do not have an overwhelming advantage.";
    private const string NORMAL = "The force will engage when it can vanquish the enemy with minimal casualties.";
    private const string ATTRITIONAL = "The force will engage in evenly matched combat; losses are likely";
    private const string AGGRESSIVE = "Here we stand and here shall we die, unbroken and unbowed, though the very hand of death itself come for us, we will spit our defiance to the end!";

    public override void _Ready()
    {
        _view = GetNode<OrderDialogView>("OrderDialogView");
        _view.RegionOptionSelected += OnRegionOptionSelected;
        _view.MissionOptionSelected += OnMissionOptionSelected;
        _view.AggressionOptionSelected += OnAggressionOptionSelected;
        _view.OrdersConfirmed += OnOrdersConfirmed;
        _view.Canceled += OnCanceled;
    }

    public void PopulateOrderData(Squad squad)
    {
        _squad = squad;
        
        string header = $"Orders for {squad.Name}, {squad.ParentUnit.Name}";
        _view.SetHeader(header);

        // determine the regions adjacent to the squad's current region
        var adjacentRegions = RegionExtensions.GetSelfAndAdjacentRegions(squad.CurrentRegion);
        PopulateRegionOptions(adjacentRegions, squad.CurrentOrders?.TargetRegion);

        if (squad.CurrentOrders == null)
        {
            _view.SetAggressionDescription("");
            _view.SetMissionDescription("");
        }
    }

    private void PopulateRegionOptions(IReadOnlyList<Region> regions, Region currentlySelectedRegion = null)
    {
        // foreach region, make a tuple of the region name and region Id
        var tuples = regions.Select(r => new Tuple<string, int>(r.Name, r.Id)).ToList();
        _view.PopulateRegionOptions(tuples);
        if (currentlySelectedRegion != null)
        {
            _view.SelectRegion(currentlySelectedRegion.Id);
        }
        else
        {
            _view.SelectRegion(-1);
        }
    }

    private void PopulateMissions(Region region)
    {
        List<Tuple<string, int>> missionOptions = new List<Tuple<string, int>>();
        missionOptions.Add(new Tuple<string, int>("Recon", (int)MissionType.Recon));
        foreach (var mission in region.SpecialMissions)
        {
            missionOptions.Add(new Tuple<string, int>(mission.MissionType.ToString(), (int)mission.MissionType));
        }
        _view.PopulateMissionOptions(missionOptions);
    }

    private void OnRegionOptionSelected(object sender, int e)
    {
        // get selected region
        Region selectedRegion = _squad.CurrentRegion.Planet.Regions.First(r => r.Id == e);
        // get the missions available for the selected region
        // get the currently selected mission, if any
        // populate the mission dropbox
        // if the currently selected mission is still possible in the newly selected region, select it in the new region
        PopulateMissions(selectedRegion);
    }

    private void OnMissionOptionSelected(object sender, int e)
    {
        // change aggression helper text
        string text;
        switch ((MissionType)e)
        {
            case MissionType.Recon:
                text = "Probe the area to find hidden enemy forces and opportunities for special missions";
                break;
            case MissionType.Assassination:
                text = "A high-value target has been identified in the area. They will likely be guarded by an elite bodyguard. Eliminating them will greatly reduce the enemy's command and control capabilities.";
                break;
                case MissionType.Ambush:
                text = "We have identified an area the enemy frequently moves forces through that contains oppotune firing lines. Setting up an ambush here will allow us to reduce enemy forces at relatively low risk to our troops, assuming they can get into position undetected.";
                break;
                case MissionType.Sabotage:
                text = "We have identified a target that, if destroyed, will greatly reduce the enemy's ability to wage war in this region. We will need to get in and out quickly, as the enemy will likely be on high alert.";
                break;
            default:
                text = "Huh?";
                break;
        }

        _view.SetMissionDescription(text);
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

    private void OnOrdersConfirmed(object sender, Tuple<int, int, int> args)
    {
        Region selectedRegion = _squad.CurrentRegion.Planet.Regions.First(r => r.Id == args.Item1);
        //mission stuff related to args.Item2;
        Aggression aggro = (Aggression)args.Item3;
        if(_squad.CurrentOrders != null)
        {
            GameDataSingleton.Instance.Sector.RemoveOrder(_squad.CurrentOrders);
        }
        _squad.CurrentOrders = new Order(_squad, selectedRegion, Disposition.Mobile, true, false, aggro, MissionType.Recon);
        GameDataSingleton.Instance.Sector.AddNewOrder(_squad.CurrentOrders);
        Visible = false;
    }

    private void OnCanceled(object sender, EventArgs e)
    {
        Visible = false;
    }
}
