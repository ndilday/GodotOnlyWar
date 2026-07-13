using System.Collections.Generic;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models.Battles;
using Xunit;

namespace OnlyWar.Tests.Battles;

public class BattleSummaryBuilderTests
{
    [Fact]
    public void Build_ComputesCasualtiesForBothSides()
    {
        List<string> lines = BattleSummaryBuilder.Build(
            "Blood Ravens", "Cult Mob", 10, 6, 8, 2, 5, false);

        Assert.Equal("The battle ended after 5 turns.", lines[0]);
        Assert.Equal("Blood Ravens suffered 4 casualties out of 10 combatants.", lines[1]);
        Assert.Equal("Cult Mob suffered 6 casualties out of 8 combatants.", lines[2]);
    }

    [Fact]
    public void Build_ClampsCasualtiesWhenRemainingExceedsStarting()
    {
        // Reinforcements or other mid-battle additions could otherwise drive this negative.
        List<string> lines = BattleSummaryBuilder.Build(
            "Blood Ravens", "Cult Mob", 5, 8, 5, 5, 3, false);

        Assert.Equal("Blood Ravens suffered 0 casualties out of 5 combatants.", lines[1]);
    }

    [Fact]
    public void Build_FallsBackToGenericNamesWhenFactionNamesAreNull()
    {
        List<string> lines = BattleSummaryBuilder.Build(
            null, null, 10, 10, 8, 0, 4, false);

        Assert.Equal("The attacking force suffered 0 casualties out of 10 combatants.", lines[1]);
        Assert.Equal("The defending force suffered 8 casualties out of 8 combatants.", lines[2]);
        Assert.Equal("The attacking force held the field.", lines[3]);
    }

    [Fact]
    public void Build_FirstSideHoldsFieldWhenSecondSideAnnihilated()
    {
        List<string> lines = BattleSummaryBuilder.Build(
            "Blood Ravens", "Cult Mob", 10, 7, 8, 0, 6, false);

        Assert.Equal("Blood Ravens held the field.", lines[3]);
    }

    [Fact]
    public void Build_SecondSideHoldsFieldWhenFirstSideAnnihilated()
    {
        List<string> lines = BattleSummaryBuilder.Build(
            "Blood Ravens", "Cult Mob", 10, 0, 8, 3, 6, false);

        Assert.Equal("Cult Mob held the field.", lines[3]);
    }

    [Fact]
    public void Build_ReportsNeitherSideWhenBothAnnihilated()
    {
        List<string> lines = BattleSummaryBuilder.Build(
            "Blood Ravens", "Cult Mob", 10, 0, 8, 0, 9, false);

        Assert.Equal("Neither side survived to hold the field.", lines[3]);
    }

    [Fact]
    public void Build_ReportsContestedFieldWhenTurnCapForcesDisengagement()
    {
        List<string> lines = BattleSummaryBuilder.Build(
            "Blood Ravens", "Cult Mob", 10, 6, 8, 5, 1000, true);

        Assert.Equal(
            "Both forces still held positions when the fighting broke off; the field was left contested.",
            lines[3]);
    }

    [Fact]
    public void GetBattleLog_AppendsClosingSummaryAfterTurnLog()
    {
        BattleHistory history = new();
        history.Turns.Add(new BattleTurn(
            new BattleState(new Dictionary<int, BattleSquad>(), new Dictionary<int, BattleSquad>()),
            new List<OnlyWar.Helpers.Battles.Actions.IAction>()));
        history.ClosingSummary.AddRange(BattleSummaryBuilder.Build(
            "Blood Ravens", "Cult Mob", 10, 6, 8, 0, 3, false));

        string log = history.GetBattleLog();

        Assert.EndsWith("Blood Ravens held the field.\n", log);
        Assert.Contains("Turn 0", log);
    }
}
