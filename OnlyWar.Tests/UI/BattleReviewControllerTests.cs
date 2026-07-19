using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Battles;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.UI;

public class BattleReviewControllerTests
{
    private static (BattleState State, int SquadId) CreateState()
    {
        BattleSquad squad = new(false, TestModelFactory.CreateSquad(
            "Review Squad", TestModelFactory.CreateSoldier(name: "Reviewer")));
        squad.Soldiers[0].TopLeft = new System.Tuple<int, int>(2, 4);
        return (new BattleState(new Dictionary<int, BattleSquad> { [squad.Id] = squad },
            new Dictionary<int, BattleSquad>()), squad.Id);
    }

    [Fact]
    public void TypedDisengagement_ProducesDepartureAndHidesSquad()
    {
        (BattleState state, int squadId) = CreateState();
        BattleSquadSnapshot previous = new BattleTurn(state, []).State.AttackerSquads[squadId];
        state.DisengageSquad(state.GetSquad(squadId));
        BattleTurn currentTurn = new(state, [],
        [
            new BattleEvent(BattleEventType.SquadDisengaged, state.TurnNumber,
                BattleSide.Attacker, squadId, [], "Review Squad broke contact.")
        ]);
        BattleSquadSnapshot current = currentTurn.State.AttackerSquads[squadId];

        Assert.Equal(ReplaySquadOverlay.Departure,
            BattleReviewController.ClassifySquadOverlay(previous, current, currentTurn.Events));
        Assert.False(BattleReviewController.ShouldDrawSquad(current));
    }

    [Fact]
    public void RoutingRoleOrEvent_ProducesRoutWithoutRequiringDisappearance()
    {
        (BattleState state, int squadId) = CreateState();
        BattleSquadSnapshot previous = new BattleTurn(state, []).State.AttackerSquads[squadId];
        state.GetSquad(squadId).WithdrawalRole = WithdrawalRole.Routing;
        BattleTurn currentTurn = new(state, [],
        [
            new BattleEvent(BattleEventType.SquadRouted, state.TurnNumber,
                BattleSide.Attacker, squadId, [], "Review Squad routed.")
        ]);
        BattleSquadSnapshot current = currentTurn.State.AttackerSquads[squadId];

        Assert.Equal(ReplaySquadOverlay.Rout,
            BattleReviewController.ClassifySquadOverlay(previous, current, currentTurn.Events));
        Assert.True(BattleReviewController.ShouldDrawSquad(current));
    }

    [Fact]
    public void EliminatedSquad_IsCasualtyRatherThanRout()
    {
        (BattleState state, int squadId) = CreateState();
        BattleSquadSnapshot previous = new BattleTurn(state, []).State.AttackerSquads[squadId];
        state.RemoveSquad(state.GetSquad(squadId));
        BattleSquadSnapshot current = new BattleTurn(state, []).State.AttackerSquads[squadId];

        Assert.Equal(ReplaySquadOverlay.Casualty,
            BattleReviewController.ClassifySquadOverlay(previous, current, []));
        Assert.False(BattleReviewController.ShouldDrawSquad(current));
    }

    [Fact]
    public void LegacyDisappearance_KeepsRoutFallback()
    {
        (BattleState state, int squadId) = CreateState();
        BattleSquadSnapshot previous = new BattleTurn(state, []).State.AttackerSquads[squadId];

        Assert.Equal(ReplaySquadOverlay.Rout,
            BattleReviewController.ClassifySquadOverlay(previous, null, []));
    }

    [Fact]
    public void DeploymentFraming_ExcludesSquadsThatAreNotDeployed()
    {
        BattleSquad deployed = new(false, TestModelFactory.CreateSquad(
            "Deployed Squad", TestModelFactory.CreateSoldier(name: "Deployed")));
        deployed.Soldiers[0].TopLeft = new System.Tuple<int, int>(3, 5);
        BattleSquad withdrawn = new(false, TestModelFactory.CreateSquad(
            "Withdrawn Squad", TestModelFactory.CreateSoldier(name: "Withdrawn")));
        withdrawn.Soldiers[0].TopLeft = new System.Tuple<int, int>(200, 200);
        BattleState state = new(
            new Dictionary<int, BattleSquad>
            {
                [deployed.Id] = deployed,
                [withdrawn.Id] = withdrawn
            },
            new Dictionary<int, BattleSquad>());
        state.DisengageSquad(state.GetSquad(withdrawn.Id));
        BattleStateSnapshot snapshot = new BattleTurn(state, []).State;

        List<System.Tuple<int, int>> positions =
            BattleReviewController.GetDeployedBoundaryPositions(snapshot).ToList();

        Assert.NotEmpty(positions);
        Assert.All(positions, position =>
        {
            Assert.InRange(position.Item1, -10, 10);
            Assert.InRange(position.Item2, -10, 10);
        });
    }
}
