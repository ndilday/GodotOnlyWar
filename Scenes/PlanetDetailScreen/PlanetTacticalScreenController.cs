using Godot;
using OnlyWar.Models.Planets;
using System;

public partial class PlanetTacticalScreenController : Control
{
    private PlanetTacticalScreenView _view;
    private TacticalRegionController[] _tacticalRegions;
    private ButtonGroup _buttonGroup;

	public event EventHandler CloseButtonPressed;

    public override void _Ready()
    {
        _view = GetNode<PlanetTacticalScreenView>("PlanetTacticalScreenView");
        _view.CloseButtonPressed += (object sender, EventArgs e) => CloseButtonPressed?.Invoke(this, e);
        _tacticalRegions = new TacticalRegionController[16];
        for(int i=1; i<=16; i++)
        {
            _tacticalRegions[i - 1] = GetNode<TacticalRegionController>($"PlanetTacticalScreenView/TacticalRegionPanel/TacticalRegionController{i}");
            _tacticalRegions[i - 1].AddToButtonGroup(_buttonGroup);
        }
        _buttonGroup = new ButtonGroup();
    }

    public void PopulatePlanetData(Planet planet)
    {
        for(int i = 0; i < planet.Regions.Length; i++)
        {
            _tacticalRegions[i].Populate(planet.Regions[i]);
        }
    }
}
