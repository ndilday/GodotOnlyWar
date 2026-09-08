using System;
using OnlyWar.Application;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Application.Adapters.Operations;
using OnlyWar.Helpers.Orders;
using OnlyWar.Helpers.Readiness;
using OnlyWar.Operations.Contracts;
using OnlyWar.Abstractions;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Helpers.Storage;
using OnlyWar.Models;

namespace OnlyWar.Tests.Fixtures;

/// <summary>
/// Small explicit composition helpers for tests that exercise a capability without constructing
/// the full application. Each call creates the dependencies it returns; no test initialization
/// hook changes process-wide state.
/// </summary>
internal static class TestPersonnelComposition
{
    internal static IOperationsPersonnelSurface CreatePersonnel()
    {
        return new OperationsPersonnelSurface(
            new MedicalReadinessDecisions(),
            new OrderCommitmentSurface());
    }

    internal static TestCampaignComposition CreateCampaign(IRNG random = null) =>
        new(random ?? new SeededRNG(1));
}

internal sealed class TestCampaignComposition
{
    public CampaignServices Services { get; }

    public TestCampaignComposition(IRNG random)
    {
        Services = new CampaignServices(
            random ?? throw new ArgumentNullException(nameof(random)),
            new GameStorage(
                RulesDatabaseFixture.RepositoryRoot,
                System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "OnlyWarTests",
                    Guid.NewGuid().ToString("N"))));
    }

    public CampaignApplication CreateApplication() => new(Services);

    public TurnController CreateTurnController(
        GameSession session,
        ISoldierTrainingService trainingService = null) =>
        new(
            session,
            Services.Readiness.Decisions,
            Services.Operations.Personnel,
            Services.Operations.Commitments,
            Services.Battle,
            trainingService);
}
