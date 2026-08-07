using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Medical;
using OnlyWar.Models;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

/// <summary>
/// Phase 2b of Design/Reference/CasualtyRealism.md: Apothecary field care.
///
/// The triage tie-break draws from the shared session RNG like all other gameplay randomness, so
/// this class needs [Collection(TestCollections.SharedState)] or it flakes under xUnit parallelism.
/// An earlier version used a private stream to avoid perturbing seeded battle baselines; that was
/// reverted deliberately (the game is in alpha, and seeded reproducibility only matters for sector
/// and chapter generation, which runs before any battle).
/// </summary>
[Collection(TestCollections.SharedState)]
public class FieldCareServiceTests
{
    private static readonly Date TestDate = new(1, 1, 1);

    private static readonly SoldierTemplate ApothecaryTemplate = new(
        901, TestModelFactory.HumanSpecies, "Apothecary",
        rank: 4, subrank: 1, isSquadLeader: false, specialistType: 0,
        Array.Empty<ValueTuple<BaseSkill, float>>(), battleValue: 2);

    private static readonly SoldierTemplate MasterTemplate = new(
        902, TestModelFactory.HumanSpecies, "Master of the Apothecarion",
        rank: 6, subrank: 1, isSquadLeader: false, specialistType: 0,
        Array.Empty<ValueTuple<BaseSkill, float>>(), battleValue: 2);

    // Two line templates that share a Rank, so the Subrank tie-break has something to decide.
    private static readonly SoldierTemplate BrotherTemplate = new(
        903, TestModelFactory.HumanSpecies, "Battle-Brother",
        rank: 1, subrank: 1, isSquadLeader: false, specialistType: 0,
        Array.Empty<ValueTuple<BaseSkill, float>>(), battleValue: 2);

    private static readonly SoldierTemplate SeniorBrotherTemplate = new(
        904, TestModelFactory.HumanSpecies, "Senior Battle-Brother",
        rank: 1, subrank: 2, isSquadLeader: false, specialistType: 0,
        Array.Empty<ValueTuple<BaseSkill, float>>(), battleValue: 2);

    private static readonly SoldierTemplate SergeantTemplate = new(
        905, TestModelFactory.HumanSpecies, "Sergeant",
        rank: 5, subrank: 1, isSquadLeader: true, specialistType: 0,
        Array.Empty<ValueTuple<BaseSkill, float>>(), battleValue: 2);

    // ---- Capacity ---------------------------------------------------------------------------

    [Fact]
    public void Capacity_IsMildlySuperlinearInTheMedicalRating()
    {
        float ordinary = FieldCareConstants.GetDailyCapacity(100f);
        float master = FieldCareConstants.GetDailyCapacity(130f);

        Assert.Equal(FieldCareConstants.BaseDailyCapacity, ordinary, 3);
        // Superlinear: 30% more rating buys MORE than 30% more capacity...
        Assert.True(master > ordinary * 1.3f, $"{master} should exceed {ordinary * 1.3f}");
        // ...but nowhere near "worth several ordinary brothers" (§3.2).
        Assert.True(master < ordinary * 2f, $"{master} should be well under {ordinary * 2f}");
    }

    [Fact]
    public void Capacity_IsZeroForAnUnevaluatedBrotherAndCappedForAFreakRating()
    {
        Assert.Equal(0f, FieldCareConstants.GetDailyCapacity(0f));
        Assert.Equal(0f, FieldCareConstants.GetDailyCapacity(-20f));
        Assert.Equal(
            FieldCareConstants.MaxDailyCapacityPerApothecary,
            FieldCareConstants.GetDailyCapacity(100000f),
            3);
    }

    // ---- The cost curve ---------------------------------------------------------------------

