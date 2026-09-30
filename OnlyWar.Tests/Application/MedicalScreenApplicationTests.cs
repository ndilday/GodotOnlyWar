using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OnlyWar.Application;
using OnlyWar.Domain;
using OnlyWar.Campaign.Simulation;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Application;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public sealed class MedicalScreenApplicationTests
{
    [Fact]
    public void ReplacingSessionRejectsOldRecoveryCommandEvenWhenPatientIdsMatch()
    {
        var application = CreateApplication(out var first);
        var view = application.QueryRecovery(new(first.Id));
        var command = new ConfirmRecoveryCommand(view.SessionToken, first.Id, null,
            RecoveryMovementChoice.DetachCasualty, view.Model.SelectedTreatment.HitLocationId,
            view.Model.SelectedTreatment.Type);
        int changes = 0;
        application.SessionChanged += (_, _) => changes++;
        var second = CreateSession(application.ActiveSession.Rules, out var secondPatient);
        application.Install(second);

        var result = application.ConfirmRecovery(command);

        Assert.False(result.Succeeded);
        Assert.Contains("campaign changed", result.Message);
        Assert.Equal(1, changes);
        Assert.NotEqual(view.SessionToken, application.SessionToken);
        Assert.Null(first.IndividualPosting);
        Assert.Null(secondPatient.IndividualPosting);
        Assert.Empty(second.Sector.PlayerForce.Army.MedicalProcedures);
    }

    [Fact]
    public void QueriesReturnDetachedValuesAndRequeryCurrentCampaign()
    {
        var application = CreateApplication(out var patient);
        var before = application.QueryMedical(new(ApothecariumSelectionKind.Soldier, patient.Id));
        patient.AssignedSquad.Name = "Renamed";
        var after = application.QueryMedical(new(ApothecariumSelectionKind.Soldier, patient.Id));
        Assert.DoesNotContain("Renamed", before.Soldier.Assignment);
        Assert.Contains("Renamed", after.Soldier.Assignment);
        application.Close();
        Assert.Empty(application.QueryMedical(new()).Tree);
        Assert.Null(application.QueryRecovery(new(patient.Id)).Model.Patient);
    }

    [Fact]
    public void InvalidTreatmentDoesNotSubstituteAnotherOptionOrMutatePatient()
    {
        var application = CreateApplication(out var patient);
        var result = application.ConfirmRecovery(new(application.SessionToken, patient.Id,
            null, RecoveryMovementChoice.DetachCasualty, -1, MedicalProcedureChoice.Cybernetic));
        Assert.False(result.Succeeded);
        Assert.Null(patient.IndividualPosting);
        Assert.Empty(application.ActiveSession.Sector.PlayerForce.Army.MedicalProcedures);
    }

    [Theory]
    [InlineData(typeof(MedicalScreenView))]
    [InlineData(typeof(RecoveryScreenView))]
    [InlineData(typeof(ConfirmRecoveryCommand))]
    public void MedicalBoundaryContainsNoLiveDomainGraph(Type root)
    {
        Assert.Empty(FindDomainGraph(root));
    }

    [Fact]
    public void BoundaryAuditDetectsNestedLiveGraphInNegativeFixture()
    {
        Assert.Contains(typeof(PlayerSoldier), FindDomainGraph(typeof(UnsafeView)));
    }

    private sealed record UnsafeView(IReadOnlyList<PlayerSoldier> Patients);

    [Fact]
    public void MedicalControllerUsesOnlyTheApplicationBoundary()
    {
        string controller = System.IO.File.ReadAllText(System.IO.Path.Combine(
            RulesDatabaseFixture.RepositoryRoot, "Scenes", "ApothecariumScreen", "ApothecariumScreenController.cs"));
        foreach (string bypass in new[] { "GameDataSingleton", "PlayerForce", "PlayerSoldier", "CampaignLocation",
            "MedicalProcedureService", "RecoveryPlanService", "MedicalRecordBuilder", "ViewModelBuilder" })
            Assert.DoesNotContain(bypass, controller);
        Assert.Contains("IMedicalScreenApplication", controller);
        Assert.Contains("SessionChanged -=", controller);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void RecoveryValidatesPatientAndStaffTogetherBeforeMovingAnyone(int capacity, bool succeeds)
    {
        var application = CreateApplication(out var patient);
        var oldForce = application.ActiveSession.Sector.PlayerForce;
        var chapter = oldForce.Army.OrderOfBattle;
        var staffTemplate = new SquadTemplate(600, "Staff", null, [], null, [],
            SquadTypes.Administrative, FormationMobilityPolicy.MembersOnly);
        var staffSquad = new Squad("Staff", chapter, staffTemplate);
        chapter.AddSquad(staffSquad);
        List<PlayerSoldier> roster = [patient];
        foreach (var role in new[] { "Apothecary", "Techmarine" })
        {
            var template = new SoldierTemplate(600 + roster.Count, TestModelFactory.HumanSpecies,
                role, 1, 1, false, 1, Array.Empty<(BaseSkill, float)>());
            var source = TestModelFactory.CreateSoldier(template: template);
            source.Id = 600 + roster.Count;
            var staff = new PlayerSoldier(source, role);
            staffSquad.AddSquadMember(staff);
            roster.Add(staff);
        }
        var ship = new Ship(700, "Hospital", new ShipTemplate(700, "Hospital", (ushort)capacity, 0, 0));
        var fleet = new Fleet("Fleet", null, null);
        fleet.TaskForces.Add(new TaskForce(700, null, null, null, null, [ship]));
        var force = new PlayerForce(null, new Army("Army", null, "Commander", chapter, roster)
            { Requisition = 1000 }, fleet);
        application.Install(new GameSession(application.ActiveSession.Rules,
            new Sector(force, [], [], fleet.TaskForces), new Date(20_000), new SeededRNG(9)));
        var recovery = application.QueryRecovery(new(patient.Id));
        var option = recovery.Model.SelectedTreatment;
        var command = new ConfirmRecoveryCommand(application.SessionToken, patient.Id,
            new MedicalLocationId(MedicalLocationKind.Ship, ship.Id),
            RecoveryMovementChoice.DetachCasualty, option.HitLocationId, option.Type);

        var result = application.ConfirmRecovery(command);

        Assert.Equal(succeeds, result.Succeeded);
        if (succeeds)
        {
            Assert.Equal(3, ship.IndividuallyBoardedSoldiers.Count);
            Assert.Single(force.Army.MedicalProcedures);
            Assert.Equal(1000 - option.RequisitionCost, force.Army.Requisition);
            Assert.False(application.ConfirmRecovery(command).Succeeded);
            Assert.Single(force.Army.MedicalProcedures);
        }
        else
        {
            Assert.All(roster, member => Assert.Null(member.IndividualPosting));
            Assert.Empty(ship.IndividuallyBoardedSoldiers);
            Assert.Empty(force.Army.MedicalProcedures);
            Assert.Equal(1000, force.Army.Requisition);
        }
    }

    // Mars pipeline (TDD §6.14): under the Mechanicus loan the planner
    // needs no Techmarine, so a chapter with none can still confirm a plan, and moves only the
    // Apothecary. Without the loan the same plan fails for want of a Techmarine.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RecoveryUnderTheMechanicusLoanMovesNoTechmarine(bool loanActive)
    {
        var application = CreateApplication(out var patient);
        var chapter = application.ActiveSession.Sector.PlayerForce.Army.OrderOfBattle;
        var staffTemplate = new SquadTemplate(600, "Staff", null, [], null, [],
            SquadTypes.Administrative, FormationMobilityPolicy.MembersOnly);
        var staffSquad = new Squad("Staff", chapter, staffTemplate);
        chapter.AddSquad(staffSquad);
        var apothecaryTemplate = new SoldierTemplate(601, TestModelFactory.HumanSpecies,
            "Apothecary", 1, 1, false, 1, Array.Empty<(BaseSkill, float)>());
        var source = TestModelFactory.CreateSoldier(template: apothecaryTemplate);
        source.Id = 601;
        var apothecary = new PlayerSoldier(source, "Apothecary");
        staffSquad.AddSquadMember(apothecary);
        var ship = new Ship(700, "Hospital", new ShipTemplate(700, "Hospital", 2, 0, 0));
        var fleet = new Fleet("Fleet", null, null);
        fleet.TaskForces.Add(new TaskForce(700, null, null, null, null, [ship]));
        var force = new PlayerForce(null, new Army("Army", null, "Commander", chapter, [patient, apothecary])
            { Requisition = 1000 }, fleet) { IsMechanicusLoanActive = loanActive };
        application.Install(new GameSession(application.ActiveSession.Rules,
            new Sector(force, [], [], fleet.TaskForces), new Date(20_000), new SeededRNG(9)));
        var destination = new MedicalLocationId(MedicalLocationKind.Ship, ship.Id);
        var recovery = application.QueryRecovery(new(patient.Id, Destination: destination));
        var option = recovery.Model.SelectedTreatment;
        var site = recovery.Model.Destinations.Single(view => view.Location == destination);

        var result = application.ConfirmRecovery(new ConfirmRecoveryCommand(application.SessionToken,
            patient.Id, destination, RecoveryMovementChoice.DetachCasualty, option.HitLocationId, option.Type));

        Assert.Equal(loanActive, result.Succeeded);
        if (loanActive)
        {
            Assert.Equal(OnlyWar.Campaign.TechmarineSupport.LoanLabel, site.TechmarineName);
            Assert.DoesNotContain(site.Reasons, reason => reason.Code == "techmarine");
            Assert.Equal(2, ship.IndividuallyBoardedSoldiers.Count);
            Assert.Single(force.Army.MedicalProcedures);
        }
        else
        {
            Assert.Null(site.TechmarineName);
            Assert.Contains(site.Reasons, reason => reason.Code == "techmarine");
            Assert.Empty(ship.IndividuallyBoardedSoldiers);
            Assert.Empty(force.Army.MedicalProcedures);
        }
    }

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

    private static CampaignApplication CreateApplication(out PlayerSoldier patient)
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        var application = TestPersonnelComposition.CreateCampaign(new SeededRNG(7)).CreateApplication();
        application.Install(CreateSession(fixture.Rules, out patient));
        return application;
    }

    private static GameSession CreateSession(GameRulesData rules, out PlayerSoldier patient)
    {
        var soldier = TestModelFactory.CreateSoldier(name: "Patient");
        soldier.Id = 1;
        patient = new PlayerSoldier(soldier, "Patient") { ProgenoidImplantDate = new Date(1) };
        var limb = patient.Body.HitLocations.First(location => location.Template.Name == "Left Arm");
        for (int i = 0; i < 3; i++) limb.Wounds.AddWound(WoundLevel.Critical);
        var chapter = new Unit("Chapter", new UnitTemplate(100, "Chapter", true, [], []));
        var squad = new Squad("Squad", chapter, TestModelFactory.SquadTemplate);
        chapter.AddSquad(squad);
        squad.AddSquadMember(patient);
        var force = new PlayerForce(null, new Army("Army", null, "Commander", chapter, [patient]),
            new Fleet("Fleet", null, null));
        return new GameSession(rules, new Sector(force, [], [], []), new Date(20_000), new SeededRNG(8));
    }
}
