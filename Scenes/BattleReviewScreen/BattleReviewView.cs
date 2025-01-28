using Godot;
using System;

public partial class BattleReviewView : DialogView
{
	private RichTextLabel _turnReportRichText;
	private Camera2D _camera;

	public override void _Ready()
	{
		base._Ready();
		_turnReportRichText = GetNode<RichTextLabel>("/TurnReportPanel/ScrollContainer/TurnReportRichTextLabel");
        _camera = GetNode<Camera2D>("/DrawPanel/SubViewportContainer/SubViewport/Camera2D");
    }
}
