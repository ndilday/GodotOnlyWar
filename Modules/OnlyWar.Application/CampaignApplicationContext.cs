using System;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Helpers.Storage;
using OnlyWar.Helpers.Turns;

namespace OnlyWar.Application;

/// <summary>
/// The mutable application context shared by the screen services. It owns only the selected
/// session and the cross-screen capabilities that must use that same session; screen-specific
/// reads and commands stay in their respective application services.
/// </summary>
public sealed class CampaignApplicationContext
{
    public CampaignServices Services { get; }
    public CampaignRecoverabilityTracker Recoverability { get; } = new();
    public GameSession ActiveSession { get; private set; }
    public Guid SessionToken { get; private set; } = Guid.NewGuid();

    public event EventHandler SessionChanged;

    public CampaignApplicationContext(CampaignServices services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public void Install(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ActiveSession = session;
        if (session.UpgradePending)
        {
            Recoverability.BeginLoadedCampaign();
        }
        else
        {
            Recoverability.BeginNewCampaign();
        }
        ReplaceSessionToken();
    }

    public void Close()
    {
        ActiveSession = null;
        ReplaceSessionToken();
    }

    public TurnResolutionResult AdvanceTurn(GameSession session = null)
    {
        GameSession target = session ?? ActiveSession
            ?? throw new InvalidOperationException("No campaign session is active.");
        return new TurnController(
            target,
            Services.Readiness.Decisions,
            Services.Operations.Personnel,
            Services.Operations.Commitments,
            Services.Battle).ProcessTurn(target.Sector);
    }

    public void Save(string filePath, GameSession session = null)
    {
        GameSession target = session ?? ActiveSession
            ?? throw new InvalidOperationException("No campaign session is active.");
        Services.Persistence.SaveWriter.Write(filePath, target);
    }

    public void MarkChanged() => Recoverability.MarkChanged();

    private void ReplaceSessionToken()
    {
        SessionToken = Guid.NewGuid();
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Common session plumbing for a screen/application service. The base deliberately exposes no
/// screen behavior; it only gives each service the same session token and change notification.
/// </summary>
public abstract class CampaignScreenApplication
{
    protected CampaignApplicationContext Context { get; }
    protected CampaignServices Services => Context.Services;
    protected GameSession ActiveSession => Context.ActiveSession;

    public Guid SessionToken => Context.SessionToken;

    public event EventHandler SessionChanged
    {
        add => Context.SessionChanged += value;
        remove => Context.SessionChanged -= value;
    }

    protected CampaignScreenApplication(CampaignApplicationContext context)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
    }

    protected bool IsCurrentSession(Guid sessionToken) =>
        ActiveSession != null && sessionToken == SessionToken;

    protected void RecordChange() => Context.MarkChanged();
}
