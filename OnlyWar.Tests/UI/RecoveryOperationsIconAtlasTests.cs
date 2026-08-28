using OnlyWar.Helpers.UI;
using Xunit;

namespace OnlyWar.Tests.UI;

public class RecoveryOperationsIconAtlasTests
{
    [Theory]
    [InlineData("sort")]
    [InlineData("recovery_time")]
    [InlineData("limb_replacement")]
    [InlineData("medical_detachment")]
    [InlineData("individual_posting")]
    [InlineData("reunion")]
    public void RecoveryOperationsIcons_AreRegistered(string key)
    {
        Assert.True(IconAtlas.HasIcon(key));
    }
}
