using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OnlyWar.Application;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Application;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public sealed class OperationsScreenApplicationTests
{
    [Fact]
    public void IssuingAndUndoingAnOrderLeavesTheSquadUncommitted()
    {
        var application = CreateApplication(out var fixture, out var region, out var squad);
        string mission = ReconKey(region);

        var issued = application.SetOrderParticipants(new(
            application.SessionToken, region.Id, mission, [squad.Id], []));

        Assert.True(issued.Succeeded, issued.Message);
        Assert.NotNull(issued.OrderId);
        Assert.Equal("order creation", issued.UndoDescription);
        Assert.Same(fixture.Sector.Orders[issued.OrderId.Value], squad.CurrentOrders);

        var undone = application.UndoLastOperation(new(
            application.SessionToken, issued.UndoToken.Value));

        Assert.True(undone.Succeeded, undone.Message);
        Assert.Null(squad.CurrentOrders);
    }

    [Fact]
    public void AnUndoTokenIsSingleUse()
    {
        var application = CreateApplication(out _, out var region, out var squad);
        var issued = application.SetOrderParticipants(new(
            application.SessionToken, region.Id, ReconKey(region), [squad.Id], []));
        Assert.True(application.UndoLastOperation(
            new(application.SessionToken, issued.UndoToken.Value)).Succeeded);

        var replayed = application.UndoLastOperation(new(
            application.SessionToken, issued.UndoToken.Value));

        Assert.False(replayed.Succeeded);
        Assert.Null(squad.CurrentOrders);
    }

    [Fact]
    public void ReplacingTheSessionRejectsAQueuedUndoAndCancellation()
    {
        var application = CreateApplication(out var fixture, out var region, out var squad);
        var issued = application.SetOrderParticipants(new(
            application.SessionToken, region.Id, ReconKey(region), [squad.Id], []));
        Guid staleToken = application.SessionToken;
        Order order = fixture.Sector.Orders[issued.OrderId.Value];

        application.Install(new GameSession(application.ActiveSession.Rules,
            new Sector(new PlayerForce(null,
                    new Army("Army", null, "Commander", null, []), new Fleet("Fleet", null, null)),
                [], [], []),
            new Date(20_000), new SeededRNG(3)));

        Assert.False(application.UndoLastOperation(
            new(staleToken, issued.UndoToken.Value)).Succeeded);
        Assert.False(application.CancelOrder(new(staleToken, order.Id)).Succeeded);
        // Nothing in the replaced campaign moved: the squad is still committed to its order.
        Assert.Same(order, squad.CurrentOrders);
    }

    [Fact]
    public void CancellationUndoRestoresEverySquadTheOrderHeld()
    {
        var application = CreateApplication(out var fixture, out var region, out var squad);
        var issued = application.SetOrderParticipants(new(
            application.SessionToken, region.Id, ReconKey(region), [squad.Id], []));
        int orderId = issued.OrderId.Value;

        var prompt = application.DescribeOrderCancellation(orderId);
        Assert.True(prompt.Exists);
        Assert.Equal(1, prompt.SquadCount);

        var cancelled = application.CancelOrder(new(application.SessionToken, orderId));
        Assert.True(cancelled.Succeeded, cancelled.Message);
        Assert.Null(squad.CurrentOrders);
        Assert.Null(cancelled.OrderId);

        var restored = application.UndoLastOperation(new(
            application.SessionToken, cancelled.UndoToken.Value));

        Assert.True(restored.Succeeded, restored.Message);
        Assert.NotNull(squad.CurrentOrders);
        Assert.Equal(orderId, squad.CurrentOrders.Id);
    }

    [Fact]
    public void AnIneligibleSquadIsRejectedWithoutChangingAnyCommitment()
    {
        var application = CreateApplication(out var fixture, out var region, out var squad);
        Region distant = fixture.Planet.Regions.First(candidate =>
            candidate != region && !region.GetAdjacentRegions().Contains(candidate));

        var result = application.SetOrderParticipants(new(
            application.SessionToken, distant.Id, ReconKey(distant), [squad.Id], []));

        Assert.False(result.Succeeded);
        Assert.Null(result.UndoToken);
        Assert.Null(squad.CurrentOrders);
        Assert.Empty(fixture.Sector.Orders);
    }

    [Theory]
    [InlineData(typeof(OperationsCommandResult))]
    [InlineData(typeof(OrderParticipantsCommand))]
    [InlineData(typeof(CancelOrderCommand))]
    [InlineData(typeof(LandForceCommand))]
    [InlineData(typeof(EmbarkForceCommand))]
    [InlineData(typeof(DetachCasualtiesCommand))]
    [InlineData(typeof(OrderCancellationPrompt))]
    [InlineData(typeof(OperationsSelection))]
    // The read side is a boundary too: a projection that still carried a Squad or Region would let
    // the screen mutate the campaign through a returned value.
    [InlineData(typeof(OperationsWorkspaceView))]
    [InlineData(typeof(WorldDossierView))]
    [InlineData(typeof(OperationsEntryView))]
    [InlineData(typeof(RegionalOperationsView))]
    [InlineData(typeof(MovementOperationsView))]
    [InlineData(typeof(DetachOperationsView))]
    [InlineData(typeof(PlanetMapProjection))]
    public void OperationsCommandBoundaryContainsNoLiveDomainGraph(Type root)
    {
        Assert.Empty(FindDomainGraph(root));
    }

    [Fact]
    public void OperationsScreenSourcesNameNoCampaignEntity()
    {
        foreach (string file in new[] { "PlanetaryOperationsScreenController.cs",
            "PlanetaryOperationsScreenView.cs" })
        {
            string source = System.IO.File.ReadAllText(System.IO.Path.Combine(
                RulesDatabaseFixture.RepositoryRoot, "Scenes", "PlanetaryOperationsScreen", file));
            foreach (string bypass in new[]
            {
                "GameDataSingleton", "Sector ", "Squad ", "PlayerSoldier", "AvailableMission",
                "OrderMutationService", "PlanetForceMovementService", "MedicalDetachmentService"
            })
            {
                Assert.DoesNotContain(bypass, source);
            }
        }
    }

    [Fact]
    public void OperationsControllerIssuesNoCampaignMutationOfItsOwn()
    {
        string controller = System.IO.File.ReadAllText(System.IO.Path.Combine(
            RulesDatabaseFixture.RepositoryRoot, "Scenes", "PlanetaryOperationsScreen",
            "PlanetaryOperationsScreenController.cs"));
        foreach (string bypass in new[]
        {
            "OrderMutationService", "MedicalDetachmentService", "OrderRestoreToken",
            "OrderAssignment", "PlanetForceMovementService.Land",
            "PlanetForceMovementService.Embark"
        })
        {
            Assert.DoesNotContain(bypass, controller);
        }
        Assert.Contains("IOperationsScreenApplication", controller);
        Assert.Contains("SessionChanged -=", controller);
    }

    private static string ReconKey(Region region) =>
        MissionAvailability.GetAvailableMissions(region, region)
            .Single(option => option.Kind == MissionAvailabilityKind.Recon).IdentityKey;

    private static IReadOnlyList<Type> FindDomainGraph(Type root)
    {
        HashSet<Type> visited = [];
        List<Type> forbidden = [];
        void Inspect(Type type)
        {
            if (!visited.Add(type) || type.IsPrimitive || type.IsEnum || type == typeof(string)
                || type == typeof(Guid) || type == typeof(decimal)) return;
            if (type.IsArray) { Inspect(type.GetElementType()); return; }
            if (type.IsGenericType)
            {
                foreach (var argument in type.GetGenericArguments()) Inspect(argument);
                return;
            }
            if (type.Assembly == typeof(Sector).Assembly) { forbidden.Add(type); return; }
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                Inspect(property.PropertyType);
        }
        Inspect(root);
        return forbidden;
    }

    private static CampaignApplication CreateApplication(
        out SectorSimulationFixture fixture, out Region region, out Squad squad)
    {
        fixture = SectorSimulationFixture.Create();
        region = fixture.Planet.Regions[7];
        squad = AddPlayerSquad(fixture, region, "Operations Squad", members: 5);
        CampaignApplication application = TestPersonnelComposition.CreateCampaign(new SeededRNG(11)).CreateApplication();
        application.Install(new GameSession(
            fixture.Rules, fixture.Sector,
            fixture.CurrentDate, new SeededRNG(12)));
        return application;
    }

    private static Squad AddPlayerSquad(
        SectorSimulationFixture fixture, Region region, string name, int members)
    {
        Faction player = fixture.Sector.PlayerForce.Faction;
        SquadTemplate template = new(
            900, "Operations Test Squad", TestModelFactory.DefaultWeapons,
            new List<SquadWeaponOption>(), TestModelFactory.TestArmor,
            TestModelFactory.SquadTemplate.Elements.ToList(), SquadTypes.None)
        {
            Faction = player
        };
        Squad squad = new(name, null, template);
        squad.AddSquadMember(TestModelFactory.CreateSoldier(
            template: TestModelFactory.SergeantTemplate, name: $"{name} Sergeant"));
        for (int index = 1; index < members; index++)
            squad.AddSquadMember(TestModelFactory.CreateSoldier(name: $"{name} Marine {index}"));

        if (!fixture.Planet.PlanetFactionMap.TryGetValue(player.Id, out PlanetFaction planetPresence))
        {
            planetPresence = new PlanetFaction(player) { IsPublic = true };
            fixture.Planet.PlanetFactionMap[player.Id] = planetPresence;
        }
        if (!region.RegionFactionMap.TryGetValue(player.Id, out RegionFaction presence))
        {
            presence = new RegionFaction(planetPresence, region) { IsPublic = true };
            region.RegionFactionMap[player.Id] = presence;
        }
        presence.LandedSquads.Add(squad);
        squad.CurrentRegion = region;
        return squad;
    }
}
