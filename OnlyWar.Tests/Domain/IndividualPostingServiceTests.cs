using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using System;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class IndividualPostingServiceTests
{
    [Fact]
    public void MedicalPosting_PreservesNominalMembership_AndExcludesPresence()
    {
        PlayerSoldier casualty = Player("Casualty");
        PlayerSoldier brother = Player("Brother");
        Squad squad = SquadWith(casualty, brother);
        var fixture = SectorSimulationFixture.Create();
        squad.CurrentRegion = fixture.Planet.Regions[0];

        new IndividualPostingService().BeginMedicalDetachment(
            casualty,
            CampaignLocation.Landed(fixture.Planet.Regions[1]),
            new Date(42, 1, 1));

        Assert.Contains(casualty, squad.Members);
        Assert.Equal(2, SoldierPresenceService.NominalCount(squad));
        Assert.Equal(1, SoldierPresenceService.PresentCount(squad));
        Assert.Same(fixture.Planet.Regions[1], casualty.EffectiveRegion);
    }

    [Fact]
    public void IndividualBoarding_ReplacesHomeSquadSeat_WithoutDoubleCounting()
    {
        PlayerSoldier casualty = Player("Casualty");
        PlayerSoldier brother = Player("Brother");
        Squad squad = SquadWith(casualty, brother);
        Ship ship = new(10, "Mercy", new ShipTemplate(10, "Transport", 5, 0, 0));
        ship.LoadSquad(squad);
        squad.BoardedLocation = ship;

        new IndividualPostingService().BeginMedicalDetachment(
            casualty, CampaignLocation.Aboard(ship), new Date(42, 1, 1));

        Assert.Equal(2, ship.LoadedSoldierCount);
        Assert.Single(ship.IndividuallyBoardedSoldiers);
        Assert.Equal(3, ship.AvailableCapacity);
    }

    [Fact]
    public void RecoveryCompletion_WaitsForExplicitReunion()
    {
        PlayerSoldier casualty = Player("Casualty");
        PlayerSoldier brother = Player("Brother");
        Squad squad = SquadWith(casualty, brother);
        var fixture = SectorSimulationFixture.Create();
        squad.CurrentRegion = fixture.Planet.Regions[0];
        IndividualPostingService service = new();
        service.BeginMedicalDetachment(
            casualty, CampaignLocation.Landed(fixture.Planet.Regions[0]), new Date(42, 1, 1));

        service.MarkAwaitingReunion(casualty);

        Assert.Equal(IndividualPostingKind.AwaitingReunion, casualty.IndividualPosting.Kind);
        Assert.Equal(1, SoldierPresenceService.PresentCount(squad));
        Assert.True(service.CanRejoin(casualty, out _));
        service.Rejoin(casualty);
        Assert.Null(casualty.IndividualPosting);
        Assert.Equal(2, SoldierPresenceService.PresentCount(squad));
    }

    [Fact]
    public void FullDestinationShip_RejectsPostingWithoutChangingSoldierOrManifest()
    {
        PlayerSoldier casualty = Player("Casualty");
        SquadWith(casualty);
        Ship fullShip = new(10, "Mercy", new ShipTemplate(10, "Transport", 1, 0, 0));
        PlayerSoldier passenger = Player("Passenger");
        Squad passengerSquad = SquadWith(passenger);
        fullShip.LoadSquad(passengerSquad);
        passengerSquad.BoardedLocation = fullShip;

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new IndividualPostingService().BeginMedicalDetachment(
                casualty, CampaignLocation.Aboard(fullShip), new Date(42, 1, 1)));

        Assert.Contains("no passenger berth", error.Message);
        Assert.Null(casualty.IndividualPosting);
        Assert.Empty(fullShip.IndividuallyBoardedSoldiers);
        Assert.Equal(0, fullShip.AvailableCapacity);
    }

    private static PlayerSoldier Player(string name) =>
        new(TestModelFactory.CreateSoldier(name: name), name);

    private static Squad SquadWith(params PlayerSoldier[] soldiers)
    {
        Squad squad = new("Test Squad", null, TestModelFactory.SquadTemplate);
        foreach (PlayerSoldier soldier in soldiers) squad.AddSquadMember(soldier);
        return squad;
    }
}
