using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Campaign.Narrative;
using OnlyWar.Domain;
using OnlyWar.Domain.Events;
using OnlyWar.Generation.Abstractions;
using OnlyWar.Generation.World;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Narrative;

// TDD §6.9: the founding the chapter belongs to, the start date it sets, and
// the myth composed from it. The new-game integration checks are in Data.NewGameSaveTests, which
// already generates a sector.
public class FoundingMythTests
{
    private static readonly Lazy<GameRulesData> Rules = new(() =>
        OnlyWar.Persistence.Database.GameRules.GameRulesLoader.Load(RulesDatabaseFixture.DatabasePath));

    private static readonly Founding Cursed = new(9, 21, "Cursed", 34991, 34991);

    [Fact]
    public void AbsoluteYear_AgreesWithTheCalendarAcrossAMillennium()
    {
        Assert.Equal(new Date(39, 496, 1), Date.FromAbsoluteYear(38496));
        Assert.Equal(38496, new Date(39, 496, 7).AbsoluteYear);
        // Year 000 is the calendar's last year of the previous millennium.
        Assert.Equal(new Date(41, 0, 1), Date.FromAbsoluteYear(40000));
        Assert.Equal(new Date(40, 999, 52), Date.FromTotalWeeks(Date.FromAbsoluteYear(40000).GetTotalWeeks() - 1));
        Assert.Equal(new Date(41, 2, 1), Date.FromAbsoluteYear(new Date(40, 998, 1).AbsoluteYear + 4));
        Assert.Equal("002.M41", new Date(41, 2, 9).ToYearString());
    }

    [Fact]
    public void Rules_LoadTheFoundingsInNumberOrder()
    {
        IReadOnlyList<Founding> foundings = Rules.Value.Foundings;
        Assert.Equal(13, foundings.Count);
        Assert.Equal(foundings.Select(founding => founding.Number).OrderBy(number => number),
            foundings.Select(founding => founding.Number));
        Assert.Equal("Cursed", foundings.Single(founding => founding.Number == 21).Epithet);
        for (int i = 1; i < foundings.Count; i++)
        {
            Assert.True(foundings[i].EarliestYear >= foundings[i - 1].LatestYear);
        }
    }

    // tests and the debug preview start on 500.M39, which is the 24th Founding of 496.M39.
    [Fact]
    public void ForCampaignStart_On500M39_IsTheTwentyFourthFoundingOf496M39()
    {
        ChapterFounding founding = FoundingSelector.ForCampaignStart(Rules.Value, new Date(39, 500, 1));
        Assert.Equal(24, founding.Founding.Number);
        Assert.Equal(new Date(39, 496, 1), founding.FoundingDate);
        Assert.Equal(new Date(39, 500, 1), founding.CampaignStartDate);
    }

    [Fact]
    public void ForCampaignStart_OutsideEveryFounding_IsUnnumbered()
    {
        ChapterFounding founding = FoundingSelector.ForCampaignStart(Rules.Value, new Date(30, 500, 1));
        Assert.Null(founding.Founding);
        Assert.Equal(29496, founding.FoundingYear);
    }

    // the founding and its year come from the seed alone, inside the founding's range, and the
    // campaign starts when the founders finish training.
    [Fact]
    public void Select_IsDeterministicAndInsideTheFoundingsRange()
    {
        HashSet<int> numbers = [];
        for (int seed = 1; seed <= 300; seed++)
        {
            ChapterFounding first = FoundingSelector.Select(Rules.Value, seed);
            Assert.Equal(first, FoundingSelector.Select(Rules.Value, seed));
            Assert.True(first.Founding.Contains(first.FoundingYear));
            Assert.Equal(first.FoundingYear + ChapterFounding.TrainingYears,
                first.CampaignStartDate.AbsoluteYear);
            Assert.Equal(1, first.CampaignStartDate.Week);
            numbers.Add(first.Founding.Number);
        }
        Assert.Equal(Rules.Value.Foundings.Count, numbers.Count);
    }

    // four or five paragraphs, one of them (or one sentence of the Election) a legend.
    [Fact]
    public void Compose_WritesFourOrFiveParagraphsWithOneMarkedLegend()
    {
        for (int seed = 1; seed <= 60; seed++)
        {
            ChapterFoundingMythPayload myth = FoundingMythComposer.Compose(Tokens(seed));
            string[] paragraphs = myth.Text.Split("\n\n");
            Assert.InRange(paragraphs.Length, 4, 5);
            Assert.Equal(1, Count(myth.Text, "It is said that"));
            Assert.DoesNotContain("{", myth.Text);
            Assert.Contains("of uncertain lineage", myth.Text);
            Assert.Contains("Iron Wardens", myth.Text);
            Assert.Equal("The Founding of the Iron Wardens", myth.Title);
            Assert.Contains(myth.AspirantWorldName, myth.Text);
            Assert.Contains("Promise", myth.Text);
        }
    }

