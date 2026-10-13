using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Application;
using OnlyWar.Campaign.Medical;
using OnlyWar.Domain;
using OnlyWar.Operations.Abstractions;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;
using OnlyWar.Medical.Abstractions;
using OnlyWar.Medical.Treatment;
using OnlyWar.Operations.Missions;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

/// <summary>
/// Techmarine repair of cybernetic parts (OnlyWar_TDD.md §6.6.1): the policy, the
/// campaign service's two pools and queue, surgery installing the augmetic tier, and the report
/// and Apothecarium projections.
/// </summary>
public class CyberneticRepairTests
{
    private const int LeftArmId = 4;
    private const int TorsoId = 3;

    private static readonly SoldierTemplate TechmarineTemplate = new(
        931, TestModelFactory.HumanSpecies, "Techmarine",
        rank: 4, subrank: 1, isSquadLeader: false, specialistType: 0,
        Array.Empty<ValueTuple<BaseSkill, float>>(), battleValue: 2);

    private static readonly SoldierTemplate BrotherTemplate = new(
        932, TestModelFactory.HumanSpecies, "Battle-Brother",
        rank: 1, subrank: 1, isSquadLeader: false, specialistType: 0,
        Array.Empty<ValueTuple<BaseSkill, float>>(), battleValue: 2);

    private static readonly BaseSkill Cybernetics = new(
        934, SkillCategory.Tech, "Engineering (Cybernetics)",
        OnlyWar.Domain.Soldiers.Attribute.Intelligence, 2f, "engineering_cybernetics");

    // ---- Constants ------------------------------------------------------------------------------

    [Fact]
    public void JobTime_IsOneWorkingDayForAModerateRepairAndFiveForAMortalOne()
    {
        Assert.Equal(8f, CyberneticRepairConstants.GetJobHours(WoundLevel.Moderate));
        Assert.Equal(40f, CyberneticRepairConstants.GetJobHours(WoundLevel.Mortal));
        Assert.Equal(2f, CyberneticRepairConstants.GetJobHours(WoundLevel.Negligible));
    }

    [Fact]
    public void SkillFactor_IsOneAtTheReferenceSkill_ShorterAboveIt_AndClamped()
    {
        Assert.Equal(1f, CyberneticRepairConstants.GetSkillFactor(12f), 4);
        Assert.Equal(0.6561f, CyberneticRepairConstants.GetSkillFactor(16f), 3);
        Assert.Equal(1.8817f, CyberneticRepairConstants.GetSkillFactor(6f), 3);
        Assert.Equal(CyberneticRepairConstants.MinSkillFactor, CyberneticRepairConstants.GetSkillFactor(40f));
        Assert.Equal(CyberneticRepairConstants.MaxSkillFactor, CyberneticRepairConstants.GetSkillFactor(-10f));
    }

    [Fact]
    public void AnEightHourJob_IsWorthFourTrainingHours()
    {
        float trainingHour = 0.2f / 56f;
        float eightHourJob = 8f * CyberneticRepairConstants.ExperiencePerRepairHour;
        Assert.Equal(4f * trainingHour, eightHourJob, 6);
    }

    // ---- Policy ---------------------------------------------------------------------------------

    [Fact]
    public void OnlyDamagedUnseveredCyberneticParts_AreRepairable()
    {
        PlayerSoldier soldier = Brother("Varro");
        HitLocation arm = Arm(soldier);
        HitLocation torso = Torso(soldier);
        torso.Wounds.AddWound(WoundLevel.Moderate);

        Assert.False(CyberneticRepairPolicy.IsRepairable(arm));   // organic would be undamaged anyway
        arm.InstallCybernetic(null);
        Assert.False(CyberneticRepairPolicy.IsRepairable(arm));   // cybernetic but undamaged
        arm.Wounds.AddWound(WoundLevel.Moderate);
        Assert.True(CyberneticRepairPolicy.IsRepairable(arm));
        Assert.False(CyberneticRepairPolicy.IsRepairable(torso)); // damaged but organic

        for (int i = 0; i < 3; i++) arm.Wounds.AddWound(WoundLevel.Critical);
        Assert.True(arm.IsSevered);
        Assert.False(CyberneticRepairPolicy.IsRepairable(arm));   // sever is final
    }

