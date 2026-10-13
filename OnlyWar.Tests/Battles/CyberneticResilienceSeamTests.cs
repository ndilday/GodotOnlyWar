using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Battles;
using OnlyWar.Battles.Resolutions;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// Per-location constitution and natural armor (OnlyWar_TDD.md §5.3,
/// settled 2026-10-12). Resolution reads a location's own values:
/// <see cref="HitLocation.EffectiveConstitution(ISoldier)"/> and <see cref="HitLocation.NaturalArmor"/>,
/// where a cybernetic part's armor replaces the template's. Natural armor is added to worn armor
/// and scaled by the weapon's armor multiplier (<see cref="RemovalMath.EffectiveArmor"/>). The
/// planner's estimate reads what a shooter can see: the species median constitution and
/// <see cref="HitLocation.PerceivedNaturalArmor"/> (the template's) for every location, so it
/// matches resolution for organic parts and deliberately ignores a cybernetic part's own values.
/// </summary>
public class CyberneticResilienceSeamTests
{
    private const int Constitution = 20;

    private static BattleSoldier Target(string name, int soldierId)
    {
        SoldierTemplate template = new(
            31_000 + soldierId,
            // Species median equal to the soldier's own, so the perceived constitution the
            // estimate reads and the real one resolution reads are the same number.
            TestModelFactory.HumanSpeciesWithConstitution(Constitution),
            $"{name} Template",
            1,
            1,
            false,
            0,
            Array.Empty<ValueTuple<BaseSkill, float>>(),
            battleValue: 10);
        Soldier soldier = TestModelFactory.CreateSoldier(template, name);
        soldier.Id = soldierId;
        soldier.Constitution = Constitution;
        BattleSquad squad = new(false, TestModelFactory.CreateSquad(name, soldier));
        return squad.Soldiers[0];
    }

    // A non-vital location that takes the soldier out of the fight, so it carries a take-out
    // term, and that a cybernetic replacement could fill.
    private static HitLocation MotiveLimb(BattleSoldier soldier) =>
        soldier.Soldier.Body.HitLocations.First(location =>
            location.Template.IsMotive && !location.Template.IsVital);

    // The test body's brain, which has natural armor 2 (its vitals do too).
    private static HitLocation ArmoredLocation(BattleSoldier soldier) =>
        soldier.Soldier.Body.HitLocations.Single(location =>
            location.Template.Name == "Brain" && location.Template.NaturalArmor > 0);

    private static HitLocationTemplate LocationTemplate(float naturalArmor) => new()
    {
        Id = 1,
        Name = "Test Location",
        NaturalArmor = naturalArmor,
        WoundMultiplier = 1,
        CrippleWound = (uint)WoundLevel.Critical,
        SeverWound = (uint)WoundLevel.Massive,
        HitProbabilityMap = [1, 1, 1]
    };

    [Fact]
    public void Constitution_IsThePartsOwnForAnInstalledAugmetic_AndTheSoldiersEverywhereElse()
    {
        BattleSoldier soldier = Target("Seam", 31_001);
        HitLocation cybernetic = MotiveLimb(soldier);
        HitLocation organic = soldier.Soldier.Body.HitLocations.First(location => location != cybernetic);
        cybernetic.InstallCybernetic(new CyberneticOption(1, "Standard Augmetic", 5f, 40f));

        Assert.False(organic.IsCybernetic);
        Assert.True(cybernetic.IsCybernetic);
        Assert.Equal(soldier.Soldier.Constitution, organic.EffectiveConstitution(soldier.Soldier));
        Assert.Equal(40f, cybernetic.EffectiveConstitution(soldier.Soldier));
        // The whole-soldier value ignores cybernetic parts by decision.
        Assert.Equal(
            soldier.Soldier.Constitution,
            RemovalMath.RepresentativeConstitution(soldier));
    }

    [Fact]
    public void ACyberneticPartWithNoConstitutionOfItsOwn_UsesTheSoldiers()
    {
        // A part from an older save, or installed with no rules option, records no value.
        BattleSoldier soldier = Target("Legacy", 31_009);
        HitLocation cybernetic = MotiveLimb(soldier);
        cybernetic.IsCybernetic = true;

        Assert.Equal(soldier.Soldier.Constitution, cybernetic.EffectiveConstitution(soldier.Soldier));
    }

    [Fact]
    public void ResolutionMeasuresAWoundToAnAugmetic_AgainstThePartsConstitution()
    {
        // The same penetrating damage that is Critical against the soldier's 20 is only Major
        // against the part's 40.
        BattleSoldier organic = Target("Organic", 31_010);
        BattleSoldier augmented = Target("Augmented", 31_011);
        MotiveLimb(augmented).InstallCybernetic(new CyberneticOption(1, "Standard Augmetic", 0f, 40f));
        float damage = Constitution * 1.5f / MotiveLimb(organic).Template.WoundMultiplier;

        Assert.Equal(WoundLevel.Critical, ResolveOn(organic, damage));
        Assert.Equal(WoundLevel.Major, ResolveOn(augmented, damage));

        static WoundLevel ResolveOn(BattleSoldier target, float damage)
        {
            HitLocation location = MotiveLimb(target);
            WoundResolver resolver = new();
            resolver.OnSoldierFall += (_, _) => { };
            resolver.OnSoldierDeath += (_, _) => { };
            resolver.WoundQueue.Add(new WoundResolution(null, null, target, damage, location));
            resolver.Resolve();
            return location.Wounds.WorstLevel;
        }
    }

