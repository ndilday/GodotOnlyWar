using System.Collections.Generic;
using System.Drawing;
using System;
using System.IO;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Aftermath;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class BattleTurnResolverTests
{
    [Fact]
    public void ProcessNextTurn_UsesImmutableCompactSnapshotsAcrossMutableSimulationTurns()
    {
        GameRulesData rules = new();
        Date battleDate = new(1, 1, 1);
        string originalDirectory = Environment.CurrentDirectory;
        try
        {
            Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
            GameDataSingleton.Instance.LoadGameDataFromBlob(rules, battleDate, null);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
        }
        RNG.Reset(42);
        BattleSquad attackers = CreateBattleSquad(CreateFaction(80_001, "Attackers"), "Attackers", 80_101);
        BattleSquad defenders = CreateBattleSquad(CreateFaction(80_002, "Defenders"), "Defenders", 80_201);
        BattleGridManager grid = new();
        Place(grid, attackers.Soldiers[0], side: true, x: 0, y: 0);
        Place(grid, defenders.Soldiers[0], side: false, x: 4, y: 0);
        BattleAftermathDependencies aftermath = new(
            battleDate, StaticRNG.Instance, NoOpPlayerBattleAftermathSink.Instance);
        BattleExecutionContext execution = new(rules, StaticRNG.Instance, aftermath);
        BattleTurnResolver resolver = new(
            grid, [attackers], [defenders], region: null, execution);
        bool completed = false;
        resolver.OnBattleComplete += (_, _) => completed = true;

        for (int turn = 0; turn < 1000 && !completed; turn++)
        {
            resolver.ProcessNextTurn();
        }

        Assert.True(completed);
        Assert.True(resolver.BattleHistory.Turns.Count >= 2);
        Assert.Equal(
            Enumerable.Range(0, resolver.BattleHistory.Turns.Count),
            resolver.BattleHistory.Turns.Select(turn => turn.TurnNumber));
        Assert.Single(resolver.BattleHistory.Turns[0].State.AttackerSquads[attackers.Id].Soldiers);
        Assert.Equal(0, resolver.BattleHistory.Turns[0].TurnNumber);
    }

    private static BattleSquad CreateBattleSquad(Faction faction, string name, int soldierId)
    {
        SquadTemplate template = new(
            soldierId,
            $"{name} Template",
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            [new SquadTemplateElement(TestModelFactory.MarineTemplate, 0, 1)],
            SquadTypes.None)
        {
            Faction = faction
        };
        Soldier soldier = TestModelFactory.CreateSoldier(name: $"{name} Soldier");
        soldier.Id = soldierId;
        Squad squad = new(name, null, template);
        squad.AddSquadMember(soldier);
        return new BattleSquad(false, squad);
    }

    private static void Place(BattleGridManager grid, BattleSoldier soldier, bool side, int x, int y)
    {
        soldier.TopLeft = (x, y);
        grid.PlaceSoldier(soldier, side, [new System.ValueTuple<int, int>(x, y)]);
    }

    private static Faction CreateFaction(int id, string name)
    {
        return new Faction(
            id,
            name,
            Color.Red,
            isPlayerFaction: false,
            isDefaultFaction: false,
            canInfiltrate: false,
            GrowthType.None,
            new Dictionary<int, Species> { [TestModelFactory.HumanSpecies.Id] = TestModelFactory.HumanSpecies },
            new Dictionary<int, SoldierTemplate> { [TestModelFactory.MarineTemplate.Id] = TestModelFactory.MarineTemplate },
            new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, Models.Units.UnitTemplate>(),
            new Dictionary<int, Models.Fleets.BoatTemplate>(),
            new Dictionary<int, Models.Fleets.ShipTemplate>(),
            new Dictionary<int, Models.Fleets.FleetTemplate>());
    }

    private sealed class NoOpPlayerBattleAftermathSink : IPlayerBattleAftermathSink
    {
        public static NoOpPlayerBattleAftermathSink Instance { get; } = new();

        public void MoveToFallenBrothers(PlayerSoldier soldier)
        {
        }

        public void AddRecoveredGeneseed(float purity)
        {
        }

        public void AddToBattleHistory(Date date, string title, IReadOnlyList<string> subEvents)
        {
        }
    }
}