    // The whole point of §3.2's "flat relative to band values" decision. Wound bands are powers of
    // 16, so a proportional cost would put an Unsurvivable wound 16 million times out of reach.
    [Fact]
    public void DemotionCost_ScalesWithBandIndexNotBandValue()
    {
        float moderate = FieldCareConstants.GetDemotionCost(WoundLevel.Moderate, 1);
        float unsurvivable = FieldCareConstants.GetDemotionCost(WoundLevel.Unsurvivable, 1);

        Assert.True(unsurvivable > moderate, "severe must still cost more than light");
        // Band VALUES differ by 16^5; costs must differ by a small constant factor.
        Assert.True(unsurvivable <= moderate * 5f,
            $"{unsurvivable} vs {moderate}: the curve is not flat enough to treat severe wounds");
    }

    [Fact]
    public void ASingleDayOfOneApothecary_CanTreatTheWorstWoundInTheGame()
    {
        float capacity = FieldCareConstants.GetDailyCapacity(100f);
        Assert.True(
            FieldCareConstants.GetDemotionCost(WoundLevel.Mortal, 1) <= capacity,
            "one Apothecary-day must be able to move a Mortal wound down a band");
    }

    // The worst wound field care can reach is the band below the location's CRIPPLE threshold: at
    // or above it the location is replacement-eligible and frozen for surgery, exactly as it is for
    // natural weekly healing. For the torso that ceiling is Critical -- a ten-week wound, and
    // precisely the case that would otherwise keep a brother out for months.
    [Fact]
    public void SevereWoundsAreActuallyTreatedInTheField()
    {
        PlayerSoldier apothecary = Apothecary("Kadmon", 100f);
        PlayerSoldier patient = Wounded("Rhys", BrotherTemplate, TorsoId, WoundLevel.Critical);
        Order order = OrderFor(apothecary, patient);

        byte before = Torso(patient).Wounds.RecoveryTimeLeft();
        Assert.Equal(10, before);
        FieldCareService.ApplyDailyFieldCare(order, new FieldCareReport());

        Assert.True(Torso(patient).Wounds.RecoveryTimeLeft() < before);
        Assert.Equal(0, Torso(patient).Wounds.CriticalWounds);
    }

    // A location carrying several wounds of one band is severe without being crippled -- two
    // Critical torso wounds are 0x20000, still short of Massive -- and the count surcharge means
    // treating them costs more than treating one, but nowhere near twice as much.
    [Fact]
    public void AMultiWoundBandIsTreatedWholesale_AtASurchargedNotDoubledPrice()
    {
        PlayerSoldier apothecary = Apothecary("Kadmon", 100f);
        PlayerSoldier patient = Wounded("Rhys", BrotherTemplate, TorsoId, WoundLevel.Critical);
        Torso(patient).Wounds.AddWound(WoundLevel.Critical);
        Assert.False(Torso(patient).IsCrippled);

        FieldCareReport report = new();
        FieldCareService.ApplyDailyFieldCare(OrderFor(apothecary, patient), report);

        Assert.Equal(0, Torso(patient).Wounds.CriticalWounds);
        Assert.Equal(2, Torso(patient).Wounds.MajorWounds);
        float single = FieldCareConstants.GetDemotionCost(WoundLevel.Critical, 1);
        Assert.True(report.Treatments[0].Cost > single);
        Assert.True(report.Treatments[0].Cost < single * 2f);
    }

    // ---- Triage -----------------------------------------------------------------------------

    [Fact]
    public void Triage_TreatsTheWorstWoundFirst()
    {
        PlayerSoldier apothecary = Apothecary("Kadmon", 100f);
        PlayerSoldier light = Wounded("Light", BrotherTemplate, TorsoId, WoundLevel.Moderate);
        PlayerSoldier grave = Wounded("Grave", BrotherTemplate, TorsoId, WoundLevel.Critical);
        Order order = OrderFor(apothecary, light, grave);

        FieldCareReport report = new();
        FieldCareService.ApplyDailyFieldCare(order, report);

        Assert.Equal(grave.Id, report.Treatments[0].SoldierId);
    }