    [Fact]
    public void NaturalArmor_IsTheTemplates_UnlessAnInstalledCyberneticPartReplacesIt()
    {
        HitLocationTemplate template = LocationTemplate(naturalArmor: 2);
        HitLocation organic = new(template, isCybernetic: false, armor: 4, woundTotal: 0, weeksOfHealing: 0);
        HitLocation cybernetic = new(template, isCybernetic: true, armor: 4, woundTotal: 0, weeksOfHealing: 0);
        HitLocation severed = new(template, isCybernetic: true, armor: 4,
            woundTotal: (uint)WoundLevel.Massive, weeksOfHealing: 0);

        Assert.Equal(2f, organic.NaturalArmor);
        // Replaces the template's 2, does not add to it.
        Assert.Equal(4f, cybernetic.NaturalArmor);
        // A severed augmetic is no longer cybernetic, so it falls back to the template.
        Assert.Equal(2f, severed.NaturalArmor);
        Assert.All(new[] { organic, cybernetic, severed },
            location => Assert.Equal(2f, location.PerceivedNaturalArmor));
    }

    [Fact]
    public void EffectiveArmor_ScalesWornAndNaturalArmorTogether()
    {
        Assert.Equal(2.5f, RemovalMath.EffectiveArmor(3f, 2f, 0.5f));
        Assert.Equal(1.5f, RemovalMath.EffectiveArmor(3f, 0f, 0.5f));
    }

    [Fact]
    public void EstimateAndResolution_Agree_ForAnOrganicLocationWithNaturalArmor()
    {
        // The estimate says a hit at ZeroProgressThreshold + DamagePerRatio * r wounds the location
        // at ratio r, and that nothing at or below EffectiveArmor wounds it. Drive the resolver
        // the way an attack action does and check it agrees on both edges.
        const float wornArmor = 3f;
        const float armorMultiplier = 0.5f;
        const float weaponWoundMultiplier = 2f;
        BattleSoldier target = Target("Organic", 31_002);
        HitLocation armored = ArmoredLocation(target);
        float locationArmor = RemovalMath.EffectiveArmor(
            wornArmor, armored.NaturalArmor, armorMultiplier);

        IReadOnlyList<TakeOutLocationTerm> terms = RemovalMath.BuildTakeOutLocationTerms(
            target, wornArmor, armorMultiplier, weaponWoundMultiplier);
        float damagePerRatio = Constitution / armored.Template.WoundMultiplier / weaponWoundMultiplier;
        TakeOutLocationTerm term = Assert.Single(terms, candidate =>
            candidate.EffectiveArmor == locationArmor && candidate.DamagePerRatio == damagePerRatio);
        Assert.Equal(2.5f, term.EffectiveArmor);
        Assert.Equal(term.EffectiveArmor, term.ZeroProgressThreshold);

        Assert.Null(Resolve(31_003, term.EffectiveArmor));
        Assert.Equal(WoundLevel.Negligible, Resolve(31_004, term.EffectiveArmor + 0.01f));
        float criticalDamage = term.ZeroProgressThreshold + term.DamagePerRatio;
        Assert.Equal(WoundLevel.Critical, Resolve(31_005, criticalDamage + 0.01f));
        Assert.Equal(WoundLevel.Major, Resolve(31_006, criticalDamage - 0.01f));

        // The action's arithmetic: penetrate worn + natural armor, then the weapon multiplier.
        WoundLevel? Resolve(int soldierId, float damage)
        {
            BattleSoldier sufferer = Target("Sufferer", soldierId);
            HitLocation location = ArmoredLocation(sufferer);
            float penetrating = damage - RemovalMath.EffectiveArmor(
                wornArmor, location.NaturalArmor, armorMultiplier);
            if (penetrating <= 0)
            {
                return null;
            }
            WoundResolver resolver = new();
            resolver.OnSoldierFall += (_, _) => { };
            resolver.OnSoldierDeath += (_, _) => { };
            resolver.WoundQueue.Add(new WoundResolution(
                null, null, sufferer, penetrating * weaponWoundMultiplier, location));
            resolver.Resolve();
            return (WoundLevel)location.Wounds.WoundTotal;
        }
    }

    [Fact]
    public void Estimate_DeliberatelyIgnoresACyberneticPartsOwnArmor()
    {
        // A bionic limb inside power armor is not visible: the estimate keeps the template's
        // natural armor and the species median constitution for it, while resolution reads the
        // part's own armor.
        BattleSoldier organic = Target("Organic", 31_007);
        BattleSoldier augmented = Target("Augmented", 31_008);
        HitLocation part = MotiveLimb(augmented);
        part.IsCybernetic = true;
        part.Armor = 7f;

        Assert.Equal(7f, part.NaturalArmor);
        Assert.Equal(part.Template.NaturalArmor, part.PerceivedNaturalArmor);

        TakeOutLocationTerm[] expected = RemovalMath.BuildUnitTakeOutTerms(organic);
        TakeOutLocationTerm[] actual = RemovalMath.BuildUnitTakeOutTerms(augmented);
        Assert.NotEmpty(expected);
        Assert.Equal(expected.Length, actual.Length);
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index].PenetrationThreshold, actual[index].PenetrationThreshold);
            Assert.Equal(expected[index].ZeroProgressThreshold, actual[index].ZeroProgressThreshold);
            Assert.Equal(expected[index].NaturalArmor, actual[index].NaturalArmor);
            Assert.Equal(expected[index].DamagePerRatio, actual[index].DamagePerRatio);
            Assert.Equal(expected[index].RequiredRatio, actual[index].RequiredRatio);
        }
    }
}
