using System.Collections.Generic;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Soldiers;
using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// Pins <see cref="RemovalMath.FindMinimumDisablingWoundRatio"/> against a deliberately naive
/// reference.
///
/// <para>WHY THIS EXISTS. The shipping carry loop is bounded at both ends: it starts at the added
/// wound's own nibble and stops at the first band that does not overflow. Both bounds are only
/// valid because the incoming wound total is normalized, and getting either one wrong is close to
/// invisible -- the first attempt started the scan at nibble 0, which made it break immediately on
/// any wound above Negligible and silently stop promoting. That produced no exception and no
/// obviously wrong number, just a take-out probability that quietly ignored accumulated damage.
/// The reference below sweeps every nibble unconditionally, the way Wounds.Normalize does.</para>
/// </summary>
public class WoundLadderTests
{
    private static readonly WoundLevel[] Bands =
    [
        WoundLevel.Negligible,
        WoundLevel.Minor,
        WoundLevel.Moderate,
        WoundLevel.Major,
        WoundLevel.Critical,
        WoundLevel.Massive,
        WoundLevel.Mortal,
        WoundLevel.Unsurvivable
    ];

    private static readonly (WoundLevel Level, float Ratio)[] Ladder =
    [
        (WoundLevel.Negligible, 0f),
        (WoundLevel.Minor, 0.125f),
        (WoundLevel.Moderate, 0.25f),
        (WoundLevel.Major, 0.5f),
        (WoundLevel.Critical, 1f),
        (WoundLevel.Massive, 2f),
        (WoundLevel.Mortal, 4f),
        (WoundLevel.Unsurvivable, 8f)
    ];

    private static bool IsNormalized(uint woundTotal)
    {
        for (int shift = 0; shift < 32; shift += 4)
        {
            if (((woundTotal >> shift) & 0xfu) > Wounds.WOUND_MAX)
            {
                return false;
            }
        }
        return true;
    }

    // The unbounded form: sweep every nibble from 0, carrying one wound up whenever a band holds
    // more than WOUND_MAX. No early start, no early exit.
    private static uint AddWoundReference(uint currentWounds, WoundLevel wound)
    {
        uint total = currentWounds + (uint)wound;
        for (int nibble = 0; nibble < 7; nibble++)
        {
            int shift = nibble * 4;
            if (((total >> shift) & 0xfu) <= Wounds.WOUND_MAX)
            {
                continue;
            }
            total &= ~(0xfu << shift);
            total += 1u << (shift + 4);
        }
        return total;
    }

    private static float FindMinimumDisablingWoundRatioReference(
        uint currentWounds,
        uint disableThreshold)
    {
        foreach ((WoundLevel level, float ratio) in Ladder)
        {
            if (AddWoundReference(currentWounds, level) >= disableThreshold)
            {
                return ratio;
            }
        }
        return float.PositiveInfinity;
    }

    /// <summary>
    /// Every normalized wound total reachable by filling one band to each legal occupancy, against
    /// every band's disable threshold.
    /// </summary>
    public static TheoryData<uint, uint> NormalizedTotalsAndThresholds()
    {
        TheoryData<uint, uint> data = [];
        List<uint> totals = [0u];
        foreach (WoundLevel band in Bands)
        {
            for (uint count = 1; count <= Wounds.WOUND_MAX; count++)
            {
                totals.Add(count * (uint)band);
            }
        }

        // Mixed occupancy too: a body that has taken hits across several bands is the ordinary
        // mid-battle case, and it is where a carry can cascade through more than one band. Every
        // pair of DISTINCT bands, so no band is filled past WOUND_MAX by the combination itself.
        for (int low = 0; low < Bands.Length; low++)
        {
            for (int high = low + 1; high < Bands.Length; high++)
            {
                totals.Add(
                    ((uint)Bands[low] * Wounds.WOUND_MAX)
                        + ((uint)Bands[high] * Wounds.WOUND_MAX));
            }
        }

        foreach (uint total in totals)
        {
            // The precondition the bounded carry rests on. Asserted rather than assumed: an
            // unnormalized total is outside the contract, and a case that smuggled one in would be
            // testing behaviour nothing in the engine can produce.
            Assert.True(IsNormalized(total), $"test data 0x{total:x} is not normalized");

            foreach (WoundLevel threshold in Bands)
            {
                data.Add(total, (uint)threshold);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(NormalizedTotalsAndThresholds))]
    public void FindMinimumDisablingWoundRatio_MatchesFullScanReference(
        uint currentWounds,
        uint disableThreshold)
    {
        Assert.Equal(
            FindMinimumDisablingWoundRatioReference(currentWounds, disableThreshold),
            RemovalMath.FindMinimumDisablingWoundRatio(currentWounds, disableThreshold));
    }

    /// <summary>
    /// The property the optimization must not break, stated directly rather than by reference: a
    /// location that already carries damage needs no MORE than a fresh one to be disabled.
    /// </summary>
    [Theory]
    [MemberData(nameof(NormalizedTotalsAndThresholds))]
    public void FindMinimumDisablingWoundRatio_IsNonIncreasingInAccumulatedWounds(
        uint currentWounds,
        uint disableThreshold)
    {
        float fresh = RemovalMath.FindMinimumDisablingWoundRatio(0u, disableThreshold);
        float wounded = RemovalMath.FindMinimumDisablingWoundRatio(currentWounds, disableThreshold);
        Assert.True(
            wounded <= fresh,
            $"wounds 0x{currentWounds:x} against threshold 0x{disableThreshold:x}: "
                + $"{wounded} should not exceed the fresh requirement {fresh}");
    }
}
