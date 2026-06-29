using System.Collections.Generic;
using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Soldiers.Ratings;
using OnlyWar.Tests.Fixtures;
using Xunit;
using Attribute = OnlyWar.Models.Soldiers.Attribute;

namespace OnlyWar.Tests.Domain;

public class RatingCalculatorTests
{
    private static readonly Date Date = new(41, 999, 1);

    [Fact]
    public void Evaluate_Product_DividesComponentProductByNormalizationLows()
    {
        RatingDefinition definition = new(1, RatingKeys.Melee, "Melee", RatingAggregation.Product,
            new[]
            {
                new RatingComponent(RatingComponentType.AttributeValue, (int)Attribute.Strength, 0),
                new RatingComponent(RatingComponentType.SkillTotal, TestSkills.Melee.Id, 1)
            },
            new[]
            {
                new RatingNormalizationFactor(2.0, 2.0, 0),
                new RatingNormalizationFactor(2.0, 2.0, 1)
            });
        var skillsById = new Dictionary<int, BaseSkill> { [TestSkills.Melee.Id] = TestSkills.Melee };
        Soldier soldier = TestModelFactory.CreateSoldier(strength: 12, skills: new Skill(TestSkills.Melee, 4));
        RatingCalculator calculator = new(new[] { definition }, [], skillsById, new FixedRNG());

        SoldierEvaluation eval = calculator.Evaluate(soldier, Date);

        float expectedAggregate = soldier.Strength * soldier.GetTotalSkillValue(TestSkills.Melee);
        Assert.Equal(expectedAggregate / 4.0f, eval[RatingKeys.Melee], precision: 4);
    }

    [Fact]
    public void Evaluate_Sum_AddsComponentsThenNormalizes()
    {
        RatingDefinition definition = new(2, RatingKeys.Ranged, "Ranged", RatingAggregation.Sum,
            new[]
            {
                new RatingComponent(RatingComponentType.AttributeValue, (int)Attribute.Dexterity, 0),
                new RatingComponent(RatingComponentType.BestSkillBonusInCategory, (int)SkillCategory.Ranged, 1)
            },
            new[] { new RatingNormalizationFactor(0.5, 0.5, 0) });
        Soldier soldier = TestModelFactory.CreateSoldier(dexterity: 11, skills: new Skill(TestSkills.Ranged, 8));
        RatingCalculator calculator = new(new[] { definition }, [], new Dictionary<int, BaseSkill>(), new FixedRNG());

        SoldierEvaluation eval = calculator.Evaluate(soldier, Date);

        float expectedAggregate = soldier.Dexterity
            + soldier.GetBestSkillInCategory(SkillCategory.Ranged).SkillBonus;
        Assert.Equal(expectedAggregate / 0.5f, eval[RatingKeys.Ranged], precision: 4);
    }

    [Fact]
    public void ApplyAwards_GrantsHighestMatchingMedalTierOnly()
    {
        // Ancient tiers reproduce the medal thresholds; an evaluation above the top
        // threshold must yield exactly one award (the §3 "highest tier wins" rule,
        // which fixes the old double-Banner bug).
        RatingDefinition ancient = new(4, RatingKeys.Ancient, "Ancient", RatingAggregation.Product,
            new[] { new RatingComponent(RatingComponentType.AttributeValue, (int)Attribute.Ego, 0) },
            new[] { new RatingNormalizationFactor(1.0, 1.0, 0) });
        RatingAwardTier[] tiers =
        {
            new(1, RatingKeys.Ancient, 4, 112, RatingAwardEffect.Award, "Banner", "Adamantium Banner of the Emperor"),
            new(2, RatingKeys.Ancient, 3, 100, RatingAwardEffect.Award, "Banner", "Gold Banner of the Emperor"),
            new(3, RatingKeys.Ancient, 2, 95, RatingAwardEffect.Award, "Banner", "Silver Banner of the Emperor"),
            new(4, RatingKeys.Ancient, 1, 85, RatingAwardEffect.Award, "Banner", "Bronze Banner of the Emperor")
        };
        RatingCalculator calculator = new(new[] { ancient }, tiers, new Dictionary<int, BaseSkill>(), new FixedRNG());
        PlayerSoldier soldier = new(TestModelFactory.CreateSoldier(), "Brother Test");
        SoldierEvaluation eval = new(Date, new Dictionary<string, float> { [RatingKeys.Ancient] = 113f });

        calculator.ApplyAwards(soldier, eval, Date);

        Assert.Single(soldier.SoldierAwards);
        Assert.Equal(4, (int)soldier.SoldierAwards[0].Level);
        Assert.Equal("Adamantium Banner of the Emperor", soldier.SoldierAwards[0].Name);
    }

