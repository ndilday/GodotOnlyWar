using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Resolutions;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

// The battle-layer half of Phase 3 (Design/Active/CasualtyRealism.md §2.1): WoundResolver's
// OnSoldierFall hook, and BattleSoldier's move speed.
//
// WoundResolver.HandleWound fires OnSoldierFall when a location crosses into crippled/severed.
// Its motive branch used to fire unconditionally, relying on the (then true) equivalence
// "a crippled motive location means !CanMove means !IsCombatEffective". Phase 3 broke that
// equivalence in both directions -- a crippled foot no longer stops anyone, and a leg no longer
// cripples until Massive -- so the branch now tests IsCombatEffective explicitly. These tests
// pin that boundary, because getting it wrong decides whether a man leaves the fight.
//
// Test soldiers have Constitution 10 and their legs/feet have NaturalArmor 0 and
// WoundMultiplier 1, so damage/10 is the wound ratio: 5 -> Major, 10 -> Critical, 20 -> Massive.
public class MotiveFallBoundaryTests
{
    private static HitLocation Find(Soldier soldier, string name) =>
        soldier.Body.HitLocations.Single(location => location.Template.Name == name);

    private static List<WoundLevel> ResolveHit(
        BattleSoldier target, Soldier soldier, string locationName, float damage)
    {
        List<WoundLevel> falls = [];
        WoundResolver resolver = new();
        resolver.OnSoldierFall += (_, level) => falls.Add(level);
        resolver.OnSoldierDeath += (_, _) => { };
        resolver.WoundQueue.Add(new WoundResolution(
            null, null, target, damage, Find(soldier, locationName)));
        resolver.Resolve();
        return falls;
    }

    // A Critical leg wound is the case the whole plan exists for. Under the old thresholds it
    // crippled the leg and ended his battle; now it is a limp.
    [Fact]
    public void CriticalLegWound_DoesNotFellTheSoldier()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        BattleSoldier battleSoldier = new(soldier, null);

        List<WoundLevel> falls = ResolveHit(battleSoldier, soldier, "Left Leg", 10f);

        Assert.Empty(falls);
        Assert.True(battleSoldier.IsCombatEffective);
        Assert.Equal(
            CasualtyConstants.CriticalMotiveSpeedMultiplier,
            battleSoldier.MotiveSpeedMultiplier);
    }

    [Fact]
    public void MassiveLegWound_FellsTheSoldier()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        BattleSoldier battleSoldier = new(soldier, null);

        List<WoundLevel> falls = ResolveHit(battleSoldier, soldier, "Left Leg", 20f);

        Assert.Equal(WoundLevel.Massive, Assert.Single(falls));
        Assert.False(battleSoldier.CanMove);
        Assert.False(battleSoldier.IsCombatEffective);
        Assert.True(battleSoldier.CanFight);
    }

    // The explicit IsCombatEffective test earns its keep here: the foot DOES cross into
    // crippled, so the old unconditional motive branch would have reported a fall and pulled a
    // man out of the battle who is still walking.
    [Fact]
    public void CrippledFoot_DoesNotFellTheSoldier()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        BattleSoldier battleSoldier = new(soldier, null);

        List<WoundLevel> falls = ResolveHit(battleSoldier, soldier, "Left Foot", 5f);

        Assert.True(Find(soldier, "Left Foot").IsCrippled);
        Assert.Empty(falls);
        Assert.True(battleSoldier.CanMove);
        Assert.True(battleSoldier.IsCombatEffective);
        Assert.Equal(CasualtyConstants.ExtremitySpeedFloor, battleSoldier.MotiveSpeedMultiplier);
    }

    [Fact]
    public void SeveredFoot_DoesNotFellTheSoldier()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        BattleSoldier battleSoldier = new(soldier, null);

        List<WoundLevel> falls = ResolveHit(battleSoldier, soldier, "Right Foot", 10f);

        Assert.True(Find(soldier, "Right Foot").IsSevered);
        Assert.Empty(falls);
        Assert.True(battleSoldier.IsCombatEffective);
    }

    // Two Critical legs compound to 0.36 and still leave him in the fight. Neither hit fires the
    // fall hook.
    [Fact]
    public void TwoCriticalLegs_CompoundWithoutFelling()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        BattleSoldier battleSoldier = new(soldier, null);

        Assert.Empty(ResolveHit(battleSoldier, soldier, "Left Leg", 10f));
        Assert.Empty(ResolveHit(battleSoldier, soldier, "Right Leg", 10f));

        Assert.True(battleSoldier.IsCombatEffective);
        Assert.Equal(0.6f * 0.6f, battleSoldier.MotiveSpeedMultiplier, 5);
    }

    // GetMoveSpeed is now base speed times the product, replacing the old flat x0.75.
    [Fact]
    public void GetMoveSpeed_ScalesByTheMotiveMultiplier()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        BattleSoldier battleSoldier = new(soldier, null);
        Assert.Equal(soldier.MoveSpeed, battleSoldier.GetMoveSpeed());

        ResolveHit(battleSoldier, soldier, "Left Leg", 5f);

        Assert.Equal(
            soldier.MoveSpeed * CasualtyConstants.MajorMotiveSpeedMultiplier,
            battleSoldier.GetMoveSpeed(),
            4);
    }

    // A Major leg wound is below every threshold that matters: no cripple, no fall, just slower.
    [Fact]
    public void MajorLegWound_OnlySlows()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        BattleSoldier battleSoldier = new(soldier, null);

        List<WoundLevel> falls = ResolveHit(battleSoldier, soldier, "Left Leg", 5f);

        Assert.Empty(falls);
        Assert.False(Find(soldier, "Left Leg").IsCrippled);
        Assert.Equal(
            CasualtyConstants.MajorMotiveSpeedMultiplier,
            battleSoldier.MotiveSpeedMultiplier);
    }
}
