using Godot;
using OnlyWar.Helpers.UI;
using System.Collections.Generic;

public partial class PlanetTacticalScreenView : CommandWorkspaceView
{
    private Panel _legacyDataPanel;
    private Panel _legacyOrbitPanel;
    private Panel _legacyButtonPanel;
    private Control _tacticalRegionPanel;

    public override void _Ready()
    {
        base._Ready();
        ClipContents = true;
        _legacyDataPanel = GetNodeOrNull<Panel>("DataPanel");
        _legacyOrbitPanel = GetNodeOrNull<Panel>("OrbitPanel");
        _legacyButtonPanel = GetNodeOrNull<Panel>("ButtonPanel");
        _tacticalRegionPanel = GetNode<Control>("TacticalRegionPanel");

        HideLegacyPanels();
        ConfigureMapPanel();
        BuildWorkspaceShell(0.705f, 0.91f, 0.755f);
        CallDeferred(nameof(LayoutRegionHexGrid));
    }

    private void HideLegacyPanels()
    {
        if (_legacyDataPanel != null) _legacyDataPanel.Visible = false;
        if (_legacyOrbitPanel != null) _legacyOrbitPanel.Visible = false;
        if (_legacyButtonPanel != null) _legacyButtonPanel.Visible = false;
    }

    private void ConfigureMapPanel()
    {
        _tacticalRegionPanel.AnchorLeft = 0.245f;
        _tacticalRegionPanel.AnchorTop = 0.08f;
        _tacticalRegionPanel.AnchorRight = 0.705f;
        _tacticalRegionPanel.AnchorBottom = 0.735f;
        _tacticalRegionPanel.OffsetLeft = 0;
        _tacticalRegionPanel.OffsetTop = 0;
        _tacticalRegionPanel.OffsetRight = 0;
        _tacticalRegionPanel.OffsetBottom = 0;
        _tacticalRegionPanel.ClipContents = true;
        _tacticalRegionPanel.SelfModulate = Colors.White;

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
        _tacticalRegionPanel.AddThemeStyleboxOverride("panel", mapStyle);
    }

    public void LayoutRegionHexGrid()
    {
        int[] rowCounts = [1, 2, 3, 4, 3, 2, 1];
        const float visibleGridWidth = 0.82f;
        const float flatTopRatio = 2f / 1.7320508f;
        const float spacingFactor = 1.04f;
        float panelWidth = Mathf.Max(_tacticalRegionPanel.Size.X, 1f);
        float panelHeight = Mathf.Max(_tacticalRegionPanel.Size.Y, 1f);
        float panelAspect = panelWidth / panelHeight;
        float hexWidth = visibleGridWidth / (1f + 3f * 1.5f * spacingFactor);
        float hexHeight = hexWidth * panelAspect / flatTopRatio;
        float xStep = hexWidth * 1.5f * spacingFactor;
        float yStep = hexHeight * 0.5f * spacingFactor;
        float tileWidth = hexWidth + 12f / panelWidth;
        float tileHeight = hexHeight + 12f / panelHeight;
        float totalVisibleHeight = hexHeight + (rowCounts.Length - 1) * yStep;
        float startY = (1f - totalVisibleHeight) * 0.5f;
        int regionIndex = 1;

        for (int row = 0; row < rowCounts.Length; row++)
        {
            int count = rowCounts[row];
            float yCenter = startY + hexHeight * 0.5f + row * yStep;
            float rowHalf = (count - 1) * 0.5f;
            for (int col = 0; col < count; col++)
            {
                Control regionControl = _tacticalRegionPanel.GetNodeOrNull<Control>($"TacticalRegionController{regionIndex}");
                if (regionControl != null)
                {
                    float xCenter = 0.5f + (col - rowHalf) * xStep;
                    regionControl.AnchorLeft = xCenter - tileWidth * 0.5f;
                    regionControl.AnchorRight = xCenter + tileWidth * 0.5f;
                    regionControl.AnchorTop = yCenter - tileHeight * 0.5f;
                    regionControl.AnchorBottom = yCenter + tileHeight * 0.5f;
                    regionControl.OffsetLeft = 0;
                    regionControl.OffsetTop = 0;
                    regionControl.OffsetRight = 0;
                    regionControl.OffsetBottom = 0;
                }
                regionIndex++;
            }
        }
    }
}