    [Fact]
    public void AFinishedJob_FullyResetsThePart_WhateverItsDamage()
    {
        PlayerSoldier soldier = DamagedAugmetic("Varro", WoundLevel.Major);
        Arm(soldier).Wounds.AddWound(WoundLevel.Minor);
        CyberneticRepairJob job = new(soldier.Id, LeftArmId, 0f, 10);

        IReadOnlyList<CyberneticRepairWork> work = CyberneticRepairPolicy.ApplyDailyRepair(
            [Provider(1, 24f, 1f)], [Facts(job, soldier)]);

        CyberneticRepairWork entry = Assert.Single(work);
        Assert.True(entry.Completed);
        Assert.Equal(16f, entry.HoursWorked, 3); // Major: 2 x 8 hours
        Assert.Equal(0u, Arm(soldier).Wounds.WoundTotal);
        Assert.True(Arm(soldier).IsCybernetic);
    }

    [Fact]
    public void AJob_ContinuesAcrossDays_WithinTheDailyBudget()
    {
        PlayerSoldier soldier = DamagedAugmetic("Varro", WoundLevel.Critical); // 24 hours
        CyberneticRepairJob job = new(soldier.Id, LeftArmId, 0f, 10);

        for (int day = 1; day <= 2; day++)
        {
            IReadOnlyList<CyberneticRepairWork> work = CyberneticRepairPolicy.ApplyDailyRepair(
                [Provider(1, CyberneticRepairConstants.DailyWorkingHours, 1f)],
                [Facts(job, soldier)], day);
            Assert.False(Assert.Single(work).Completed);
            Assert.Equal(8f, work[0].HoursWorked, 3);
        }
        Assert.Equal(16f, job.HoursCompleted, 3);
        Assert.Equal(8f, CyberneticRepairPolicy.GetRemainingHours(job, Arm(soldier)), 3);
        Assert.NotEqual(0u, Arm(soldier).Wounds.WoundTotal);

        Assert.True(Assert.Single(CyberneticRepairPolicy.ApplyDailyRepair(
            [Provider(1, CyberneticRepairConstants.DailyWorkingHours, 1f)],
            [Facts(job, soldier)], 3)).Completed);
        Assert.Equal(0u, Arm(soldier).Wounds.WoundTotal);
    }

    [Fact]
    public void SkillShortensTheJob()
    {
        PlayerSoldier slow = DamagedAugmetic("Slow", WoundLevel.Moderate);
        PlayerSoldier fast = DamagedAugmetic("Fast", WoundLevel.Moderate);

        CyberneticRepairWork reference = Assert.Single(CyberneticRepairPolicy.ApplyDailyRepair(
            [Provider(1, 8f, 1f)], [Facts(new(slow.Id, LeftArmId, 0f, 10), slow)]));
        CyberneticRepairWork skilled = Assert.Single(CyberneticRepairPolicy.ApplyDailyRepair(
            [Provider(2, 8f, CyberneticRepairConstants.GetSkillFactor(16f))],
            [Facts(new(fast.Id, LeftArmId, 0f, 10), fast)]));

        Assert.Equal(8f, reference.HoursWorked, 3);
        Assert.True(skilled.HoursWorked < reference.HoursWorked);
        Assert.Equal(8f * CyberneticRepairConstants.GetSkillFactor(16f), skilled.HoursWorked, 3);
    }

    [Fact]
    public void JobsAreWorkedInQueueOrder_AndLeftoverHoursMoveOnToTheNextJob()
    {
        PlayerSoldier first = DamagedAugmetic("First", WoundLevel.Moderate);  // 8 h
        PlayerSoldier second = DamagedAugmetic("Second", WoundLevel.Critical); // 24 h
        PlayerSoldier third = DamagedAugmetic("Third", WoundLevel.Minor);     // 4 h

        IReadOnlyList<CyberneticRepairWork> work = CyberneticRepairPolicy.ApplyDailyRepair(
            [Provider(1, 8f, 1f), Provider(2, 8f, 1f)],
            [
                Facts(new(first.Id, LeftArmId, 0f, 10), first),
                Facts(new(second.Id, LeftArmId, 0f, 10), second),
                Facts(new(third.Id, LeftArmId, 0f, 10), third)
            ]);

        // No triage: the long second job takes the rest of the day ahead of the short third one.
        Assert.Equal([first.Id, second.Id], work.Select(entry => entry.SoldierId).ToArray());
        Assert.Equal(0u, Arm(first).Wounds.WoundTotal);
        Assert.NotEqual(0u, Arm(second).Wounds.WoundTotal);
        Assert.NotEqual(0u, Arm(third).Wounds.WoundTotal);
        Assert.Equal(16f, work.Sum(entry => entry.HoursWorked), 3);
    }

