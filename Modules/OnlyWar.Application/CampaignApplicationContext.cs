using System;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
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
    internal CampaignServices Services { get; }
    public CampaignRecoverabilityTracker Recoverability { get; } = new();
    internal GameSession ActiveSession { get; private set; }
    internal OperationsReadContext OperationsRead { get; private set; }
    internal OperationsCommandContext OperationsCommand { get; private set; }
    internal MedicalReadContext MedicalRead { get; private set; }
    internal MedicalCommandContext MedicalCommand { get; private set; }
    internal RecoveryPlanService MedicalRecoveryPlans { get; }
    internal FleetCommandContext FleetCommand { get; private set; }
    internal TrainingContext Training { get; private set; }
    public Guid SessionToken { get; private set; } = Guid.NewGuid();

    public event EventHandler SessionChanged;

    public CampaignApplicationContext(CampaignServices services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        MedicalRecoveryPlans = new RecoveryPlanService(
            Services.Operations.Personnel,
            Services.Operations.Commitments);
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
        OperationsRead = new OperationsReadContext(
            session.Sector,
            session.CurrentDate,
            Services.Readiness.Decisions,
            Services.Operations.Availability);
        OperationsCommand = new OperationsCommandContext(
            session.Sector,
            session.CurrentDate,
            session.Identity,
            Services.Readiness.Decisions,
            Services.Operations.Personnel,
            Services.Operations.Availability);
        MedicalRead = new MedicalReadContext(session.Sector, session.CurrentDate);
        MedicalCommand = new MedicalCommandContext(
            session.Sector,
            session.CurrentDate,
            MedicalRecoveryPlans);
        FleetCommand = new FleetCommandContext(session.Sector, session.Rules);
        Training = new TrainingContext(
            session.Sector,
            session.Rules,
            session.CurrentDate,
            session.Identity);
        ReplaceSessionToken();
    }

    public void Close()
    {
        ActiveSession = null;
        OperationsRead = null;
        OperationsCommand = null;
        MedicalRead = null;
        MedicalCommand = null;
        FleetCommand = null;
        Training = null;
        ReplaceSessionToken();
    }

    public TurnResolutionResult AdvanceTurn(GameSession session = null)
    {
        GameSession target = session ?? ActiveSession
            ?? throw new InvalidOperationException("No campaign session is active.");
        BattleEngagementResolver engagement = Services.Battle.CreateEngagementResolver(target);
        return new TurnController(
            target,
            Services.Readiness.Decisions,
            Services.Operations.Personnel,
            Services.Operations.Commitments,
            engagement,
            engagement).ProcessTurn(target.Sector);
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
