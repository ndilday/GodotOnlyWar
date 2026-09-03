using System.Linq;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

// Pins the CanFight / CanMove split introduced by Phase 0 of Design/Reference/CasualtyRealism.md.
//
// Before the split, Soldier.CanFight answered one question -- "is this man out of the fight?" --
// by folding hands, vitals and motive locations together. It is now three properties:
//
//   CanFight          hands + vital (consciousness) locations; motive locations are irrelevant
//   CanMove           motive locations only
//   IsCombatEffective CanFight && CanMove -- the old combined predicate, which is what every
//                     caller that used to read CanFight now reads
//
// Phase 0 was a pure refactor. Phase 3 has since replaced the binary CanMove with a graded
// speed multiplier (MotiveImpairment), so CanMove now means "speed above zero" and the leg and
// foot cases below assert the NEW answers -- a Critical leg keeps him moving, only Massive or a
// sever stops him, and a foot never zeros movement on its own. An untreated severed foot still
// removes him from battlefield combat effectiveness, while CanFight remains true.
// The band-by-band curve itself is pinned by MotiveImpairmentTests.
public class CanFightCanMoveTests
{
    private static HitLocation Find(Soldier soldier, string name) =>
        soldier.Body.HitLocations.Single(location => location.Template.Name == name);

    private static void Cripple(HitLocation location) =>
        location.Wounds = new Wounds(location.Template.CrippleWound, 0);

    private static void Sever(HitLocation location) =>
        location.Wounds = new Wounds(location.Template.SeverWound, 0);

    [Fact]
    public void UnwoundedSoldier_CanFightAndCanMove()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();

