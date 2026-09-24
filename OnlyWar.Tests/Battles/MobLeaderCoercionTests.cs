using System.Collections.Generic;
using OnlyWar.Battles;
using OnlyWar.Battles.Models;
using OnlyWar.Domain;
using Xunit;
using static OnlyWar.Tests.Battles.MoraleServiceHarness;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// Mob leader coercion: when a MobMentality squad would rout and has a leader, the leader spends
/// the next round coercing it and the rout is ignored. A leader who has just coerced cannot do it
/// again at the very next check, so a mob that keeps breaking routs one turn later instead of
/// being held in place by its leader for the rest of the battle. Each check here is triggered by
/// a casualty, because a squad with nothing to react to does not check.
/// </summary>
public class MobLeaderCoercionTests
{
    private static (MoraleServiceHarness Harness, BattleSquad Mob) Create()
    {
        BattleSquad mob = CreateSquad(
            "Nobz", 92_010, CreateFaction(92_000, FactionBehavior.MobMentality), troops: 4, withLeader: true);
        BattleSquad enemy = CreateSquad(
            "Marines", 92_020, CreateFaction(92_001, FactionBehavior.None), troops: 4, withLeader: false);
        BattleGridManager grid = new();
        Place(grid, mob, side: false, x: 0, y: 0);
        Place(grid, enemy, side: true, x: 0, y: 200);
        return (new MoraleServiceHarness(grid, [enemy], [mob]), mob);
    }

    private static List<BattleEvent> LoseOneAndCheck(MoraleServiceHarness harness, BattleSquad mob, double z)
    {
        harness.StartTurn();
        harness.Kill(mob, 1);
        harness.Random.DefaultZ = z;
        return harness.Check(BattleSide.Opposing);
    }

    [Fact]
    public void MobThatBreaksTwiceInARow_IsCoercedOnce_ThenRouts()
    {
        (MoraleServiceHarness harness, BattleSquad mob) = Create();
        Assert.NotNull(harness.Live(mob).SquadLeader);

        List<BattleEvent> first = LoseOneAndCheck(harness, mob, Fail);
        Assert.True(Has(first, BattleEventType.MobLeaderSuppressionCommitted));
        Assert.False(Has(first, BattleEventType.SquadRouted));
        Assert.NotEqual(WithdrawalRole.Routing, harness.Live(mob).WithdrawalRole);

        List<BattleEvent> second = LoseOneAndCheck(harness, mob, Fail);
        Assert.False(Has(second, BattleEventType.MobLeaderSuppressionCommitted));
        Assert.True(Has(second, BattleEventType.SquadRouted));
        Assert.Equal(WithdrawalRole.Routing, harness.Live(mob).WithdrawalRole);
    }

    [Fact]
    public void MobThatHoldsBetweenBreaks_CanBeCoercedAgain()
    {
        (MoraleServiceHarness harness, BattleSquad mob) = Create();

        Assert.True(Has(LoseOneAndCheck(harness, mob, Fail), BattleEventType.MobLeaderSuppressionCommitted));
        Assert.False(Has(LoseOneAndCheck(harness, mob, Hold), BattleEventType.SquadRouted));

        // A turn of holding has passed since the last coercion, so the leader may coerce again.
        List<BattleEvent> third = LoseOneAndCheck(harness, mob, Fail);
        Assert.True(Has(third, BattleEventType.MobLeaderSuppressionCommitted));
        Assert.False(Has(third, BattleEventType.SquadRouted));
    }
}
