using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Xunit;

namespace OnlyWar.Tests.Domain;

public sealed class SquadLineageTests
{
    [Fact]
    public void FormatterAndAllocator_CreateStableFormalDesignations()
    {
        Faction player = CreatePlayerFaction();
        SquadTemplate tactical = CreateTemplate(player, SquadTypes.None, "Tactical Squad");
        Unit company = CreateCompany(player, tactical, "Fourth Company");

        Squad first = new("Legacy Name", company, tactical);
        company.AddSquad(first);
        Squad second = new("Other Name", company, tactical);
        company.AddSquad(second);

        Assert.Equal(1, first.FormationOrdinal);
        Assert.Equal("I Tactical Squad, 4 Co.", first.Name);
        Assert.Equal(2, second.FormationOrdinal);
        Assert.Equal("II Tactical Squad, 4 Co.", second.Name);
        Assert.Equal("XIV", SquadDesignationFormatter.ToRoman(14));
    }

    [Fact]
    public void BattleCompanyAllocator_ReservesNinthForAssaultAndTenthForDevastator()
    {
        Faction player = CreatePlayerFaction();
        SquadTemplate tactical = CreateTemplate(player, SquadTypes.None, "Tactical Squad");
        SquadTemplate assault = CreateTemplate(player, SquadTypes.Fast, "Assault Squad");
        SquadTemplate devastator = CreateTemplate(player, SquadTypes.Heavy, "Devastator Squad");
        UnitTemplate template = new(9200, "Company", false, null,
            [new SquadTemplateSlot(tactical, 0, 8),
             new SquadTemplateSlot(assault, 0, 1),
             new SquadTemplateSlot(devastator, 0, 1)]);
        template.Faction = player;
        Unit company = new(9300, "Fourth Company", template, []);

        Squad assaultSquad = new(assault.Name, company, assault);
        company.AddSquad(assaultSquad);
        Squad devastatorSquad = new(devastator.Name, company, devastator);
        company.AddSquad(devastatorSquad);
        for (int i = 0; i < 8; i++)
        {
            Squad squad = new(tactical.Name, company, tactical);
            company.AddSquad(squad);
            Assert.Equal(i + 1, squad.FormationOrdinal);
        }

        Assert.Equal(9, assaultSquad.FormationOrdinal);
        Assert.Equal("IX Assault Squad, 4 Co.", assaultSquad.Name);
        Assert.Equal(10, devastatorSquad.FormationOrdinal);
        Assert.Equal("X Devastator Squad, 4 Co.", devastatorSquad.Name);
    }

    [Fact]
    public void EmptyNonScoutIsRetainedAndDetachedFromDeployment()
    {
        Faction player = CreatePlayerFaction();
        SquadTemplate tactical = CreateTemplate(player, SquadTypes.None, "Tactical Squad");
        Unit company = CreateCompany(player, tactical, "Fourth Company");
        Squad squad = new("Line", company, tactical);
        company.AddSquad(squad);
        Army army = new("Chapter", null, null, company, []);
        army.PopulateSquadMap();

        EmptySquadLifecycleResult result = new SquadLifecycleService(army).HandleEmptySquad(squad);

        Assert.Equal(EmptySquadLifecycleResult.Retained, result);
        Assert.Contains(squad, company.Squads);
        Assert.True(army.SquadMap.ContainsKey(squad.Id));
    }

    [Theory]
    [InlineData(false, EmptySquadLifecycleResult.Discarded)]
    [InlineData(true, EmptySquadLifecycleResult.Retained)]
    public void EmptyScoutRetentionDependsOnBattleHistory(
        bool hasHistory,
        EmptySquadLifecycleResult expected)
    {
        Faction player = CreatePlayerFaction();
        SquadTemplate scouts = CreateTemplate(player, SquadTypes.Scout, "Scout Squad");
        Unit company = CreateCompany(player, scouts, "Tenth Company");
        Squad squad = new("Scout Line", company, scouts) { HasBattleHistory = hasHistory };
        company.AddSquad(squad);
        Army army = new("Chapter", null, null, company, []);
        army.PopulateSquadMap();
        RecruitmentProgram recruitment = new();
        recruitment.Procedures.Add(new RecruitmentProcedure { ReservedSquadId = squad.Id });

        EmptySquadLifecycleResult result = new SquadLifecycleService(
            army, recruitment).HandleEmptySquad(squad);

        Assert.Equal(expected, result);
        Assert.Equal(hasHistory, company.Squads.Contains(squad));
        Assert.Equal(hasHistory, army.SquadMap.ContainsKey(squad.Id));
        Assert.Equal(hasHistory ? squad.Id : null, recruitment.Procedures[0].ReservedSquadId);
    }

    private static Faction CreatePlayerFaction() => new(
        9001, "Test Chapter", Color.Gold, true, false,
        FactionBehavior.None, GrowthType.None,
        new Dictionary<int, OnlyWar.Models.Soldiers.Species>(),
        new Dictionary<int, OnlyWar.Models.Soldiers.SoldierTemplate>(),
        new Dictionary<int, SquadTemplate>(),
        new Dictionary<int, UnitTemplate>(),
        new Dictionary<int, OnlyWar.Models.Fleets.BoatTemplate>(),
        new Dictionary<int, OnlyWar.Models.Fleets.ShipTemplate>(),
        new Dictionary<int, OnlyWar.Models.Fleets.FleetTemplate>());

    private static SquadTemplate CreateTemplate(Faction faction, SquadTypes type, string name)
    {
        SquadTemplate template = new(
            9100 + (int)type, name, TestModelFactory.DefaultWeapons, [],
            TestModelFactory.TestArmor, [], type);
        template.Faction = faction;
        return template;
    }

    private static Unit CreateCompany(Faction faction, SquadTemplate squad, string name)
    {
        UnitTemplate template = new(9200, "Company", false, null,
            [new SquadTemplateSlot(squad, 0, 10)]);
        template.Faction = faction;
        return new Unit(9300, name, template, []);
    }
}
