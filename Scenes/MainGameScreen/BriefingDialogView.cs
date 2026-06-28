using Godot;

// View for the Promised World opening briefing (Design/OpeningScenario.md §5). Follows the
// DialogView/EndOfTurnDialogView pattern: the base wires the single acknowledge button (the
// inherited "CloseButton", relabelled "For the Emperor" in the scene) to CloseButtonPressed;
// this view just renders the composed, BBCode briefing text into a RichTextLabel.
public partial class BriefingDialogView : DialogView
{
    private RichTextLabel _briefingLabel;

    public override void _Ready()
    {
        base._Ready();
        _briefingLabel = GetNode<RichTextLabel>("Panel/MarginContainer/ScrollContainer/BriefingLabel");
    }

    public void SetBriefingText(string text)
    {
        _briefingLabel.Text = text;
    }
}