    [Fact]
    public void Triage_BreaksSeverityTiesOnRankThenSubrank()
    {
        // Capacity is spent one treatment at a time; with three equally-wounded men the order the
        // treatments come out in IS the triage order.
        PlayerSoldier apothecary = Apothecary("Kadmon", 100f);
        PlayerSoldier junior = Wounded("Junior", BrotherTemplate, TorsoId, WoundLevel.Moderate);
        PlayerSoldier senior = Wounded("Senior", SeniorBrotherTemplate, TorsoId, WoundLevel.Moderate);
        PlayerSoldier sergeant = Wounded("Sergeant", SergeantTemplate, TorsoId, WoundLevel.Moderate);
        Order order = OrderFor(apothecary, junior, senior, sergeant);

        FieldCareReport report = new();
        FieldCareService.ApplyDailyFieldCare(order, report);

        Assert.Equal(3, report.TreatmentCount);
        // Rank first (the sergeant outranks both brothers), then subrank (senior over junior).
        Assert.Equal(
            new[] { sergeant.Id, senior.Id, junior.Id },
            report.Treatments.Select(t => t.SoldierId).ToArray());
    }

    // Two men identical in severity, rank and subrank: only the random draw separates them.
    //
    // The guarantee is that the SESSION RNG decides it, so re-running from the same seed reproduces
    // the same queue. Resetting the shared stream is the whole point -- field care draws from it
    // like everything else, rather than from a private stream kept isolated to protect seeded
    // battle baselines.
    [Fact]
    public void Triage_RandomBreaksTheLastTie_AndReplaysFromTheSameSeed()
    {
        PlayerSoldier apothecary = Apothecary("Kadmon", 100f);
        PlayerSoldier a = Wounded("Alpha", BrotherTemplate, TorsoId, WoundLevel.Moderate, id: 4001);
        PlayerSoldier b = Wounded("Beta", BrotherTemplate, TorsoId, WoundLevel.Moderate, id: 4002);
        Order order = OrderFor(apothecary, a, b);

        int[] Resolve()
        {
            RNG.Reset(20260807);
            Torso(a).Wounds.HealWounds();
            Torso(b).Wounds.HealWounds();
            Torso(a).Wounds.AddWound(WoundLevel.Moderate);
            Torso(b).Wounds.AddWound(WoundLevel.Moderate);
            FieldCareReport report = new();
            FieldCareService.ApplyDailyFieldCare(order, report);
            return report.Treatments.Select(t => t.SoldierId).ToArray();
        }

        int[] first = Resolve();
        Assert.Equal(2, first.Length);
        Assert.Equal(first, Resolve());
        Assert.Equal(first, Resolve());
    }

    [Fact]
    public void DailyRetriage_LetsALaterCasualtyDisplaceAnEarlierOne()
    {
        PlayerSoldier apothecary = Apothecary("Kadmon", 100f);
        PlayerSoldier dayOne = Wounded("DayOne", BrotherTemplate, TorsoId, WoundLevel.Moderate);
        PlayerSoldier dayFour = Healthy("DayFour", BrotherTemplate);
        Order order = OrderFor(apothecary, dayOne, dayFour);

        FieldCareReport report = new();
        FieldCareService.ApplyDailyFieldCare(order, report);
        Assert.Equal(dayOne.Id, report.Treatments[0].SoldierId);

        // Day 4: a fresh, far worse casualty arrives and takes the head of the queue.
        Torso(dayFour).Wounds.AddWound(WoundLevel.Critical);
        FieldCareReport dayFourReport = new();
        FieldCareService.ApplyDailyFieldCare(order, dayFourReport);

        Assert.Equal(dayFour.Id, dayFourReport.Treatments[0].SoldierId);
    }

    // ---- Reach, dedup and the daily seam -----------------------------------------------------

