using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Abstractions;
using OnlyWar.Application;
using OnlyWar.Battles;
using OnlyWar.Battles.Resolutions;
using OnlyWar.Campaign.Medical;
using OnlyWar.Domain;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Units;
using OnlyWar.Medical.Abstractions;
using OnlyWar.Medical.Treatment;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

/// <summary>
/// Vital-location replacement outside the battle aftermath (OnlyWar_TDD.md §5.3):
/// the needs-organ-replacement flag, the organ procedure, augmetic organ hit routing and repair.
/// The aftermath rolls are tested in <c>BattleAftermathPolicyTests</c>, persistence in
/// <c>CyberneticRepairPersistenceTests</c>.
/// </summary>
public class VitalOrganReplacementTests
{
    private static readonly CyberneticOption OrganOption = new(2, "Vital Organ Augmetic", 5f, 40f);

    // ---- Rules data -----------------------------------------------------------------------------

    [Fact]
    public void TheShippedRules_MarkTheVitalsAndTorsoOfEveryBody_AndOneOrganTier()
    {
        GameRulesData rules = SectorSimulationFixture.Create().Rules;
        List<HitLocationTemplate> locations = rules.BodyHitLocationTemplateMap.Values
            .SelectMany(body => body)
            .ToList();

        Assert.Contains(locations, location => location.SeveredMayNeedOrganReplacement);
        Assert.Contains(locations, location => location.CrippledMayNeedOrganReplacement);
        Assert.All(
            locations.Where(location => location.SeveredMayNeedOrganReplacement),
            location => Assert.Equal("Vitals", location.Name));
        Assert.All(
            locations.Where(location => location.CrippledMayNeedOrganReplacement),
            location => Assert.Equal("Torso", location.Name));
        Assert.All(
            locations.Where(location => location.Name is "Vitals" or "Torso"),
            location => Assert.True(VitalOrganRules.CanHoldAugmeticOrgans(location)));

        Assert.True(rules.VitalOrganCyberneticOption.IsAugmeticOrganTier);
        Assert.False(rules.StandardCyberneticOption.IsAugmeticOrganTier);
    }

    [Fact]
    public void TheRulesValidator_RejectsMisplacedOrganFlagsAndASecondOrganTier()
    {
        GameRulesBlob blob = new()
        {
            BodyTemplates = new Dictionary<int, List<HitLocationTemplate>>
            {
                [0] =
                [
                    new HitLocationTemplate { Id = 1, Name = "Arm", SeveredMayNeedOrganReplacement = true },
                    new HitLocationTemplate
                    {
                        Id = 2, Name = "Chest", IsVital = true,
                        SeveredMayNeedOrganReplacement = true, CrippledMayNeedOrganReplacement = true
                    }
                ]
            },
            CyberneticOptions =
            [
                new CyberneticOption(1, "Organ A", 5f, 40f, isAugmeticOrganTier: true),
                new CyberneticOption(2, "Organ B", 5f, 40f, isAugmeticOrganTier: true)
            ]
        };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => OnlyWar.Persistence.Database.GameRules.RulesDatabaseValidator.Validate(blob));

