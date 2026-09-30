using Godot;
using OnlyWar.Application;
using System;

/// <summary>
/// Drives the Armory workspace through <see cref="IArmoryScreenApplication"/> only. Sending a
/// brother to Mars and the Armory promotions are confirmed first: neither can be undone, and a
/// brother on Mars cannot be recalled.
/// </summary>
public partial class ArmoryScreenController : MainScreenController
{
    private enum PendingAction { None, SendToMars, Promote }

    private const int DialogWidth = 560;

    private IArmoryScreenApplication _application;
    private Guid _sessionToken;
    private ArmoryScreenView _view;
    private ConfirmationDialog _confirmation;
    private AcceptDialog _blocked;
    private PendingAction _pendingAction;
    private int _pendingSoldierId;
    private int _pendingTemplateId;

    public event EventHandler CampaignChanged;

    public void Configure(IArmoryScreenApplication application)
    {
        if (_application != null) _application.SessionChanged -= OnSessionChanged;
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _application.SessionChanged += OnSessionChanged;
        _sessionToken = application.SessionToken;
    }

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<ArmoryScreenView>("ArmoryScreenView");
        _view.SendToMarsPressed += OnSendToMarsPressed;
        _view.PromotePressed += OnPromotePressed;
        _view.ReturnDestinationSelected += OnReturnDestinationSelected;

        _confirmation = new ConfirmationDialog
        {
            Title = "Confirm",
            OkButtonText = "CONFIRM",
            DialogAutowrap = true
        };
        _confirmation.Confirmed += OnConfirmed;
        _confirmation.Canceled += ClearPending;
        AddChild(_confirmation);
        _blocked = new AcceptDialog { Title = "Armory", DialogAutowrap = true };
        AddChild(_blocked);
        Render();
    }

    public override void _ExitTree()
    {
        if (_application != null) _application.SessionChanged -= OnSessionChanged;
        if (_view != null)
        {
            _view.SendToMarsPressed -= OnSendToMarsPressed;
            _view.PromotePressed -= OnPromotePressed;
            _view.ReturnDestinationSelected -= OnReturnDestinationSelected;
        }
        if (_confirmation != null)
        {
            _confirmation.Confirmed -= OnConfirmed;
            _confirmation.Canceled -= ClearPending;
        }
    }

    /// <summary>
    /// Rebuilds the screen from the current campaign. The instance is reused between openings,
    /// so the host calls this after other screens or a turn may have changed the chapter.
    /// </summary>
    public override void RefreshFromExternalChange() => Render();

    private void OnSessionChanged(object sender, EventArgs e)
    {
        _sessionToken = _application.SessionToken;
        ClearPending();
        if (_view != null) Render();
    }

    private void Render()
    {
        if (_application == null || _view == null) return;
        ArmoryOverview overview = _application.QueryArmory();
        _sessionToken = overview.SessionToken;
        _view.ShowArmory(overview);
    }

    private void OnSendToMarsPressed(object sender, int soldierId) =>
        Ask(PendingAction.SendToMars, soldierId, _application.DescribeSendToMars(soldierId));

    private void OnPromotePressed(object sender, ArmoryScreenView.PromotionRequest request) =>
        Ask(PendingAction.Promote, request.SoldierId,
            _application.DescribePromotion(request.SoldierId, request.TemplateId), request.TemplateId);

    private void Ask(PendingAction action, int soldierId, ArmoryPrompt prompt, int templateId = 0)
    {
        if (!prompt.CanProceed)
        {
            ShowBlocked(prompt.Title, prompt.Message);
            Render();
            return;
        }
        _pendingAction = action;
        _pendingSoldierId = soldierId;
        _pendingTemplateId = templateId;
        _confirmation.Title = prompt.Title;
        _confirmation.DialogText = prompt.Message;
        PopupWrapped(_confirmation);
    }

    private void OnConfirmed()
    {
        ArmoryCommandResult result = _pendingAction switch
        {
            PendingAction.SendToMars => _application.SendToMars(_sessionToken, _pendingSoldierId),
            PendingAction.Promote => _application.Promote(_sessionToken, _pendingSoldierId, _pendingTemplateId),
            _ => null
        };
        ClearPending();
        Apply(result);
    }

    private void OnReturnDestinationSelected(object sender, string destinationKey) =>
        Apply(_application.SetMarsReturnDestination(_sessionToken, destinationKey));

    private void Apply(ArmoryCommandResult result)
    {
        if (result == null) return;
        if (result.Succeeded)
        {
            CampaignChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            ShowBlocked("Armory", result.Message);
        }
        Render();
    }

    private void ShowBlocked(string title, string message)
    {
        _blocked.Title = title;
        _blocked.DialogText = message;
        PopupWrapped(_blocked);
    }

    // With DialogAutowrap on, the text wraps to the dialog's width instead of setting it. Reset
    // the size on every popup: the height then grows to fit this message, not the last one.
    private static void PopupWrapped(AcceptDialog dialog)
    {
        dialog.Size = new Vector2I(DialogWidth, 0);
        dialog.PopupCentered(new Vector2I(DialogWidth, 0));
    }

    private void ClearPending()
    {
        _pendingAction = PendingAction.None;
        _pendingSoldierId = 0;
        _pendingTemplateId = 0;
    }
}
