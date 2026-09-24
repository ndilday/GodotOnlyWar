using System.Collections.Generic;
using OnlyWar.Battles;
using OnlyWar.Battles.Models;
using OnlyWar.Domain;
using OnlyWar.Domain.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;
using static OnlyWar.Tests.Battles.MoraleServiceHarness;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// Event-triggered morale checks. A squad checks at the end of a turn only when something
/// happened to it: casualties, its leader lost, its side's last command squad destroyed, its
/// synapse coverage lost, or a friendly squad (at any range) routed the turn before. On a quiet
/// turn it rolls nothing, except that a Shaken squad rolls to rally, and a rally can only make it
/// Steady. Every test uses a z that fails every soldier, so any check that runs is visible.
/// </summary>
public class MoraleCheckTriggerTests
{
    private static readonly Faction Imperial = CreateFaction(93_000, FactionBehavior.None);
    private static readonly Faction Mob = CreateFaction(93_001, FactionBehavior.MobMentality);
    private static readonly Faction Enemy = CreateFaction(93_002, FactionBehavior.None);

    private static MoraleServiceHarness Create(BattleGridManager grid, params BattleSquad[] opposing)
    {
        BattleSquad enemy = CreateSquad("Enemy", 93_900, Enemy, troops: 4, withLeader: false);
        Place(grid, enemy, side: true, x: 0, y: 2000);
        return new MoraleServiceHarness(grid, [enemy], opposing);
    }

    [Fact]
    public void SquadWithNothingToReactTo_DoesNotCheck()
    {
        BattleGridManager grid = new();
        BattleSquad troops = CreateSquad("Troops", 93_010, Imperial, troops: 5, withLeader: false);
        Place(grid, troops, side: false, x: 0, y: 0);
        MoraleServiceHarness harness = Create(grid, troops);
        harness.Random.DefaultZ = Fail;

        harness.StartTurn();
        List<BattleEvent> events = harness.Check(BattleSide.Opposing);

        Assert.False(Has(events, BattleEventType.SquadRouted));
        Assert.Equal(MoraleState.Steady, harness.Live(troops).MoraleState);
    }

    [Fact]
    public void Casualties_TriggerACheck()
    {
        BattleGridManager grid = new();
        BattleSquad troops = CreateSquad("Troops", 93_020, Imperial, troops: 5, withLeader: false);
        Place(grid, troops, side: false, x: 0, y: 0);
        MoraleServiceHarness harness = Create(grid, troops);
        harness.Random.DefaultZ = Fail;

        harness.StartTurn();
        harness.Kill(troops, 1);

        Assert.True(Has(harness.Check(BattleSide.Opposing), BattleEventType.SquadRouted, troops));
    }

    [Fact]
    public void LastCommandSquadDestroyed_TriggersACheckOnce_ForTheWholeSide()
    {
        BattleGridManager grid = new();
        BattleSquad hq = CreateSquad(
            "Captain", 93_030, Imperial, troops: 1, withLeader: false, squadType: SquadTypes.HQ);
        BattleSquad steady = CreateSquad("Steady Troops", 93_031, Imperial, troops: 5, withLeader: false);
        BattleSquad breaking = CreateSquad("Breaking Troops", 93_032, Imperial, troops: 5, withLeader: false);
        Place(grid, hq, side: false, x: 0, y: 0);
        Place(grid, steady, side: false, x: 0, y: 500);
        Place(grid, breaking, side: false, x: 0, y: 1000);
        MoraleServiceHarness harness = Create(grid, hq, steady, breaking);

        // The Captain dies. Every squad on the side checks, even one far from him: one holds,
        // one breaks.
        harness.StartTurn();
        harness.Kill(hq, 1);
        harness.Random.Enqueue(Hold, Hold, Hold, Hold, Hold);
        harness.Random.DefaultZ = Fail;
        List<BattleEvent> events = harness.Check(BattleSide.Opposing);
        Assert.False(Has(events, BattleEventType.SquadRouted, steady));
        Assert.True(Has(events, BattleEventType.SquadRouted, breaking));

        // Two turns later neither the Captain's death nor the rout is new, so the survivor rolls
        // nothing and holds despite the failing draws. (The skipped turn in between is the one on
        // which the rout would have triggered it.)
        harness.StartTurn();
        harness.StartTurn();
        List<BattleEvent> later = harness.Check(BattleSide.Opposing);
        Assert.False(Has(later, BattleEventType.SquadRouted, steady));
        Assert.Equal(MoraleState.Steady, harness.Live(steady).MoraleState);
    }

