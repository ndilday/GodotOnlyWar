using Godot;
using System;

public partial class BattleReviewView : DialogView
{
	private RichTextLabel _turnReportRichText;
	private RichTextLabel _turnReportLabel;
	private Button _previousTurnButton, _nextTurnButton;
	private Camera2D _camera;

	public override void _Ready()
	{
		base._Ready();
		_turnReportRichText = GetNode<RichTextLabel>("/TurnReportPanel/ScrollContainer/TurnReportRichText");
		_turnReportLabel = GetNode<RichTextLabel>("/TurnReportPanel/TurnReportLabel");
		_camera = GetNode<Camera2D>("/DrawPanel/SubViewportContainer/SubViewport/Camera2D");
		_previousTurnButton = GetNode<Button>("TurnReportPanel/PreviousTurnButton");
        _nextTurnButton = GetNode<Button>("TurnReportPanel/NextTurnButton");
    }

	public void SetTurnReportLabel(string text)
	{
		_turnReportLabel.Text = text;
	}

	public void SetTurnReportText(string text)
    {
        _turnReportRichText.Text = text;
    }

    public void EnableTurnButtons(bool isPreviousEnabled, bool isNextEnabled)
	{
        _previousTurnButton.Disabled = !isPreviousEnabled;
        _nextTurnButton.Disabled = !isNextEnabled;
    }
}
