using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Orders;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

public class BattleStateTests
{
    [Fact]
    public void CopyConstructor_PreservesMovementStateIndependently()
    {
        BattleSquad squad = new(false, TestModelFactory.CreateSquad(
            "Moving Squad",
            TestModelFactory.CreateSoldier(name: "Walker")));
        PlaceSquad(squad, 0);
        BattleState original = new(
            new Dictionary<int, BattleSquad> { [squad.Id] = squad },
            new Dictionary<int, BattleSquad>());
        BattleSquad originalSquad = original.GetSquad(squad.Id);
        BattleSoldier originalSoldier = originalSquad.Soldiers[0];
        originalSquad.MovementTier = SquadMovementTier.Walk;
        originalSquad.Status = BattleSquadStatus.Disengaged;
        originalSquad.WithdrawalRole = WithdrawalRole.Bound;
        originalSoldier.LeftoverMovement = 2.4f;
        original.AttackerSide.Intent = BattleSideIntent.FightingWithdrawal;
        original.AttackerSide.WithdrawalHeading = 3;
        original.AttackerSide.CoveringSquadId = squad.Id;
        original.AttackerSide.WithdrawalStartedTurn = 4;

        BattleState copy = new(original);
        BattleSquad copiedSquad = copy.GetSquad(squad.Id);
        BattleSoldier copiedSoldier = copiedSquad.Soldiers[0];

        originalSquad.MovementTier = SquadMovementTier.Run;
        originalSoldier.LeftoverMovement = 0;

        Assert.Equal(SquadMovementTier.Walk, copiedSquad.MovementTier);
        Assert.Equal(BattleSquadStatus.Disengaged, copiedSquad.Status);
        Assert.Equal(WithdrawalRole.Bound, copiedSquad.WithdrawalRole);
        Assert.Equal(2.4f, copiedSoldier.LeftoverMovement);
        Assert.Same(copiedSquad, copiedSoldier.BattleSquad);
        Assert.Equal(BattleSideIntent.FightingWithdrawal, copy.AttackerSide.Intent);
        Assert.Equal((ushort)3, copy.AttackerSide.WithdrawalHeading);
        Assert.Equal(squad.Id, copy.AttackerSide.CoveringSquadId);
        Assert.Equal(4, copy.AttackerSide.WithdrawalStartedTurn);
        Assert.NotSame(original.AttackerSide, copy.AttackerSide);
    }

    [Fact]
    public void RemoveSquad_RemovesNpcSquadFromMissionSide()
    {
        BattleSquad npcAttacker = new(false, TestModelFactory.CreateSquad(
            "Cult Attackers",
            TestModelFactory.CreateSoldier(name: "Cultist")));
        BattleSquad playerDefender = new(true, TestModelFactory.CreateSquad(
            "Chapter Defenders",
            TestModelFactory.CreateSoldier(name: "Battle Brother")));
        PlaceSquad(npcAttacker, 0);
        PlaceSquad(playerDefender, 10);
        BattleState state = CreateState(npcAttacker, playerDefender);
        BattleSquad stateSquad = state.AttackerSquads[npcAttacker.Id];

        state.RemoveSquad(stateSquad);

        Assert.Empty(state.AttackerSquads);
        Assert.Same(stateSquad, state.AllAttackerSquads[npcAttacker.Id]);
        Assert.Equal(BattleSquadStatus.Eliminated, stateSquad.Status);
        Assert.Single(state.OpposingSquads);
    }

    [Fact]
    public void RemoveSquad_RemovesPlayerSquadFromOpposingSide()
    {
        BattleSquad npcAttacker = new(false, TestModelFactory.CreateSquad(
            "Cult Attackers",
            TestModelFactory.CreateSoldier(name: "Cultist")));
        BattleSquad playerDefender = new(true, TestModelFactory.CreateSquad(
            "Chapter Defenders",
            TestModelFactory.CreateSoldier(name: "Battle Brother")));
        PlaceSquad(npcAttacker, 0);
        PlaceSquad(playerDefender, 10);
        BattleState state = CreateState(npcAttacker, playerDefender);

        state.RemoveSquad(state.OpposingSquads[playerDefender.Id]);

        Assert.Single(state.AttackerSquads);
        Assert.Empty(state.OpposingSquads);
        Assert.Equal(BattleSquadStatus.Eliminated,
            state.AllOpposingSquads[playerDefender.Id].Status);
    }