    // Design/Reference/SpecialistAttachment.md §8 trap 1: MissionTurnProcessor.BuildMissionElements
    // fans ONE order into several independent single-squad elements under
    // MissionForceMode.IndependentSquads, each with its own MissionStepDriver. A pass hung off a
    // driver would run once per element and make an Apothecary silently worth 3x. The service is
    // per-ORDER, so the guarantee it can offer is this: N calls do N days of work, not N x elements.
    [Fact]
    public void FieldCare_IsPerOrder_SoOneCallPerDayIsOneDayOfCapacity()
    {
        PlayerSoldier apothecary = Apothecary("Kadmon", 100f);
        PlayerSoldier patient = Wounded("Rhys", BrotherTemplate, TorsoId, WoundLevel.Critical);
        Order order = OrderFor(apothecary, patient);

        FieldCareReport oneCall = new();
        FieldCareService.ApplyDailyFieldCare(order, oneCall);
        float spentOnce = oneCall.CapacitySpent;
        Assert.True(spentOnce > 0f);

        // Reset and run the same day THREE times, as a driver-hung pass on a three-element
        // IndependentSquads order would have.
        PlayerSoldier apothecary2 = Apothecary("Kadmon", 100f);
        PlayerSoldier patient2 = Wounded("Rhys", BrotherTemplate, TorsoId, WoundLevel.Critical);
        Order order2 = OrderFor(apothecary2, patient2);
        FieldCareReport threeCalls = new();
        for (int i = 0; i < 3; i++)
        {
            FieldCareService.ApplyDailyFieldCare(order2, threeCalls);
        }

        // Three calls really do treat three times as much -- which is exactly why the CALLER must
        // dedup by order, and why MissionTurnProcessor collects distinct orders before the day loop
        // rather than iterating scheduled mission elements.
        Assert.True(threeCalls.CapacitySpent > spentOnce);
        Assert.Equal(spentOnce, oneCall.CapacitySpent, 3);
    }

    [Fact]
    public void Reach_IsTheOrder_IncludingItsAttachedSpecialists()
    {
        // The Apothecary is ATTACHED (Phase 2a), not a member of an assigned squad, and the patient
        // is in a squad under the order. This is the shape the whole feature is built for.
        PlayerSoldier apothecary = Apothecary("Kadmon", 100f);
        PlayerSoldier patient = Wounded("Rhys", BrotherTemplate, TorsoId, WoundLevel.Major);

        Squad line = new("Line Squad", null, TestModelFactory.SquadTemplate);
        AttachToSquad(line, patient);
        Order order = new([line], false, true, Aggression.Normal, null);
        order.AttachedSoldiers.Add(apothecary);
        apothecary.AttachedOrder = order;

        FieldCareReport report = new();
        FieldCareService.ApplyDailyFieldCare(order, report);

        Assert.NotEmpty(report.Treatments);
        Assert.Equal(patient.Id, report.Treatments[0].SoldierId);
        Assert.Contains(apothecary.Id, report.ApothecaryIds);
    }

    [Fact]
    public void NoApothecary_MeansNoTreatmentAndNoReport()
    {
        PlayerSoldier patient = Wounded("Rhys", BrotherTemplate, TorsoId, WoundLevel.Critical);
        Order order = OrderFor(null, patient);
        uint before = Torso(patient).Wounds.WoundTotal;

        FieldCareReport report = new();
        FieldCareService.ApplyDailyFieldCare(order, report);

        Assert.Equal(before, Torso(patient).Wounds.WoundTotal);
        Assert.False(report.HasApothecary);
        Assert.Equal(0, report.TreatmentCount);
    }

    // ---- What field care may not touch --------------------------------------------------------

