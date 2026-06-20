using System.Linq;
using OnlyWar.Models.Soldiers.Ratings;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Data;

public class RatingDefinitionDataTests
{
    [Fact]
    public void RulesDatabase_DefinesAllSevenRatings()
    {
        var rules = RulesDatabaseFixture.LoadRules();
        var keys = rules.RatingDefinitions.Select(d => d.Key).ToHashSet();

        foreach (string key in new[]
        {
            RatingKeys.Melee, RatingKeys.Ranged, RatingKeys.Leadership, RatingKeys.Ancient,
            RatingKeys.Medical, RatingKeys.Tech, RatingKeys.Piety
        })
        {
            Assert.Contains(key, keys);
        }
    }

    [Fact]
    public void MeleeDefinition_MatchesTheMigratedFormula()
    {
        var rules = RulesDatabaseFixture.LoadRules();
        RatingDefinition melee = rules.RatingDefinitions.Single(d => d.Key == RatingKeys.Melee);

        Assert.Equal(RatingAggregation.Product, melee.Aggregation);
        Assert.Equal(2, melee.Components.Count);
        Assert.Contains(melee.Components, c => c.ComponentType == RatingComponentType.AttributeValue);
        Assert.Contains(melee.Components, c => c.ComponentType == RatingComponentType.SkillTotal);
        Assert.Equal(2, melee.NormalizationFactors.Count);
        Assert.All(melee.NormalizationFactors, f =>
        {
            Assert.Equal(1.44, f.Low, precision: 3);
            Assert.Equal(1.76, f.High, precision: 3);
        });
    }

    [Fact]
    public void RangedDefinition_IsSumAndUsesBestSkillInCategory()
    {
        var rules = RulesDatabaseFixture.LoadRules();
        RatingDefinition ranged = rules.RatingDefinitions.Single(d => d.Key == RatingKeys.Ranged);

        Assert.Equal(RatingAggregation.Sum, ranged.Aggregation);
        Assert.Contains(ranged.Components, c => c.ComponentType == RatingComponentType.BestSkillBonusInCategory);
    }

    [Fact]
    public void AwardTiers_ReproduceMedalAndFlagShape()
    {
        var rules = RulesDatabaseFixture.LoadRules();

        var swordTiers = rules.RatingAwardTiers.Where(t => t.RatingKey == RatingKeys.Melee).ToList();
        Assert.Equal(4, swordTiers.Count);
        Assert.All(swordTiers, t => Assert.Equal(RatingAwardEffect.Award, t.Effect));
        Assert.All(swordTiers, t => Assert.Equal("Sword", t.AwardType));

        var rangedTiers = rules.RatingAwardTiers.Where(t => t.RatingKey == RatingKeys.Ranged).ToList();
        Assert.Equal(4, rangedTiers.Count);
        Assert.All(rangedTiers, t => Assert.Contains("{bestSkillInCategory}", t.NameTemplate));

        var medicalTiers = rules.RatingAwardTiers.Where(t => t.RatingKey == RatingKeys.Medical).ToList();
        Assert.Single(medicalTiers);
        Assert.Equal(RatingAwardEffect.HistoryFlag, medicalTiers[0].Effect);
    }
}
