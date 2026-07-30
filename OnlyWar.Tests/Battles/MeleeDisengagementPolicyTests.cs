using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Battles;

using Xunit;

namespace OnlyWar.Tests.Battles;

public class MeleeDisengagementPolicyTests
{
    // An even duel: I hit him as often as he hits me, and turning my back would cost 25 points
    // of hit probability. Score = 0 + 0.25 = 0.25.
    private static MeleeDisengagementPolicy.Input EvenMatch(
        MoraleState morale = MoraleState.Steady) =>
        new(0.5f, 0.5f, 0.75f, 1, morale);

    [Fact]
    public void SteadySoldierWinningTheExchange_StandsAndFights()
    {
        // The marine-versus-gaunt case: he is winning at melee and fleeing would only expose him.
        var winning = EvenMatch() with { ChanceIHitHim = 0.75f, ChanceHeHitsMeStanding = 0.3f };

        MeleeDisengagementPolicy.Result result = MeleeDisengagementPolicy.Evaluate(winning);

        Assert.Equal(MeleeDisengagementChoice.StandAndFight, result.Choice);
        Assert.Equal("wins_the_exchange", result.Reason);
    }

    [Fact]
    public void SoldierBadlyOutclassed_KeepsRunning()
    {
        // The genestealer case: standing is a losing duel by more than fleeing costs, so he takes
        // the free swing and goes.
        var outclassed = EvenMatch() with
        {
            ChanceIHitHim = 0.35f,
            ChanceHeHitsMeStanding = 0.7f,
            ChanceHeHitsMeRunning = 0.95f
        };

        MeleeDisengagementPolicy.Result result = MeleeDisengagementPolicy.Evaluate(outclassed);

        Assert.Equal(MeleeDisengagementChoice.KeepRunning, result.Choice);
        Assert.Equal("loses_the_exchange", result.Reason);
    }

    [Fact]
    public void LosingTheDuelButPunishedWorseForFleeing_StandsAnyway()
    {
        // Losing the exchange by 0.1 while running would cost 0.4: staying is still the lesser
        // evil, and the reason records that it was fear of the free swings, not confidence.
        var cornered = EvenMatch() with
        {
            ChanceIHitHim = 0.4f,
            ChanceHeHitsMeStanding = 0.5f,
            ChanceHeHitsMeRunning = 0.9f
        };

        MeleeDisengagementPolicy.Result result = MeleeDisengagementPolicy.Evaluate(cornered);

        Assert.Equal(MeleeDisengagementChoice.StandAndFight, result.Choice);
        Assert.Equal("fleeing_costs_more", result.Reason);
    }

    [Fact]
    public void ShakenSquad_DemandsAClearlyWinningMatchupBeforeStanding()
    {
        // Identical matchup, different nerve. Steady troops stand on an even trade; shaken ones
        // need to be clearly ahead.
        Assert.Equal(
            MeleeDisengagementChoice.StandAndFight,
            MeleeDisengagementPolicy.Evaluate(EvenMatch(MoraleState.Steady)).Choice);
        Assert.Equal(
            MeleeDisengagementChoice.KeepRunning,
            MeleeDisengagementPolicy.Evaluate(EvenMatch(MoraleState.Shaken)).Choice);

        var winning = EvenMatch(MoraleState.Shaken) with
        {
            ChanceIHitHim = 0.75f,
            ChanceHeHitsMeStanding = 0.3f
        };
        Assert.Equal(
            MeleeDisengagementChoice.StandAndFight,
            MeleeDisengagementPolicy.Evaluate(winning).Choice);
    }

    [Fact]
    public void BeingSwarmed_PushesTowardBreakingContact()
    {
        // An even duel is worth standing for against one man. Against three it is not: he gets
        // pulled down while the squad leaves without him.
        Assert.Equal(
            MeleeDisengagementChoice.StandAndFight,
            MeleeDisengagementPolicy.Evaluate(EvenMatch()).Choice);

        var swarmed = EvenMatch() with { AdjacentEnemies = 3 };
        MeleeDisengagementPolicy.Result result = MeleeDisengagementPolicy.Evaluate(swarmed);
        Assert.Equal(MeleeDisengagementChoice.KeepRunning, result.Choice);
        Assert.Equal("outnumbered_in_contact", result.Reason);
    }

    [Fact]
    public void DecisiveFighter_HoldsEvenWhenSurrounded()
    {
        // The counterweight to the case above: a marine who beats each of them three times out of
        // four does not flee four gaunts, because turning his back is what would kill him.
        var dominant = EvenMatch() with
        {
            ChanceIHitHim = 0.75f,
            ChanceHeHitsMeStanding = 0.3f,
            AdjacentEnemies = 4
        };

        Assert.Equal(
            MeleeDisengagementChoice.StandAndFight,
            MeleeDisengagementPolicy.Evaluate(dominant).Choice);
    }

    [Fact]
    public void TraceRenderer_UsesStableFields()
    {
        string trace = MeleeDisengagementPolicy.Evaluate(EvenMatch()).Trace.Render();

        Assert.Equal("MELEE_DISENGAGE chance_i_hit=0.5 chance_he_hits_standing=0.5 " +
                     "chance_he_hits_running=0.75 adjacent_enemies=1 morale=Steady " +
                     "exchange_advantage=0 cost_of_fleeing=0.25 outnumbered_penalty=0 " +
                     "score=0.25 threshold=0 decision=StandAndFight reason=wins_the_exchange", trace);
    }
}
