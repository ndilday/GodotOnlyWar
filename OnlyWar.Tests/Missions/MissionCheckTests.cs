using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Missions;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using System.Linq;
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

    // Skill no longer selects the commander outright — it only separates leaders of equal rank,
    // subrank, and tenure, which is the case here (two sergeants of the same template).
    [Fact]
    public void LeaderMissionTest_SkillBreaksTieBetweenLeadersOfEqualRank()
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

    // A bad captain still outranks a good sergeant: the force lives with the senior officer's
    // judgment rather than fielding whichever leader happens to be most talented.
    [Fact]
    public void LeaderMissionTest_HigherRankCommandsOverMoreSkilledJuniorLeader()
    {
        Soldier captain = TestModelFactory.CreateSoldier(
            TestModelFactory.CaptainTemplate, "Mediocre Captain",
            charisma: 10, skills: new Skill(TestSkills.Leadership, 1));
        Soldier sergeant = TestModelFactory.CreateSoldier(
            TestModelFactory.SergeantTemplate, "Gifted Sergeant",
            charisma: 18, skills: new Skill(TestSkills.Leadership, 64));
        LeaderMissionTest missionTest = new(TestSkills.Leadership, difficulty: 5);
        RecordingRng random = new(0.75);

        float actual = missionTest.RunMissionCheck(
            [CreateBattleSquad(captain), CreateBattleSquad(sergeant)],
            random);

        Assert.Equal(ExpectedMarginFor(captain, TestSkills.Leadership, difficulty: 5, zDraw: 0.75),
            actual, precision: 5);
    }

    // Subrank separates leaders sharing a Rank, mirroring the chapter's Veteran Sergeant (subrank
    // 15) over plain Sergeant (12) at Rank 5.
    [Fact]
    public void LeaderMissionTest_HigherSubrankCommandsWithinSameRank()
    {
        Soldier veteranSergeant = TestModelFactory.CreateSoldier(
            TestModelFactory.VeteranSergeantTemplate, "Veteran Sergeant",
            charisma: 10, skills: new Skill(TestSkills.Leadership, 1));
        Soldier sergeant = TestModelFactory.CreateSoldier(
            TestModelFactory.SergeantTemplate, "Gifted Sergeant",
            charisma: 18, skills: new Skill(TestSkills.Leadership, 64));
        LeaderMissionTest missionTest = new(TestSkills.Leadership, difficulty: 5);
        RecordingRng random = new(0.75);

        float actual = missionTest.RunMissionCheck(
            [CreateBattleSquad(veteranSergeant), CreateBattleSquad(sergeant)],
            random);

        Assert.Equal(
            ExpectedMarginFor(veteranSergeant, TestSkills.Leadership, difficulty: 5, zDraw: 0.75),
            actual, precision: 5);
    }

    // A force whose only sergeant is down falls back on its best remaining brother. Previously the
    // roster-level guard let this through and the check ran on a null leader, auto-failing at
    // -5 sigma regardless of who was still standing.
    [Fact]
    public void LeaderMissionTest_FallsBackToBestIndividualWhenLeaderIsIncapacitated()
    {
        Soldier sergeant = TestModelFactory.CreateSoldier(
            TestModelFactory.SergeantTemplate, "Downed Sergeant",
            charisma: 10, skills: new Skill(TestSkills.Leadership, 1));
        Soldier survivor = TestModelFactory.CreateSoldier(
            name: "Senior Brother", charisma: 18, skills: new Skill(TestSkills.Leadership, 64));
        Incapacitate(sergeant);
        LeaderMissionTest missionTest = new(TestSkills.Leadership, difficulty: 5);
        RecordingRng random = new(0.75);

        float actual = missionTest.RunMissionCheck(
            [CreateBattleSquad(sergeant, survivor)],
            random);

        Assert.Equal(ExpectedMarginFor(survivor, TestSkills.Leadership, difficulty: 5, zDraw: 0.75),
            actual, precision: 5);
    }

    private static void Incapacitate(Soldier soldier)
    {
        soldier.Body.HitLocations
            .First(location => location.Template.IsVital && !location.Template.HoldsProgenoid)
            .Wounds.AddWound(WoundLevel.Massive);
        Assert.False(soldier.CanFight);
    }

    private static float ExpectedMarginFor(
        Soldier soldier, BaseSkill skill, float difficulty, double zDraw)
    {
        return ((soldier.GetTotalSkillValue(skill) - difficulty) / 5.0f) - (float)zDraw;
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
