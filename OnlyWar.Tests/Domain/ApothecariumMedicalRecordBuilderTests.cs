using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Models;
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
        Assert.Contains(summary.ReplacementOptions, o => o.LocationName == "Left Arm" && o.Type == ReplacementType.Cybernetic);
        Assert.Contains(summary.ReplacementOptions, o => o.LocationName == "Left Arm" && o.Type == ReplacementType.VatGrown);
        Assert.Equal("Safe", summary.GeneSeedStatus);
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
        Assert.Contains(summary.SeriousWounds, row => row.SoldierName == "Out" && row.Recommendation == "assign replacement");
        Assert.Contains(summary.SeriousWounds, row => row.SoldierName == "Wounded" && row.Wound.Contains("Torso"));
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