    [Fact]
    public void ReplacementEligibleAndSeveredLocationsAreLeftAlone()
    {
        // "Surgery remains surgery" (§2.6): a crippled leg awaiting a replacement is frozen for
        // field care exactly as it is for natural weekly healing.
        PlayerSoldier apothecary = Apothecary("Kadmon", 130f);
        PlayerSoldier patient = Healthy("Rhys", BrotherTemplate);
        HitLocation leg = patient.Body.HitLocations.First(hl => hl.Template.Name == "Left Leg");
        leg.Wounds.AddWound(WoundLevel.Massive);
        Assert.True(leg.IsReplacementEligible);
        uint before = leg.Wounds.WoundTotal;

        Order order = OrderFor(apothecary, patient);
        FieldCareReport report = new();
        FieldCareService.ApplyDailyFieldCare(order, report);

        Assert.Equal(before, leg.Wounds.WoundTotal);
        Assert.Equal(0, report.TreatmentCount);
    }

    [Fact]
    public void LightWoundsAreNotWorthAnApothecarysDay()
    {
        // Bands below Moderate carry no healing clock and clear on the next natural pass anyway.
        PlayerSoldier apothecary = Apothecary("Kadmon", 100f);
        PlayerSoldier patient = Wounded("Rhys", BrotherTemplate, TorsoId, WoundLevel.Minor);

        FieldCareReport report = new();
        FieldCareService.ApplyDailyFieldCare(OrderFor(apothecary, patient), report);

        Assert.Equal(0, report.TreatmentCount);
    }

    // ---- Treatment is visible immediately ------------------------------------------------------

    // §2.6's central decision: treatment is a forced demotion applied THE DAY IT HAPPENS, not a
    // credit settled at turn end. A brother hit on day 2 and treated that evening must enter the
    // day-3 battle at reduced severity, and battle setup reads live wound state -- so "visible to
    // the next day" is exactly "the wound object changed before the next day's pass ran".
    [Fact]
    public void TreatmentOnDayTwo_IsAlreadyInEffectOnDayThree()
    {
        PlayerSoldier apothecary = Apothecary("Kadmon", 100f);
        PlayerSoldier patient = Wounded("Rhys", BrotherTemplate, TorsoId, WoundLevel.Critical);
        Order order = OrderFor(apothecary, patient);

        FieldCareService.ApplyDailyFieldCare(order, new FieldCareReport());
        uint afterDayTwo = Torso(patient).Wounds.WoundTotal;
        Assert.Equal(1, Torso(patient).Wounds.MajorWounds);
        Assert.Equal(0, Torso(patient).Wounds.CriticalWounds);

        // Day 3 opens on the treated body, and keeps going down.
        FieldCareService.ApplyDailyFieldCare(order, new FieldCareReport());
        Assert.True(Torso(patient).Wounds.WoundTotal < afterDayTwo);
    }

    // ---- Garrison ------------------------------------------------------------------------------

    [Fact]
    public void GarrisonCare_TreatsCoLocatedBrothersWhoAreNotOnAMission()
    {
        Region region = TestRegion(7);
        PlayerSoldier apothecary = Apothecary("Kadmon", 100f);
        PlayerSoldier patient = Wounded("Rhys", BrotherTemplate, TorsoId, WoundLevel.Critical);
        PlaceInRegion(region, apothecary, patient);

        IReadOnlyList<FieldCareReport> reports = FieldCareService.ApplyGarrisonFieldCare(
            [apothecary, patient]);

        Assert.Single(reports);
        Assert.True(reports[0].TreatmentCount > 0);
        // A full week of garrison care walks a ten-week wound all the way down to Minor, which the
        // next natural pass clears outright. Field care never bothers with Minor itself.
        Assert.True(Torso(patient).Wounds.WoundTotal < (uint)WoundLevel.Moderate);
    }

