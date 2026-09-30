using OnlyWar.Campaign;
using OnlyWar.Domain;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;
using OnlyWar.Medical.Abstractions;
using OnlyWar.Medical.Readiness;
using OnlyWar.Operations.Abstractions;
using OnlyWar.Operations.Orders;
using OnlyWar.Operations.Personnel;
using OnlyWar.Operations.Readiness;
using OnlyWar.Tests.Fixtures;
using System;
using System.Linq;
using Xunit;

namespace OnlyWar.Tests.Domain;

/// <summary>
/// Mars pipeline (TDD §6.14): the off-sector location, the
/// Mechanicus posting purpose, and the rule that a fit brother on Mars is available for no duty.
/// </summary>
public class MechanicusPostingTests
{
    private static readonly Date Departure = new(41, 998, 1);
    private static readonly Date Return = new(42, 18, 1);

    [Fact]
    public void OffSector_IsNeverTheSamePlaceAsAnything()
    {
        var fixture = SectorSimulationFixture.Create();

        Assert.True(CampaignLocation.OffSector.IsOffSector);
        Assert.False(CampaignLocation.OffSector.IsShip);
        Assert.False(CampaignLocation.OffSector.IsRegion);
        Assert.False(CampaignLocation.OffSector.IsSamePlace(CampaignLocation.OffSector));
        Assert.False(CampaignLocation.OffSector.IsSamePlace(
            CampaignLocation.Landed(fixture.Planet.Regions[0])));
        Assert.False(CampaignLocation.Landed(fixture.Planet.Regions[0])
            .IsSamePlace(CampaignLocation.OffSector));
    }

    [Fact]
    public void OffSector_IsAcceptedOnlyForTheMechanicusPurpose()
    {
        var fixture = SectorSimulationFixture.Create();
        PlayerSoldier soldier = Player("Brother Adept");
        AdministrativeSquad(soldier);
        IndividualPostingService service = Postings();

        Assert.True(service.CanCreate(
            soldier, IndividualPostingPurpose.Mechanicus, CampaignLocation.OffSector, out _));
        Assert.False(service.CanCreate(
            soldier, IndividualPostingPurpose.Independent, CampaignLocation.OffSector, out _));
        Assert.False(service.CanCreate(
            soldier, IndividualPostingPurpose.Medical, CampaignLocation.OffSector, out _));
        Assert.False(service.CanCreate(
            soldier,
            IndividualPostingPurpose.Mechanicus,
            CampaignLocation.Landed(fixture.Planet.Regions[0]),
            out string reason));
        Assert.Contains("outside the sector", reason);
    }

    [Fact]
    public void MechanicusPosting_KeepsMembership_RecordsReturnDate_AndLeavesSoldierUnreachable()
    {
        var fixture = SectorSimulationFixture.Create();
        PlayerSoldier adept = Player("Brother Adept");
        PlayerSoldier brother = Player("Brother");
        Squad squad = AdministrativeSquad(adept, brother);
        squad.CurrentRegion = fixture.Planet.Regions[0];
        IndividualPostingService service = Postings();

        service.Create(
            adept, IndividualPostingPurpose.Mechanicus, CampaignLocation.OffSector, Departure, Return);

        Assert.Contains(adept, squad.Members);
        Assert.Equal(1, SoldierPresenceService.PresentCount(squad));
        Assert.True(adept.IsOffSector);
        Assert.Equal(Return, adept.IndividualPosting.ExpectedReturnDate);
        Assert.False(CampaignLocationService.AreCoLocated(adept, squad));
        Assert.Equal("Off-sector", CampaignLocationService.Format(CampaignLocationService.ForSoldier(adept)));
        Assert.False(service.CanRejoin(adept, out _));
        Assert.Throws<InvalidOperationException>(() =>
            service.Move(adept, CampaignLocation.Landed(fixture.Planet.Regions[0])));
    }

    [Fact]
    public void RestorePhysical_AcceptsAnOffSectorMechanicusPosting()
    {
        PlayerSoldier adept = Player("Brother Adept");
        AdministrativeSquad(adept);

        Postings().RestorePhysical(
            adept, IndividualPostingPurpose.Mechanicus, CampaignLocation.OffSector, Departure, Return);

        Assert.True(adept.IsOffSector);
        Assert.Equal(IndividualPostingPurpose.Mechanicus, adept.IndividualPosting.Purpose);
        Assert.Equal(Departure, adept.IndividualPosting.StartedDate);
        Assert.Equal(Return, adept.IndividualPosting.ExpectedReturnDate);
    }

    [Fact]
    public void DutyReadiness_RejectsAFitBrotherOnMars()
    {
        PlayerSoldier adept = PostedToMars(out _);

        Assert.True(adept.IsCombatEffective);
        DutyReadinessEvaluation duty = DutyReadinessService.Evaluate(adept);
        Assert.False(duty.IsDutyReady);
        Assert.Equal(DutyReadinessReasonCode.OffSector, duty.ReasonCode);
        Assert.True(adept.ToDutyReadinessFacts().IsOffSector);
        Assert.Equal(
            DutyReadinessReasonCode.OffSector,
            new MedicalReadinessDecisions().EvaluateSoldier(adept).ReasonCode);
    }

