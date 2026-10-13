using OnlyWar.Domain.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

/// <summary>
/// The skill bonus curve: <c>log2(points + 2^(difficulty - 4)) - difficulty</c>. At 0 points it
/// equals the untrained bonus, so every fraction of a point is an improvement.
/// </summary>
public class SkillBonusCurveTests
{
    [Theory]
    [InlineData(0f)]
    [InlineData(2f)]
    [InlineData(3f)]
    public void ZeroPoints_EqualsUntrained(float difficulty)
    {
        Assert.Equal(Skill.UntrainedBonus, Skill.CalculateBonus(0f, difficulty), 5);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(2f)]
    [InlineData(3f)]
    public void EveryFractionOfAPoint_IsBetterThanUntrained_AndMorePointsAreBetter(float difficulty)
    {
        float previous = Skill.UntrainedBonus;
        foreach (float points in new[] { 0.001f, 0.01f, 0.1f, 0.25f, 1f, 2f, 16f })
        {
            float bonus = Skill.CalculateBonus(points, difficulty);
            Assert.True(bonus > previous, $"{points} points at difficulty {difficulty}: {bonus} <= {previous}");
            previous = bonus;
        }
    }

    [Fact]
    public void ManyPoints_MatchThePlainLogCurve()
    {
        Assert.Equal(2.02f, Skill.CalculateBonus(16f, 2f), 2);
        Assert.Equal(-0.83f, Skill.CalculateBonus(2f, 2f), 2);
    }

    [Fact]
    public void TotalSkillValue_RisesWithTheFirstFractionOfAPoint()
    {
        Soldier soldier = TestModelFactory.CreateSoldier(name: "Varro");
        BaseSkill skill = new(951, SkillCategory.Tech, "Engineering (Cybernetics)",
            Attribute.Intelligence, 2f);
        float untrained = soldier.GetTotalSkillValue(skill);

        soldier.AddSkillPoints(skill, 0f);
        Assert.Equal(untrained, soldier.GetTotalSkillValue(skill), 5);

        soldier.AddSkillPoints(skill, 0.01f);
        Assert.True(soldier.GetTotalSkillValue(skill) > untrained);
    }
}
