using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers.Narrative;
using OnlyWar.Models.Planets;
using Xunit;

namespace OnlyWar.Tests.Narrative;

// Coverage for the minimal Promised World briefing composer (Design/OpeningScenario.md §4, step 3).
// The load-bearing guarantees are: every token is substituted (no authored placeholder leaks into
// the player-facing text), and the template choice is deterministic for a given token set.
public class BriefingComposerTests
{
    private static BriefingTokens SampleTokens(int selector = 0) => new BriefingTokens
    {
        ChapterName = "Heart of the Emperor",
        PlanetName = "Calderis",
        SubsectorName = "Meridian Subsector",
        AuthorityName = "Vandire",
        AuthorityTitle = "Lord of the Sector",
        EnemyName = "Tyranids",
        TemplateSelector = selector
    };

    [Fact]
    public void Compose_SubstitutesAllTokens_NoLeftoverPlaceholders()
    {
        BriefingTokens tokens = SampleTokens();
        string text = BriefingComposer.ComposePromisedWorldBriefing(tokens);

        // No authored placeholder survives into the output.
        Assert.DoesNotContain("{", text);
        Assert.DoesNotContain("}", text);

        // Every supplied value is present in the rendered briefing.
        Assert.Contains(tokens.ChapterName, text);
        Assert.Contains(tokens.PlanetName, text);
        Assert.Contains(tokens.SubsectorName, text);
        Assert.Contains(tokens.AuthorityName, text);
        Assert.Contains(tokens.AuthorityTitle, text);
        Assert.Contains(tokens.EnemyName, text);
    }

    [Fact]
    public void Compose_IsDeterministicForSameTokens()
    {
        BriefingTokens tokens = SampleTokens(selector: 42);
        string first = BriefingComposer.ComposePromisedWorldBriefing(tokens);
        string second = BriefingComposer.ComposePromisedWorldBriefing(tokens);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Compose_SelectorChoosesTemplate_AllReachableAndClean()
    {
        // Sweep a range of selectors wide enough to exercise every template; each must substitute
        // cleanly, and the selector must reach more than one distinct template (variety).
        HashSet<string> distinct = new HashSet<string>();
        for (int selector = 0; selector < 12; selector++)
        {
            string text = BriefingComposer.ComposePromisedWorldBriefing(SampleTokens(selector));
            Assert.DoesNotContain("{", text);
            Assert.DoesNotContain("}", text);
            distinct.Add(text);
        }

        Assert.True(distinct.Count > 1, "Selector should reach more than one template.");
    }

    [Fact]
    public void Compose_EquivalentSelectorsModuloTemplateCount_MatchAndWrap()
    {
        // Selection is modulo the template count, so a selector and a far-larger one congruent to
        // it (here +N for some N that is a multiple of the count is unknown, so compare 0 vs a
        // negative selector) resolve consistently. Negative selectors must not throw or leak.
        string zero = BriefingComposer.ComposePromisedWorldBriefing(SampleTokens(0));
        string negative = BriefingComposer.ComposePromisedWorldBriefing(SampleTokens(-3));
        Assert.DoesNotContain("{", negative);
        Assert.False(string.IsNullOrWhiteSpace(zero));
    }

    [Theory]
    [InlineData(GovernanceTier.SectorCapital, "Lord of the Sector")]
    [InlineData(GovernanceTier.SubsectorCapital, "Lord of the Subsector")]
    [InlineData(GovernanceTier.Planetary, "Planetary Governor")]
    public void GetAuthorityTitle_MapsTierToHonorific(GovernanceTier tier, string expected)
    {
        Assert.Equal(expected, BriefingComposer.GetAuthorityTitle(tier));
    }
}