    [Fact]
    public void CharacterAvailability_RejectsABrotherOnMarsForOrdersMovementAndTransfer()
    {
        var fixture = SectorSimulationFixture.Create();
        PlayerSoldier adept = PostedToMars(out _);
        CharacterAvailabilityService availability = new();

        Assert.Equal(
            CharacterAvailabilityReasonCode.OffSector,
            availability.EvaluateOrderAssignment(adept, null).ReasonCode);
        Assert.Equal(
            CharacterAvailabilityReasonCode.OffSector,
            availability.EvaluateMovement(
                adept, CampaignLocation.Landed(fixture.Planet.Regions[0])).ReasonCode);
        Assert.Equal(
            CharacterAvailabilityReasonCode.OffSector,
            availability.EvaluateOrganizationalTransfer(adept).ReasonCode);
    }

    [Fact]
    public void OperationsPersonnel_RejectsABrotherOnMarsForOrderAssignment()
    {
        PlayerSoldier adept = PostedToMars(out _);

        PersonnelAvailabilityDecision decision = TestPersonnelComposition.CreatePersonnel()
            .EvaluateOrderAssignment(PersonnelAvailabilityProjection.ForOrderAssignment(adept, null));

        Assert.False(decision.IsAllowed);
        Assert.Equal((int)PersonnelAvailabilityReasonCode.OffSector, decision.ReasonCode);
    }

    [Fact]
    public void Transfer_OffersNoOpeningToABrotherOnMars()
    {
        Unit chapter = new(1, "Chapter", new UnitTemplate(1, "Chapter Template", true, [], []), []);
        SquadTemplate lineTemplate = new(
            1,
            "Line Squad",
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            [
                new SquadTemplateElement(TestModelFactory.SergeantTemplate, 0, 1),
                new SquadTemplateElement(TestModelFactory.MarineTemplate, 0, 4)
            ],
            SquadTypes.None);
        Squad source = new("Source Squad", chapter, lineTemplate);
        chapter.AddSquad(source);
        Squad target = new("Target Squad", chapter, lineTemplate);
        chapter.AddSquad(target);
        target.AddSquadMember(Player("Sergeant Titus", TestModelFactory.SergeantTemplate));
        PlayerSoldier brother = Player("Brother Marius");
        source.AddSquadMember(brother);
        SoldierTransferService transfers = new();
        Assert.NotEmpty(transfers.GetTransferOptions(chapter, brother));

        Postings().Create(
            brother, IndividualPostingPurpose.Mechanicus, CampaignLocation.OffSector, Departure, Return);

        Assert.Empty(transfers.GetTransferOptions(chapter, brother));
        Assert.False(transfers.HasLegalTransferOption(
            transfers.CreateContext(chapter), brother, promotionOnly: false));
    }

    [Fact]
    public void FindMovableStaff_NeverSelectsAFitTechmarineOnMars()
    {
        SoldierTemplate techmarine = new(
            90, TestModelFactory.HumanSpecies, "Techmarine", 5, 1, false, 5, []);
        PlayerSoldier adept = Player("Brother Adept", techmarine);
        Squad armory = AdministrativeSquad(adept);
        Unit chapter = new(1, "Chapter", new UnitTemplate(1, "Chapter Template", true, [], []), []);
        chapter.AddSquad(armory);
        PlayerForce force = new(
            null, new Army("Test Army", null, "Test Chapter", chapter, [adept]), null);
        Assert.Same(adept, RecoveryPlanService.FindMovableStaff(force, MedicalProcedureService.IsTechmarine));

        Postings().Create(
            adept, IndividualPostingPurpose.Mechanicus, CampaignLocation.OffSector, Departure, Return);

        Assert.True(adept.IsCombatEffective);
        Assert.Null(RecoveryPlanService.FindMovableStaff(force, MedicalProcedureService.IsTechmarine));
    }

    private static PlayerSoldier PostedToMars(out Squad squad)
    {
        PlayerSoldier adept = Player("Brother Adept");
        squad = AdministrativeSquad(adept);
        Postings().Create(
            adept, IndividualPostingPurpose.Mechanicus, CampaignLocation.OffSector, Departure, Return);
        return adept;
    }

    private static IndividualPostingService Postings() => new(new OrderCommitmentSurface());

    private static PlayerSoldier Player(string name, SoldierTemplate template = null) =>
        new(TestModelFactory.CreateSoldier(template, name), name);

    private static Squad AdministrativeSquad(params PlayerSoldier[] soldiers)
    {
        SquadTemplate template = new(
            1,
            "Armory",
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            [new SquadTemplateElement(TestModelFactory.MarineTemplate, 0, 10)],
            SquadTypes.Administrative,
            FormationMobilityPolicy.MembersOnly);
        Squad squad = new("Armory", null, template);
        foreach (PlayerSoldier soldier in soldiers) squad.AddSquadMember(soldier);
        return squad;
    }
}
