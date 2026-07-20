using System.Collections.Generic;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Battles;
using OnlyWar.Tests.Fixtures;
using Xunit;
using System.Linq;

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
        sourceSoldier.TopLeft = (3, 7);
        sourceSoldier.LeftoverMovement = 1.25f;
        sourceSoldier.TurnsRunning = 2;
        squad.MovementTier = SquadMovementTier.Walk;
        BattleState liveState = new(
            new Dictionary<int, BattleSquad> { [squad.Id] = squad },
            new Dictionary<int, BattleSquad>());
        BattleSoldier liveSoldier = liveState.GetSoldier(sourceSoldier.Soldier.Id);

        BattleTurn turn = new(liveState, []);
        BattleSoldierSnapshot snapshot = turn.State.Soldiers[liveSoldier.Soldier.Id];

        liveSoldier.TopLeft = (20, 20);
        liveSoldier.LeftoverMovement = 4.5f;
        liveSoldier.TurnsRunning = 9;
        liveState.GetSquad(squad.Id).MovementTier = SquadMovementTier.Run;
        liveState.GetSquad(squad.Id).Soldiers.Clear();
        liveState.AdvanceTurn();

        Assert.Equal(0, turn.TurnNumber);
        Assert.Equal(3, snapshot.X);
        Assert.Equal(7, snapshot.Y);
        Assert.Equal(1.25f, snapshot.LeftoverMovement);
        Assert.Equal(2, snapshot.TurnsRunning);
        Assert.Equal(SquadMovementTier.Walk, turn.State.AttackerSquads[squad.Id].MovementTier);
        Assert.Single(turn.State.AttackerSquads[squad.Id].Soldiers);
    }

    [Fact]
    public void Capture_RetainsDisengagedRosterAndWithdrawalState()
    {
        BattleSquad squad = new(false, TestModelFactory.CreateSquad(
            "Withdrawing Squad",
            TestModelFactory.CreateSoldier(name: "Survivor")));
        BattleSoldier soldier = squad.Soldiers[0];
        soldier.TopLeft = (5, 6);
        BattleState state = new(
            new Dictionary<int, BattleSquad> { [squad.Id] = squad },
            new Dictionary<int, BattleSquad>());
        BattleSquad stateSquad = state.GetSquad(squad.Id);
        stateSquad.WithdrawalRole = WithdrawalRole.Bound;
        state.AttackerSide.Intent = BattleSideIntent.FightingWithdrawal;
        state.AttackerSide.WithdrawalHeading = 6;
        state.DisengageSquad(stateSquad);

        BattleTurn turn = new(state, [],
        [
            new BattleEvent(BattleEventType.SquadDisengaged, state.TurnNumber,
                BattleSide.Attacker, squad.Id, [], "Withdrawing Squad broke contact.")
        ]);

        BattleSquadSnapshot snapshot = turn.State.AttackerSquads[squad.Id];
        Assert.Equal(BattleSquadStatus.Disengaged, snapshot.Status);
        Assert.Equal(WithdrawalRole.None, snapshot.WithdrawalRole);
        Assert.Single(snapshot.Soldiers);
        Assert.Single(turn.State.Soldiers);
        Assert.Empty(state.Soldiers);
        Assert.Equal(BattleSideIntent.FightingWithdrawal, turn.State.AttackerSide.Intent);
        Assert.Equal((ushort)6, turn.State.AttackerSide.WithdrawalHeading);
        BattleEvent battleEvent = Assert.Single(turn.Events);
        Assert.Equal(BattleEventType.SquadDisengaged, battleEvent.Type);
        Assert.Equal(squad.Id, battleEvent.PrimarySquadId);
    }

    [Fact]
    public void BattleOutcome_CopiesAndOrdersStatusIds()
    {
        BattleOutcome outcome = new(
            BattleEndReason.Withdrawal,
            BattleSide.Opposing,
            disengagedSquadIds: [9, 2, 9],
            eliminatedSquadIds: [7],
            routingSquadIds: [5],
            rearGuardSquadIds: [3]);
        BattleHistory history = new() { Outcome = outcome };

        Assert.Equal(BattleEndReason.Withdrawal, history.Outcome.EndReason);
        Assert.Equal(BattleSide.Opposing, history.Outcome.SideHoldingField);
        Assert.Equal([2, 9], history.Outcome.DisengagedSquadIds.ToArray());
        Assert.Equal([7], history.Outcome.EliminatedSquadIds.ToArray());
        Assert.Equal([5], history.Outcome.RoutingSquadIds.ToArray());
        Assert.Equal([3], history.Outcome.RearGuardSquadIds.ToArray());
    }

    [Fact]
    public void RemoveSoldier_PrunesMutableLookupWithoutChangingCapturedTurn()
    {
        BattleSquad squad = new(false, TestModelFactory.CreateSquad(
            "Casualty Squad",
            TestModelFactory.CreateSoldier(name: "Casualty")));
        BattleSoldier liveSoldier = squad.Soldiers[0];
        liveSoldier.TopLeft = (0, 0);
        BattleState liveState = new(
            new Dictionary<int, BattleSquad> { [squad.Id] = squad },
            new Dictionary<int, BattleSquad>());
        BattleTurn captured = new(liveState, []);

        liveState.RemoveSoldier(liveSoldier.Soldier.Id);

        Assert.DoesNotContain(liveSoldier.Soldier.Id, liveState.Soldiers.Keys);
        Assert.Contains(liveSoldier.Soldier.Id, captured.State.Soldiers.Keys);
    }
}
