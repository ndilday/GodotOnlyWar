using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models.Soldiers;
using Xunit;

namespace OnlyWar.Tests.Domain;

// Characterization tests for the natural-healing cadence in Wounds.ApplyWeekOfHealing.
// These pin down what the model ACTUALLY does today, against what RecoveryTimeLeft()
// advertises, so the medical/apothecary design work can build on a measured number.
public class WoundHealingCadenceTests
{
    // Steps a lone wound of the given level to zero, returning the number of weekly passes
    // required and the band it occupied after each pass.
    private static (int Weeks, List<uint> Trace) HealToZero(WoundLevel level, int cap = 60)
    {
        Wounds wounds = new(0, 0);
        wounds.AddWound(level);
        List<uint> trace = [];
        int weeks = 0;
        while (wounds.WoundTotal > 0 && weeks < cap)
        {
            wounds.ApplyWeekOfHealing();
            weeks++;
            trace.Add(wounds.WoundTotal);
        }
        return (weeks, trace);
    }

    [Theory]
    [InlineData(WoundLevel.Moderate)]
    [InlineData(WoundLevel.Major)]
    [InlineData(WoundLevel.Critical)]
    [InlineData(WoundLevel.Massive)]
    [InlineData(WoundLevel.Mortal)]
    [InlineData(WoundLevel.Unsurvivable)]
    public void ActualHealingTime_MatchesAdvertisedRecoveryTimeLeft(WoundLevel level)
    {
        Wounds fresh = new(0, 0);
        fresh.AddWound(level);
        int advertised = fresh.RecoveryTimeLeft();

        (int actual, List<uint> trace) = HealToZero(level);

        Assert.True(
            actual == advertised,
            $"{level}: RecoveryTimeLeft() advertises {advertised} weeks, actual healing took "
            + $"{actual}. Per-pass WoundTotal trace: [{string.Join(", ", trace)}]");
    }

    // A step-down deposits its wounds into a band that may already be occupied. The packed model
    // treats WOUND_MAX+1 wounds in a band as one wound of the band above, and AddWound enforces
    // that -- but the healing path never has. Demoting 3 Critical wounds onto 3 existing Major
    // wounds must not leave 6 Major wounds sitting in a nibble the rest of the model considers
    // impossible.
    [Fact]
    public void StepDownDoesNotLeaveABandOverItsMaximum()
    {
        Wounds wounds = new(0, 0);
        for (int i = 0; i < 3; i++)
        {
            wounds.AddWound(WoundLevel.Critical);
        }
        for (int i = 0; i < 3; i++)
        {
            wounds.AddWound(WoundLevel.Major);
        }
        Assert.Equal(3, wounds.CriticalWounds);
        Assert.Equal(3, wounds.MajorWounds);

        List<string> trace = [];
        for (int week = 1; week <= 12; week++)
        {
            wounds.ApplyWeekOfHealing();
            trace.Add($"w{week}: {wounds.CriticalWounds}C/{wounds.MajorWounds}Mj/"
                + $"{wounds.ModerateWounds}Mo/{wounds.MinorWounds}Mi");
            Assert.True(
                wounds.MajorWounds <= Wounds.WOUND_MAX
                && wounds.ModerateWounds <= Wounds.WOUND_MAX
                && wounds.CriticalWounds <= Wounds.WOUND_MAX,
                $"A band exceeded WOUND_MAX during healing. Trace: [{string.Join("; ", trace)}]");
        }
    }

