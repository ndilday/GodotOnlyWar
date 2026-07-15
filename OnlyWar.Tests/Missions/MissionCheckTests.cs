using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Missions;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Missions;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class MissionCheckTests
{
    [Fact]
    public void IndividualMissionTest_UsesHighestSkilledAbleSoldier()
    {
        BattleSquad squad = CreateBattleSquad(
            TestModelFactory.CreateSoldier(name: "Low", dexterity: 10, skills: new Skill(TestSkills.Stealth, 1)),
            TestModelFactory.CreateSoldier(name: "High", dexterity: 10, skills: new Skill(TestSkills.Stealth, 16)));
        IndividualMissionTest missionTest = new(TestSkills.Stealth, difficulty: 5);

        RNG.Reset(99);
        float expected = ExpectedMargin(zAdvantage: (13 - 5) / 5.0f, seed: 99);
        RNG.Reset(99);
        float actual = missionTest.RunMissionCheck([squad], StaticRNG.Instance);

        Assert.Equal(expected, actual, precision: 5);
    }

    [Fact]
    public void LeaderMissionTest_UsesBestSquadLeaderWhenPresent()
    {
        BattleSquad firstSquad = CreateBattleSquad(
            TestModelFactory.CreateSoldier(TestModelFactory.SergeantTemplate, "Decent Leader", charisma: 11, skills: new Skill(TestSkills.Leadership, 4)),
            TestModelFactory.CreateSoldier(name: "Brilliant Non-Leader", charisma: 18, skills: new Skill(TestSkills.Leadership, 64)));
        BattleSquad secondSquad = CreateBattleSquad(
            TestModelFactory.CreateSoldier(TestModelFactory.SergeantTemplate, "Best Leader", charisma: 12, skills: new Skill(TestSkills.Leadership, 8)));
        LeaderMissionTest missionTest = new(TestSkills.Leadership, difficulty: 5);

        RNG.Reset(10);
        float expected = ExpectedMargin(zAdvantage: (15 - 5) / 5.0f, seed: 10);
        RNG.Reset(10);
        float actual = missionTest.RunMissionCheck(
            [firstSquad, secondSquad],
            StaticRNG.Instance);

        Assert.Equal(expected, actual, precision: 5);
    }

    [Fact]
    public void LeaderMissionTest_FallsBackToBestIndividualWhenNoLeaderExists()
    {
        BattleSquad squad = CreateBattleSquad(
            TestModelFactory.CreateSoldier(name: "Low", charisma: 10, skills: new Skill(TestSkills.Leadership, 1)),
            TestModelFactory.CreateSoldier(name: "High", charisma: 13, skills: new Skill(TestSkills.Leadership, 4)));
        LeaderMissionTest missionTest = new(TestSkills.Leadership, difficulty: 5);

        RNG.Reset(11);
        float expected = ExpectedMargin(zAdvantage: (15 - 5) / 5.0f, seed: 11);
        RNG.Reset(11);
        float actual = missionTest.RunMissionCheck([squad], StaticRNG.Instance);

        Assert.Equal(expected, actual, precision: 5);
    }

    [Fact]
    public void SquadMissionTest_UsesAverageSkillAcrossAbleSoldiers()
    {
        BattleSquad squad = CreateBattleSquad(
            TestModelFactory.CreateSoldier(name: "First", dexterity: 10, skills: new Skill(TestSkills.Stealth, 1)),
            TestModelFactory.CreateSoldier(name: "Second", dexterity: 14, skills: new Skill(TestSkills.Stealth, 4)));
        SquadMissionTest missionTest = new(TestSkills.Stealth, difficulty: 5);

        RNG.Reset(12);
        float expected = ExpectedMargin(zAdvantage: (12 - 5) / 5.0f, seed: 12);
        RNG.Reset(12);
        float actual = missionTest.RunMissionCheck([squad], StaticRNG.Instance);

        Assert.Equal(expected, actual, precision: 5);
    }

    [Fact]
    public void IndividualMissionTest_UsesInjectedRandomStream()
    {
        Soldier scout = TestModelFactory.CreateSoldier(
            name: "Scout",
            dexterity: 10,
            skills: new Skill(TestSkills.Stealth, 1));
        BattleSquad squad = CreateBattleSquad(scout);
        IndividualMissionTest missionTest = new(TestSkills.Stealth, difficulty: 5);
        var random = new RecordingRng(0.75);

        float actual = missionTest.RunMissionCheck([squad], random);

        float expected = ((scout.GetTotalSkillValue(TestSkills.Stealth) - 5) / 5.0f) - 0.75f;
        Assert.Equal(expected, actual, precision: 5);
        Assert.Equal(1, random.NormalDraws);
    }

    private static BattleSquad CreateBattleSquad(params Soldier[] soldiers)
    {
        return new BattleSquad(true, TestModelFactory.CreateSquad("Test Squad", soldiers));
    }

    private static float ExpectedMargin(float zAdvantage, int seed)
    {
        RNG.Reset(seed);
        return zAdvantage - (float)RNG.NextRandomZValue();
    }

    private sealed class RecordingRng(double normalValue) : IRNG
    {
        public int NormalDraws { get; private set; }

        public double GetDoubleInRange(double lowerBound, double upperBound) =>
            throw new System.NotSupportedException();

        public double GetLinearDouble() => throw new System.NotSupportedException();

        public int GetIntBelowMax(int min, int max) => throw new System.NotSupportedException();

        public double NextRandomZValue()
        {
            NormalDraws++;
            return normalValue;
        }
    }
}
