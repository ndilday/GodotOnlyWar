using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Missions;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Missions;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class MissionExperienceCalculatorTests
{
    // --- Pure formula tests -------------------------------------------------

    [Fact]
    public void CalculatePointsForMargin_NearZeroMarginTeachesMoreThanLargePositiveMargin()
    {
        float nearZero = MissionExperienceCalculator.CalculatePointsForMargin(0f);
        float trivialSuccess = MissionExperienceCalculator.CalculatePointsForMargin(4f);

        Assert.True(nearZero > trivialSuccess);
    }

    [Fact]
    public void CalculatePointsForMargin_DeepNegativeMarginTeachesLessThanNearZeroMargin()
    {
        float nearZero = MissionExperienceCalculator.CalculatePointsForMargin(0f);
        float hopelessFailure = MissionExperienceCalculator.CalculatePointsForMargin(-6f);

        Assert.True(hopelessFailure < nearZero);
    }

    [Fact]
    public void CalculatePointsForMargin_NarrowFailureTeachesComparablyToNarrowSuccess()
    {
        // A hard-won success and a near-miss failure should teach comparably - neither should
        // dominate the other by a wide margin, and failure should never be rewarded more than
        // an equally-narrow success as such.
        float narrowSuccess = MissionExperienceCalculator.CalculatePointsForMargin(0.25f);
        float narrowFailure = MissionExperienceCalculator.CalculatePointsForMargin(-0.25f);

        Assert.True(narrowFailure >= narrowSuccess * 0.9f);
        Assert.True(narrowSuccess >= narrowFailure * 0.5f);
    }

    [Fact]
    public void CalculatePointsForMargin_IsNonNegativeAndBoundedByThePeak()
    {
        // At extreme margins the Gaussian bump underflows to (approximately) zero in float
        // precision, which is the intended "teaches essentially nothing" behavior - it should
        // never go negative or exceed the peak award.
        Assert.InRange(MissionExperienceCalculator.CalculatePointsForMargin(100f),
            0f, MissionExperienceCalculator.BasePointsPerCheck);
        Assert.InRange(MissionExperienceCalculator.CalculatePointsForMargin(-100f),
            0f, MissionExperienceCalculator.BasePointsPerCheck);
        Assert.Equal(MissionExperienceCalculator.BasePointsPerCheck,
            MissionExperienceCalculator.CalculatePointsForMargin(MissionExperienceCalculator.BumpCenterMargin),
            precision: 5);
    }

    // --- Integration with the mission checks ---------------------------------

    [Fact]
    public void RunMissionCheck_AwardsSkillPointsToAllAblePlayerSoldiersInSkillUsed()
    {
        PlayerSoldier first = CreatePlayerSoldier("First", dexterity: 10, skillPoints: 1);
        PlayerSoldier second = CreatePlayerSoldier("Second", dexterity: 10, skillPoints: 1);
        BattleSquad squad = CreateBattleSquad(first, second);
        SquadMissionTest missionTest = new(TestSkills.Stealth, difficulty: 5);

        float firstBefore = first.GetTotalSkillValue(TestSkills.Stealth);
        float secondBefore = second.GetTotalSkillValue(TestSkills.Stealth);

        RNG.Reset(42);
        missionTest.RunMissionCheck([squad]);

        Assert.True(first.GetTotalSkillValue(TestSkills.Stealth) > firstBefore);
        Assert.True(second.GetTotalSkillValue(TestSkills.Stealth) > secondBefore);
    }

    [Fact]
    public void RunMissionCheck_DoesNotAwardNonPlayerSoldiers()
    {
        Soldier npc = TestModelFactory.CreateSoldier(name: "NPC", dexterity: 10,
            skills: new Skill(TestSkills.Stealth, 1));
        BattleSquad squad = CreateBattleSquad(false, npc);
        SquadMissionTest missionTest = new(TestSkills.Stealth, difficulty: 5);

        float before = npc.GetTotalSkillValue(TestSkills.Stealth);

        RNG.Reset(42);
        missionTest.RunMissionCheck([squad]);

        Assert.Equal(before, npc.GetTotalSkillValue(TestSkills.Stealth));
    }

    [Fact]
    public void RunMissionCheck_NonPlayerMissionDoesNotLogFieldExperience()
    {
        Soldier npc = TestModelFactory.CreateSoldier(name: "NPC", dexterity: 10,
            skills: new Skill(TestSkills.Stealth, 1));
        BattleSquad squad = CreateBattleSquad(false, npc);
        SquadMissionTest missionTest = new(TestSkills.Stealth, difficulty: 5);
        List<string> logs = [];
        GameLogLevel previousMinimumLevel = GameLog.MinimumLevel;
        var previousSink = GameLog.Sink;

        try
        {
            GameLog.MinimumLevel = GameLogLevel.Trace;
            GameLog.Sink = (level, message) => logs.Add(message);
            RNG.Reset(42);
            missionTest.RunMissionCheck([squad]);
        }
        finally
        {
            GameLog.MinimumLevel = previousMinimumLevel;
            GameLog.Sink = previousSink;
        }

        Assert.Empty(logs);
    }

    [Fact]
    public void RunMissionCheck_AwardsOnlyInTheSkillUsedByTheCheck()
    {
        PlayerSoldier soldier = CreatePlayerSoldier("Solo", dexterity: 10, skillPoints: 1);
        BattleSquad squad = CreateBattleSquad(soldier);
        SquadMissionTest missionTest = new(TestSkills.Stealth, difficulty: 5);

        float leadershipBefore = soldier.GetTotalSkillValue(TestSkills.Leadership);

        RNG.Reset(42);
        missionTest.RunMissionCheck([squad]);

        Assert.Equal(leadershipBefore, soldier.GetTotalSkillValue(TestSkills.Leadership));
    }

    [Fact]
    public void IndividualMissionTest_AwardsEveryAbleParticipant_NotJustTheBestSkilled()
    {
        PlayerSoldier low = CreatePlayerSoldier("Low", dexterity: 10, skillPoints: 1);
        PlayerSoldier high = CreatePlayerSoldier("High", dexterity: 10, skillPoints: 16);
        BattleSquad squad = CreateBattleSquad(low, high);
        IndividualMissionTest missionTest = new(TestSkills.Stealth, difficulty: 5);

        float lowBefore = low.GetTotalSkillValue(TestSkills.Stealth);

        RNG.Reset(7);
        missionTest.RunMissionCheck([squad]);

        // "Low" was not the soldier whose skill resolved the check, but he still participated
        // in (and thus learns from) the mission.
        Assert.True(low.GetTotalSkillValue(TestSkills.Stealth) > lowBefore);
    }

    private static PlayerSoldier CreatePlayerSoldier(string name, float dexterity, float skillPoints)
    {
        Soldier soldier = TestModelFactory.CreateSoldier(
            name: name, dexterity: dexterity, skills: new Skill(TestSkills.Stealth, skillPoints));
        return new PlayerSoldier(soldier, name);
    }

    private static BattleSquad CreateBattleSquad(params ISoldier[] soldiers)
    {
        return CreateBattleSquad(true, soldiers);
    }

    private static BattleSquad CreateBattleSquad(bool isPlayerSquad, params ISoldier[] soldiers)
    {
        Squad squad = new("Test Squad", null, TestModelFactory.SquadTemplate);
        foreach (ISoldier soldier in soldiers)
        {
            squad.AddSquadMember(soldier);
        }
        return new BattleSquad(isPlayerSquad, squad);
    }
}
