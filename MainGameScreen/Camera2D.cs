using Godot;
using System;
using System.Runtime.CompilerServices;

public partial class Camera2D : Godot.Camera2D
{
    [Export(PropertyHint.Range, "10, 100, 5")]
    int MapBorderPixels = 100;
    [Export]
	SectorMap _sectorMap;
	float _minZoom, _maxZoom;
	Vector2I _mapPixelDimensions;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
        _mapPixelDimensions = new(_sectorMap.GridDimensions.X * _sectorMap.CellSize.X + 2 * MapBorderPixels, 
								  _sectorMap.GridDimensions.Y * _sectorMap.CellSize.Y + 2 * MapBorderPixels);
    }

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public override void _Input(InputEvent @event)
	{
        if (@event is InputEventMouseButton emb)
        {
            if (emb.ButtonIndex == MouseButton.Left)
            {
                Vector2 gmpos = GetGlobalMousePosition();
                Vector2I mousePosition = new((int)(gmpos.X), (int)(gmpos.Y));
                Vector2I gridPosition = _sectorMap.CalculateGridCoordinates(mousePosition);
                int index = _sectorMap.GridPositionToIndex(gridPosition);
                string text = $"({gridPosition.X},{gridPosition.Y})\nPlanet: {_sectorMap.HasPlanet[index]}\nSubsector: {_sectorMap.SectorIds[index]}";
                _sectorMap.GetNode<TopMenu>("CanvasLayer/TopMenu").SetDebugText(text);
            }
            // zoom in
            else if (emb.ButtonIndex == MouseButton.WheelUp)
            {
                ZoomIn();
            }
            // zoom out
            else if (emb.ButtonIndex == MouseButton.WheelDown)
            {
                ZoomOut();
            }
        }

        // Handle keyboard zoom
        else if (@event is InputEventKey eventKey)
        {
            if (eventKey.Pressed && (eventKey.Keycode == Key.Equal || eventKey.Keycode == Key.KpAdd))
            {
                ZoomIn();
            }
            else if (eventKey.Pressed && (eventKey.Keycode == Key.Minus || eventKey.Keycode == Key.KpSubtract))
            {
                ZoomOut();
            }
        }
        else if (@event is InputEventMouseMotion emm && emm.ButtonMask == MouseButtonMask.Right)
		{
			// TODO: add clamping
			Position -= emm.Relative * Zoom;
		}
	}

	private void ZoomIn()
	{
		float maxZoom = 10;
		float newZoom = Math.Min(1.5f * Zoom.X, maxZoom);
		Zoom = new(newZoom, newZoom);
	}
	private void ZoomOut()
	{
		
		// calculate total possible map width
		// double borders plus 
        Vector2 screenSize = GetViewport().GetVisibleRect().Size;
		float minZoomX = screenSize.X / _mapPixelDimensions.X;
		float minZoomY = screenSize.Y / _mapPixelDimensions.Y;
		float minZoom = Math.Min(minZoomX, minZoomY);
		float newZoom = Math.Max(2 * Zoom.X / 3, minZoom);
        Zoom = new(newZoom, newZoom);
	}
}
