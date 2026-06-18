using Godot;
using System;
using System.Runtime.CompilerServices;

public partial class Camera2D : Godot.Camera2D
{
    [Export(PropertyHint.Range, "10, 100, 5")]
    public int MapBorderPixels { get; set; } = 100;
    [Export]
	SectorMap _sectorMap;
    [Export]
    float MaxZoom = 10;
    // Screen-space pixels occluded by the floating UI panels on each edge. The
    // map is kept reachable within the unoccluded gap between them.
    [Export]
    float LeftUiInset = 218;
    [Export]
    float RightUiInset = 334;
    [Export]
    float TopUiInset = 64;
    [Export]
    float BottomUiInset = 72;
	Vector2I _mapPixelDimensions;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
        _mapPixelDimensions = new(_sectorMap.GridDimensions.X * _sectorMap.CellSize.X + 2 * MapBorderPixels, 
								  _sectorMap.GridDimensions.Y * _sectorMap.CellSize.Y + 2 * MapBorderPixels);
    }

	public override void _Input(InputEvent @event)
	{
        if (@event is InputEventMouseButton emb)
        {
            // zoom in
            if (emb.ButtonIndex == MouseButton.WheelUp && emb.IsPressed())
            {
                ZoomIn(GetGlobalMousePosition());
                GetViewport().SetInputAsHandled();
            }
            // zoom out
            else if (emb.ButtonIndex == MouseButton.WheelDown && emb.IsPressed())
            {
                ZoomOut(GetGlobalMousePosition());
                GetViewport().SetInputAsHandled();
            }
        }

        // Handle keyboard zoom
        else if (@event is InputEventKey eventKey)
        {
            if (eventKey.Pressed && (eventKey.Keycode == Key.Equal || eventKey.Keycode == Key.KpAdd))
            {
                ZoomIn(null);
            }
            else if (eventKey.Pressed && (eventKey.Keycode == Key.Minus || eventKey.Keycode == Key.KpSubtract))
            {
                ZoomOut(null);
            }
        }
        else if (@event is InputEventMouseMotion emm && emm.ButtonMask == MouseButtonMask.Right)
		{
            Position -= emm.Relative;
            ClampCamera();
        }
	}

    // Returns the most zoomed-out level allowed: the smallest zoom at which the
    // map (including its border) still covers the unoccluded gap between the
    // floating UI panels on both axes.
    private float GetMinZoom()
    {
        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        float gapWidth = viewportSize.X - LeftUiInset - RightUiInset;
        float gapHeight = viewportSize.Y - TopUiInset - BottomUiInset;
        return Math.Max(gapWidth / _mapPixelDimensions.X,
                        gapHeight / _mapPixelDimensions.Y);
    }

    // Enforces the zoom range and keeps the map covering (or centered within) the
    // unoccluded gap between the UI panels, so its edges stay reachable.
    private void ClampCamera()
    {
        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;

        float clampedZoom = Math.Clamp(Zoom.X, GetMinZoom(), MaxZoom);
        if (clampedZoom != Zoom.X)
        {
            Zoom = new Vector2(clampedZoom, clampedZoom);
        }

        Vector2 mapMin = new(-MapBorderPixels, -MapBorderPixels);
        Vector2 mapSize = _mapPixelDimensions;

        Vector2 pos = Position;
        pos.X = ClampAxis(pos.X, viewportSize.X, LeftUiInset, RightUiInset, clampedZoom, mapMin.X, mapSize.X);
        pos.Y = ClampAxis(pos.Y, viewportSize.Y, TopUiInset, BottomUiInset, clampedZoom, mapMin.Y, mapSize.Y);
        Position = pos;
    }

    // anchor_mode is FixedTopLeft, so Position is the world coordinate at the
    // viewport's top-left. We constrain the *unoccluded* gap (the part of the
    // viewport not hidden by panels) to stay within the map bounds.
    private static float ClampAxis(float pos, float viewportExtent, float lowInset, float highInset,
                                   float zoom, float mapMin, float mapExtent)
    {
        // World-space extent of the visible gap, and world offset from the
        // viewport edge to where that gap begins.
        float gapExtent = (viewportExtent - lowInset - highInset) / zoom;
        float lowOffset = lowInset / zoom;

        // If the map is smaller than the gap, center the map within the gap.
        if (mapExtent <= gapExtent)
        {
            return mapMin - (gapExtent - mapExtent) / 2f - lowOffset;
        }
        // Otherwise keep the gap's edges within the map.
        return Math.Clamp(pos, mapMin - lowOffset, mapMin + mapExtent - gapExtent - lowOffset);
    }

    public void ZoomIn(Vector2? zoomCenter)
	{
        if (!zoomCenter.HasValue)
        {
            // If no zoom center is provided, use the center of the viewport
            zoomCenter = Position + GetViewport().GetVisibleRect().Size / (2 * Zoom.X);
        }
        float newZoom = Math.Min(1.5f * Zoom.X, MaxZoom);
        ZoomTo(newZoom, zoomCenter.Value);
	}
	public void ZoomOut(Vector2? zoomCenter)
	{
        if (!zoomCenter.HasValue)
        {
            // If no zoom center is provided, use the center of the viewport
            zoomCenter = Position + GetViewport().GetVisibleRect().Size / (2 * Zoom.X);
        }
		float newZoom = Math.Max(2 * Zoom.X / 3, GetMinZoom());
        ZoomTo(newZoom, zoomCenter.Value);
	}

    public void ZoomTo(float zoomLevel, Vector2 zoomCenter)
    {
        // zoom and then adjust
        zoomLevel = Math.Clamp(zoomLevel, GetMinZoom(), MaxZoom);
        Zoom = new Vector2(zoomLevel, zoomLevel);

        // Calculate the new center after zooming
        Vector2 newCenter = Position + GetViewport().GetVisibleRect().Size / (2 * zoomLevel);
        // Adjust the position to keep the zoom center fixed
        Position += zoomCenter - newCenter;
        ClampCamera();
    }
}