    [Fact]
    public void Compose_IsTheSameForTheSameSeed()
    {
        Assert.Equal(FoundingMythComposer.Compose(Tokens(7)), FoundingMythComposer.Compose(Tokens(7)));
        Assert.Contains(Enumerable.Range(1, 20), seed =>
            FoundingMythComposer.Compose(Tokens(seed)).Text != FoundingMythComposer.Compose(Tokens(7)).Text);
    }

    [Fact]
    public void Compose_NamesTheFoundingAndItsEpithet()
    {
        ChapterFoundingMythPayload myth = FoundingMythComposer.Compose(Tokens(3));
        Assert.Contains("the Twenty-First Founding", myth.Text);
        Assert.Contains("Later ages would call it the Cursed Founding.", myth.Text);
        Assert.Contains("991.M35", myth.Text);
        Assert.Equal(21, myth.FoundingNumber);
        Assert.Equal("Cursed", myth.FoundingEpithet);
        Assert.Equal(34991, myth.FoundingYear);
    }

    [Fact]
    public void Compose_UnnumberedFounding_SaysTheNumberIsLost()
    {
        ChapterFoundingMythPayload myth = FoundingMythComposer.Compose(
            Tokens(3) with { Founding = new ChapterFounding(null, 38496) });
        Assert.Contains("a founding whose number the records do not preserve", myth.Text);
        Assert.Equal(0, myth.FoundingNumber);
        Assert.Null(myth.FoundingId);
    }

    // the aspirant world is invented, and never shares a name with a sector world.
    [Fact]
    public void Compose_AspirantWorldAvoidsTheSectorsWorldNames()
    {
        string chosen = FoundingMythComposer.Compose(Tokens(5)).AspirantWorldName;
        ChapterFoundingMythPayload avoided = FoundingMythComposer.Compose(
            Tokens(5) with { SectorWorldNames = [chosen.ToUpperInvariant()] });
        Assert.NotEqual(chosen, avoided.AspirantWorldName);
        Assert.DoesNotContain(chosen, avoided.Text);
    }

    // the Mars cohort is a count, not names.
    [Theory]
    [InlineData(12, "Twelve brothers were sent to Mars to begin their decades-long training as Techmarines.")]
    [InlineData(1, "One brother was sent to Mars to begin his decades-long training as a Techmarine.")]
    [InlineData(31, "31 brothers were sent to Mars")]
    public void Compose_StatesTheMarsCohortAsACount(int count, string expected)
    {
        ChapterFoundingMythPayload myth = FoundingMythComposer.Compose(Tokens(2) with { MarsCohortCount = count });
        Assert.Contains(expected, myth.Text);
        Assert.Equal(count, myth.MarsCohortCount);
    }

    [Fact]
    public void Compose_WithoutAMarsCohort_DoesNotMentionMars()
    {
        Assert.DoesNotContain("Mars", FoundingMythComposer.Compose(Tokens(2) with { MarsCohortCount = 0 }).Text);
    }

    [Fact]
    public void Compose_NamesOnlyTheFoundersWhoHoldAnOffice()
    {
        ChapterFoundingMythPayload myth = FoundingMythComposer.Compose(
            Tokens(4) with { AncientName = null, LibrarianName = null });
        Assert.Contains("Varro Kade, Captain of the First Company", myth.Text);
        Assert.Contains("Tycho Renn, the Chapter's first Champion", myth.Text);
        Assert.DoesNotContain("Ancient", myth.Text);
        Assert.DoesNotContain("Librarius", myth.Text);
    }

    [Fact]
    public void Compose_WithoutAChapterMaster_KeepsTheVoteButNotTheName()
    {
        for (int seed = 1; seed <= 30; seed++)
        {
            ChapterFoundingMythPayload myth = FoundingMythComposer.Compose(
                Tokens(seed) with { ChapterMasterName = null });
            Assert.Contains("The annals do not keep his name.", myth.Text);
            Assert.DoesNotContain(myth.LegendKey, new[] { "for-standing", "blades" });
        }
    }

    [Fact]
    public void Compose_InvasionCharge_UsesTheSingularEnemyName()
    {
        ChapterFoundingMythPayload myth = FoundingMythComposer.Compose(
            Tokens(1) with { Invasion = true, EnemyName = "Orks" });
        Assert.Contains("Ork invasion", myth.Text);
        Assert.DoesNotContain("Orks", myth.Text);
    }

    private static FoundingMythTokens Tokens(int seed) => new()
    {
        ChapterName = "Iron Wardens",
        Seed = seed,
        Founding = new ChapterFounding(Cursed, 34991),
        CampaignStartDate = Date.FromAbsoluteYear(34995),
        FounderCount = 1000,
        ChapterMasterName = "Aurelian Vex",
        ChampionName = "Tycho Renn",
        AncientName = "Hadrian Moll",
        LibrarianName = "Ezekiel Sarn",
        FirstCaptainName = "Varro Kade",
        MarsCohortCount = 12,
        AuthorityTitle = "Lord of the Sector",
        AuthorityName = "Cassia Thorne",
        PlanetName = "Promise",
        SubsectorName = "Halcyon Subsector",
        EnemyName = "Tyranids",
        Invasion = false
    };

    private static int Count(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }
}
