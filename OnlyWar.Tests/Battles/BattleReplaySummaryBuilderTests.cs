using System;
using System.Collections.Generic;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

public class BattleReplaySummaryBuilderTests
{
    private static int _nextSoldierId = 1;

    [Fact]
    public void Build_DefaultsSelectionToFirstPlayerFormation()
    {
        BattleSquad playerSquad = CreateBattleSquad(true, "Alpha", "Sergeant Alpha", "Brother Alpha");
        BattleSquad opposingSquad = CreateBattleSquad(false, "Cult Mob", "Cultist One", "Cultist Two");
        BattleHistory history = CreateHistory(playerSquad, opposingSquad);

        BattleReplayDisplay display = new BattleReplaySummaryBuilder().Build(history, 0);

        Assert.Equal(playerSquad.Id, display.SelectedFormationId);
        Assert.Equal("Alpha", display.SelectedFormation.Name);
        Assert.Equal("Battle Chronicle", display.BattleTitle);
        Assert.Equal("Deployment", display.PhaseLabel);
    }

    [Fact]
    public void Build_ForceHierarchyReportsCurrentStrengthAndLosses()
    {
        BattleSquad playerSquad = CreateBattleSquad(true, "Alpha", "Sergeant Alpha", "Brother Alpha");
        BattleSquad opposingSquad = CreateBattleSquad(false, "Cult Mob", "Cultist One", "Cultist Two", "Cultist Three");
        BattleState initialState = CreateState(playerSquad, opposingSquad);
        BattleState currentState = new(initialState);
        currentState.GetSquad(opposingSquad.Id).Soldiers.RemoveAt(0);
        BattleHistory history = new();
        history.Turns.Add(new BattleTurn(initialState, []));
        history.Turns.Add(new BattleTurn(currentState, []));

        BattleReplayDisplay display = new BattleReplaySummaryBuilder().Build(history, 1, opposingSquad.Id);

        BattleForceHierarchyNode opposingRoot = Assert.Single(display.ForceHierarchy, node => !node.IsPlayerForce);
        Assert.Equal(3, opposingRoot.StartingStrength);
        Assert.Equal(2, opposingRoot.CurrentStrength);
        Assert.Equal(1, opposingRoot.Losses);
        Assert.Equal(opposingSquad.Id, display.SelectedFormation.FormationId);
        Assert.Equal(1, display.SelectedFormation.Losses);
        Assert.Equal("Pressed", display.SelectedFormation.MoraleLabel);
    }

    [Fact]
    public void Build_CurrentTurnEventsAndCasualtyTimelineUseRequestedTurn()
    {
        BattleSquad playerSquad = CreateBattleSquad(true, "Alpha", "Sergeant Alpha", "Brother Alpha");
        BattleSquad opposingSquad = CreateBattleSquad(false, "Cult Mob", "Cultist One", "Cultist Two");
        BattleState initialState = CreateState(playerSquad, opposingSquad);
        BattleState currentState = new(initialState);
        currentState.GetSquad(opposingSquad.Id).Soldiers.RemoveAt(0);
        FakeAction action = new(playerSquad.Soldiers[0].Soldier.Id, "Sergeant Alpha opened fire.");
        BattleHistory history = new();
        history.Turns.Add(new BattleTurn(initialState, []));
        history.Turns.Add(new BattleTurn(currentState, [action]));

        BattleReplayDisplay display = new BattleReplaySummaryBuilder().Build(history, 1, playerSquad.Id);

        BattleEventEntry eventEntry = Assert.Single(display.CurrentTurnEvents);
        Assert.Equal("Sergeant Alpha", eventEntry.ActorName);
        Assert.Equal("Alpha", eventEntry.FormationName);
        Assert.Equal("Action", eventEntry.EventType);
        Assert.Contains("opened fire", eventEntry.Text);
        Assert.Equal(2, display.Timeline.Count);
        Assert.True(display.Timeline[1].IsSelected);
        Assert.Equal(1, display.CasualtiesByRound[1].OpposingLossesThisRound);
        Assert.Equal(1, display.CasualtiesByRound[1].OpposingCumulativeLosses);
    }

    private static BattleHistory CreateHistory(BattleSquad playerSquad, BattleSquad opposingSquad)
    {
        BattleHistory history = new();
        history.Turns.Add(new BattleTurn(CreateState(playerSquad, opposingSquad), []));
        return history;
    }

    private static BattleState CreateState(BattleSquad playerSquad, BattleSquad opposingSquad)
    {
        return new BattleState(
            new Dictionary<int, BattleSquad> { [playerSquad.Id] = playerSquad },
            new Dictionary<int, BattleSquad> { [opposingSquad.Id] = opposingSquad });
    }

    private static BattleSquad CreateBattleSquad(bool isPlayerSquad, string squadName, params string[] soldierNames)
    {
        List<Soldier> soldiers = [];
        for (int i = 0; i < soldierNames.Length; i++)
        {
            SoldierTemplate template = i == 0 ? TestModelFactory.SergeantTemplate : TestModelFactory.MarineTemplate;
            Soldier soldier = TestModelFactory.CreateSoldier(template, soldierNames[i]);
            soldier.Id = _nextSoldierId++;
            soldiers.Add(soldier);
        }

        BattleSquad squad = new(isPlayerSquad, TestModelFactory.CreateSquad(squadName, soldiers.ToArray()));
        for (int i = 0; i < squad.Soldiers.Count; i++)
        {
            squad.Soldiers[i].TopLeft = new Tuple<int, int>(i + 1, 2);
            squad.Soldiers[i].Orientation = 0;
        }

        return squad;
    }

    private sealed class FakeAction : IAction
    {
        private readonly string _description;

        public int ActorId { get; }

        public FakeAction(int actorId, string description)
        {
            ActorId = actorId;
            _description = description;
        }

        public void Execute(BattleState state)
        {
        }

        public string Description()
        {
            return _description;
        }
    }
}
