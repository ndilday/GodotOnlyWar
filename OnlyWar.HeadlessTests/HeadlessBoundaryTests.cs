using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using OnlyWar.Builders;
using OnlyWar.Medical.Abstractions;
using OnlyWar.Persistence.Abstractions;
using OnlyWar.Runtime.Abstractions;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Readiness;
using OnlyWar.Helpers.Battles;
using OnlyWar.Application;
using OnlyWar.Models;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Geometry;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.HeadlessTests;

public class HeadlessBoundaryTests
{
    [Fact]
    public void AssembliesHaveOnlyTheirAllowedProductionDependencies()
    {
        AssertReferences(typeof(Coordinate).Assembly, []);
        AssertReferences(typeof(IRNG).Assembly, []);
        AssertReferences(typeof(BattleSquadFactory).Assembly,
             ["OnlyWar.Domain", "OnlyWar.Persistence", "OnlyWar.Campaign", "OnlyWar.Operations",
             "OnlyWar.Generation", "OnlyWar.Battles", "OnlyWar.Medical.Abstractions",
             "OnlyWar.Abstractions", "OnlyWar.Battles.Abstractions", "OnlyWar.Application.Abstractions",
             "OnlyWar.Runtime", "OnlyWar.Runtime.Abstractions", "OnlyWar.Medical",
             "OnlyWar.Generation.Abstractions", "OnlyWar.Operations.Abstractions",
             "OnlyWar.Persistence.Abstractions"]);
        AssertReferences(typeof(CampaignApplication).Assembly,
            ["OnlyWar.Domain", "OnlyWar.Persistence", "OnlyWar.Campaign", "OnlyWar.Operations",
             "OnlyWar.Generation", "OnlyWar.Battles", "OnlyWar.Medical.Abstractions",
             "OnlyWar.Abstractions", "OnlyWar.Battles.Abstractions", "OnlyWar.Application.Abstractions",
             "OnlyWar.Runtime", "OnlyWar.Runtime.Abstractions", "OnlyWar.Medical",
             "OnlyWar.Generation.Abstractions", "OnlyWar.Operations.Abstractions",
             "OnlyWar.Persistence.Abstractions"]);
        AssertReferences(typeof(FactionStrategyController).Assembly,
            ["OnlyWar.Abstractions", "OnlyWar.Domain", "OnlyWar.Medical.Abstractions",
             "OnlyWar.Operations", "OnlyWar.Medical", "OnlyWar.Generation",
             "OnlyWar.Application.Abstractions", "OnlyWar.Runtime", "OnlyWar.Battles",
             "OnlyWar.Battles.Abstractions", "OnlyWar.Generation.Abstractions",
             "OnlyWar.Operations.Abstractions"]);
        // Tactical execution reaches nothing but the shared domain and its boundary contracts: no
        // Engine bridge, no campaign orchestration, no current session (SB-06).
        AssertReferences(typeof(OnlyWar.Helpers.Battles.BattleTurnResolver).Assembly,
            ["OnlyWar.Domain", "OnlyWar.Battles.Abstractions", "OnlyWar.Abstractions",
             "OnlyWar.Runtime"]);
        AssertReferences(typeof(OnlyWar.Medical.Readiness.DutyReadinessPolicy).Assembly,
            ["OnlyWar.Domain", "OnlyWar.Medical.Abstractions", "OnlyWar.Abstractions"]);
        AssertReferences(typeof(OnlyWar.Persistence.Files.AtomicCampaignFileStore).Assembly,
            ["OnlyWar.Domain", "OnlyWar.Persistence.Abstractions"], allowSqlite: true);
        AssertReferences(typeof(OnlyWar.Runtime.Factories.RuntimeSoldierFactory).Assembly,
            ["OnlyWar.Domain", "OnlyWar.Abstractions", "OnlyWar.Runtime.Abstractions"]);
        // Operations owns order/mission sequencing and carries only neutral operational elements;
        // the Application adapter owns the tactical BattleSquad projection (SB-12).
        AssertReferences(typeof(OnlyWar.Models.Missions.MissionContext).Assembly,
            ["OnlyWar.Battles.Abstractions", "OnlyWar.Domain", "OnlyWar.Medical.Abstractions",
             "OnlyWar.Abstractions", "OnlyWar.Runtime", "OnlyWar.Operations.Abstractions"]);
        // Generation constructs initial state over explicit ports; it never references campaign
        // orchestration or turn simulation, which is what keeps the two acyclic (SB-09).
        AssertReferences(typeof(SectorBuilder).Assembly,
            ["OnlyWar.Domain", "OnlyWar.Runtime", "OnlyWar.Abstractions",
             "OnlyWar.Generation.Abstractions"]);
        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(),
            assembly => assembly.GetName().Name is "OnlyWarGodot" or "GodotSharp");
    }

    [Fact]
    public void CampaignDoesNotGrantApplicationFriendAccess()
    {
        Assembly campaign = typeof(FactionStrategyController).Assembly;

        Assert.DoesNotContain(
            campaign.GetCustomAttributes<System.Runtime.CompilerServices.InternalsVisibleToAttribute>(),
            friend => friend.AssemblyName == "OnlyWar.Application");
    }

    [Fact]
    public void InMemoryEquipmentCatalogPreservesTemplateIdentityWithoutStorage()
    {
        EquipmentTemplate item = new(7, "Test equipment", 1);
        EquipmentRulesCatalog catalog = new(
            new Dictionary<int, EquipmentTemplate> { [item.Id] = item },
            new Dictionary<int, AmmunitionType>(),
            new Dictionary<int, EquipmentKitTemplate>(),
            new Dictionary<int, PersonalEquipmentRole>());

        Assert.Same(item, catalog.EquipmentTemplates[7]);
        Assert.Empty(catalog.AmmunitionTypes);
        Assert.DoesNotContain(typeof(EquipmentRulesCatalog).GetMethods(),
            method => method.GetParameters().Any(parameter =>
                parameter.ParameterType.Namespace?.StartsWith("System.Data") == true));
    }

    [Fact]
    public void PersistenceContractsDoNotExposeStorageProviderTypes()
    {
        Assembly persistence = typeof(IAtomicCampaignFileStore).Assembly;
        IEnumerable<Type> publicSurface = persistence.GetExportedTypes()
            .Where(type => type.Namespace?.StartsWith(
                "OnlyWar.Persistence.Abstractions", StringComparison.Ordinal) == true)
            .SelectMany(type => new[] { type }
                .Concat(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .SelectMany(method => new[] { method.ReturnType }
                        .Concat(method.GetParameters().Select(parameter => parameter.ParameterType))))
                .Concat(type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .Select(property => property.PropertyType)))
            .SelectMany(UnwrapTypes)
            .Distinct();

        Assert.DoesNotContain(publicSurface, type =>
            type.Namespace?.StartsWith("System.Data", StringComparison.Ordinal) == true
            || type.Namespace?.StartsWith("Microsoft.Data.Sqlite", StringComparison.Ordinal) == true
            || type.Name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CatalogRejectsInvalidInMemoryDataWithoutAttemptingFileAccess()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new GameRulesData(new GameRulesBlob()));
        Assert.Contains("exactly one default sector generation profile", error.Message);
        Assert.Single(typeof(GameRulesData).GetConstructors());
        Assert.Equal(typeof(GameRulesBlob), typeof(GameRulesData).GetConstructors()[0].GetParameters()[0].ParameterType);
    }

    [Fact]
    public void MedicalPoliciesConsumeExplicitFactsAndOwnTransitions()
    {
        DutyReadinessEvaluation evaluation = OnlyWar.Medical.Readiness.DutyReadinessPolicy.Evaluate(
            new DutyReadinessFacts(
                "Brother Test",
                IsCombatEffective: true,
                HasUntreatedSeveredLimb: false,
                IsProcedureReserved: true,
                FunctioningHands: 2,
                WorstWoundLevel: WoundLevel.None));

        Assert.False(evaluation.IsDutyReady);
        Assert.Equal(DutyReadinessReasonCode.ProcedureReservation, evaluation.ReasonCode);

        SquadReadinessSnapshot squad = OnlyWar.Medical.Readiness.SquadReadinessPolicy.Evaluate(
            new SquadReadinessFacts(
                Establishment: 1,
                IsAdministrative: false,
                RequiresLeader: true,
                Members:
                [
                    new SquadMemberReadinessFacts(
                        Id: 1,
                        IsPresent: true,
                        IsCombatEffective: true,
                        IsDutyReady: true,
                        IsProcedureReserved: false,
                        IsIndividuallyPosted: false,
                        IsLeader: false,
                        IsLeaderDutyReady: false,
                        DutyReasonCode: DutyReadinessReasonCode.Ready)
                ],
                Commitment: SquadCommitmentKind.Free));

        Assert.Equal(SquadLeaderStatus.Vacant, squad.LeaderStatus);
        Assert.Contains(SquadReadinessBlocker.Leaderless, squad.StructuralBlockers);
    }

    [Fact]
    public void RuntimeConstructionAndPersistenceUseExplicitBoundaries()
    {
        var random = new SeededRNG(seed: 17);
        var allocator = new OnlyWar.Runtime.Allocators.SequentialEntityIdAllocator(firstId: 100);
        RuntimeSoldier runtimeSoldier = new OnlyWar.Runtime.Factories.RuntimeSoldierFactory()
            .Create(TestModelFactory.MarineTemplate, random, allocator);

        Assert.Equal(100, runtimeSoldier.Id);
        Assert.Equal(TestModelFactory.MarineTemplate.Species.BodyTemplate.HitLocations.Length,
            runtimeSoldier.Body.HitLocations.Length);

        var names = new OnlyWar.Runtime.Naming.NameGenerator();
        names.Reset(new SeededRNG(seed: 18));
        Assert.NotNull(names.GetFullName(new SeededRNG(seed: 19)));
        Assert.True(names.GivenNameCount > 0);
        Assert.True(names.SurnameCount > 0);

        string path = Path.Combine(Path.GetTempPath(), $"onlywar-phase3-{Guid.NewGuid():N}.bin");
        try
        {
            IAtomicCampaignFileStore store = new OnlyWar.Persistence.Files.AtomicCampaignFileStore();
            store.Write(path, new byte[] { 1, 2, 3, 5, 8 });
            Assert.Equal(new byte[] { 1, 2, 3, 5, 8 }, store.Read(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void FieldCarePolicyOwnsWoundDemotionAndReturnsDetachedResult()
    {
        Body body = new(HumanBodyTemplate.Instance);
        HitLocation location = body.HitLocations.First(item => item.Template.Id == 4);
        location.Wounds.AddWound(WoundLevel.Moderate);
        uint before = location.Wounds.WoundTotal;

        IReadOnlyList<FieldCareTreatmentResult> treatments =
            OnlyWar.Medical.Treatment.FieldCarePolicy.ApplyDailyCare(
                [new FieldCareProviderFacts(1, "Apothecary", 1f)],
                [new FieldCarePatientFacts(2, "Patient", 1, 0, body)],
                new SeededRNG(seed: 23),
                day: 2);

        FieldCareTreatmentResult treatment = Assert.Single(treatments);
        Assert.Equal(2, treatment.Day);
        Assert.Equal(WoundLevel.Moderate, treatment.FromBand);
        Assert.Equal("Left Arm", treatment.LocationName);
        Assert.True(location.Wounds.WoundTotal < before);
    }

    [Fact]
    public void GeometryKeepsCellOrderAndSignedCoordinatesWithoutGodot()
    {
        Planet planet = new(1, "Test world", new Coordinate(10, 10), 1, null, 1, 1);
        Subsector subsector = Assert.Single(SubsectorBuilder.BuildSubsectors([planet], new GridCell(30, 30), 20));
        Assert.Contains(new GridCell(10, 10), subsector.Cells);
        Assert.Equal(subsector.Cells.OrderBy(cell => cell.Y).ThenBy(cell => cell.X), subsector.Cells);
        Assert.Equal(new PlanePoint(-0.5f, 1.5f), new PlanePoint(-1, 1).Lerp(new PlanePoint(1, 3), 0.25f));
    }

    [Fact]
    public void ExplicitReservationAgreesAtSquadAndBattleBoundaries()
    {
        SquadTemplate template = new(10, "Test formation", TestModelFactory.DefaultWeapons, [],
            TestModelFactory.TestArmor, [new(TestModelFactory.MarineTemplate, 0, 5)], SquadTypes.None);
        Squad squad = new("Test formation", null, template);
        PlayerSoldier soldier = new(TestModelFactory.CreateSoldier(), "Test brother");
        squad.AddSquadMember(soldier);
        ChapterOperationalDoctrine doctrine = new(null, false, 1);
        RecruitmentProgram reservations = new();
        reservations.Procedures.Add(new RecruitmentProcedure
        {
            Type = RecruitmentProcedureType.BlackCarapace,
            SubjectId = soldier.Id
        });

        Assert.Equal(DutyReadinessReasonCode.ProcedureReservation,
            DutyReadinessService.Evaluate(soldier, doctrine, reservations).ReasonCode);
        Assert.Equal(0, SquadReadinessService.Evaluate(squad, program: reservations, doctrine: doctrine).Strength.DutyReady);
        Assert.Empty(BattleSquadFactory.GetParticipants(squad, doctrine, reservations));
        Assert.Single(BattleSquadFactory.GetParticipants(squad, doctrine, new RecruitmentProgram()));
        Assert.True(DutyReadinessService.Evaluate(soldier, doctrine, null).IsDutyReady);
    }

    private static void AssertReferences(Assembly assembly, string[] allowed, bool allowSqlite = false)
    {
        foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
        {
            Assert.False(reference.Name.StartsWith("Godot"));
            if (reference.Name.StartsWith("OnlyWar")) Assert.Contains(reference.Name, allowed);
            if (!allowSqlite)
                Assert.DoesNotContain("Sqlite", reference.Name, StringComparison.OrdinalIgnoreCase);
        }
        Assert.All(assembly.GetCustomAttributes<System.Runtime.CompilerServices.InternalsVisibleToAttribute>(),
            friend => Assert.Contains(friend.AssemblyName,
                new[] { "OnlyWar.Tests", "OnlyWar.HeadlessTests" }));
    }

    private static IEnumerable<Type> UnwrapTypes(Type type)
    {
        if (type.IsByRef || type.IsPointer || type.IsArray)
        {
            foreach (Type nested in UnwrapTypes(type.GetElementType())) yield return nested;
            yield break;
        }
        if (type.IsGenericType)
        {
            yield return type.GetGenericTypeDefinition();
            foreach (Type argument in type.GetGenericArguments())
            {
                foreach (Type nested in UnwrapTypes(argument)) yield return nested;
            }
            yield break;
        }
        yield return type;
    }
}