    [Fact]
    public void ApplyAwards_InterpolatesBestSkillInCategoryIntoAwardName()
    {
        RatingDefinition ranged = new(2, RatingKeys.Ranged, "Ranged", RatingAggregation.Sum,
            new[] { new RatingComponent(RatingComponentType.BestSkillBonusInCategory, (int)SkillCategory.Ranged, 0) },
            new[] { new RatingNormalizationFactor(1.0, 1.0, 0) });
        RatingAwardTier[] tiers =
        {
            new(1, RatingKeys.Ranged, 3, 115, RatingAwardEffect.Award, "Gun", "Gold {bestSkillInCategory} of the Emperor")
        };
        RatingCalculator calculator = new(new[] { ranged }, tiers, new Dictionary<int, BaseSkill>(), new FixedRNG());
        PlayerSoldier soldier = new(
            TestModelFactory.CreateSoldier(skills: new Skill(TestSkills.Ranged, 8)), "Brother Test");
        SoldierEvaluation eval = new(Date, new Dictionary<string, float> { [RatingKeys.Ranged] = 116f });

        calculator.ApplyAwards(soldier, eval, Date);

        Assert.Single(soldier.SoldierAwards);
        Assert.Equal($"Gold {TestSkills.Ranged.Name} of the Emperor", soldier.SoldierAwards[0].Name);
    }

    [Fact]
    public void ApplyAwards_AddsHistoryEntryForFlagEffect()
    {
        RatingDefinition medical = new(5, RatingKeys.Medical, "Medical", RatingAggregation.Product,
            new[] { new RatingComponent(RatingComponentType.AttributeValue, (int)Attribute.Intelligence, 0) },
            new[] { new RatingNormalizationFactor(1.0, 1.0, 0) });
        RatingAwardTier[] tiers =
        {
            new(1, RatingKeys.Medical, 1, 115, RatingAwardEffect.HistoryFlag, null,
                "Flagged for potential training as Apothecary")
        };
        RatingCalculator calculator = new(new[] { medical }, tiers, new Dictionary<int, BaseSkill>(), new FixedRNG());
        PlayerSoldier soldier = new(TestModelFactory.CreateSoldier(), "Brother Test");
        SoldierEvaluation eval = new(Date, new Dictionary<string, float> { [RatingKeys.Medical] = 116f });

        calculator.ApplyAwards(soldier, eval, Date);

        Assert.Empty(soldier.SoldierAwards);
        Assert.Contains(soldier.SoldierHistory, e => e.Contains("Flagged for potential training as Apothecary"));
    }

    [Fact]
    public void ApplyAwards_RecordsHistoryFlagOnlyOnceAcrossEvaluations()
    {
        // Ratings are re-evaluated every training pass; a flag whose threshold stays
        // exceeded must not stack a duplicate history entry each time.
        RatingDefinition medical = new(5, RatingKeys.Medical, "Medical", RatingAggregation.Product,
            new[] { new RatingComponent(RatingComponentType.AttributeValue, (int)Attribute.Intelligence, 0) },
            new[] { new RatingNormalizationFactor(1.0, 1.0, 0) });
        RatingAwardTier[] tiers =
        {
            new(1, RatingKeys.Medical, 1, 115, RatingAwardEffect.HistoryFlag, null,
                "Flagged for potential training as Apothecary")
        };
        RatingCalculator calculator = new(new[] { medical }, tiers, new Dictionary<int, BaseSkill>(), new FixedRNG());
        PlayerSoldier soldier = new(TestModelFactory.CreateSoldier(), "Brother Test");
        SoldierEvaluation eval = new(Date, new Dictionary<string, float> { [RatingKeys.Medical] = 116f });

        calculator.ApplyAwards(soldier, eval, Date);
        calculator.ApplyAwards(soldier, eval, Date);
        calculator.ApplyAwards(soldier, eval, Date);

        Assert.Single(soldier.SoldierEvents,
            e => e.Type == SoldierEventType.RatingFlag
                 && e.Detail == "Flagged for potential training as Apothecary");
    }
}