    [Fact]
    public void ASeveredPart_IsSkipped_AndItsJobIsNotWorked()
    {
        PlayerSoldier severed = DamagedAugmetic("Severed", WoundLevel.Moderate);
        for (int i = 0; i < 3; i++) Arm(severed).Wounds.AddWound(WoundLevel.Critical);
        PlayerSoldier next = DamagedAugmetic("Next", WoundLevel.Moderate);
        CyberneticRepairJob severedJob = new(severed.Id, LeftArmId, 0f, 10);

        IReadOnlyList<CyberneticRepairWork> work = CyberneticRepairPolicy.ApplyDailyRepair(
            [Provider(1, 8f, 1f)],
            [Facts(severedJob, severed), Facts(new(next.Id, LeftArmId, 0f, 10), next)]);

        Assert.Equal(next.Id, Assert.Single(work).SoldierId);
        Assert.Equal(0f, severedJob.HoursCompleted);
        Assert.True(Arm(severed).IsSevered);
    }

    // ---- Service: the queue ---------------------------------------------------------------------

    [Fact]
    public void QueueingARepair_ChargesRequisitionAtOnce_AndOnlyOnce()
    {
        PlayerSoldier patient = DamagedAugmetic("Varro", WoundLevel.Moderate);
        PlayerForce force = ForceOf(25, patient);

        Assert.True(CyberneticRepairService.TryQueueRepair(force, patient, LeftArmId));
        Assert.Equal(15, force.Army.Requisition);
        CyberneticRepairJob job = Assert.Single(force.Army.CyberneticRepairJobs);
        Assert.Equal(CyberneticRepairConstants.RequisitionCost, job.RequisitionCost);

        // A second request for the same part is refused and costs nothing.
        Assert.False(CyberneticRepairService.TryQueueRepair(force, patient, LeftArmId));
        Assert.Equal(15, force.Army.Requisition);
    }

    [Fact]
    public void QueueingIsRefused_ForAnOrganicOrSeveredPart_OrWithoutRequisition()
    {
        PlayerSoldier patient = DamagedAugmetic("Varro", WoundLevel.Moderate);
        Torso(patient).Wounds.AddWound(WoundLevel.Moderate);
        PlayerForce poor = ForceOf(5, patient);
        Assert.False(CyberneticRepairService.TryQueueRepair(poor, patient, LeftArmId));

        PlayerForce rich = ForceOf(100, patient);
        Assert.False(CyberneticRepairService.TryQueueRepair(rich, patient, TorsoId));
        for (int i = 0; i < 3; i++) Arm(patient).Wounds.AddWound(WoundLevel.Critical);
        Assert.False(CyberneticRepairService.TryQueueRepair(rich, patient, LeftArmId));
        Assert.Equal(100, rich.Army.Requisition);
        Assert.Empty(rich.Army.CyberneticRepairJobs);
    }

    [Fact]
    public void PruneJobs_DropsAJobWhosePartWasSevered()
    {
        PlayerSoldier patient = DamagedAugmetic("Varro", WoundLevel.Moderate);
        PlayerForce force = ForceOf(100, patient);
        Assert.True(CyberneticRepairService.TryQueueRepair(force, patient, LeftArmId));
        for (int i = 0; i < 3; i++) Arm(patient).Wounds.AddWound(WoundLevel.Critical);

        CyberneticRepairService.PruneJobs(force);

        Assert.Empty(force.Army.CyberneticRepairJobs);
        Assert.Equal(90, force.Army.Requisition); // no refund
    }

    // ---- Service: garrison and field ------------------------------------------------------------

    [Fact]
    public void Garrison_ACoLocatedTechmarineRepairsTheQueue_AndEarnsCyberneticsExperience()
    {
        PlayerSoldier techmarine = Techmarine("Ferrus");
        PlayerSoldier patient = DamagedAugmetic("Varro", WoundLevel.Major);
        PlaceInRegion(TestRegion(7), techmarine, patient);
        PlayerForce force = ForceOf(100, techmarine, patient);
        Assert.True(CyberneticRepairService.TryQueueRepair(force, patient, LeftArmId));

        IReadOnlyList<CyberneticRepairReport> reports =
            CyberneticRepairService.ApplyGarrisonRepair(force, Cybernetics);

        CyberneticRepairReport report = Assert.Single(reports);
        Assert.Equal(1, report.CompletedRepairCount);
        Assert.Equal(0u, Arm(patient).Wounds.WoundTotal);
        Assert.Empty(force.Army.CyberneticRepairJobs);
        Assert.True(report.HoursByProvider[techmarine.Id] > 0f);
        Assert.Equal(
            report.HoursByProvider[techmarine.Id] * CyberneticRepairConstants.ExperiencePerRepairHour,
            PointsIn(techmarine, Cybernetics), 5);
    }

