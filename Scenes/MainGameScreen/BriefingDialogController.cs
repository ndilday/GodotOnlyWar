// Controller for the Promised World opening briefing dialog (Design/OpeningScenario.md §5).
// Mirrors EndOfTurnDialogController: the base DialogController forwards the view's acknowledge
// (CloseButton) press as CloseButtonPressed; MainGameScene shows this once on a new game and sets
// CampaignScenario.BriefingAcknowledged on dismiss.
public partial class BriefingDialogController : DialogController
{
    private BriefingDialogView _view;

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<BriefingDialogView>("DialogView");
    }

    public void SetBriefing(string briefingText)
    {
        _view.SetBriefingText(briefingText);
    }
}
