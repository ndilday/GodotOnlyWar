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
        missionOptions.Add(new Tuple<string, int>("Recon", -1));
        if (_currentlySelectedRegion == _squad.CurrentRegion)
        {
            missionOptions.Add(new Tuple<string, int>("Defend", -3));
            missionOptions.Add(new Tuple<string, int>("Patrol", -4));
        }
        else if(_currentlySelectedRegion.RegionFactionMap.Values.Any(rf => !rf.PlanetFaction.Faction.IsDefaultFaction && !rf.PlanetFaction.Faction.IsPlayerFaction))
        {
            missionOptions.Add(new Tuple<string, int>("Attack", -2));
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
        _currentlySelectedRegion = _squad.CurrentRegion.Planet.Regions.First(r => r.Id == e);
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
        if (e == -1)
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
            case MissionType.Extermination:
                text = "We have identified a hidden enemy cell that we can take out.";
                break;
            case MissionType.Patrol:
                text = "Move around the region, attempting to find hidden or infiltrating enemy forces";
                break;
            case MissionType.Recon:
                text = "Probe the area to find hidden enemy forces and opportunities for special missions";
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

        Mission mission;
        if(args.Item2 == -1)
        {
            // use the first non-player, non-default region faction in this region
            RegionFaction enemyRegionFaction = selectedRegion.RegionFactionMap.Values.First(rf => !rf.PlanetFaction.Faction.IsPlayerFaction && !rf.PlanetFaction.Faction.IsDefaultFaction);
            if ((enemyRegionFaction == null))
            {
                enemyRegionFaction = selectedRegion.RegionFactionMap.Values.First(rf => rf.PlanetFaction.Faction.IsDefaultFaction);
            }
            mission = new Mission(MissionType.Recon, enemyRegionFaction, 0);
        }
        else if(args.Item2 == -2)
        {
            RegionFaction enemyRegionFaction = selectedRegion.RegionFactionMap.Values.First(rf => !rf.PlanetFaction.Faction.IsPlayerFaction && !rf.PlanetFaction.Faction.IsDefaultFaction && rf.IsPublic);
            if ((enemyRegionFaction == null))
            {
                if (!selectedRegion.RegionFactionMap.Values.Any(rf => !rf.PlanetFaction.Faction.IsPlayerFaction))
                {
                    selectedRegion.RegionFactionMap[GameDataSingleton.Instance.Sector.PlayerForce.Faction.Id] = 
                        new RegionFaction(selectedRegion.Planet.PlanetFactionMap[GameDataSingleton.Instance.Sector.PlayerForce.Faction.Id], selectedRegion);
                }
                enemyRegionFaction = selectedRegion.RegionFactionMap[GameDataSingleton.Instance.Sector.PlayerForce.Faction.Id];
            }
            mission = new Mission(MissionType.Advance, enemyRegionFaction, 0);
        }
        else
        {
            mission = selectedRegion.SpecialMissions.First(m => m.Id == args.Item2);
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
}
