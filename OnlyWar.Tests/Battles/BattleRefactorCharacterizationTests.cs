using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Helpers.Battles.Aftermath;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.FactionBehaviors;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;

using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// Packet-0 characterization coverage. These assertions deliberately compare the resolver's
/// observable state and history, rather than planner internals, so they remain useful while the
/// orchestrator is decomposed.
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class BattleRefactorCharacterizationTests
{
    [Fact]
    public void ResolverSerialAndParallelPlanning_ProduceTheSameBattleTurn()
    {
        BattleHistory serial = RunCharacterizationBattle(maxPlanningDegreeOfParallelism: 1);
        BattleHistory parallel = RunCharacterizationBattle(maxPlanningDegreeOfParallelism: 4);

        Assert.Equal(Characterize(serial), Characterize(parallel));
        Assert.True(serial.Turns[1].Actions.Select(action => action.ActorId).Distinct().Count() >= 4);
    }

    private static BattleHistory RunCharacterizationBattle(int maxPlanningDegreeOfParallelism)
    {
        BattleSquad firstLeft = CreateSquad("First Left", 90_000);
        BattleSquad firstRight = CreateSquad("First Right", 91_000);
        BattleSquad secondLeft = CreateSquad("Second Left", 92_000);
        BattleSquad secondRight = CreateSquad("Second Right", 93_000);
        List<BattleSquad> first = [firstLeft, firstRight];
        List<BattleSquad> second = [secondLeft, secondRight];
        BattleGridManager grid = new();

        PlaceLine(grid, firstLeft, side: true, x: 0, y: 0);
        PlaceLine(grid, firstRight, side: true, x: 0, y: 4);
        PlaceLine(grid, secondLeft, side: false, x: 15, y: 0);
        PlaceLine(grid, secondRight, side: false, x: 15, y: 4);

        SeededRNG random = new(90_123);
        GameRulesData rules = new();
        BattleAftermathDependencies aftermath = new(
            new Date(1, 1, 1), random, NoOpPlayerBattleAftermathSink.Instance);
        BattleExecutionContext execution = new(
            rules,
            random,
            aftermath,
            maxPlanningDegreeOfParallelism: maxPlanningDegreeOfParallelism);
        BattleTurnResolver resolver = new(grid, first, second, region: null, execution);

        resolver.ProcessNextTurn();
        return resolver.BattleHistory;
    }

    private static string Characterize(BattleHistory history)
    {
        BattleOutcome outcome = history.Outcome;
        Dictionary<int, string> squadNames = history.Turns
            .SelectMany(turn => turn.State.AttackerSquads.Values.Concat(turn.State.OpposingSquads.Values))
            .GroupBy(squad => squad.Id)
            .ToDictionary(group => group.Key, group => group.First().Name);
        string turns = string.Join("/", history.Turns.Select(turn =>
            $"{turn.TurnNumber}:{string.Join(",", turn.Actions.Select(DescribeAction))}:"
            + $"{string.Join(",", turn.Events.Select(battleEvent => DescribeEvent(battleEvent, squadNames)))}:"
            + $"{string.Join(",", turn.State.AttackerSquads.Values.Concat(turn.State.OpposingSquads.Values)
                .OrderBy(squad => squad.Id)
                .Select(DescribeSquad))}"));
        string casualties = $"killed={string.Join(",", history.KilledSoldierIds.OrderBy(id => id))};"
            + $"incapacitated={string.Join(",", history.IncapacitatedSoldierIds.OrderBy(id => id))};"
            + $"damaged={string.Join(",", history.DamagedSoldierIds.OrderBy(id => id))}";
        string result = outcome == null
            ? "none"
            : $"{outcome.EndReason}:{outcome.SideHoldingField}:"
                + $"{string.Join(",", outcome.DisengagedSquadIds.Select(id => squadNames[id]))}:"
                + $"{string.Join(",", outcome.EliminatedSquadIds.Select(id => squadNames[id]))}:"
                + $"{string.Join(",", outcome.RoutingSquadIds.Select(id => squadNames[id]))}:"
                + $"{string.Join(",", outcome.RearGuardSquadIds.Select(id => squadNames[id]))}";
        return $"turnCount={history.Turns.Count};{casualties};outcome={result};turns={turns}";
    }

    private static string DescribeAction(IAction action)
    {
        return action switch
        {
            ShootAction shot => $"Shoot:{shot.ShooterId}:{shot.TargetId}:{shot.WeaponId}:"
                + $"{shot.NumberOfShots}:{shot.CommittedShots}:{shot.HitCount}",
            _ => $"{action.GetType().Name}:{action.ActorId}:{action.Description().Replace("\n", "\\n")}"
        };
    }

    private static string DescribeEvent(BattleEvent battleEvent, IReadOnlyDictionary<int, string> squadNames) =>
        $"{battleEvent.TurnNumber}:{battleEvent.Type}:{battleEvent.Side}:"
        + $"{(battleEvent.PrimarySquadId is int primary ? squadNames[primary] : "none")}:"
        + $"{string.Join(",", battleEvent.RelatedSquadIds.Select(id => squadNames[id]))}:"
        + $"{battleEvent.Description}:{battleEvent.RealizedMobSupport}";

    private static string DescribeSquad(BattleSquadSnapshot squad) =>
        $"{squad.Name}:{squad.Status}:{squad.WithdrawalRole}:{squad.MoraleState}:"
        + string.Join(",", squad.Soldiers.OrderBy(soldier => soldier.Id).Select(soldier =>
            $"{soldier.Id}@{soldier.X},{soldier.Y}:{soldier.LeftoverMovement}:"
            + $"{soldier.TurnsRunning}:{soldier.TurnsShooting}"));

    private static BattleSquad CreateSquad(string name, int firstSoldierId)
    {
        Faction faction = CreateFaction(firstSoldierId + 10_000, name);
        SquadTemplate template = new(
            firstSoldierId,
            $"{name} Template",
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            [new SquadTemplateElement(TestModelFactory.MarineTemplate, 0, 2)],
            SquadTypes.None)
        {
            Faction = faction
        };
        Squad squad = new(name, null, template);
        for (int index = 0; index < 2; index++)
        {
            Soldier soldier = TestModelFactory.CreateSoldier(
                TestModelFactory.MarineTemplate, $"{name} {index + 1}");
            soldier.Id = firstSoldierId + index;
            squad.AddSquadMember(soldier);
        }
        return new BattleSquad(false, squad);
    }

    private static void PlaceLine(
        BattleGridManager grid,
        BattleSquad squad,
        bool side,
        int x,
        int y)
    {
        for (int index = 0; index < squad.Soldiers.Count; index++)
        {
            BattleSoldier soldier = squad.Soldiers[index];
            soldier.TopLeft = (x + index, y);
            grid.PlaceSoldier(soldier, side, [soldier.TopLeft.Value]);
        }
    }

    private static Faction CreateFaction(int id, string name)
    {
        return new Faction(
            id,
            name,
            Color.Red,
            isPlayerFaction: false,
            isDefaultFaction: false,
            behavior: FactionBehavior.None,
            GrowthType.None,
            new Dictionary<int, Species> { [TestModelFactory.HumanSpecies.Id] = TestModelFactory.HumanSpecies },
            new Dictionary<int, SoldierTemplate>
            {
                [TestModelFactory.MarineTemplate.Id] = TestModelFactory.MarineTemplate
            },
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
