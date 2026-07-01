using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class SoldierFilterServiceTests
{
    private readonly SoldierFilterService _service = new();
    private static readonly Date CurrentDate = new(41, 1000, 1);

    // Veteran marine: enlisted 990, promoted 995, transferred 998, holds a "Sword" honor.
    // -> service 10y, rank 5y, squad 2y.
    private PlayerSoldier BuildVeteran(Squad squad)
    {
        PlayerSoldier soldier = AddPlayerSoldier(squad, TestModelFactory.MarineTemplate, "Brother Marius");
        soldier.AddEvent(new SoldierEvent(new Date(41, 990, 1), SoldierEventType.AcceptedToTraining, "accepted"));
        soldier.AddEvent(new SoldierEvent(new Date(41, 995, 1), SoldierEventType.Promotion, "promoted"));
        soldier.AddEvent(new SoldierEvent(new Date(41, 998, 1), SoldierEventType.Transfer, "transferred"));
        soldier.AddAward(new SoldierAward(new Date(41, 996, 1), "Bronze Sword of the Emperor", "Sword", 1));
        return soldier;
    }

    // Fresh sergeant: enlisted 999, never promoted or transferred, no honors.
    // -> service / rank / squad all 1y (rank and squad fall back to enlistment).
    private PlayerSoldier BuildRecruit(Squad squad)
    {
        PlayerSoldier soldier = AddPlayerSoldier(squad, TestModelFactory.SergeantTemplate, "Sergeant Kaus");
        soldier.AddEvent(new SoldierEvent(new Date(41, 999, 1), SoldierEventType.AcceptedToTraining, "accepted"));
        return soldier;
    }

    [Fact]
    public void Apply_WithNoConditions_ReturnsEveryone()
    {
        Squad squad = CreateSquad();
        PlayerSoldier a = BuildVeteran(squad);
        PlayerSoldier b = BuildRecruit(squad);

        var result = _service.Apply([a, b], [], CurrentDate);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Apply_RankEqualsAndNotEquals_FiltersByRole()
    {
        Squad squad = CreateSquad();
        PlayerSoldier a = BuildVeteran(squad);
        PlayerSoldier b = BuildRecruit(squad);

        var equals = _service.Apply([a, b],
            [Condition(SoldierFilterField.Rank, SoldierFilterOperator.Equals, text: "Test Sergeant")], CurrentDate);
        var notEquals = _service.Apply([a, b],
            [Condition(SoldierFilterField.Rank, SoldierFilterOperator.NotEquals, text: "Test Sergeant")], CurrentDate);

        Assert.Equal([b], equals);
        Assert.Equal([a], notEquals);
    }

    [Fact]
    public void Apply_HonorHasAndDoesNotHave_FiltersByAwardTypeAndLevel()
    {
        Squad squad = CreateSquad();
        PlayerSoldier a = BuildVeteran(squad);
        PlayerSoldier b = BuildRecruit(squad);
        PlayerSoldier c = AddPlayerSoldier(squad, TestModelFactory.MarineTemplate, "Brother Severan");
        c.AddAward(new SoldierAward(new Date(41, 997, 1), "Silver Sword of the Emperor", "Sword", 2));

        var has = _service.Apply([a, b],
            [Condition(SoldierFilterField.Honor, SoldierFilterOperator.Has,
                text: SoldierHonorFilterOption.ToValue("Sword", 1))], CurrentDate);
        var lacks = _service.Apply([a, b, c],
            [Condition(SoldierFilterField.Honor, SoldierFilterOperator.DoesNotHave,
                text: SoldierHonorFilterOption.ToValue("Sword", 1))], CurrentDate);

        Assert.Equal([a], has);
        Assert.Equal([b, c], lacks);
    }

    [Fact]
    public void Apply_TimeInService_ComparesAgainstEnlistment()
    {
        Squad squad = CreateSquad();
        PlayerSoldier a = BuildVeteran(squad);
        PlayerSoldier b = BuildRecruit(squad);

        var atLeast = _service.Apply([a, b],
            [Condition(SoldierFilterField.TimeInService, SoldierFilterOperator.AtLeast, number: 5, unit: SoldierDurationUnit.Years)], CurrentDate);
        var atMost = _service.Apply([a, b],
            [Condition(SoldierFilterField.TimeInService, SoldierFilterOperator.AtMost, number: 5, unit: SoldierDurationUnit.Years)], CurrentDate);

        Assert.Equal([a], atLeast);
        Assert.Equal([b], atMost);
    }

    [Fact]
    public void Apply_TimeInRank_AnchorsToLatestPromotion()
    {
        Squad squad = CreateSquad();
        PlayerSoldier a = BuildVeteran(squad); // promoted 995 -> 5y in rank
        PlayerSoldier b = BuildRecruit(squad); // never promoted -> 1y (enlistment fallback)

        var atLeast = _service.Apply([a, b],
            [Condition(SoldierFilterField.TimeInRank, SoldierFilterOperator.AtLeast, number: 5, unit: SoldierDurationUnit.Years)], CurrentDate);

        Assert.Equal([a], atLeast);
    }

    [Fact]
    public void Apply_TimeInSquad_AnchorsToLatestTransfer()
    {
        Squad squad = CreateSquad();
        PlayerSoldier a = BuildVeteran(squad); // transferred 998 -> 2y in squad
        PlayerSoldier b = BuildRecruit(squad); // never transferred -> 1y (enlistment fallback)

        var atLeast = _service.Apply([a, b],
            [Condition(SoldierFilterField.TimeInSquad, SoldierFilterOperator.AtLeast, number: 2, unit: SoldierDurationUnit.Years)], CurrentDate);

        Assert.Equal([a], atLeast);
    }

    [Fact]
    public void Apply_DurationInWeeks_UsesWeekThresholdDirectly()
    {
        Squad squad = CreateSquad();
        PlayerSoldier a = BuildVeteran(squad); // 520 weeks in service
        PlayerSoldier b = BuildRecruit(squad); // 52 weeks in service

        var atMost = _service.Apply([a, b],
            [Condition(SoldierFilterField.TimeInService, SoldierFilterOperator.AtMost, number: 60, unit: SoldierDurationUnit.Weeks)], CurrentDate);

        Assert.Equal([b], atMost);
    }

    [Fact]
    public void Apply_MultipleConditions_AreAndedTogether()
    {
        Squad squad = CreateSquad();
        PlayerSoldier a = BuildVeteran(squad);
        PlayerSoldier b = BuildRecruit(squad);

        var result = _service.Apply([a, b],
            [
                Condition(SoldierFilterField.Rank, SoldierFilterOperator.NotEquals, text: "Test Sergeant"),
                Condition(SoldierFilterField.Honor, SoldierFilterOperator.Has, text: "Sword")
            ], CurrentDate);

        Assert.Equal([a], result);
    }

    [Fact]
    public void GetAvailableRoles_ReturnsDistinctRolesMostSeniorFirst()
    {
        Squad squad = CreateSquad();
        PlayerSoldier a = BuildVeteran(squad);   // "Test Marine", rank 1
        PlayerSoldier b = BuildRecruit(squad);   // "Test Sergeant", rank 2

        var roles = _service.GetAvailableRoles([a, b]);

        Assert.Equal(["Test Sergeant", "Test Marine"], roles);
    }

    [Fact]
    public void GetAvailableHonors_ReturnsDistinctAwardTiersInScope()
    {
        Squad squad = CreateSquad();
        PlayerSoldier a = BuildVeteran(squad);
        PlayerSoldier b = BuildRecruit(squad);
        PlayerSoldier c = AddPlayerSoldier(squad, TestModelFactory.MarineTemplate, "Brother Severan");
        c.AddAward(new SoldierAward(new Date(41, 997, 1), "Silver Sword of the Emperor", "Sword", 2));

        var honors = _service.GetAvailableHonors([a, b]);
        var tieredHonors = _service.GetAvailableHonors([a, b, c]);

        Assert.Equal([SoldierHonorFilterOption.ToValue("Sword", 1)], honors.Select(option => option.Value));
        Assert.Equal(
            [SoldierHonorFilterOption.ToValue("Sword", 2), SoldierHonorFilterOption.ToValue("Sword", 1)],
            tieredHonors.Select(option => option.Value));
        Assert.Contains(tieredHonors, option => option.Label == "Silver Sword of the Emperor (Level 2)");
    }

    private static SoldierFilterCondition Condition(SoldierFilterField field, SoldierFilterOperator op,
        string text = null, int number = 0, SoldierDurationUnit unit = SoldierDurationUnit.Years)
    {
        return new SoldierFilterCondition
        {
            Field = field,
            Operator = op,
            TextValue = text,
            NumberValue = number,
            Unit = unit
        };
    }

    private static Squad CreateSquad()
    {
        SquadTemplate template = new(
            1,
            "Tactical Squad",
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            [new SquadTemplateElement(TestModelFactory.MarineTemplate, 0, 4)],
            SquadTypes.None,
            10);
        UnitTemplate unitTemplate = new(1, "Test Unit", true, [template], []);
        Unit unit = new(1, "Test Unit", unitTemplate, []);
        Squad squad = new("Test Squad", unit, template);
        unit.AddSquad(squad);
        return squad;
    }

    private static PlayerSoldier AddPlayerSoldier(Squad squad, SoldierTemplate template, string name)
    {
        PlayerSoldier soldier = new(TestModelFactory.CreateSoldier(template, name), name);
        squad.AddSquadMember(soldier);
        return soldier;
    }
}
