using Godot;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class OrderDialogController : Control
{
    private OrderDialogView _view;
    private Squad _squad;

    public override void _Ready()
    {
        _view = GetNode<OrderDialogView>("OrderDialogView");
        _view.RegionOptionSelected += OnRegionOptionSelected;
    }

    public void PopulateOrderData(Squad squad)
    {
        _squad = squad;
        // determine the regions adjacent to the squad's current region
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
}
