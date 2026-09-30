using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OnlyWar.Domain;
using OnlyWar.Domain.Extensions;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class MedicalProcedureServiceTests
{
    private static readonly SoldierTemplate ApothecaryTemplate =
        new(10, TestModelFactory.HumanSpecies, "Apothecary", 1, 1, false, 0, Array.Empty<ValueTuple<BaseSkill, float>>());
    private static readonly SoldierTemplate TechmarineTemplate =
        new(11, TestModelFactory.HumanSpecies, "Techmarine", 1, 1, false, 0, Array.Empty<ValueTuple<BaseSkill, float>>());

    private static ReplacementOption CyberneticLeftArm(int cost = 40) =>
        new(4, MedicalProcedureType.Cybernetic, "Left Arm", "Cybernetic Left Arm", "desc", 4, cost, true);

    [Fact]
    public void SeveredReplacementDurations_UseFourWeeksCyberneticAndSixVatGrown()
    {
        Assert.Equal(4, MedicalProcedureRules.GetWeeks(MedicalProcedureType.Cybernetic, isSevered: true));
        Assert.Equal(6, MedicalProcedureRules.GetWeeks(MedicalProcedureType.VatGrown, isSevered: true));
    }

    [Fact]
    public void EvaluateRequisites_AllMet_WhenStaffCoLocatedSiteValidAndAffordable()
    {
        (PlayerForce force, PlayerSoldier wounded) = BuildScenario(
            apothecaryPresent: true, techmarinePresent: true, requisition: 100, developedWorld: true);
        MedicalProcedureService service = new();

        IReadOnlyList<ProcedureRequisite> requisites =
            service.EvaluateRequisites(force, wounded, CyberneticLeftArm());

        Assert.All(requisites, r => Assert.True(r.IsMet, $"requisite not met: {r.Label}"));
        Assert.True(service.CanAssign(force, wounded, CyberneticLeftArm()));
    }

    [Fact]
    public void TryAssign_Succeeds_DeductsRequisitionAndCreatesProcedure()
    {
        (PlayerForce force, PlayerSoldier wounded) = BuildScenario(
            apothecaryPresent: true, techmarinePresent: true, requisition: 100, developedWorld: true);
        MedicalProcedureService service = new();

        bool assigned = service.TryAssign(force, wounded, CyberneticLeftArm(40));

        Assert.True(assigned);
        Assert.Equal(60, force.Army.Requisition);
        MedicalProcedure procedure = Assert.Single(force.Army.MedicalProcedures);
        Assert.Equal(wounded.Id, procedure.SoldierId);
        Assert.Equal(4, procedure.HitLocationTemplateId);
        Assert.Equal(MedicalProcedureType.Cybernetic, procedure.ProcedureType);
        Assert.Equal(4, procedure.WeeksRemaining);
        Assert.Equal(40, procedure.RequisitionCost);
        Assert.True(wounded.IsUndergoingMedicalProcedure);
        Assert.False(wounded.IsDeployable);
    }

    [Fact]
    public void TryAssign_Fails_WhenInsufficientRequisition()
    {
        (PlayerForce force, PlayerSoldier wounded) = BuildScenario(
            apothecaryPresent: true, techmarinePresent: true, requisition: 10, developedWorld: true);
        MedicalProcedureService service = new();

        bool assigned = service.TryAssign(force, wounded, CyberneticLeftArm(40));

        Assert.False(assigned);
        Assert.Equal(10, force.Army.Requisition);
        Assert.Empty(force.Army.MedicalProcedures);
    }

    [Fact]
    public void EvaluateRequisites_MarksApothecaryUnmet_WhenNoneCoLocated()
    {
        (PlayerForce force, PlayerSoldier wounded) = BuildScenario(
            apothecaryPresent: false, techmarinePresent: true, requisition: 100, developedWorld: true);
        MedicalProcedureService service = new();

        IReadOnlyList<ProcedureRequisite> requisites =
            service.EvaluateRequisites(force, wounded, CyberneticLeftArm());

        Assert.False(requisites.First(r => r.Label.StartsWith("Apothecary")).IsMet);
        Assert.True(requisites.First(r => r.Label.StartsWith("Techmarine")).IsMet);
        Assert.False(service.CanAssign(force, wounded, CyberneticLeftArm()));
    }

    [Fact]
    public void EvaluateRequisites_MarksSiteUnmet_OnUndevelopedWorld()
    {
        (PlayerForce force, PlayerSoldier wounded) = BuildScenario(
            apothecaryPresent: true, techmarinePresent: true, requisition: 100, developedWorld: false);
        MedicalProcedureService service = new();

        IReadOnlyList<ProcedureRequisite> requisites =
            service.EvaluateRequisites(force, wounded, CyberneticLeftArm());

        Assert.False(requisites.First(r => r.Label == "Valid surgery site").IsMet);
    }

    [Fact]
    public void EvaluateRequisites_RejectsAnApothecariumProcedureForACyberneticLocation()
    {
        (PlayerForce force, PlayerSoldier wounded) = BuildScenario(
            apothecaryPresent: true, techmarinePresent: true, requisition: 100, developedWorld: true);
        HitLocation cyberneticArm = wounded.Body.HitLocations.First(location => location.Template.Id == 4);
        cyberneticArm.Wounds.HealWounds();
        cyberneticArm.Wounds.AddWound(WoundLevel.Critical);
        cyberneticArm.IsCybernetic = true;
        MedicalProcedureService service = new();

        IReadOnlyList<ProcedureRequisite> requisites =
            service.EvaluateRequisites(force, wounded, CyberneticLeftArm());

        Assert.False(requisites.First(r => r.Label == "Organic hit location").IsMet);
        Assert.False(service.CanAssign(force, wounded, CyberneticLeftArm()));
    }

    // Mars pipeline (TDD §6.14): the Mechanicus loan stands in for
    // the Techmarine while the founding cohort trains on Mars, and only while the loan runs.
    [Fact]
    public void EvaluateRequisites_MechanicusLoan_MeetsTheTechmarineLineWithNoTechmarine_AndEndsWithTheLoan()
    {
        (PlayerForce force, PlayerSoldier wounded) = BuildScenario(
            apothecaryPresent: true, techmarinePresent: false, requisition: 100, developedWorld: true);
        force.IsMechanicusLoanActive = true;
        MedicalProcedureService service = new();

        IReadOnlyList<ProcedureRequisite> onLoan =
            service.EvaluateRequisites(force, wounded, CyberneticLeftArm());

        Assert.True(Assert.Single(onLoan, r => r.Label == TechmarineSupport.LoanLabel).IsMet);
        Assert.DoesNotContain(onLoan, r => r.Label == TechmarineSupport.CoLocatedLabel);
        Assert.True(service.CanAssign(force, wounded, CyberneticLeftArm()));

        force.IsMechanicusLoanActive = false;
        IReadOnlyList<ProcedureRequisite> afterLoan =
            service.EvaluateRequisites(force, wounded, CyberneticLeftArm());

        Assert.False(Assert.Single(afterLoan, r => r.Label == TechmarineSupport.CoLocatedLabel).IsMet);
        Assert.DoesNotContain(afterLoan, r => r.Label == TechmarineSupport.LoanLabel);
        Assert.False(service.CanAssign(force, wounded, CyberneticLeftArm()));
    }

    [Fact]
    public void EvaluateRequisites_MechanicusLoan_DoesNotWaiveTheOtherRequisites()
    {
        (PlayerForce force, PlayerSoldier wounded) = BuildScenario(
            apothecaryPresent: false, techmarinePresent: false, requisition: 10, developedWorld: false);
        force.IsMechanicusLoanActive = true;
        MedicalProcedureService service = new();

        IReadOnlyList<ProcedureRequisite> requisites =
            service.EvaluateRequisites(force, wounded, CyberneticLeftArm(40));

        Assert.True(requisites.Single(r => r.Label == TechmarineSupport.LoanLabel).IsMet);
        Assert.False(requisites.Single(r => r.Label.StartsWith("Apothecary")).IsMet);
        Assert.False(requisites.Single(r => r.Label == "Valid surgery site").IsMet);
        Assert.False(requisites.Single(r => r.Label.StartsWith("Requisition")).IsMet);
    }

    [Fact]
    public void CareDestination_MechanicusLoan_RemovesTheTechmarineReason_AndOnlyWhileItRuns()
    {
        (PlayerForce force, PlayerSoldier wounded) = BuildScenario(
            apothecaryPresent: true, techmarinePresent: false, requisition: 100, developedWorld: true);
        CampaignLocation site = CampaignLocationService.ForSoldier(wounded);
        CareDestinationService destinations = new();

        CareDestinationCandidate withoutLoan =
            destinations.Evaluate(force, wounded, CyberneticLeftArm(), site);
        Assert.Contains(withoutLoan.Reasons, reason => reason.Code == "techmarine");
        Assert.False(withoutLoan.IsTechmarineSupportOnLoan);
        Assert.False(TechmarineSupport.IsAvailableAt(force, site));

        force.IsMechanicusLoanActive = true;
        CareDestinationCandidate onLoan =
            destinations.Evaluate(force, wounded, CyberneticLeftArm(), site);

        Assert.DoesNotContain(onLoan.Reasons, reason => reason.Code == "techmarine");
        Assert.True(onLoan.IsTechmarineSupportOnLoan);
        Assert.Null(onLoan.Techmarine);
        Assert.Equal(CareDestinationState.Ready, onLoan.State);
        Assert.True(TechmarineSupport.IsAvailableAt(force, site));
    }

    [Fact]
    public void TechmarineSupport_FindsACoLocatedTechmarineWithoutTheLoan()
    {
        (PlayerForce force, PlayerSoldier wounded) = BuildScenario(
            apothecaryPresent: true, techmarinePresent: true, requisition: 100, developedWorld: true);
        CampaignLocation site = CampaignLocationService.ForSoldier(wounded);

        Assert.False(TechmarineSupport.IsOnLoan(force));
        Assert.Equal("Brother Forge", TechmarineSupport.FindTechmarineAt(force, site)?.Name);
        Assert.True(TechmarineSupport.IsAvailableAt(force, site));
        Assert.False(TechmarineSupport.IsAvailableAt(force, CampaignLocation.OffSector));
    }

    private static (PlayerForce, PlayerSoldier) BuildScenario(
        bool apothecaryPresent, bool techmarinePresent, int requisition, bool developedWorld)
    {
        Faction player = BuildPlayerFaction();
        Region region = BuildImperialRegion(player, developedWorld);

        UnitTemplate chapterTemplate = new(100, "Chapter", true, new List<SquadTemplate>(), new List<UnitTemplate>());
        UnitTemplate companyTemplate = new(101, "Company", false, new List<SquadTemplate>(), new List<UnitTemplate>());
        Unit chapter = new("Test Chapter", chapterTemplate);
        Unit company = new("1st Company", companyTemplate) { ParentUnit = chapter };
        chapter.ChildUnits.Add(company);
        Squad squad = new("Test Squad", company, TestModelFactory.SquadTemplate) { CurrentRegion = region };
        company.AddSquad(squad);

        List<PlayerSoldier> soldiers = [];
        PlayerSoldier wounded = MakeSoldier(1, "Wounded", TestModelFactory.MarineTemplate);
        wounded.Body.HitLocations.First(hl => hl.Template.Name == "Left Arm").Wounds.AddWound(WoundLevel.Critical);
        wounded.Body.HitLocations.First(hl => hl.Template.Name == "Left Arm").Wounds.AddWound(WoundLevel.Critical);
        wounded.Body.HitLocations.First(hl => hl.Template.Name == "Left Arm").Wounds.AddWound(WoundLevel.Critical);
        squad.AddSquadMember(wounded);
        soldiers.Add(wounded);

        if (apothecaryPresent)
        {
            PlayerSoldier apothecary = MakeSoldier(2, "Brother Medic", ApothecaryTemplate);
            squad.AddSquadMember(apothecary);
            soldiers.Add(apothecary);
        }
        if (techmarinePresent)
        {
            PlayerSoldier techmarine = MakeSoldier(3, "Brother Forge", TechmarineTemplate);
            squad.AddSquadMember(techmarine);
            soldiers.Add(techmarine);
        }

        Army army = new("Test Army", null, "Commander", chapter, soldiers) { Requisition = requisition };
        PlayerForce force = new(player, army, null);
        return (force, wounded);
    }

    private static PlayerSoldier MakeSoldier(int id, string name, SoldierTemplate template)
    {
        Soldier soldier = TestModelFactory.CreateSoldier(template: template, name: name);
        soldier.Id = id;
        return new PlayerSoldier(soldier, name);
    }

    private static Region BuildImperialRegion(Faction player, bool developedWorld)
    {
        PlanetTemplate template = new(
            1,
            developedWorld ? "Hive" : "Agri",
            1,
            new LogNormalValueTemplate { Floor = 1000, Scale = 0 },
            new LogNormalValueTemplate { Floor = 2000, Scale = 0 },
            new NormalizedValueTemplate { BaseValue = 1, StandardDeviation = 0 },
            new LinearValueTemplate { MinValue = 0, MaxValue = 0 });
        Planet planet = new(1, "Test World", new Coordinate(1, 1), 1, template, 1, 0);
        Region region = new(0, planet, 0, "Region 0",
            RegionExtensions.GetCoordinatesFromRegionNumber(0), 0);
        planet.Regions[0] = region;
        PlanetFaction planetFaction = new(player) { IsPublic = true };
        planet.PlanetFactionMap[player.Id] = planetFaction;
        RegionFaction regionFaction = new(planetFaction, region)
        {
            IsPublic = true,
            Population = OnlyWar.Medical.Treatment.MedicalFacilityPolicy.MinimumImperialPopulation
        };
        region.RegionFactionMap[player.Id] = regionFaction;
        return region;
    }

    private static Faction BuildPlayerFaction()
    {
        return new Faction(
            2, "Test Chapter", Color.Red, true, false, FactionBehavior.None, GrowthType.None,
            new Dictionary<int, Species> { [TestModelFactory.HumanSpecies.Id] = TestModelFactory.HumanSpecies },
            new Dictionary<int, SoldierTemplate>(),
            new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, UnitTemplate>(),
            new Dictionary<int, BoatTemplate>(),
            new Dictionary<int, ShipTemplate>(),
            new Dictionary<int, FleetTemplate>());
    }
}