    [Fact]
    public void FriendlySquadRouting_TriggersACheckNextTurn_AtAnyRange()
    {
        BattleGridManager grid = new();
        BattleSquad first = CreateSquad("First", 93_040, Imperial, troops: 5, withLeader: false);
        BattleSquad distant = CreateSquad("Distant", 93_041, Imperial, troops: 5, withLeader: false);
        Place(grid, first, side: false, x: 0, y: 0);
        Place(grid, distant, side: false, x: 0, y: 1500);
        MoraleServiceHarness harness = Create(grid, first, distant);
        harness.Random.DefaultZ = Fail;

        harness.StartTurn();
        harness.Kill(first, 1);
        List<BattleEvent> routTurn = harness.Check(BattleSide.Opposing);
        Assert.True(Has(routTurn, BattleEventType.SquadRouted, first));
        // Same turn: the distant squad has not heard yet.
        Assert.False(Has(routTurn, BattleEventType.SquadRouted, distant));

        harness.StartTurn();
        Assert.True(Has(harness.Check(BattleSide.Opposing), BattleEventType.SquadRouted, distant));
    }

    [Fact]
    public void LosingSynapseCoverage_TriggersACheck()
    {
        BattleGridManager grid = new();
        BattleSquad provider = CreateSquad(
            "Synapse", 93_050, Imperial, troops: 1, withLeader: false,
            troopTemplate: TestModelFactory.SynapseProviderTemplate);
        BattleSquad gaunts = CreateSquad("Gaunts", 93_051, Imperial, troops: 5, withLeader: false);
        Place(grid, provider, side: false, x: 0, y: 0);
        Place(grid, gaunts, side: false, x: 0, y: 5);
        MoraleServiceHarness harness = Create(grid, provider, gaunts);
        harness.Random.DefaultZ = Fail;

        harness.StartTurn();
        Assert.False(Has(harness.Check(BattleSide.Opposing), BattleEventType.SquadRouted, gaunts));

        harness.StartTurn();
        harness.Kill(provider, 1);
        Assert.True(Has(harness.Check(BattleSide.Opposing), BattleEventType.SquadRouted, gaunts));
    }

    /// <summary>
    /// Leader plus four: after one casualty the leader holds and two of the three troops fail,
    /// a fail fraction of 0.5 against the leader-raised thresholds of 0.4 and 0.65.
    /// </summary>
    private static (MoraleServiceHarness Harness, BattleSquad Squad) CreateShaken(Faction faction)
    {
        BattleGridManager grid = new();
        BattleSquad squad = CreateSquad("Shaken", 93_060, faction, troops: 4, withLeader: true);
        Place(grid, squad, side: false, x: 0, y: 0);
        MoraleServiceHarness harness = Create(grid, squad);
        harness.StartTurn();
        harness.Kill(squad, 1);
        harness.Random.Enqueue(Hold, Fail, Fail, Hold);
        harness.Check(BattleSide.Opposing);
        Assert.Equal(MoraleState.Shaken, harness.Live(squad).MoraleState);
        return (harness, squad);
    }

    [Fact]
    public void ShakenSquad_RollsToRallyOnQuietTurns_AndARallyNeverMakesItWorse()
    {
        (MoraleServiceHarness harness, BattleSquad squad) = CreateShaken(Imperial);

        // A failed rally leaves the squad Shaken. It does not rout.
        harness.StartTurn();
        harness.Random.DefaultZ = Fail;
        Assert.False(Has(harness.Check(BattleSide.Opposing), BattleEventType.SquadRouted));
        Assert.Equal(MoraleState.Shaken, harness.Live(squad).MoraleState);
        Assert.NotEqual(WithdrawalRole.Routing, harness.Live(squad).WithdrawalRole);

        // A passed rally restores it.
        harness.StartTurn();
        harness.Random.DefaultZ = Hold;
        harness.Check(BattleSide.Opposing);
        Assert.Equal(MoraleState.Steady, harness.Live(squad).MoraleState);

        // A Steady squad with nothing to react to rolls nothing at all.
        harness.StartTurn();
        harness.Random.DefaultZ = Fail;
        harness.Check(BattleSide.Opposing);
        Assert.Equal(MoraleState.Steady, harness.Live(squad).MoraleState);
    }

    [Fact]
    public void MobRallyRoll_NeverCallsOnLeaderCoercion()
    {
        (MoraleServiceHarness harness, BattleSquad squad) = CreateShaken(Mob);

        harness.StartTurn();
        harness.Random.DefaultZ = Fail;
        List<BattleEvent> events = harness.Check(BattleSide.Opposing);

        Assert.False(Has(events, BattleEventType.MobLeaderSuppressionCommitted));
        Assert.False(Has(events, BattleEventType.SquadRouted));
        Assert.Equal(MoraleState.Shaken, harness.Live(squad).MoraleState);
    }
}
