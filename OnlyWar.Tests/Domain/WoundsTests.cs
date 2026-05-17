using OnlyWar.Models.Soldiers;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class WoundsTests
{
    [Fact]
    public void AddWound_IncrementsExpectedSeverityNibble()
    {
        Wounds wounds = new(0, 0);

        wounds.AddWound(WoundLevel.Major);

        Assert.Equal(1, wounds.MajorWounds);
        Assert.Equal((uint)WoundLevel.Major, wounds.WoundTotal);
        Assert.Equal(6, wounds.RecoveryTimeLeft());
    }

    [Fact]
    public void AddWound_RollsSixWoundsIntoNextSeverity()
    {
        Wounds wounds = new(0, 0);

        for (int i = 0; i < 6; i++)
        {
            wounds.AddWound(WoundLevel.Minor);
        }

        Assert.Equal(0, wounds.MinorWounds);
        Assert.Equal(1, wounds.ModerateWounds);
    }

    [Fact]
    public void AddWound_ResetsHealingProgress()
    {
        Wounds wounds = new((uint)WoundLevel.Major, 0x00002000);

        wounds.AddWound(WoundLevel.Minor);

        Assert.Equal((uint)0, wounds.WeeksOfHealing);
    }

    [Fact]
    public void ApplyWeekOfHealing_ClearsNegligibleAndMinorWounds()
    {
        Wounds wounds = new((uint)WoundLevel.Negligible + (uint)WoundLevel.Minor, 0);

        wounds.ApplyWeekOfHealing();

        Assert.Equal(0, wounds.NegligibleWounds);
        Assert.Equal(0, wounds.MinorWounds);
        Assert.Equal(0, wounds.RecoveryTimeLeft());
    }

    [Theory]
    [InlineData(WoundLevel.Moderate, 3)]
    [InlineData(WoundLevel.Major, 6)]
    [InlineData(WoundLevel.Critical, 10)]
    [InlineData(WoundLevel.Massive, 15)]
    [InlineData(WoundLevel.Mortal, 21)]
    [InlineData(WoundLevel.Unsurvivable, 28)]
    public void RecoveryTimeLeft_ReportsExpectedInitialRecovery(WoundLevel woundLevel, byte expectedWeeks)
    {
        Wounds wounds = new((uint)woundLevel, 0);

        Assert.Equal(expectedWeeks, wounds.RecoveryTimeLeft());
    }

    [Fact]
    public void ApplyWeekOfHealing_DegradesModerateWoundToMinorAfterTwoWeeks()
    {
        Wounds wounds = new((uint)WoundLevel.Moderate, 0);

        wounds.ApplyWeekOfHealing();
        wounds.ApplyWeekOfHealing();

        Assert.Equal(0, wounds.ModerateWounds);
        Assert.Equal(1, wounds.MinorWounds);
    }

    [Fact]
    public void HealWounds_ClearsAllWounds()
    {
        Wounds wounds = new((uint)WoundLevel.Mortal + (uint)WoundLevel.Major, 0);

        wounds.HealWounds();

        Assert.Equal((uint)0, wounds.WoundTotal);
        Assert.Equal(0, wounds.RecoveryTimeLeft());
    }
}