    // Because each band's dwell time is one week shorter than the band above it, and all occupied
    // bands run their clocks together, a band always empties a week before the band above steps
    // down into it. A full-house location is the hardest case: five wounds in every band at once.
    [Fact]
    public void NoBandEverExceedsItsMaximum_EvenFromAFullHouse()
    {
        Wounds wounds = new(0, 0);
        foreach (WoundLevel level in new[]
        {
            WoundLevel.Moderate, WoundLevel.Major, WoundLevel.Critical,
            WoundLevel.Massive, WoundLevel.Mortal, WoundLevel.Unsurvivable
        })
        {
            for (int i = 0; i < 5; i++)
            {
                wounds.AddWound(level);
            }
        }

        List<string> trace = [];
        for (int week = 1; week <= 40 && wounds.WoundTotal > 0; week++)
        {
            wounds.ApplyWeekOfHealing();
            trace.Add($"w{week}:0x{wounds.WoundTotal:x}");
            for (int shift = 0; shift < 32; shift += 4)
            {
                Assert.True(
                    ((wounds.WoundTotal >> shift) & 0xf) <= Wounds.WOUND_MAX,
                    $"Band at shift {shift} exceeded WOUND_MAX. Trace: [{string.Join("; ", trace)}]");
            }
        }
        Assert.Equal(0u, wounds.WoundTotal);
    }

    // Normalization's remaining job: AddWound piling wounds into one band folds them upward, so
    // the packed total stays a valid magnitude for the cripple/sever/CanFight comparisons.
    [Fact]
    public void AddWoundFoldsAnOverfullBandIntoTheBandAbove()
    {
        Wounds wounds = new(0, 0);
        for (int i = 0; i < 6; i++)
        {
            wounds.AddWound(WoundLevel.Major);
        }

        Assert.Equal(1, wounds.CriticalWounds);
        Assert.Equal(0, wounds.MajorWounds);
    }

    // Worked example: a marine comes off the field with three Critical and three Major wounds in
    // one location, no healing yet. Wounds are discrete injuries and all of them convalesce at the
    // same time, so the Major wounds must NOT sit frozen waiting on the Critical ones.
    [Fact]
    public void MultiWoundLocation_ConvalescesOnADocumentedTimeline()
    {
        Wounds wounds = new(0, 0);
        for (int i = 0; i < 3; i++)
        {
            wounds.AddWound(WoundLevel.Critical);
        }
        for (int i = 0; i < 3; i++)
        {
            wounds.AddWound(WoundLevel.Major);
        }

        List<string> actual = [];
        for (int week = 1; week <= 10; week++)
        {
            wounds.ApplyWeekOfHealing();
            actual.Add($"w{week}:{wounds.CriticalWounds}C/{wounds.MajorWounds}Mj/"
                + $"{wounds.ModerateWounds}Mo/{wounds.MinorWounds}Mi");
        }

        string[] expected =
        [
            "w1:3C/3Mj/0Mo/0Mi",
            "w2:3C/3Mj/0Mo/0Mi",
            "w3:3C/0Mj/3Mo/0Mi",   // the Major wounds step down on their own three-week clock
            "w4:0C/3Mj/3Mo/0Mi",   // the Critical wounds follow one week later, onto an empty band
            "w5:0C/3Mj/0Mo/3Mi",
            "w6:0C/3Mj/0Mo/0Mi",
            "w7:0C/0Mj/3Mo/0Mi",
            "w8:0C/0Mj/3Mo/0Mi",
            "w9:0C/0Mj/0Mo/3Mi",
            "w10:0C/0Mj/0Mo/0Mi",
        ];

        Assert.True(
            expected.SequenceEqual(actual),
            $"Expected [{string.Join("; ", expected)}]\nActual   [{string.Join("; ", actual)}]");
    }

    [Fact]
    public void DemotionDoesNotCascadeThroughMultipleBandsInOnePass()
    {
        Wounds wounds = new(0, 0);
        wounds.AddWound(WoundLevel.Major);

        // Three passes is the advertised dwell time at Major; the wound should arrive at
        // Moderate and stay there, not fall straight through to Minor in the same pass.
        wounds.ApplyWeekOfHealing();
        wounds.ApplyWeekOfHealing();
        wounds.ApplyWeekOfHealing();

        Assert.True(
            wounds.ModerateWounds == 1 && wounds.MinorWounds == 0,
            $"Expected exactly one Moderate wound after the Major stepped down, but found "
            + $"{wounds.MajorWounds} Major / {wounds.ModerateWounds} Moderate / "
            + $"{wounds.MinorWounds} Minor (WoundTotal 0x{wounds.WoundTotal:x}).");
    }
}
