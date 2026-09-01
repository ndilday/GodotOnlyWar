using System;
using System.IO;
using OnlyWar.Helpers.UI;
using Xunit;

namespace OnlyWar.Tests.UI;

public class IconAssetRegistryTests
{
    [Fact]
    public void ModManifest_RegistersNamespacedIconWithoutPollutingCoreKeySpace()
    {
        string manifestPath = Path.Combine(
            Path.GetTempPath(), $"onlywar-icons-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(manifestPath, """
                {
                  "atlas": "mod_atlas.png",
                  "icons": {
                    "award_duelist": { "x": 0, "y": 0, "w": 32, "h": 32 }
                  }
                }
                """);

            IconAssetRegistry.RegisterManifest(manifestPath, "iron_halo");

            Assert.True(IconAtlas.HasIcon("iron_halo:award_duelist"));
            Assert.False(IconAtlas.HasIcon("award_duelist"));
        }
        finally
        {
            IconAssetRegistry.ClearPackage("iron_halo");
            File.Delete(manifestPath);
        }
    }
}