    [Fact]
    public void GarrisonCare_DoesNotReachAnotherLocation()
    {
        PlayerSoldier apothecary = Apothecary("Kadmon", 100f);
        PlayerSoldier patient = Wounded("Rhys", BrotherTemplate, TorsoId, WoundLevel.Critical);
        PlaceInRegion(TestRegion(7), apothecary);
        PlaceInRegion(TestRegion(8), patient);
        uint before = Torso(patient).Wounds.WoundTotal;

        FieldCareService.ApplyGarrisonFieldCare([apothecary, patient]);

        Assert.Equal(before, Torso(patient).Wounds.WoundTotal);
    }

    // Field beats garrison by construction, not by a priority rule (§3.3): an Apothecary under
    // orders fails the very "not on a mission" test that defines the garrison pool, so the two
    // pools are disjoint and no man can spend the same day twice.
    [Fact]
    public void AnApothecaryUnderOrders_DoesNotAlsoClearTheBacklogAtHome()
    {
        Region region = TestRegion(7);
        PlayerSoldier apothecary = Apothecary("Kadmon", 100f);
        PlayerSoldier homeWounded = Wounded("Rhys", BrotherTemplate, TorsoId, WoundLevel.Critical);
        PlaceInRegion(region, apothecary, homeWounded);

        // Send him forward.
        Order order = new([new Squad("Line", null, TestModelFactory.SquadTemplate)],
                          false, true, Aggression.Normal, null);
        order.AttachedSoldiers.Add(apothecary);
        apothecary.AttachedOrder = order;

        uint before = Torso(homeWounded).Wounds.WoundTotal;
        FieldCareService.ApplyGarrisonFieldCare([apothecary, homeWounded]);

        Assert.Equal(before, Torso(homeWounded).Wounds.WoundTotal);
    }

    // ---- Learn-by-doing --------------------------------------------------------------------------

    [Fact]
    public void TreatingEarnsMedicalExperience_AndOnlyWhenCapacityWasActuallySpent()
    {
        BaseSkill firstAid = new(
            910, SkillCategory.Military, "First Aid",
            OnlyWar.Models.Soldiers.Attribute.Intelligence, 0);
        PlayerSoldier apothecary = Apothecary("Kadmon", 100f);
        PlayerSoldier patient = Wounded("Rhys", BrotherTemplate, TorsoId, WoundLevel.Critical);
        Order order = OrderFor(apothecary, patient);

        // Measured in POINTS INVESTED rather than total skill value: a day's practice is a fraction
        // of a point, and the skill-value curve reads a fraction of a point as still worse than
        // untrained. The learn-by-doing question is whether the practice was banked.
        Assert.Equal(0f, PointsIn(apothecary, firstAid));
        FieldCareService.ApplyDailyFieldCare(order, new FieldCareReport(), [firstAid]);
        Assert.True(PointsIn(apothecary, firstAid) > 0f, "a day spent treating must teach something");

        // A day with nobody left to treat teaches nothing.
        PlayerSoldier idle = Apothecary("Idle", 100f);
        Order idleOrder = OrderFor(idle, Healthy("Fit", BrotherTemplate));
        FieldCareService.ApplyDailyFieldCare(idleOrder, new FieldCareReport(), [firstAid]);
        Assert.Equal(0f, PointsIn(idle, firstAid));
    }

    private static float PointsIn(PlayerSoldier soldier, BaseSkill skill) =>
        soldier.Skills.FirstOrDefault(s => s.BaseSkill.Id == skill.Id)?.PointsInvested ?? 0f;

    // ---- Coverage readout ------------------------------------------------------------------------

    [Fact]
    public void CoverageReadout_NamesTheApothecaryUnderTheSameOrder()
    {
        PlayerSoldier apothecary = Apothecary("Kadmon", 100f);
        PlayerSoldier patient = Wounded("Rhys", BrotherTemplate, TorsoId, WoundLevel.Major);
        OrderFor(apothecary, patient);

        IReadOnlyList<PlayerSoldier> covering =
            FieldCareService.GetCoveringApothecaries(patient, [apothecary, patient]);

        Assert.Single(covering);
        Assert.Equal(apothecary.Id, covering[0].Id);
    }

