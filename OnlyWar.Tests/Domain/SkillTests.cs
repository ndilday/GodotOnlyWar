using OnlyWar.Domain.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class SkillTests
{
    [Fact]
    public void Constructor_ClampsNegativePointsToZero()
    {
        Skill skill = new(TestSkills.Ranged, -10);

        Assert.Equal(0, skill.PointsInvested);
    }

    // log2(points + 2^(difficulty - 4)) - difficulty; the test ranged skill has difficulty 0, so
    // the offset is 1/16 and 0 points give exactly the untrained -4.
    [Theory]
    [InlineData(0, -4)]
    [InlineData(1, 0.08746)]
    [InlineData(2, 1.04439)]
    [InlineData(4, 2.02237)]
    [InlineData(8, 3.01123)]
    public void SkillBonus_UsesShiftedLogTwoProgression(float points, float expectedBonus)
    {
        Skill skill = new(TestSkills.Ranged, points);

        Assert.Equal(expectedBonus, skill.SkillBonus, precision: 4);
    }

    [Fact]
    public void AddPoints_IgnoresNonPositiveValues()
    {
        Skill skill = new(TestSkills.Ranged, 2);

        skill.AddPoints(0);
        skill.AddPoints(-10);

        Assert.Equal(2, skill.PointsInvested);
    }

    [Fact]
    public void SoldierTotalSkill_UsesBaseAttributeAndSkillBonus()
    {
        Soldier soldier = TestModelFactory.CreateSoldier(
            dexterity: 12,
            skills: new Skill(TestSkills.Ranged, 4));

        Assert.Equal(14.02237f, soldier.GetTotalSkillValue(TestSkills.Ranged), precision: 4);
    }

    [Fact]
    public void SoldierTotalSkill_UsesUntrainedPenaltyWhenSkillIsMissing()
    {
        Soldier soldier = TestModelFactory.CreateSoldier(dexterity: 12);

        Assert.Equal(8, soldier.GetTotalSkillValue(TestSkills.Ranged), precision: 5);
    }
}
