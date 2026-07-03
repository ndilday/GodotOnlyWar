using Godot;
using OnlyWar.Helpers.UI;
using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;

public partial class RegionScreenView : CommandWorkspaceView
{
    private Panel _legacyDataPanel;
    private Panel _legacySquadTreePanel;
    private Panel _legacyOrdersPanel;
    private Panel _regionPanel;

    private TacticalRegionController _centerRegionController;
    private TacticalRegionController _northRegionController;
    private TacticalRegionController _northeastRegionController;
    private TacticalRegionController _southeastRegionController;
    private TacticalRegionController _southRegionController;
    private TacticalRegionController _southwestRegionController;
    private TacticalRegionController _northwestRegionController;

    public event EventHandler<Region> AdjacentRegionClicked;

    public override void _Ready()
    {
        base._Ready();
        ClipContents = true;

        _legacyDataPanel = GetNodeOrNull<Panel>("DataPanel");
        _legacySquadTreePanel = GetNodeOrNull<Panel>("SquadTreePanel");
        _legacyOrdersPanel = GetNodeOrNull<Panel>("OrdersPanel");
        _regionPanel = GetNode<Panel>("RegionPanel");

        _centerRegionController = GetNode<TacticalRegionController>("RegionPanel/TacticalRegionCenter");
        _northRegionController = GetNode<TacticalRegionController>("RegionPanel/TacticalRegionNorth");
        _northeastRegionController = GetNode<TacticalRegionController>("RegionPanel/TacticalRegionNortheast");
        _southeastRegionController = GetNode<TacticalRegionController>("RegionPanel/TacticalRegionSoutheast");
        _southRegionController = GetNode<TacticalRegionController>("RegionPanel/TacticalRegionSouth");
        _southwestRegionController = GetNode<TacticalRegionController>("RegionPanel/TacticalRegionSouthwest");
        _northwestRegionController = GetNode<TacticalRegionController>("RegionPanel/TacticalRegionNorthwest");

        ConnectAdjacentRegionSignal(_northRegionController);
        ConnectAdjacentRegionSignal(_northeastRegionController);
        ConnectAdjacentRegionSignal(_southeastRegionController);
        ConnectAdjacentRegionSignal(_southRegionController);
        ConnectAdjacentRegionSignal(_southwestRegionController);
        ConnectAdjacentRegionSignal(_northwestRegionController);

        HideLegacyPanels();
        ConfigureRegionPanel();
        BuildWorkspaceShell(0.755f, 0.785f);
    }

    public void PopulateAdjacentRegions(Region centerRegion, Dictionary<string, Region> adjacentRegions, MapLayer layers)
    {
        _centerRegionController.Populate(centerRegion, layers, true);

        void SetupAdjacentRegion(TacticalRegionController controller, string direction)
        {
            if (adjacentRegions.TryGetValue(direction, out Region region))
            {
                controller.Populate(region, layers, false);
                controller.Visible = true;
            }
            else
            {
                controller.Visible = false;
            }
        }

        SetupAdjacentRegion(_northRegionController, "N");
        SetupAdjacentRegion(_northeastRegionController, "NE");
        SetupAdjacentRegion(_southeastRegionController, "SE");
        SetupAdjacentRegion(_southRegionController, "S");
        SetupAdjacentRegion(_southwestRegionController, "SW");
        SetupAdjacentRegion(_northwestRegionController, "NW");
    }

    private void HideLegacyPanels()
    {
        if (_legacyDataPanel != null) _legacyDataPanel.Visible = false;
        if (_legacySquadTreePanel != null) _legacySquadTreePanel.Visible = false;
        if (_legacyOrdersPanel != null) _legacyOrdersPanel.Visible = false;
    }

    private void ConfigureRegionPanel()
    {
        _regionPanel.AnchorLeft = 0.245f;
        _regionPanel.AnchorTop = 0.08f;
        _regionPanel.AnchorRight = 0.755f;
        _regionPanel.AnchorBottom = 0.785f;
        _regionPanel.OffsetLeft = 0;
        _regionPanel.OffsetTop = 0;
        _regionPanel.OffsetRight = 0;
        _regionPanel.OffsetBottom = 0;
        _regionPanel.MouseFilter = MouseFilterEnum.Pass;

        StyleBoxFlat mapStyle = new()
        {
            BgColor = new Color(0.005f, 0.006f, 0.007f, 0.74f),
            BorderColor = new Color(0.45f, 0.35f, 0.18f, 0.86f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 1,
            CornerRadiusTopRight = 1,
            CornerRadiusBottomLeft = 1,
            CornerRadiusBottomRight = 1
        };
        _regionPanel.AddThemeStyleboxOverride("panel", mapStyle);
    }

    private void ConnectAdjacentRegionSignal(TacticalRegionController controller)
    {
        if (controller != null)
        {
            controller.TacticalRegionPressed += (sender, region) => AdjacentRegionClicked?.Invoke(this, region);
        }
    }
}
