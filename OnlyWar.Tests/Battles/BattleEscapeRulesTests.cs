using OnlyWar.Helpers.Battles;
using Xunit;

namespace OnlyWar.Tests.Battles;

public class BattleEscapeRulesTests
{
    [Fact]
    public void ActivelyPursuedSquad_RemainsRegardlessOfDistance()
    {
        BattleEscapeRules.Result result = BattleEscapeRules.Evaluate(Input(
            pursued: true,
            separation: 100,
            attackRange: 10,
            pursuerSpeed: 5,
            withdrawalSpeed: 8));

        Assert.False(result.Escapes);
        Assert.Equal("actively_pursued", result.Reason);
    }

    [Fact]
    public void UnpursuedFasterWithdrawerOutsideRange_Escapes()
    {
        BattleEscapeRules.Result result = BattleEscapeRules.Evaluate(Input(
            pursued: false,
            separation: 20,
            attackRange: 10,
            pursuerSpeed: 6,
            withdrawalSpeed: 8));

        Assert.True(result.Escapes);
        Assert.True(float.IsPositiveInfinity(result.EarliestInterceptTurns));
        Assert.Equal("no_possible_intercept", result.Reason);
    }

    [Fact]
    public void RelativeSpeedCanKeepUnpursuedSquadInsideRetargetHorizon()
    {
        // Ten yards outside useful range and a five-yard relative speed advantage means the
        // enemy can recover the attack envelope in exactly two turns, so the squad remains.
        BattleEscapeRules.Result result = BattleEscapeRules.Evaluate(Input(
            pursued: false,
            separation: 20,
            attackRange: 10,
            pursuerSpeed: 11,
            withdrawalSpeed: 6));

        Assert.False(result.Escapes);
        Assert.Equal(2, result.EarliestInterceptTurns);
        Assert.Equal("enemy_can_retarget", result.Reason);
    }

    [Fact]
    public void RelativeSpeedBeyondRetargetHorizon_AllowsEscape()
    {
        // The same geometric gap takes five turns when the relative advantage is only two.
        BattleEscapeRules.Result result = BattleEscapeRules.Evaluate(Input(
            pursued: false,
            separation: 20,
            attackRange: 10,
            pursuerSpeed: 8,
            withdrawalSpeed: 6));

        Assert.True(result.Escapes);
        Assert.Equal(5, result.EarliestInterceptTurns);
        Assert.Equal("beyond_retarget_horizon", result.Reason);
    }

    [Fact]
    public void UnpursuedSquadInsideUsefulRange_Remains()
    {
        BattleEscapeRules.Result result = BattleEscapeRules.Evaluate(Input(
            pursued: false,
            separation: 9,
            attackRange: 10,
            pursuerSpeed: 5,
            withdrawalSpeed: 8));

        Assert.False(result.Escapes);
        Assert.Equal(0, result.EarliestInterceptTurns);
        Assert.Equal("inside_attack_range", result.Reason);
    }

    private static BattleEscapeRules.Input Input(
        bool pursued,
        float separation,
        float attackRange,
        float pursuerSpeed,
        float withdrawalSpeed) =>
        new(
            Turn: 7,
            IsFirstSide: true,
            WithdrawingSquadId: 42,
            IsPursued: pursued,
            Threats:
            [
                new BattleEscapeRules.Threat(
                    PursuerSquadId: 84,
                    separation,
                    attackRange,
                    pursuerSpeed,
                    withdrawalSpeed)
            ]);
}
