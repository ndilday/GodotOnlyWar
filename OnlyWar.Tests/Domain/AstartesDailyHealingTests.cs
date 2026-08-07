using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace OnlyWar.Tests.Domain;

/// <summary>
/// Phase 1b of Design/Reference/CasualtyRealism.md (§2.5): an Astartes sheds his Negligible wounds
/// overnight, so a day's worth of grazes cannot compound into a real wound -- while a single
/// battle's worth still does, because a battle resolves inside one day and the pass never runs
/// during one.
/// </summary>
public class AstartesDailyHealingTests
{
    [Fact]
    public void DailyPass_ClearsNegligibleWoundsForAstartes()
    {
        Soldier marine = CreateAstartes();
        HitLocation location = FirstLocation(marine);
        location.Wounds.AddWound(WoundLevel.Negligible);
        location.Wounds.AddWound(WoundLevel.Negligible);

        MedicalTurnProcessor.ApplyDailyHealing(marine);

        Assert.Equal(0u, location.Wounds.WoundTotal);
    }

    [Fact]
    public void DailyPass_LeavesMinorAndAboveOnTheWeeklyCascade()
    {
        Soldier marine = CreateAstartes();
        HitLocation location = FirstLocation(marine);
        location.Wounds.AddWound(WoundLevel.Negligible);
        location.Wounds.AddWound(WoundLevel.Minor);
        location.Wounds.AddWound(WoundLevel.Major);
        uint before = location.Wounds.WoundTotal;
        uint clockBefore = location.Wounds.WeeksOfHealing;

        MedicalTurnProcessor.ApplyDailyHealing(marine);

        // Only the bottom nibble moved; the Major wound and every band clock are untouched.
        Assert.Equal(before & 0xfffffff0, location.Wounds.WoundTotal);
        Assert.Equal((byte)1, location.Wounds.MinorWounds);
        Assert.Equal((byte)1, location.Wounds.MajorWounds);
        Assert.Equal(clockBefore, location.Wounds.WeeksOfHealing);
    }

    [Fact]
    public void DailyPass_DoesNothingForSpeciesWithoutAcceleratedHealing()
    {
        Soldier trooper = TestModelFactory.CreateSoldier(TestModelFactory.MarineTemplate, "Trooper");
        HitLocation location = FirstLocation(trooper);
        location.Wounds.AddWound(WoundLevel.Negligible);

        MedicalTurnProcessor.ApplyDailyHealing(trooper);

        Assert.Equal((byte)1, location.Wounds.NegligibleWounds);
    }

    [Fact]
    public void DailyPass_IsIdempotentSoRunningItTwiceCostsNothing()
    {
        // The whole seam design leans on this: the mission day loop and the weekly upkeep pass
        // both invoke it without either knowing what the other did.
        Soldier marine = CreateAstartes();
        HitLocation location = FirstLocation(marine);
        location.Wounds.AddWound(WoundLevel.Moderate);
        location.Wounds.AddWound(WoundLevel.Negligible);

        MedicalTurnProcessor.ApplyDailyHealing(marine);
        uint afterFirst = location.Wounds.WoundTotal;
        uint clockAfterFirst = location.Wounds.WeeksOfHealing;
        MedicalTurnProcessor.ApplyDailyHealing(marine);

        Assert.Equal(afterFirst, location.Wounds.WoundTotal);
        Assert.Equal(clockAfterFirst, location.Wounds.WeeksOfHealing);
    }

    [Fact]
    public void ABattlesWorthOfGrazesStillPromotes_ADaysWorthNoLongerDoes()
    {
        // The boundary that makes §2.5 meaningful rather than cosmetic. Wounds.Normalize folds a
        // band once it exceeds WOUND_MAX (5), so the sixth Negligible graze becomes one Minor
        // wound. Inside a single engagement nothing clears them, so they still compound - and the
        // Minor wound that results is real and survives the daily pass. Spread over separate days
        // with the pass in between, the same six grazes never add up to anything.
        const int grazesToPromote = Wounds.WOUND_MAX + 1;

        Soldier inOneBattle = CreateAstartes();
        HitLocation battleLocation = FirstLocation(inOneBattle);
        for (int hit = 0; hit < grazesToPromote; hit++)
        {
            battleLocation.Wounds.AddWound(WoundLevel.Negligible);
        }

        Assert.Equal((byte)1, battleLocation.Wounds.MinorWounds);
        MedicalTurnProcessor.ApplyDailyHealing(inOneBattle);
        Assert.Equal((byte)1, battleLocation.Wounds.MinorWounds);

        Soldier overSeparateDays = CreateAstartes();
        HitLocation dayLocation = FirstLocation(overSeparateDays);
        for (int day = 0; day < grazesToPromote; day++)
        {
            dayLocation.Wounds.AddWound(WoundLevel.Negligible);
            MedicalTurnProcessor.ApplyDailyHealing(overSeparateDays);
        }

        Assert.Equal(0u, dayLocation.Wounds.WoundTotal);
    }

    [Fact]
    public void DailyPass_LeavesASeveredLocationAlone()
    {
        Soldier marine = CreateAstartes();
        HitLocation hand = marine.Body.HitLocations.First(
            location => location.Template.Name == "Left Hand");
        hand.Wounds.AddWound(WoundLevel.Critical);
        hand.Wounds.AddWound(WoundLevel.Negligible);
        uint before = hand.Wounds.WoundTotal;

        MedicalTurnProcessor.ApplyDailyHealing(marine);

        Assert.True(hand.IsSevered);
        Assert.Equal(before, hand.Wounds.WoundTotal);
    }

    [Fact]
    public void GarrisonUpkeep_RunsTheDailyPassOverTheWholeRoster()
    {
        // Days outside a mission still get the pass. A collection overload exists precisely so the
        // upkeep sweep and the mission day loop share one implementation.
        Soldier astartes = CreateAstartes();
        Soldier trooper = TestModelFactory.CreateSoldier(TestModelFactory.MarineTemplate, "Trooper");
        FirstLocation(astartes).Wounds.AddWound(WoundLevel.Negligible);
        FirstLocation(trooper).Wounds.AddWound(WoundLevel.Negligible);

        MedicalTurnProcessor.ApplyDailyHealing(new List<ISoldier> { astartes, null, trooper });

        Assert.Equal(0u, FirstLocation(astartes).Wounds.WoundTotal);
        Assert.Equal((byte)1, FirstLocation(trooper).Wounds.NegligibleWounds);
    }

    private static HitLocation FirstLocation(ISoldier soldier) =>
        soldier.Body.HitLocations.First(location => location.Template.Name == "Torso");

    // A local Astartes species so the shared fixture's process-wide statics stay untouched.
    private static readonly Species _astartesSpecies = new(
        900,
        "Test Astartes",
        Value(10), Value(10), Value(10), Value(10), Value(10), Value(10), Value(10),
        Value(0), Value(10), Value(6), Value(1),
        1,
        1,
        0f,
        0f,
        SpeciesAbilities.AcceleratedHealing,
        HumanBodyTemplate.Instance,
        TestModelFactory.DefaultUnarmedWeapon);

    private static readonly SoldierTemplate _astartesTemplate = new(
        900,
        _astartesSpecies,
        "Test Astartes",
        1,
        1,
        false,
        0,
        Array.Empty<ValueTuple<BaseSkill, float>>(),
        battleValue: 2);

    private static Soldier CreateAstartes() =>
        TestModelFactory.CreateSoldier(_astartesTemplate, "Brother Grazed");

    private static NormalizedValueTemplate Value(float value) =>
        new() { BaseValue = value, StandardDeviation = 0 };
}