    [Fact]
    public void Garrison_ATechmarineElsewhere_DoesNotRepair_ButTheLoanDoes()
    {
        PlayerSoldier techmarine = Techmarine("Ferrus");
        PlayerSoldier patient = DamagedAugmetic("Varro", WoundLevel.Moderate);
        PlaceInRegion(TestRegion(7), techmarine);
        PlaceInRegion(TestRegion(8), patient);
        PlayerForce force = ForceOf(100, techmarine, patient);
        Assert.True(CyberneticRepairService.TryQueueRepair(force, patient, LeftArmId));

        CyberneticRepairService.ApplyGarrisonRepair(force, Cybernetics);
        Assert.NotEqual(0u, Arm(patient).Wounds.WoundTotal);

        force.IsMechanicusLoanActive = true;
        IReadOnlyList<CyberneticRepairReport> reports =
            CyberneticRepairService.ApplyGarrisonRepair(force, Cybernetics);

        Assert.Equal(0u, Arm(patient).Wounds.WoundTotal);
        CyberneticRepairReport worked = Assert.Single(reports, report => report.HoursSpent > 0f);
        Assert.Equal(8f, worked.HoursByProvider[CyberneticRepairReport.LoanProviderId], 3);
        Assert.Equal(0f, PointsIn(techmarine, Cybernetics));
    }

    [Fact]
    public void ATechmarineUnderOrders_DoesNotAlsoRepairTheQueueAtHome()
    {
        Region region = TestRegion(7);
        PlayerSoldier techmarine = Techmarine("Ferrus");
        PlayerSoldier patient = DamagedAugmetic("Varro", WoundLevel.Moderate);
        PlaceInRegion(region, techmarine, patient);
        PlayerForce force = ForceOf(100, techmarine, patient);
        Assert.True(CyberneticRepairService.TryQueueRepair(force, patient, LeftArmId));

        Order order = new([new Squad("Line", null, TestModelFactory.SquadTemplate)],
                          false, true, Aggression.Normal, null);
        order.AssignedCharacters.Add(techmarine);
        techmarine.CurrentOrder = order;

        CyberneticRepairService.ApplyGarrisonRepair(force, Cybernetics);

        Assert.NotEqual(0u, Arm(patient).Wounds.WoundTotal);
    }

    [Fact]
    public void Field_TheOrdersTechmarineRepairs_ButNotOnADayWithAMissionStep()
    {
        PlayerSoldier techmarine = Techmarine("Ferrus");
        PlayerSoldier patient = DamagedAugmetic("Varro", WoundLevel.Moderate);
        Order order = OrderFor(techmarine, patient);
        PlayerForce force = ForceOf(100, techmarine, patient);
        Assert.True(CyberneticRepairService.TryQueueRepair(force, patient, LeftArmId));
        CyberneticRepairReport report = new();

        CyberneticRepairService.ApplyDailyFieldRepair(
            order, force, report, Cybernetics, day: 1, missionStepToday: true);
        Assert.NotEqual(0u, Arm(patient).Wounds.WoundTotal);
        Assert.Contains(techmarine.Id, report.TechnicianIds);
        Assert.Equal(0f, report.HoursSpent);

        // An untrained Techmarine (skill about 6, factor about 1.9) needs about 15 hours for a
        // Moderate repair: a full day, then most of the next.
        CyberneticRepairService.ApplyDailyFieldRepair(
            order, force, report, Cybernetics, day: 2, missionStepToday: false);
        Assert.Equal(CyberneticRepairConstants.DailyWorkingHours, report.HoursSpent, 3);
        Assert.NotEqual(0u, Arm(patient).Wounds.WoundTotal);

        CyberneticRepairService.ApplyDailyFieldRepair(
            order, force, report, Cybernetics, day: 3, missionStepToday: false);
        Assert.Equal(0u, Arm(patient).Wounds.WoundTotal);
        Assert.Equal(1, report.CompletedRepairCount);
        Assert.Equal([2, 3], report.Work.Select(work => work.Day).ToArray());
        Assert.InRange(report.HoursSpent, 8f, 2f * CyberneticRepairConstants.DailyWorkingHours);
    }

