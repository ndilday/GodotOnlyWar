using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class SoldierTrainingCalculatorTests
{
    [Fact]
    public void ApplyMarineWorkExperienceByType_AppliesSoldierTemplateTrainingProfileWeights()
    {
        TrainingProfile profile = new(
            1,
            "test_work",
            [
                new TrainingProfileEntry(TestSkills.Ranged, 1),
                new TrainingProfileEntry(TestSkills.Melee, 3)
            ]);
        SoldierTemplate template = new(
            100,
            TestModelFactory.HumanSpecies,
            "Profiled Marine",
            1,
            1,
            false,
            0,
            [],
            profile);
        Soldier soldier = TestModelFactory.CreateSoldier(template);
        SoldierTrainingCalculator calculator = new(
            [TestSkills.Ranged, TestSkills.Melee, TestSkills.Stealth, TestSkills.Leadership],
            [profile]);

        calculator.ApplyMarineWorkExperienceByType(soldier, 8);

        Assert.Equal(2, soldier.Skills.Single(s => s.BaseSkill == TestSkills.Ranged).PointsInvested);
        Assert.Equal(6, soldier.Skills.Single(s => s.BaseSkill == TestSkills.Melee).PointsInvested);
    }

    [Fact]
    public void ApplyMarineWorkExperienceByType_AppliesAttributeTrainingProfileEntries()
    {
        TrainingProfile profile = new(
            1,
            "attribute_work",
            [
                new TrainingProfileEntry(Attribute.Strength, 1),
                new TrainingProfileEntry(Attribute.Dexterity, 1)
            ]);
        SoldierTemplate template = new(
            101,
            TestModelFactory.HumanSpecies,
            "Attribute Marine",
            1,
            1,
            false,
            0,
            [],
            profile);
        Soldier soldier = TestModelFactory.CreateSoldier(template, strength: 11, dexterity: 11);
        SoldierTrainingCalculator calculator = new(
            [TestSkills.Ranged, TestSkills.Melee, TestSkills.Stealth, TestSkills.Leadership],
            [profile]);

        calculator.ApplyMarineWorkExperienceByType(soldier, 20);

        Assert.Equal(12, soldier.Strength, precision: 5);
        Assert.Equal(12, soldier.Dexterity, precision: 5);
    }

    [Fact]
    public void TrainScouts_AppliesFocusTrainingProfiles()
    {
        TrainingProfile meleeFocus = new(1, "scout_focus_melee", [new TrainingProfileEntry(TestSkills.Melee, 1)]);
        TrainingProfile rangedFocus = new(2, "scout_focus_ranged", [new TrainingProfileEntry(TestSkills.Ranged, 1)]);
        TrainingProfile physicalFocus = new(3, "scout_focus_physical", [new TrainingProfileEntry(Attribute.Strength, 1)]);
        TrainingProfile vehicleFocus = new(4, "scout_focus_vehicles", [new TrainingProfileEntry(TestSkills.Stealth, 1)]);
        TrainingProfile balanced = new(
            5,
            "scout_focus_balanced",
            [
                new TrainingProfileEntry(TestSkills.Melee, 1),
                new TrainingProfileEntry(TestSkills.Ranged, 1)
            ]);
        Soldier leader = TestModelFactory.CreateSoldier(
            TestModelFactory.SergeantTemplate,
            skills: new Skill(TestSkills.Leadership, 4));
        Soldier scout = TestModelFactory.CreateSoldier();
        var squad = TestModelFactory.CreateSquad("Scout Squad", leader, scout);
        SoldierTrainingCalculator calculator = new(
            [TestSkills.Ranged, TestSkills.Melee, TestSkills.Stealth, TestSkills.Leadership, new BaseSkill(6, SkillCategory.Professional, "Teaching", Attribute.Intelligence, 0, SkillRoleKeys.Teaching)],
            [meleeFocus, rangedFocus, physicalFocus, vehicleFocus, balanced],
            scoutTrainingOptions:
            [
                new ScoutTrainingOption(ScoutTrainingOptionKeys.Melee, "Melee", meleeFocus),
                new ScoutTrainingOption(ScoutTrainingOptionKeys.Ranged, "Ranged", rangedFocus),
                new ScoutTrainingOption(ScoutTrainingOptionKeys.Physical, "Physical", physicalFocus),
                new ScoutTrainingOption(ScoutTrainingOptionKeys.Vehicles, "Vehicles", vehicleFocus),
                new ScoutTrainingOption(ScoutTrainingOptionKeys.Balanced, "Balanced", balanced)
            ]);

        calculator.TrainScouts(
            [squad],
            new Dictionary<int, string> { [squad.Id] = ScoutTrainingOptionKeys.Balanced });

        Assert.True(scout.Skills.Single(s => s.BaseSkill == TestSkills.Melee).PointsInvested > 0);
        Assert.True(scout.Skills.Single(s => s.BaseSkill == TestSkills.Ranged).PointsInvested > 0);
    }

    [Fact]
    public void TrainScouts_CapableSergeantDrillsAtBonusRate()
    {
        Assert.Equal(8.8f, TrainScoutsAndGetMeleePoints(sergeantTeachingPoints: 4, points: 8), precision: 4);
    }

    [Fact]
    public void TrainScouts_SubParSergeantDrillsAtBaselineRate()
    {
        Assert.Equal(8f, TrainScoutsAndGetMeleePoints(sergeantTeachingPoints: 0, points: 8), precision: 4);
    }

    // Regression: a scout squad whose sergeant was killed in battle used to throw an NRE here,
    // which killed the whole turn and left the campaign unable to advance.
    [Fact]
    public void TrainScouts_SquadWithNoSergeantDrillsAtReducedRateRatherThanThrowing()
    {
        Assert.Equal(6f, TrainScoutsAndGetMeleePoints(sergeantTeachingPoints: null, points: 8), precision: 4);
    }

    // Melee is the only focus, so the points the scout banks equal the squad's learning rate
    // directly. A null sergeantTeachingPoints means the squad has no sergeant at all.
    private static float TrainScoutsAndGetMeleePoints(float? sergeantTeachingPoints, float points)
    {
        BaseSkill teaching = new(6, SkillCategory.Professional, "Teaching", Attribute.Intelligence, 0, SkillRoleKeys.Teaching);
        TrainingProfile meleeFocus = new(
            1, "scout_focus_melee", [new TrainingProfileEntry(TestSkills.Melee, 1)]);
        Soldier scout = TestModelFactory.CreateSoldier(name: "Scout");
        Squad squad = sergeantTeachingPoints.HasValue
            ? TestModelFactory.CreateSquad(
                "Scout Squad",
                TestModelFactory.CreateSoldier(
                    TestModelFactory.SergeantTemplate,
                    "Sergeant",
                    skills: new Skill(teaching, sergeantTeachingPoints.Value)),
                scout)
            : TestModelFactory.CreateSquad("Scout Squad", scout);
        SoldierTrainingCalculator calculator = new(
            [TestSkills.Melee, teaching],
            [meleeFocus],
            scoutTrainingOptions:
            [new ScoutTrainingOption(ScoutTrainingOptionKeys.Melee, "Melee", meleeFocus)]);

        calculator.TrainScouts(
            [squad],
            new Dictionary<int, string> { [squad.Id] = ScoutTrainingOptionKeys.Melee },
            points);

        return scout.Skills.Single(s => s.BaseSkill == TestSkills.Melee).PointsInvested;
    }
}
