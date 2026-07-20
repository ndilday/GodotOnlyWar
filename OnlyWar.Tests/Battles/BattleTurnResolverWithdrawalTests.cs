using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Aftermath;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;

using Xunit;

namespace OnlyWar.Tests.Battles;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class BattleTurnResolverWithdrawalTests
{
    [Fact]
    public void ProcessNextTurn_RecordsTypedAnnihilationForInitiallyEliminatedSide()
    {
        BattleSquad attackers = CreateSquad("Eliminated", 73_001, TestModelFactory.MarineTemplate);
        BattleSquad defenders = CreateSquad("Holder", 73_002, TestModelFactory.MarineTemplate);
        attackers.Soldiers[0].TopLeft = (0, 0);
        attackers.Status = BattleSquadStatus.Eliminated;
        BattleGridManager grid = new();
        Place(grid, defenders.Soldiers[0], false, 20, 0);
        BattleTurnResolver resolver = CreateResolver(
            grid,
            [attackers],
            [defenders],
            Aggression.Aggressive,
            Aggression.Aggressive);

        resolver.ProcessNextTurn();

        Assert.NotNull(resolver.BattleHistory.Outcome);
        Assert.Equal(BattleEndReason.Annihilation, resolver.BattleHistory.Outcome.EndReason);
        Assert.Equal(BattleSide.Opposing, resolver.BattleHistory.Outcome.SideHoldingField);
        Assert.Contains(attackers.Id, resolver.BattleHistory.Outcome.EliminatedSquadIds);
        Assert.Empty(resolver.BattleHistory.Outcome.DisengagedSquadIds);
    }

    [Fact]
    public void ProcessNextTurn_VoluntaryWithdrawalWithBreakOffEmitsTypedEventsAndOutcome()
    {
        SoldierTemplate zeroValueHuman = new(
            73_010,
            TestModelFactory.HumanSpecies,
            "Zero Value Human",
            1,
            1,
            false,
            0,
            Array.Empty<ValueTuple<BaseSkill, float>>(),
            battleValue: 0);
        BattleSquad withdrawing = CreateSquad("Withdrawing", 73_011, zeroValueHuman);
        BattleSquad holder = CreateSquad("Holder", 73_012, TestModelFactory.MarineTemplate);
        BattleGridManager grid = new();
        Place(grid, withdrawing.Soldiers[0], true, 0, 0);
        Place(grid, holder.Soldiers[0], false, 1_000, 0);
        BattleTurnResolver resolver = CreateResolver(
            grid,
            [withdrawing],
            [holder],
            Aggression.Normal,
            Aggression.Avoid);

        resolver.ProcessNextTurn();

        BattleOutcome outcome = Assert.IsType<BattleOutcome>(resolver.BattleHistory.Outcome);
        Assert.Equal(BattleEndReason.Withdrawal, outcome.EndReason);
        Assert.Equal(BattleSide.Opposing, outcome.SideHoldingField);
        Assert.Contains(withdrawing.Id, outcome.DisengagedSquadIds);
        IReadOnlyList<BattleEvent> events = resolver.BattleHistory.Turns.Last().Events;
        Assert.Contains(events, battleEvent =>
            battleEvent.Type == BattleEventType.WithdrawalOrdered
            && battleEvent.Side == BattleSide.Attacker);
        Assert.Contains(events, battleEvent =>
            battleEvent.Type == BattleEventType.PursuitEnded
            && battleEvent.Side == BattleSide.Opposing);
        Assert.Contains(events, battleEvent =>
            battleEvent.Type == BattleEventType.SquadDisengaged
            && battleEvent.PrimarySquadId == withdrawing.Id);
        Assert.Contains(events, battleEvent =>
            battleEvent.Type == BattleEventType.ForceDisengaged
            && battleEvent.Side == BattleSide.Attacker);
    }