    [Fact]
    public void Field_ReachIsTheOrder()
    {
        PlayerSoldier techmarine = Techmarine("Ferrus");
        PlayerSoldier outsider = DamagedAugmetic("Outsider", WoundLevel.Moderate);
        Order order = OrderFor(techmarine);
        PlayerForce force = ForceOf(100, techmarine, outsider);
        Assert.True(CyberneticRepairService.TryQueueRepair(force, outsider, LeftArmId));

        CyberneticRepairService.ApplyDailyFieldRepair(
            order, force, new CyberneticRepairReport(), Cybernetics, 1, false);

        Assert.NotEqual(0u, Arm(outsider).Wounds.WoundTotal);
        Assert.Single(force.Army.CyberneticRepairJobs);
    }

    // ---- Surgery installs the tier --------------------------------------------------------------

    [Fact]
    public void CyberneticReplacement_InstallsTheOptionsArmorAndConstitution()
    {
        PlayerSoldier soldier = Brother("Varro");
        for (int i = 0; i < 3; i++) Arm(soldier).Wounds.AddWound(WoundLevel.Critical);
        Assert.True(Arm(soldier).IsReplacementEligible);
        MedicalProcedure procedure = new(soldier.Id, LeftArmId, MedicalProcedureType.Cybernetic, 1, 0);
        CyberneticOption option = new(1, "Standard Augmetic", 5f, 40f);

        MedicalHealthPolicy.AdvanceProcedure(
            procedure, new Dictionary<int, Body> { [soldier.Id] = soldier.Body }, option);

        HitLocation arm = Arm(soldier);
        Assert.True(arm.IsCybernetic);
        Assert.Equal(5f, arm.NaturalArmor);
        Assert.Equal(40f, arm.EffectiveConstitution(soldier));
        // The rest of the body keeps the soldier's own constitution.
        Assert.Equal(soldier.Constitution, Torso(soldier).EffectiveConstitution(soldier));
    }

    [Fact]
    public void TheShippedRules_CarryOneAugmeticTier()
    {
        CyberneticOption option = SectorSimulationFixture.Create().Rules.StandardCyberneticOption;
        Assert.NotNull(option);
        Assert.Equal(5f, option.Armor);
        Assert.Equal(40f, option.Constitution);
    }

    // ---- Report and Apothecarium ----------------------------------------------------------------

    [Fact]
    public void ReportLine_NamesTheTechmarineAndTheRepairs_AndIsSilentWithoutWork()
    {
        MissionOutcomeClassification silent = new() { CyberneticRepairTechnicians = ["Ferrus"] };
        Assert.Equal("", MissionReportSummaryBuilder.BuildCyberneticRepairLine(silent));

        MissionOutcomeClassification worked = new()
        {
            CyberneticRepairTechnicians = ["Ferrus"],
            CyberneticRepairsCompleted = 2,
            CyberneticRepairHours = 12f
        };
        Assert.Equal(" Ferrus repaired 2 augmetics in the field (12 hours).",
            MissionReportSummaryBuilder.BuildCyberneticRepairLine(worked));
    }

    [Fact]
    public void Classifier_CarriesTheOrdersRepairReport()
    {
        CyberneticRepairReport report = new();
        report.AddTechnician(5, "Ferrus");
        report.RecordWork(new CyberneticRepairWork(5, "Ferrus", 9, "Varro", LeftArmId, "Left Arm", 8f, true));
        report.RecordWork(new CyberneticRepairWork(
            CyberneticRepairReport.LoanProviderId, CyberneticRepairReport.LoanProviderName,
            9, "Varro", TorsoId, "Torso", 2f, false));
        MissionContext context = new(null, [], []) { CyberneticRepair = report };

        MissionOutcomeClassification classification = MissionOutcomeClassifier.Classify(context);

        Assert.Equal(["Ferrus", CyberneticRepairReport.LoanProviderName],
            classification.CyberneticRepairTechnicians);
        Assert.Equal(1, classification.CyberneticRepairsCompleted);
        Assert.Equal(10f, classification.CyberneticRepairHours, 3);
    }

