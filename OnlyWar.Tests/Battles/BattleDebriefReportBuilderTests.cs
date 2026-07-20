using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace OnlyWar.Tests.Battles;

public class BattleDebriefReportBuilderTests
{
    [Fact]
    public void Build_ReportsBothSidesDeathsAndPlayerMedicalOutcomes()
    {
        UnitTemplate companyTemplate = new(901, "Company", false, [], []);
        Unit company = new("Third Company", companyTemplate);
        Squad playerSquad = new("Aquila Squad", company, TestModelFactory.SquadTemplate);
        company.AddSquad(playerSquad);
        PlayerSoldier dead = CreatePlayerSoldier(91_001, "Cassian");
        PlayerSoldier replacement = CreatePlayerSoldier(91_002, "Marius");
        PlayerSoldier recovering = CreatePlayerSoldier(91_003, "Titus");
        playerSquad.AddSquadMember(dead);
        playerSquad.AddSquadMember(replacement);
        playerSquad.AddSquadMember(recovering);

        for (int i = 0; i < 3; i++)
        {
            replacement.Body.HitLocations.First(location => location.Template.Name == "Left Arm")
                .Wounds.AddWound(WoundLevel.Critical);
        }
        recovering.Body.HitLocations.First(location => location.Template.Name == "Torso")
            .Wounds.AddWound(WoundLevel.Moderate);

        Soldier enemy = TestModelFactory.CreateSoldier(name: "Cultist");
        enemy.Id = 91_004;
        Squad enemySquad = TestModelFactory.CreateSquad("Cult Mob", enemy);
        BattleSquad playerBattleSquad = new(true, playerSquad);
        BattleSquad enemyBattleSquad = new(false, enemySquad);
        foreach (BattleSoldier soldier in playerBattleSquad.Soldiers.Concat(enemyBattleSquad.Soldiers))
        {
            soldier.TopLeft = (soldier.Soldier.Id, 1);
        }

        BattleState state = new(
            new Dictionary<int, BattleSquad> { [playerBattleSquad.Id] = playerBattleSquad },
            new Dictionary<int, BattleSquad> { [enemyBattleSquad.Id] = enemyBattleSquad });
        BattleHistory history = new();
        history.Turns.Add(new BattleTurn(state, []));
        history.DamagedSoldierIds.UnionWith([dead.Id, replacement.Id, recovering.Id]);
        history.KilledSoldierIds.UnionWith([dead.Id, enemy.Id]);

        BattleDebriefReport report = BattleDebriefReportBuilder.Build(history);

        Assert.Equal(1, report.PlayerDeaths);
        Assert.Equal(1, report.OpposingDeaths);
        Assert.Equal(3, report.PlayerCasualties.Count);
        Assert.Equal(BattleCasualtyDisposition.Dead,
            report.PlayerCasualties.Single(entry => entry.SoldierId == dead.Id).Disposition);
        BattleCasualtyEntry replacementEntry = report.PlayerCasualties.Single(entry => entry.SoldierId == replacement.Id);
        Assert.Equal(BattleCasualtyDisposition.ReplacementRequired, replacementEntry.Disposition);
        Assert.Equal("Aquila Squad", replacementEntry.Squad);
        Assert.Equal("Third Company", replacementEntry.Company);
        Assert.Equal("Test Marine", replacementEntry.Rank);
        BattleCasualtyEntry recoveringEntry = report.PlayerCasualties.Single(entry => entry.SoldierId == recovering.Id);
        Assert.Equal(BattleCasualtyDisposition.Recovering, recoveringEntry.Disposition);
        Assert.True(recoveringEntry.RecoveryWeeks > 0);
    }

    [Fact]
    public void Build_ExcludesUndamagedPlayerSoldiers()
    {
        PlayerSoldier healthy = CreatePlayerSoldier(91_010, "Valens");
        Squad squad = new("Healthy Squad", null, TestModelFactory.SquadTemplate);
        squad.AddSquadMember(healthy);
        BattleSquad battleSquad = new(true, squad);
        battleSquad.Soldiers[0].TopLeft = new System.ValueTuple<int, int>(1, 1);
        BattleState state = new(
            new Dictionary<int, BattleSquad> { [battleSquad.Id] = battleSquad },
            new Dictionary<int, BattleSquad>());
        BattleHistory history = new();
        history.Turns.Add(new BattleTurn(state, []));

        BattleDebriefReport report = BattleDebriefReportBuilder.Build(history);

        Assert.Empty(report.PlayerCasualties);
        Assert.Equal(0, report.PlayerDeaths);
    }

    private static PlayerSoldier CreatePlayerSoldier(int id, string name)
    {
        Soldier soldier = TestModelFactory.CreateSoldier(name: name);
        soldier.Id = id;
        return new PlayerSoldier(soldier, name);
    }
}
