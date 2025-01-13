using Godot;
using System;
using System.Runtime.CompilerServices;

public partial class Camera2D : Godot.Camera2D
{
    [Export(PropertyHint.Range, "10, 100, 5")]
    int MapBorderPixels = 100;
    [Export]
	SectorMap _sectorMap;
    [Export]
    float MaxZoom = 10;
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

        }
	}

    /*private void ClampCameraPosition(Vector2 moveVector)
    {
        // Get the current viewport size and zoom
        Vector2 viewportSize = GetViewportRect().Size;
        Vector2 currentZoom = Zoom;

        // Calculate the edges of the viewable area in world coordinates
        // The offsets here ensure that the camera stops at the edge of the border
        float leftEdge = -MapBorderPixels * currentZoom.X;
        float topEdge = -MapBorderPixels * currentZoom.Y;
        float rightEdge = (_mapPixelDimensions.X + MapBorderPixels) * currentZoom.X;
        float bottomEdge = (_mapPixelDimensions.Y + MapBorderPixels) * currentZoom.Y;

        // Calculate the new position
        Vector2 newPosition = Position + moveVector;

        // Clamp the new position within the calculated bounds
        newPosition.X = Mathf.Clamp(newPosition.X, leftEdge - viewportSize.X / 2, rightEdge - viewportSize.X / 2);
        newPosition.Y = Mathf.Clamp(newPosition.Y, bottomEdge - viewportSize.Y / 2, topEdge - viewportSize.Y / 2);

        // Update the camera position
        Position = newPosition;
    }*/

    private void ZoomIn(Vector2? zoomCenter)
	{
        if (!zoomCenter.HasValue)
        {
            // If no zoom center is provided, use the center of the viewport
            zoomCenter = GetViewport().GetVisibleRect().Size / 2;
        }
        float newZoom = Math.Min(1.5f * Zoom.X, MaxZoom);
        ZoomTo(newZoom, zoomCenter.Value);
	}
	private void ZoomOut(Vector2? zoomCenter)
	{
        if (!zoomCenter.HasValue)
        {
            // If no zoom center is provided, use the center of the viewport
            zoomCenter = GetViewport().GetVisibleRect().Size / 2;
        }
        Vector2 screenSize = GetViewport().GetVisibleRect().Size;
		float minZoomX = screenSize.X / _mapPixelDimensions.X;
		float minZoomY = screenSize.Y / _mapPixelDimensions.Y;
		float minZoom = Math.Min(minZoomX, minZoomY);
		float newZoom = Math.Max(2 * Zoom.X / 3, minZoom);
        ZoomTo(newZoom, zoomCenter.Value);
	}

    public void ZoomTo(float zoomLevel, Vector2 zoomCenter)
    {
        // zoom and then adjust
        Zoom = new Vector2(zoomLevel, zoomLevel);
        GD.Print($"zoomCenter: {zoomCenter.X},{zoomCenter.Y}");

        // Calculate the new center after zooming
        Vector2 newCenter = Position + GetViewport().GetVisibleRect().Size / (2 * zoomLevel);
        GD.Print($"current Position: {Position.X},{Position.Y}");
        GD.Print($"newCenter: {newCenter.X},{newCenter.Y}");
        // Adjust the position to keep the zoom center fixed
        Position += zoomCenter - newCenter;
        GD.Print($"new Position: {Position.X},{Position.Y}");
    }
}
