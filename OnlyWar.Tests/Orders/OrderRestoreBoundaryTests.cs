using OnlyWar.Medical.Readiness;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Domain.Missions;
using OnlyWar.Operations.Orders;
using OnlyWar.Operations.Planetary;
using OnlyWar.Domain;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Orders;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class OrderRestoreBoundaryTests
{
    [Theory]
    [InlineData(SquadTypes.Administrative)]
    public void Restore_Character_RestoresCanonicalMembership(SquadTypes kind)
    {
        var fixture = SectorSimulationFixture.Create();
        Squad squad = CreateSquad(fixture, "First", true);
        PlayerSoldier character = CreateCharacter(fixture, kind);
        Order order = CancelledOrder(fixture);

        var result = OrderMutationService.RestoreParticipants(
            fixture.Sector, order, [squad], [character], new MedicalReadinessDecisions(),
            personnel: TestPersonnelComposition.CreatePersonnel());

        Assert.True(result.Succeeded, result.Message);
        Assert.Same(order, squad.CurrentOrders);
        Assert.Same(order, character.CurrentOrder);
        Assert.Contains(character, order.AssignedCharacters);
        Assert.Contains(character, character.AssignedSquad.Members);
        Assert.Null(character.IndividualPosting);
    }

    [Fact]
    public void Restore_CharacterAssignedElsewhere_DoesNotRestoreEarlierSquad()
    {
        var fixture = SectorSimulationFixture.Create();
        Squad squad = CreateSquad(fixture, "First", true);
        PlayerSoldier character = CreateCharacter(fixture, SquadTypes.Administrative);
        Order other = CancelledOrder(fixture);
        other.AssignedCharacters.Add(character);
        character.CurrentOrder = other;
        Order order = CancelledOrder(fixture);

        var result = OrderMutationService.RestoreParticipants(
            fixture.Sector, order, [squad], [character], new MedicalReadinessDecisions(),
            personnel: TestPersonnelComposition.CreatePersonnel());

        Assert.False(result.Succeeded);
        Assert.Null(squad.CurrentOrders);
        Assert.Same(other, character.CurrentOrder);
        Assert.Contains(character, other.AssignedCharacters);
        Assert.Empty(order.AssignedSquads);
        Assert.DoesNotContain(order, fixture.Sector.Orders.Values);
    }

    [Fact]
    public void CancellationToken_RestoresCapturedParticipantsAndCannotRunTwice()
    {
        var fixture = SectorSimulationFixture.Create();
        Squad squad = CreateSquad(fixture, "First", true);
        Order order = new([squad], true, false, Aggression.Normal,
            new Mission(MissionType.Recon, fixture.DefaultRegionFaction(0), 0),
            fixture.Sector.PlayerForce.Faction);
        fixture.Sector.AddNewOrder(order);
        var token = OrderMutationService.CaptureCancellationUndo(fixture.Sector, order);

        Assert.NotNull(token);
        Assert.True(OrderMutationService.Cancel(fixture.Sector, order).Succeeded);
        Assert.Null(squad.CurrentOrders);
        Assert.Empty(order.AssignedSquads);
        var restored = OrderMutationService.Restore(
            fixture.Sector, token, new MedicalReadinessDecisions(),
            personnel: TestPersonnelComposition.CreatePersonnel());

        Assert.True(restored.Succeeded, restored.Message);
        Assert.Same(order, squad.CurrentOrders);
        Assert.Single(order.AssignedSquads);
        Assert.False(OrderMutationService.Restore(
            fixture.Sector, token, new MedicalReadinessDecisions(),
            personnel: TestPersonnelComposition.CreatePersonnel()).Succeeded);
        Assert.Single(order.AssignedSquads);
    }

    [Fact]
    public void CancellationToken_AfterReplacement_DoesNotMutateEitherCampaign()
    {
        var original = SectorSimulationFixture.Create();
        Squad squad = CreateSquad(original, "First", true);
        Order order = new([squad], true, false, Aggression.Normal,
            new Mission(MissionType.Recon, original.DefaultRegionFaction(0), 0),
            original.Sector.PlayerForce.Faction);
        original.Sector.AddNewOrder(order);
        var token = OrderMutationService.CaptureCancellationUndo(original.Sector, order);
        Assert.True(OrderMutationService.Cancel(original.Sector, order).Succeeded);
        var replacement = SectorSimulationFixture.Create();

        Assert.False(OrderMutationService.Restore(
            replacement.Sector, token, new MedicalReadinessDecisions(),
            personnel: TestPersonnelComposition.CreatePersonnel()).Succeeded);

        Assert.Null(squad.CurrentOrders);
        Assert.Empty(order.AssignedSquads);
        Assert.DoesNotContain(order, original.Sector.Orders.Values);
        Assert.DoesNotContain(order, replacement.Sector.Orders.Values);
    }

    [Fact]
    public void Restore_InvalidSecondSquad_DoesNotCommitFirstSquadOrRegisterOrder()
    {
        var fixture = SectorSimulationFixture.Create();
        Squad first = CreateSquad(fixture, "First", true);
        Squad second = CreateSquad(fixture, "Second", false);
        Order order = CancelledOrder(fixture);

        var result = OrderMutationService.RestoreParticipants(
            fixture.Sector, order, [first, second], [], new MedicalReadinessDecisions(),
            personnel: TestPersonnelComposition.CreatePersonnel());

        Assert.False(result.Succeeded);
        Assert.Null(first.CurrentOrders);
        Assert.Null(second.CurrentOrders);
        Assert.Empty(order.AssignedSquads);
        Assert.DoesNotContain(order, fixture.Sector.Orders.Values);
    }

    [Fact]
    public void Restore_ValidSquads_RestoresBothSidesAndRegistersOrder()
    {
        var fixture = SectorSimulationFixture.Create();
        Squad first = CreateSquad(fixture, "First", true);
        Squad second = CreateSquad(fixture, "Second", true);
        Order order = CancelledOrder(fixture);

        var result = OrderMutationService.RestoreParticipants(
            fixture.Sector, order, [first, second], [], new MedicalReadinessDecisions(),
            personnel: TestPersonnelComposition.CreatePersonnel());

        Assert.True(result.Succeeded, result.Message);
        Assert.Same(order, first.CurrentOrders);
        Assert.Same(order, second.CurrentOrders);
        Assert.Equal(new[] { first, second }, order.AssignedSquads);
        Assert.Same(order, fixture.Sector.Orders[order.Id]);
    }

    [Fact]
    public void Restore_AfterCampaignReplacement_RejectsOldObjects()
    {
        var original = SectorSimulationFixture.Create();
        Squad squad = CreateSquad(original, "Original", true);
        Order order = CancelledOrder(original);
        var replacement = SectorSimulationFixture.Create();

        var result = OrderMutationService.RestoreParticipants(
            replacement.Sector, order, [squad], [], new MedicalReadinessDecisions(),
            personnel: TestPersonnelComposition.CreatePersonnel());

        Assert.False(result.Succeeded);
        Assert.Null(squad.CurrentOrders);
        Assert.Empty(order.AssignedSquads);
        Assert.DoesNotContain(order, replacement.Sector.Orders.Values);
    }

    // SB-05a: deployment doctrine comes from the force the command names. Both directions matter:
    // the named campaign's doctrine must apply, and no ambient campaign may leak in.
    [Fact]
    public void IssueOrder_AppliesTheNamedCampaignsDoctrineNotTheInstalledOnes()
    {
        var issuing = SectorSimulationFixture.CreateDetached();
        var installed = SectorSimulationFixture.CreateDetached();
        AvailableMission recon = new("Recon", MissionAvailabilityKind.Recon);

        // The other campaign would wave anything through; the one being commanded demands more
        // duty-ready brothers than the five-man squad has.
        installed.Sector.PlayerForce.Army.ChapterOperationalDoctrine
            .MinimumDutyReadySquadStrength = 1;
        issuing.Sector.PlayerForce.Army.ChapterOperationalDoctrine
            .MinimumDutyReadySquadStrength = 6;

        Squad squad = CreateSquad(issuing, "First", true);
        Assert.Null(OrderAssignment.AssignSquadsToMission(
            issuing.OrderCommands, [squad], issuing.Planet.Regions[0], recon, -1,
            Aggression.Normal));
        Assert.Empty(issuing.Sector.Orders.Values);

        // Flip only the commanded campaign's doctrine; the other campaign now forbids what the
        // named one allows, and the named one must still win.
        issuing.Sector.PlayerForce.Army.ChapterOperationalDoctrine
            .MinimumDutyReadySquadStrength = 1;
        installed.Sector.PlayerForce.Army.ChapterOperationalDoctrine
            .MinimumDutyReadySquadStrength = 99;

        Order order = OrderAssignment.AssignSquadsToMission(
            issuing.OrderCommands, [squad], issuing.Planet.Regions[0], recon, -1,
            Aggression.Normal);

        Assert.NotNull(order);
        Assert.Same(order, Assert.Single(issuing.Sector.Orders.Values));
        Assert.Empty(installed.Sector.Orders.Values);
    }

    // SB-05a: order issue and cancellation resolve against the campaign they were handed. Two
    // detached sessions can run the same commands with no campaign installed in the process, and
    // neither one's orders leak into the other.
    [Fact]
    public void IssueAndCancel_OnDetachedSessions_MutateOnlyTheSuppliedCampaign()
    {
        var first = SectorSimulationFixture.CreateDetached();
        var second = SectorSimulationFixture.CreateDetached();
        Squad firstSquad = CreateSquad(first, "First", true);
        Squad secondSquad = CreateSquad(second, "Second", true);
        AvailableMission recon = new("Recon", MissionAvailabilityKind.Recon);

        Order firstOrder = OrderAssignment.AssignSquadsToMission(
            first.OrderCommands, [firstSquad], first.Planet.Regions[0], recon, -1, Aggression.Normal);
        Order secondOrder = OrderAssignment.AssignSquadsToMission(
            second.OrderCommands, [secondSquad], second.Planet.Regions[0], recon, -1, Aggression.Normal);

        Assert.NotNull(firstOrder);
        Assert.NotNull(secondOrder);
        Assert.Same(firstOrder, Assert.Single(first.Sector.Orders.Values));
        Assert.Same(secondOrder, Assert.Single(second.Sector.Orders.Values));
        Assert.True(OrderAssignment.UnassignSquads([firstSquad]));

        Assert.Empty(first.Sector.Orders.Values);
        Assert.Same(secondOrder, Assert.Single(second.Sector.Orders.Values));
        Assert.Same(secondOrder, secondSquad.CurrentOrders);
    }

    private static Order CancelledOrder(SectorSimulationFixture fixture) => new(
        [], true, false, Aggression.Normal,
        new Mission(MissionType.Recon, fixture.DefaultRegionFaction(0), 0),
        fixture.Sector.PlayerForce.Faction);

    private static PlayerSoldier CreateCharacter(SectorSimulationFixture fixture, SquadTypes kind)
    {
        SquadTemplate template = new(991, "Specialists", TestModelFactory.DefaultWeapons,
            [], TestModelFactory.TestArmor,
            [new SquadTemplateElement(TestModelFactory.MarineTemplate, 0, 4)], kind)
        { Faction = fixture.Sector.PlayerForce.Faction };
        Squad home = new("Specialists", null, template);
        PlayerSoldier character = new(TestModelFactory.CreateSoldier(), "Specialist");
        home.AddSquadMember(character);
        home.CurrentRegion = fixture.Planet.Regions[0];
        return character;
    }

    private static Squad CreateSquad(SectorSimulationFixture fixture, string name, bool leader)
    {
        SquadTemplate template = new(990, "Boundary test", TestModelFactory.DefaultWeapons,
            new List<SquadWeaponOption>(), TestModelFactory.TestArmor,
            TestModelFactory.SquadTemplate.Elements.ToList(), SquadTypes.None)
        { Faction = fixture.Sector.PlayerForce.Faction };
        Squad squad = new(name, null, template);
        for (int index = 0; index < 5; index++)
            squad.AddSquadMember(TestModelFactory.CreateSoldier(
                template: leader && index == 0 ? TestModelFactory.SergeantTemplate : null));
        squad.CurrentRegion = fixture.Planet.Regions[0];
        return squad;
    }
}
