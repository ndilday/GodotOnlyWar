using Godot;
using OnlyWar.Helpers;
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
        _view.AggressionOptionSelected += OnAggressionOptionSelected;
    }

    public void PopulateOrderData(Squad squad)
    {
        _squad = squad;
        // determine the regions adjacent to the squad's current region
        var adjacentRegions = RegionExtensions.GetAdjacentRegions(squad.CurrentRegion);
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
    }

    private void PopulateMissions(Region region)
    {

    }

    private void OnRegionOptionSelected(object sender, int e)
    {
        // get the missions available for the selected region
        // get the currently selected mission, if any
        // populate the mission dropbox
        // if the currently selected mission is still possible in the newly selected region, select it in the new region
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
            case 3:
                text = NORMAL;
                break;
            case 4:
                text = ATTRITIONAL;
                break;
            default:
                text = AGGRESSIVE;
                break;
        }

        _view.SetAggressionDescription(text);
    }
}