        Assert.Contains("HitLocationTemplate 1 ('Arm') has an organ-replacement flag but is not vital", error.Message);
        Assert.Contains("HitLocationTemplate 2 ('Chest') sets both", error.Message);
        Assert.Contains("more than one row with IsAugmeticOrganTier", error.Message);
    }

    // ---- The flag -------------------------------------------------------------------------------

    [Fact]
    public void AFlaggedLocation_DoesNotHealWeeklyOrDaily()
    {
        PlayerSoldier soldier = Brother("Varro");
        HitLocation torso = Location(soldier, "Torso");
        torso.Wounds.AddWound(WoundLevel.Massive);
        torso.Wounds.AddWound(WoundLevel.Negligible);
        torso.RequiresOrganReplacement = true;
        uint before = torso.Wounds.WoundTotal;

        MedicalHealthPolicy.ApplyDailyHealing(soldier.Body, acceleratedHealing: true);
        for (int week = 0; week < 20; week++)
        {
            MedicalHealthPolicy.ApplyWeeklyHealing(soldier.Body);
        }

        Assert.Equal(before, torso.Wounds.WoundTotal);
    }

    [Fact]
    public void FieldCare_SkipsAFlaggedLocation()
    {
        PlayerSoldier soldier = Brother("Varro");
        HitLocation torso = Location(soldier, "Torso");
        torso.Wounds.AddWound(WoundLevel.Massive);
        torso.RequiresOrganReplacement = true;

        IReadOnlyList<FieldCareTreatmentResult> treatments = FieldCarePolicy.ApplyDailyCare(
            [new FieldCareProviderFacts(99, "Apothecary", 1000f)],
            [new FieldCarePatientFacts(soldier.Id, soldier.Name, 1, 1, soldier.Body)],
            new FixedRNG());

        Assert.Empty(treatments);
        Assert.Equal((uint)WoundLevel.Massive, torso.Wounds.WoundTotal);
    }

    [Fact]
    public void AFlaggedSeveredVitals_IsNotASeveredVitalForCasualtyClassification()
    {
        PlayerSoldier soldier = Brother("Varro");
        HitLocation vitals = Location(soldier, "Vitals");
        vitals.Wounds.AddWound(WoundLevel.Massive);
        Assert.True(CasualtyStateEvaluator.HasSeveredVitalLocation(soldier));

        vitals.RequiresOrganReplacement = true;

        Assert.False(CasualtyStateEvaluator.HasSeveredVitalLocation(soldier));
        Assert.False(soldier.IsCombatEffective);
    }

    [Fact]
    public void OrganReplacementOptions_UseTheNotSeveredPriceRow()
    {
        PlayerSoldier soldier = Brother("Varro");
        HitLocation vitals = Location(soldier, "Vitals");
        vitals.Wounds.AddWound(WoundLevel.Massive);
        vitals.RequiresOrganReplacement = true;

        MedicalSoldierSummary summary = new ApothecariumMedicalRecordBuilder().BuildSoldierSummary(soldier);

        MedicalTreatmentOptionView cybernetic = summary.ReplacementOptions
            .Single(option => option.Type == MedicalProcedureChoice.Cybernetic);
        MedicalTreatmentOptionView vatGrown = summary.ReplacementOptions
            .Single(option => option.Type == MedicalProcedureChoice.VatGrown);
        Assert.Equal((4, 25), (cybernetic.Weeks, cybernetic.RequisitionCost));
        Assert.Equal((10, 70), (vatGrown.Weeks, vatGrown.RequisitionCost));
        Assert.Equal(vitals.Template.Id, cybernetic.HitLocationId);
    }

    // ---- The procedure --------------------------------------------------------------------------

    [Theory]
    [InlineData(MedicalProcedureType.Cybernetic, 1)]
    [InlineData(MedicalProcedureType.VatGrown, 0)]
    public void Completion_ClearsWoundsAndFlag_KeepsTheLocationOrganic(
        MedicalProcedureType type, int expectedOrgans)
    {
        PlayerSoldier soldier = Brother("Varro");
        HitLocation torso = Location(soldier, "Torso");
        torso.Wounds.AddWound(WoundLevel.Massive);
        torso.RequiresOrganReplacement = true;

        MedicalProcedureCompletion completion = Complete(soldier, torso, type);

        Assert.NotNull(completion);
        Assert.Equal(0u, torso.Wounds.WoundTotal);
        Assert.False(torso.RequiresOrganReplacement);
        Assert.False(torso.IsReplacementEligible);
        Assert.False(torso.IsCybernetic);
        Assert.Equal(expectedOrgans, torso.AugmeticOrgans.Count);
        Assert.Equal(MedicalProcedureRules.GetWeeks(type, isSevered: false), completion.ProcedureDurationWeeks);
        // The progenoid gland survives; the location still holds one and is not severed.
        Assert.True(torso.Template.HoldsProgenoid);
        Assert.False(torso.IsSevered);
        Assert.True(soldier.IsCombatEffective);
    }

    [Fact]
    public void ACyberneticReplacementAtTheCap_RestoresTheLocationButAddsNoOrgan()
    {
        PlayerSoldier soldier = Brother("Varro");
        HitLocation torso = Location(soldier, "Torso");
        for (int i = 0; i < VitalOrganRules.MaximumAugmeticOrgansPerLocation; i++)
        {
            Assert.NotNull(torso.AddAugmeticOrgan(OrganOption));
        }
        torso.Wounds.AddWound(WoundLevel.Massive);
        torso.RequiresOrganReplacement = true;

        Complete(soldier, torso, MedicalProcedureType.Cybernetic);

        Assert.Equal(VitalOrganRules.MaximumAugmeticOrgansPerLocation, torso.AugmeticOrgans.Count);
        Assert.Equal(0u, torso.Wounds.WoundTotal);
        Assert.False(torso.RequiresOrganReplacement);
    }

    [Theory]
    [InlineData(MedicalProcedureType.Cybernetic, 1)]
    [InlineData(MedicalProcedureType.VatGrown, 0)]
    public void ReplacingASeveredAugmeticOrgan_NeverGrowsTheCount(
        MedicalProcedureType type, int expectedOrgans)
    {
        PlayerSoldier soldier = Brother("Varro");
        HitLocation vitals = Location(soldier, "Vitals");
        AugmeticOrgan lost = vitals.AddAugmeticOrgan(OrganOption);
        lost.Wounds.AddWound(WoundLevel.Massive);
        Assert.True(lost.IsSevered);
        vitals.RequiresOrganReplacement = true;

        Complete(soldier, vitals, type);

        Assert.Equal(expectedOrgans, vitals.AugmeticOrgans.Count);
        Assert.DoesNotContain(lost, vitals.AugmeticOrgans);
        Assert.All(vitals.AugmeticOrgans, organ => Assert.Equal(0u, organ.Wounds.WoundTotal));
        Assert.True(soldier.IsCombatEffective);
    }

    // ---- Augmetic organs in battle --------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void HitRouting_StrikesAnOrganAtNTimesThePerOrganChance(int organCount)
    {
        const int Steps = 100;
        int organHits = 0;
        for (int step = 0; step < Steps; step++)
        {
            BattleSoldier sufferer = Sufferer(out HitLocation vitals);
            for (int i = 0; i < organCount; i++) vitals.AddAugmeticOrgan(OrganOption);
            WoundResolver resolver = new(new FixedLinearRng((step + 0.5) / Steps));
            resolver.WoundQueue.Add(new WoundResolution(null, null, sufferer, 1f, vitals));

            resolver.Resolve();

            if (vitals.AugmeticOrgans.Any(organ => organ.Wounds.WoundTotal > 0)) organHits++;
        }

        Assert.Equal(
            (int)System.Math.Round(Steps * organCount * VitalOrganRules.HitChancePerAugmeticOrgan),
            organHits);
    }

    [Fact]
    public void HitRouting_ALocationWithoutOrgans_DrawsNothing()
    {
        BattleSoldier sufferer = Sufferer(out HitLocation vitals);
        WoundResolver resolver = new(new ThrowingRng());
        resolver.WoundQueue.Add(new WoundResolution(null, null, sufferer, 1f, vitals));

        resolver.Resolve();

        Assert.NotEqual(0u, vitals.Wounds.WoundTotal);
    }

    [Theory]
    [InlineData(0.1, 0)]
    [InlineData(0.3, 1)]
    public void AnOrganHit_ChoosesTheOrganUniformly_AndUsesTheOrgansConstitution(double roll, int struck)
    {
        BattleSoldier sufferer = Sufferer(out HitLocation vitals);
        vitals.AddAugmeticOrgan(OrganOption);
        vitals.AddAugmeticOrgan(OrganOption);
        WoundResolver resolver = new(new FixedLinearRng(roll));
        // Soldier constitution 10, organ constitution 40, wound multiplier 1.5: 20 damage is a
        // ratio of 0.75 against the organ (Major) and 3 against the flesh (Massive).
        resolver.WoundQueue.Add(new WoundResolution(null, null, sufferer, 20f, vitals));

        resolver.Resolve();

        Assert.Equal(0u, vitals.Wounds.WoundTotal);
        Assert.Equal((uint)WoundLevel.Major, vitals.AugmeticOrgans[struck].Wounds.WoundTotal);
        Assert.Equal(0u, vitals.AugmeticOrgans[1 - struck].Wounds.WoundTotal);
    }

    [Fact]
    public void ACrippledAugmeticOrgan_TakesTheBrotherOutOfTheFight()
    {
        BattleSoldier sufferer = Sufferer(out HitLocation vitals);
        vitals.AddAugmeticOrgan(OrganOption);
        Assert.True(sufferer.IsCombatEffective);
        bool died = false;
        WoundResolver resolver = new(new FixedLinearRng(0.0));
        resolver.OnSoldierDeath += (_, _) => died = true;
        // 40 x 1.5 / 40 = 1.5 => Critical, the Vitals cripple threshold.
        resolver.WoundQueue.Add(new WoundResolution(null, null, sufferer, 40f, vitals));

        resolver.Resolve();

        Assert.True(died);
        Assert.True(vitals.HasCrippledAugmeticOrgan);
        Assert.False(vitals.IsCrippled);
        Assert.False(sufferer.CanFight);
        Assert.False(sufferer.IsCombatEffective);
    }

    // ---- Repair ---------------------------------------------------------------------------------

    [Fact]
    public void ARepairedOrgan_IsFullyRestored_AndAnOrganNeverHealsNaturally()
    {
        PlayerSoldier soldier = Brother("Varro");
        HitLocation vitals = Location(soldier, "Vitals");
        AugmeticOrgan organ = vitals.AddAugmeticOrgan(OrganOption);
        organ.Wounds.AddWound(WoundLevel.Major);
        for (int week = 0; week < 10; week++) MedicalHealthPolicy.ApplyWeeklyHealing(soldier.Body);
        Assert.Equal((uint)WoundLevel.Major, organ.Wounds.WoundTotal);

        CyberneticRepairJob job = new(soldier.Id, vitals.Template.Id, 0f, 10, organ.Index);
        IReadOnlyList<CyberneticRepairWork> work = CyberneticRepairPolicy.ApplyDailyRepair(
            [new CyberneticRepairProviderFacts(1, "Techmarine", 24f, 1f)],
            [new CyberneticRepairJobFacts(job, soldier.Name, vitals, organ)]);

        CyberneticRepairWork entry = Assert.Single(work);
        Assert.True(entry.Completed);
        Assert.Equal(organ.Index, entry.OrganIndex);
        Assert.Equal(0u, organ.Wounds.WoundTotal);
    }

    [Fact]
    public void OrganRepair_QueuesByOrganIdentity_AndAppearsInTheApothecarium()
    {
        PlayerSoldier soldier = Brother("Varro");
        HitLocation vitals = Location(soldier, "Vitals");
        vitals.AddAugmeticOrgan(OrganOption);
        AugmeticOrgan damaged = vitals.AddAugmeticOrgan(OrganOption);
        damaged.Wounds.AddWound(WoundLevel.Moderate);
        PlayerForce force = ForceOf(100, soldier);
        ApothecariumMedicalRecordBuilder builder = new();

        CyberneticRepairOptionView option = Assert.Single(
            builder.BuildSoldierSummary(soldier, force).SafeCyberneticRepairs);
        Assert.Equal(damaged.Index, option.OrganIndex);
        Assert.True(option.CanQueue);
        Assert.False(CyberneticRepairService.TryQueueRepair(force, soldier, vitals.Template.Id, 1));
        Assert.True(CyberneticRepairService.TryQueueRepair(force, soldier, vitals.Template.Id, damaged.Index));

        CyberneticRepairJob job = Assert.Single(force.Army.CyberneticRepairJobs);
        Assert.Equal(damaged.Index, job.OrganIndex);
        Assert.Equal(2, builder.BuildSoldierSummary(soldier, force).Wounds
            .Single(wound => wound.HitLocationId == vitals.Template.Id).AugmeticOrganCount);
        Assert.Equal(damaged.Name, Assert.Single(builder.BuildVault(force, new Date(1, 1, 1)).SafeRepairQueue).LocationName);
    }

    // ---- Fixtures -------------------------------------------------------------------------------

    private static MedicalProcedureCompletion Complete(
        PlayerSoldier soldier, HitLocation location, MedicalProcedureType type)
    {
        MedicalProcedure procedure = new(soldier.Id, location.Template.Id, type, 1, 25);
        return MedicalHealthPolicy.AdvanceProcedure(
            procedure,
            new Dictionary<int, Body> { [soldier.Id] = soldier.Body },
            new CyberneticOption(1, "Standard Augmetic", 5f, 40f),
            OrganOption);
    }

    // A whole human body with constitution 10, and its Vitals.
    private static BattleSoldier Sufferer(out HitLocation vitals)
    {
        Soldier soldier = TestModelFactory.CreateSoldier(name: "Wounded Marine");
        soldier.Constitution = 10;
        vitals = Location(soldier, "Vitals");
        return new BattleSoldier(soldier, null);
    }

    private static HitLocation Location(ISoldier soldier, string name) =>
        soldier.Body.HitLocations.First(location => location.Template.Name == name);

    private static PlayerSoldier Brother(string name) =>
        new(TestModelFactory.CreateSoldier(TestModelFactory.MarineTemplate, name), name);

    private static PlayerForce ForceOf(int requisition, params PlayerSoldier[] roster)
    {
        Unit chapter = new("Chapter", new UnitTemplate(100, "Chapter", true, [], []));
        return new PlayerForce(
            null,
            new Army("Army", null, "Commander", chapter, roster) { Requisition = requisition },
            new OnlyWar.Domain.Fleet("Fleet", null, null));
    }

    private sealed class FixedLinearRng(double value) : IRNG
    {
        public double GetLinearDouble() => value;
        public double GetDoubleInRange(double lowerBound, double upperBound) => lowerBound;
        public int GetIntBelowMax(int min, int max) => min;
        public double NextRandomZValue() => 0.0;
    }

    private sealed class ThrowingRng : IRNG
    {
        public double GetLinearDouble() => throw new InvalidOperationException("No draw expected.");
        public double GetDoubleInRange(double lowerBound, double upperBound) => throw new InvalidOperationException();
        public int GetIntBelowMax(int min, int max) => throw new InvalidOperationException();
        public double NextRandomZValue() => throw new InvalidOperationException();
    }
}
