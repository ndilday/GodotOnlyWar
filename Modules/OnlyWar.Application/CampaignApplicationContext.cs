using System;
using OnlyWar.Domain;
using OnlyWar.Battles;
using OnlyWar.Campaign.Recruitment;
using OnlyWar.Campaign.Simulation;
using OnlyWar.Persistence.Storage;
using OnlyWar.Campaign.Turns;
using OnlyWar.Runtime;

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
    internal FleetScreenContext FleetScreen { get; private set; }
    internal TrainingContext Training { get; private set; }
    internal TrainingScreenContext TrainingScreen { get; private set; }
    internal CommandScreenContext Command { get; private set; }
    internal DiplomacyScreenContext Diplomacy { get; private set; }
    internal CampaignNavigationContext Navigation { get; private set; }
    internal MainScreenContext Main { get; private set; }
    internal SectorMapContext SectorMap { get; private set; }
    internal SessionControlContext SessionControl { get; private set; }
    internal ChapterScreenContext Chapter { get; private set; }
    internal MusterScreenContext Muster { get; private set; }
    internal LoadoutScreenContext Loadout { get; private set; }
    public Guid SessionToken { get; private set; } = Guid.NewGuid();
    // What the turn in progress is doing now. A host resolving the turn off its UI thread polls
    // this to tell the player where a long end of turn has got to.
    public TurnProgress TurnProgress { get; } = new();

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
        FleetCommand = new FleetCommandContext(session.Sector, session.Rules, session.Identity);
        FleetScreen = new FleetScreenContext(FleetCommand);
        Training = new TrainingContext(
            session.Sector,
            session.Rules,
            session.CurrentDate,
            session.Identity);
        TrainingScreen = new TrainingScreenContext(Training);
        RecruitmentPromotionService promotions = new(
            session.Sector,
            session.Rules,
            session.CurrentDate,
            session.Random,
            session.Identity,
            Services.NameGenerator);
        Command = new CommandScreenContext(
            session.Sector, session.Rules, session.CurrentDate);
        Diplomacy = new DiplomacyScreenContext(session.Sector, session.Rules);
        Navigation = new CampaignNavigationContext(session.Sector);
        Main = new MainScreenContext(
            session.Sector,
            session.Rules,
            session.CurrentDate,
            promotions,
            () => AdvanceTurn());
        SectorMap = new SectorMapContext(
            session.Sector, session.Rules.SectorGenerationProfile);
        SessionControl = new SessionControlContext(
            session.Sector,
            session.Rules,
            Services.Persistence.SaveManager,
            Recoverability,
            path => Save(path));
        Chapter = new ChapterScreenContext(
            session.Sector,
            session.Rules,
            session.CurrentDate,
            session.Identity,
            promotions);
        Muster = new MusterScreenContext(
            session.Sector,
            session.Rules,
            session.CurrentDate,
            session.Identity);
        Loadout = new LoadoutScreenContext(
            session.Sector,
            session.Rules,
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
        FleetScreen = null;
        Training = null;
        TrainingScreen = null;
        Command = null;
        Diplomacy = null;
        Navigation = null;
        Main = null;
        SectorMap = null;
        SessionControl = null;
        Chapter = null;
        Muster = null;
        Loadout = null;
        ReplaceSessionToken();
    }

    public TurnResolutionResult AdvanceTurn(GameSession session = null)
    {
        GameSession target = session ?? ActiveSession
            ?? throw new InvalidOperationException("No campaign session is active.");
        TurnProgress.Report(string.Empty);
        BattleEngagementResolver engagement =
            Services.Battle.CreateEngagementResolver(target, TurnProgress);
        try
        {
            return new TurnController(
                target,
                Services.Readiness.Decisions,
                Services.Operations.Personnel,
                Services.Operations.Commitments,
                engagement,
                engagement,
                nameGenerator: Services.NameGenerator,
                progress: TurnProgress).ProcessTurn(target.Sector);
        }
        finally
        {
            TurnProgress.Report(string.Empty);
        }
    }

    public void Save(string filePath, GameSession session = null)
    {
        GameSession target = session ?? ActiveSession
            ?? throw new InvalidOperationException("No campaign session is active.");
        Services.Persistence.SaveWriter.Write(filePath, target);
    }

    internal SystemInspectorContext CreateSystemInspectorContext(
        IOperationsScreenQueries operationsQueries,
        IFleetScreenApplication fleet) =>
        ActiveSession == null
            ? null
            : new SystemInspectorContext(ActiveSession.Sector, operationsQueries, fleet);

    public void MarkChanged() => Recoverability.MarkChanged();

    internal bool HasSession => ActiveSession != null;

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
        Context.HasSession && sessionToken == SessionToken;

    protected void RecordChange() => Context.MarkChanged();
}