    [Fact]
    public void RemoveSquad_CanBeRepeatedForMultipleCasualtiesFromWipedSquad()
    {
        BattleSquad squad = new(false, TestModelFactory.CreateSquad(
            "Wiped Squad",
            TestModelFactory.CreateSoldier(name: "First Casualty"),
            TestModelFactory.CreateSoldier(name: "Second Casualty")));
        PlaceSquad(squad, 0);
        BattleState state = new(
            new Dictionary<int, BattleSquad> { [squad.Id] = squad },
            new Dictionary<int, BattleSquad>());
        BattleSquad stateSquad = state.AttackerSquads[squad.Id];

        state.RemoveSquad(stateSquad);
        state.RemoveSquad(stateSquad);

        Assert.Empty(state.AttackerSquads);
        Assert.Single(state.AllAttackerSquads);
        Assert.Equal(BattleSquadStatus.Eliminated, stateSquad.Status);
    }

    [Fact]
    public void DisengageSquad_RetainsRosterButRemovesSquadFromSimulation()
    {
        BattleSquad squad = new(false, TestModelFactory.CreateSquad(
            "Escaping Squad",
            TestModelFactory.CreateSoldier(name: "Survivor")));
        PlaceSquad(squad, 0);
        BattleState state = new(
            new Dictionary<int, BattleSquad> { [squad.Id] = squad },
            new Dictionary<int, BattleSquad>());
        BattleSquad stateSquad = state.GetSquad(squad.Id);
        int soldierId = stateSquad.Soldiers[0].Soldier.Id;

        state.DisengageSquad(stateSquad);
        state.DisengageSquad(stateSquad);

        Assert.Empty(state.ActiveAttackerSquads);
        Assert.Single(state.AllAttackerSquads);
        Assert.Single(stateSquad.Soldiers);
        Assert.DoesNotContain(soldierId, state.Soldiers.Keys);
        Assert.Equal(BattleSquadStatus.Disengaged, stateSquad.Status);
    }

    [Fact]
    public void Profile_InitializesSideFactsFromStartingRoster()
    {
        BattleSquad squad = new(false, TestModelFactory.CreateSquad(
            "Ambushers",
            TestModelFactory.CreateSoldier(name: "First"),
            TestModelFactory.CreateSoldier(name: "Second")));
        PlaceSquad(squad, 0);
        BattleState state = new(
            new Dictionary<int, BattleSquad> { [squad.Id] = squad },
            new Dictionary<int, BattleSquad>(),
            new BattleSideProfile(Aggression.Cautious, BattleRole.Ambusher),
            new BattleSideProfile(Aggression.Aggressive, BattleRole.Ambushed));

        Assert.Equal(Aggression.Cautious, state.AttackerSide.Aggression);
        Assert.Equal(BattleRole.Ambusher, state.AttackerSide.BattleRole);
        Assert.Equal(2, state.AttackerSide.StartingSoldierCount);
        Assert.Equal(squad.Soldiers.Sum(s => s.Soldier.Template.BattleValue),
            state.AttackerSide.StartingBattleValue);
        Assert.Equal(Aggression.Aggressive, state.OpposingSide.Aggression);
        Assert.Equal(BattleRole.Ambushed, state.OpposingSide.BattleRole);
    }

    private static BattleState CreateState(BattleSquad missionSide, BattleSquad opposingSide)
    {
        return new BattleState(
            new Dictionary<int, BattleSquad> { [missionSide.Id] = missionSide },
            new Dictionary<int, BattleSquad> { [opposingSide.Id] = opposingSide });
    }

    private static void PlaceSquad(BattleSquad squad, int x)
    {
        foreach (BattleSoldier soldier in squad.Soldiers)
        {
            soldier.TopLeft = (x, 0);
        }
    }
}