    [Fact]
    public void Apothecarium_ShowsTheRepairOptionAndThenTheQueuedJob()
    {
        PlayerSoldier patient = DamagedAugmetic("Varro", WoundLevel.Major);
        PlayerForce force = ForceOf(100, patient);
        ApothecariumMedicalRecordBuilder builder = new();

        CyberneticRepairOptionView option = Assert.Single(
            builder.BuildSoldierSummary(patient, force).SafeCyberneticRepairs);
        Assert.False(option.IsQueued);
        Assert.True(option.CanQueue);
        Assert.Equal(16f, option.HoursRemaining, 3);
        Assert.Equal(CyberneticRepairConstants.RequisitionCost, option.RequisitionCost);

        Assert.True(CyberneticRepairService.TryQueueRepair(force, patient, LeftArmId));
        CyberneticRepairOptionView queued = Assert.Single(
            builder.BuildSoldierSummary(patient, force).SafeCyberneticRepairs);
        Assert.True(queued.IsQueued);
        Assert.False(queued.CanQueue);
        Assert.Contains("Repair queued", builder.BuildSoldierSummary(patient, force).Wounds
            .Single(wound => wound.HitLocationId == LeftArmId).Recovery);

        CyberneticRepairQueueRow row = Assert.Single(
            builder.BuildVault(force, new Date(1, 1, 1)).SafeRepairQueue);
        Assert.Equal(1, row.Position);
        Assert.Equal(patient.Id, row.SoldierId);
        Assert.Equal("Left Arm", row.LocationName);
    }

    // ---- Fixtures -------------------------------------------------------------------------------

    private static CyberneticRepairProviderFacts Provider(int id, float hours, float factor) =>
        new(id, $"Provider {id}", hours, factor);

    private static CyberneticRepairJobFacts Facts(CyberneticRepairJob job, PlayerSoldier soldier) =>
        new(job, soldier.Name, Arm(soldier));

    private static HitLocation Arm(ISoldier soldier) =>
        soldier.Body.HitLocations.First(location => location.Template.Id == LeftArmId);

    private static HitLocation Torso(ISoldier soldier) =>
        soldier.Body.HitLocations.First(location => location.Template.Id == TorsoId);

    private static float PointsIn(PlayerSoldier soldier, BaseSkill skill) =>
        soldier.Skills.FirstOrDefault(s => s.BaseSkill.Id == skill.Id)?.PointsInvested ?? 0f;

    private static PlayerSoldier Brother(string name) => Make(name, BrotherTemplate);

    private static PlayerSoldier Techmarine(string name) => Make(name, TechmarineTemplate);

    private static PlayerSoldier DamagedAugmetic(string name, WoundLevel damage)
    {
        PlayerSoldier soldier = Brother(name);
        Arm(soldier).InstallCybernetic(new CyberneticOption(1, "Standard Augmetic", 5f, 40f));
        Arm(soldier).Wounds.AddWound(damage);
        return soldier;
    }

    private static PlayerSoldier Make(string name, SoldierTemplate template)
    {
        Soldier baseSoldier = TestModelFactory.CreateSoldier(template, name);
        return new PlayerSoldier(baseSoldier, name);
    }

    private static PlayerForce ForceOf(int requisition, params PlayerSoldier[] roster)
    {
        Unit chapter = new("Chapter", new UnitTemplate(100, "Chapter", true, [], []));
        return new PlayerForce(
            null,
            new Army("Army", null, "Commander", chapter, roster) { Requisition = requisition },
            new OnlyWar.Domain.Fleet("Fleet", null, null));
    }

    private static Order OrderFor(PlayerSoldier techmarine, params PlayerSoldier[] members)
    {
        Squad squad = new("Test Squad", null, TestModelFactory.SquadTemplate);
        if (techmarine != null) AttachToSquad(squad, techmarine);
        foreach (PlayerSoldier member in members) AttachToSquad(squad, member);
        return new Order([squad], false, true, Aggression.Normal, null);
    }

    private static void AttachToSquad(Squad squad, PlayerSoldier soldier)
    {
        squad.AddSquadMember(soldier);
        soldier.AssignedSquad = squad;
    }

    private static Region TestRegion(int id) =>
        new(id, null, 0, $"Region {id}", new RegionCoordinate(0, 0), 0f);

    private static void PlaceInRegion(Region region, params PlayerSoldier[] soldiers)
    {
        Squad squad = new($"Garrison {region.Id}", null, TestModelFactory.SquadTemplate)
        {
            CurrentRegion = region
        };
        foreach (PlayerSoldier soldier in soldiers) AttachToSquad(squad, soldier);
    }
}
