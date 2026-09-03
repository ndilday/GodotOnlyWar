using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class ChapterOperationalDoctrineTests
{
    [Fact]
    public void Defaults_UseMajorThreshold_LeaderRequirement_AndFiveMembers()
    {
        ChapterOperationalDoctrine doctrine = new();

        Assert.Equal(WoundLevel.Major, doctrine.InjuryThreshold);
        Assert.True(doctrine.RequireDutyReadySquadLeader);
        Assert.Equal(5, doctrine.MinimumDutyReadySquadStrength);
        Assert.False(doctrine.IsIncapacitatedPolicy);
        Assert.Equal("Incapacitated", ChapterOperationalDoctrine.DescribeThreshold(null));
    }

    [Fact]
    public void Thresholds_AreInclusive_AndIncapacitatedAddsNoWoundRestriction()
    {
        PlayerSoldier major = CreatePlayer("Major");
        Wound(major, "Torso", WoundLevel.Major);

        Assert.False(DutyReadinessService.Evaluate(
            major, new ChapterOperationalDoctrine(WoundLevel.Major)).IsDutyReady);
        Assert.Equal(
            DutyReadinessReasonCode.ChapterInjuryThreshold,
            DutyReadinessService.Evaluate(
                major, new ChapterOperationalDoctrine(WoundLevel.Major)).ReasonCode);
        Assert.True(DutyReadinessService.Evaluate(
            major, new ChapterOperationalDoctrine(null)).IsDutyReady);
    }

    [Fact]
    public void WorstWound_UsesTheHighestLocationBand_WithoutSummingLocations()
    {
        PlayerSoldier soldier = CreatePlayer("Multi-location");
        Wound(soldier, "Left Arm", WoundLevel.Minor);
        Wound(soldier, "Right Leg", WoundLevel.Minor);

        Assert.Equal(WoundLevel.Minor, DutyReadinessService.GetWorstWound(soldier));
        Assert.True(DutyReadinessService.Evaluate(
            soldier, new ChapterOperationalDoctrine(WoundLevel.Moderate)).IsDutyReady);
    }

    [Fact]
    public void PhysicalExclusions_OverrideEveryInjuryPolicy()
    {
        foreach (WoundLevel? threshold in ChapterOperationalDoctrine.InjuryThresholdOptions)
        {
            PlayerSoldier oneArm = CreatePlayer("One arm");
            Wound(oneArm, "Left Arm", oneArm.Body.HitLocations
                .Single(location => location.Template.Name == "Left Arm")
                .Template.CrippleWound);
            DutyReadinessEvaluation armResult = DutyReadinessService.Evaluate(
                oneArm, new ChapterOperationalDoctrine(threshold));
            Assert.False(armResult.IsDutyReady);
            Assert.Equal(DutyReadinessReasonCode.InsufficientFunctioningArms, armResult.ReasonCode);

            PlayerSoldier severed = CreatePlayer("Severed");
            HitLocation foot = severed.Body.HitLocations
                .Single(location => location.Template.Name == "Left Foot");
            foot.Wounds = new Wounds(foot.Template.SeverWound, 0);
            DutyReadinessEvaluation severedResult = DutyReadinessService.Evaluate(
                severed, new ChapterOperationalDoctrine(threshold));
            Assert.False(severedResult.IsDutyReady);
            Assert.Equal(DutyReadinessReasonCode.UntreatedSeverance, severedResult.ReasonCode);

            PlayerSoldier reserved = CreatePlayer("Reserved");
            reserved.IsUndergoingMedicalProcedure = true;
            DutyReadinessEvaluation procedureResult = DutyReadinessService.Evaluate(
                reserved, new ChapterOperationalDoctrine(threshold));
            Assert.False(procedureResult.IsDutyReady);
            Assert.Equal(DutyReadinessReasonCode.ProcedureReservation, procedureResult.ReasonCode);
        }
    }

    [Fact]
    public void SquadDoctrine_LeaderCountsTowardMinimum_AndUnavailableLeaderBlocks()
    {
        Squad squad = CreateLedSquad(5);
        ChapterOperationalDoctrine doctrine = new(null, true, 5);

        SquadReadinessSnapshot ready = SquadReadinessService.Evaluate(
            squad, doctrine: doctrine);
        Assert.Equal(5, ready.Strength.DutyReady);
        Assert.DoesNotContain(
            SquadReadinessBlocker.BelowMinimumDutyReadyStrength,
            ready.StructuralBlockers);
        Assert.True(ready.CanBeginDeployment);

        PlayerSoldier leader = (PlayerSoldier)squad.SquadLeader;
        Wound(leader, "Torso", WoundLevel.Major);
        SquadReadinessSnapshot leaderWithheld = SquadReadinessService.Evaluate(
            squad, doctrine: new ChapterOperationalDoctrine(WoundLevel.Major, true, 4));

        Assert.Contains(
            SquadReadinessBlocker.RequiredLeaderUnavailable,
            leaderWithheld.StructuralBlockers);
        Assert.False(leaderWithheld.CanBeginDeployment);
    }

    [Fact]
    public void SquadDoctrine_BlocksFourAndAllowsFiveDutyReadyMembers()
    {
        Squad squad = CreateLedSquad(4);
        ChapterOperationalDoctrine doctrine = new(null, false, 5);

        SquadReadinessSnapshot four = SquadReadinessService.Evaluate(
            squad, doctrine: doctrine);
        Assert.Equal(4, four.Strength.DutyReady);
        Assert.Contains(
            SquadReadinessBlocker.BelowMinimumDutyReadyStrength,
            four.StructuralBlockers);
        Assert.False(four.CanBeginDeployment);

        squad.AddSquadMember(TestModelFactory.CreateSoldier(name: "Fifth"));
        SquadReadinessSnapshot five = SquadReadinessService.Evaluate(
            squad, doctrine: doctrine);
        Assert.Equal(5, five.Strength.DutyReady);
        Assert.DoesNotContain(
            SquadReadinessBlocker.BelowMinimumDutyReadyStrength,
            five.StructuralBlockers);
        Assert.True(five.CanBeginDeployment);
    }

    [Fact]
    public void IndividualCharacter_IsEvaluatedWithoutSquadMinimumOrLeaderGate()
    {
        SquadTemplate administrativeTemplate = new(
            991,
            "Administrative Pool",
            TestModelFactory.DefaultWeapons,
            new List<SquadWeaponOption>(),
            TestModelFactory.TestArmor,
            [new SquadTemplateElement(TestModelFactory.MarineTemplate, 0, 10)],
            SquadTypes.Administrative);
        Squad administrative = new("Administrative Pool", null, administrativeTemplate);
        PlayerSoldier character = CreatePlayer("Attached Character");
        administrative.AddSquadMember(character);

        SquadReadinessSnapshot squadResult = SquadReadinessService.Evaluate(
            administrative, doctrine: new ChapterOperationalDoctrine(null, true, 5));

        Assert.Equal(SquadReadinessState.NotApplicable, squadResult.StructuralState);
        Assert.True(DutyReadinessService.IsDutyReady(
            character, new ChapterOperationalDoctrine(null)));
    }

    [Fact]
    public void BattleBoundary_UsesDutyReadyParticipants_AndKeepsWithheldMemberOut()
    {
        Squad squad = CreateLedSquad(6);
        PlayerSoldier withheld = (PlayerSoldier)squad.Members
            .OfType<PlayerSoldier>()
            .Last();
        Wound(withheld, "Torso", WoundLevel.Major);
        ChapterOperationalDoctrine doctrine = new(WoundLevel.Major, true, 5);

        BattleSquad battleSquad = BattleSquadFactory.Create(true, squad, doctrine);

        Assert.Equal(5, battleSquad.AbleSoldiers.Count);
        Assert.DoesNotContain(
            battleSquad.AbleSoldiers,
            soldier => soldier.Soldier.Id == withheld.Id);
        Assert.Equal(5, SquadStrengthSnapshotBuilder.Build(
            squad, doctrine: doctrine).DutyReady);
    }

    [Fact]
    public void AttachedCharacter_FormsIndependentOnePersonBattleElement()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        SquadTemplate poolTemplate = new(
            993,
            "Specialist Pool",
            TestModelFactory.DefaultWeapons,
            new List<SquadWeaponOption>(),
            TestModelFactory.TestArmor,
            [new SquadTemplateElement(TestModelFactory.MarineTemplate, 0, 10)],
            SquadTypes.PermitsIndividualDetachment)
        {
            Faction = fixture.Sector.PlayerForce.Faction
        };
        Squad pool = new("Specialist Pool", null, poolTemplate);
        PlayerSoldier character = CreatePlayer("Attached Character");
        pool.AddSquadMember(character);

        BattleSquad element = BattleSquadFactory.CreateAttachedCharacter(
            character,
            tacticalId: 700,
            fixture.Sector.PlayerForce.Faction,
            new ChapterOperationalDoctrine(WoundLevel.Major, true, 5));

        Assert.NotNull(element);
        Assert.Same(character, element.CampaignCharacter);
        Assert.Same(pool, element.CampaignSquad);
        Assert.Single(element.AbleSoldiers);

        Wound(character, "Torso", WoundLevel.Major);
        Assert.Null(BattleSquadFactory.CreateAttachedCharacter(
            character,
            tacticalId: 701,
            fixture.Sector.PlayerForce.Faction,
            new ChapterOperationalDoctrine(WoundLevel.Major, true, 5)));
    }

    private static Squad CreateLedSquad(int memberCount)
    {
        SquadTemplate template = new(
            992,
            "Led Formation",
            TestModelFactory.DefaultWeapons,
            new List<SquadWeaponOption>(),
            TestModelFactory.TestArmor,
            [
                new SquadTemplateElement(TestModelFactory.SergeantTemplate, 0, 1),
                new SquadTemplateElement(TestModelFactory.MarineTemplate, 0, 9)
            ],
            SquadTypes.None);
        Squad squad = new("Led Formation", null, template);
        squad.AddSquadMember(new PlayerSoldier(
            TestModelFactory.CreateSoldier(TestModelFactory.SergeantTemplate, "Sergeant"),
            "Sergeant"));
        for (int index = 1; index < memberCount; index++)
        {
            squad.AddSquadMember(new PlayerSoldier(
                TestModelFactory.CreateSoldier(name: $"Marine {index}"),
                $"Marine {index}"));
        }
        return squad;
    }

    private static PlayerSoldier CreatePlayer(string name) =>
        new(TestModelFactory.CreateSoldier(name: name), name);

    private static void Wound(PlayerSoldier soldier, string locationName, WoundLevel wound)
    {
        HitLocation location = soldier.Body.HitLocations
            .Single(item => item.Template.Name == locationName);
        location.Wounds.AddWound(wound);
    }

    private static void Wound(PlayerSoldier soldier, string locationName, uint woundTotal)
    {
        HitLocation location = soldier.Body.HitLocations
            .Single(item => item.Template.Name == locationName);
        location.Wounds = new Wounds(woundTotal, 0);
    }
}
