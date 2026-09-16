using OnlyWar.Domain;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class FactionGenerationPolicyTests
{
    [Fact]
    public void ScenarioProfileCatalog_ResolvesKeysWithoutCaseSensitivity()
    {
        ScenarioInfiltratorOverride infiltratorOverride = new(
            "promised_world",
            42,
            2,
            0,
            0.05f,
            1f / 33f,
            0.05f,
            3);
        ScenarioProfile profile = new(
            "promised_world",
            ScenarioKeys.PromisedWorld,
            42,
            1,
            50_000_000,
            2,
            3,
            1,
            4,
            0.5f,
            0.5f,
            infiltratorOverride);

        ScenarioProfileCatalog catalog = new([profile]);

        Assert.Same(profile, catalog.GetRequired("PROMISED_WORLD"));
        Assert.Same(profile, catalog.GetRequired("promised_world"));
        Assert.Same(profile, catalog.GetRequiredForScenario(ScenarioKeys.PromisedWorld, 42));
        Assert.Same(infiltratorOverride, profile.InfiltratorOverride);
    }

    [Fact]
    public void ScenarioFactionSelection_DistinguishesDefaultRandomAndExplicitChoices()
    {
        Assert.Null(ScenarioFactionSelection.Default.FactionId);
        Assert.False(ScenarioFactionSelection.Default.IsRandom);

        Assert.Null(ScenarioFactionSelection.Random.FactionId);
        Assert.True(ScenarioFactionSelection.Random.IsRandom);

        ScenarioFactionSelection explicitChoice = ScenarioFactionSelection.ForFaction(42);
        Assert.Equal(42, explicitChoice.FactionId);
        Assert.False(explicitChoice.IsRandom);
    }

    [Fact]
    public void PlanetPresenceCatalog_PrioritizesTemplateSpecificRules()
    {
        FactionPlanetPresenceRule defaultRule = new(
            SectorGenerationProfileKeys.Standard,
            7,
            null,
            FactionPresenceMode.Hidden,
            0.02,
            0,
            0.05,
            1.0 / 33.0);
        FactionPlanetPresenceRule specificRule = defaultRule with
        {
            PlanetTemplateId = 12,
            PresenceMode = FactionPresenceMode.Public
        };
        FactionPlanetPresenceCatalog catalog = new([defaultRule, specificRule]);

        Assert.Equal(
            [specificRule],
            catalog.GetApplicableRules(SectorGenerationProfileKeys.Standard, 12));
        Assert.Equal(
            [defaultRule],
            catalog.GetApplicableRules(SectorGenerationProfileKeys.Standard, 13));
    }
}