    [Fact]
    public void CoverageReadout_GoesEmptyForTheMenHeLeftBehind()
    {
        Region region = TestRegion(7);
        PlayerSoldier apothecary = Apothecary("Kadmon", 100f);
        PlayerSoldier homeWounded = Wounded("Rhys", BrotherTemplate, TorsoId, WoundLevel.Major);
        PlaceInRegion(region, apothecary, homeWounded);

        Assert.Single(FieldCareService.GetCoveringApothecaries(
            homeWounded, [apothecary, homeWounded]));

        Order order = new([new Squad("Line", null, TestModelFactory.SquadTemplate)],
                          false, true, Aggression.Normal, null);
        order.AttachedSoldiers.Add(apothecary);
        apothecary.AttachedOrder = order;

        Assert.Empty(FieldCareService.GetCoveringApothecaries(
            homeWounded, [apothecary, homeWounded]));
    }

    // ---- The Master is better, and not by much ---------------------------------------------------

    [Fact]
    public void TheMasterOfTheApothecarion_OutworksAnOrdinaryApothecaryWithoutReplacingSeveral()
    {
        int TreatmentsFor(SoldierTemplate template, float rating)
        {
            PlayerSoldier apothecary = MakePlayerSoldier("Medic", template, rating);
            List<PlayerSoldier> patients = Enumerable.Range(0, 12)
                .Select(i => Wounded($"P{i}", BrotherTemplate, TorsoId, WoundLevel.Moderate))
                .ToList();
            Order order = OrderFor(apothecary, patients.ToArray());
            FieldCareReport report = new();
            FieldCareService.ApplyDailyFieldCare(order, report);
            return report.TreatmentCount;
        }

        int ordinary = TreatmentsFor(ApothecaryTemplate, 100f);
        int master = TreatmentsFor(MasterTemplate, 130f);

        Assert.True(master > ordinary, $"master {master} should beat ordinary {ordinary}");
        Assert.True(master < ordinary * 2, $"master {master} should not be worth two of {ordinary}");
    }

    // ---- Fixtures ---------------------------------------------------------------------------------

    private const int TorsoId = 3;

    private static HitLocation Torso(PlayerSoldier soldier) =>
        soldier.Body.HitLocations.First(hl => hl.Template.Id == TorsoId);

    private static PlayerSoldier Apothecary(string name, float medicalRating) =>
        MakePlayerSoldier(name, ApothecaryTemplate, medicalRating);

    private static PlayerSoldier Healthy(string name, SoldierTemplate template, int? id = null) =>
        MakePlayerSoldier(name, template, 0f, id);

    private static PlayerSoldier Wounded(
        string name, SoldierTemplate template, int locationId, WoundLevel level, int? id = null)
    {
        PlayerSoldier soldier = MakePlayerSoldier(name, template, 0f, id);
        soldier.Body.HitLocations.First(hl => hl.Template.Id == locationId)
            .Wounds.AddWound(level);
        return soldier;
    }

    private static PlayerSoldier MakePlayerSoldier(
        string name, SoldierTemplate template, float medicalRating, int? id = null)
    {
        Soldier baseSoldier = TestModelFactory.CreateSoldier(template, name);
        if (id.HasValue) baseSoldier.Id = id.Value;
        PlayerSoldier soldier = new(baseSoldier, name);
        soldier.AddEvaluation(new SoldierEvaluation(
            TestDate, 0f, 0f, 0f, medicalRating, 0f, 0f, 0f));
        return soldier;
    }

    // Puts everyone in one squad and issues that squad an order, which is the simplest shape that
    // exercises "reach is the order". A null apothecary just means the order has none.
    private static Order OrderFor(PlayerSoldier apothecary, params PlayerSoldier[] members)
    {
        Squad squad = new("Test Squad", null, TestModelFactory.SquadTemplate);
        if (apothecary != null) AttachToSquad(squad, apothecary);
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
