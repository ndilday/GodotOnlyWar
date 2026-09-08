using System;
using System.Linq;
using OnlyWar.Abstractions;
using OnlyWar.Battles.Abstractions;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Application.Adapters.Generation;
using OnlyWar.Helpers.Application.Adapters.Operations;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Aftermath;
using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Helpers.Storage;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Helpers.Readiness;
using OnlyWar.Medical.Abstractions;
using OnlyWar.Operations.Contracts;
using OnlyWar.Helpers.Orders;
using OnlyWar.Models;

namespace OnlyWar.Application;

/// <summary>
/// The application-owned composition root for one campaign host. The grouped capabilities are
/// ordinary objects held by the application; no subsystem needs to discover its implementation
/// through a process-wide default.
/// </summary>
public sealed class CampaignServices
{
    public IRNG Random { get; }
    public PersistenceServices Persistence { get; }
    public ReadinessServices Readiness { get; }
    public OperationsServices Operations { get; }
    public BattleServices Battle { get; }
    public GenerationServices Generation { get; }

    public CampaignServices(IRNG random, GameStorage storage)
    {
        Random = random ?? throw new ArgumentNullException(nameof(random));
        Persistence = new PersistenceServices(storage);
        Readiness = new ReadinessServices(new MedicalReadinessDecisions());

        IOrderCommitmentSurface commitments = new OrderCommitmentSurface();
        Operations = new OperationsServices(
            new OperationsPersonnelSurface(Readiness.Decisions, commitments),
            commitments);
        Battle = new BattleServices();
        Generation = new GenerationServices(Readiness, Operations, Battle);
    }
}

/// <summary>Persistence capabilities for the host's selected install and save directories.</summary>
public sealed class PersistenceServices
{
    public GameStorage Storage { get; }
    public GameStateDataAccess GameState { get; }
    public CampaignLoader CampaignLoader { get; }
    public CurrentCampaignSaveWriter SaveWriter { get; }
    public SaveGameManager SaveManager { get; }

    public PersistenceServices(GameStorage storage)
    {
        Storage = storage ?? throw new ArgumentNullException(nameof(storage));
        GameState = new GameStateDataAccess(Storage.SaveSchemaPath);
        CampaignLoader = new CampaignLoader(Storage, GameState);
        SaveWriter = new CurrentCampaignSaveWriter(GameState);
        SaveManager = new SaveGameManager(Storage.SaveDirectory);
    }
}

/// <summary>Medical policy capabilities shared by Operations and application workflows.</summary>
public sealed class ReadinessServices
{
    public IReadinessDecisions Decisions { get; }

    public ReadinessServices(IReadinessDecisions decisions) =>
        Decisions = decisions ?? throw new ArgumentNullException(nameof(decisions));
}

/// <summary>Operations capabilities that require live Campaign state to apply.</summary>
public sealed class OperationsServices
{
    public IOperationsPersonnelSurface Personnel { get; }
    public IPersonnelAvailabilityQueries Availability => Personnel;
    public IOrderCommitmentSurface Commitments { get; }

    public OperationsServices(
        IOperationsPersonnelSurface personnel,
        IOrderCommitmentSurface commitments)
    {
        Personnel = personnel ?? throw new ArgumentNullException(nameof(personnel));
        Commitments = commitments ?? throw new ArgumentNullException(nameof(commitments));
    }
}

/// <summary>
/// Battle construction belongs to the application because it joins live Campaign state to the
/// Battles ports. The resolver is created per session so it cannot retain a previous campaign.
/// </summary>
public sealed class BattleServices
{
    public BattleEngagementResolver CreateEngagementResolver(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        CampaignBattleEquipmentSource equipment =
            new(session.Rules, session.Sector.PlayerForce);
        BattleAftermathDependencies aftermath = new(
            session.CurrentDate,
            session.Random,
            new PlayerBattleAftermathSink(session.Sector.PlayerForce));
        BattleExecutionContext execution = new(
            session.Rules,
            session.Random,
            aftermath);
        return new BattleEngagementResolver(
            execution,
            equipment,
            regionId => session.Sector.Planets.Values
                .SelectMany(planet => planet.Regions)
                .FirstOrDefault(region => region?.Id == regionId));
    }
}

/// <summary>Generation capabilities assembled from the same Operations and Medical services.</summary>
public sealed class GenerationServices
{
    private readonly IReadinessDecisions _readiness;
    private readonly OperationsServices _operations;
    private readonly BattleServices _battle;

    public GenerationServices(
        ReadinessServices readiness,
        OperationsServices operations,
        BattleServices battle)
    {
        _readiness = readiness?.Decisions
            ?? throw new ArgumentNullException(nameof(readiness));
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _battle = battle ?? throw new ArgumentNullException(nameof(battle));
    }

    public GenerationSupport CreateSupport(GameRulesData rules, Date date, IRNG random) =>
        CandidateGenerationSupport.For(
            rules,
            date,
            random,
            _readiness,
            _operations.Personnel,
            _operations.Commitments,
            _battle);
}
