using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class SoldierTrainingCalculatorValidationTests
{
    [Fact]
    public void RealRulesDatabase_ContainsEverySkillTheTrainingCalculatorRequires()
    {
        var rules = RulesDatabaseFixture.LoadRules();
        var skillNames = rules.BaseSkills.Values.Select(s => s.Name).ToHashSet();

        // Mirrors the load-time validation in GameRulesData; tied to the calculator's
        // own required-skill list so a rename of either side fails here (TDD §8.3).
        foreach (string required in SoldierTrainingCalculator.RequiredSkillNames)
        {
            Assert.Contains(required, skillNames);
        }
    }

    [Fact]
    public void Constructor_Succeeds_WithRealRulesDatabaseSkills()
    {
        var rules = RulesDatabaseFixture.LoadRules();

        var calculator = new SoldierTrainingCalculator(rules.BaseSkills.Values, rules.TrainingProfiles.Values);

        Assert.NotNull(calculator);
    }
}
