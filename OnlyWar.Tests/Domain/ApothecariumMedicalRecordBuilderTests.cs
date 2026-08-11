using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class ApothecariumMedicalRecordBuilderTests
{
    [Fact]
    public void BuildVault_CountsMatureImmatureAndSoonMaturingProgenoids()
    {
        Date currentDate = new(20_000);
        PlayerSoldier sixYearMarine = CreatePlayerSoldier(1, "Six-Year", currentDate.GetTotalWeeks() - 6 * 52);
        PlayerSoldier nineYearMarine = CreatePlayerSoldier(2, "Nine-Year", currentDate.GetTotalWeeks() - (9 * 52 + 20));
        PlayerForce force = CreateForce(currentDate, sixYearMarine, nineYearMarine);
        force.GeneseedStockpile = 7;
        ApothecariumMedicalRecordBuilder builder = new();

        GeneSeedVaultSummary summary = builder.BuildVault(force, currentDate);

        Assert.Equal(7, summary.Stockpile);
        Assert.Equal(2, summary.MatureImplanted);
        Assert.Equal(2, summary.ImmatureImplanted);
        Assert.Equal(1, summary.MaturingWithinOneYear);
        Assert.Equal(0, summary.AtRiskImplanted);
    }

    [Fact]
    public void BuildSoldierSummary_OffersReplacementOptionsForSeveredFunctionalLocation()
    {
        Date currentDate = new(20_000);
        PlayerSoldier soldier = CreatePlayerSoldier(3, "Orest", currentDate.GetTotalWeeks() - 6 * 52);
        HitLocation leftArm = soldier.Body.HitLocations.First(hl => hl.Template.Name == "Left Arm");
        leftArm.Wounds.AddWound(WoundLevel.Critical);
        leftArm.Wounds.AddWound(WoundLevel.Critical);
        leftArm.Wounds.AddWound(WoundLevel.Critical);
        ApothecariumMedicalRecordBuilder builder = new();

        MedicalSoldierSummary summary = builder.BuildSoldierSummary(soldier);

        Assert.Contains(summary.Wounds, w => w.LocationName == "Left Arm" && w.NeedsReplacement && w.Severity == MedicalSeverity.Lost);
        Assert.Contains(summary.ReplacementOptions, o =>
            o.LocationName == "Left Arm" && o.Type == MedicalProcedureType.Cybernetic && o.Weeks == 4);
        Assert.Contains(summary.ReplacementOptions, o =>
            o.LocationName == "Left Arm" && o.Type == MedicalProcedureType.VatGrown && o.Weeks == 6);
        Assert.Equal("Safe", summary.GeneSeedStatus);
    }

    [Fact]
    public void BuildSoldierSummary_DoesNotOfferReplacementForCrippledVitalLocation()
    {
        PlayerSoldier soldier = CreatePlayerSoldier(17, "Crippled Vital", 20_000);
        HitLocation torso = soldier.Body.HitLocations.First(hl => hl.Template.Name == "Torso");
        torso.Wounds.AddWound(WoundLevel.Massive);

        MedicalSoldierSummary summary = new ApothecariumMedicalRecordBuilder()
            .BuildSoldierSummary(soldier);

        Assert.True(torso.IsCrippled);
        Assert.False(torso.IsSevered);
        Assert.False(torso.IsReplacementEligible);
        Assert.DoesNotContain(summary.ReplacementOptions, option => option.LocationName == "Torso");
        Assert.Contains(summary.Wounds, wound =>
            wound.LocationName == "Torso"
            && !wound.NeedsReplacement
            && wound.Recovery == "15 weeks");
    }

    [Fact]
    public void BuildSoldierSummary_UsesActiveProcedureWeeksForRecovery()
    {
        Date currentDate = new(20_000);
        PlayerSoldier soldier = CreatePlayerSoldier(13, "Augmetic", currentDate.GetTotalWeeks() - 6 * 52);
        PlayerForce force = CreateForce(currentDate, soldier);
        HitLocation[] locations =
        [
            soldier.Body.HitLocations.First(hl => hl.Template.Name == "Left Arm"),
            soldier.Body.HitLocations.First(hl => hl.Template.Name == "Right Arm"),
            soldier.Body.HitLocations.First(hl => hl.Template.Name == "Left Foot")
        ];

        foreach (HitLocation location in locations)
        {
            location.Wounds.AddWound(WoundLevel.Critical);
            location.Wounds.AddWound(WoundLevel.Critical);
            location.Wounds.AddWound(WoundLevel.Critical);
            force.Army.MedicalProcedures.Add(new MedicalProcedure(
                soldier.Id, location.Template.Id, MedicalProcedureType.Cybernetic, 2, 40));
        }

        ApothecariumMedicalRecordBuilder builder = new();

        MedicalSoldierSummary summary = builder.BuildSoldierSummary(soldier, force);

        Assert.Equal(2, summary.MaxRecoveryWeeks);
        Assert.Empty(summary.ReplacementOptions);
        Assert.All(
            summary.Wounds.Where(wound => wound.NeedsReplacement),
            wound => Assert.Equal("2 weeks", wound.Recovery));
        Assert.All(
            summary.Wounds.Where(wound => wound.NeedsReplacement),
            wound => Assert.Equal("Replacement in progress", wound.Status));
    }

    [Fact]
    public void BuildSoldierSummary_LabelsCyberneticLocationsAndExplainsTheirRecovery()
    {
        PlayerSoldier soldier = CreatePlayerSoldier(15, "Augmetic", 20_000);
        HitLocation arm = soldier.Body.HitLocations.First(hl => hl.Template.Name == "Left Arm");
        arm.IsCybernetic = true;
        arm.Wounds.AddWound(WoundLevel.Moderate);

        MedicalSoldierSummary summary = new ApothecariumMedicalRecordBuilder()
            .BuildSoldierSummary(soldier);

        WoundLocationSummary wound = Assert.Single(
            summary.Wounds, w => w.LocationName == "Left Arm");
        Assert.True(wound.IsCybernetic);
        Assert.Equal("Cybernetic", wound.Status);
        Assert.Equal("Cybernetic repair required", wound.Recovery);
        Assert.DoesNotContain(summary.ReplacementOptions, option => option.LocationName == "Left Arm");
    }

    [Fact]
    public void BuildSoldierSummary_ReturnsDestroyedCyberneticLocationsToNormalReplacementFlow()
    {
        PlayerSoldier soldier = CreatePlayerSoldier(16, "Destroyed Augmetic", 20_000);
        HitLocation arm = soldier.Body.HitLocations.First(hl => hl.Template.Name == "Left Arm");
        arm.IsCybernetic = true;
        arm.Wounds.AddWound(WoundLevel.Critical);
        arm.Wounds.AddWound(WoundLevel.Critical);
        arm.Wounds.AddWound(WoundLevel.Critical);

        MedicalSoldierSummary summary = new ApothecariumMedicalRecordBuilder()
            .BuildSoldierSummary(soldier);

        Assert.False(arm.IsCybernetic);
        Assert.Contains(summary.ReplacementOptions,
            option => option.LocationName == "Left Arm" && option.Type == MedicalProcedureType.Cybernetic);
        Assert.Contains(summary.ReplacementOptions,
            option => option.LocationName == "Left Arm" && option.Type == MedicalProcedureType.VatGrown);
        Assert.Contains(summary.Wounds,
            wound => wound.LocationName == "Left Arm" && !wound.IsCybernetic && wound.Status == "Severed");
    }

    [Fact]
    public void BuildSoldierSummary_DoesNotOfferASeparateHandWhenItsArmIsSevered()
    {
        Date currentDate = new(20_000);
        PlayerSoldier soldier = CreatePlayerSoldier(14, "One Procedure", currentDate.GetTotalWeeks() - 6 * 52);
        PlayerForce force = CreateForce(currentDate, soldier);
        HitLocation arm = soldier.Body.HitLocations.First(hl => hl.Template.Name == "Left Arm");
        HitLocation hand = soldier.Body.HitLocations.First(hl => hl.Template.Name == "Left Hand");
        arm.Wounds.AddWound(WoundLevel.Critical);
        arm.Wounds.AddWound(WoundLevel.Critical);
        arm.Wounds.AddWound(WoundLevel.Critical);
        hand.Wounds.AddWound(WoundLevel.Critical);

        MedicalSoldierSummary summary = new ApothecariumMedicalRecordBuilder()
            .BuildSoldierSummary(soldier, force);

        Assert.True(arm.IsReplacementEligible);
        Assert.True(arm.IsSevered);
        Assert.True(hand.IsSevered);
        Assert.False(hand.IsReplacementEligible);
        Assert.Contains(summary.ReplacementOptions, option => option.LocationName == "Left Arm");
        Assert.DoesNotContain(summary.ReplacementOptions, option => option.LocationName == "Left Hand");
        Assert.Contains(summary.Wounds, wound =>
            wound.LocationName == "Left Hand" && wound.Recovery == "Covered by arm replacement");
    }

    [Fact]
    public void BuildSoldierSummary_IncludesCurrentLocationInAssignmentHeader()
    {
        Date currentDate = new(20_000);
        PlayerSoldier soldier = CreatePlayerSoldier(12, "Orest", currentDate.GetTotalWeeks() - 6 * 52);
        PlayerForce force = CreateForce(currentDate, soldier);
        Squad squad = force.Army.OrderOfBattle.GetAllSquads().Single();
        squad.BoardedLocation = new Ship(7, "Glory of Terra", new ShipTemplate(7, "Strike Cruiser", 20, 0, 0));
        ApothecariumMedicalRecordBuilder builder = new();

        MedicalSoldierSummary summary = builder.BuildSoldierSummary(soldier);

        Assert.Equal("Test Squad, 1st Company - Glory of Terra, in transit", summary.Assignment);
    }

    [Fact]
    public void BuildSquadSummary_DerivesReadinessRollupAndSeriousWounds()
    {
        Date currentDate = new(20_000);
        PlayerSoldier healthy = CreatePlayerSoldier(4, "Healthy", currentDate.GetTotalWeeks() - 6 * 52);
        PlayerSoldier outOfAction = CreatePlayerSoldier(5, "Out", currentDate.GetTotalWeeks() - 6 * 52);
        PlayerSoldier wounded = CreatePlayerSoldier(6, "Wounded", currentDate.GetTotalWeeks() - 6 * 52);
        outOfAction.Body.HitLocations.First(hl => hl.Template.Name == "Left Leg").Wounds.AddWound(WoundLevel.Massive);
        wounded.Body.HitLocations.First(hl => hl.Template.Name == "Torso").Wounds.AddWound(WoundLevel.Moderate);
        PlayerForce force = CreateForce(currentDate, healthy, outOfAction, wounded);
        Squad squad = force.Army.OrderOfBattle.GetAllSquads().Single();
        ApothecariumMedicalRecordBuilder builder = new();

        MedicalUnitSummary summary = builder.BuildSquadSummary(squad);

        Assert.Equal(1, summary.HealthyCount);
        Assert.Equal(2, summary.WoundedCount);
        Assert.Equal(1, summary.OutOfActionCount);
        Assert.True(summary.MaxRecoveryWeeks >= 3);
        Assert.Contains(summary.SeriousWounds, row => row.SoldierName == "Out" && row.Recommendation == "recover");
        Assert.Contains(summary.SeriousWounds, row => row.SoldierName == "Wounded" && row.Wound.Contains("Torso"));
    }

    [Fact]
    public void BuildVault_SurfacesRealAggregatePurityFromForce()
    {
        Date currentDate = new(20_000);
        PlayerSoldier marine = CreatePlayerSoldier(10, "Pure", currentDate.GetTotalWeeks() - 6 * 52);
        PlayerForce force = CreateForce(currentDate, marine);
        force.GeneseedStockpile = 4;
        force.GeneseedPurity = 0.80f;
        ApothecariumMedicalRecordBuilder builder = new();

        GeneSeedVaultSummary summary = builder.BuildVault(force, currentDate);

        Assert.Equal(0.80f, summary.AggregatePurity, 3);
        // 0.80 falls in the Degraded tier (>= 0.70, < 0.85).
        Assert.Equal("Degraded", summary.PurityStatus);
        Assert.Contains(summary.Rows, r => r.Title == "Aggregate gene-seed purity");
    }

    [Fact]
    public void BuildVault_EmptyStockpileReportsNoMeaningfulPurity()
    {
        Date currentDate = new(20_000);
        PlayerSoldier marine = CreatePlayerSoldier(11, "Fresh", currentDate.GetTotalWeeks() - 6 * 52);
        PlayerForce force = CreateForce(currentDate, marine);
        force.GeneseedStockpile = 0;
        ApothecariumMedicalRecordBuilder builder = new();

        GeneSeedVaultSummary summary = builder.BuildVault(force, currentDate);

        Assert.Equal("No stock", summary.PurityStatus);
        Assert.Equal(MedicalSeverity.None, summary.PuritySeverity);
    }

    private static PlayerSoldier CreatePlayerSoldier(int id, string name, int implantWeek)
    {
        Soldier soldier = TestModelFactory.CreateSoldier(name: name);
        soldier.Id = id;
        return new PlayerSoldier(soldier, name)
        {
            ProgenoidImplantDate = new Date(implantWeek)
        };
    }

    private static PlayerForce CreateForce(Date currentDate, params PlayerSoldier[] soldiers)
    {
        UnitTemplate chapterTemplate = new(100, "Chapter", true, new List<SquadTemplate>(), new List<UnitTemplate>());
        UnitTemplate companyTemplate = new(101, "Company", false, new List<SquadTemplate>(), new List<UnitTemplate>());
        Unit chapter = new("Test Chapter", chapterTemplate);
        Unit company = new("1st Company", companyTemplate) { ParentUnit = chapter };
        chapter.ChildUnits.Add(company);

        Squad squad = new("Test Squad", company, TestModelFactory.SquadTemplate);
        company.AddSquad(squad);
        foreach (PlayerSoldier soldier in soldiers)
        {
            squad.AddSquadMember(soldier);
        }

        Army army = new("Test Army", null, "Commander", chapter, soldiers);
        return new PlayerForce(null, army, null);
    }
}
