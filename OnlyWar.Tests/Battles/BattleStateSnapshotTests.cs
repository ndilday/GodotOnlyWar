using System.Collections.Generic;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Battles;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

public class BattleStateSnapshotTests
{
    [Fact]
    public void Capture_RetainsCompactHistoricalValuesWhenLiveStateChanges()
    {
        BattleSquad squad = new(false, TestModelFactory.CreateSquad(
            "Snapshot Squad",
            TestModelFactory.CreateSoldier(name: "Snapshot Soldier")));
        BattleSoldier sourceSoldier = squad.Soldiers[0];
        sourceSoldier.TopLeft = new System.Tuple<int, int>(3, 7);
        sourceSoldier.TurnsRunning = 2;
        BattleState liveState = new(
            new Dictionary<int, BattleSquad> { [squad.Id] = squad },
            new Dictionary<int, BattleSquad>());
        BattleSoldier liveSoldier = liveState.GetSoldier(sourceSoldier.Soldier.Id);

        BattleTurn turn = new(liveState, []);
        BattleSoldierSnapshot snapshot = turn.State.Soldiers[liveSoldier.Soldier.Id];

        liveSoldier.TopLeft = new System.Tuple<int, int>(20, 20);
        liveSoldier.TurnsRunning = 9;
        liveState.GetSquad(squad.Id).Soldiers.Clear();
        liveState.AdvanceTurn();

        Assert.Equal(0, turn.TurnNumber);
        Assert.Equal(3, snapshot.X);
        Assert.Equal(7, snapshot.Y);
        Assert.Equal(2, snapshot.TurnsRunning);
        Assert.Single(turn.State.AttackerSquads[squad.Id].Soldiers);
    }

    [Fact]
    public void RemoveSoldier_PrunesMutableLookupWithoutChangingCapturedTurn()
    {
        BattleSquad squad = new(false, TestModelFactory.CreateSquad(
            "Casualty Squad",
            TestModelFactory.CreateSoldier(name: "Casualty")));
        BattleSoldier liveSoldier = squad.Soldiers[0];
        liveSoldier.TopLeft = new System.Tuple<int, int>(0, 0);
        BattleState liveState = new(
            new Dictionary<int, BattleSquad> { [squad.Id] = squad },
            new Dictionary<int, BattleSquad>());
        BattleTurn captured = new(liveState, []);

        liveState.RemoveSoldier(liveSoldier.Soldier.Id);

        Assert.DoesNotContain(liveSoldier.Soldier.Id, liveState.Soldiers.Keys);
        Assert.Contains(liveSoldier.Soldier.Id, captured.State.Soldiers.Keys);
    }
}
