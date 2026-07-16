using System.Collections.Generic;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Battles;
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
        originalSoldier.LeftoverMovement = 2.4f;

        BattleState copy = new(original);
        BattleSquad copiedSquad = copy.GetSquad(squad.Id);
        BattleSoldier copiedSoldier = copiedSquad.Soldiers[0];

        originalSquad.MovementTier = SquadMovementTier.Run;
        originalSoldier.LeftoverMovement = 0;

        Assert.Equal(SquadMovementTier.Walk, copiedSquad.MovementTier);
        Assert.Equal(2.4f, copiedSoldier.LeftoverMovement);
        Assert.Same(copiedSquad, copiedSoldier.BattleSquad);
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

        state.RemoveSquad(state.AttackerSquads[npcAttacker.Id]);

        Assert.Empty(state.AttackerSquads);
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
            soldier.TopLeft = new System.Tuple<int, int>(x, 0);
        }
    }
}
