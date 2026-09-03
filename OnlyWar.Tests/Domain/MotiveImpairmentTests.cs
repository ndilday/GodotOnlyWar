using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

// Pins the graded motive impairment curve introduced by Phase 3 of
// Design/Reference/CasualtyRealism.md §2.1. The rules it exists to protect:
//
//   * each wound band maps to a fixed multiplier (1.0 / 0.85 / 0.6 / 0)
//   * the two legs COMPOUND MULTIPLICATIVELY -- two bad legs are worse than one
//   * a foot NEVER reaches zero, crippled or severed; it floors, while untreated severance still
//     removes the soldier from battlefield combat effectiveness
//   * only a leg at Massive (its new cripple/sever threshold) fells a soldier
//   * CanMove is exactly "the product is above zero", not "nothing is crippled"
//
// The last of those is the load-bearing correctness point of the phase: if CanMove kept
// returning false below the top band, IsCombatEffective would still remove the man from the
// battle and none of the grading would be observable.
public class MotiveImpairmentTests
{
    private static HitLocation Find(Soldier soldier, string name) =>
        soldier.Body.HitLocations.Single(location => location.Template.Name == name);

    private static Soldier Wound(string locationName, WoundLevel level)
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        Find(soldier, locationName).Wounds.AddWound(level);
        return soldier;
    }

    // -- the band curve, leg by leg ------------------------------------------------------

    [Theory]
    [InlineData(WoundLevel.Negligible, 1f)]
    [InlineData(WoundLevel.Minor, 1f)]
    [InlineData(WoundLevel.Moderate, 1f)]
    [InlineData(WoundLevel.Major, 0.85f)]
    [InlineData(WoundLevel.Critical, 0.6f)]
    [InlineData(WoundLevel.Massive, 0f)]
    [InlineData(WoundLevel.Mortal, 0f)]
    [InlineData(WoundLevel.Unsurvivable, 0f)]
    public void LegWoundBand_MapsToItsMultiplier(WoundLevel level, float expected)
    {
        Soldier soldier = Wound("Left Leg", level);

        Assert.Equal(expected, soldier.MotiveSpeedMultiplier);
    }

    // The band, not the wound count, is what the curve reads: five Major wounds are still the
    // Major band (the sixth would promote to Critical and Normalize would fold it).
    [Fact]
    public void MultipleWoundsWithinOneBand_StayOnThatBandsMultiplier()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        HitLocation leg = Find(soldier, "Left Leg");
        for (int i = 0; i < 5; i++)
        {
            leg.Wounds.AddWound(WoundLevel.Major);
        }

        Assert.Equal(5, leg.Wounds.MajorWounds);
        Assert.Equal(CasualtyConstants.MajorMotiveSpeedMultiplier, soldier.MotiveSpeedMultiplier);
    }

    // -- compounding ---------------------------------------------------------------------

    [Fact]
    public void TwoWoundedLegs_CompoundMultiplicatively()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        Find(soldier, "Left Leg").Wounds.AddWound(WoundLevel.Critical);
        Find(soldier, "Right Leg").Wounds.AddWound(WoundLevel.Critical);

        Assert.Equal(0.6f * 0.6f, soldier.MotiveSpeedMultiplier, 5);
        // Slower than one bad leg, but emphatically still in the fight.
        Assert.True(soldier.CanMove);
        Assert.True(soldier.IsCombatEffective);
    }

    [Fact]
    public void MixedBandsAcrossLegs_CompoundMultiplicatively()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        Find(soldier, "Left Leg").Wounds.AddWound(WoundLevel.Major);
        Find(soldier, "Right Leg").Wounds.AddWound(WoundLevel.Critical);

        Assert.Equal(0.85f * 0.6f, soldier.MotiveSpeedMultiplier, 5);
        Assert.True(soldier.CanMove);
    }

    [Fact]
    public void EveryMotiveLocationWounded_CompoundsAcrossAllFour()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        Find(soldier, "Left Leg").Wounds.AddWound(WoundLevel.Major);
        Find(soldier, "Right Leg").Wounds.AddWound(WoundLevel.Major);
        Find(soldier, "Left Foot").Wounds.AddWound(WoundLevel.Moderate);
        Find(soldier, "Right Foot").Wounds.AddWound(WoundLevel.Moderate);

        // Moderate is below the feet's Major cripple threshold, so the feet contribute 1.0.
        Assert.Equal(0.85f * 0.85f, soldier.MotiveSpeedMultiplier, 5);
        Assert.True(soldier.CanMove);
    }

    // -- the foot floor: the load-bearing rule of the phase -------------------------------

    [Fact]
    public void CrippledFoot_FloorsRatherThanZeroing()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        HitLocation foot = Find(soldier, "Left Foot");
        foot.Wounds.AddWound(WoundLevel.Major);

        Assert.True(foot.IsCrippled);
        Assert.Equal(CasualtyConstants.ExtremitySpeedFloor, soldier.MotiveSpeedMultiplier);
        Assert.True(soldier.CanMove);
        Assert.True(soldier.IsCombatEffective);
    }

    [Fact]
    public void SeveredFoot_FloorsMovementButIncapacitatesTheSoldier()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        HitLocation foot = Find(soldier, "Right Foot");
        foot.Wounds = new Wounds(foot.Template.SeverWound, 0);

        Assert.True(foot.IsSevered);
        Assert.Equal(CasualtyConstants.ExtremitySpeedFloor, soldier.MotiveSpeedMultiplier);
        Assert.True(soldier.CanMove);
        Assert.True(soldier.HasUntreatedSeveredLimb);
        Assert.False(soldier.IsCombatEffective);
    }

    // Both feet retain a positive motive speed, but either untreated severance is enough to
    // incapacitate the soldier for battlefield purposes.
    [Fact]
    public void BothFeetSevered_RetainSpeedButIncapacitateTheSoldier()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        foreach (string name in new[] { "Left Foot", "Right Foot" })
        {
            HitLocation foot = Find(soldier, name);
            foot.Wounds = new Wounds(foot.Template.SeverWound, 0);
        }

        float floor = CasualtyConstants.ExtremitySpeedFloor;
        Assert.Equal(floor * floor, soldier.MotiveSpeedMultiplier, 5);
        Assert.True(soldier.MotiveSpeedMultiplier > 0f);
        Assert.True(soldier.CanMove);
        Assert.True(soldier.HasUntreatedSeveredLimb);
        Assert.False(soldier.IsCombatEffective);
    }

    // An unsurvivable-magnitude wound to a foot is still a foot. Nothing about the extremity
    // floor is band-dependent.
    [Fact]
    public void CatastrophicFootWound_StillFloors()
    {
        Soldier soldier = Wound("Left Foot", WoundLevel.Unsurvivable);

        Assert.Equal(CasualtyConstants.ExtremitySpeedFloor, soldier.MotiveSpeedMultiplier);
        Assert.True(soldier.CanMove);
    }

    // -- the felling boundary ------------------------------------------------------------

    // The single most important pair in this file: Critical keeps him fighting, Massive does not.
    [Fact]
    public void LegFellsOnlyAtMassive()
    {
        Soldier critical = Wound("Left Leg", WoundLevel.Critical);
        Assert.True(critical.CanMove);
        Assert.True(critical.IsCombatEffective);

        Soldier massive = Wound("Left Leg", WoundLevel.Massive);
        Assert.False(massive.CanMove);
        Assert.Equal(0f, massive.MotiveSpeedMultiplier);
        Assert.False(massive.IsCombatEffective);
        // He can still hold a bolter; there is simply no prone fire for him to use it from
        // (CasualtyRealism.md §2.2 cuts stance), so he leaves the battle.
        Assert.True(massive.CanFight);
    }

    [Fact]
    public void SeveredLeg_ZeroesSpeed()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        HitLocation leg = Find(soldier, "Right Leg");
        leg.Wounds = new Wounds(leg.Template.SeverWound, 0);

        Assert.True(leg.IsSevered);
        Assert.Equal(0f, soldier.MotiveSpeedMultiplier);
        Assert.False(soldier.CanMove);
    }

    // One ruined leg zeroes the product no matter how healthy everything else is: a zero factor
    // is absorbing.
    [Fact]
    public void OneMassiveLeg_ZeroesEvenWithHealthyRemainder()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        Find(soldier, "Left Leg").Wounds.AddWound(WoundLevel.Massive);
        Find(soldier, "Right Leg").Wounds.AddWound(WoundLevel.Minor);

        Assert.Equal(0f, soldier.MotiveSpeedMultiplier);
        Assert.False(soldier.CanMove);
    }

    // -- classification -------------------------------------------------------------------

    // The principal/extremity split is derived from the body's own cripple thresholds, not from
    // location names, so it keeps working for any authored body. Assert it holds for the human
    // body as shipped.
    [Fact]
    public void LegsArePrincipalAndFeetAreNot()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        Body body = soldier.Body;

        Assert.True(MotiveImpairment.IsPrincipal(body, Find(soldier, "Left Leg").Template));
        Assert.True(MotiveImpairment.IsPrincipal(body, Find(soldier, "Right Leg").Template));
        Assert.False(MotiveImpairment.IsPrincipal(body, Find(soldier, "Left Foot").Template));
        Assert.False(MotiveImpairment.IsPrincipal(body, Find(soldier, "Right Foot").Template));
        Assert.Equal((uint)WoundLevel.Massive, MotiveImpairment.GetPrincipalCrippleThreshold(body));
    }

    // A body whose only motive location cripples below Massive still has that location as its
    // principal one -- there is nothing else to stand on, so it can fell. This is the case the
    // rejected absolute-threshold rule got wrong (WoundResolverTests builds exactly such a body).
    [Fact]
    public void SoleMotiveLocation_IsPrincipalEvenBelowMassive()
    {
        HitLocationTemplate template = new()
        {
            Id = 0,
            Name = "Body",
            NaturalArmor = 0,
            WoundMultiplier = 1,
            HitProbabilityMap = [1, 1, 1],
            CrippleWound = (uint)WoundLevel.Critical,
            SeverWound = (uint)WoundLevel.Massive,
            IsMotive = true
        };
        Body body = new(new List<HitLocation> { new(template) });

        Assert.True(MotiveImpairment.IsPrincipal(body, template));
        Assert.Equal(1f, MotiveImpairment.CalculateSpeedMultiplier(body));

        body.HitLocations[0].Wounds.AddWound(WoundLevel.Critical);

        Assert.Equal(0f, MotiveImpairment.CalculateSpeedMultiplier(body));
    }

    // The hard-coded fallback templates must agree with
    // Database/RulesMigration_LegCrippleThreshold.sql, or a run without the rules DB fells
    // marines the loaded rules would only slow.
    [Theory]
    [InlineData("Left Leg")]
    [InlineData("Right Leg")]
    public void FallbackHumanTemplate_CripplesLegsAtMassive(string name)
    {
        HitLocationTemplate template = HumanBodyTemplate.Instance.HitLocations
            .Single(location => location.Name == name);

        Assert.Equal((uint)WoundLevel.Massive, template.CrippleWound);
    }

    [Theory]
    [InlineData("Left Leg")]
    [InlineData("Right Leg")]
    public void FallbackTyranidTemplate_CripplesLegsAtMassive(string name)
    {
        HitLocationTemplate template = TyranidWarriorBodyTemplate.Instance.HitLocations
            .Single(location => location.Name == name);

        Assert.Equal((uint)WoundLevel.Massive, template.CrippleWound);
    }

    [Theory]
    [InlineData("Left Foot")]
    [InlineData("Right Foot")]
    public void FallbackTemplates_LeaveFeetCripplingAtMajor(string name)
    {
        Assert.Equal(
            (uint)WoundLevel.Major,
            HumanBodyTemplate.Instance.HitLocations.Single(l => l.Name == name).CrippleWound);
        Assert.Equal(
            (uint)WoundLevel.Major,
            TyranidWarriorBodyTemplate.Instance.HitLocations.Single(l => l.Name == name).CrippleWound);
    }

    // Non-motive damage must not touch movement at all.
    [Fact]
    public void NonMotiveWounds_DoNotAffectSpeed()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        Find(soldier, "Left Arm").Wounds.AddWound(WoundLevel.Massive);
        Find(soldier, "Torso").Wounds.AddWound(WoundLevel.Critical);

        Assert.Equal(1f, soldier.MotiveSpeedMultiplier);
        Assert.True(soldier.CanMove);
    }
}
