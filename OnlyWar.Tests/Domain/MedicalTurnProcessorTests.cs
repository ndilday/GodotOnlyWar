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
    public void ApplyWeeklyHealing_DoesNotHealACyberneticLocation()
    {
        Body body = new(HumanBodyTemplate.Instance);
        HitLocation arm = Location(body, 4);
        arm.IsCybernetic = true;
        arm.Wounds.AddWound(WoundLevel.Moderate);
        uint before = arm.Wounds.WoundTotal;

        MedicalTurnProcessor.ApplyWeeklyHealing(body);

        Assert.Equal(before, arm.Wounds.WoundTotal);
    }

    [Fact]
    public void ASeveredCyberneticLocationStopsBeingCybernetic()
    {
        Body body = new(HumanBodyTemplate.Instance);
        HitLocation arm = Location(body, 4);
        arm.IsCybernetic = true;
        arm.Wounds.AddWound(WoundLevel.Critical);
        arm.Wounds.AddWound(WoundLevel.Critical);
        arm.Wounds.AddWound(WoundLevel.Critical);

        Assert.True(arm.IsSevered);
        Assert.False(arm.IsCybernetic);
    }

    [Fact]
    public void ApplyWeeklyHealing_DoesNotHealAHandCoveredByASeveredArm()
    {
        Body body = new(HumanBodyTemplate.Instance);
        HitLocation arm = Location(body, 4);
        HitLocation hand = Location(body, 6);
        arm.Wounds.AddWound(WoundLevel.Critical);
        arm.Wounds.AddWound(WoundLevel.Critical);
        arm.Wounds.AddWound(WoundLevel.Critical);
        hand.Wounds.AddWound(WoundLevel.Moderate);
        uint before = hand.Wounds.WoundTotal;

        Assert.True(hand.IsCoveredBySeveredParent);
        Assert.False(hand.IsReplacementEligible);
        MedicalTurnProcessor.ApplyWeeklyHealing(body);

        Assert.Equal(before, hand.Wounds.WoundTotal);
    }

    [Fact]
    public void ApplyWeeklyHealing_DoesNotHealACrippledFunctionalLocation()
    {
        Body body = new(HumanBodyTemplate.Instance);
        // Left Foot (motive) crippled but not severed: needs a replacement, so it is frozen.
        //
        // Was the Left Leg on one Critical wound until Phase 3 of
        // Design/Reference/CasualtyRealism.md raised the leg cripple threshold to Massive, which
        // collided with the leg's sever threshold and briefly deleted "crippled but not severed"
        // as a state a leg could hold. RulesMigration_LegSeverThreshold.sql restored it by moving
        // leg sever up to Mortal (§2.1, "Why sever moved too"), and the leg case is pinned
        // separately below. This one deliberately STAYS on the foot: it is the extremity half of
        // the rule, where cripple and sever have always been a band apart, and it would be a
        // regression to lose coverage of it by moving the test back.
        HitLocation foot = Location(body, 11);
        foot.Wounds.AddWound(WoundLevel.Major);
        Assert.True(foot.IsCrippled);
        Assert.False(foot.IsSevered);
        Assert.True(foot.IsReplacementEligible);
        uint before = foot.Wounds.WoundTotal;

        MedicalTurnProcessor.ApplyWeeklyHealing(body);

        Assert.Equal(before, foot.Wounds.WoundTotal);
    }

    // Design/Reference/CasualtyRealism.md §2.1, "Why sever moved too" (2026-08-06). Phase 3 raised leg
    // CRIPPLE to Massive while leg SEVER was already Massive, collapsing the two thresholds onto one
    // band -- so every leg wound that felled a marine also took the leg off, and "crippled but not
    // severed" ceased to exist for the body's principal motive location. That state is exactly what
    // §2.3's Incapacitated outcome is built on. Moving sever up to Mortal restores it, and this is
    // the test that says so: a leg at Massive fells the man and stays attached.
    [Fact]
    public void ALegAtMassive_IsCrippledButNotSevered_AndStaysFrozen()
    {
        Body body = new(HumanBodyTemplate.Instance);
        HitLocation leg = Location(body, 9);
        leg.Wounds.AddWound(WoundLevel.Massive);

        Assert.True(leg.IsCrippled);
        Assert.False(leg.IsSevered);
        Assert.True(leg.IsReplacementEligible);

        uint before = leg.Wounds.WoundTotal;
        MedicalTurnProcessor.ApplyWeeklyHealing(body);
        Assert.Equal(before, leg.Wounds.WoundTotal);
    }

    // The other half of the same decision: the leg does still come off, one band higher up. Without
    // this the migration could be silently reverted to "legs never sever" and nothing would notice.
    [Fact]
    public void ALegAtMortal_IsSevered()
    {
        Body body = new(HumanBodyTemplate.Instance);
        HitLocation leg = Location(body, 9);
        leg.Wounds.AddWound(WoundLevel.Mortal);

        Assert.True(leg.IsSevered);
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
        Assert.True(soldier.IsUndergoingMedicalProcedure);
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
        Assert.False(soldier.IsUndergoingMedicalProcedure);
    }

    [Fact]
    public void ResolveProcedures_ArmReplacementAlsoRestoresItsGroupedHand()
    {
        PlayerSoldier soldier = SeveredArmSoldier(1, out HitLocation arm);
        HitLocation hand = soldier.Body.HitLocations.First(location => location.Template.Name == "Left Hand");
        hand.Wounds.AddWound(WoundLevel.Critical);
        List<MedicalProcedure> procedures =
            [new(1, arm.Template.Id, MedicalProcedureType.Cybernetic, 1, 40)];

        Assert.True(hand.IsSevered);
        MedicalTurnProcessor.ResolveProcedures(
            procedures,
            new Dictionary<int, PlayerSoldier> { [1] = soldier });

        Assert.Empty(procedures);
        Assert.Equal((uint)0, arm.Wounds.WoundTotal);
        Assert.Equal((uint)0, hand.Wounds.WoundTotal);
        Assert.True(arm.IsCybernetic);
        Assert.True(hand.IsCybernetic);
        Assert.False(hand.IsSevered);
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
