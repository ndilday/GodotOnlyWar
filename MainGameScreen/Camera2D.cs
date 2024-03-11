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
		if(@event is InputEventMouseMotion emm && emm.ButtonMask == MouseButtonMask.Right)
		{
			// TODO: add clamping
			Position -= emm.Relative * Zoom;
		}
		else if(@event is InputEventMouseButton emb && emb.Pressed)
		{
			switch(emb.ButtonIndex)
			{
				case MouseButton.WheelUp:
					ZoomIn();
					break;
				case MouseButton.WheelDown:
					ZoomOut();
					break;
			}
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