        Assert.True(soldier.CanFight);
        Assert.True(soldier.CanMove);
        Assert.True(soldier.IsCombatEffective);
    }

    // The headline case for the whole plan. A Critical leg wound used to cripple the leg and end
    // the marine's battle outright; since Phase 3 raised the leg cripple threshold to Massive it
    // slows him instead, and he stays in the fight.
    [Fact]
    public void CriticalLegWound_SlowsButDoesNotStop()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        HitLocation leg = Find(soldier, "Left Leg");

        leg.Wounds.AddWound(WoundLevel.Critical);

        Assert.False(leg.IsCrippled);
        Assert.True(soldier.CanFight);
        Assert.True(soldier.CanMove);
        Assert.True(soldier.IsCombatEffective);
        Assert.Equal(CasualtyConstants.CriticalMotiveSpeedMultiplier, soldier.MotiveSpeedMultiplier);
    }

    // At Massive the leg is both crippled and severed, and a load-bearing location at zero takes
    // the whole product to zero.
    [Fact]
    public void MassiveLegWound_StopsMovementButNotFighting()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();

        Cripple(Find(soldier, "Left Leg"));

        Assert.True(soldier.CanFight);
        Assert.False(soldier.CanMove);
        Assert.Equal(0f, soldier.MotiveSpeedMultiplier);
        Assert.False(soldier.IsCombatEffective);
    }

    [Fact]
    public void SeveredLeg_StopsMovementButNotFighting()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();

        Sever(Find(soldier, "Right Leg"));

        Assert.True(soldier.CanFight);
        Assert.False(soldier.CanMove);
        Assert.False(soldier.IsCombatEffective);
    }

    // Feet are motive too, so they land on CanMove rather than CanFight -- but the foot floor
    // means they can never take CanMove to false. Untreated severance is a separate physical
    // battlefield-incapacity rule, covered by MotiveImpairmentTests.
    [Fact]
    public void CrippledFoot_SlowsSeverelyButNeverStops()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();

        Cripple(Find(soldier, "Left Foot"));

        Assert.True(soldier.CanFight);
        Assert.True(soldier.CanMove);
        Assert.True(soldier.IsCombatEffective);
        Assert.Equal(CasualtyConstants.ExtremitySpeedFloor, soldier.MotiveSpeedMultiplier);
    }

    // One usable hand group is enough to keep fighting; losing the last one is not.
    [Fact]
    public void LosingEveryHandGroup_StopsFightingButNotMovement()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();

        Cripple(Find(soldier, "Left Arm"));
        Cripple(Find(soldier, "Left Hand"));

        Assert.True(soldier.CanFight);
        Assert.True(soldier.CanMove);
        Assert.True(soldier.IsCombatEffective);

        Cripple(Find(soldier, "Right Hand"));

        Assert.Equal(0, soldier.FunctioningHands);
        Assert.False(soldier.CanFight);
        // Hands have nothing to do with walking.
        Assert.True(soldier.CanMove);
        Assert.False(soldier.IsCombatEffective);
    }

    // Per the current rule, a crippled vital location clears CanFight. That is deliberately
    // unchanged by Phase 0: the vital term stayed with CanFight rather than moving to CanMove.
    // Phase 1 gives this case a name ("incapacitated") without changing the predicate.
    [Fact]
    public void CrippledVitalLocation_StopsFightingButNotMovement()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        HitLocation vital = soldier.Body.HitLocations
            .First(location => location.Template.IsVital && !location.Template.IsMotive);

        vital.Wounds.AddWound(WoundLevel.Massive);
        Assert.True(vital.IsCrippled);

        Assert.False(soldier.CanFight);
        Assert.True(soldier.CanMove);
        Assert.False(soldier.IsCombatEffective);
    }

    // A wounded-but-not-crippled location changes nothing on either axis.
    [Fact]
    public void WoundBelowCrippleThreshold_LeavesBothCapabilitiesIntact()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        HitLocation leg = Find(soldier, "Left Leg");

        leg.Wounds.AddWound(WoundLevel.Minor);
        Assert.False(leg.IsCrippled);

        Assert.True(soldier.CanFight);
        Assert.True(soldier.CanMove);
        Assert.True(soldier.IsCombatEffective);
    }

    // PlayerSoldier wraps a Soldier; the split must be visible through the wrapper, since the
    // chapter and Apothecarium surfaces read it there.
    [Fact]
    public void PlayerSoldierWrapper_ForwardsBothCapabilities()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        PlayerSoldier player = new(soldier, soldier.Name);

        Assert.True(player.CanFight);
        Assert.True(player.CanMove);
        Assert.True(player.IsCombatEffective);
        Assert.True(player.IsDeployable);

        Cripple(Find(soldier, "Left Leg"));

        Assert.True(player.CanFight);
        Assert.False(player.CanMove);
        Assert.Equal(0f, player.MotiveSpeedMultiplier);
        Assert.False(player.IsCombatEffective);
        Assert.False(player.IsDeployable);
    }

    // Deployability decision, Phase 3 (§3.3 "Deployability"): an impaired-but-mobile brother may
    // deploy, because under graded impairment he can genuinely still fight. The old rule barred
    // anyone with a crippled motive location, which would now bar a man limping at 0.6.
    [Fact]
    public void ImpairedButMobileBrother_RemainsDeployable()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        PlayerSoldier player = new(soldier, soldier.Name);

        Find(soldier, "Left Leg").Wounds.AddWound(WoundLevel.Critical);
        Cripple(Find(soldier, "Right Foot"));

        Assert.True(player.IsDeployable);
        Assert.Equal(
            CasualtyConstants.CriticalMotiveSpeedMultiplier * CasualtyConstants.ExtremitySpeedFloor,
            player.MotiveSpeedMultiplier,
            5);
    }

    [Fact]
    public void OneFunctioningHandGroup_RemainsCombatEffectiveButIsNotDeployable()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        PlayerSoldier player = new(soldier, soldier.Name);

        Cripple(Find(soldier, "Left Arm"));
        Cripple(Find(soldier, "Left Hand"));

        Assert.Equal(1, player.FunctioningHands);
        Assert.True(player.CanFight);
        Assert.True(player.CanMove);
        Assert.True(player.IsCombatEffective);
        Assert.False(player.IsDeployable);
    }

    [Fact]
    public void SeveredNonLimbLocation_DoesNotTriggerLimbIncapacity()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        PlayerSoldier player = new(soldier, soldier.Name);
        HitLocation eye = Find(soldier, "Eyes");

        eye.Wounds.AddWound(WoundLevel.Major);

        Assert.True(eye.IsSevered);
        Assert.False(player.HasUntreatedSeveredLimb);
        Assert.True(player.IsCombatEffective);
        Assert.True(player.IsDeployable);
    }

    // A crippled vital still bars deployment -- that half of the old rule is unchanged in effect,
    // it just routes through CanFight now.
    [Fact]
    public void CrippledVitalLocation_BarsDeployment()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        PlayerSoldier player = new(soldier, soldier.Name);
        HitLocation vital = soldier.Body.HitLocations
            .First(location => location.Template.IsVital && !location.Template.IsMotive);

        vital.Wounds.AddWound(WoundLevel.Massive);

        Assert.True(player.CanMove);
        Assert.False(player.IsDeployable);
    }
}