    [Fact]
    public void ProcessNextTurn_BurrowCapableWithdrawingSquadDisengagesImmediately()
    {
        BattleSquad burrowers = CreateSquad(
            "Burrowers",
            73_021,
            TestModelFactory.BurrowerTemplate);
        BattleSquad pursuers = CreateSquad("Pursuers", 73_022, TestModelFactory.MarineTemplate);
        BattleGridManager grid = new();
        Place(grid, burrowers.Soldiers[0], true, 0, 0);
        Place(grid, pursuers.Soldiers[0], false, 1_000, 0);
        BattleTurnResolver resolver = CreateResolver(
            grid,
            [burrowers],
            [pursuers],
            Aggression.Normal,
            Aggression.Normal);

        resolver.ProcessNextTurn();

        BattleOutcome outcome = Assert.IsType<BattleOutcome>(resolver.BattleHistory.Outcome);
        Assert.Equal(BattleEndReason.Withdrawal, outcome.EndReason);
        Assert.Contains(burrowers.Id, outcome.DisengagedSquadIds);
        BattleEvent disengagement = Assert.Single(
            resolver.BattleHistory.Turns.Last().Events,
            battleEvent => battleEvent.Type == BattleEventType.SquadDisengaged);
        Assert.Equal(burrowers.Id, disengagement.PrimarySquadId);
        Assert.Contains("burrowing capability", disengagement.Description);
    }

    private static BattleTurnResolver CreateResolver(
        BattleGridManager grid,
        IList<BattleSquad> attackers,
        IList<BattleSquad> defenders,
        Aggression attackerAggression,
        Aggression defenderAggression)
    {
        GameRulesData rules = new();
        Date date = new(1, 1, 1);
        string originalDirectory = Environment.CurrentDirectory;
        try
        {
            Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
            GameDataSingleton.Instance.LoadGameDataFromBlob(rules, date, null);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
        }

        RNG.Reset(73_000);
        BattleAftermathDependencies aftermath = new(
            date,
            StaticRNG.Instance,
            NoOpPlayerBattleAftermathSink.Instance);
        BattleExecutionContext execution = new(rules, StaticRNG.Instance, aftermath);
        return new BattleTurnResolver(
            grid,
            attackers,
            defenders,
            region: null,
            execution,
            new BattleSideProfile(attackerAggression, BattleRole.Attacker),
            new BattleSideProfile(defenderAggression, BattleRole.Defender));
    }

    private static BattleSquad CreateSquad(
        string name,
        int soldierId,
        SoldierTemplate soldierTemplate)
    {
        Faction faction = CreateFaction(soldierId + 10_000, name, soldierTemplate);
        SquadTemplate squadTemplate = new(
            soldierId,
            $"{name} Template",
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            [new SquadTemplateElement(soldierTemplate, 0, 1)],
            SquadTypes.None)
        {
            Faction = faction
        };
        Soldier soldier = TestModelFactory.CreateSoldier(soldierTemplate, $"{name} Soldier");
        soldier.Id = soldierId;
        Squad squad = new(name, null, squadTemplate);
        squad.AddSquadMember(soldier);
        return new BattleSquad(false, squad);
    }

    private static void Place(
        BattleGridManager grid,
        BattleSoldier soldier,
        bool side,
        int x,
        int y)
    {
        soldier.TopLeft = new ValueTuple<int, int>(x, y);
        grid.PlaceSoldier(soldier, side, [soldier.TopLeft.Value]);
    }

    private static Faction CreateFaction(int id, string name, SoldierTemplate template)
    {
        return new Faction(
            id,
            name,
            Color.Red,
            isPlayerFaction: false,
            isDefaultFaction: false,
            canInfiltrate: false,
            GrowthType.None,
            new Dictionary<int, Species> { [template.Species.Id] = template.Species },
            new Dictionary<int, SoldierTemplate> { [template.Id] = template },
            new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, Models.Units.UnitTemplate>(),
            new Dictionary<int, Models.Fleets.BoatTemplate>(),
            new Dictionary<int, Models.Fleets.ShipTemplate>(),
            new Dictionary<int, Models.Fleets.FleetTemplate>());
    }

    private sealed class NoOpPlayerBattleAftermathSink : IPlayerBattleAftermathSink
    {
        public static NoOpPlayerBattleAftermathSink Instance { get; } = new();

        public void MoveToFallenBrothers(PlayerSoldier soldier) { }
        public void AddRecoveredGeneseed(float purity) { }
        public void AddToBattleHistory(Date date, string title, IReadOnlyList<string> subEvents) { }
    }
}
