using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class MedicalTurnProcessorTests
{
    private static HitLocation Location(Body body, int templateId) =>
        body.HitLocations.First(hl => hl.Template.Id == templateId);

    private static PlayerSoldier SeveredArmSoldier(int id, out HitLocation arm)
    {
        Soldier soldier = TestModelFactory.CreateSoldier(name: "Subject");
        soldier.Id = id;
        PlayerSoldier playerSoldier = new(soldier, "Subject");
        arm = playerSoldier.Body.HitLocations.First(hl => hl.Template.Name == "Left Arm");
        arm.Wounds.AddWound(WoundLevel.Critical);
        arm.Wounds.AddWound(WoundLevel.Critical);
        arm.Wounds.AddWound(WoundLevel.Critical);
        return playerSoldier;
    }

    [Fact]
    public void ApplyWeeklyHealing_DegradesAHealableWoundOverTime()
    {
        Body body = new(HumanBodyTemplate.Instance);
        // Left Arm (id 4): a Moderate wound is well below the arm's cripple threshold, so it
        // heals naturally.
        HitLocation arm = Location(body, 4);
        arm.Wounds.AddWound(WoundLevel.Moderate);

        MedicalTurnProcessor.ApplyWeeklyHealing(body);
        MedicalTurnProcessor.ApplyWeeklyHealing(body);

        Assert.Equal(0, arm.Wounds.ModerateWounds);
        Assert.Equal(1, arm.Wounds.MinorWounds);
    }

    [Fact]
    public void ApplyWeeklyHealing_DoesNotHealASeveredLocation()
    {
        Body body = new(HumanBodyTemplate.Instance);
        // Left Arm severed (3x Critical reaches its sever threshold).
        HitLocation arm = Location(body, 4);
        arm.Wounds.AddWound(WoundLevel.Critical);
        arm.Wounds.AddWound(WoundLevel.Critical);
        arm.Wounds.AddWound(WoundLevel.Critical);
        Assert.True(arm.IsSevered);
        uint before = arm.Wounds.WoundTotal;

        MedicalTurnProcessor.ApplyWeeklyHealing(body);

        Assert.Equal(before, arm.Wounds.WoundTotal);
        Assert.True(arm.IsSevered);
    }

    [Fact]
    public void ApplyWeeklyHealing_DoesNotHealACrippledFunctionalLocation()
    {
        Body body = new(HumanBodyTemplate.Instance);
        // Left Leg (motive) crippled but not severed: needs a replacement, so it is frozen.
        HitLocation leg = Location(body, 9);
        leg.Wounds.AddWound(WoundLevel.Critical);
        Assert.True(leg.IsCrippled);
        Assert.False(leg.IsSevered);
        Assert.True(leg.IsReplacementEligible);
        uint before = leg.Wounds.WoundTotal;

        MedicalTurnProcessor.ApplyWeeklyHealing(body);

        Assert.Equal(before, leg.Wounds.WoundTotal);
    }

    [Fact]
    public void ResolveProcedures_DecrementsButDoesNotCompleteWhileWeeksRemain()
    {
        PlayerSoldier soldier = SeveredArmSoldier(1, out HitLocation arm);
        Dictionary<int, PlayerSoldier> map = new() { [1] = soldier };
        MedicalProcedure procedure = new(1, arm.Template.Id, MedicalProcedureType.Cybernetic, 2, 40);
        List<MedicalProcedure> procedures = [procedure];

        MedicalTurnProcessor.ResolveProcedures(procedures, map);

        Assert.Single(procedures);
        Assert.Equal(1, procedure.WeeksRemaining);
        Assert.True(arm.IsSevered);
        Assert.False(arm.IsCybernetic);
    }

    [Fact]
    public void ResolveProcedures_CyberneticCompletionRestoresLocationAndMarksItAugmetic()
    {
        PlayerSoldier soldier = SeveredArmSoldier(1, out HitLocation arm);
        Dictionary<int, PlayerSoldier> map = new() { [1] = soldier };
        List<MedicalProcedure> procedures =
            [new(1, arm.Template.Id, MedicalProcedureType.Cybernetic, 1, 40)];

        MedicalTurnProcessor.ResolveProcedures(procedures, map);

        Assert.Empty(procedures);
        Assert.Equal((uint)0, arm.Wounds.WoundTotal);
        Assert.False(arm.IsSevered);
        Assert.True(arm.IsCybernetic);
    }

    [Fact]
    public void ResolveProcedures_VatGrownCompletionRestoresLocationWithoutMarkingItAugmetic()
    {
        PlayerSoldier soldier = SeveredArmSoldier(1, out HitLocation arm);
        Dictionary<int, PlayerSoldier> map = new() { [1] = soldier };
        List<MedicalProcedure> procedures =
            [new(1, arm.Template.Id, MedicalProcedureType.VatGrown, 1, 95)];

        MedicalTurnProcessor.ResolveProcedures(procedures, map);

        Assert.Empty(procedures);
        Assert.Equal((uint)0, arm.Wounds.WoundTotal);
        Assert.False(arm.IsSevered);
        Assert.False(arm.IsCybernetic);
    }
}
